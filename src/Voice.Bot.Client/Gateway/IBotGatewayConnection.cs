namespace Voice.Bot.Client.Gateway;

/// <summary>
/// Deliberately dumb transport for the bot-gateway (<c>/hubs/bots</c>): forwards raw
/// <c>(topic, json)</c> pairs with zero knowledge of event shapes. Keeping all Type/PayloadJson
/// parsing out of this interface is what lets <c>GatewayEventParsingTests</c> exercise the actual
/// parsing logic with synthetic strings, without a real <c>HubConnection</c> or mocked SignalR
/// internals.
/// </summary>
internal interface IBotGatewayConnection : IAsyncDisposable
{
    /// <summary>Raised for every message on any of the four topics in <see cref="GatewayTopics"/> - args are (topic, rawJson).</summary>
    event Action<string, string>? MessageReceived;

    /// <summary>Raised on connection-level failures (initial connect, dropped/exhausted reconnect, handler exceptions).</summary>
    event Action<Exception>? Error;

    Task StartAsync(CancellationToken ct);
    Task StopAsync();
}
