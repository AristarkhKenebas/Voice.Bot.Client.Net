namespace Voice.Bot.Client.Net.Intents;

/// <summary>
/// Discord-style Gateway Intents bitmask granted to the bot by its owner - gates both which
/// <c>BotsApi</c> RPC categories are reachable and which gateway event categories are delivered on
/// <c>/hubs/bots</c>. This is a bit-for-bit mirror of the backend's <c>Vc.Common.Types.BotIntents</c>
/// (kept as a local copy rather than a project reference - see the plan's rationale for an
/// independently versionable client). If the backend adds a new intent, this enum needs a matching
/// update; there is no automatic sync between the two repos.
/// </summary>
[Flags]
public enum BotIntents : ulong
{
    None = 0,

    /// <summary>Group/channel structure and membership - GetMyGroups/GetGroup/GetGroupUsers/GetChannel, and group-events delivery.</summary>
    Guilds = 1UL << 0,

    /// <summary>Messages - SendMessage/UpdateMessage/DeleteMessage/AddReaction/RemoveReaction/Typing, and chat-events delivery.</summary>
    GuildMessages = 1UL << 1,

    /// <summary>Voice channel presence - JoinVoiceChannel/LeaveVoiceChannel, and channel-events delivery.</summary>
    GuildVoiceStates = 1UL << 2,
}

public static class BotIntentsExtensions
{
    public static bool Has(this BotIntents granted, BotIntents required) => (granted & required) == required;
}
