using System.Net.Http;
using Voice.Bot.Client.Net.Internal;

namespace Voice.Bot.Client.Net;

/// <summary>
/// Configuration for <see cref="BotClient.Create"/>. Mutable (not <c>init</c>-only) so it can be
/// populated from a configuration delegate - e.g. <c>AddVoiceBotClient(opts => opts.BotToken = ...)</c>
/// - the same shape as the standard <c>Microsoft.Extensions.Options</c> pattern.
/// </summary>
public sealed class BotClientOptions
{
    /// <summary>The bot's own token (<c>ibot_...</c>) - opaque to this library, passed through as-is.</summary>
    public required string BotToken { get; set; }

    /// <summary>Base address of the Voice API. Both gRPC calls and the realtime gateway (<c>/hubs/bots</c>) use it.</summary>
    public string Endpoint { get; set; } = "https://api.iopta.org";

    /// <summary>
    /// Opts into accepting an untrusted/self-signed TLS certificate on the gRPC channel and the gateway
    /// connection (its WebSocket included) - useful against a local dev backend. Defaults to <c>false</c>: this library never
    /// disables certificate validation unless the caller explicitly asks for it.
    /// </summary>
    public bool AllowUntrustedCertificates { get; set; }

    /// <summary>
    /// Escape hatch to further customize the HTTP handler (proxies, client certs, etc.) of both gRPC calls and the
    /// gateway. The gateway's WebSocket doesn't use it; when a setting here is required to reach the server,
    /// the gateway falls back to its HTTP transports.
    /// </summary>
    public Action<SocketsHttpHandler>? ConfigureHttpHandler { get; set; }

    /// <summary>
    /// How long a single API call may take before it fails with <see cref="Grpc.Core.StatusCode.DeadlineExceeded"/>;
    /// <c>null</c> or <see cref="Timeout.InfiniteTimeSpan"/> for no limit.
    /// </summary>
    public TimeSpan? RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long uploading one file (or one part of a large file) to storage may take;
    /// <c>null</c> or <see cref="Timeout.InfiniteTimeSpan"/> for no limit.
    /// </summary>
    public TimeSpan? UploadTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Prefix that triggers text commands (<see cref="Commands.CommandService"/>,
    /// <c>[Command]</c>-attributed <see cref="Commands.ModuleBase"/> methods) - e.g. <c>"!"</c> matches
    /// a message like <c>!ping</c>. The first of <see cref="TextCommandPrefixes"/>; setting it replaces
    /// them all with this one.
    /// </summary>
    public string TextCommandPrefix
    {
        get => TextCommandPrefixes.FirstOrDefault() ?? "";
        set => TextCommandPrefixes = [value];
    }

    /// <summary>
    /// Every prefix that triggers text commands, e.g. <c>["!", "?"]</c>. Defaults to the
    /// <c>VOICE_PREFIX</c> environment variable, or <c>"!"</c>. An empty list leaves only
    /// <see cref="MentionPrefix"/>.
    /// </summary>
    public IList<string> TextCommandPrefixes { get; set; } = [Environment.GetEnvironmentVariable(PrefixVariable) is { Length: > 0 } prefix ? prefix : "!"];

    /// <summary>Whether a mention of the bot works as a prefix too: <c>@bot ping</c>. Default <c>true</c>.</summary>
    public bool MentionPrefix { get; set; } = true;

    /// <summary>Whether text command names ignore case, so <c>!PING</c> runs <c>ping</c>. Default <c>true</c>.</summary>
    public bool CaseInsensitiveCommands { get; set; } = true;

    /// <summary>
    /// Whether text commands and <see cref="BotClient.OnMessage"/> skip messages from other bots. Default
    /// <c>true</c>. The bot's own messages are always skipped.
    /// </summary>
    public bool IgnoreBots { get; set; } = true;

    /// <summary>
    /// Whether the hosted service makes the registered slash commands match the
    /// <c>[SlashCommand]</c> methods on start (create, update, delete). Default <c>true</c>.
    /// </summary>
    public bool SyncCommands { get; set; } = true;

    /// <summary>Whether the built-in <c>help</c> text command lists the text and slash commands. A module's own <c>help</c> command wins. Default <c>true</c>.</summary>
    public bool HelpCommand { get; set; } = true;

    /// <summary>
    /// Whether <see cref="BotClient.Cache"/> remembers groups, channels, roles and members. When off, every
    /// lookup asks the server. Default <c>true</c>.
    /// </summary>
    public bool EnableCache { get; set; } = true;

    /// <summary>
    /// How command handlers run unless their attribute says otherwise. <see cref="RunMode.Sync"/> (default)
    /// runs them in event order; <see cref="RunMode.Async"/> runs them in the background.
    /// </summary>
    public RunMode DefaultRunMode { get; set; } = RunMode.Sync;

    /// <summary>The texts the bot answers with on its own: errors, failed checks, cooldowns, bad arguments.</summary>
    public BotReplies Replies { get; set; } = new();

    /// <summary>Retries of failed API calls; <see cref="RetryOptions.None"/> turns them off.</summary>
    public RetryOptions Retry { get; set; } = new();

    /// <summary>Client-side request throttle; <c>null</c> (default) for none.</summary>
    public ThrottleOptions? Throttle { get; set; }

    /// <summary>Environment variable the token is read from when <see cref="BotToken"/> is empty.</summary>
    public const string TokenVariable = "VOICE_BOT_TOKEN";

    /// <summary>Environment variable the default <see cref="TextCommandPrefixes"/> come from.</summary>
    public const string PrefixVariable = "VOICE_PREFIX";

    /// <summary>
    /// Options with <see cref="BotToken"/> read from the <c>VOICE_BOT_TOKEN</c> environment variable and
    /// the prefix from <c>VOICE_PREFIX</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException"><c>VOICE_BOT_TOKEN</c> is not set.</exception>
    public static BotClientOptions FromEnvironment()
    {
        var options = new BotClientOptions { BotToken = "" };
        options.ResolveToken();
        return options;
    }

    /// <summary>Falls back to <c>VOICE_BOT_TOKEN</c> when no token was given.</summary>
    internal void ResolveToken()
    {
        if (!string.IsNullOrWhiteSpace(BotToken)) return;
        BotToken = Environment.GetEnvironmentVariable(TokenVariable) is { Length: > 0 } token
            ? token
            : throw new InvalidOperationException($"Set BotClientOptions.BotToken or the {TokenVariable} environment variable.");
    }

    internal HttpMessageHandler CreateHttpHandler() => new UserAgentHandler(CreateSocketsHandler());

    internal SocketsHttpHandler CreateSocketsHandler()
    {
        var handler = new SocketsHttpHandler();
        ConfigureHttpHandler?.Invoke(handler);
        if (AllowUntrustedCertificates)
        {
            handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        }

        return handler;
    }
}
