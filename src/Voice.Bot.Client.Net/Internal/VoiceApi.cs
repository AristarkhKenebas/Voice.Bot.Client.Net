using System.Runtime.CompilerServices;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Vc.Backend.WebApi.Attachments.Grpc;
using Vc.Backend.WebApi.Channels.Grpc;
using Vc.Backend.WebApi.Groups.Grpc;
using Vc.Backend.WebApi.Interactions.Grpc;
using Vc.Backend.WebApi.Messages.Grpc;
using Vc.Backend.WebApi.Users.Grpc;

namespace Voice.Bot.Client.Net.Internal;

/// <summary>
/// The generated clients of every Voice API service over one shared <see cref="CallInvoker"/>. Every
/// call goes through <see cref="CallAsync{T}"/>, which attaches <c>Authorization: Bearer &lt;bot-token&gt;</c>,
/// applies <see cref="BotClientOptions.RequestTimeout"/>, turns a failed call's <see cref="RpcException"/>
/// into a <see cref="BotApiException"/> - callers never see raw <see cref="RpcException"/> - and retries it
/// per <see cref="BotClientOptions.Retry"/>.
/// </summary>
internal sealed class VoiceApi : IAsyncDisposable
{
    public const int MaxMessageSize = 64 * 1024 * 1024;

    private readonly GrpcChannel? _ownedChannel;
    private readonly Metadata _authHeaders;
    private readonly TimeSpan? _timeout;

    public Retrier Retrier { get; }

    public UsersApi.UsersApiClient Users { get; }
    public GroupsApi.GroupsApiClient Groups { get; }
    public ChannelsApi.ChannelsApiClient Channels { get; }
    public MessagesApi.MessagesApiClient Messages { get; }
    public AttachmentsApi.AttachmentsApiClient Attachments { get; }
    public InteractionsApi.InteractionsApiClient Interactions { get; }

    public VoiceApi(
        CallInvoker invoker, string botToken, TimeSpan? timeout, GrpcChannel? ownedChannel = null,
        RetryOptions? retry = null, ThrottleOptions? throttle = null, ILogger? logger = null)
    {
        _ownedChannel = ownedChannel;
        Retrier = new Retrier(retry ?? new RetryOptions(), throttle, logger ?? NullLogger.Instance);
        _authHeaders = new Metadata { { "Authorization", $"Bearer {botToken}" } };
        _timeout = Limit(timeout);
        Users = new UsersApi.UsersApiClient(invoker);
        Groups = new GroupsApi.GroupsApiClient(invoker);
        Channels = new ChannelsApi.ChannelsApiClient(invoker);
        Messages = new MessagesApi.MessagesApiClient(invoker);
        Attachments = new AttachmentsApi.AttachmentsApiClient(invoker);
        Interactions = new InteractionsApi.InteractionsApiClient(invoker);
    }

    public static VoiceApi Create(BotClientOptions options, ILogger? logger = null)
    {
        var channel = GrpcChannel.ForAddress(options.Endpoint, new GrpcChannelOptions
        {
            HttpHandler = options.CreateHttpHandler(),
            MaxReceiveMessageSize = MaxMessageSize,
        });
        return new VoiceApi(channel.CreateCallInvoker(), options.BotToken, options.RequestTimeout, channel, options.Retry, options.Throttle, logger);
    }

    /// <summary><c>null</c>, <see cref="Timeout.InfiniteTimeSpan"/> and anything too large to be a deadline all mean "no limit".</summary>
    public static TimeSpan? Limit(TimeSpan? timeout) =>
        timeout is { } value && value > TimeSpan.Zero && value < TimeSpan.FromDays(365) ? value : null;

    /// <param name="idempotency">Whether the call may be repeated after a network error - see <see cref="RetryOptions"/>.</param>
    public Task<T> CallAsync<T>(
        Func<CallOptions, AsyncUnaryCall<T>> call, CancellationToken ct, Idempotency idempotency = Idempotency.Safe,
        [CallerMemberName] string caller = "") =>
        Retrier.RunAsync(() => CallOnceAsync(call, ct), idempotency, caller, ct);

    /// <summary>
    /// A delete call: retried like a safe call, and <see cref="StatusCode.NotFound"/> after a retry counts as
    /// success, since the attempt whose answer was lost already deleted it.
    /// </summary>
    public Task DeleteAsync<T>(Func<CallOptions, AsyncUnaryCall<T>> call, CancellationToken ct, [CallerMemberName] string caller = "") =>
        Retrier.RunAsync(() => CallOnceAsync(call, ct), Idempotency.Delete, caller, ct);

    private async Task<T> CallOnceAsync<T>(Func<CallOptions, AsyncUnaryCall<T>> call, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var options = new CallOptions(_authHeaders, _timeout is { } timeout ? DateTime.UtcNow + timeout : null, ct);
        try
        {
            return await call(options).ConfigureAwait(false);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled && ct.IsCancellationRequested)
        {
            throw new OperationCanceledException(ex.Message, ex, ct);
        }
        catch (RpcException ex)
        {
            throw BotApiException.FromRpcException(ex);
        }
    }

    public ValueTask DisposeAsync()
    {
        _ownedChannel?.Dispose();
        return ValueTask.CompletedTask;
    }
}
