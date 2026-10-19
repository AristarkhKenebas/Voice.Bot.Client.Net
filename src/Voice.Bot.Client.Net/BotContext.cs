using Voice.Bot.Client.Net.Commands;
using Voice.Bot.Client.Net.Events;
using Voice.Bot.Client.Net.Interactions;
using Voice.Bot.Client.Net.Models;

namespace Voice.Bot.Client.Net;

/// <summary>
/// What every handler gets - a text command (<see cref="CommandContext"/>), a slash command
/// (<see cref="InteractionContext"/>) or a message event (<see cref="BotClient.OnMessage"/>): who, where,
/// which command, and the actions to answer.
/// </summary>
/// <example>
/// <code>
/// await using (Context.BeginTyping())
/// {
///     var name = await Context.AskAsync("What is your name?");
///     if (name is null) { await Context.ReplyAsync("Too slow!"); return; }
///     if (await Context.ConfirmAsync($"Save {name.Content}?")) await Context.ReplyAsync("Saved.");
/// }
/// </code>
/// </example>
public abstract class BotContext
{
    /// <summary>The ✅ reaction <see cref="ConfirmAsync"/> counts as "yes".</summary>
    public const string Yes = "✅";

    /// <summary>The ❌ reaction <see cref="ConfirmAsync"/> counts as "no".</summary>
    public const string No = "❌";

    private protected BotContext(BotClient client, Guid userId, Guid channelId, Guid? groupId, string? commandName)
    {
        Client = client;
        UserId = userId;
        ChannelId = channelId;
        GroupId = groupId;
        CommandName = commandName;
    }

    /// <summary>The client, for anything the context has no shortcut for.</summary>
    public BotClient Client { get; }

    /// <summary>Who wrote the message or used the command.</summary>
    public Guid UserId { get; }

    /// <summary>Where it happened.</summary>
    public Guid ChannelId { get; }

    /// <summary>The group, <c>null</c> in direct messages.</summary>
    public Guid? GroupId { get; }

    /// <summary>Whether this happened in direct messages rather than in a group.</summary>
    public bool IsDirect => GroupId is null;

    /// <summary>The command's name as declared (not the alias that was typed); <c>null</c> for a plain message.</summary>
    public string? CommandName { get; }

    /// <summary>Whether this is a slash command (<see cref="InteractionContext"/>).</summary>
    public abstract bool IsSlash { get; }

    /// <summary>The message that triggered the handler, <c>null</c> for a slash command.</summary>
    public abstract Guid? MessageId { get; }

    /// <summary>The last message the bot sent through this context; <see cref="EditAsync"/> changes it.</summary>
    public Message? LastSent { get; private set; }

    /// <summary>
    /// Answers in the same channel: as a reply to the message for text commands, as a plain message for
    /// slash commands (the API can't answer the interaction itself yet).
    /// </summary>
    public Task<Message> ReplyAsync(string content, CancellationToken ct = default) => ReplyAsync(content, [], ct);

    /// <summary>Answers like <see cref="ReplyAsync(string, CancellationToken)"/>, uploading <paramref name="files"/> first.</summary>
    public async Task<Message> ReplyAsync(string content, IEnumerable<FileUpload> files, CancellationToken ct = default) =>
        LastSent = await PostReplyAsync(content, await UploadAsync(files, ct).ConfigureAwait(false), ct).ConfigureAwait(false);

    /// <summary>Posts to the same channel without replying to anything.</summary>
    public Task<Message> SendAsync(string content, CancellationToken ct = default) => SendAsync(content, [], ct);

    /// <summary>Posts to the same channel, uploading <paramref name="files"/> first.</summary>
    public async Task<Message> SendAsync(string content, IEnumerable<FileUpload> files, CancellationToken ct = default) =>
        LastSent = await Client.SendMessageAsync(ChannelId, content, attachmentIds: await UploadAsync(files, ct).ConfigureAwait(false), ct: ct).ConfigureAwait(false);

    /// <summary>Reacts to the triggering message.</summary>
    /// <exception cref="InvalidOperationException">A slash command has no message.</exception>
    public Task ReactAsync(string emoji, CancellationToken ct = default) =>
        Client.AddReactionAsync(RequireMessageId(), emoji, ct);

