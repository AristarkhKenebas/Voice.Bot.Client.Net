using System.Reflection;
using Voice.Bot.Client.Net.Models;

namespace Voice.Bot.Client.Net.Interactions;

/// <summary>Syncing <c>[SlashCommand]</c>-declared commands to the backend.</summary>
public sealed partial class InteractionService
{
    /// <summary>
    /// Command registration: builds one command definition per registered
    /// <c>[SlashCommand]</c>-attributed module method (name/description from the attribute, options
    /// from <c>[SlashCommandOption]</c>-decorated parameters - see <see cref="GetEffectiveOptionName"/>
    /// for how option names stay consistent with dispatch-time binding), resolves the bot's own id via
    /// <c>GetMeAsync</c>, and reconciles against what's already registered: registers commands that
    /// don't exist yet, updates ones whose name/description/options changed, and - when
    /// <paramref name="deleteMissing"/> - deletes registered commands no module declares anymore. This
    /// is the closest equivalent to Discord's actual bulk-overwrite semantics that the per-command CRUD
    /// API here allows. Requires the bot's own <see cref="Intents.BotIntents.ApplicationCommands"/> intent.
    /// </summary>
    public async Task RegisterCommandsAsync(bool deleteMissing = true, CancellationToken ct = default)
    {
        var me = await _client.GetMeAsync(ct).ConfigureAwait(false);
        var declared = _handlers.Where(h => h.Kind == HandlerKind.SlashCommand).Select(BuildDeclaredCommand).ToList();
        var existing = await _client.GetBotCommandsAsync(me.Id, ct).ConfigureAwait(false);
        var existingByName = existing.ToDictionary(c => c.Name);

        foreach (var command in declared)
        {
            if (existingByName.TryGetValue(command.Name, out var existingCommand))
            {
                if (!IsSameDefinition(existingCommand, command))
                {
                    await _client.UpdateCommandAsync(me.Id, existingCommand.Id, command.Name, command.Description, command.Options, ct: ct)
                        .ConfigureAwait(false);
                }
            }
            else
            {
                await _client.RegisterCommandAsync(me.Id, command.Name, command.Description, command.Options, ct: ct).ConfigureAwait(false);
            }
        }

        if (deleteMissing)
        {
            var declaredNames = declared.Select(d => d.Name).ToHashSet();
            foreach (var stale in existing.Where(e => !declaredNames.Contains(e.Name)))
            {
                await _client.DeleteCommandAsync(me.Id, stale.Id, ct).ConfigureAwait(false);
            }
        }
    }

    private static (string Name, string Description, IReadOnlyList<CommandOptionDefinition> Options) BuildDeclaredCommand(RegisteredHandler handler)
    {
        var attribute = handler.Method.GetCustomAttribute<SlashCommandAttribute>()!;
        var options = handler.Method.GetParameters()
            .Select(p => (Parameter: p, Option: p.GetCustomAttribute<SlashCommandOptionAttribute>()))
            .Where(x => x.Option is not null)
            .Select(x => new CommandOptionDefinition(
                GetEffectiveOptionName(x.Parameter), x.Option!.Description, InferOptionType(x.Parameter.ParameterType), x.Option.Required, x.Option.Autocomplete))
            .ToList();

        return (attribute.Name, attribute.Description, options);
    }

    private static CommandOptionType InferOptionType(Type clrType)
    {
        if (clrType == typeof(int) || clrType == typeof(long)) return CommandOptionType.Integer;
        if (clrType == typeof(bool)) return CommandOptionType.Boolean;
        if (clrType == typeof(double)) return CommandOptionType.Number;
        return CommandOptionType.String;
    }

    private static bool IsSameDefinition(BotCommandDefinition existing, (string Name, string Description, IReadOnlyList<CommandOptionDefinition> Options) declared) =>
        existing.Description == declared.Description && existing.Options.SequenceEqual(declared.Options);
}
