namespace Voice.Bot.Client.Net.Events;

/// <summary>
/// Mirrors the backend's <c>MessageReactionEvent</c> (<c>chat-events</c> payload for
/// <c>"MessageReactionAdded"</c>/<c>"MessageReactionRemoved"</c>) - the wire payload also carries an
/// <c>Added</c> bool, dropped here since which <see cref="BotClient"/> event fired already says that.
/// </summary>
public sealed record MessageReactionEvent(
    Guid MessageId,
    Guid ChannelId,
    Guid UserId,
    string Emoji,
    IReadOnlyDictionary<string, IReadOnlyList<Guid>> Reactions);
