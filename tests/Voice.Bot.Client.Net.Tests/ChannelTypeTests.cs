using FluentAssertions;
using Voice.Bot.Client.Net.Models;
using Xunit;

namespace Voice.Bot.Client.Net.Tests;

/// <summary>
/// Regression coverage for a real bug: <see cref="ChannelType"/>'s member order must match
/// <c>Vc.Common.Types.ChannelType</c> on the backend exactly, since the wire format is a plain
/// <c>int32</c> (<c>BotChannelInfo.type</c>), not a proto enum - there's no name-based mapping to
/// catch a drift. An earlier revision inserted an <c>Unknown</c> sentinel at position 0, silently
/// shifting every other member's wire value by one (<see cref="ChannelType.Text"/> serialized as 1
/// instead of 0, which the backend reads back as <see cref="ChannelType.Voice"/>).
/// </summary>
public class ChannelTypeTests
{
    [Theory]
    [InlineData(ChannelType.Text, 0)]
    [InlineData(ChannelType.Voice, 1)]
    [InlineData(ChannelType.DirectMessage, 2)]
    [InlineData(ChannelType.GroupMessage, 3)]
    [InlineData(ChannelType.Tribune, 4)]
    [InlineData(ChannelType.Posts, 5)]
    [InlineData(ChannelType.Forum, 6)]
    [InlineData(ChannelType.Events, 7)]
    [InlineData(ChannelType.Board, 8)]
    public void ChannelType_ShouldMatchBackendWireValue(ChannelType type, int expectedWireValue)
    {
        ((int)type).Should().Be(expectedWireValue);
    }
}
