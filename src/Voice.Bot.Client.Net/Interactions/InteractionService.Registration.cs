using System.Reflection;
using Voice.Bot.Client.Net.Models;

namespace Voice.Bot.Client.Net.Interactions;

/// <summary>Syncing <c>[SlashCommand]</c>-declared commands to the backend.</summary>
public sealed partial class InteractionService
{
    /// <summary>
    /// Builds one command definition per registered <c>[SlashCommand]</c> method (name, description and
    /// default permissions from the attribute, options from <c>[SlashCommandOption]</c> parameters - see
    /// <see cref="GetEffectiveOptionName"/> for how option names stay consistent with dispatch-time
    /// binding) and reconciles them with what's registered via <see cref="BotClient.SyncCommandsAsync"/>:
    /// registers new commands, updates changed ones and - when <paramref name="deleteMissing"/> - deletes
    /// commands no module declares anymore.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// No module is registered and <paramref name="deleteMissing"/> is set - syncing would delete every command.
    /// </exception>
    public Task<CommandSyncResult> RegisterCommandsAsync(bool deleteMissing = true, CancellationToken ct = default)
    {
        if (_handlers.Count == 0 && deleteMissing)
        {
            throw new InvalidOperationException(
                "No [SlashCommand] methods are registered, so syncing would delete every command. Add modules first, or pass deleteMissing: false.");
        }

        return _client.SyncCommandsAsync(_handlers.Select(BuildDefinition).ToList(), deleteMissing, ct);
    }

    private static CommandDefinition BuildDefinition(RegisteredHandler handler)
    {
        var attribute = handler.Method.GetCustomAttribute<SlashCommandAttribute>()!;
        var options = handler.Method.GetParameters()
            .Select(p => (Parameter: p, Option: p.GetCustomAttribute<SlashCommandOptionAttribute>()))
            .Where(x => x.Option is not null)
            .Select(x => new CommandOptionDefinition(
                GetEffectiveOptionName(x.Parameter), x.Option!.Description, x.Option.ExplicitType ?? InferOptionType(x.Parameter.ParameterType), x.Option.Required))
            .ToList();

        return new CommandDefinition(attribute.Name, attribute.Description, options, attribute.DefaultMemberPermissions);
    }

    private static CommandOptionType InferOptionType(Type clrType)
    {
        var type = Nullable.GetUnderlyingType(clrType) ?? clrType;
        if (type == typeof(int) || type == typeof(long)) return CommandOptionType.Integer;
        if (type == typeof(bool)) return CommandOptionType.Boolean;
        if (type == typeof(double) || type == typeof(decimal) || type == typeof(float)) return CommandOptionType.Number;
        return CommandOptionType.String;
    }
}
