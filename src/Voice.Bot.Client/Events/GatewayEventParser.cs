using System.Text.Json;
using Voice.Bot.Client.Gateway;

namespace Voice.Bot.Client.Events;

/// <summary>
/// Flattened first JSON layer common to all four gateway topics
/// (<c>KafkaEventBase</c>/<c>KafkaChannelEvent</c>/<c>KafkaGroupEventBase</c>/<c>KafkaInteractionEvent</c>
/// on the backend) - fields that don't apply to a given topic just deserialize to their default value,
/// which System.Text.Json does silently for JSON properties absent from the payload.
/// </summary>
internal sealed record GatewayEnvelope
{
    public string Type { get; init; } = "";
    public string PayloadJson { get; init; } = "";
    public long Timestamp { get; init; }
    public Guid GroupId { get; init; }
    public Guid ChatId { get; init; }
    public Guid UserId { get; init; }
    public Guid MessageId { get; init; }
}

/// <summary>Mirrors the backend's <c>MessageDeletedEvent</c>.</summary>
internal sealed record MessageDeletedPayload(Guid Id, bool Success);

/// <summary>Mirrors the backend's <c>Vc.Backend.Contracts.Events.Users.UserStatusChangedEvent</c> - the <c>group-events</c> payload for <c>"UserStatusChanged"</c>.</summary>
internal sealed record UserStatusChangedPayload(Guid UserId, Voice.Bot.Client.Models.UserStatus PresenceStatus, DateTimeOffset LastActivity);

/// <summary>
/// Pure <c>(topic, json) -&gt; GatewayParseResult</c> parser - the two-level JSON decoding
/// (envelope, then <c>PayloadJson</c> by <c>Type</c>) lives entirely here, deliberately separate from
/// <see cref="IBotGatewayConnection"/>/<see cref="BotClient"/>, so it can be unit-tested with synthetic
/// strings and no SignalR/network involved at all. Never throws - any failure becomes
/// <see cref="GatewayParseResult.ParseError"/>.
/// </summary>
internal static class GatewayEventParser
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static GatewayParseResult Parse(string topic, string json)
    {
        GatewayEnvelope envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<GatewayEnvelope>(json, Options)
                       ?? throw new JsonException("Gateway message deserialized to null.");
        }
        catch (Exception ex)
        {
            return new GatewayParseResult.ParseError(topic, "<unparseable envelope>", ex);
        }

        try
        {
            return topic switch
            {
                GatewayTopics.ChatEvents => ParseChatEvent(envelope),
                GatewayTopics.ChannelEvents => ParseChannelEvent(envelope),
                GatewayTopics.GroupEvents => ParseGroupEvent(envelope),
                GatewayTopics.InteractionEvents => ParseInteractionEvent(envelope),
                _ => Unknown(topic, envelope),
            };
        }
        catch (Exception ex)
        {
            return new GatewayParseResult.ParseError(topic, envelope.Type, ex);
        }
    }

    private static GatewayParseResult ParseChatEvent(GatewayEnvelope e) => e.Type switch
    {
        "MessageCreated" => new GatewayParseResult.MessageCreated(Deserialize<GatewayMessage>(e.PayloadJson)),
        "MessageUpdated" => new GatewayParseResult.MessageUpdated(Deserialize<GatewayMessage>(e.PayloadJson)),
        "MessageDeleted" => MessageDeleted(e),
        "MessageReactionAdded" => new GatewayParseResult.ReactionAdded(Deserialize<MessageReactionEvent>(e.PayloadJson)),
        "MessageReactionRemoved" => new GatewayParseResult.ReactionRemoved(Deserialize<MessageReactionEvent>(e.PayloadJson)),
        "UserTyping" => new GatewayParseResult.UserTyping(Deserialize<TypingEvent>(e.PayloadJson)),
        "BotTyping" => new GatewayParseResult.BotTyping(Deserialize<BotTypingEvent>(e.PayloadJson)),
        _ => Unknown(GatewayTopics.ChatEvents, e),
    };

    private static GatewayParseResult ParseChannelEvent(GatewayEnvelope e) => e.Type switch
    {
        "UserConnected" => new GatewayParseResult.UserConnected(
            new ChannelPresenceEvent(e.GroupId, e.ChatId, e.UserId, Deserialize<UserSnapshot>(e.PayloadJson))),
        "UserDisconnected" => new GatewayParseResult.UserDisconnected(
            new ChannelPresenceEvent(e.GroupId, e.ChatId, e.UserId, Deserialize<UserSnapshot>(e.PayloadJson))),
        _ => Unknown(GatewayTopics.ChannelEvents, e),
    };

    private static GatewayParseResult ParseGroupEvent(GatewayEnvelope e) => e.Type switch
    {
        "UserJoined" => new GatewayParseResult.UserJoinedGroup(
            new GroupJoinedEvent(e.GroupId, e.UserId, Deserialize<GroupSnapshot>(e.PayloadJson))),
        "UserStatusChanged" => UserStatusChanged(e),
        _ => Unknown(GatewayTopics.GroupEvents, e),
    };

    private static GatewayParseResult ParseInteractionEvent(GatewayEnvelope e) => e.Type switch
    {
        "InteractionCreated" => new GatewayParseResult.InteractionCreated(Deserialize<BotInteractionEvent>(e.PayloadJson)),
        _ => Unknown(GatewayTopics.InteractionEvents, e),
    };

    private static GatewayParseResult MessageDeleted(GatewayEnvelope e)
    {
        var payload = Deserialize<MessageDeletedPayload>(e.PayloadJson);
        return new GatewayParseResult.MessageDeleted(payload.Id, payload.Success);
    }

    private static GatewayParseResult UserStatusChanged(GatewayEnvelope e)
    {
        var payload = Deserialize<UserStatusChangedPayload>(e.PayloadJson);
        return new GatewayParseResult.UserStatusChanged(
            new UserStatusChangedEvent(e.GroupId, payload.UserId, payload.PresenceStatus, payload.LastActivity));
    }

    private static GatewayParseResult.Unknown Unknown(string topic, GatewayEnvelope e) =>
        new(new UnknownGatewayEvent(topic, e.Type, e.PayloadJson, e.Timestamp));

    private static T Deserialize<T>(string payloadJson) =>
        JsonSerializer.Deserialize<T>(payloadJson, Options)
        ?? throw new JsonException($"Gateway payload for {typeof(T).Name} deserialized to null.");
}
