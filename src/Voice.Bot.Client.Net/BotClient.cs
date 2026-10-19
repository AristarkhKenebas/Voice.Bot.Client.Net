using System.Net.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using GatewayQueue = System.Threading.Channels.Channel;
using UnboundedChannelOptions = System.Threading.Channels.UnboundedChannelOptions;
using Voice.Bot.Client.Net.Events;
using Voice.Bot.Client.Net.Gateway;
using Voice.Bot.Client.Net.Internal;
using Voice.Bot.Client.Net.Models;

namespace Voice.Bot.Client.Net;

/// <summary>
/// Facade for the Voice API (bots are ordinary users authenticated with their bot token) and the
/// realtime gateway (<c>/hubs/bots</c> SignalR). Construct via <see cref="Create"/>; every API method
/// throws <see cref="BotApiException"/> (never a raw <c>RpcException</c>) on failure.
/// </summary>
/// <remarks>
/// Gateway events are handled one at a time, in the order they arrive: a handler for a message's
/// creation finishes before the handler for its deletion starts. A slow handler therefore delays the
/// events after it - move long work to <c>Task.Run</c> or <see cref="RunMode.Async"/>. The one exception
/// is a handler that waits for another event (<see cref="BotContext.AskAsync"/>,
/// <see cref="BotContext.ConfirmAsync"/>, <see cref="WaitForAsync{T}"/>, <see cref="CollectReactionsAsync"/>):
/// from the moment it starts waiting, the events after it are handled meanwhile, so one user's question
/// doesn't hold up everyone else.
/// <para>
/// Split into one partial file per concern (see <c>BotClient.*.cs</c> next to this file) - this file
/// only holds construction, the gateway events every part of the API can raise, and disposal.
/// </para>
/// </remarks>
public sealed partial class BotClient : IAsyncDisposable, IDisposable
{
    private readonly VoiceApi _api;
    private readonly IBotGatewayConnection _gateway;
    private readonly Uploader _uploader;
    private readonly SemaphoreSlim _meLock = new(1, 1);
    private User? _me;
    private readonly System.Threading.Channels.Channel<ParsedEvent> _events =
        GatewayQueue.CreateUnbounded<ParsedEvent>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Task _dispatchLoop;
    private readonly CancellationTokenSource _lifetime = new();
    private int _disposed;

    public event Func<MessageCreatedEvent, Task>? MessageCreated;
    public event Func<MessageUpdatedEvent, Task>? MessageUpdated;
    public event Func<MessageDeletedEvent, Task>? MessageDeleted;
    public event Func<ReactionEvent, Task>? ReactionAdded;
    public event Func<ReactionEvent, Task>? ReactionRemoved;
    public event Func<TypingEvent, Task>? UserTyping;
    public event Func<BotTypingEvent, Task>? BotTyping;
    public event Func<GroupStructureChangedEvent, Task>? GroupStructureChanged;
    public event Func<MemberEvent, Task>? MemberJoined;
    public event Func<MemberEvent, Task>? MemberLeft;
    public event Func<MemberEvent, Task>? MemberKicked;
    public event Func<UserProfileUpdatedEvent, Task>? UserProfileUpdated;
    public event Func<UserStatusChangedEvent, Task>? UserStatusChanged;
    public event Func<VoiceEvent, Task>? VoiceConnected;
    public event Func<VoiceEvent, Task>? VoiceDisconnected;
    public event Func<BotInteractionEvent, Task>? InteractionCreated;
    public event Func<UnknownGatewayEvent, Task>? UnhandledEvent;

    /// <summary>
    /// The gateway (re)connected. Raised once per successful connection, in event order, with the bot's
    /// own freshly fetched profile - a good place to set the status or announce the bot.
    /// </summary>
    public event Func<User, Task>? Ready;

    /// <summary>Connection failures, unparseable gateway data and exceptions thrown by event handlers.</summary>
    public event Func<Exception, Task>? ConnectionError;

    /// <summary>
    /// Lazily filled groups, channels, roles and members, cleared by the gateway events that change them.
    /// See <see cref="BotCache"/>.
    /// </summary>
    public BotCache Cache { get; }

