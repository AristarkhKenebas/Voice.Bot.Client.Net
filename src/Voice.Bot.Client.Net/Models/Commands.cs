using Voice.Bot.Client.Net.Internal;
using WireCommand = Vc.Backend.WebApi.Interactions.Grpc.BotCommand;
using WireOption = Vc.Backend.WebApi.Interactions.Grpc.CommandOption;
using WireChoice = Vc.Backend.WebApi.Interactions.Grpc.CommandOptionChoice;
using WireOptionType = Vc.Backend.WebApi.Interactions.Grpc.CommandOptionType;

namespace Voice.Bot.Client.Net.Models;

/// <summary>Type of a slash-command option value.</summary>
public enum CommandOptionType
{
    String = 0,
    Integer = 1,
    Boolean = 2,
    User = 3,
    Channel = 4,
    Role = 5,
    Number = 6,
    SubCommand = 7,
    SubCommandGroup = 8,
}

/// <summary>A ready-made value users can pick for an option.</summary>
public sealed record CommandChoice(string Name, string Value);

/// <summary>An option of a slash command.</summary>
public sealed record CommandOptionDefinition(
    string Name,
    string Description,
    CommandOptionType Type,
    bool Required = false,
    IReadOnlyList<CommandChoice>? Choices = null)
{
    public bool Equals(CommandOptionDefinition? other) =>
        other is not null && Name == other.Name && Description == other.Description && Type == other.Type &&
        Required == other.Required &&
        (Choices ?? []).SequenceEqual(other.Choices ?? []);

    public override int GetHashCode() => HashCode.Combine(Name, Description, Type, Required);
}

/// <summary>What a slash command should look like - input for <c>RegisterCommandAsync</c> and <c>SyncCommandsAsync</c>.</summary>
/// <param name="DefaultMemberPermissions">Permissions a member needs to see the command; <see cref="Permissions.None"/> for everyone.</param>
public sealed record CommandDefinition(
    string Name,
    string Description,
    IReadOnlyList<CommandOptionDefinition>? Options = null,
    Permissions DefaultMemberPermissions = Permissions.None)
{
    public bool Equals(CommandDefinition? other) =>
        other is not null && Name == other.Name && Description == other.Description &&
        DefaultMemberPermissions == other.DefaultMemberPermissions && (Options ?? []).SequenceEqual(other.Options ?? []);

    public override int GetHashCode() => HashCode.Combine(Name, Description, DefaultMemberPermissions);
}

/// <summary>A registered slash command.</summary>
public sealed record BotCommandDefinition(
    Guid Id,
    Guid BotId,
    string Name,
    string Description,
    IReadOnlyList<CommandOptionDefinition> Options,
    Permissions DefaultMemberPermissions,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt)
{
    /// <summary>The command as a <see cref="CommandDefinition"/> - handy for changing one field and calling <c>UpdateCommandAsync</c>.</summary>
    public CommandDefinition ToDefinition() => new(Name, Description, Options, DefaultMemberPermissions);
}

/// <summary>What <c>SyncCommandsAsync</c> did.</summary>
public sealed record CommandSyncResult(
    IReadOnlyList<BotCommandDefinition> Created, IReadOnlyList<BotCommandDefinition> Updated, IReadOnlyList<BotCommandDefinition> Deleted);

internal static class CommandExtensions
{
    public static BotCommandDefinition ToModel(this WireCommand wire) => new(
        Wire.Id(wire.Id),
        Wire.Id(wire.BotId),
        wire.Name,
        wire.Description,
        wire.Options.Select(o => o.ToModel()).ToList(),
        (Permissions)wire.DefaultMemberPermissions,
        Wire.Time(wire.CreatedAt),
        Wire.Time(wire.UpdatedAt));

    public static CommandOptionDefinition ToModel(this WireOption wire) => new(
        wire.Name,
        wire.Description,
        (CommandOptionType)(int)wire.Type,
        wire.Required,
        wire.Choices.Select(c => new CommandChoice(c.Name, c.Value)).ToList());

    public static WireOption ToWire(this CommandOptionDefinition option)
    {
        if (!Enum.IsDefined(option.Type))
        {
            throw new ArgumentException($"Option '{option.Name}' has an unknown type {option.Type}.", nameof(option));
        }

        var wire = new WireOption
        {
            Name = option.Name,
            Description = option.Description,
            Type = (WireOptionType)(int)option.Type,
            Required = option.Required,
        };
        wire.Choices.AddRange((option.Choices ?? []).Select(c => new WireChoice { Name = c.Name, Value = c.Value }));
        return wire;
    }
}
