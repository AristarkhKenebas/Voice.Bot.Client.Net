using Voice.Bot.Client.Components;
using Voice.Bot.Client.Events;
using Voice.Bot.Client.Gateway;
using Voice.Bot.Client.Models;
using Voice.Bot.Client.Transport;
using ModelBot = Voice.Bot.Client.Models.Bot;
using GrpcSetStatusRequest = Vc.Backend.WebApi.Bots.Apis.Grpc.SetStatusRequest;
using GrpcJoinVoiceChannelRequest = Vc.Backend.WebApi.Bots.Apis.Grpc.JoinVoiceChannelRequest;
using GrpcLeaveVoiceChannelRequest = Vc.Backend.WebApi.Bots.Apis.Grpc.LeaveVoiceChannelRequest;
using GrpcSendMessageRequest = Vc.Backend.WebApi.Bots.Apis.Grpc.SendMessageRequest;
using GrpcUpdateMessageRequest = Vc.Backend.WebApi.Bots.Apis.Grpc.UpdateMessageRequest;
using GrpcDeleteMessageRequest = Vc.Backend.WebApi.Bots.Apis.Grpc.DeleteMessageRequest;
using GrpcReactionRequest = Vc.Backend.WebApi.Bots.Apis.Grpc.ReactionRequest;
using GrpcTypingRequest = Vc.Backend.WebApi.Bots.Apis.Grpc.TypingRequest;
using GrpcBotTypingRequest = Vc.Backend.WebApi.Bots.Apis.Grpc.BotTypingRequest;
using GrpcGetGroupRequest = Vc.Backend.WebApi.Bots.Apis.Grpc.GetGroupRequest;
using GrpcGetGroupUsersRequest = Vc.Backend.WebApi.Bots.Apis.Grpc.GetGroupUsersRequest;
using GrpcGetChannelRequest = Vc.Backend.WebApi.Bots.Apis.Grpc.GetChannelRequest;
using GrpcRespondToInteractionRequest = Vc.Backend.WebApi.Bots.Apis.Grpc.RespondToInteractionRequest;
using GrpcSendInteractionFollowupRequest = Vc.Backend.WebApi.Bots.Apis.Grpc.SendInteractionFollowupRequest;
using GrpcInteractionResponseKind = Vc.Backend.WebApi.Bots.Apis.Grpc.InteractionResponseKind;
using GrpcCreateCategoryRequest = Vc.Backend.WebApi.Bots.Apis.Grpc.CreateCategoryRequest;
using GrpcCreateChannelRequest = Vc.Backend.WebApi.Bots.Apis.Grpc.CreateChannelRequest;
using GrpcCreateRoleRequest = Vc.Backend.WebApi.Bots.Apis.Grpc.CreateRoleRequest;
using GrpcUpdateRoleRequest = Vc.Backend.WebApi.Bots.Apis.Grpc.UpdateRoleRequest;
using GrpcDeleteRoleRequest = Vc.Backend.WebApi.Bots.Apis.Grpc.DeleteRoleRequest;
using GrpcRoleAssignmentRequest = Vc.Backend.WebApi.Bots.Apis.Grpc.RoleAssignmentRequest;
using GrpcInteractionChoice = Vc.Backend.WebApi.Bots.Apis.Grpc.InteractionChoice;
using GrpcRegisterCommandRequest = Vc.Backend.WebApi.Interactions.Grpc.RegisterCommandRequest;
using GrpcUpdateCommandRequest = Vc.Backend.WebApi.Interactions.Grpc.UpdateCommandRequest;
using GrpcDeleteCommandRequest = Vc.Backend.WebApi.Interactions.Grpc.DeleteCommandRequest;
using GrpcGetBotCommandsRequest = Vc.Backend.WebApi.Interactions.Grpc.GetBotCommandsRequest;

namespace Voice.Bot.Client;

