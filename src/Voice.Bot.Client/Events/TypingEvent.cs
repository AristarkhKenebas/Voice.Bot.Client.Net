namespace Voice.Bot.Client.Events;

/// <summary>Mirrors the backend's <c>UserTypingEvent</c> (<c>chat-events</c> payload for <c>"UserTyping"</c>).</summary>
public sealed record TypingEvent(Guid ChannelId, Guid UserId, string Username, bool IsTyping);
