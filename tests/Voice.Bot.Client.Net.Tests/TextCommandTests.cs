using FluentAssertions;
using Voice.Bot.Client.Net.Commands;
using Voice.Bot.Client.Net.Interactions;
using Voice.Bot.Client.Net.Testing;
using Xunit;

namespace Voice.Bot.Client.Net.Tests;

public class TextCommandTests
{
    internal static async Task<FakeVoiceServer> StartAsync<TModule>(Action<BotClientOptions>? configure = null) where TModule : ModuleBase
    {
        var server = new FakeVoiceServer(configure);
        var commands = new CommandService(server.Client);
        commands.AddModule<TModule>();
        commands.Initialize();
        return server;
    }

    [Fact]
    public async Task Prefixes_MentionAndCase_ShouldAllRunTheCommand()
    {
        await using var server = await StartAsync<TextModule>(o => o.TextCommandPrefixes = ["!", "?"]);

        await server.SendMessageAsync("!ping");
        await server.SendMessageAsync("?PING");
        await server.SendMessageAsync("!p");
        await server.SendMessageAsync($"<@{server.BotId}> ping");
        await server.SendMessageAsync("ping");

        server.SentMessages.Select(m => m.Content).Should().Equal("pong", "pong", "pong", "pong");
        server.SentMessages[0].ReplyTo.Should().NotBeNull();
    }

    [Fact]
    public async Task CaseSensitiveCommands_ShouldNotMatchOtherCase()
    {
        await using var server = await StartAsync<TextModule>(o => o.CaseInsensitiveCommands = false);

        await server.SendMessageAsync("!PING");

        server.SentMessages.Should().BeEmpty();
    }

    [Theory]
    [InlineData("!add 1", "11")]
    [InlineData("!add 1 2", "3")]
    [InlineData("!add", "`a` is required.\nUsage: `!add <a> [b]`")]
    [InlineData("!add x", "`a` must be a whole number.\nUsage: `!add <a> [b]`")]
    [InlineData("!say  hello   \"big\" world ", "hello   \"big\" world")]
    [InlineData("!say \"only quoted\"", "\"only quoted\"")]
    [InlineData("!say it\\'s", "it\\'s")]
    [InlineData("!say line one\n  line two", "line one\n  line two")]
    [InlineData("!flag yes 0.5 high", "True|0.5|High")]
    [InlineData("!flag off 2", "False|2|Low")]
    [InlineData("!flag maybe 2", "`on` must be yes or no.\nUsage: `!flag <on> <ratio> [level]`")]
    [InlineData("!flag on 1 huge", "`level` must be one of: low, high.\nUsage: `!flag <on> <ratio> [level]`")]
    public async Task Arguments_ShouldBindOrExplainTheUsage(string content, string reply)
    {
        await using var server = await StartAsync<TextModule>();

        await server.SendMessageAsync(content);

        server.LastSentMessage!.Content.Should().Be(reply);
    }

    [Fact]
    public async Task MentionArguments_ShouldBindToGuids()
    {
        await using var server = await StartAsync<TextModule>();
        var user = Guid.NewGuid();

        await server.SendMessageAsync($"!who {Mention.User(user)} new here");
        await server.SendMessageAsync($"!who {user}");

        server.SentMessages.Select(m => m.Content).Should().Equal($"{user}|new here", $"{user}|-");
    }

    [Fact]
    public async Task Bots_ShouldBeIgnoredUnlessAllowed()
    {
        await using var server = await StartAsync<TextModule>();
        var otherBot = server.AddUser("other-bot", isBot: true, server.GroupId);

        await server.SendMessageAsync("!ping", otherBot);
        server.SentMessages.Should().BeEmpty();

        await using var allowing = await StartAsync<TextModule>(o => o.IgnoreBots = false);
        var friendlyBot = allowing.AddUser("friendly-bot", isBot: true, allowing.GroupId);
        await allowing.SendMessageAsync("!ping", friendlyBot);
        allowing.SentMessages.Should().ContainSingle().Which.Content.Should().Be("pong");
    }

    [Fact]
    public async Task Errors_ShouldGetDefaultRepliesAndRaiseTheEvent()
    {
        await using var server = new FakeVoiceServer();
        var commands = new CommandService(server.Client);
        commands.AddModule<TextModule>();
        var failures = new List<Exception>();
        commands.CommandExecutionFailed += (ex, _) => { failures.Add(ex); return Task.CompletedTask; };
        commands.Initialize();

        await server.SendMessageAsync("!boom");
        await server.SendMessageAsync("!denied");
        await server.SendMessageAsync("!limited");

        server.SentMessages.Select(m => m.Content).Should().Equal(
            "Something went wrong.", "I don't have permission to do that.", "I'm being rate limited, please try again in a moment.");
        failures.Select(f => f.Message).Should().Equal("kaboom", "missing ManageRoles", "Bad gRPC response. HTTP status code: 429");
    }

