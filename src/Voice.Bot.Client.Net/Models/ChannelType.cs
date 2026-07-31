namespace Voice.Bot.Client.Net.Models;

/// <summary>
/// Mirrors <c>Vc.Common.Types.ChannelType</c> on the backend - member order/values must match exactly
/// (<see cref="Text"/> = 0, not a separate <c>Unknown</c> sentinel - the backend has none). The wire
/// format is a plain <c>int32</c> (<c>BotChannelInfo.type</c>/<c>BotChannelDetails.type</c>), not a
/// proto enum, so an unrecognized future value already round-trips as an out-of-range
/// <see cref="ChannelType"/> instead of throwing - no <c>Unknown</c> member is needed for that.
/// </summary>
public enum ChannelType
{
    Text,
    Voice,
    DirectMessage,
    GroupMessage,
    Tribune,
    Posts,
    Forum,
    Events
}