/// <summary>
/// Facade for the Iopta <c>BotsApi</c> (self-service, bot-token auth) and the bot-gateway
/// (<c>/hubs/bots</c> SignalR). Construct via <see cref="Create"/>; every gRPC method throws
/// <see cref="BotApiException"/> (never a raw <c>RpcException</c>) on failure.
/// </summary>
public sealed class BotClient : IAsyncDisposable
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

    public async Task<ModelBot> GetMeAsync(CancellationToken ct = default) =>
        (await _transport.GetMeAsync(ct)).ToModel();

    /// <summary>
    /// Sets the bot's own presence status. Broadcasts live (Discord-style) to every group the bot
    /// belongs to - see <see cref="UserStatusChanged"/> for the other side of that broadcast.
    /// </summary>
    public Task SetStatusAsync(UserStatus status, string? statusText = null, CancellationToken ct = default) =>
        _transport.SetStatusAsync(new GrpcSetStatusRequest { Status = status.ToGrpc(), StatusText = statusText ?? "" }, ct);

    public async Task<JoinVoiceChannelResult> JoinVoiceChannelAsync(Guid channelId, CancellationToken ct = default) =>
        (await _transport.JoinVoiceChannelAsync(new GrpcJoinVoiceChannelRequest { ChannelId = channelId.ToString() }, ct)).ToModel();

    public Task LeaveVoiceChannelAsync(Guid channelId, CancellationToken ct = default) =>
        _transport.LeaveVoiceChannelAsync(new GrpcLeaveVoiceChannelRequest { ChannelId = channelId.ToString() }, ct);

    public async Task<MessageInfo> SendMessageAsync(
        Guid channelId, string content, Guid? replyTo = null, IReadOnlyCollection<Guid>? attachmentIds = null,
        ComponentBuilder? components = null, CancellationToken ct = default)
    {
        var request = new GrpcSendMessageRequest { ChannelId = channelId.ToString(), Content = content };
        if (replyTo.HasValue) request.ReplyTo = replyTo.Value.ToString();
        if (attachmentIds is not null) request.AttachmentIds.AddRange(attachmentIds.Select(id => id.ToString()));
        if (components is not null) request.ComponentsJson = components.Build();

        return (await _transport.SendMessageAsync(request, ct)).ToModel();
    }

    public async Task<MessageInfo> UpdateMessageAsync(
        Guid messageId, string content, ComponentBuilder? components = null, CancellationToken ct = default)
    {
        var request = new GrpcUpdateMessageRequest { MessageId = messageId.ToString(), Content = content };
        if (components is not null) request.ComponentsJson = components.Build();

        return (await _transport.UpdateMessageAsync(request, ct)).ToModel();
    }

    public async Task<DeleteMessageResult> DeleteMessageAsync(Guid messageId, CancellationToken ct = default) =>
        (await _transport.DeleteMessageAsync(new GrpcDeleteMessageRequest { MessageId = messageId.ToString() }, ct)).ToModel();

    public async Task<MessageInfo> AddReactionAsync(Guid messageId, string emoji, CancellationToken ct = default) =>
        (await _transport.AddReactionAsync(new GrpcReactionRequest { MessageId = messageId.ToString(), Emoji = emoji }, ct)).ToModel();

    public async Task<MessageInfo> RemoveReactionAsync(Guid messageId, string emoji, CancellationToken ct = default) =>
        (await _transport.RemoveReactionAsync(new GrpcReactionRequest { MessageId = messageId.ToString(), Emoji = emoji }, ct)).ToModel();

    public Task TypingAsync(Guid channelId, bool isTyping = true, CancellationToken ct = default) =>
        _transport.TypingAsync(new GrpcTypingRequest { ChannelId = channelId.ToString(), IsTyping = isTyping }, ct);

    /// <summary>
    /// Reactive "what the bot is doing right now" status (Thinking/Explored/Processing/Reviewed/
    /// Opened/ReSearch/Fetched), Discord/Claude-style - richer than the plain on/off <see cref="TypingAsync"/>.
    /// No explicit "clear" call: it's cleared automatically once this bot's next <see cref="SendMessageAsync"/>
    /// in the same channel lands, or by a server-side safety-net timeout - see <see cref="BotTyping"/>
    /// for the other side of this broadcast.
    /// </summary>
    public Task SetTypingAsync(BotTypingState state, Guid channelId, CancellationToken ct = default) =>
        _transport.SetTypingAsync(new GrpcBotTypingRequest { ChannelId = channelId.ToString(), State = state.ToGrpc() }, ct);

    public async Task<IReadOnlyList<BotGroupInfo>> GetMyGroupsAsync(CancellationToken ct = default) =>
        (await _transport.GetMyGroupsAsync(ct)).Groups.Select(g => g.ToModel()).ToList();

    public async Task<BotGroupDetails> GetGroupAsync(Guid groupId, CancellationToken ct = default) =>
        (await _transport.GetGroupAsync(new GrpcGetGroupRequest { GroupId = groupId.ToString() }, ct)).ToModel();

    public async Task<IReadOnlyList<User>> GetGroupUsersAsync(Guid groupId, CancellationToken ct = default) =>
        (await _transport.GetGroupUsersAsync(new GrpcGetGroupUsersRequest { GroupId = groupId.ToString() }, ct)).Users.Select(u => u.ToModel()).ToList();

    public async Task<BotChannelDetails> GetChannelAsync(Guid channelId, CancellationToken ct = default) =>
        (await _transport.GetChannelAsync(new GrpcGetChannelRequest { ChannelId = channelId.ToString() }, ct)).ToModel();

    public Task RespondToInteractionAsync(Guid interactionId, InteractionResponse response, CancellationToken ct = default)
    {
        var request = new GrpcRespondToInteractionRequest
        {
            InteractionId = interactionId.ToString(),
            Kind = (GrpcInteractionResponseKind)(int)response.Kind,
            Content = response.Content ?? "",
            ComponentsJson = response.ComponentsJson ?? "",
            ModalJson = response.ModalJson ?? "",
            Ephemeral = response.Ephemeral,
        };
        if (response.AutocompleteChoices is not null)
        {
            request.AutocompleteChoices.AddRange(
                response.AutocompleteChoices.Select(c => new GrpcInteractionChoice { Name = c.Name, Value = c.Value }));
        }

        return _transport.RespondToInteractionAsync(request, ct);
    }

    public async Task<MessageInfo> SendInteractionFollowupAsync(
        Guid interactionId, string content, ComponentBuilder? components = null, CancellationToken ct = default)
    {
        var request = new GrpcSendInteractionFollowupRequest { InteractionId = interactionId.ToString(), Content = content };
        if (components is not null) request.ComponentsJson = components.Build();

        return (await _transport.SendInteractionFollowupAsync(request, ct)).ToModel();
    }

    /// <summary>
    /// Creates a category in a group this bot belongs to (requires the <c>Guilds</c> intent and
    /// <c>ManageChannels</c> in the bot's managed role for that group). Creating the group/server itself
    /// is deliberately not exposed to bots - only structure within an existing one.
    /// </summary>
    public async Task<BotCategoryInfo> CreateCategoryAsync(Guid groupId, string name, CancellationToken ct = default) =>
        (await _transport.CreateCategoryAsync(new GrpcCreateCategoryRequest { GroupId = groupId.ToString(), Name = name }, ct)).ToModel();

    /// <summary>Creates a channel under a category in a group this bot belongs to (requires <c>Guilds</c> intent + <c>ManageChannels</c>).</summary>
    public async Task<BotChannelInfo> CreateChannelAsync(Guid categoryId, string name, ChannelType type, CancellationToken ct = default) =>
        (await _transport.CreateChannelAsync(
            new GrpcCreateChannelRequest { CategoryId = categoryId.ToString(), Name = name, Type = (int)type }, ct)).ToModel();

    /// <summary>Creates a role in a group this bot belongs to (requires <c>Guilds</c> intent + <c>ManageRoles</c>).</summary>
    public async Task<BotRole> CreateRoleAsync(
        Guid groupId, string name, string? color = null, IReadOnlyCollection<long>? permissions = null, CancellationToken ct = default)
    {
        var request = new GrpcCreateRoleRequest { GroupId = groupId.ToString(), Name = name };
        if (color is not null) request.Color = color;
        if (permissions is not null) request.Permissions.AddRange(permissions);

        return (await _transport.CreateRoleAsync(request, ct)).ToModel();
    }

    /// <summary>Updates a role's name/permissions/color (requires <c>Guilds</c> intent + <c>ManageRoles</c>, and role-hierarchy above the target role).</summary>
    public async Task<BotRole> UpdateRoleAsync(Guid roleId, string name, long permissions, string? color = null, CancellationToken ct = default)
    {
        var request = new GrpcUpdateRoleRequest { RoleId = roleId.ToString(), Name = name, Permissions = permissions };
        if (color is not null) request.Color = color;

        return (await _transport.UpdateRoleAsync(request, ct)).ToModel();
    }

    /// <summary>Deletes a role (requires <c>Guilds</c> intent + <c>ManageRoles</c>). The <c>@everyone</c> role and managed (bot) roles cannot be deleted this way.</summary>
    public Task DeleteRoleAsync(Guid roleId, CancellationToken ct = default) =>
        _transport.DeleteRoleAsync(new GrpcDeleteRoleRequest { RoleId = roleId.ToString() }, ct);

    /// <summary>Grants a role to a group member (requires <c>Guilds</c> intent + <c>ManageRoles</c>, and role-hierarchy above the target role).</summary>
    public Task AssignRoleAsync(Guid groupId, Guid targetUserId, Guid roleId, CancellationToken ct = default) =>
        _transport.AssignRoleAsync(
            new GrpcRoleAssignmentRequest { GroupId = groupId.ToString(), TargetUserId = targetUserId.ToString(), RoleId = roleId.ToString() }, ct);

    /// <summary>Revokes a role from a group member (requires <c>Guilds</c> intent + <c>ManageRoles</c>, and role-hierarchy above the target role).</summary>
    public Task RemoveRoleAsync(Guid groupId, Guid targetUserId, Guid roleId, CancellationToken ct = default) =>
        _transport.RemoveRoleAsync(
            new GrpcRoleAssignmentRequest { GroupId = groupId.ToString(), TargetUserId = targetUserId.ToString(), RoleId = roleId.ToString() }, ct);

    /// <summary>
    /// Registers a new slash command for <paramref name="botId"/> - requires the bot's own
    /// <see cref="Intents.BotIntents.ApplicationCommands"/> intent. Most callers should use
    /// <see cref="Interactions.InteractionService.RegisterCommandsAsync"/> instead of calling this
    /// directly - it builds the request from a module's <c>[SlashCommand]</c>/
    /// <c>[SlashCommandOption]</c> attributes and reconciles against what's already registered.
    /// </summary>
    public async Task<BotCommandDefinition> RegisterCommandAsync(
        Guid botId, string name, string description, IReadOnlyList<CommandOptionDefinition>? options = null,
        ulong defaultMemberPermissions = 0, CancellationToken ct = default)
    {
        var request = new GrpcRegisterCommandRequest { BotId = botId.ToString(), Name = name, Description = description, DefaultMemberPermissions = defaultMemberPermissions };
        if (options is not null) request.Options.AddRange(options.Select(o => o.ToWire()));

        return (await _interactionsAdmin.RegisterCommandAsync(request, ct)).ToModel();
    }

    public async Task<BotCommandDefinition> UpdateCommandAsync(
        Guid botId, Guid commandId, string name, string description, IReadOnlyList<CommandOptionDefinition>? options = null,
        ulong defaultMemberPermissions = 0, CancellationToken ct = default)
    {
        var request = new GrpcUpdateCommandRequest
        {
            BotId = botId.ToString(), CommandId = commandId.ToString(), Name = name, Description = description, DefaultMemberPermissions = defaultMemberPermissions,
        };
        if (options is not null) request.Options.AddRange(options.Select(o => o.ToWire()));

        return (await _interactionsAdmin.UpdateCommandAsync(request, ct)).ToModel();
    }

    public Task DeleteCommandAsync(Guid botId, Guid commandId, CancellationToken ct = default) =>
        _interactionsAdmin.DeleteCommandAsync(new GrpcDeleteCommandRequest { BotId = botId.ToString(), CommandId = commandId.ToString() }, ct);

    public async Task<IReadOnlyList<BotCommandDefinition>> GetBotCommandsAsync(Guid botId, CancellationToken ct = default) =>
        (await _interactionsAdmin.GetBotCommandsAsync(new GrpcGetBotCommandsRequest { BotId = botId.ToString() }, ct)).Commands.Select(c => c.ToModel()).ToList();

    public Task StartGatewayAsync(CancellationToken ct = default) => _gateway.StartAsync(ct);

    public Task StopGatewayAsync() => _gateway.StopAsync();

    private void OnGatewayMessageReceived(string topic, string json)
    {
        // Fire-and-forget from the gateway's synchronous callback - failures surface via ConnectionError,
        // never as an unobserved exception that could take down the connection's dispatch loop.
        _ = HandleGatewayMessageAsync(topic, json);
    }

    private async Task HandleGatewayMessageAsync(string topic, string json)
    {
        try
        {
            var result = GatewayEventParser.Parse(topic, json);
            await DispatchAsync(result).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await RaiseAsync(ConnectionError, ex).ConfigureAwait(false);
        }
    }

    private Task DispatchAsync(GatewayParseResult result) => result switch
    {
        GatewayParseResult.MessageCreated e => RaiseAsync(MessageCreated, e.Message),
        GatewayParseResult.MessageUpdated e => RaiseAsync(MessageUpdated, e.Message),
        GatewayParseResult.MessageDeleted e => RaiseAsync(MessageDeleted, e.MessageId, e.Success),
        GatewayParseResult.ReactionAdded e => RaiseAsync(ReactionAdded, e.Reaction),
        GatewayParseResult.ReactionRemoved e => RaiseAsync(ReactionRemoved, e.Reaction),
        GatewayParseResult.UserTyping e => RaiseAsync(UserTyping, e.Typing),
        GatewayParseResult.BotTyping e => RaiseAsync(BotTyping, e.Typing),
        GatewayParseResult.UserConnected e => RaiseAsync(UserConnected, e.Presence),
        GatewayParseResult.UserDisconnected e => RaiseAsync(UserDisconnected, e.Presence),
        GatewayParseResult.UserJoinedGroup e => RaiseAsync(UserJoinedGroup, e.Joined),
        GatewayParseResult.UserStatusChanged e => RaiseAsync(UserStatusChanged, e.StatusChanged),
        GatewayParseResult.InteractionCreated e => RaiseAsync(InteractionCreated, e.Interaction),
        GatewayParseResult.Unknown e => RaiseAsync(UnhandledEvent, e.Event),
        GatewayParseResult.ParseError e => RaiseAsync(ConnectionError, e.Exception),
        _ => Task.CompletedTask,
    };

    private void OnGatewayError(Exception ex) => _ = RaiseAsync(ConnectionError, ex);

    private static async Task RaiseAsync<T>(Func<T, Task>? handler, T arg)
    {
        if (handler is null) return;
        foreach (var d in handler.GetInvocationList())
        {
            await ((Func<T, Task>)d)(arg).ConfigureAwait(false);
        }
    }

    private static async Task RaiseAsync(Func<Guid, bool, Task>? handler, Guid messageId, bool success)
    {
        if (handler is null) return;
        foreach (var d in handler.GetInvocationList())
        {
            await ((Func<Guid, bool, Task>)d)(messageId, success).ConfigureAwait(false);
        }
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
