using System.Text.Json;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Voice.Bot.Client.Net.Gateway;
using Voice.Bot.Client.Net.Internal;
using Voice.Bot.Client.Net.Models;
using WireMessage = Vc.Backend.WebApi.Messages.Grpc.Message;
using WireUser = Vc.Backend.Contracts.Grpc.User;

namespace Voice.Bot.Client.Net.Testing;

/// <summary>
/// An in-memory Voice server for unit tests of a bot: a <see cref="BotClient"/> wired to it without any
/// network, a small world (a group with an owner, a member and a <c>#general</c> channel), ways to
/// simulate what users do, and a record of everything the bot did.
/// </summary>
/// <remarks>
/// The simulation methods deliver their event at once and return a task that completes when the bot's
/// handlers for it have finished, including the events those handlers caused; for handlers in
/// <see cref="RunMode.Async"/> use <see cref="WaitForSentMessageAsync"/>. While a handler waits for an answer
/// (<see cref="BotContext.AskAsync"/>), start the answer without awaiting the earlier calls first:
/// <code>
/// var command = server.SendMessageAsync("!name");
/// await server.WaitForSentMessageAsync(m =&gt; m.Content == "What is your name?");
/// await server.SendMessageAsync("Bob");
/// await command;
/// </code>
/// A handler waiting for an answer counts as busy, so until the answer comes every simulation method
/// waits for it too; to let other users act meanwhile, start their calls without awaiting them either.
/// <para>
/// Like the real server, it answers a call first and sends the gateway events the call caused after
/// that: the bot's own messages come back as <c>MessageCreated</c>, creating, assigning and removing roles
/// sends <c>RoleCreated</c>/<c>RoleAssigned</c>/<c>RoleRemoved</c>, and kicking or banning a member sends
/// <c>UserKicked</c>. The bot starts connected; events sent while it is disconnected
/// (<see cref="DisconnectAsync"/>) are lost, unless <see cref="QueueWhileDisconnected"/> keeps them for the
/// next <see cref="ConnectAsync"/>.
/// </para>
/// <para>
/// Differences from the real server: retries are off (<see cref="RetryOptions.None"/>, so a
/// <see cref="FailNext"/> failure reaches the bot; set <c>o.Retry</c> in the constructor's
/// <c>configure</c> to test retries), every call must carry a bot token, but the User-Agent isn't checked
/// since there is no HTTP layer.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// await using var server = new FakeVoiceServer();
/// var commands = new CommandService(server.Client);
/// commands.AddModule&lt;PingModule&gt;();
/// commands.Initialize();
///
/// await server.SendMessageAsync("!ping");
///
/// Assert.Equal("pong", server.LastSentMessage?.Content);
/// </code>
/// </example>
public sealed partial class FakeVoiceServer : IAsyncDisposable
{
    private readonly object _lock = new();
    private readonly Dictionary<Guid, FakeUser> _users = [];
    private readonly Dictionary<Guid, FakeGroup> _groups = [];
    private readonly Dictionary<Guid, WireMessage> _messages = [];
    private readonly List<Guid> _messageOrder = [];
    private readonly Dictionary<string, Queue<PlannedFailure>> _failures = [];
    private readonly List<string> _calls = [];
    private readonly List<Message> _sent = [];
    private readonly List<SentReaction> _reactions = [];
    private readonly List<SentReaction> _removedReactions = [];
    private readonly List<Guid> _deleted = [];
    private readonly List<EditedMessage> _edited = [];
    private readonly List<TypingSignal> _typing = [];
    private readonly List<UploadedFile> _uploads = [];
    private readonly List<ModerationAction> _moderation = [];
    private readonly List<(string Topic, string Json)> _queued = [];
    private readonly object _laterLock = new();
    private Task _later = Task.CompletedTask;
    private bool _connected = true;

