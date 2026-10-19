using Voice.Bot.Client.Net.Internal;
using WireCommonUser = Vc.Backend.Contracts.Grpc.User;
using WireUserResponse = Vc.Backend.WebApi.Users.Grpc.UserResponse;

namespace Voice.Bot.Client.Net.Models;

/// <summary>Presence status of a user.</summary>
public enum UserStatus
{
    Online = 0,
    Offline = 1,
    DoNotDisturb = 2,
    Invisible = 3,

    /// <summary>The server didn't say - e.g. a message author in a gateway event without a presence status.</summary>
    Unknown = -1,
}

/// <summary>A user or a bot - bots are ordinary users with <see cref="IsBot"/> set.</summary>
/// <param name="StatusText">The free-text status line the user wrote, <c>null</c> when empty.</param>
public sealed record User(
    Guid Id,
    string Username,
    string? DisplayName,
    string? Description,
    string? StatusText,
    UserStatus Status,
    string? AvatarUrl,
    string? BannerUrl,
    bool IsBot,
    DateTimeOffset? CreatedAt);

internal static class UserExtensions
{
    public static User ToModel(this WireCommonUser wire) => new(
        Wire.Id(wire.Id),
        wire.Username,
        Wire.Text(wire.DisplayName),
        Wire.Text(wire.Description),
        Wire.Text(wire.Status),
        (UserStatus)(int)wire.UserStatus,
        Wire.Text(wire.AvatarUrl),
        Wire.Text(wire.BannerUrl),
        wire.IsBot,
        Wire.Time(wire.CreatedAt));

    public static User ToModel(this WireUserResponse wire) => new(
        Wire.Id(wire.Id),
        wire.Username,
        Wire.Text(wire.DisplayName),
        Wire.Text(wire.Description),
        Wire.Text(wire.Status),
        (UserStatus)(int)wire.UserStatus,
        Wire.Text(wire.AvatarUrl),
        Wire.Text(wire.BannerUrl),
        wire.IsBot,
        Wire.Time(wire.CreatedAt));

    /// <summary>A missing nested user (the server left the field unset) maps to an empty placeholder instead of throwing.</summary>
    public static User ToModelOrEmpty(this WireCommonUser? wire) =>
        wire is null || string.IsNullOrEmpty(wire.Id)
            ? new User(Guid.Empty, "", null, null, null, UserStatus.Offline, null, null, false, null)
            : wire.ToModel();
}
