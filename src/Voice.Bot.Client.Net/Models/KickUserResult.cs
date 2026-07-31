using Wire = Vc.Backend.WebApi.Bots.Apis.Grpc.KickUserResponse;

namespace Voice.Bot.Client.Net.Models;

/// <summary>Mirrors <c>bots.KickUserResponse</c>.</summary>
public sealed record KickUserResult(Guid GroupId, Guid UserId, bool Success);

internal static class KickUserResultExtensions
{
    public static KickUserResult ToModel(this Wire grpc) => new(Guid.Parse(grpc.GroupId), Guid.Parse(grpc.UserId), grpc.Success);
}
