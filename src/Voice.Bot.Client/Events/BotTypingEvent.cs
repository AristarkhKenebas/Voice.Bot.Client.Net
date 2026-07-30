using Voice.Bot.Client.Models;

namespace Voice.Bot.Client.Events;

/// <summary>
/// Mirrors the backend's <c>BotTypingEvent</c> (<c>chat-events</c> payload for <c>"BotTyping"</c>).
/// IsActive=false clears the indicator - State is meaningless in that case, same convention as the
/// backend record.
/// </summary>
public sealed record BotTypingEvent(Guid ChannelId, Guid BotId, string Username, BotTypingState State, bool IsActive);