    /// <summary>Deletes the triggering message (someone else's needs <see cref="Permissions.ManageMessages"/>).</summary>
    /// <exception cref="InvalidOperationException">A slash command has no message.</exception>
    public Task DeleteAsync(CancellationToken ct = default) => Client.DeleteMessageAsync(RequireMessageId(), ct);

    /// <summary>Changes the text of <see cref="LastSent"/>.</summary>
    /// <example>
    /// <code>
    /// await Context.ReplyAsync("Counting...");
    /// await Context.EditAsync($"Done: {total}");
    /// </code>
    /// </example>
    /// <exception cref="InvalidOperationException">Nothing was sent through this context yet.</exception>
    public async Task<Message> EditAsync(string content, CancellationToken ct = default)
    {
        var sent = LastSent ?? throw new InvalidOperationException("The bot hasn't sent anything through this context yet.");
        return LastSent = await Client.UpdateMessageAsync(sent.Id, content, ct).ConfigureAwait(false);
    }

    /// <summary>Shows "typing..." in the channel until the scope is disposed. See <see cref="BotClient.BeginTyping"/>.</summary>
    public TypingScope BeginTyping() => Client.BeginTyping(ChannelId);

    /// <summary>
    /// Asks a question and waits for the next message from the same user in the same channel. The answer
    /// is not run as a text command, even if it looks like one. While waiting, the handler doesn't hold up
    /// other events.
    /// </summary>
    /// <param name="timeout">How long to wait; one minute by default.</param>
    /// <returns>The answer, or <c>null</c> if none came in time.</returns>
    public async Task<Message?> AskAsync(string question, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var answer = Client.WaitForEventAsync<MessageCreatedEvent>(
            GatewayEventKind.MessageCreated, e => e.Message.Author.Id == UserId && e.Message.ChannelId == ChannelId,
            timeout ?? TimeSpan.FromMinutes(1), stop.Token, Client.MarkAnswer);
        try
        {
            await ReplyAsync(question, ct).ConfigureAwait(false);
        }
        catch
        {
            // Nobody saw the question, so nobody will answer it: stop waiting instead of leaving the wait behind.
            await stop.CancelAsync().ConfigureAwait(false);
            try
            {
                await answer.ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
            {
            }

            throw;
        }

        try
        {
            return (await answer.ConfigureAwait(false)).Message;
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    /// <summary>
    /// Asks a yes/no question with <see cref="Yes"/> and <see cref="No"/> reactions under it.
    /// </summary>
    /// <param name="timeout">How long to wait; one minute by default.</param>
    /// <returns><c>true</c> when the user picked <see cref="Yes"/>; <c>false</c> for <see cref="No"/> or no answer in time.</returns>
    public async Task<bool> ConfirmAsync(string question, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var prompt = await ReplyAsync(question, ct).ConfigureAwait(false);
        var answer = Client.WaitForEventAsync<ReactionEvent>(
            GatewayEventKind.ReactionAdded, r => r.MessageId == prompt.Id && r.UserId == UserId && r.Emoji is Yes or No,
            timeout ?? TimeSpan.FromMinutes(1), ct);
        foreach (var emoji in new[] { Yes, No })
        {
            try
            {
                await Client.AddReactionAsync(prompt.Id, emoji, ct).ConfigureAwait(false);
            }
            catch (BotApiException)
            {
            }
        }

        try
        {
            return (await answer.ConfigureAwait(false)).Emoji == Yes;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    /// <summary>
    /// Posts the answer to the command. The single place that decides how a reply looks, so slash
    /// commands can switch to real interaction responses once the API has them.
    /// </summary>
    private protected abstract Task<Message> PostReplyAsync(string content, IReadOnlyCollection<Guid> attachmentIds, CancellationToken ct);

    private Guid RequireMessageId() =>
        MessageId ?? throw new InvalidOperationException("A slash command has no message to react to or delete.");

    private async Task<IReadOnlyCollection<Guid>> UploadAsync(IEnumerable<FileUpload> files, CancellationToken ct)
    {
        var ids = new List<Guid>();
        foreach (var file in files)
        {
            ids.Add((await Client.UploadAsync(file, ct).ConfigureAwait(false)).Id);
        }

        return ids;
    }
}
