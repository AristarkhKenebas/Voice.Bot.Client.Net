using Voice.Bot.Client.Net.Models;

namespace Voice.Bot.Client.Net.Commands;

/// <summary>
/// Wraps the triggering <see cref="Models.Message"/> plus the owning <see cref="Voice.Bot.Client.Net.BotClient"/>,
/// scoping reply calls to its channel. Used for text commands and for <see cref="BotClient.OnMessage"/>
/// handlers (there <see cref="BotContext.CommandName"/> is <c>null</c>).
/// </summary>
public sealed class CommandContext : BotContext
{
    public Message Message { get; }

    /// <summary>The author's id; the same as <see cref="BotContext.UserId"/>.</summary>
    public Guid AuthorId => Message.Author.Id;

    /// <summary>The author's profile.</summary>
    public User Author => Message.Author;

    /// <summary>The prefix the command was called with (<c>"!"</c>, or the bot's mention); <c>null</c> for a plain message.</summary>
    public string? Prefix { get; }

    /// <summary>The arguments after the command name, split shell-style; empty for a plain message.</summary>
    public IReadOnlyList<string> Arguments { get; }

    /// <inheritdoc />
    public override bool IsSlash => false;

    /// <inheritdoc />
    public override Guid? MessageId => Message.Id;

    internal CommandContext(BotClient client, Message message, Guid? groupId, string? commandName = null, string? prefix = null, IReadOnlyList<string>? arguments = null)
        : base(client, message.Author.Id, message.ChannelId, groupId, commandName)
    {
        Message = message;
        Prefix = prefix;
        Arguments = arguments ?? [];
    }

    /// <summary>Sends a reply in the same channel as <see cref="Message"/>, as a reply to it.</summary>
    private protected override Task<Message> PostReplyAsync(string content, IReadOnlyCollection<Guid> attachmentIds, CancellationToken ct) =>
        Client.SendMessageAsync(Message.ChannelId, content, replyTo: Message.Id, attachmentIds: attachmentIds, ct: ct);
}
