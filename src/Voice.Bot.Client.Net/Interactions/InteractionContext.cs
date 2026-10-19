using Voice.Bot.Client.Net.Events;
using Voice.Bot.Client.Net.Models;

namespace Voice.Bot.Client.Net.Interactions;

/// <summary>Wraps one <see cref="BotInteractionEvent"/> plus the owning <see cref="BotClient"/>.</summary>
/// <remarks>
/// The current API has no way to answer an interaction directly, so a command handler replies by
/// posting a regular message to the channel the command was used in - see <see cref="BotContext.ReplyAsync(string, CancellationToken)"/>.
/// </remarks>
public sealed class InteractionContext : BotContext
{
    public BotInteractionEvent Interaction { get; }

    /// <summary>The option values by name, as the user typed them.</summary>
    public IReadOnlyDictionary<string, string> Options => Interaction.Data.Options;

    /// <inheritdoc />
    public override bool IsSlash => true;

    /// <inheritdoc />
    public override Guid? MessageId => null;

    internal InteractionContext(BotClient client, BotInteractionEvent interaction)
        : base(client, interaction.InvokingUserId, interaction.ChannelId, interaction.GroupId, interaction.Data.CommandName)
    {
        Interaction = interaction;
    }

    /// <summary>Posts a message to the channel the command was used in.</summary>
    private protected override Task<Message> PostReplyAsync(string content, IReadOnlyCollection<Guid> attachmentIds, CancellationToken ct) =>
        Client.SendMessageAsync(Interaction.ChannelId, content, attachmentIds: attachmentIds, ct: ct);
}
