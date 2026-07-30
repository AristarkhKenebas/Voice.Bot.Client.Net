using System.Reflection;
using Voice.Bot.Client.Events;
using Voice.Bot.Client.Models;

namespace Voice.Bot.Client.Interactions;

internal enum HandlerKind { SlashCommand, Component, Modal, Autocomplete }

internal sealed record RegisteredHandler(HandlerKind Kind, string Match, string? OptionName, MethodInfo Method, Type ModuleType);

/// <summary>
/// Discovers <see cref="InteractionModuleBase"/> subclasses (via <see cref="AddModule{T}"/>/
/// <see cref="AddModulesFromAssembly"/>), then dispatches incoming <c>BotClient.InteractionCreated</c>
/// events to the matching attributed method by command name / <c>CustomId</c> pattern /
/// (command name, focused option). Call <see cref="Initialize"/> once, after registering every module.
/// </summary>
public sealed class InteractionService
{
    private readonly BotClient _client;
    private readonly Func<Type, object>? _moduleFactory;
    private readonly List<RegisteredHandler> _handlers = [];
    private bool _initialized;

    /// <param name="moduleFactory">
    /// Resolves a module instance for a given module <see cref="Type"/> - defaults to
    /// <see cref="Activator.CreateInstance(Type)"/> (parameterless constructor) when omitted. Pass a
    /// DI resolver (e.g. <c>IServiceProvider.GetRequiredService</c>) to let modules take constructor-injected services.
    /// </param>
    public InteractionService(BotClient client, Func<Type, object>? moduleFactory = null)
    {
        _client = client;
        _moduleFactory = moduleFactory;
    }

    public void AddModule<T>() where T : InteractionModuleBase => AddModule(typeof(T));

    public void AddModule(Type moduleType)
    {
        if (!typeof(InteractionModuleBase).IsAssignableFrom(moduleType) || moduleType.IsAbstract)
        {
            throw new ArgumentException($"{moduleType} must be a non-abstract subclass of InteractionModuleBase.", nameof(moduleType));
        }

        foreach (var method in moduleType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (method.GetCustomAttribute<SlashCommandAttribute>() is { } slash)
                _handlers.Add(new RegisteredHandler(HandlerKind.SlashCommand, slash.Name, null, method, moduleType));

            if (method.GetCustomAttribute<ComponentInteractionAttribute>() is { } component)
                _handlers.Add(new RegisteredHandler(HandlerKind.Component, component.CustomIdPattern, null, method, moduleType));

            if (method.GetCustomAttribute<ModalInteractionAttribute>() is { } modal)
                _handlers.Add(new RegisteredHandler(HandlerKind.Modal, modal.CustomIdPattern, null, method, moduleType));

            if (method.GetCustomAttribute<AutocompleteHandlerAttribute>() is { } autocomplete)
                _handlers.Add(new RegisteredHandler(HandlerKind.Autocomplete, autocomplete.CommandName, autocomplete.OptionName, method, moduleType));
        }
    }

    /// <summary>Registers every non-abstract <see cref="InteractionModuleBase"/> subclass found in <paramref name="assembly"/>.</summary>
    public void AddModulesFromAssembly(Assembly assembly)
    {
        foreach (var type in assembly.GetTypes())
        {
            if (typeof(InteractionModuleBase).IsAssignableFrom(type) && !type.IsAbstract)
            {
                AddModule(type);
            }
        }
    }

