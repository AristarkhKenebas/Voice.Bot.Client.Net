using Grpc.Core;

namespace Voice.Bot.Client.Net.Testing;

/// <summary>A failure <see cref="FakeVoiceServer.FailNext"/> planned for a call.</summary>
/// <param name="AfterApplying">The call does its work first and only its answer is lost, like a dropped connection.</param>
internal sealed record PlannedFailure(RpcException Error, bool AfterApplying);

/// <summary>
/// Answers unary gRPC calls in memory by method name - no network, no server. Checks that every call
/// carries <c>Authorization: Bearer ...</c> (the User-Agent travels on the HTTP layer, which this
/// invoker replaces, so it is not checked). Gateway events a handler sends with <see cref="AfterResponse"/>
/// go out after the call's answer, like on the real server.
/// </summary>
internal sealed class FakeCallInvoker : CallInvoker
{
    [ThreadStatic]
    private static List<Action>? _afterResponse;

    private readonly Dictionary<string, Func<object, object>> _handlers = [];
    private readonly Func<string, PlannedFailure?> _failure;
    private readonly Action<Action> _later;

    /// <param name="failure">The failure planned for the next call of a method, if any; also counts the call.</param>
    /// <param name="later">Runs work after the current call has answered, in call order.</param>
    public FakeCallInvoker(Func<string, PlannedFailure?> failure, Action<Action> later)
    {
        _failure = failure;
        _later = later;
    }

    /// <summary>From inside a handler: runs <paramref name="work"/> (a gateway event, say) once the call has answered.</summary>
    public static void AfterResponse(Action work) =>
        (_afterResponse ?? throw new InvalidOperationException("AfterResponse works only inside a call handler.")).Add(work);

    /// <summary>Handles <c>/{service}/{method}</c>, e.g. <c>("messages.MessagesApi", "CreateMessage")</c>.</summary>
    public void Handle<TRequest, TResponse>(string service, string method, Func<TRequest, TResponse> handler) =>
        _handlers[$"{service}/{method}"] = request => handler((TRequest)request)!;

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
    {
        Task<TResponse> response;
        try
        {
            options.CancellationToken.ThrowIfCancellationRequested();
            if (options.Headers?.GetValue("authorization") is not { Length: > 7 } authorization || !authorization.StartsWith("Bearer ", StringComparison.Ordinal))
            {
                throw new RpcException(new Status(StatusCode.Unauthenticated, "No bot token."));
            }

            var failure = _failure(method.Name);
            if (failure is { AfterApplying: false }) throw failure.Error;
            if (!_handlers.TryGetValue($"{method.ServiceName}/{method.Name}", out var handler))
            {
                throw new RpcException(new Status(StatusCode.Unimplemented, $"FakeVoiceServer does not implement {method.FullName}."));
            }

            var after = _afterResponse = [];
            TResponse result;
            try
            {
                result = (TResponse)handler(request);
            }
            finally
            {
                _afterResponse = null;
            }

            response = after.Count == 0 && failure is null ? Task.FromResult(result) : Answer(result, failure?.Error, after);
        }
        catch (OperationCanceledException)
        {
            response = Task.FromException<TResponse>(new RpcException(new Status(StatusCode.Cancelled, "Cancelled")));
        }
        catch (RpcException ex)
        {
            response = Task.FromException<TResponse>(ex);
        }

        return new AsyncUnaryCall<TResponse>(response, Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => [], () => { });
    }

    /// <summary>
    /// Answers through <c>later</c>: the caller sees the answer first (its continuation runs right away), the
    /// events the call caused follow.
    /// </summary>
    private Task<TResponse> Answer<TResponse>(TResponse result, RpcException? error, List<Action> after)
    {
        var answer = new TaskCompletionSource<TResponse>();
        _later(() =>
        {
            if (error is null) answer.SetResult(result);
            else answer.SetException(error);
            foreach (var work in after) work();
        });
        return answer.Task;
    }

    public override TResponse BlockingUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) =>
        AsyncUnaryCall(method, host, options, request).ResponseAsync.GetAwaiter().GetResult();

    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) => throw new NotSupportedException();

    public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method, string? host, CallOptions options) => throw new NotSupportedException();

    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method, string? host, CallOptions options) => throw new NotSupportedException();
}

/// <summary>A gateway that never connects; <see cref="FakeVoiceServer"/> injects events straight into the client.</summary>
internal sealed class NullGateway : Gateway.IBotGatewayConnection
{
#pragma warning disable CS0067 // The fake server injects events directly.
    public event Action<string, string>? MessageReceived;
    public event Action<Exception>? Error;
    public event Action? Connected;
#pragma warning restore CS0067

    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;

    public Task StopAsync() => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>Accepts uploads to the fake storage URLs and remembers the bytes.</summary>
internal sealed class FakeStorage(Action<string, byte[]> stored) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        stored(request.RequestUri!.ToString(), body);
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue($"\"{Guid.NewGuid():N}\"");
        return response;
    }
}
