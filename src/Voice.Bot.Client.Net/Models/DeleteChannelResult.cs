using Wire = Vc.Backend.WebApi.Bots.Apis.Grpc.DeleteChannelResponse;

namespace Voice.Bot.Client.Net.Models;

/// <summary>Mirrors <c>bots.DeleteChannelResponse</c>.</summary>
public sealed record DeleteChannelResult(Guid Id, bool Success);

internal static class DeleteChannelResultExtensions
{
    public static DeleteChannelResult ToModel(this Wire grpc) => new(Guid.Parse(grpc.Id), grpc.Success);
}
