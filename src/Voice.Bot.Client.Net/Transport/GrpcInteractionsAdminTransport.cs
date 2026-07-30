using Grpc.Core;
using Grpc.Net.Client;
using Vc.Backend.WebApi.Interactions.Grpc;

namespace Voice.Bot.Client.Net.Transport;

/// <summary>
/// Real <see cref="IInteractionsAdminTransport"/> - wraps the generated
/// <see cref="InteractionsApi.InteractionsApiClient"/> over a <see cref="GrpcChannel"/> built from
/// <see cref="BotClientOptions.BotsApiEndpoint"/> (Interactions shares the same gateway endpoint as
/// BotsApi), same bot-token-as-Bearer and TLS handling as <see cref="GrpcBotsApiTransport"/>.
/// </summary>
internal sealed class GrpcInteractionsAdminTransport : IInteractionsAdminTransport, IAsyncDisposable
{
    private readonly GrpcChannel _channel;
    private readonly InteractionsApi.InteractionsApiClient _client;
    private readonly Metadata _authHeaders;

    public GrpcInteractionsAdminTransport(BotClientOptions options)
    {
        var handler = new SocketsHttpHandler();
        options.ConfigureHttpHandler?.Invoke(handler);

        if (options.AllowUntrustedCertificates)
        {
            handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        }

        _channel = GrpcChannel.ForAddress(options.BotsApiEndpoint, new GrpcChannelOptions { HttpHandler = handler });
        _client = new InteractionsApi.InteractionsApiClient(_channel);
        _authHeaders = new Metadata { { "Authorization", $"Bearer {options.BotToken}" } };
    }

    public Task<BotCommand> RegisterCommandAsync(RegisterCommandRequest request, CancellationToken ct) =>
        CallAsync(() => _client.RegisterCommandAsync(request, _authHeaders, cancellationToken: ct));

    public Task<BotCommand> UpdateCommandAsync(UpdateCommandRequest request, CancellationToken ct) =>
        CallAsync(() => _client.UpdateCommandAsync(request, _authHeaders, cancellationToken: ct));

    public Task DeleteCommandAsync(DeleteCommandRequest request, CancellationToken ct) =>
        CallVoidAsync(() => _client.DeleteCommandAsync(request, _authHeaders, cancellationToken: ct));

    public Task<GetBotCommandsResponse> GetBotCommandsAsync(GetBotCommandsRequest request, CancellationToken ct) =>
        CallAsync(() => _client.GetBotCommandsAsync(request, _authHeaders, cancellationToken: ct));

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
