using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace Voice.Bot.Client.Net.Internal;

/// <summary>A command was refused for a reason the user should hear (bad arguments, cooldown) - not an error.</summary>
internal sealed class CommandRejectedException(string? reply) : Exception(reply)
{
    public string? Reply { get; } = reply;
}

/// <summary>What the framework knows about a command beyond its handler: checks, cooldown and run mode.</summary>
internal sealed class CommandMetadata
{
    private readonly CooldownTracker? _cooldown;

    /// <summary>The command's cooldown counter, <c>null</c> without <see cref="CooldownAttribute"/> - for tests.</summary>
    internal CooldownTracker? Cooldown => _cooldown;

    public CommandMetadata(MethodInfo method, Type moduleType, RunMode? runMode)
    {
        Checks = [.. moduleType.GetCustomAttributes<CheckAttribute>(inherit: true), .. method.GetCustomAttributes<CheckAttribute>(inherit: true)];
        var cooldown = method.GetCustomAttribute<CooldownAttribute>() ?? moduleType.GetCustomAttribute<CooldownAttribute>(inherit: true);
        _cooldown = cooldown is null ? null : new CooldownTracker(cooldown);
        RunMode = runMode;
    }

    public IReadOnlyList<CheckAttribute> Checks { get; }

    public RunMode? RunMode { get; }

    /// <summary>Counts a use, or throws <see cref="CommandRejectedException"/> with the time left.</summary>
    public void UseCooldown(BotContext context)
    {
        if (_cooldown?.TryUse(context) is { } left) throw new CommandRejectedException(context.Client.Options.Replies.Cooldown(left));
    }
}

/// <summary>
/// Sliding-window use counter of one command, per <see cref="CooldownScope"/> key. Keys whose window has
/// passed are dropped, so the map holds only the users (channels, groups) seen within one period.
/// </summary>
internal sealed class CooldownTracker(CooldownAttribute cooldown)
{
    private readonly Dictionary<Guid, Queue<long>> _uses = [];
    private int _sweepAt = 64;
    private long _lastSweep = Stopwatch.GetTimestamp();

    /// <returns><c>null</c> when the use is allowed (and counted), otherwise how long is left.</returns>
    public TimeSpan? TryUse(BotContext context)
    {
        var key = cooldown.Scope switch
        {
            CooldownScope.User => context.UserId,
            CooldownScope.Channel => context.ChannelId,
            CooldownScope.Group => context.GroupId ?? context.ChannelId,
            _ => Guid.Empty,
        };
        var now = Stopwatch.GetTimestamp();
        var window = (long)(cooldown.Period.TotalSeconds * Stopwatch.Frequency);
        lock (_uses)
        {
            if (_uses.Count >= _sweepAt || now - _lastSweep >= window) Sweep(now, window);
            if (!_uses.TryGetValue(key, out var times)) _uses[key] = times = new Queue<long>();
            while (times.Count > 0 && now - times.Peek() >= window) times.Dequeue();
            if (times.Count >= cooldown.Uses) return Stopwatch.GetElapsedTime(now, times.Peek() + window);
            times.Enqueue(now);
            return null;
        }
    }

    /// <summary>How many keys are remembered - for tests.</summary>
    internal int Count
    {
        get
        {
            lock (_uses) return _uses.Count;
        }
    }

    /// <summary>Forgets the keys whose last use is out of the window; runs once per window or when the map doubles, so it costs little per use.</summary>
    private void Sweep(long now, long window)
    {
        _lastSweep = now;
        foreach (var (key, times) in _uses.ToList())
        {
            if (times.Count == 0 || now - times.Last() >= window) _uses.Remove(key);
        }

        _sweepAt = Math.Max(64, _uses.Count * 2);
    }
}

/// <summary>
/// Runs a command the same way for text and slash commands: middleware around checks, the handler body
/// (argument binding, cooldown, the module method) and error handling with the default replies.
/// </summary>
internal sealed class CommandRunner(BotClient client, ILogger logger)
{
    private readonly List<Func<BotContext, Func<Task>, Task>> _middleware = [];

    public void Use(Func<BotContext, Func<Task>, Task> middleware)
    {
        lock (_middleware) _middleware.Add(middleware);
    }

