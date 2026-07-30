using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using Vc.Backend.WebApi.Bots.Apis.Grpc;
using GrpcBot = Vc.Backend.Contracts.Grpc.Bot;

namespace Voice.Bot.Client.Net.Transport;

/// <summary>
/// Real <see cref="IBotsApiTransport"/> - wraps the generated <see cref="BotsApi.BotsApiClient"/> over
/// a <see cref="GrpcChannel"/> built from <see cref="BotClientOptions.BotsApiEndpoint"/>. Every call
/// attaches <c>Authorization: Bearer &lt;bot-token&gt;</c> metadata (the bot-token auth scheme, not
/// Keycloak JWT - see BotTokenAuthenticationHandler on the backend) and every <see cref="RpcException"/>
/// is translated into a <see cref="BotApiException"/> before it reaches the caller.
/// </summary>
internal sealed class GrpcBotsApiTransport : IBotsApiTransport, IAsyncDisposable
{
    private readonly GrpcChannel _channel;
    private readonly BotsApi.BotsApiClient _client;
    private readonly Metadata _authHeaders;

    public GrpcBotsApiTransport(BotClientOptions options)
    {
        var handler = new SocketsHttpHandler();
        options.ConfigureHttpHandler?.Invoke(handler);

        if (options.AllowUntrustedCertificates)
        {
            handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        }

        _channel = GrpcChannel.ForAddress(options.BotsApiEndpoint, new GrpcChannelOptions { HttpHandler = handler });
        _client = new BotsApi.BotsApiClient(_channel);
        _authHeaders = new Metadata { { "Authorization", $"Bearer {options.BotToken}" } };
    }

    public Task<GrpcBot> GetMeAsync(CancellationToken ct) =>
        CallAsync(() => _client.GetMeAsync(new Empty(), _authHeaders, cancellationToken: ct));

    public Task SetStatusAsync(SetStatusRequest request, CancellationToken ct) =>
        CallVoidAsync(() => _client.SetStatusAsync(request, _authHeaders, cancellationToken: ct));

    public Task<JoinVoiceChannelResponse> JoinVoiceChannelAsync(JoinVoiceChannelRequest request, CancellationToken ct) =>
        CallAsync(() => _client.JoinVoiceChannelAsync(request, _authHeaders, cancellationToken: ct));

    public Task LeaveVoiceChannelAsync(LeaveVoiceChannelRequest request, CancellationToken ct) =>
        CallVoidAsync(() => _client.LeaveVoiceChannelAsync(request, _authHeaders, cancellationToken: ct));

    public Task<MessageInfo> SendMessageAsync(SendMessageRequest request, CancellationToken ct) =>
        CallAsync(() => _client.SendMessageAsync(request, _authHeaders, cancellationToken: ct));

    public Task<MessageInfo> UpdateMessageAsync(UpdateMessageRequest request, CancellationToken ct) =>
        CallAsync(() => _client.UpdateMessageAsync(request, _authHeaders, cancellationToken: ct));

    public Task<DeleteMessageResponse> DeleteMessageAsync(DeleteMessageRequest request, CancellationToken ct) =>
        CallAsync(() => _client.DeleteMessageAsync(request, _authHeaders, cancellationToken: ct));

    public Task<MessageInfo> AddReactionAsync(ReactionRequest request, CancellationToken ct) =>
        CallAsync(() => _client.AddReactionAsync(request, _authHeaders, cancellationToken: ct));

    public Task<MessageInfo> RemoveReactionAsync(ReactionRequest request, CancellationToken ct) =>
        CallAsync(() => _client.RemoveReactionAsync(request, _authHeaders, cancellationToken: ct));

    public Task TypingAsync(TypingRequest request, CancellationToken ct) =>
        CallVoidAsync(() => _client.TypingAsync(request, _authHeaders, cancellationToken: ct));

    public Task SetTypingAsync(BotTypingRequest request, CancellationToken ct) =>
        CallVoidAsync(() => _client.SetTypingAsync(request, _authHeaders, cancellationToken: ct));

    public Task<GetMyGroupsResponse> GetMyGroupsAsync(CancellationToken ct) =>
        CallAsync(() => _client.GetMyGroupsAsync(new Empty(), _authHeaders, cancellationToken: ct));

    public Task<BotGroupDetails> GetGroupAsync(GetGroupRequest request, CancellationToken ct) =>
        CallAsync(() => _client.GetGroupAsync(request, _authHeaders, cancellationToken: ct));

    public Task<GetGroupUsersResponse> GetGroupUsersAsync(GetGroupUsersRequest request, CancellationToken ct) =>
        CallAsync(() => _client.GetGroupUsersAsync(request, _authHeaders, cancellationToken: ct));

    public Task<BotChannelDetails> GetChannelAsync(GetChannelRequest request, CancellationToken ct) =>
        CallAsync(() => _client.GetChannelAsync(request, _authHeaders, cancellationToken: ct));

    public Task RespondToInteractionAsync(RespondToInteractionRequest request, CancellationToken ct) =>
        CallVoidAsync(() => _client.RespondToInteractionAsync(request, _authHeaders, cancellationToken: ct));

    public Task<MessageInfo> SendInteractionFollowupAsync(SendInteractionFollowupRequest request, CancellationToken ct) =>
        CallAsync(() => _client.SendInteractionFollowupAsync(request, _authHeaders, cancellationToken: ct));

    public Task<BotCategoryInfo> CreateCategoryAsync(CreateCategoryRequest request, CancellationToken ct) =>
        CallAsync(() => _client.CreateCategoryAsync(request, _authHeaders, cancellationToken: ct));

    public Task<BotChannelInfo> CreateChannelAsync(CreateChannelRequest request, CancellationToken ct) =>
        CallAsync(() => _client.CreateChannelAsync(request, _authHeaders, cancellationToken: ct));

    public Task<BotRole> CreateRoleAsync(CreateRoleRequest request, CancellationToken ct) =>
        CallAsync(() => _client.CreateRoleAsync(request, _authHeaders, cancellationToken: ct));

    public Task<BotRole> UpdateRoleAsync(UpdateRoleRequest request, CancellationToken ct) =>
        CallAsync(() => _client.UpdateRoleAsync(request, _authHeaders, cancellationToken: ct));

    public Task DeleteRoleAsync(DeleteRoleRequest request, CancellationToken ct) =>
        CallVoidAsync(() => _client.DeleteRoleAsync(request, _authHeaders, cancellationToken: ct));

    public Task AssignRoleAsync(RoleAssignmentRequest request, CancellationToken ct) =>
        CallVoidAsync(() => _client.AssignRoleAsync(request, _authHeaders, cancellationToken: ct));

    public Task RemoveRoleAsync(RoleAssignmentRequest request, CancellationToken ct) =>
        CallVoidAsync(() => _client.RemoveRoleAsync(request, _authHeaders, cancellationToken: ct));

    private static async Task<T> CallAsync<T>(Func<AsyncUnaryCall<T>> call)
    {
        try
        {
            return await call();
        }
        catch (RpcException ex)
        {
            throw BotApiException.FromRpcException(ex);
        }
    }

    private static async Task CallVoidAsync<T>(Func<AsyncUnaryCall<T>> call)
    {
        try
        {
            await call();
        }
        catch (RpcException ex)
        {
            throw BotApiException.FromRpcException(ex);
        }
    }

    public ValueTask DisposeAsync()
    {
        _channel.Dispose();
        return ValueTask.CompletedTask;
    }
}
