using Voice.Bot.Client.Net.Components;
using Voice.Bot.Client.Net.Events;
using Voice.Bot.Client.Net.Models;

namespace Voice.Bot.Client.Net.Interactions;

/// <summary>Wraps one <see cref="BotInteractionEvent"/> plus the owning <see cref="BotClient"/>, scoping every response call to it.</summary>
public sealed class InteractionContext
{
    private readonly BotClient _client;

    public BotInteractionEvent Interaction { get; }

    internal InteractionContext(BotClient client, BotInteractionEvent interaction)
    {
        _client = client;
        Interaction = interaction;
    }

    public Task RespondAsync(string content, bool ephemeral = false, ComponentBuilder? components = null) =>
        _client.RespondToInteractionAsync(
            Interaction.InteractionId,
            new InteractionResponse(InteractionResponseKind.ChannelMessage, Content: content, ComponentsJson: components?.Build(), Ephemeral: ephemeral));

    public Task DeferAsync(bool ephemeral = false) =>
        _client.RespondToInteractionAsync(
            Interaction.InteractionId, new InteractionResponse(InteractionResponseKind.DeferredChannelMessage, Ephemeral: ephemeral));

    /// <summary>For <see cref="InteractionType.MessageComponent"/> handlers - edits the message the component is attached to.</summary>
    public Task UpdateAsync(string content, ComponentBuilder? components = null) =>
        _client.RespondToInteractionAsync(
            Interaction.InteractionId,
            new InteractionResponse(InteractionResponseKind.UpdateMessage, Content: content, ComponentsJson: components?.Build()));

    /// <summary>Raw-JSON escape hatch - most callers should use <see cref="RespondWithModalAsync(ModalBuilder)"/> instead.</summary>
    public Task RespondWithModalAsync(string modalJson) =>
        _client.RespondToInteractionAsync(Interaction.InteractionId, new InteractionResponse(InteractionResponseKind.Modal, ModalJson: modalJson));

    public Task RespondWithModalAsync(ModalBuilder modal) => RespondWithModalAsync(modal.Build());

    public Task RespondWithAutocompleteAsync(IReadOnlyList<(string Name, string Value)> choices) =>
        _client.RespondToInteractionAsync(
            Interaction.InteractionId, new InteractionResponse(InteractionResponseKind.AutocompleteResult, AutocompleteChoices: choices));

    public Task<MessageInfo> FollowupAsync(string content, ComponentBuilder? components = null) =>
        _client.SendInteractionFollowupAsync(Interaction.InteractionId, content, components);
}
