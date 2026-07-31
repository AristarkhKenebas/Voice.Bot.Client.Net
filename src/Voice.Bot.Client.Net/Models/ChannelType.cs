namespace Voice.Bot.Client.Net.Models;

/// <summary>
/// Mirrors <c>Vc.Common.Types.ChannelType</c> on the backend. The wire format is a plain
/// <c>int32</c> (<c>BotChannelInfo.type</c>/<c>BotChannelDetails.type</c>), not a proto enum, so an
/// unrecognized future value still round-trips as an out-of-range <see cref="ChannelType"/> instead
/// of throwing.
/// </summary>
public enum ChannelType
{
    Unknown,
    Text,
    Voice,
    DirectMessage,
    GroupMessage,
    Tribune,
    Posts,
    Forum,
    Events
}