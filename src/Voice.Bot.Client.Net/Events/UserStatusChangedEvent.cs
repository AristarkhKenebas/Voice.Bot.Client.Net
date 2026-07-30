using Voice.Bot.Client.Net.Models;

namespace Voice.Bot.Client.Net.Events;

/// <summary>
/// <c>group-events</c> payload wrapper for <c>"UserStatusChanged"</c> - live presence broadcast
/// (Discord-style) fired whenever a user or bot in a shared group changes status. Mirrors the
/// backend's <c>UserStatusChangedEvent</c>, with <see cref="GroupId"/> taken from the envelope (the
/// same event is published once per group the changed user/bot belongs to).
/// </summary>
public sealed record UserStatusChangedEvent(Guid GroupId, Guid UserId, UserStatus PresenceStatus, DateTimeOffset LastActivity);
