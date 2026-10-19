using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Voice.Bot.Client.Net.Commands;
using Voice.Bot.Client.Net.Events;
using Voice.Bot.Client.Net.Extensions;
using Voice.Bot.Client.Net.Interactions;
using Voice.Bot.Client.Net.Models;
using Voice.Bot.Client.Net.Testing;
using Xunit;

namespace Voice.Bot.Client.Net.Tests;

public class SlashCommandTests
{
    private static async Task<(FakeVoiceServer Server, InteractionService Interactions)> StartAsync(Action<BotClientOptions>? configure = null)
    {
        var server = new FakeVoiceServer(configure);
        var interactions = new InteractionService(server.Client);
        interactions.AddModule<SlashModule>();
        interactions.Initialize();
        await Task.CompletedTask;
        return (server, interactions);
    }

    [Fact]
    public async Task Options_ShouldConvertUseDefaultsOrExplainTheProblem()
    {
        var (server, _) = await StartAsync();
        await using var _ = server;
        var user = Guid.NewGuid();

        await server.InvokeSlashCommandAsync("days");
        await server.InvokeSlashCommandAsync("days", new Dictionary<string, string> { ["n"] = "5", ["user"] = user.ToString() });
        await server.InvokeSlashCommandAsync("days", new Dictionary<string, string> { ["n"] = "five" });
        await server.InvokeSlashCommandAsync("unknown");

        server.SentMessages.Select(m => m.Content).Should().Equal("3|-", $"5|{user}", "`n` must be a whole number.");
        server.SentMessages[0].ReplyTo.Should().BeNull();
    }

    [Fact]
    public async Task Sync_ShouldDeclareTypedOptionsAndPermissions()
    {
        var (server, interactions) = await StartAsync();
        await using var _ = server;

        await interactions.RegisterCommandsAsync();

        var days = server.RegisteredCommands.Single(c => c.Name == "days");
        days.DefaultMemberPermissions.Should().Be(Permissions.ManageMessages);
        days.Options!.Select(o => (o.Name, o.Type, o.Required)).Should().Equal(("n", CommandOptionType.Integer, false), ("user", CommandOptionType.User, false));
    }

    [Fact]
    public async Task HostedService_ShouldInstallModulesConnectAndSync()
    {
        var (server, interactions) = await StartAsync();
        await using var _ = server;
        var commands = new CommandService(server.Client);
        var module = new GreeterModule();
        var hosted = new BotClientHostedService(server.Client, interactions, commands, [module], NullLogger<BotClientHostedService>.Instance);

        await hosted.StartAsync(CancellationToken.None);
        await hosted.Connecting;

        server.RegisteredCommands.Select(c => c.Name).Should().BeEquivalentTo("days", "guarded", "crash");
        module.Installed.Should().BeSameAs(server.Client);
        await hosted.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task HostedService_ShouldSkipSyncWhenTurnedOff()
    {
        var (server, interactions) = await StartAsync(o => o.SyncCommands = false);
        await using var _ = server;
        var hosted = new BotClientHostedService(server.Client, interactions, new CommandService(server.Client), [], NullLogger<BotClientHostedService>.Instance);

        await hosted.StartAsync(CancellationToken.None);
        await hosted.Connecting;

        server.RegisteredCommands.Should().BeEmpty();
    }

    [Fact]
    public async Task ChecksCooldownsAndErrors_ShouldWorkForSlashCommandsToo()
    {
        var (server, interactions) = await StartAsync();
        await using var _ = server;
        Exception? failed = null;
        interactions.HandlerFailed += (ex, _) => { failed = ex; return Task.CompletedTask; };

        await server.InvokeSlashCommandAsync("guarded", channelId: Guid.NewGuid());
        await server.InvokeSlashCommandAsync("guarded");
        await server.InvokeSlashCommandAsync("guarded", userId: server.OwnerId);
        await server.InvokeSlashCommandAsync("crash");

        server.SentMessages.Select(m => m.Content).Should().Equal(
            "This command works only in a group.", "guarded ok", "Slow down! Try again in 30s.", "Something went wrong.");
        failed!.Message.Should().Be("slash kaboom");
    }

    private sealed class GreeterModule : IBotModule
    {
        public BotClient? Installed { get; private set; }

        public void Install(BotClient client) => Installed = client;
    }
}

public class CheckTests
{
    [Fact]
    public async Task GroupAndDmOnly_ShouldDependOnWhereTheCommandIsUsed()
    {
        await using var server = await TextCommandTests.StartAsync<CheckedModule>();
        var commands = new CommandService(server.Client);
        commands.AddModule<DmModule>();
        commands.Initialize();
        var dm = Guid.NewGuid();

        await server.SendMessageAsync("!owner", server.OwnerId, dm);
        await server.SendMessageAsync("!secret", channelId: dm);
        await server.SendMessageAsync("!secret");

        server.SentMessages.Select(m => m.Content).Should().Equal(
            "This command works only in a group.", "psst", "This command works only in direct messages.");
    }

    [Fact]
    public async Task OwnerRolesAndPermissions_ShouldGuardCommands()
    {
        await using var server = await TextCommandTests.StartAsync<CheckedModule>();
        var mods = server.AddRole("Moderators");
        var moderator = server.AddUser("mod", groupId: server.GroupId);
        server.GiveRole(moderator, mods);

        await server.SendMessageAsync("!owner");
        await server.SendMessageAsync("!owner", server.OwnerId);
        await server.SendMessageAsync("!mods");
        await server.SendMessageAsync("!mods", moderator);
        await server.SendMessageAsync("!kick");
        await server.SendMessageAsync("!kick", server.OwnerId);
        await server.SendMessageAsync("!custom");

        server.SentMessages.Select(m => m.Content).Should().Equal(
            "Only the group owner can use this command.", "owner ok",
            "You can't use this command.", "mods ok",
            "You need these permissions: KickMembers.", "kick ok",
            "Mods only.");
    }

