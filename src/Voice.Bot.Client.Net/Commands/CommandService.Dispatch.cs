using System.Text;
using Microsoft.Extensions.Logging;
using Voice.Bot.Client.Net.Events;
using Voice.Bot.Client.Net.Internal;
using Voice.Bot.Client.Net.Models;

namespace Voice.Bot.Client.Net.Commands;

/// <summary>Matching an incoming message to a registered command, binding its arguments, and invoking it.</summary>
public sealed partial class CommandService
{
    private const string HelpName = "help";

    private async Task DispatchAsync(MessageCreatedEvent created)
    {
        var message = created.Message;
        // Never react to the bot itself, and by default not to other bots either - the obvious way a naive
        // prefix-command bot starts replying to its own replies in a loop.
        if (message.Author.IsBot && (_client.Options.IgnoreBots || await IsMeAsync(message.Author.Id).ConfigureAwait(false))) return;

        // The answer to a question (BotContext.AskAsync) belongs to the command that asked it.
        if (_client.IsAnswer(created)) return;

        if (await MatchPrefixAsync(message.Content).ConfigureAwait(false) is not { } match) return;
        var (prefix, text) = match;

        var tokens = CommandArgumentParser.Split(text);
        if (tokens.Count == 0) return;

        var comparison = _client.Options.CaseInsensitiveCommands ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var name = tokens[0].Value;
        var command = _commands.FirstOrDefault(c => c.Matches(name, comparison));
        var arguments = tokens.Skip(1).ToList();
        if (command is null)
        {
            if (_client.Options.HelpCommand && string.Equals(name, HelpName, comparison))
            {
                await ReplyHelpAsync(new CommandContext(_client, message, created.GroupId, HelpName, prefix, [.. arguments.Select(a => a.Value)])).ConfigureAwait(false);
                return;
            }

            // No module claims this command - not an error, raw MessageCreated subscribers may still handle it.
            _logger.LogDebug("Unknown text command {Command} from {UserId}", name, message.Author.Id);
            return;
        }

        var context = new CommandContext(_client, message, created.GroupId, command.Name, prefix, [.. arguments.Select(a => a.Value)]);
        await _runner.RunAsync(context, command.Metadata, _services, () => ExecuteAsync(command, context, text, arguments),
            ex => RaiseExecutionFailedAsync(ex, message)).ConfigureAwait(false);
    }

