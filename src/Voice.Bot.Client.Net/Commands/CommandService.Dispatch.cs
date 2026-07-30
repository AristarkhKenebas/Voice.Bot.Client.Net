using System.Reflection;
using Voice.Bot.Client.Net.Events;
using Voice.Bot.Client.Net.Internal;

namespace Voice.Bot.Client.Net.Commands;

/// <summary>Matching an incoming message to a registered command, binding its arguments, and invoking it.</summary>
public sealed partial class CommandService
{
    private async Task DispatchAsync(GatewayMessage message)
    {
        // Never react to bots (including ourselves) - the obvious way a naive prefix-command bot
        // starts replying to its own replies in a loop.
        if (message.Author.IsBot) return;
        if (string.IsNullOrEmpty(Prefix) || !message.Content.StartsWith(Prefix, StringComparison.Ordinal)) return;

        var tokens = CommandArgumentParser.Tokenize(message.Content[Prefix.Length..]);
        if (tokens.Count == 0) return;

        var command = _commands.FirstOrDefault(c => c.Matches(tokens[0]));
        if (command is null) return; // no module claims this command - not an error, raw MessageCreated subscribers may still handle it

        var module = (ModuleBase)(_moduleFactory?.Invoke(command.ModuleType) ?? Activator.CreateInstance(command.ModuleType)!);
        module.Context = new CommandContext(_client, message);

        try
        {
            var args = BuildArguments(command.Method, tokens.Skip(1).ToList());
            if (command.Method.Invoke(module, args) is Task task)
            {
                await task.ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var actual = ex is TargetInvocationException { InnerException: { } inner } ? inner : ex;
            await RaiseExecutionFailedAsync(actual, message).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Positional binding: token <c>i</c> feeds parameter <c>i</c>, converted via
    /// <see cref="ArgumentConverter"/>. Exception - a trailing <see cref="string"/> parameter is
    /// "greedy" and joins every remaining token (space-separated) instead of just the next one, so
    /// <c>!echo hello world</c> binds a single <c>(string text)</c> parameter to <c>"hello world"</c>
    /// rather than dropping everything after the first token.
    /// </summary>
    private static object?[] BuildArguments(MethodInfo method, IReadOnlyList<string> tokens)
    {
        var parameters = method.GetParameters();
        if (parameters.Length == 0) return [];

        var args = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            var isTrailingString = i == parameters.Length - 1 && parameters[i].ParameterType == typeof(string);
            string? raw = isTrailingString
                ? (i < tokens.Count ? string.Join(' ', tokens.Skip(i)) : null)
                : (i < tokens.Count ? tokens[i] : null);

            args[i] = ArgumentConverter.Convert(raw, parameters[i].ParameterType);
        }

        return args;
    }

    private async Task RaiseExecutionFailedAsync(Exception ex, GatewayMessage message)
    {
        var handler = CommandExecutionFailed;
        if (handler is null) return;
        foreach (var d in handler.GetInvocationList())
        {
            await ((Func<Exception, GatewayMessage, Task>)d)(ex, message).ConfigureAwait(false);
        }
    }
}
