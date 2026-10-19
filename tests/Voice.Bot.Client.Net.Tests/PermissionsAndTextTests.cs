using FluentAssertions;
using Voice.Bot.Client.Net.Commands;
using Xunit;

namespace Voice.Bot.Client.Net.Tests;

public class PermissionsTests
{
    [Fact]
    public void Flags_ShouldMatchTheDocumentedBits()
    {
        Enum.GetValues<Permissions>().Where(p => p != Permissions.None).Should().HaveCount(40);
        ((ulong)Permissions.ViewChannels).Should().Be(1UL);
        ((ulong)Permissions.KickMembers).Should().Be(1UL << 11);
        ((ulong)Permissions.Administrator).Should().Be(1UL << 14);
        ((ulong)Permissions.ManageMessages).Should().Be(1UL << 25);
        ((ulong)Permissions.Speak).Should().Be(1UL << 31);
        ((ulong)Permissions.RequestToSpeak).Should().Be(1UL << 39);
        Enum.GetValues<Permissions>().Where(p => p != Permissions.None).Select(p => (ulong)p)
            .Should().Equal(Enumerable.Range(0, 40).Select(bit => 1UL << bit));
    }

    [Fact]
    public void Has_ShouldTreatAdministratorAsEverything()
    {
        var moderator = Permissions.KickMembers | Permissions.BanMembers;

        moderator.Has(Permissions.KickMembers).Should().BeTrue();
        moderator.Has(Permissions.KickMembers | Permissions.ManageMessages).Should().BeFalse();
        moderator.Missing(Permissions.KickMembers | Permissions.ManageMessages).Should().Be(Permissions.ManageMessages);
        Permissions.Administrator.Has(Permissions.RequestToSpeak | Permissions.ManageServer).Should().BeTrue();
        Permissions.Administrator.Missing(Permissions.ManageServer).Should().Be(Permissions.None);
        moderator.ToString().Should().Be("KickMembers, BanMembers");
    }
}

public class MentionAndFormatTests
{
    private static readonly Guid Id = Guid.Parse("2f1c4b7e-1111-4222-8333-944455556666");

    [Fact]
    public void Mentions_ShouldBuildAndParse()
    {
        Mention.User(Id).Should().Be($"<@{Id}>");
        Mention.Channel(Id).Should().Be($"<#{Id}>");
        Mention.Role(Id).Should().Be($"<@&{Id}>");
        (Mention.Everyone, Mention.Here).Should().Be(("@everyone", "@here"));

        var other = Guid.NewGuid();
        var text = $"hi <@{Id}> and <@{other}>, see <#{other}> with <@&{Id}> <@{Id}>";
        Mention.ParseUsers(text).Should().Equal(Id, other);
        Mention.ParseChannels(text).Should().Equal(other);
        Mention.ParseRoles(text).Should().Equal(Id);

        Mention.TryParseUser($" <@{Id}> ", out var user).Should().BeTrue();
        user.Should().Be(Id);
        Mention.TryParseUser(Id.ToString(), out _).Should().BeTrue();
        Mention.TryParseUser($"<#{Id}>", out _).Should().BeFalse();
        Mention.TryParseRole($"<@&{Id}>x", out _).Should().BeFalse();
    }

    [Fact]
    public void Split_ShouldCutAtLinesThenWordsThenAnywhere()
    {
        Format.Split("short").Should().Equal("short");
        Format.Split("").Should().Equal("");
        Format.Split("aaa bbb\nccc ddd", 9).Should().Equal("aaa bbb", "ccc ddd");
        Format.Split("aaa bbb ccc", 8).Should().Equal("aaa bbb", "ccc");
        Format.Split("abcdefghij", 4).Should().Equal("abcd", "efgh", "ij");
        Format.Split(new string('x', 4500)).Select(p => p.Length).Should().Equal(2000, 2000, 500);
    }

    [Fact]
    public void CodeHelpers_ShouldWrapText()
    {
        Format.CodeBlock("var x = 1;", "cs").Should().Be("```cs\nvar x = 1;\n```");
        Format.CodeBlock("a ``` b").Should().NotContain("a ``` b");
        Format.Code("x").Should().Be("`x`");
        BotReplies.FormatDuration(TimeSpan.FromSeconds(3725)).Should().Be("1h 2m 5s");
        BotReplies.FormatDuration(TimeSpan.FromSeconds(42.2)).Should().Be("43s");
        BotReplies.FormatDuration(TimeSpan.FromMilliseconds(500)).Should().Be("0.5s");
    }

    [Fact]
    public void Tokenizer_ShouldHandleQuotesAndEscapesShellStyle()
    {
        CommandArgumentParser.Tokenize("a 'b c' \"d \\\" e\" f\\ g don't").Should().Equal("a", "b c", "d \" e", "f g", "don't");
        CommandArgumentParser.Split("x  \"y z\"").Select(t => t.Start).Should().Equal(0, 3);
    }
}