    [Fact]
    public async Task BotHasPermissions_ShouldCheckTheBotsOwnPermissions()
    {
        await using var server = await TextCommandTests.StartAsync<CheckedModule>();

        await server.SendMessageAsync("!purge");
        server.SetPermissions(server.BotId, Permissions.ManageMessages | Permissions.SendMessages);
        await server.SendMessageAsync("!purge");

        server.SentMessages.Select(m => m.Content).Should().Equal("I need these permissions: ManageMessages.", "purge ok");
    }

    [Fact]
    public async Task Cooldown_ShouldAnswerWithTheTimeLeftPerUser()
    {
        await using var server = await TextCommandTests.StartAsync<CheckedModule>(o => o.Replies.Cooldown = left => $"wait {left.TotalSeconds > 50}");

        await server.SendMessageAsync("!once");
        await server.SendMessageAsync("!once");
        await server.SendMessageAsync("!once", server.OwnerId);

        server.SentMessages.Select(m => m.Content).Should().Equal("once ok", "wait True", "once ok");
    }

    [Fact]
    public async Task UsersCheck_ShouldOnlyLetListedUsersIn()
    {
        await using var server = new FakeVoiceServer();
        var check = new UsersAttribute(server.OwnerId.ToString());
        var message = await server.SendMessageAsync("hi");
        var context = new CommandContext(server.Client, message, server.GroupId);
        var ownerContext = new CommandContext(server.Client, message with { Author = message.Author with { Id = server.OwnerId } }, server.GroupId);

        (await check.CheckAsync(context, null)).Should().Be(CheckResult.Fail("You can't use this command."));
        (await check.CheckAsync(ownerContext, null)).Should().Be(CheckResult.Success);
        ((Action)(() => _ = new UsersAttribute("not-a-guid"))).Should().Throw<FormatException>();
    }
}

public class ConversationTests
{
    [Fact]
    public async Task Ask_ShouldReturnTheNextMessageOfTheSameUser()
    {
        await using var server = await TextCommandTests.StartAsync<TextModule>();
        var stranger = server.AddUser("stranger", groupId: server.GroupId);

        var command = server.SendMessageAsync("!name");
        await server.WaitForSentMessageAsync(m => m.Content == "What is your name?");
        var mallory = server.SendMessageAsync("Mallory", stranger);
        await server.SendMessageAsync("Bob");
        await Task.WhenAll(command, mallory);

        server.LastSentMessage!.Content.Should().Be("Hi Bob");
    }

    [Fact]
    public async Task Ask_ShouldReturnNullOnTimeout()
    {
        await using var server = await TextCommandTests.StartAsync<TextModule>();

        await server.SendMessageAsync("!quick");

        server.LastSentMessage!.Content.Should().Be("Too slow");
    }

    [Fact]
    public async Task Confirm_ShouldOfferReactionsAndReadTheAnswer()
    {
        await using var server = await TextCommandTests.StartAsync<TextModule>();

        var command = server.SendMessageAsync("!wipe");
        var prompt = await server.WaitForSentMessageAsync(m => m.Content == "Sure?");
        var owner = server.AddReactionAsync(prompt.Id, BotContext.Yes, server.OwnerId);
        await server.AddReactionAsync(prompt.Id, BotContext.Yes);
        await Task.WhenAll(command, owner);

        server.LastSentMessage!.Content.Should().Be("Wiped");
        server.Reactions.Select(r => r.Emoji).Should().Equal(BotContext.Yes, BotContext.No);
    }

    [Fact]
    public async Task WaitFor_ShouldReturnTheMatchingEventOrTimeOut()
    {
        await using var server = new FakeVoiceServer();
        var newcomer = Guid.NewGuid();

        var joined = server.Client.WaitForAsync<MemberEvent>(e => e.UserId == newcomer, TimeSpan.FromSeconds(5));
        await server.MemberJoinAsync(Guid.NewGuid());
        await server.MemberJoinAsync(newcomer);
        (await joined).GroupId.Should().Be(server.GroupId);

        var never = () => server.Client.WaitForAsync<MemberEvent>(timeout: TimeSpan.FromMilliseconds(50));
        await never.Should().ThrowAsync<TimeoutException>();
    }

    [Fact]
    public async Task CollectReactions_ShouldYieldReactionsOnTheMessageUntilTheTimeIsUp()
    {
        await using var server = new FakeVoiceServer();
        var poll = await server.Client.SendMessageAsync(server.ChannelId, "Vote!");
        var other = await server.Client.SendMessageAsync(server.ChannelId, "Other");

        var collected = Task.Run(async () =>
        {
            var reactions = new List<string>();
            await foreach (var reaction in server.Client.CollectReactionsAsync(poll.Id, TimeSpan.FromMilliseconds(500))) reactions.Add(reaction.Emoji);
            return reactions;
        });
        await Task.Delay(50);
        await server.AddReactionAsync(poll.Id, "👍");
        await server.AddReactionAsync(other.Id, "👎");
        await server.RemoveReactionAsync(poll.Id, "👍");
        await server.AddReactionAsync(poll.Id, "🎉", server.OwnerId);

        (await collected).Should().Equal("👍", "🎉");
    }
}
