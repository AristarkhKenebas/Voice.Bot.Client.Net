namespace Voice.Bot.Client.Net.Events;

/// <summary>
/// Closed result of <see cref="GatewayEventParser.Parse"/> - one case per <see cref="BotClient"/>
/// gateway event, plus <see cref="Unknown"/> (unrecognized <c>Type</c>, not an error) and
/// <see cref="ParseError"/> (recognized <c>Type</c>, malformed payload - surfaced via
/// <c>BotClient.ConnectionError</c>, distinct from <see cref="Unknown"/> which is expected
/// forward-compat behavior).
/// </summary>
internal abstract record GatewayParseResult
{
    private GatewayParseResult()
    {
    }

    public sealed record MessageCreated(GatewayMessage Message) : GatewayParseResult;
    public sealed record MessageUpdated(GatewayMessage Message) : GatewayParseResult;
    public sealed record MessageDeleted(Guid MessageId, bool Success) : GatewayParseResult;
    public sealed record ReactionAdded(MessageReactionEvent Reaction) : GatewayParseResult;
    public sealed record ReactionRemoved(MessageReactionEvent Reaction) : GatewayParseResult;
    public sealed record UserTyping(TypingEvent Typing) : GatewayParseResult;
    public sealed record BotTyping(BotTypingEvent Typing) : GatewayParseResult;
    public sealed record UserConnected(ChannelPresenceEvent Presence) : GatewayParseResult;
    public sealed record UserDisconnected(ChannelPresenceEvent Presence) : GatewayParseResult;
    public sealed record UserJoinedGroup(GroupJoinedEvent Joined) : GatewayParseResult;
    public sealed record UserStatusChanged(UserStatusChangedEvent StatusChanged) : GatewayParseResult;
    public sealed record InteractionCreated(BotInteractionEvent Interaction) : GatewayParseResult;
    public sealed record Unknown(UnknownGatewayEvent Event) : GatewayParseResult;
    public sealed record ParseError(string Topic, string Type, Exception Exception) : GatewayParseResult;
}
