using Wire = Vc.Backend.WebApi.Bots.Apis.Grpc.BotChannelDetails;

namespace Voice.Bot.Client.Models;

/// <summary>Mirrors <c>bots.BotChannelDetails</c> - a single channel's details returned by <c>GetChannelAsync</c>.</summary>
public sealed record BotChannelDetails(
    Guid Id,
    Guid GroupId,
    Guid? CategoryId,
    string Name,
    ChannelType Type,
    string Position);

internal static class BotChannelDetailsExtensions
{
    public static BotChannelDetails ToModel(this Wire grpc) => new(
        Guid.Parse(grpc.Id),
        Guid.Parse(grpc.GroupId),
        string.IsNullOrEmpty(grpc.CategoryId) ? null : Guid.Parse(grpc.CategoryId),
        grpc.Name,
        (ChannelType)grpc.Type,
        grpc.Position);
}
