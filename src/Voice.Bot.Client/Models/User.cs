using Wire = Vc.Backend.Contracts.Grpc.User;

namespace Voice.Bot.Client.Models;

/// <summary>
/// Mirrors <c>common.User</c>. Intentionally omits the wire's separate <c>status</c> string field (6) -
/// <c>user_status</c> (the typed <see cref="UserStatus"/> enum, field 16) carries the same information
/// in a form worth exposing; the loose string is not.
/// </summary>
public sealed record User(
    Guid Id,
    string Username,
    string Email,
    string? DisplayName,
    string? Description,
    string? PreferredLanguage,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset LastActivity,
    bool IsConfirmed,
    bool IsBot,
    string SubscriptionType,
    Guid? AvatarId,
    Guid? BannerId,
    UserStatus Status);

internal static class UserExtensions
{
    public static User ToModel(this Wire grpc) => new(
        Guid.Parse(grpc.Id),
        grpc.Username,
        grpc.Email,
        string.IsNullOrEmpty(grpc.DisplayName) ? null : grpc.DisplayName,
        string.IsNullOrEmpty(grpc.Description) ? null : grpc.Description,
        string.IsNullOrEmpty(grpc.PreferredLanguage) ? null : grpc.PreferredLanguage,
        grpc.CreatedAt.ToDateTimeOffset(),
        grpc.UpdatedAt.ToDateTimeOffset(),
        grpc.LastActivity.ToDateTimeOffset(),
        grpc.IsConfirmed,
        grpc.IsBot,
        grpc.SubscriptionType,
        string.IsNullOrEmpty(grpc.AvatarId) ? null : Guid.Parse(grpc.AvatarId),
        string.IsNullOrEmpty(grpc.BannerId) ? null : Guid.Parse(grpc.BannerId),
        grpc.UserStatus.ToModel());
}
