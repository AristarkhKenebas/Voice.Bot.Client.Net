using System.Reflection;
using Voice.Bot.Client.Net.Events;
using Voice.Bot.Client.Net.Internal;

namespace Voice.Bot.Client.Net.Interactions;

/// <summary>Matching an incoming interaction to a registered handler and invoking it.</summary>
public sealed partial class InteractionService
{
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
            args[i] = ArgumentConverter.Convert(raw, parameters[i].ParameterType);
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
}
