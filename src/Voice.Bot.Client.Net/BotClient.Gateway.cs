using Microsoft.Extensions.Logging;
using Voice.Bot.Client.Net.Events;
using Voice.Bot.Client.Net.Models;

namespace Voice.Bot.Client.Net;

/// <summary>Gateway lifecycle (<see cref="StartGatewayAsync"/>/<see cref="StopGatewayAsync"/>) and event dispatch.</summary>
public sealed partial class BotClient
{
    private readonly TaskCompletionSource _firstReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static readonly object ConnectedMarker = new();

    /// <summary>
    /// Connects to the gateway. While the server is unreachable it keeps trying with growing pauses
    /// (up to 30 seconds) until it connects or <paramref name="ct"/> is cancelled. Once connected, it
    /// reconnects by itself whenever the connection drops. Every successful connection raises <see cref="Ready"/>.
    /// </summary>
    /// <exception cref="BotApiException">
    /// With <see cref="Grpc.Core.StatusCode.Unauthenticated"/> when the server rejects the token (HTTP 401), or
    /// <see cref="Grpc.Core.StatusCode.PermissionDenied"/> when it refuses the bot (HTTP 403) - neither is retried.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled before the connection was made.</exception>
    public Task StartGatewayAsync(CancellationToken ct = default) => _gateway.StartAsync(ct);

    /// <summary>
    /// Disconnects from the gateway. Tasks scheduled with <see cref="Every"/> and <see cref="At"/> keep their
    /// schedule, so a later <see cref="StartGatewayAsync"/> needs no new <c>Every</c>/<c>At</c> calls; they
    /// stop on disposal.
    /// </summary>
    public async Task StopGatewayAsync()
    {
        await _gateway.StopAsync().ConfigureAwait(false);
        _logger.LogInformation("Disconnected from the gateway");
    }

    /// <summary>
    /// The event the dispatch loop is handling in this async flow (set per client, so two clients in one
    /// process don't see each other's), letting a handler that starts waiting for another event hand the
    /// loop back. Background runs get <see cref="DispatchSlot.Detached"/>.
    /// </summary>
    private readonly AsyncLocal<DispatchSlot?> _slot = new();

    /// <summary>Handlers that let the loop go on while they wait for an answer; <see cref="WhenIdleAsync"/> waits for them too.</summary>
    private readonly HashSet<Task> _released = [];

    /// <summary>Background work - <see cref="RunMode.Async"/> commands; disposal waits for it a little.</summary>
    private readonly HashSet<Task> _background = [];

    private int _connections;

    /// <summary>
    /// Parses on arrival, so the cache is cleared and <see cref="WaitForAsync{T}"/> sees the event even while
    /// a handler is still busy with an earlier one, then queues it for the handlers.
    /// </summary>
    private void OnGatewayMessageReceived(string topic, string json)
    {
        var parsed = GatewayEventParser.Parse(topic, json);
        if (parsed.Kind != GatewayEventKind.ParseError)
        {
            Cache.Invalidate(parsed.Kind, parsed.Event);
            OfferToWaiters(parsed);
        }

        _events.Writer.TryWrite(parsed);
    }

    /// <summary>
    /// Every connection after the first one clears <see cref="Cache"/> at once: the events missed while
    /// offline are lost, so nothing remembered from before can be trusted.
    /// </summary>
    private void OnGatewayConnected()
    {
        if (Interlocked.Increment(ref _connections) > 1) Cache.Clear();
        _events.Writer.TryWrite(new ParsedEvent(GatewayEventKind.Connected, ConnectedMarker));
    }