    /// <summary>Creates the server, its world and a client connected to it.</summary>
    /// <param name="configure">Changes the client's options (prefixes, replies, retries ...); the token is set already.</param>
    /// <param name="loggerFactory">Receives the client's logs; <c>null</c> for none.</param>
    public FakeVoiceServer(Action<BotClientOptions>? configure = null, ILoggerFactory? loggerFactory = null)
    {
        BotId = AddUserCore("test-bot", isBot: true);
        OwnerId = AddUserCore("owner", isBot: false);
        UserId = AddUserCore("user", isBot: false);
        GroupId = AddGroup("Test group", OwnerId);
        ChannelId = _groups[GroupId].Channels[0].Id;
        AddMember(UserId, GroupId);

        var options = new BotClientOptions { BotToken = "ibot_fake", Retry = RetryOptions.None };
        configure?.Invoke(options);
        var invoker = new FakeCallInvoker(TakeFailure, Later);
        RegisterHandlers(invoker);
        var api = new VoiceApi(invoker, options.BotToken, options.RequestTimeout, retry: options.Retry, throttle: options.Throttle);
        var uploader = new Uploader(new HttpClient(new FakeStorage(StoreUpload)), options.UploadTimeout);
        Client = new BotClient(api, new NullGateway(), uploader, options, loggerFactory);
    }

    /// <summary>The client to build the bot on.</summary>
    public BotClient Client { get; }

    /// <summary>The bot's own user id.</summary>
    public Guid BotId { get; }

    /// <summary>The owner of the default group.</summary>
    public Guid OwnerId { get; }

    /// <summary>A plain member of the default group; the default author of simulated messages.</summary>
    public Guid UserId { get; }

    /// <summary>The default group.</summary>
    public Guid GroupId { get; }

    /// <summary>The default group's <c>#general</c> text channel; the default place of simulated messages.</summary>
    public Guid ChannelId { get; }

    /// <summary>What members may do without any role; the group owner always has <see cref="Permissions.Administrator"/>.</summary>
    public Permissions DefaultPermissions { get; set; } =
        Permissions.ViewChannels | Permissions.SendMessages | Permissions.ReadMessageHistory | Permissions.AddReactions |
        Permissions.AttachFiles | Permissions.UseApplicationCommands | Permissions.CreateInvite | Permissions.Connect | Permissions.Speak;

    /// <summary>Whether messages the bot sends are delivered back to it through the gateway, like on the real server. Default <c>true</c>.</summary>
    public bool EchoBotMessages { get; set; } = true;

    /// <summary>Whether the bot is connected to the gateway; events sent while it isn't are lost (see <see cref="QueueWhileDisconnected"/>).</summary>
    public bool IsConnected
    {
        get
        {
            lock (_lock) return _connected;
        }
    }

    /// <summary>
    /// Keeps the events sent while the bot is disconnected and delivers them after the next
    /// <see cref="ConnectAsync"/>. Off by default: the real server doesn't replay missed events.
    /// </summary>
    public bool QueueWhileDisconnected { get; set; }

    /// <summary>Messages the bot sent, oldest first, as they were when sent.</summary>
    public IReadOnlyList<Message> SentMessages => Snapshot(_sent);

    /// <summary>The newest message the bot sent, <c>null</c> if none.</summary>
    public Message? LastSentMessage
    {
        get
        {
            lock (_lock) return _sent.Count == 0 ? null : _sent[^1];
        }
    }

    /// <summary>Reactions the bot added.</summary>
    public IReadOnlyList<SentReaction> Reactions => Snapshot(_reactions);

    /// <summary>Reactions the bot removed.</summary>
    public IReadOnlyList<SentReaction> RemovedReactions => Snapshot(_removedReactions);

    /// <summary>Messages the bot deleted.</summary>
    public IReadOnlyList<Guid> DeletedMessages => Snapshot(_deleted);

    /// <summary>Messages the bot edited.</summary>
    public IReadOnlyList<EditedMessage> EditedMessages => Snapshot(_edited);

