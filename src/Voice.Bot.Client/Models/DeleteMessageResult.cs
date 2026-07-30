using Wire = Vc.Backend.WebApi.Bots.Apis.Grpc.DeleteMessageResponse;

namespace Voice.Bot.Client.Models;

/// <summary>Mirrors <c>bots.DeleteMessageResponse</c>.</summary>
public sealed record DeleteMessageResult(Guid Id, bool Success);

internal static class DeleteMessageResultExtensions
{
    public static DeleteMessageResult ToModel(this Wire grpc) => new(Guid.Parse(grpc.Id), grpc.Success);
}
