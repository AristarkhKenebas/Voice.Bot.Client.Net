using Voice.Bot.Client.Net.Components;
using Voice.Bot.Client.Net.Models;
using GrpcSendMessageRequest = Vc.Backend.WebApi.Bots.Apis.Grpc.SendMessageRequest;
using GrpcUpdateMessageRequest = Vc.Backend.WebApi.Bots.Apis.Grpc.UpdateMessageRequest;
using GrpcDeleteMessageRequest = Vc.Backend.WebApi.Bots.Apis.Grpc.DeleteMessageRequest;
using GrpcReactionRequest = Vc.Backend.WebApi.Bots.Apis.Grpc.ReactionRequest;
using GrpcGetChannelMessagesRequest = Vc.Backend.WebApi.Bots.Apis.Grpc.GetChannelMessagesRequest;
using GrpcGetMessageRequest = Vc.Backend.WebApi.Bots.Apis.Grpc.GetMessageRequest;
using GrpcTypingRequest = Vc.Backend.WebApi.Bots.Apis.Grpc.TypingRequest;
using GrpcBotTypingRequest = Vc.Backend.WebApi.Bots.Apis.Grpc.BotTypingRequest;

namespace Voice.Bot.Client.Net;

/// <summary>Sending/editing/deleting messages, reactions and typing indicators - requires the <c>GuildMessages</c> intent.</summary>
public sealed partial class BotClient
{
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

    /// <summary>
    /// Recent messages in a channel, oldest to newest - requires the <c>GuildMessages</c> intent. Gives the
    /// bot context (channel history) rather than only reacting to new events from the Gateway. To page
    /// backwards, pass the id of the oldest message received so far as <paramref name="beforeId"/>.
    /// </summary>
    public async Task<IReadOnlyList<MessageInfo>> GetChannelMessagesAsync(
        Guid channelId, int limit = 50, Guid? beforeId = null, CancellationToken ct = default)
    {
        var request = new GrpcGetChannelMessagesRequest { ChannelId = channelId.ToString(), Limit = limit };
        if (beforeId.HasValue) request.BeforeId = beforeId.Value.ToString();

        var response = await _transport.GetChannelMessagesAsync(request, ct);
        return response.Messages.Select(m => m.ToModel()).ToList();
    }

    /// <summary>Details of a single message by id - useful when only the message id is known (e.g. from an interaction).</summary>
    public async Task<MessageInfo> GetMessageAsync(Guid messageId, CancellationToken ct = default) =>
        (await _transport.GetMessageAsync(new GrpcGetMessageRequest { MessageId = messageId.ToString() }, ct)).ToModel();

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
}