    /// <summary>Every change of the bot's typing indicator.</summary>
    public IReadOnlyList<TypingSignal> Typing => Snapshot(_typing);

    /// <summary>Files the bot uploaded.</summary>
    public IReadOnlyList<UploadedFile> Uploads => Snapshot(_uploads);

    /// <summary>Kicks, bans and unbans.</summary>
    public IReadOnlyList<ModerationAction> Moderation => Snapshot(_moderation);

    /// <summary>The names of every API method the bot called, in order - e.g. to check what the cache saved.</summary>
    public IReadOnlyList<string> Calls => Snapshot(_calls);

    /// <summary>Adds a user; with <paramref name="groupId"/>, also as a member of that group.</summary>
    public Guid AddUser(string username, bool isBot = false, Guid? groupId = null)
    {
        lock (_lock)
        {
            var id = AddUserCore(username, isBot);
            if (groupId is { } group) AddMember(id, group);
            return id;
        }
    }

    /// <summary>Adds a group with a <c>#general</c> text channel; the bot is a member.</summary>
    /// <param name="ownerId"><c>null</c> for <see cref="OwnerId"/>.</param>
    public Guid AddGroup(string name, Guid? ownerId = null)
    {
        lock (_lock)
        {
            var group = new FakeGroup(Guid.NewGuid(), name, ownerId ?? OwnerId);
            _groups[group.Id] = group;
            group.Channels.Add(new FakeChannel(Guid.NewGuid(), "general", ChannelType.Text));
            group.Members[group.OwnerId] = [];
            group.Members[BotId] = [];
            return group.Id;
        }
    }

    /// <summary>Adds a channel to a group (the default group when <paramref name="groupId"/> is <c>null</c>).</summary>
    public Guid AddChannel(string name, ChannelType type = ChannelType.Text, Guid? groupId = null)
    {
        lock (_lock)
        {
            var channel = new FakeChannel(Guid.NewGuid(), name, type);
            Group(groupId).Channels.Add(channel);
            return channel.Id;
        }
    }

    /// <summary>Adds a role to a group.</summary>
    public Guid AddRole(string name, Permissions permissions = Permissions.None, Guid? groupId = null)
    {
        lock (_lock)
        {
            var role = new FakeRole(Guid.NewGuid(), name, permissions);
            Group(groupId).Roles.Add(role);
            return role.Id;
        }
    }

    /// <summary>Makes a user a member of a group.</summary>
    public void AddMember(Guid userId, Guid? groupId = null)
    {
        lock (_lock) Group(groupId).Members.TryAdd(userId, []);
    }

    /// <summary>Gives a member a role.</summary>
    public void GiveRole(Guid userId, Guid roleId, Guid? groupId = null)
    {
        lock (_lock)
        {
            var group = Group(groupId);
            group.Members.TryAdd(userId, []);
            if (!group.Members[userId].Contains(roleId)) group.Members[userId].Add(roleId);
        }
    }

    /// <summary>Sets a user's effective permissions in a group, overriding owner and roles.</summary>
    public void SetPermissions(Guid userId, Permissions permissions, Guid? groupId = null)
    {
        lock (_lock) Group(groupId).Permissions[userId] = permissions;
    }

    /// <summary>
    /// Makes the next <paramref name="times"/> calls of an API method (e.g. <c>"CreateMessage"</c>) fail
    /// with <paramref name="status"/> - to test error handling and retries.
    /// </summary>
    /// <param name="afterApplying">
    /// The call does its work (and sends its events) before failing, as when the connection drops before
    /// the answer arrives - to test that a retried call doesn't do things twice.
    /// </param>
    public void FailNext(string method, StatusCode status, string? message = null, int times = 1, bool afterApplying = false)
    {
        lock (_lock)
        {
            if (!_failures.TryGetValue(method, out var queue)) _failures[method] = queue = new Queue<PlannedFailure>();
            for (var i = 0; i < times; i++) queue.Enqueue(new PlannedFailure(new RpcException(new Status(status, message ?? status.ToString())), afterApplying));
        }
    }

