namespace Voice.Bot.Client.Events;

/// <summary>
/// A gateway message whose <c>Type</c> this library doesn't recognize (any of the four topics) - raised
/// via <c>BotClient.UnhandledEvent</c> instead of throwing/disconnecting, so the backend can add new
/// event types without breaking already-deployed bots.
/// </summary>
public sealed record UnknownGatewayEvent(string Topic, string Type, string RawPayloadJson, long Timestamp);
