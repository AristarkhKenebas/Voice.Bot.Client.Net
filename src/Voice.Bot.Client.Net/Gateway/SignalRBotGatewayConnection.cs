using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;

namespace Voice.Bot.Client.Net.Gateway;

/// <summary>
/// Real <see cref="IBotGatewayConnection"/> - wraps a <see cref="HubConnection"/> to
/// <c>/hubs/bots</c>, authenticated via the SignalR <c>AccessTokenProvider</c> (query-string
/// <c>?access_token=</c> on the WebSocket handshake, per ASP.NET Core SignalR client behavior - not a
/// header). The backend subscribes the connection to the right SignalR groups server-side on connect;
/// this class does nothing beyond opening the connection and forwarding whatever the four topics send.
/// </summary>
internal sealed class SignalRBotGatewayConnection : IBotGatewayConnection
{
    private readonly HubConnection _connection;

    public event Action<string, string>? MessageReceived;
    public event Action<Exception>? Error;

    public SignalRBotGatewayConnection(BotClientOptions options)
    {
        _connection = new HubConnectionBuilder()
            .WithUrl($"{options.BotsApiEndpoint}/hubs/bots", httpOptions =>
            {
                httpOptions.AccessTokenProvider = () => Task.FromResult<string?>(options.BotToken);
                if (options.AllowUntrustedCertificates)
                {
                    httpOptions.HttpMessageHandlerFactory = handler =>
                    {
                        if (handler is HttpClientHandler clientHandler)
                        {
                            clientHandler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
                        }
                        return handler;
                    };
                }
            })
            .WithAutomaticReconnect()
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
                    Error?.Invoke(ex);
                }
            });
        }

        _connection.Closed += ex => { if (ex is not null) Error?.Invoke(ex); return Task.CompletedTask; };
        _connection.Reconnecting += ex => { if (ex is not null) Error?.Invoke(ex); return Task.CompletedTask; };
    }

    public Task StartAsync(CancellationToken ct) => _connection.StartAsync(ct);

    public Task StopAsync() => _connection.StopAsync();

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}
