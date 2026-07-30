using Voice.Bot.Client.Net.Events;
using Voice.Bot.Client.Net.Gateway;
using Voice.Bot.Client.Net.Models;
using Voice.Bot.Client.Net.Transport;

namespace Voice.Bot.Client.Net;

/// <summary>
/// Facade for the Iopta <c>BotsApi</c> (self-service, bot-token auth) and the bot-gateway
/// (<c>/hubs/bots</c> SignalR). Construct via <see cref="Create"/>; every gRPC method throws
/// <see cref="BotApiException"/> (never a raw <c>RpcException</c>) on failure.
/// </summary>
/// <remarks>
/// Split into one partial file per concern (see <c>BotClient.*.cs</c> next to this file) - this file
/// only holds construction, the gateway events every part of the API can raise, and disposal.
/// </remarks>
public sealed partial class BotClient : IAsyncDisposable
{
    private readonly IBotsApiTransport _transport;
    private readonly IBotGatewayConnection _gateway;
    private readonly IInteractionsAdminTransport _interactionsAdmin;

    public event Func<GatewayMessage, Task>? MessageCreated;
    public event Func<GatewayMessage, Task>? MessageUpdated;
    public event Func<Guid, bool, Task>? MessageDeleted;
    public event Func<MessageReactionEvent, Task>? ReactionAdded;
    public event Func<MessageReactionEvent, Task>? ReactionRemoved;
    public event Func<TypingEvent, Task>? UserTyping;
    public event Func<BotTypingEvent, Task>? BotTyping;
    public event Func<ChannelPresenceEvent, Task>? UserConnected;
    public event Func<ChannelPresenceEvent, Task>? UserDisconnected;
    public event Func<GroupJoinedEvent, Task>? UserJoinedGroup;
    public event Func<UserStatusChangedEvent, Task>? UserStatusChanged;
    public event Func<BotInteractionEvent, Task>? InteractionCreated;
    public event Func<UnknownGatewayEvent, Task>? UnhandledEvent;
    public event Func<Exception, Task>? ConnectionError;

    /// <summary>Wires the real gRPC transports and SignalR gateway connection from <paramref name="options"/>.</summary>
    public static BotClient Create(BotClientOptions options) =>
        new(new GrpcBotsApiTransport(options), new SignalRBotGatewayConnection(options), new GrpcInteractionsAdminTransport(options));

    internal BotClient(IBotsApiTransport transport, IBotGatewayConnection gateway, IInteractionsAdminTransport interactionsAdmin)
    {
        _transport = transport;
        _gateway = gateway;
        _interactionsAdmin = interactionsAdmin;
        _gateway.MessageReceived += OnGatewayMessageReceived;
        _gateway.Error += OnGatewayError;
    }

    public async ValueTask DisposeAsync()
    {
        _gateway.MessageReceived -= OnGatewayMessageReceived;
        _gateway.Error -= OnGatewayError;
        await _gateway.DisposeAsync();
        if (_transport is IAsyncDisposable disposableTransport)
        {
            await disposableTransport.DisposeAsync();
        }
        if (_interactionsAdmin is IAsyncDisposable disposableInteractionsAdmin)
        {
            await disposableInteractionsAdmin.DisposeAsync();
        }
    }
}