    /// <param name="body">Binds arguments, applies the cooldown and calls the handler; may throw <see cref="CommandRejectedException"/>.</param>
    /// <param name="raiseFailed">Raises the service's failure event.</param>
    public Task RunAsync(BotContext context, CommandMetadata metadata, IServiceProvider? services, Func<Task> body, Func<Exception, Task> raiseFailed)
    {
        if ((metadata.RunMode ?? client.Options.DefaultRunMode) == RunMode.Async)
        {
            client.RunInBackground(() => ExecuteAsync(context, metadata, services, body, raiseFailed, background: true));
            return Task.CompletedTask;
        }

        return ExecuteAsync(context, metadata, services, body, raiseFailed, background: false);
    }

    private async Task ExecuteAsync(
        BotContext context, CommandMetadata metadata, IServiceProvider? services, Func<Task> body, Func<Exception, Task> raiseFailed, bool background)
    {
        try
        {
            await Chain(context, async () =>
            {
                foreach (var check in metadata.Checks)
                {
                    var result = await check.CheckAsync(context, services).ConfigureAwait(false);
                    if (!result.Passed)
                    {
                        logger.LogDebug("Command {Command} refused by {Check} for {UserId}", context.CommandName, check.GetType().Name, context.UserId);
                        await TryReplyAsync(context, result.Reply).ConfigureAwait(false);
                        return;
                    }
                }

                await body().ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        catch (CommandRejectedException rejected)
        {
            await TryReplyAsync(context, rejected.Reply).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!background)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            logger.LogDebug(ex, "Command {Command} was cancelled", context.CommandName);
        }
        catch (Exception ex)
        {
            await HandleErrorAsync(context, ex, raiseFailed).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Nests the middleware around <paramref name="last"/>. Each <c>next</c> runs its part of the chain at
    /// most once: a middleware that calls it twice gets the first run's task back instead of a second run of
    /// the handler.
    /// </summary>
    private Task Chain(BotContext context, Func<Task> last)
    {
        Func<BotContext, Func<Task>, Task>[] middleware;
        lock (_middleware) middleware = [.. _middleware];

        var next = last;
        for (var i = middleware.Length - 1; i >= 0; i--)
        {
            var current = middleware[i];
            var inner = Once(next);
            next = () => current(context, inner);
        }

        return next();
    }

    private static Func<Task> Once(Func<Task> step)
    {
        var started = 0;
        var run = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return () =>
        {
            if (Interlocked.Exchange(ref started, 1) == 0) _ = ForwardAsync(step, run);
            return run.Task;
        };
    }

    private static async Task ForwardAsync(Func<Task> step, TaskCompletionSource run)
    {
        try
        {
            await step().ConfigureAwait(false);
            run.TrySetResult();
        }
        catch (OperationCanceledException ex)
        {
            run.TrySetCanceled(ex.CancellationToken);
        }
        catch (Exception ex)
        {
            run.TrySetException(ex);
        }
    }

    private async Task HandleErrorAsync(BotContext context, Exception error, Func<Exception, Task> raiseFailed)
    {
        logger.LogError(error, "Command {Command} failed for {UserId} in {ChannelId}", context.CommandName, context.UserId, context.ChannelId);
        try
        {
            await raiseFailed(error).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "A command failure handler failed");
        }

        var replies = client.Options.Replies;
        var reply = error switch
        {
            BotApiException { IsRateLimited: true } => replies.RateLimited,
            BotApiException { StatusCode: StatusCode.PermissionDenied } => replies.Forbidden,
            _ => replies.Error,
        };
        await TryReplyAsync(context, reply).ConfigureAwait(false);
    }

    private async Task TryReplyAsync(BotContext context, string? reply)
    {
        if (string.IsNullOrEmpty(reply)) return;
        try
        {
            await context.ReplyAsync(reply).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is BotApiException or OperationCanceledException or ObjectDisposedException)
        {
            logger.LogWarning(ex, "Could not answer {Command}", context.CommandName);
        }
    }

    /// <summary>Calls a handler method and awaits what it returns, rethrowing its own exception rather than a <see cref="TargetInvocationException"/>.</summary>
    public static async Task InvokeAsync(MethodInfo method, object module, object?[] args)
    {
        object? result;
        try
        {
            result = method.Invoke(module, args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }

        switch (result)
        {
            case Task task:
                await task.ConfigureAwait(false);
                break;
            case ValueTask valueTask:
                await valueTask.ConfigureAwait(false);
                break;
        }
    }
}
