using FluentAssertions;
using Voice.Bot.Client.Net.Commands;
using Xunit;

namespace Voice.Bot.Client.Net.Tests;

public class CommandArgumentParserTests
{
    [Fact]
    public void Tokenize_ShouldSplitOnWhitespace()
    {
        CommandArgumentParser.Tokenize("a b c").Should().Equal("a", "b", "c");
    }

    [Fact]
    public void Tokenize_ShouldCollapseRepeatedWhitespace()
    {
        CommandArgumentParser.Tokenize("a   b").Should().Equal("a", "b");
    }

    [Fact]
    public void Tokenize_EmptyInput_ShouldReturnNoTokens()
    {
        CommandArgumentParser.Tokenize("").Should().BeEmpty();
        CommandArgumentParser.Tokenize("   ").Should().BeEmpty();
    }

    [Fact]
    public void Tokenize_QuotedSubstring_ShouldBeKeptAsOneToken()
    {
        CommandArgumentParser.Tokenize("tag \"hello world\" 3").Should().Equal("tag", "hello world", "3");
    }

    [Fact]
    public void Tokenize_UnterminatedQuote_ShouldTakeRestOfInputAsOneToken()
    {
        CommandArgumentParser.Tokenize("tag \"hello world").Should().Equal("tag", "hello world");
    }
}
