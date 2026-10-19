namespace Voice.Bot.Client.Net.Gateway;

/// <summary>
/// Deliberately dumb transport for the bot-gateway (<c>/hubs/bots</c>): forwards raw
/// <c>(topic, json)</c> pairs with zero knowledge of event shapes, so parsing and dispatch can be
/// tested with synthetic strings, without a real <c>HubConnection</c>.
/// </summary>
internal interface IBotGatewayConnection : IAsyncDisposable
{
    /// <summary>Raised for every message on any of the four topics in <see cref="GatewayTopics"/> - args are (topic, rawJson).</summary>
    event Action<string, string>? MessageReceived;

    /// <summary>Raised on connection-level failures (failed connect attempts, dropped connections, handler exceptions).</summary>
    event Action<Exception>? Error;

    /// <summary>Raised every time the connection is (re)established.</summary>
    event Action? Connected;

    Task StartAsync(CancellationToken ct);
    Task StopAsync();
}
