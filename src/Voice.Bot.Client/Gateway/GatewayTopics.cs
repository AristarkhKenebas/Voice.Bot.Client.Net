namespace Voice.Bot.Client.Gateway;

/// <summary>
/// SignalR hub method/topic names the backend uses for <c>hubContext.Clients.Group(...).SendAsync
/// (topicName, event)</c> on <c>/hubs/bots</c> - these are the actual Kafka topic names
/// (<c>Vc.Backend.Kafka.Consumer.KafkaConsumerOptions</c> defaults on the backend), not fixed
/// SignalR-only constants, but the client only needs to know their string values to subscribe.
/// </summary>
internal static class GatewayTopics
{
    public const string ChatEvents = "chat-events";
    public const string ChannelEvents = "channel-events";
    public const string GroupEvents = "group-events";
    public const string InteractionEvents = "interaction-events";

    public static readonly IReadOnlyList<string> All = [ChatEvents, ChannelEvents, GroupEvents, InteractionEvents];
}