    /// <summary>
    /// Simulates the gateway (re)connecting, which raises <see cref="BotClient.Ready"/>, then delivers what
    /// <see cref="QueueWhileDisconnected"/> kept.
    /// </summary>
    public async Task ConnectAsync()
    {
        List<(string Topic, string Json)> queued;
        lock (_lock)
        {
            _connected = true;
            queued = [.. _queued];
            _queued.Clear();
        }

        Client.InjectConnected();
        foreach (var (topic, json) in queued) Client.Inject(topic, json);
        await IdleAsync().ConfigureAwait(false);
    }

    /// <summary>Simulates losing the gateway connection: events sent until the next <see cref="ConnectAsync"/> don't reach the bot.</summary>
    public Task DisconnectAsync()
    {
        lock (_lock) _connected = false;
        return IdleAsync();
    }

    /// <summary>A user writes a message; returns once the bot's handlers are done.</summary>
    /// <param name="userId"><c>null</c> for <see cref="UserId"/>.</param>
    /// <param name="channelId"><c>null</c> for <see cref="ChannelId"/>.</param>
    public async Task<Message> SendMessageAsync(string content, Guid? userId = null, Guid? channelId = null, Guid? replyTo = null)
    {
        WireMessage wire;
        Guid? groupId;
        lock (_lock)
        {
            wire = NewMessage(userId ?? UserId, channelId ?? ChannelId, content, replyTo);
            groupId = GroupOf(Guid.Parse(wire.ChannelId));
        }

        Deliver(GatewayTopics.ChatEvents, "MessageCreated", groupId, MessagePayload(wire));
        await IdleAsync().ConfigureAwait(false);
        return wire.ToModel();
    }

    /// <summary>A user runs one of the bot's slash commands; returns once the bot's handlers are done.</summary>
    /// <param name="options">Option values by name, as the user typed them.</param>
    public Task InvokeSlashCommandAsync(string name, IReadOnlyDictionary<string, string>? options = null, Guid? userId = null, Guid? channelId = null)
    {
        var channel = channelId ?? ChannelId;
        Guid? groupId;
        lock (_lock) groupId = GroupOf(channel);
        Deliver(GatewayTopics.InteractionEvents, "InteractionCreated", groupId, new
        {
            InteractionId = Guid.NewGuid(), Type = 0, InvokingUserId = userId ?? UserId, ChannelId = channel, GroupId = groupId,
            Data = new { CommandName = name, Options = options ?? new Dictionary<string, string>() },
        });
        return IdleAsync();
    }

    /// <summary>A user reacts to a message; returns once the bot's handlers are done.</summary>
    public Task AddReactionAsync(Guid messageId, string emoji, Guid? userId = null) => ReactAsync(messageId, emoji, userId ?? UserId, added: true);

    /// <summary>A user takes a reaction back; returns once the bot's handlers are done.</summary>
    public Task RemoveReactionAsync(Guid messageId, string emoji, Guid? userId = null) => ReactAsync(messageId, emoji, userId ?? UserId, added: false);

    /// <summary>A user joins a group; returns once the bot's handlers are done.</summary>
    public Task MemberJoinAsync(Guid userId, Guid? groupId = null)
    {
        var group = groupId ?? GroupId;
        lock (_lock) Group(group).Members.TryAdd(userId, []);
        Deliver(GatewayTopics.GroupEvents, "UserJoined", group, new { UserId = userId });
        return IdleAsync();
    }

    /// <summary>A user leaves a group; returns once the bot's handlers are done.</summary>
    public Task MemberLeaveAsync(Guid userId, Guid? groupId = null)
    {
        var group = groupId ?? GroupId;
        lock (_lock) Group(group).Members.Remove(userId);
        Deliver(GatewayTopics.GroupEvents, "UserLeft", group, new { UserId = userId });
        return IdleAsync();
    }

