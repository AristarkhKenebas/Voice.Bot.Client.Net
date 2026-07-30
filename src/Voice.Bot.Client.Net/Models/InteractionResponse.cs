namespace Voice.Bot.Client.Net.Models;

/// <summary>Mirrors <c>bots.InteractionResponseKind</c>.</summary>
public enum InteractionResponseKind
{
    ChannelMessage = 0,
    DeferredChannelMessage = 1,
    UpdateMessage = 2,
    DeferredUpdateMessage = 3,
    Modal = 4,
    AutocompleteResult = 5,
}

/// <summary>What the bot sends back via <c>BotClient.RespondToInteractionAsync</c>. Mirrors <c>bots.RespondToInteractionRequest</c>.</summary>
public sealed record InteractionResponse(
    InteractionResponseKind Kind,
    string? Content = null,
    string? ComponentsJson = null,
    string? ModalJson = null,
    IReadOnlyList<(string Name, string Value)>? AutocompleteChoices = null,
    bool Ephemeral = false);
