using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using Grpc.Core;
using Microsoft.AspNetCore.SignalR.Client;
using Voice.Bot.Client.Net.Internal;

namespace Voice.Bot.Client.Net.Gateway;

/// <summary>
/// Real <see cref="IBotGatewayConnection"/> - wraps a <see cref="HubConnection"/> to
/// <c>/hubs/bots</c>, authenticated via the SignalR <c>AccessTokenProvider</c> (query-string
/// <c>?access_token=</c> on the WebSocket handshake, per ASP.NET Core SignalR client behavior - not a
/// header). The backend subscribes the connection to the right SignalR groups server-side on connect;
/// this class opens the connection, keeps it open and forwards whatever the four topics send.
/// </summary>
/// <remarks>
/// The token travels in the WebSocket URL, so every error this class reports goes through
/// <see cref="Redact"/> first, and the <see cref="HubConnection"/> gets no logger (its debug logs print
/// that URL).
/// </remarks>
internal sealed partial class SignalRBotGatewayConnection : IBotGatewayConnection
{
    private readonly HubConnection _connection;
    private readonly string _token;
    private readonly GatewayRetryPolicy _retry = new();
    private readonly object _lock = new();
    private CancellationTokenSource? _running;

    public event Action<string, string>? MessageReceived;
    public event Action<Exception>? Error;
    public event Action? Connected;

    public SignalRBotGatewayConnection(BotClientOptions options)
    {
        _token = options.BotToken;
        _connection = new HubConnectionBuilder()
            .WithUrl($"{options.Endpoint.TrimEnd('/')}/hubs/bots", httpOptions =>
            {
                httpOptions.AccessTokenProvider = () => Task.FromResult<string?>(options.BotToken);
                httpOptions.Headers["User-Agent"] = UserAgentHandler.Value;
                httpOptions.HttpMessageHandlerFactory = inner => new UserAgentHandler(
                    options.AllowUntrustedCertificates || options.ConfigureHttpHandler is not null ? options.CreateSocketsHandler() : inner);

                // The WebSocket doesn't go through the handler above, so it needs the opt-in of its own.
                if (options.AllowUntrustedCertificates)
                {
                    httpOptions.WebSocketConfiguration = socket => socket.RemoteCertificateValidationCallback = (_, _, _, _) => true;
                }
            })
            .WithAutomaticReconnect(_retry)
            .Build();

        foreach (var topic in GatewayTopics.All)
        {
            _connection.On<JsonElement>(topic, payload =>
            {
                try
                {
                    MessageReceived?.Invoke(topic, payload.GetRawText());
                }
                catch (Exception ex)
                {
                    RaiseError(ex);
                }
            });
        }

        _connection.Closed += OnClosedAsync;
        _connection.Reconnecting += ex => { if (ex is not null) RaiseError(ex); return Task.CompletedTask; };
        _connection.Reconnected += _ => { Connected?.Invoke(); return Task.CompletedTask; };
    }

    public async Task StartAsync(CancellationToken ct)
    {
        CancellationTokenSource running;
        lock (_lock)
        {
            _running?.Cancel();
            _running = running = new CancellationTokenSource();
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, running.Token);
        await ConnectAsync(linked.Token).ConfigureAwait(false);
    }

    public async Task StopAsync()
    {
        lock (_lock)
        {
            _running?.Cancel();
            _running = null;
        }

        await _connection.StopAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        lock (_lock)
        {
            _running?.Cancel();
            _running = null;
        }

        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    private async Task ConnectAsync(CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            if (_connection.State != HubConnectionState.Disconnected) return;
            try
            {
                await _connection.StartAsync(ct).ConfigureAwait(false);
                Connected?.Invoke();
                return;
            }
            catch (Exception ex) when (IsRejected(ex, HttpStatusCode.Unauthorized))
            {
                throw new BotApiException(StatusCode.Unauthenticated, "The gateway rejected the bot token.", Redact(ex, _token));
            }
            catch (Exception ex) when (IsRejected(ex, HttpStatusCode.Forbidden))
            {
                throw new BotApiException(StatusCode.PermissionDenied, "The gateway refused the bot (HTTP 403).", Redact(ex, _token));
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                RaiseError(ex);
            }

            await Task.Delay(_retry.Delay(attempt + 1), ct).ConfigureAwait(false);
        }
    }

    private Task OnClosedAsync(Exception? error)
    {
        CancellationTokenSource? running;
        lock (_lock) running = _running;
        if (running is null || running.IsCancellationRequested) return Task.CompletedTask;

        if (error is not null) RaiseError(error);
        _ = RestartAsync(running.Token);
        return Task.CompletedTask;
    }

    private async Task RestartAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(_retry.Delay(1), ct).ConfigureAwait(false);
            await ConnectAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            RaiseError(ex);
        }
    }

    private void RaiseError(Exception ex) => Error?.Invoke(Redact(ex, _token));

    /// <summary>
    /// <paramref name="error"/> itself when neither it nor its inner exceptions mention the token; otherwise
    /// a <see cref="BotApiException"/> (<see cref="StatusCode.Unavailable"/>) with the same messages, the
    /// token and every <c>access_token=</c> value masked, and without the original exceptions.
    /// </summary>
    internal static Exception Redact(Exception error, string token)
    {
        if (!Mentions(error.ToString(), token)) return error;
        var messages = new List<string>();
        for (var current = error; current is not null; current = current.InnerException)
        {
            messages.Add($"{current.GetType().Name}: {Mask(current.Message, token)}");
        }

        var status = error is BotApiException api ? api.StatusCode : StatusCode.Unavailable;
        return new BotApiException(status, string.Join(" ---> ", messages));
    }

    private static bool Mentions(string text, string token) =>
        AccessToken().IsMatch(text) || token.Length > 0 && text.Contains(token, StringComparison.Ordinal);

    private static string Mask(string text, string token)
    {
        var masked = AccessToken().Replace(text, "access_token=***");
        return token.Length > 0 ? masked.Replace(token, "***", StringComparison.Ordinal) : masked;
    }

    [GeneratedRegex(@"access_token=[^&\s'""]*", RegexOptions.IgnoreCase)]
    private static partial Regex AccessToken();

    private static bool IsRejected(Exception ex, HttpStatusCode status) =>
        ex is HttpRequestException { StatusCode: { } code } && code == status ||
        ex.InnerException is { } inner && IsRejected(inner, status);
}

/// <summary>Pauses of 0, 2, 5, 10 and then 30 seconds with up to 20% random jitter, retrying forever.</summary>
internal sealed class GatewayRetryPolicy : IRetryPolicy
{
    private static readonly TimeSpan[] Delays =
        [TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];

    public TimeSpan? NextRetryDelay(RetryContext retryContext) => Delay(retryContext.PreviousRetryCount);

    /// <param name="attempt">0 for the first retry.</param>
    public TimeSpan Delay(long attempt)
    {
        var delay = Delays[(int)Math.Min(attempt, Delays.Length - 1)];
        return delay + delay * (Random.Shared.NextDouble() * 0.2);
    }
}