    /// <summary>The options the client was created with; the command services read their settings from here.</summary>
    internal BotClientOptions Options { get; }

    /// <summary>Where the client and the command services write their logs.</summary>
    internal ILoggerFactory LoggerFactory { get; }

    private readonly ILogger _logger;

    /// <summary>Wires the real gRPC channel, gateway connection and upload client from <paramref name="options"/>.</summary>
    /// <param name="options">When <see cref="BotClientOptions.BotToken"/> is empty, the <c>VOICE_BOT_TOKEN</c> environment variable is used.</param>
    /// <param name="loggerFactory">Receives connection state, retries and command errors; <c>null</c> for no logging.</param>
    public static BotClient Create(BotClientOptions options, ILoggerFactory? loggerFactory = null)
    {
        options.ResolveToken();
        var logging = loggerFactory ?? NullLoggerFactory.Instance;
        var api = VoiceApi.Create(options, logging.CreateLogger<BotClient>());
        return new BotClient(
            api,
            new SignalRBotGatewayConnection(options),
            new Uploader(new HttpClient(options.CreateHttpHandler()), options.UploadTimeout, api.Retrier),
            options,
            logging);
    }

    internal BotClient(
        VoiceApi api, IBotGatewayConnection gateway, Uploader uploader, BotClientOptions? options = null, ILoggerFactory? loggerFactory = null)
    {
        _api = api;
        _gateway = gateway;
        _uploader = uploader;
        Options = options ?? new BotClientOptions { BotToken = "" };
        LoggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = LoggerFactory.CreateLogger<BotClient>();
        Cache = new BotCache(this, Options.EnableCache);
        _gateway.MessageReceived += OnGatewayMessageReceived;
        _gateway.Error += OnGatewayError;
        _gateway.Connected += OnGatewayConnected;
        _dispatchLoop = Task.Run(DispatchLoopAsync);
    }

    /// <summary>
    /// Makes the API calls inside a <c>using</c> block follow <paramref name="retry"/> instead of
    /// <see cref="BotClientOptions.Retry"/>. Applies to the current async flow only, so other work is
    /// unaffected.
    /// </summary>
    /// <example>
    /// <code>
    /// using (client.WithRetries(RetryOptions.None))
    /// {
    ///     await client.SendMessageAsync(channelId, "fails fast");
    /// }
    /// </code>
    /// </example>
    public IDisposable WithRetries(RetryOptions retry) => _api.Retrier.Use(retry);

    /// <summary>
    /// Shuts the client down: stops the gateway and the scheduled tasks, stops handling queued events, ends
    /// pending waits (<see cref="WaitForAsync{T}"/> throws <see cref="OperationCanceledException"/>,
    /// <see cref="BotContext.AskAsync"/> too), gives the handlers still running a few seconds to finish and
    /// releases the connections. Safe to call from inside an event handler.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;

        await _lifetime.CancelAsync().ConfigureAwait(false);
        _gateway.MessageReceived -= OnGatewayMessageReceived;
        _gateway.Error -= OnGatewayError;
        _gateway.Connected -= OnGatewayConnected;
        await _gateway.DisposeAsync().ConfigureAwait(false);
        _events.Writer.TryComplete();
        CancelWaiters();
        if (_slot.Value is null)
        {
            var running = Task.WhenAll([_dispatchLoop, .. Snapshot(_released), .. Snapshot(_background)]);
            await running.WaitAsync(ShutdownGrace).ContinueWith(_ => { }, TaskScheduler.Default).ConfigureAwait(false);
        }

        await _api.DisposeAsync().ConfigureAwait(false);
        _uploader.Dispose();
    }

    /// <summary>Same as <see cref="DisposeAsync"/>, for containers and code that can only dispose synchronously.</summary>
    public void Dispose()
    {
        if (_slot.Value is not null)
        {
            _ = DisposeAsync().AsTask();
            return;
        }

        Task.Run(() => DisposeAsync().AsTask()).GetAwaiter().GetResult();
    }

    /// <summary>How long disposal waits for the handlers that are still running.</summary>
    private static readonly TimeSpan ShutdownGrace = TimeSpan.FromSeconds(5);
}
