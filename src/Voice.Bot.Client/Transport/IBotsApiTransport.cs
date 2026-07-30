using Vc.Backend.WebApi.Bots.Apis.Grpc;
using GrpcBot = Vc.Backend.Contracts.Grpc.Bot;

namespace Voice.Bot.Client.Transport;

/// <summary>
/// One method per <c>BotsApi</c> RPC, working directly with generated proto types. Kept separate from
/// <see cref="Client.BotClient"/> so tests can substitute a mock and exercise request/response mapping
/// without a live gRPC channel. Implementations must translate a failed call's <c>RpcException</c> into
/// <see cref="BotApiException"/> (see <see cref="GrpcBotsApiTransport"/>) - callers never see raw
/// <c>RpcException</c>.
/// </summary>
internal interface IBotsApiTransport
{
    Task<GrpcBot> GetMeAsync(CancellationToken ct);
    Task SetStatusAsync(SetStatusRequest request, CancellationToken ct);
    Task<JoinVoiceChannelResponse> JoinVoiceChannelAsync(JoinVoiceChannelRequest request, CancellationToken ct);
    Task LeaveVoiceChannelAsync(LeaveVoiceChannelRequest request, CancellationToken ct);
    Task<MessageInfo> SendMessageAsync(SendMessageRequest request, CancellationToken ct);
    Task<MessageInfo> UpdateMessageAsync(UpdateMessageRequest request, CancellationToken ct);
    Task<DeleteMessageResponse> DeleteMessageAsync(DeleteMessageRequest request, CancellationToken ct);
    Task<MessageInfo> AddReactionAsync(ReactionRequest request, CancellationToken ct);
    Task<MessageInfo> RemoveReactionAsync(ReactionRequest request, CancellationToken ct);
    Task TypingAsync(TypingRequest request, CancellationToken ct);
    Task SetTypingAsync(BotTypingRequest request, CancellationToken ct);
    Task<GetMyGroupsResponse> GetMyGroupsAsync(CancellationToken ct);
    Task<BotGroupDetails> GetGroupAsync(GetGroupRequest request, CancellationToken ct);
    Task<GetGroupUsersResponse> GetGroupUsersAsync(GetGroupUsersRequest request, CancellationToken ct);
    Task<BotChannelDetails> GetChannelAsync(GetChannelRequest request, CancellationToken ct);
    Task RespondToInteractionAsync(RespondToInteractionRequest request, CancellationToken ct);
    Task<MessageInfo> SendInteractionFollowupAsync(SendInteractionFollowupRequest request, CancellationToken ct);
    Task<BotCategoryInfo> CreateCategoryAsync(CreateCategoryRequest request, CancellationToken ct);
    Task<BotChannelInfo> CreateChannelAsync(CreateChannelRequest request, CancellationToken ct);
    Task<BotRole> CreateRoleAsync(CreateRoleRequest request, CancellationToken ct);
    Task<BotRole> UpdateRoleAsync(UpdateRoleRequest request, CancellationToken ct);
    Task DeleteRoleAsync(DeleteRoleRequest request, CancellationToken ct);
    Task AssignRoleAsync(RoleAssignmentRequest request, CancellationToken ct);
    Task RemoveRoleAsync(RoleAssignmentRequest request, CancellationToken ct);
}
