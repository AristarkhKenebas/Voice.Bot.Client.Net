using Wire = Vc.Backend.WebApi.Interactions.Grpc.BotCommand;
using WireOption = Vc.Backend.WebApi.Interactions.Grpc.CommandOption;

namespace Voice.Bot.Client.Models;

/// <summary>
/// Mirrors <c>interactions.CommandOption</c>. Static <c>choices</c> aren't modeled here (a deliberate
/// scope cut) - use <see cref="Autocomplete"/> plus an <c>[AutocompleteHandler]</c> for dynamic
/// choices instead.
/// </summary>
public sealed record CommandOptionDefinition(
    string Name,
    string Description,
    CommandOptionType Type,
    bool Required = false,
    bool Autocomplete = false);

/// <summary>Mirrors <c>interactions.BotCommand</c> - a registered slash command, as returned by RegisterCommand/UpdateCommand/GetBotCommands.</summary>
public sealed record BotCommandDefinition(
    Guid Id,
    Guid BotId,
    string Name,
    string Description,
    IReadOnlyList<CommandOptionDefinition> Options,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

internal static class BotCommandDefinitionExtensions
{
    public static BotCommandDefinition ToModel(this Wire grpc) => new(
        Guid.Parse(grpc.Id),
        Guid.Parse(grpc.BotId),
        grpc.Name,
        grpc.Description,
        grpc.Options.Select(o => o.ToModel()).ToList(),
        grpc.CreatedAt.ToDateTimeOffset(),
        grpc.UpdatedAt.ToDateTimeOffset());

    public static CommandOptionDefinition ToModel(this WireOption grpc) =>
        new(grpc.Name, grpc.Description, (CommandOptionType)grpc.Type, grpc.Required, grpc.Autocomplete);

    public static WireOption ToWire(this CommandOptionDefinition option) => new()
    {
        Name = option.Name,
        Description = option.Description,
        Type = (Vc.Backend.WebApi.Interactions.Grpc.CommandOptionType)(int)option.Type,
        Required = option.Required,
        Autocomplete = option.Autocomplete,
    };
}
