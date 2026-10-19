using Voice.Bot.Client.Net.Internal;
using Voice.Bot.Client.Net.Models;
using Vc.Backend.WebApi.Messages.Grpc;
using Message = Voice.Bot.Client.Net.Models.Message;
using WireSearchHas = Vc.Backend.WebApi.Messages.Grpc.SearchMessagesHas;
using WireSearchSort = Vc.Backend.WebApi.Messages.Grpc.SearchMessagesSort;

namespace Voice.Bot.Client.Net;

/// <summary>Sending, editing, searching and forwarding messages, reactions and the typing indicator.</summary>
public sealed partial class BotClient
{
    /// <summary>
    /// Sends a message to a channel. Mentions are written right in the text: <c>&lt;@user_id&gt;</c>,
    /// <c>&lt;#channel_id&gt;</c>, <c>&lt;@&amp;role_id&gt;</c>, <c>@everyone</c>, <c>@here</c>.
    /// </summary>
    /// <param name="attachmentIds">Files uploaded with <see cref="UploadAsync(string, ReadOnlyMemory{byte}, string, CancellationToken)"/> or <see cref="UploadFileAsync"/>.</param>
    public async Task<Message> SendMessageAsync(
        Guid channelId, string content, Guid? replyTo = null, IReadOnlyCollection<Guid>? attachmentIds = null, CancellationToken ct = default)
    {
        var request = new CreateMessageRequest { ChannelId = channelId.ToString(), Content = content };
        if (replyTo is { } reply) request.ReplyTo = reply.ToString();
        if (attachmentIds is not null) request.AttachmentIds.AddRange(attachmentIds.Select(id => id.ToString()));
        return (await _api.CallAsync(o => _api.Messages.CreateMessageAsync(request, o), ct, Idempotency.Unsafe).ConfigureAwait(false)).ToModel();
    }

    /// <summary>Changes the text of the bot's own message.</summary>
    public async Task<Message> UpdateMessageAsync(Guid messageId, string content, CancellationToken ct = default) =>
        (await _api.CallAsync(o => _api.Messages.UpdateMessageAsync(new UpdateMessageRequest { Id = messageId.ToString(), Content = content }, o), ct).ConfigureAwait(false)).ToModel();

    public async Task DeleteMessageAsync(Guid messageId, CancellationToken ct = default) =>
        await _api.DeleteAsync(o => _api.Messages.DeleteMessageAsync(new DeleteMessageRequest { Id = messageId.ToString() }, o), ct).ConfigureAwait(false);

    /// <summary>A single message; throws <see cref="BotApiException"/> with <c>NotFound</c> if it doesn't exist.</summary>
    public async Task<Message> GetMessageAsync(Guid messageId, CancellationToken ct = default) =>
        (await _api.CallAsync(o => _api.Messages.GetMessageAsync(new GetMessageRequest { MessageId = messageId.ToString() }, o), ct).ConfigureAwait(false)).ToModel();

    /// <summary>
    /// Recent messages in a channel, oldest to newest. To page backwards, pass the id of the oldest
    /// message received so far as <paramref name="beforeId"/>.
    /// </summary>
    public async Task<IReadOnlyList<Message>> GetChannelMessagesAsync(
        Guid channelId, int limit = 50, Guid? beforeId = null, CancellationToken ct = default)
    {
        var request = new GetChannelMessagesRequest { ChannelId = channelId.ToString(), Limit = limit, BeforeId = beforeId?.ToString() ?? "" };
        return (await _api.CallAsync(o => _api.Messages.GetChannelMessagesAsync(request, o), ct).ConfigureAwait(false)).Messages.Select(m => m.ToModel()).ToList();
    }

    /// <summary>A message together with up to <paramref name="before"/> older and <paramref name="after"/> newer ones, oldest first.</summary>
    public async Task<MessagesAround> GetMessagesAroundAsync(
        Guid channelId, Guid messageId, int before = 25, int after = 25, CancellationToken ct = default)
    {
        var request = new GetMessagesAroundRequest { ChannelId = channelId.ToString(), MessageId = messageId.ToString(), Before = before, After = after };
        var response = await _api.CallAsync(o => _api.Messages.GetMessagesAroundAsync(request, o), ct).ConfigureAwait(false);
        return new MessagesAround(response.Messages.Select(m => m.ToModel()).ToList(), response.TargetFound);
    }

    /// <summary>Searches messages in a group.</summary>
    public async Task<SearchResults> SearchMessagesAsync(Guid groupId, MessageSearch search, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(search.Sort)) throw new ArgumentException($"Unknown sort {search.Sort}.", nameof(search));
        var request = new SearchMessagesRequest
        {
            GroupId = groupId.ToString(),
            Query = search.Query,
            Before = search.Before ?? "",
            After = search.After ?? "",
            Sort = (WireSearchSort)(int)search.Sort,
            PageSize = search.PageSize,
            PageToken = search.PageToken ?? "",
        };
        request.ChannelIds.AddRange((search.ChannelIds ?? []).Select(id => id.ToString()));
        request.AuthorIds.AddRange((search.AuthorIds ?? []).Select(id => id.ToString()));
        foreach (var filter in (search.Has ?? []).Distinct())
        {
            if (!Enum.IsDefined(filter)) throw new ArgumentException($"Unknown filter {filter}.", nameof(search));
            request.Has.Add((WireSearchHas)(int)filter);
        }

        return (await _api.CallAsync(o => _api.Messages.SearchMessagesAsync(request, o), ct).ConfigureAwait(false)).ToModel();
    }

    /// <summary>Forwards a message into channels; one result per channel, in the same order.</summary>
    /// <param name="idempotencyKey">Protects against double forwarding when retrying; <c>null</c> generates a new one.</param>
    public async Task<IReadOnlyList<ForwardResult>> ForwardMessageAsync(
        Guid messageId, IReadOnlyCollection<Guid> channelIds, string? idempotencyKey = null, CancellationToken ct = default)
    {
        var request = new ForwardMessageRequest { SourceMessageId = messageId.ToString(), IdempotencyKey = idempotencyKey ?? Guid.NewGuid().ToString() };
        request.DestinationChannelIds.AddRange(channelIds.Select(id => id.ToString()));
        return (await _api.CallAsync(o => _api.Messages.ForwardMessageAsync(request, o), ct).ConfigureAwait(false)).Results.Select(r => r.ToModel()).ToList();
    }

    public async Task<Message> AddReactionAsync(Guid messageId, string emoji, CancellationToken ct = default) =>
        (await _api.CallAsync(o => _api.Messages.AddReactionAsync(new ReactionRequest { MessageId = messageId.ToString(), Emoji = emoji }, o), ct).ConfigureAwait(false)).ToModel();

    public async Task<Message> RemoveReactionAsync(Guid messageId, string emoji, CancellationToken ct = default) =>
        (await _api.CallAsync(o => _api.Messages.RemoveReactionAsync(new ReactionRequest { MessageId = messageId.ToString(), Emoji = emoji }, o), ct).ConfigureAwait(false)).ToModel();

    /// <summary>Shows (<paramref name="isTyping"/> true) or hides the "typing" indicator in a channel.</summary>
    public async Task TypingAsync(Guid channelId, bool isTyping = true, CancellationToken ct = default) =>
        await _api.CallAsync(o => _api.Messages.TypingAsync(new TypingRequest { ChannelId = channelId.ToString(), IsTyping = isTyping }, o), ct).ConfigureAwait(false);
}
