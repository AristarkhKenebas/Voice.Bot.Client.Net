using Voice.Bot.Client.Net.Models;

namespace Voice.Bot.Client.Net.Events;

/// <summary>
/// Mirrors <c>MessageAuthorEvent</c> - the author snapshot embedded in a <see cref="GatewayMessage"/>.
/// Richer than <see cref="Models.User"/> (this is what the backend actually publishes for
/// <c>chat-events</c>), so kept as its own type rather than reusing <see cref="Models.User"/>.
/// </summary>
public sealed record MessageAuthor(
    Guid Id,
    string Username,
    string Email,
    string? DisplayName,
    string? Description,
    string? Status,
    string? PreferredLanguage,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset LastActivity,
    UserStatus PresenceStatus,
    bool IsConfirmed,
    bool IsBot,
    string SubscriptionType,
    Guid? AvatarId,
    Guid? BannerId);

/// <summary>Mirrors <c>MessageAttachmentEvent</c>.</summary>
public sealed record MessageAttachment(
    Guid Id,
    Guid UploaderId,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Status,
    string DownloadUrl,
    DateTimeOffset ExpiresAt);

/// <summary>
/// Mirrors <c>MessageCreatedEvent</c> - the <c>chat-events</c> payload for <c>"MessageCreated"</c>/
/// <c>"MessageUpdated"</c>. Richer than <see cref="Models.MessageInfo"/> (full author + attachments,
/// not just an author id), which is why the gateway carries its own message type instead of reusing
/// the REST-call result type.
/// </summary>
public sealed record GatewayMessage(
    Guid Id,
    Guid ChannelId,
    MessageAuthor Author,
    string Content,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool IsEdited,
    bool IsDeleted,
    Guid? ReplyTo,
    IReadOnlyList<MessageAttachment> Attachments,
    IReadOnlyDictionary<string, IReadOnlyList<Guid>> Reactions);