    private async Task ExecuteAsync(RegisteredCommand command, CommandContext context, string text, IReadOnlyList<ArgumentToken> arguments)
    {
        var args = BuildArguments(command, context.Prefix ?? Prefix, text, arguments);
        command.Metadata.UseCooldown(context);
        await _modules.RunAsync<ModuleBase>(command.ModuleType, async module =>
        {
            module.Context = context;
            try
            {
                await CommandRunner.InvokeAsync(command.Method, module, args).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (!await module.OnErrorAsync(ex).ConfigureAwait(false)) throw;
            }
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Positional binding: token <c>i</c> feeds parameter <c>i</c>, converted via
    /// <see cref="ArgumentConverter"/>. Exception - a trailing <see cref="string"/> parameter is
    /// "greedy" and takes the rest of the text exactly as typed instead of just the next token (quotes,
    /// line breaks and inner spacing kept, trailing whitespace dropped), so <c>!echo hello world</c> binds a
    /// single <c>(string text)</c> parameter to <c>"hello world"</c> rather than dropping everything after
    /// the first token. Tokens left over without a greedy parameter are rejected
    /// (<see cref="BotReplies.UnexpectedArgument"/>).
    /// </summary>
    /// <exception cref="CommandRejectedException">A required argument is missing, one doesn't convert, or there are too many.</exception>
    private object?[] BuildArguments(RegisteredCommand command, string prefix, string text, IReadOnlyList<ArgumentToken> tokens)
    {
        var parameters = command.Parameters;
        var args = new object?[parameters.Count];
        for (var i = 0; i < parameters.Count; i++)
        {
            var parameter = parameters[i];
            string? raw = i >= tokens.Count ? null
                : parameter.IsRemainder ? text[tokens[i].Start..].TrimEnd()
                : tokens[i].Value;

            if (raw is null)
            {
                if (!parameter.IsOptional) throw Rejected(_client.Options.Replies.MissingArgument(parameter.Name), command, prefix);
                args[i] = ArgumentConverter.Missing(parameter.Parameter);
                continue;
            }

            try
            {
                args[i] = ArgumentConverter.Convert(raw, parameter.Parameter.ParameterType);
            }
            catch (FormatException)
            {
                throw Rejected(_client.Options.Replies.InvalidArgument(parameter.Name, ArgumentConverter.Describe(parameter.Parameter.ParameterType)), command, prefix);
            }
        }

        var greedy = parameters.Count > 0 && parameters[^1].IsRemainder;
        if (!greedy && tokens.Count > parameters.Count && _client.Options.Replies.UnexpectedArgument is { } unexpected)
        {
            throw Rejected(unexpected(tokens[parameters.Count].Value), command, prefix);
        }

        return args;
    }

    private CommandRejectedException Rejected(string? problem, RegisteredCommand command, string prefix)
    {
        var usage = _client.Options.Replies.Usage?.Invoke(command.Usage(prefix));
        return new CommandRejectedException(problem is null ? usage : usage is null ? problem : $"{problem}\n{usage}");
    }

    /// <summary>The prefix the message starts with and the text after it, or <c>null</c> when it isn't a command.</summary>
    private async Task<(string Prefix, string Text)?> MatchPrefixAsync(string content)
    {
        foreach (var prefix in Prefixes.Where(p => p.Length > 0).OrderByDescending(p => p.Length))
        {
            if (content.StartsWith(prefix, StringComparison.Ordinal)) return (prefix, content[prefix.Length..]);
        }

        if (_client.Options.MentionPrefix && content.StartsWith("<@", StringComparison.Ordinal))
        {
            var end = content.IndexOf('>', StringComparison.Ordinal);
            if (end > 0 && Mention.TryParseUser(content[..(end + 1)], out var mentioned) && await IsMeAsync(mentioned).ConfigureAwait(false))
            {
                return (content[..(end + 1)] + " ", content[(end + 1)..]);
            }
        }

        return null;
    }

    private async Task<bool> IsMeAsync(Guid userId)
    {
        try
        {
            return (await _client.GetCachedMeAsync().ConfigureAwait(false)).Id == userId;
        }
        catch (BotApiException ex)
        {
            _logger.LogWarning(ex, "Could not fetch the bot's profile");
            return false;
        }
    }

    private async Task ReplyHelpAsync(CommandContext context)
    {
        var prefix = context.Prefix ?? Prefix;
        var comparison = _client.Options.CaseInsensitiveCommands ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var text = new StringBuilder();
        if (context.Arguments.Count > 0 && _commands.FirstOrDefault(c => c.Matches(context.Arguments[0], comparison)) is { } one)
        {
            text.Append('`').Append(one.Usage(prefix)).Append('`');
            if (one.Description is not null) text.Append(" - ").Append(one.Description);
            if (one.Aliases.Count > 0) text.Append("\nAliases: ").AppendJoin(", ", one.Aliases);
        }
        else
        {
            text.AppendLine("Text commands:");
            foreach (var command in _commands)
            {
                text.Append('`').Append(command.Usage(prefix)).Append('`');
                if (command.Description is not null) text.Append(" - ").Append(command.Description);
                text.AppendLine();
            }

            text.Append('`').Append(prefix).Append(HelpName).Append(" [command]` - Shows this list or one command");
            if (Interactions?.Commands is { Count: > 0 } slash)
            {
                text.AppendLine().AppendLine().AppendLine("Slash commands:");
                foreach (var definition in slash)
                {
                    text.Append("`/").Append(definition.Name);
                    foreach (var option in definition.Options ?? []) text.Append(option.Required ? $" <{option.Name}>" : $" [{option.Name}]");
                    text.Append("` - ").AppendLine(definition.Description);
                }
            }
        }

        try
        {
            var parts = Format.Split(text.ToString().TrimEnd());
            await context.ReplyAsync(parts[0]).ConfigureAwait(false);
            foreach (var part in parts.Skip(1)) await context.SendAsync(part).ConfigureAwait(false);
        }
        catch (BotApiException ex)
        {
            _logger.LogWarning(ex, "Could not answer help");
        }
    }

    private async Task RaiseExecutionFailedAsync(Exception ex, Message message)
    {
        var handler = CommandExecutionFailed;
        if (handler is null) return;
        foreach (var d in handler.GetInvocationList())
        {
            await ((Func<Exception, Message, Task>)d)(ex, message).ConfigureAwait(false);
        }
    }
}
