using System.Net.Http;

namespace Voice.Bot.Client.Net;

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
    public string BotsApiEndpoint { get; set; } = "https://api.iopta.org";

    /// <summary>
    /// Opts into accepting an untrusted/self-signed TLS certificate on the gRPC channel and the gateway
    /// connection - useful against a local dev backend. Defaults to <c>false</c>: this library never
    /// disables certificate validation unless the caller explicitly asks for it.
    /// </summary>
    public bool AllowUntrustedCertificates { get; set; }

    /// <summary>Escape hatch to further customize the gRPC channel's HTTP handler (proxies, client certs, etc.).</summary>
    public Action<SocketsHttpHandler>? ConfigureHttpHandler { get; set; }

    /// <summary>
    /// Prefix that triggers text commands (<see cref="Commands.CommandService"/>,
    /// <c>[Command]</c>-attributed <see cref="Commands.ModuleBase"/> methods) - e.g. <c>"!"</c> matches
    /// a message like <c>!ping</c>. Only used when <c>AddIoptaBotClient</c> wires up the DI-registered
    /// <see cref="Commands.CommandService"/>; constructing one directly takes its own prefix parameter.
    /// </summary>
    public string TextCommandPrefix { get; set; } = "!";
}
