using Voice.Bot.Client.Net.Models;

namespace Voice.Bot.Client.Net.Events;

/// <summary>A new message.</summary>
public sealed record MessageCreatedEvent(Guid? GroupId, Message Message);

/// <summary>A message was edited.</summary>
public sealed record MessageUpdatedEvent(Guid? GroupId, Message Message);

/// <summary>A message was deleted.</summary>
public sealed record MessageDeletedEvent(Guid? GroupId, Guid? ChannelId, Guid MessageId);

/// <summary>A reaction was added or removed. <see cref="Reactions"/> are all reactions of the message after the change.</summary>
public sealed record ReactionEvent(
    Guid? GroupId, Guid ChannelId, Guid MessageId, Guid UserId, string Emoji, IReadOnlyDictionary<string, IReadOnlyList<Guid>> Reactions);

/// <summary>A user started or stopped typing.</summary>
public sealed record TypingEvent(Guid? GroupId, Guid ChannelId, Guid UserId, string Username, bool IsTyping);

/// <summary>What another bot that shows a work indicator is doing.</summary>
public enum BotTypingState
{
    Thinking = 0,
    Explored = 1,
    Processing = 2,
    Reviewed = 3,
    Opened = 4,
    ReSearch = 5,
    Fetched = 6,
    Unknown = -1,
}

/// <summary>Another bot shows what it's doing.</summary>
public sealed record BotTypingEvent(Guid? GroupId, Guid ChannelId, Guid BotId, string Username, BotTypingState State, bool IsActive);

/// <summary>Public profile of a user inside gateway events.</summary>
public sealed record GatewayUser(
    Guid Id, string Username, string? DisplayName, string? Description, string? StatusText, Guid? AvatarId, UserStatus? Status, bool IsBot);

public sealed record ChannelSnapshot(Guid Id, Guid? CategoryId, string Name, ChannelType Type, string Position, IReadOnlyList<GatewayUser> Users);

public sealed record CategorySnapshot(Guid Id, string Name, string Position, IReadOnlyList<ChannelSnapshot> Channels);

/// <summary>The whole group at the moment of the event.</summary>
public sealed record GroupSnapshot(
    Guid Id,
    Guid OwnerId,
    string Name,
    AccessType? AccessType,
    VerificationLevel? VerificationLevel,
    DateTimeOffset? CreatedAt,
    Guid? AvatarId,
    Guid? BannerId,
    IReadOnlyList<CategorySnapshot> Categories);

/// <summary>The group's structure changed: categories, channels or roles.</summary>
/// <param name="Type">The original event name, e.g. <c>"ChannelCreated"</c> or <c>"RoleAssigned"</c>.</param>
/// <param name="Group">The full current group, <c>null</c> if the server didn't send it.</param>
public sealed record GroupStructureChangedEvent(string Type, Guid GroupId, GroupSnapshot? Group);

/// <summary>A member joined, left or was kicked from a group.</summary>
public sealed record MemberEvent(Guid GroupId, Guid UserId, GroupSnapshot? Group);

/// <summary>A user changed their profile; arrives once for every group shared with the bot. <c>null</c> fields weren't sent.</summary>
public sealed record UserProfileUpdatedEvent(
    Guid? GroupId,
    Guid UserId,
    string? DisplayName,
    string? Description,
    string? StatusText,
    Guid? AvatarId,
    Guid? BannerId,
    UserStatus? Status,
    DateTimeOffset? LastActivity);

/// <summary>A user changed their presence status or status line.</summary>
public sealed record UserStatusChangedEvent(Guid? GroupId, Guid UserId, UserStatus? Status, string? StatusText, DateTimeOffset? LastActivity);

/// <summary>Someone joined or left a voice channel.</summary>
public sealed record VoiceEvent(Guid? GroupId, Guid ChannelId, GatewayUser User);

/// <summary>What the user did.</summary>
public enum InteractionType
{
    ApplicationCommand = 0,
    MessageComponent = 1,
    ModalSubmit = 2,
    ApplicationCommandAutocomplete = 3,
    Unknown = -1,
}

/// <summary>Which fields are filled depends on <see cref="BotInteractionEvent.Type"/>.</summary>
/// <param name="Options">Slash-command option values by name, all as strings.</param>
public sealed record InteractionData(
    string? CommandName,
    IReadOnlyDictionary<string, string> Options,
    string? CustomId,
    IReadOnlyList<string> Values,
    string? FocusedOption,
    IReadOnlyDictionary<string, string> ModalFields);

/// <summary>A user invoked one of the bot's slash commands (or interacted with a message component).</summary>
public sealed record BotInteractionEvent(
    Guid InteractionId, Guid InvokingUserId, Guid ChannelId, Guid? GroupId, InteractionType Type, InteractionData Data);

/// <summary>An event this version of the SDK doesn't know. The raw data is in <see cref="PayloadJson"/>.</summary>
public sealed record UnknownGatewayEvent(string Topic, string Type, Guid? GroupId, string PayloadJson);