    /// <summary>
    /// Sends any gateway event, e.g. <c>SendEventAsync("group-events", "ChannelCreated", groupId, new { })</c>;
    /// returns once the bot's handlers are done.
    /// </summary>
    /// <param name="topic"><c>chat-events</c>, <c>group-events</c>, <c>channel-events</c> or <c>interaction-events</c>.</param>
    public Task SendEventAsync(string topic, string type, Guid? groupId, object payload)
    {
        Deliver(topic, type, groupId, payload);
        return IdleAsync();
    }

    /// <summary>Waits until every event so far has been handled, including the events the bot's calls caused.</summary>
    public Task WhenIdleAsync() => IdleAsync();

    /// <summary>
    /// The first message the bot sent that matches <paramref name="predicate"/> - already sent or sent
    /// within <paramref name="timeout"/> (default 5 s). For handlers that run in the background.
    /// </summary>
    /// <exception cref="TimeoutException">No such message in time.</exception>
    public async Task<Message> WaitForSentMessageAsync(Func<Message, bool>? predicate = null, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (true)
        {
            lock (_lock)
            {
                if (_sent.FirstOrDefault(m => predicate?.Invoke(m) ?? true) is { } found) return found;
            }

            if (DateTime.UtcNow > deadline) throw new TimeoutException("The bot sent no matching message in time.");
            await Task.Delay(10).ConfigureAwait(false);
        }
    }

    /// <summary>Disposes the client.</summary>
    public ValueTask DisposeAsync() => Client.DisposeAsync();

    private async Task ReactAsync(Guid messageId, string emoji, Guid userId, bool added)
    {
        WireMessage? message;
        Guid? groupId;
        lock (_lock)
        {
            _messages.TryGetValue(messageId, out message);
            groupId = message is null ? GroupId : GroupOf(Guid.Parse(message.ChannelId));
            if (message is not null) Toggle(message, emoji, userId, added);
        }

        Deliver(GatewayTopics.ChatEvents, added ? "MessageReactionAdded" : "MessageReactionRemoved", groupId, new
        {
            ChannelId = message?.ChannelId ?? ChannelId.ToString(), MessageId = messageId, UserId = userId, Emoji = emoji,
        });
        await IdleAsync().ConfigureAwait(false);
    }

    /// <summary>Sends a gateway event if the bot is connected; keeps or drops it otherwise.</summary>
    private void Deliver(string topic, string type, Guid? groupId, object payload)
    {
        var json = JsonSerializer.Serialize(new { Type = type, GroupId = groupId, PayloadJson = JsonSerializer.Serialize(payload) });
        lock (_lock)
        {
            if (!_connected)
            {
                if (QueueWhileDisconnected) _queued.Add((topic, json));
                return;
            }
        }

        Client.Inject(topic, json);
    }

    /// <summary>Runs <paramref name="work"/> after everything queued before it - the answers and events of earlier calls.</summary>
    private void Later(Action work)
    {
        lock (_laterLock) _later = RunLaterAsync(_later, work);
    }

    private async Task RunLaterAsync(Task previous, Action work)
    {
        await previous.ConfigureAwait(false);

        // A moment for the caller to start awaiting the answer, so it sees the answer before the events.
        await Task.Delay(1).ConfigureAwait(false);
        try
        {
            work();
        }
        catch (Exception ex)
        {
            await Client.ReportAsync(ex).ConfigureAwait(false);
        }
    }

    /// <summary>Waits until the answers and events of the bot's calls are out and the bot has handled every event.</summary>
    private async Task IdleAsync()
    {
        while (true)
        {
            Task later;
            lock (_laterLock) later = _later;
            await later.ConfigureAwait(false);
            await Client.WhenIdleAsync().ConfigureAwait(false);
            lock (_laterLock)
            {
                if (_later == later) return;
            }
        }
    }

