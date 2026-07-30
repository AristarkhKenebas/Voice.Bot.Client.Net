namespace Voice.Bot.Client.Events;

/// <summary>
/// Defensive projection of a user snapshot embedded in <c>channel-events</c>
/// (<c>"UserConnected"</c>/<c>"UserDisconnected"</c>) and <c>group-events</c> payloads. The backend
/// currently serializes its raw internal domain user model there, not a clean contract - this type
/// only pulls the handful of fields that are safe/stable to expose, and deliberately does not capture
/// or forward any of the payload's other fields. Every field but <see cref="Id"/> is nullable: if the
/// backend's shape drifts, deserialization should degrade gracefully rather than fail the whole event.
/// </summary>
public sealed record UserSnapshot(
    Guid Id,
    string? Username,
    string? DisplayName,
    Guid? AvatarId,
    bool IsBot);
