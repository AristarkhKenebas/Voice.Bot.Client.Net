using Wire = Vc.Backend.WebApi.Bots.Apis.Grpc.BotGroupInfo;

namespace Voice.Bot.Client.Net.Models;

/// <summary>Mirrors <c>bots.BotGroupInfo</c> - lightweight group projection returned by <c>GetMyGroupsAsync</c>.</summary>
public sealed record BotGroupInfo(
    Guid Id,
    Guid OwnerId,
    string Name,
    DateTimeOffset CreatedAt,
    Guid? AvatarId,
    Guid? BannerId);

internal static class BotGroupInfoExtensions
{
    public static BotGroupInfo ToModel(this Wire grpc) => new(
        Guid.Parse(grpc.Id),
        Guid.Parse(grpc.OwnerId),
        grpc.Name,
        grpc.CreatedAt.ToDateTimeOffset(),
        string.IsNullOrEmpty(grpc.AvatarId) ? null : Guid.Parse(grpc.AvatarId),
        string.IsNullOrEmpty(grpc.BannerId) ? null : Guid.Parse(grpc.BannerId));
}
