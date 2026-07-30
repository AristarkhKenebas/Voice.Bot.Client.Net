using Wire = Vc.Backend.WebApi.Bots.Apis.Grpc.JoinVoiceChannelResponse;

namespace Voice.Bot.Client.Net.Models;

/// <summary>Mirrors <c>bots.JoinVoiceChannelResponse</c> - a LiveKit access token for the room.</summary>
public sealed record JoinVoiceChannelResult(string Token, string ServerUrl);

internal static class JoinVoiceChannelResultExtensions
{
    public static JoinVoiceChannelResult ToModel(this Wire grpc) => new(grpc.Token, grpc.ServerUrl);
}
