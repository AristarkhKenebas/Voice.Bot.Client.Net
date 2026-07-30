using Wire = Vc.Backend.WebApi.Bots.Apis.Grpc.BotChannelInfo;

namespace Voice.Bot.Client.Models;

/// <summary>Mirrors <c>bots.BotChannelInfo</c> - a channel nested under a category in <see cref="BotCategoryInfo"/>.</summary>
public sealed record BotChannelInfo(
    Guid Id,
    Guid? CategoryId,
    string Name,
    ChannelType Type,
    string Position);

internal static class BotChannelInfoExtensions
{
    public static BotChannelInfo ToModel(this Wire grpc) => new(
        Guid.Parse(grpc.Id),
        string.IsNullOrEmpty(grpc.CategoryId) ? null : Guid.Parse(grpc.CategoryId),
        grpc.Name,
        (ChannelType)grpc.Type,
        grpc.Position);
}