    /// <summary>
    /// Completes once every event received so far has been handled, including handlers that went on waiting
    /// for an answer (handlers in <see cref="RunMode.Async"/> may still run).
    /// </summary>
    internal async Task WhenIdleAsync()
    {
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_events.Writer.TryWrite(new ParsedEvent(GatewayEventKind.Barrier, barrier))) barrier.TrySetResult();
        await barrier.Task.ConfigureAwait(false);
        while (Snapshot(_released) is { Length: > 0 } waiting)
        {
            await Task.WhenAll(waiting).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Runs <paramref name="work"/> on the thread pool, outside the dispatch loop's flow (waiting there
    /// doesn't release the loop), and lets disposal wait for it.
    /// </summary>
    internal void RunInBackground(Func<Task> work) =>
        Track(_background, Task.Run(async () =>
        {
            _slot.Value = DispatchSlot.Detached;
            await work().ConfigureAwait(false);
        }));

    /// <summary>
    /// Called when a handler starts waiting for another event: from now on the dispatch loop goes on with the
    /// next events instead of waiting for this handler, so one user's question doesn't hold up everyone else.
    /// </summary>
    private void ReleaseDispatch() => _slot.Value?.Release();

    /// <summary>Feeds a raw gateway message as if the server had sent it - for the testing kit.</summary>
    internal void Inject(string topic, string json) => OnGatewayMessageReceived(topic, json);

    /// <summary>Simulates a successful (re)connection - for the testing kit.</summary>
    internal void InjectConnected() => OnGatewayConnected();

    private async Task DispatchLoopAsync()
    {
        await foreach (var parsed in _events.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            if (Volatile.Read(ref _disposed) == 1 && parsed.Kind != GatewayEventKind.Barrier) continue;
            var slot = new DispatchSlot();
            var handling = DispatchInSlotAsync(parsed, slot);
            if (await Task.WhenAny(handling, slot.Released).ConfigureAwait(false) != handling) Track(_released, handling);
        }
    }

    private async Task DispatchInSlotAsync(ParsedEvent parsed, DispatchSlot slot)
    {
        _slot.Value = slot;
        try
        {
            await DispatchAsync(parsed).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await RaiseErrorAsync(ex).ConfigureAwait(false);
        }
    }

    private static void Track(HashSet<Task> tasks, Task task)
    {
        lock (tasks) tasks.Add(task);
        task.ContinueWith(
            done =>
            {
                _ = done.Exception;
                lock (tasks) tasks.Remove(done);
            },
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private static Task[] Snapshot(HashSet<Task> tasks)
    {
        lock (tasks) return [.. tasks];
    }

    private Task DispatchAsync(ParsedEvent parsed) => parsed.Kind switch
    {
        GatewayEventKind.MessageCreated => RaiseAsync(MessageCreated, (MessageCreatedEvent)parsed.Event),
        GatewayEventKind.MessageUpdated => RaiseAsync(MessageUpdated, (MessageUpdatedEvent)parsed.Event),
        GatewayEventKind.MessageDeleted => RaiseAsync(MessageDeleted, (MessageDeletedEvent)parsed.Event),
        GatewayEventKind.ReactionAdded => RaiseAsync(ReactionAdded, (ReactionEvent)parsed.Event),
        GatewayEventKind.ReactionRemoved => RaiseAsync(ReactionRemoved, (ReactionEvent)parsed.Event),
        GatewayEventKind.UserTyping => RaiseAsync(UserTyping, (TypingEvent)parsed.Event),
        GatewayEventKind.BotTyping => RaiseAsync(BotTyping, (BotTypingEvent)parsed.Event),
        GatewayEventKind.GroupStructureChanged => RaiseAsync(GroupStructureChanged, (GroupStructureChangedEvent)parsed.Event),
        GatewayEventKind.MemberJoined => RaiseAsync(MemberJoined, (MemberEvent)parsed.Event),
        GatewayEventKind.MemberLeft => RaiseAsync(MemberLeft, (MemberEvent)parsed.Event),
        GatewayEventKind.MemberKicked => RaiseAsync(MemberKicked, (MemberEvent)parsed.Event),
        GatewayEventKind.UserProfileUpdated => RaiseAsync(UserProfileUpdated, (UserProfileUpdatedEvent)parsed.Event),
        GatewayEventKind.UserStatusChanged => RaiseAsync(UserStatusChanged, (UserStatusChangedEvent)parsed.Event),
        GatewayEventKind.VoiceConnected => RaiseAsync(VoiceConnected, (VoiceEvent)parsed.Event),
        GatewayEventKind.VoiceDisconnected => RaiseAsync(VoiceDisconnected, (VoiceEvent)parsed.Event),
        GatewayEventKind.InteractionCreated => RaiseAsync(InteractionCreated, (BotInteractionEvent)parsed.Event),
        GatewayEventKind.Unknown => RaiseAsync(UnhandledEvent, (UnknownGatewayEvent)parsed.Event),
        GatewayEventKind.ParseError => RaiseErrorAsync((Exception)parsed.Event),
        GatewayEventKind.Connected => RaiseReadyAsync(),
        GatewayEventKind.Barrier => Complete((TaskCompletionSource)parsed.Event),
        _ => Task.CompletedTask,
    };

    private static Task Complete(TaskCompletionSource barrier)
    {
        barrier.TrySetResult();
        return Task.CompletedTask;
    }

    /// <summary>Refreshes the remembered profile (it may have changed while disconnected) and raises <see cref="Ready"/>.</summary>
    private async Task RaiseReadyAsync()
    {
        User me;
        try
        {
            me = await GetMeAsync().ConfigureAwait(false);
            _me = me;
        }
        catch (Exception ex) when (ex is BotApiException or OperationCanceledException)
        {
            _logger.LogWarning(ex, "Connected to the gateway, but could not fetch the bot's profile");
            await RaiseErrorAsync(ex).ConfigureAwait(false);
            if (_me is null) return;
            me = _me;
        }

        _logger.LogInformation("Connected to the gateway as {Username} ({BotId})", me.Username, me.Id);
        _firstReady.TrySetResult();
        await RaiseAsync(Ready, me).ConfigureAwait(false);
    }

    private void OnGatewayError(Exception ex)
    {
        _logger.LogWarning(ex, "Gateway connection problem: {Message}", ex.Message);
        _ = RaiseErrorAsync(ex);
    }

    /// <summary>Calls every subscriber in order; one that throws is reported to <see cref="ConnectionError"/> and doesn't stop the others.</summary>
    private async Task RaiseAsync<T>(Func<T, Task>? handler, T arg)
    {
        if (handler is null) return;
        foreach (var d in handler.GetInvocationList())
        {
            try
            {
                await ((Func<T, Task>)d)(arg).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An event handler for {Event} failed", typeof(T).Name);
                await RaiseErrorAsync(ex).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Raises <see cref="ConnectionError"/> - for the testing kit.</summary>
    internal Task ReportAsync(Exception error) => RaiseErrorAsync(error);

    private async Task RaiseErrorAsync(Exception error)
    {
        var handler = ConnectionError;
        if (handler is null) return;
        foreach (var d in handler.GetInvocationList())
        {
            try
            {
                await ((Func<Exception, Task>)d)(error).ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }

    /// <summary>One event in the dispatch loop: released when its handler starts waiting for another event.</summary>
    private sealed class DispatchSlot
    {
        /// <summary>For flows outside the loop: already released, so releasing it again does nothing.</summary>
        public static readonly DispatchSlot Detached = CreateDetached();

        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Released => _released.Task;

        public void Release() => _released.TrySetResult();

        private static DispatchSlot CreateDetached()
        {
            var slot = new DispatchSlot();
            slot.Release();
            return slot;
        }
    }
}
