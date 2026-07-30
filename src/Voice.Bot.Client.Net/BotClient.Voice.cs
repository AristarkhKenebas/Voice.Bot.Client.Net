using Voice.Bot.Client.Net.Models;
using GrpcJoinVoiceChannelRequest = Vc.Backend.WebApi.Bots.Apis.Grpc.JoinVoiceChannelRequest;
using GrpcLeaveVoiceChannelRequest = Vc.Backend.WebApi.Bots.Apis.Grpc.LeaveVoiceChannelRequest;

namespace Voice.Bot.Client.Net;

/// <summary>Voice channel connect/disconnect - requires the <c>GuildVoiceStates</c> intent.</summary>
public sealed partial class BotClient
{
    public async Task<JoinVoiceChannelResult> JoinVoiceChannelAsync(Guid channelId, CancellationToken ct = default) =>
        (await _transport.JoinVoiceChannelAsync(new GrpcJoinVoiceChannelRequest { ChannelId = channelId.ToString() }, ct)).ToModel();

    public Task LeaveVoiceChannelAsync(Guid channelId, CancellationToken ct = default) =>
        _transport.LeaveVoiceChannelAsync(new GrpcLeaveVoiceChannelRequest { ChannelId = channelId.ToString() }, ct);
}
