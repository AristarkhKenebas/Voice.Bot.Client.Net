using System.Net.Http;

namespace Voice.Bot.Client;

/// <summary>
/// Configuration for <see cref="BotClient.Create"/>. Mutable (not <c>init</c>-only) so it can be
/// populated from a configuration delegate - e.g. <c>AddIoptaBotClient(opts => opts.BotToken = ...)</c>
/// - the same shape as the standard <c>Microsoft.Extensions.Options</c> pattern.
/// </summary>
public sealed class BotClientOptions
{
    /// <summary>The bot's own token (<c>ibot_{hex32}_{secret}</c>) - opaque to this library, passed through as-is.</summary>
    public required string BotToken { get; set; }

    /// <summary>
    /// <c>BotsApi</c> gRPC endpoint (self-service, bot-token auth). Default matches the backend's dev
    /// Compose port for <c>Vc.Backend.WebApi.Bots.Apis</c> - NOT the owner-only <c>UsersBots</c> port
    /// (8096), which this library does not call.
    /// </summary>
    public string BotsApiEndpoint { get; set; } = "https://localhost:8099";

    /// <summary>Bot-gateway SignalR hub endpoint (<c>/hubs/bots</c>).</summary>
    public string GatewayEndpoint { get; set; } = "https://localhost:8098/hubs/bots";

    /// <summary>
    /// <c>InteractionsApi</c> gRPC endpoint - the 4 slash-command CRUD RPCs
    /// (<see cref="Interactions.InteractionService.RegisterCommandsAsync"/>), same bot-token auth as
    /// <see cref="BotsApiEndpoint"/>. Default matches the backend's dev port for
    /// <c>Vc.Backend.WebApi.Interactions</c>.
    /// </summary>
    public string InteractionsApiEndpoint { get; set; } = "https://localhost:8109";

    /// <summary>
    /// Opts into accepting an untrusted/self-signed TLS certificate on the gRPC channel and the gateway
    /// connection - useful against a local dev backend. Defaults to <c>false</c>: this library never
    /// disables certificate validation unless the caller explicitly asks for it.
    /// </summary>
    public bool AllowUntrustedCertificates { get; set; }

    /// <summary>Escape hatch to further customize the gRPC channel's HTTP handler (proxies, client certs, etc.).</summary>
    public Action<SocketsHttpHandler>? ConfigureHttpHandler { get; set; }
}
