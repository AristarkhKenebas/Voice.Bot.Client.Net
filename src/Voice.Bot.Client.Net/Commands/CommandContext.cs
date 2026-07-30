using Voice.Bot.Client.Net.Components;
using Voice.Bot.Client.Net.Events;
using Voice.Bot.Client.Net.Models;

namespace Voice.Bot.Client.Net.Commands;

/// <summary>Wraps the triggering <see cref="GatewayMessage"/> plus the owning <see cref="Voice.Bot.Client.Net.BotClient"/>, scoping reply calls to its channel.</summary>
public sealed class CommandContext
{
    private readonly BotClient _client;

    public GatewayMessage Message { get; }
    public Guid ChannelId => Message.ChannelId;
    public Guid AuthorId => Message.Author.Id;

    internal CommandContext(BotClient client, GatewayMessage message)
    {
        _client = client;
        Message = message;
    }

    /// <summary>Sends a reply in the same channel as <see cref="Message"/> (as a reply-to, Discord-style).</summary>
    public Task<MessageInfo> ReplyAsync(string content, ComponentBuilder? components = null, CancellationToken ct = default) =>
        _client.SendMessageAsync(Message.ChannelId, content, replyTo: Message.Id, components: components, ct: ct);
}