    private static object MessagePayload(WireMessage wire) => new
    {
        wire.Id,
        wire.ChannelId,
        wire.Content,
        wire.ReplyTo,
        CreatedAt = wire.CreatedAt?.ToDateTimeOffset(),
        Author = new { wire.Author.Id, wire.Author.Username, wire.Author.DisplayName, wire.Author.IsBot },
        Attachments = wire.Attachments.Select(a => new { a.Id, a.FileName, a.ContentType, a.SizeBytes, a.Status, a.DownloadUrl }),
        Reactions = wire.Reactions.ToDictionary(r => r.Key, r => r.Value.UserIds.ToList()),
    };

    private Guid AddUserCore(string username, bool isBot)
    {
        var id = Guid.NewGuid();
        _users[id] = new FakeUser(id, username, isBot);
        return id;
    }

    private FakeGroup Group(Guid? groupId) =>
        _groups.TryGetValue(groupId ?? GroupId, out var group) ? group : throw new ArgumentException($"No group {groupId}.", nameof(groupId));

    private Guid? GroupOf(Guid channelId) => _groups.Values.FirstOrDefault(g => g.Channels.Any(c => c.Id == channelId))?.Id;

    private WireUser WireUserOf(Guid id) =>
        _users.TryGetValue(id, out var user)
            ? new WireUser { Id = id.ToString(), Username = user.Username, IsBot = user.IsBot }
            : new WireUser { Id = id.ToString(), Username = "unknown" };

    private WireMessage NewMessage(Guid authorId, Guid channelId, string content, Guid? replyTo)
    {
        var wire = new WireMessage
        {
            Id = Guid.NewGuid().ToString(),
            ChannelId = channelId.ToString(),
            Author = WireUserOf(authorId),
            Content = content,
            CreatedAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
            ReplyTo = replyTo?.ToString() ?? "",
        };
        _messages[Guid.Parse(wire.Id)] = wire;
        _messageOrder.Add(Guid.Parse(wire.Id));
        return wire;
    }

    private static void Toggle(WireMessage message, string emoji, Guid userId, bool added)
    {
        if (!message.Reactions.TryGetValue(emoji, out var users))
        {
            users = new Vc.Backend.WebApi.Messages.Grpc.ReactionUsers();
            message.Reactions[emoji] = users;
        }

        users.UserIds.Remove(userId.ToString());
        if (added) users.UserIds.Add(userId.ToString());
        if (users.UserIds.Count == 0) message.Reactions.Remove(emoji);
    }

    private PlannedFailure? TakeFailure(string method)
    {
        lock (_lock)
        {
            _calls.Add(method);
            return _failures.TryGetValue(method, out var queue) && queue.Count > 0 ? queue.Dequeue() : null;
        }
    }

    private IReadOnlyList<T> Snapshot<T>(List<T> list)
    {
        lock (_lock) return [.. list];
    }

    private sealed record FakeUser(Guid Id, string Username, bool IsBot);

    private sealed record FakeChannel(Guid Id, string Name, ChannelType Type);

    private sealed record FakeRole(Guid Id, string Name, Permissions Permissions);

    private sealed record FakeBan(Guid UserId, string Reason, Guid BannedBy);

    private sealed class FakeGroup(Guid id, string name, Guid ownerId)
    {
        public Guid Id { get; } = id;
        public string Name { get; } = name;
        public Guid OwnerId { get; } = ownerId;
        public Guid CategoryId { get; } = Guid.NewGuid();
        public List<FakeChannel> Channels { get; } = [];
        public List<FakeRole> Roles { get; } = [];
        public Dictionary<Guid, List<Guid>> Members { get; } = [];
        public Dictionary<Guid, Permissions> Permissions { get; } = [];
        public List<FakeBan> Bans { get; } = [];
    }
}
