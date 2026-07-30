using Voice.Bot.Client.Net.Intents;
using Wire = Vc.Backend.Contracts.Grpc.Bot;

namespace Voice.Bot.Client.Net.Models;

/// <summary>Mirrors <c>common.Bot</c> - the profile returned by <c>GetMeAsync</c>.</summary>
public sealed record Bot(
    Guid Id,
    Guid OwnerId,
    string Username,
    string? DisplayName,
    string? Description,
    Guid? AvatarId,
    UserStatus Status,
    bool IsEnabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    BotIntents Intents);

internal static class BotExtensions
{
    public static Bot ToModel(this Wire grpc) => new(
        Guid.Parse(grpc.Id),
        Guid.Parse(grpc.OwnerId),
        grpc.Username,
        string.IsNullOrEmpty(grpc.DisplayName) ? null : grpc.DisplayName,
        string.IsNullOrEmpty(grpc.Description) ? null : grpc.Description,
        string.IsNullOrEmpty(grpc.AvatarId) ? null : Guid.Parse(grpc.AvatarId),
        grpc.UserStatus.ToModel(),
        grpc.IsEnabled,
        grpc.CreatedAt.ToDateTimeOffset(),
        grpc.UpdatedAt.ToDateTimeOffset(),
        (BotIntents)grpc.Intents);
}