    /// <summary>Subscribes to <c>BotClient.InteractionCreated</c>. Safe to call more than once - later calls are no-ops.</summary>
    public void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        _client.InteractionCreated += DispatchAsync;
    }

    /// <summary>
    /// Discord.Net-style command registration: builds one command definition per registered
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

    private async Task DispatchAsync(BotInteractionEvent interaction)
    {
        (RegisteredHandler Handler, string? Capture)? match = interaction.Type switch
        {
            InteractionType.ApplicationCommand => FindByName(HandlerKind.SlashCommand, interaction.Data.CommandName),
            InteractionType.MessageComponent => FindByCustomId(HandlerKind.Component, interaction.Data.CustomId),
            InteractionType.ModalSubmit => FindByCustomId(HandlerKind.Modal, interaction.Data.CustomId),
            InteractionType.ApplicationCommandAutocomplete => FindAutocomplete(interaction.Data.CommandName, interaction.Data.FocusedOption),
            _ => null,
        };

        if (match is not { } found) return; // no module claims this interaction - not an error, raw InteractionCreated subscribers may still handle it

        var module = (InteractionModuleBase)(_moduleFactory?.Invoke(found.Handler.ModuleType) ?? Activator.CreateInstance(found.Handler.ModuleType)!);
        module.Context = new InteractionContext(_client, interaction);

        var args = BuildArguments(found.Handler, interaction, found.Capture);
        if (found.Handler.Method.Invoke(module, args) is Task task)
        {
            await task.ConfigureAwait(false);
        }
    }

    private (RegisteredHandler, string?)? FindByName(HandlerKind kind, string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var handler = _handlers.FirstOrDefault(h => h.Kind == kind && h.Match == name);
        return handler is null ? null : (handler, null);
    }

    private (RegisteredHandler, string?)? FindByCustomId(HandlerKind kind, string? customId)
    {
        if (string.IsNullOrEmpty(customId)) return null;

        foreach (var handler in _handlers.Where(h => h.Kind == kind))
        {
            if (TryMatchCustomId(handler.Match, customId, out var capture))
            {
                return (handler, capture);
            }
        }

        return null;
    }

    private (RegisteredHandler, string?)? FindAutocomplete(string? commandName, string? focusedOption)
    {
        if (string.IsNullOrEmpty(commandName) || string.IsNullOrEmpty(focusedOption)) return null;
        var handler = _handlers.FirstOrDefault(h => h.Kind == HandlerKind.Autocomplete && h.Match == commandName && h.OptionName == focusedOption);
        return handler is null ? null : (handler, null);
    }

    private static bool TryMatchCustomId(string pattern, string customId, out string? capture)
    {
        var starIndex = pattern.IndexOf('*');
        if (starIndex < 0)
        {
            capture = null;
            return pattern == customId;
        }

        var prefix = pattern[..starIndex];
        if (!customId.StartsWith(prefix, StringComparison.Ordinal))
        {
            capture = null;
            return false;
        }

        capture = customId[prefix.Length..];
        return true;
    }

    private static object?[] BuildArguments(RegisteredHandler handler, BotInteractionEvent interaction, string? wildcardCapture)
    {
        var parameters = handler.Method.GetParameters();
        if (parameters.Length == 0) return [];

        var args = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            var name = GetEffectiveOptionName(parameters[i]);
            string? raw = handler.Kind switch
            {
                HandlerKind.SlashCommand => GetOrDefault(interaction.Data.Options, name),
                HandlerKind.Component => wildcardCapture,
                HandlerKind.Modal => GetOrDefault(interaction.Data.ModalFields, name) ?? wildcardCapture,
                HandlerKind.Autocomplete => GetOrDefault(interaction.Data.Options, name) ?? interaction.Data.FocusedOption,
                _ => null,
            };
            args[i] = ConvertArgument(raw, parameters[i].ParameterType);
        }

        return args;
    }

    /// <summary>
    /// The option name a parameter is bound by, both when <see cref="RegisterCommandsAsync"/> declares
    /// it and when an incoming interaction's option values are matched back onto handler parameters -
    /// <see cref="SlashCommandOptionAttribute.Name"/> if set, else the CLR parameter name lowercased.
    /// </summary>
    private static string GetEffectiveOptionName(ParameterInfo parameter) =>
        parameter.GetCustomAttribute<SlashCommandOptionAttribute>()?.Name ?? parameter.Name!.ToLowerInvariant();

    private static string? GetOrDefault(IReadOnlyDictionary<string, string>? dict, string key) =>
        dict is not null && dict.TryGetValue(key, out var value) ? value : null;

    private static object? ConvertArgument(string? raw, Type targetType)
    {
        if (raw is null) return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
        if (targetType == typeof(string)) return raw;
        if (targetType == typeof(Guid)) return Guid.TryParse(raw, out var g) ? g : Guid.Empty;
        if (targetType == typeof(bool)) return bool.TryParse(raw, out var b) && b;
        if (targetType == typeof(int)) return int.TryParse(raw, out var i) ? i : 0;
        if (targetType == typeof(long)) return long.TryParse(raw, out var l) ? l : 0L;
        if (targetType == typeof(double)) return double.TryParse(raw, out var d) ? d : 0d;
        return Convert.ChangeType(raw, targetType);
    }
}
