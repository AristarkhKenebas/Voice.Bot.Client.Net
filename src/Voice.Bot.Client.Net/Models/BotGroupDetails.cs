using Wire = Vc.Backend.WebApi.Bots.Apis.Grpc.BotGroupDetails;

namespace Voice.Bot.Client.Net.Models;

/// <summary>Mirrors <c>bots.BotGroupDetails</c> - full group detail (categories/channels) returned by <c>GetGroupAsync</c>.</summary>
public sealed record BotGroupDetails(
    Guid Id,
    Guid OwnerId,
    string Name,
    DateTimeOffset CreatedAt,
    Guid? AvatarId,
    Guid? BannerId,
    IReadOnlyList<BotCategoryInfo> Categories);

internal static class BotGroupDetailsExtensions
{
    public static BotGroupDetails ToModel(this Wire grpc) => new(
        Guid.Parse(grpc.Id),
        Guid.Parse(grpc.OwnerId),
        grpc.Name,
        grpc.CreatedAt.ToDateTimeOffset(),
        string.IsNullOrEmpty(grpc.AvatarId) ? null : Guid.Parse(grpc.AvatarId),
        string.IsNullOrEmpty(grpc.BannerId) ? null : Guid.Parse(grpc.BannerId),
        grpc.Categories.Select(c => c.ToModel()).ToList());
}