    [Fact]
    public async Task ErrorReply_CanBeChangedOrTurnedOff()
    {
        await using var server = await StartAsync<TextModule>(o => o.Replies.Error = null);
        await server.SendMessageAsync("!boom");
        server.SentMessages.Should().BeEmpty();

        await using var custom = await StartAsync<TextModule>(o => o.Replies.Error = "Oops");
        await custom.SendMessageAsync("!boom");
        custom.LastSentMessage!.Content.Should().Be("Oops");
    }

    [Fact]
    public async Task ModuleErrorHandler_ShouldTakeOverTheError()
    {
        await using var server = new FakeVoiceServer();
        var commands = new CommandService(server.Client);
        commands.AddModule<HandledErrorsModule>();
        var raised = false;
        commands.CommandExecutionFailed += (_, _) => { raised = true; return Task.CompletedTask; };
        commands.Initialize();

        await server.SendMessageAsync("!oops");

        HandledErrorsModule.Handled!.Message.Should().Be("handled here");
        raised.Should().BeFalse();
        server.SentMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task Help_ShouldListTextAndSlashCommands()
    {
        await using var server = new FakeVoiceServer();
        var interactions = new InteractionService(server.Client);
        interactions.AddModule<SlashModule>();
        var commands = new CommandService(server.Client) { Interactions = interactions };
        commands.AddModule<TextModule>();
        commands.Initialize();

        await server.SendMessageAsync("!help");
        var help = server.LastSentMessage!.Content;
        help.Should().Contain("`!ping` - Replies with pong").And.Contain("`!add <a> [b]` - Adds two numbers")
            .And.Contain("`!say <text...>`").And.Contain("`/days [n] [user]` - Counts days");

        await server.SendMessageAsync("!help p");
        server.LastSentMessage!.Content.Should().Be("`!ping` - Replies with pong\nAliases: p");
    }

    [Fact]
    public async Task Help_ShouldGiveWayToAModulesOwnHelpOrBeTurnedOff()
    {
        await using var server = await StartAsync<OwnHelpModule>();
        await server.SendMessageAsync("!help");
        server.LastSentMessage!.Content.Should().Be("my own help");

        await using var off = await StartAsync<TextModule>(o => o.HelpCommand = false);
        await off.SendMessageAsync("!help");
        off.SentMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task Middleware_ShouldWrapCommandsInOrderAndCanStopThem()
    {
        await using var server = new FakeVoiceServer();
        var commands = new CommandService(server.Client);
        commands.AddModule<TextModule>();
        var log = new List<string>();
        commands.Use(async (ctx, next) => { log.Add($"outer {ctx.CommandName}"); await next(); log.Add("outer done"); })
            .Use(async (ctx, next) =>
            {
                log.Add("inner");
                if (ctx.UserId != server.OwnerId) await next();
            });
        commands.Initialize();

        await server.SendMessageAsync("!p");
        await server.SendMessageAsync("!p", server.OwnerId);

        log.Should().Equal("outer ping", "inner", "outer done", "outer ping", "inner", "outer done");
        server.SentMessages.Should().ContainSingle();
    }

    [Fact]
    public async Task ContextActions_ShouldReplyEditReactAndAttach()
    {
        await using var server = await StartAsync<TextModule>();

        var command = await server.SendMessageAsync("!edit");
        await server.SendMessageAsync("!file");

        server.EditedMessages.Should().ContainSingle().Which.Content.Should().Be("Done: 42");
        server.Reactions.Should().ContainSingle().Which.Should().Be(new SentReaction(command.Id, "✅"));
        server.Uploads.Should().ContainSingle().Which.ContentType.Should().Be("text/csv");
        server.LastSentMessage!.Attachments.Single().FileName.Should().Be("report.csv");
    }

    [Fact]
    public async Task AsyncRunMode_ShouldNotHoldUpOtherEvents()
    {
        await using var server = await StartAsync<TextModule>();

        await server.SendMessageAsync("!background");
        await server.SendMessageAsync("!ping");

        server.SentMessages.Select(m => m.Content).Should().Equal("pong");
        (await server.WaitForSentMessageAsync(m => m.Content == "done later")).Should().NotBeNull();
    }

    [Fact]
    public void Commands_ShouldDescribeTheirUsage()
    {
        var server = new FakeVoiceServer();
        var commands = new CommandService(server.Client, "?");
        commands.AddModule<TextModule>();

        commands.Commands.Single(c => c.Name == "ping").Should().BeEquivalentTo(new TextCommandInfo("ping", ["p"], "Replies with pong", "?ping"));
        commands.Commands.Single(c => c.Name == "who").Usage.Should().Be("?who <user> [note...]");
    }
}
