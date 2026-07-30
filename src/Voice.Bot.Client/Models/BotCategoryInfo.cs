using Wire = Vc.Backend.WebApi.Bots.Apis.Grpc.BotCategoryInfo;

namespace Voice.Bot.Client.Models;

/// <summary>Mirrors <c>bots.BotCategoryInfo</c> - one category within <see cref="BotGroupDetails"/>.</summary>
public sealed record BotCategoryInfo(
    Guid Id,
    string Name,
    string Position,
    IReadOnlyList<BotChannelInfo> Channels);

internal static class BotCategoryInfoExtensions
{
    public static BotCategoryInfo ToModel(this Wire grpc) => new(
        Guid.Parse(grpc.Id),
        grpc.Name,
        grpc.Position,
        grpc.Channels.Select(c => c.ToModel()).ToList());
}
