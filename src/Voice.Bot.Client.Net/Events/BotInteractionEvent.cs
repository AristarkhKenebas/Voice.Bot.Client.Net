namespace Voice.Bot.Client.Net.Events;

/// <summary>
/// Mirrors the backend's <c>InteractionType</c> - what triggered the interaction. Member order/values
/// must match the backend enum exactly: the wire payload serializes this as a plain number, not a
/// string.
/// </summary>
public enum InteractionType
{
    ApplicationCommand = 0,
    MessageComponent = 1,
    ModalSubmit = 2,
    ApplicationCommandAutocomplete = 3,
}

/// <summary>
/// Mirrors the backend's <c>InteractionRequestPayload</c> - which fields are populated depends on
/// <see cref="BotInteractionEvent.Type"/> (e.g. <see cref="CustomId"/>/<see cref="Values"/> only for
/// <see cref="InteractionType.MessageComponent"/>).
/// </summary>
public sealed record InteractionData(
    string? CommandName,
    IReadOnlyDictionary<string, string>? Options,
    string? CustomId,
    IReadOnlyList<string>? Values,
    string? FocusedOption,
    IReadOnlyDictionary<string, string>? ModalFields);

/// <summary>
/// <c>interaction-events</c> payload for <c>"InteractionCreated"</c> - a slash command invocation,
/// component click, modal submit, or autocomplete request the bot needs to answer via
/// <c>BotClient.RespondToInteractionAsync</c> within the backend's ack window. Delivered to the bot's
/// personal SignalR group regardless of granted <see cref="Intents.BotIntents"/> - interactions aren't
/// intent-gated.
/// </summary>
public sealed record BotInteractionEvent(
    Guid InteractionId,
    Guid InvokingUserId,
    Guid ChannelId,
    Guid GroupId,
    InteractionType Type,
    InteractionData Data);
