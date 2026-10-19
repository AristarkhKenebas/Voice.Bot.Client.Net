using System.Collections.Concurrent;
using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Voice.Bot.Client.Net.Commands;
using Voice.Bot.Client.Net.Events;
using Voice.Bot.Client.Net.Extensions;
using Voice.Bot.Client.Net.Gateway;
using Voice.Bot.Client.Net.Interactions;
using Voice.Bot.Client.Net.Internal;
using Voice.Bot.Client.Net.Models;
using Voice.Bot.Client.Net.Testing;
using Xunit;

namespace Voice.Bot.Client.Net.Tests;

public sealed class EchoModule : ModuleBase
{
    [Command("echo")]
    public Task Echo(string text) => ReplyAsync("[" + text + "]");

    [Command("dup")]
    public Task Dup() => ReplyAsync("A");
}

public sealed class DuplicateModule : ModuleBase
{
    [Command("DUP")]
    public Task Dup() => ReplyAsync("B");
}

public sealed class SpacedAliasModule : ModuleBase
{
    [Command("fine")]
    [Alias("not fine")]
    public Task Fine() => ReplyAsync("ok");
}

public sealed class FailingAskModule : ModuleBase
{
    [Command("ask")]
    public async Task Ask() => await Context.AskAsync("name?", TimeSpan.FromSeconds(30));
}

public sealed class DuplicateSlashModule : InteractionModuleBase
{
    [SlashCommand("again", "Says it again")]
    public Task Again() => ReplyAsync("again");
}

public sealed class ManyCommandsModule : ModuleBase
{
    [Command("c00", Description = "A command with a rather long description that takes up a good part of a line")] public Task C00() => Task.CompletedTask;
    [Command("c01", Description = "A command with a rather long description that takes up a good part of a line")] public Task C01() => Task.CompletedTask;
    [Command("c02", Description = "A command with a rather long description that takes up a good part of a line")] public Task C02() => Task.CompletedTask;
    [Command("c03", Description = "A command with a rather long description that takes up a good part of a line")] public Task C03() => Task.CompletedTask;
    [Command("c04", Description = "A command with a rather long description that takes up a good part of a line")] public Task C04() => Task.CompletedTask;
    [Command("c05", Description = "A command with a rather long description that takes up a good part of a line")] public Task C05() => Task.CompletedTask;
    [Command("c06", Description = "A command with a rather long description that takes up a good part of a line")] public Task C06() => Task.CompletedTask;
    [Command("c07", Description = "A command with a rather long description that takes up a good part of a line")] public Task C07() => Task.CompletedTask;
    [Command("c08", Description = "A command with a rather long description that takes up a good part of a line")] public Task C08() => Task.CompletedTask;
    [Command("c09", Description = "A command with a rather long description that takes up a good part of a line")] public Task C09() => Task.CompletedTask;
    [Command("c10", Description = "A command with a rather long description that takes up a good part of a line")] public Task C10() => Task.CompletedTask;
    [Command("c11", Description = "A command with a rather long description that takes up a good part of a line")] public Task C11() => Task.CompletedTask;
    [Command("c12", Description = "A command with a rather long description that takes up a good part of a line")] public Task C12() => Task.CompletedTask;
    [Command("c13", Description = "A command with a rather long description that takes up a good part of a line")] public Task C13() => Task.CompletedTask;
    [Command("c14", Description = "A command with a rather long description that takes up a good part of a line")] public Task C14() => Task.CompletedTask;
    [Command("c15", Description = "A command with a rather long description that takes up a good part of a line")] public Task C15() => Task.CompletedTask;
    [Command("c16", Description = "A command with a rather long description that takes up a good part of a line")] public Task C16() => Task.CompletedTask;
    [Command("c17", Description = "A command with a rather long description that takes up a good part of a line")] public Task C17() => Task.CompletedTask;
    [Command("c18", Description = "A command with a rather long description that takes up a good part of a line")] public Task C18() => Task.CompletedTask;
    [Command("c19", Description = "A command with a rather long description that takes up a good part of a line")] public Task C19() => Task.CompletedTask;
    [Command("c20", Description = "A command with a rather long description that takes up a good part of a line")] public Task C20() => Task.CompletedTask;
    [Command("c21", Description = "A command with a rather long description that takes up a good part of a line")] public Task C21() => Task.CompletedTask;
    [Command("c22", Description = "A command with a rather long description that takes up a good part of a line")] public Task C22() => Task.CompletedTask;
    [Command("c23", Description = "A command with a rather long description that takes up a good part of a line")] public Task C23() => Task.CompletedTask;
    [Command("c24", Description = "A command with a rather long description that takes up a good part of a line")] public Task C24() => Task.CompletedTask;
    [Command("c25", Description = "A command with a rather long description that takes up a good part of a line")] public Task C25() => Task.CompletedTask;
}

public class DispatchTests
{
    [Fact]
    public async Task WaitingForAnAnswer_ShouldNotHoldUpOtherUsers()
    {
        await using var server = await TextCommandTests.StartAsync<TextModule>();
        var other = server.AddUser("other", groupId: server.GroupId);

        var asking = server.SendMessageAsync("!name");
        await server.WaitForSentMessageAsync(m => m.Content == "What is your name?");
        var ping = server.SendMessageAsync("!ping", other);
        await server.WaitForSentMessageAsync(m => m.Content == "pong");
        asking.IsCompleted.Should().BeFalse();

        await server.SendMessageAsync("Bob");
        await asking;
        await ping;
        server.SentMessages.Select(m => m.Content).Should().Equal("What is your name?", "pong", "Hi Bob");
    }

    [Fact]
    public async Task AnAnswer_ShouldNotAlsoRunAsACommand()
    {
        await using var server = await TextCommandTests.StartAsync<TextModule>();

        var asking = server.SendMessageAsync("!name");
        await server.WaitForSentMessageAsync(m => m.Content == "What is your name?");
        await server.SendMessageAsync("!ping");
        await asking;

        server.SentMessages.Select(m => m.Content).Should().Equal("What is your name?", "Hi !ping");
    }

    [Fact]
    public async Task Ask_WhenTheQuestionFails_ShouldStopWaiting()
    {
        await using var server = new FakeVoiceServer(o => o.Replies.Error = null);
        var commands = new CommandService(server.Client);
        commands.AddModule<FailingAskModule>();
        commands.Initialize();
        server.FailNext("CreateMessage", StatusCode.Internal, "boom");

        await server.SendMessageAsync("!ask");

        server.Client.PendingWaits.Should().Be(0);
    }

    [Fact]
    public async Task WaitFor_ShouldSkipTheBotsOwnMessagesAndReactions()
    {
        await using var server = new FakeVoiceServer();
        var message = await server.SendMessageAsync("poll");
        var next = server.Client.WaitForAsync<MessageCreatedEvent>(timeout: TimeSpan.FromSeconds(5));
        var reactions = new List<string>();
        var collecting = Task.Run(async () =>
        {
            await foreach (var reaction in server.Client.CollectReactionsAsync(message.Id, TimeSpan.FromMilliseconds(500))) reactions.Add(reaction.Emoji);
        });
        await Task.Delay(50);

        await server.Client.SendMessageAsync(server.ChannelId, "my own");
        await server.AddReactionAsync(message.Id, "🤖", server.BotId);
        await server.WhenIdleAsync();
        next.IsCompleted.Should().BeFalse();
        await server.SendMessageAsync("from a user");
        await server.AddReactionAsync(message.Id, "👍");

        (await next).Message.Content.Should().Be("from a user");
        await collecting;
        reactions.Should().Equal("👍");
    }

    [Fact]
    public async Task MiddlewareCallingNextTwice_ShouldRunTheCommandOnce()
    {
        await using var server = new FakeVoiceServer();
        var commands = new CommandService(server.Client);
        commands.AddModule<TextModule>();
        commands.Use(async (_, next) =>
        {
            await next();
            await next();
        });
        commands.Initialize();

        await server.SendMessageAsync("!ping");

        server.SentMessages.Select(m => m.Content).Should().Equal("pong");
    }

    [Fact]
    public async Task Disposing_ShouldLetRunningHandlersFinish()
    {
        var server = new FakeVoiceServer();
        var finished = false;
        var started = new TaskCompletionSource();
        server.Client.MessageCreated += async _ =>
        {
            started.TrySetResult();
            await Task.Delay(200);
            finished = true;
        };
        _ = server.SendMessageAsync("slow");
        await started.Task;

        await server.DisposeAsync();

        finished.Should().BeTrue();
    }

    [Fact]
    public async Task DisposingAnotherClientFromAHandler_ShouldStillWaitForItsHandlers()
    {
        await using var first = new FakeVoiceServer();
        var second = new FakeVoiceServer();
        var finished = false;
        var started = new TaskCompletionSource();
        second.Client.MessageCreated += async _ =>
        {
            started.TrySetResult();
            await Task.Delay(200);
            finished = true;
        };
        var disposed = new TaskCompletionSource<bool>();
        first.Client.MessageCreated += async _ =>
        {
            await second.DisposeAsync();
            disposed.TrySetResult(finished);
        };
        _ = second.SendMessageAsync("slow");
        await started.Task;

        await first.SendMessageAsync("dispose the other one");

        (await disposed.Task).Should().BeTrue();
    }

    [Fact]
    public async Task Reconnecting_ShouldClearTheCache()
    {
        await using var server = new FakeVoiceServer();
        await server.ConnectAsync();
        await server.Client.Cache.GetGroupAsync(server.GroupId);
        await server.Client.Cache.GetGroupAsync(server.GroupId);

        await server.ConnectAsync();
        await server.Client.Cache.GetGroupAsync(server.GroupId);

        server.Calls.Count(c => c == "GetGroup").Should().Be(2);
    }
}

public class CommandRegistrationTests
{
    [Fact]
    public async Task DuplicateTextCommandNames_ShouldBeRejected()
    {
        await using var server = new FakeVoiceServer();
        var commands = new CommandService(server.Client);
        commands.AddModule<EchoModule>();

        commands.Invoking(c => c.AddModule<DuplicateModule>()).Should().Throw<ArgumentException>()
            .WithMessage("*'DUP'*DuplicateModule.Dup*EchoModule.Dup*");
        commands.Commands.Select(c => c.Name).Should().Equal("echo", "dup");
    }

    [Fact]
    public async Task CaseSensitiveCommands_ShouldAllowNamesThatDifferInCase()
    {
        await using var server = new FakeVoiceServer(o => o.CaseInsensitiveCommands = false);
        var commands = new CommandService(server.Client);
        commands.AddModule<EchoModule>();

        commands.Invoking(c => c.AddModule<DuplicateModule>()).Should().NotThrow();
    }

    [Fact]
    public async Task NamesWithSpaces_ShouldBeRejected()
    {
        await using var server = new FakeVoiceServer();
        var commands = new CommandService(server.Client);

        commands.Invoking(c => c.AddModule<SpacedAliasModule>()).Should().Throw<ArgumentException>().WithMessage("*'not fine'*one word*");
        commands.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task DuplicateSlashCommands_ShouldBeRejectedAtRegistration()
    {
        await using var server = new FakeVoiceServer();
        var interactions = new InteractionService(server.Client);
        interactions.AddModule<DuplicateSlashModule>();

        interactions.Invoking(i => i.AddModule<DuplicateSlashModule>()).Should().Throw<ArgumentException>().WithMessage("*'again'*already used*");
        interactions.Commands.Should().ContainSingle();
    }

    [Fact]
    public async Task ExtraArguments_ShouldBeAnsweredUnlessTurnedOff()
    {
        await using (var server = await TextCommandTests.StartAsync<TextModule>())
        {
            await server.SendMessageAsync("!ping now");
            server.LastSentMessage!.Content.Should().Be("Unexpected argument: `now`.\nUsage: `!ping`");
        }

        await using (var server = await TextCommandTests.StartAsync<TextModule>(o => o.Replies.UnexpectedArgument = null))
        {
            await server.SendMessageAsync("!ping now");
            server.LastSentMessage!.Content.Should().Be("pong");
        }
    }

    [Fact]
    public async Task Remainder_ShouldKeepTheRawText()
    {
        await using var server = await TextCommandTests.StartAsync<EchoModule>();

        await server.SendMessageAsync("!echo \"a b\"");
        await server.SendMessageAsync("!echo \"a b\" c");
        await server.SendMessageAsync("!echo ```\nx  y\n```");

        server.SentMessages.Select(m => m.Content).Should().Equal("[\"a b\"]", "[\"a b\" c]", "[```\nx  y\n```]");
    }

    [Fact]
    public async Task MentionPrefix_ShouldAcceptTheNicknameForm()
    {
        await using var server = await TextCommandTests.StartAsync<TextModule>();

        await server.SendMessageAsync($"<@!{server.BotId}> ping");

        server.LastSentMessage!.Content.Should().Be("pong");
    }

    [Fact]
    public async Task LongHelp_ShouldBeSplitIntoSeveralMessages()
    {
        await using var server = await TextCommandTests.StartAsync<ManyCommandsModule>();

        await server.SendMessageAsync("!help");

        server.SentMessages.Should().HaveCountGreaterThan(1).And.OnlyContain(m => m.Content.Length <= Format.DefaultMessageLimit);
        string.Join("\n", server.SentMessages.Select(m => m.Content)).Should().Contain("`!c00`").And.Contain("`!c25`");
    }

    [Fact]
    public async Task CooldownTracker_ShouldForgetKeysOutOfTheWindow()
    {
        await using var server = new FakeVoiceServer();
        var tracker = new CooldownTracker(new CooldownAttribute(1, 0.05));
        CommandContext From(Guid user) => new(server.Client, new Message(
            Guid.NewGuid(), server.ChannelId, new User(user, "u", null, null, null, UserStatus.Online, null, null, false, null), "!x",
            null, null, false, false, null, null, null, new Dictionary<string, IReadOnlyList<Guid>>(), [], [], [], [], false, false, null, null), server.GroupId);

        for (var i = 0; i < 100; i++) tracker.TryUse(From(Guid.NewGuid())).Should().BeNull();
        tracker.Count.Should().Be(100);
        await Task.Delay(80);
        tracker.TryUse(From(Guid.NewGuid())).Should().BeNull();

        tracker.Count.Should().Be(1);
    }
}

public class ScheduleFixTests
{
    [Fact]
    public void At_WhenTheTimerFiresEarly_ShouldNotRunTheSameSlotAgain()
    {
        var slot = new DateTimeOffset(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);

        var next = BotClient.NextDailyRun(slot, slot.AddMilliseconds(-2), new TimeOnly(9, 0), TimeZoneInfo.Utc);

        next.Should().Be(slot.AddDays(1));
    }

    [Theory]
    [InlineData("2026-10-23T00:00:00Z", "02:30")]
    [InlineData("2026-03-27T00:00:00Z", "02:30")]
    [InlineData("2026-10-23T00:00:00Z", "09:00")]
    public void At_AcrossADaylightSavingChange_ShouldRunOncePerDay(string start, string time)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        DateTimeOffset? planned = null;
        var runs = new List<DateTimeOffset>();
        for (var i = 0; i < 4; i++)
        {
            planned = BotClient.NextDailyRun(planned, planned ?? DateTimeOffset.Parse(start), TimeOnly.Parse(time), zone);
            runs.Add(planned.Value);
        }

        runs.Select(r => TimeZoneInfo.ConvertTime(r, zone).Date).Should().OnlyHaveUniqueItems();
        runs.Zip(runs.Skip(1), (a, b) => b - a).Should().OnlyContain(gap => gap >= TimeSpan.FromHours(23) && gap <= TimeSpan.FromHours(25));
    }

    [Fact]
    public async Task Every_ShouldAcceptLongIntervalsAndRejectAbsurdOnes()
    {
        await using var server = new FakeVoiceServer();
        var errors = 0;
        server.Client.ConnectionError += _ => { Interlocked.Increment(ref errors); return Task.CompletedTask; };

        using var schedule = server.Client.Every(TimeSpan.FromDays(60), _ => Task.CompletedTask);
        await server.ConnectAsync();
        await Task.Delay(100);

        errors.Should().Be(0);
        server.Client.Invoking(c => c.Every(TimeSpan.MaxValue, _ => Task.CompletedTask)).Should().Throw<ArgumentOutOfRangeException>();
    }
}

public class PageCycleTests
{
    [Fact]
    public async Task Pages_ShouldStopWhenTheServerGoesInCircles()
    {
        var tokens = new[] { "a", "b", "a", "b" };
        var calls = 0;
        var items = new List<int>();

        await foreach (var item in BotClient.EnumeratePagesAsync((_, _) =>
                       {
                           var page = new Page<int>([calls], tokens[calls]);
                           calls++;
                           return Task.FromResult(page);
                       }))
        {
            items.Add(item);
        }

        items.Should().Equal(0, 1, 2);
        calls.Should().Be(3);
    }
}

public class RetrySafetyTests
{
    private static readonly RetryOptions Fast = new() { MaxRetries = 2, BaseDelay = TimeSpan.FromMilliseconds(1), Jitter = 0 };

    [Fact]
    public async Task ADeleteWhoseAnswerWasLost_ShouldCountAsDone()
    {
        await using var server = new FakeVoiceServer(o => o.Retry = Fast);
        var message = await server.Client.SendMessageAsync(server.ChannelId, "bye");
        server.FailNext("DeleteMessage", StatusCode.Unavailable, afterApplying: true);

        await server.Client.DeleteMessageAsync(message.Id);

        server.DeletedMessages.Should().Equal(message.Id);
        server.Calls.Count(c => c == "DeleteMessage").Should().Be(2);
    }

    [Fact]
    public async Task ADeleteOfSomethingMissing_ShouldStillFail()
    {
        await using var server = new FakeVoiceServer(o => o.Retry = Fast);

        var act = () => server.Client.DeleteMessageAsync(Guid.NewGuid());

        (await act.Should().ThrowAsync<BotApiException>()).Which.StatusCode.Should().Be(StatusCode.NotFound);
    }

    [Theory]
    [InlineData("JoinByInvite")]
    [InlineData("StartChannelConnection")]
    public async Task CallsThatCantBeRepeated_ShouldNotBeRetriedOnNetworkErrors(string method)
    {
        await using var server = new FakeVoiceServer(o => o.Retry = Fast);
        server.FailNext(method, StatusCode.Unavailable, times: 3);

        var act = method == "JoinByInvite"
            ? (Func<Task>)(() => server.Client.JoinByInviteAsync("code"))
            : () => server.Client.JoinVoiceChannelAsync(server.ChannelId);

        (await act.Should().ThrowAsync<BotApiException>()).Which.StatusCode.Should().Be(StatusCode.Unavailable);
        server.Calls.Count(c => c == method).Should().Be(1);
    }
}

public class TextSplitTests
{
    [Fact]
    public void TinyLimits_ShouldNotLoopForeverOrCutAnEmojiInHalf()
    {
        Format.Split("a😀b", 1).Should().Equal("a", "😀", "b");
        Format.Split("😀😀", 3).Should().Equal("😀", "😀");
    }

    [Fact]
    public void ACutCodeBlock_ShouldBeClosedAndReopened()
    {
        var code = string.Join("\n", Enumerable.Range(0, 30).Select(i => $"line {i:D2} of the code"));
        var text = "Here:\n```cs\n" + code + "\n```\nDone.";

        var parts = Format.Split(text, 200);

        parts.Should().HaveCountGreaterThan(1).And.OnlyContain(p => p.Length <= 200);
        parts.Should().OnlyContain(p => p.Split('\n').Count(l => l.StartsWith("```", StringComparison.Ordinal)) % 2 == 0);
        parts.Skip(1).First().Should().StartWith("```cs\n");
        string.Concat(parts).Should().Contain("line 29 of the code");
    }
}

public class GatewaySafetyTests
{
    [Fact]
    public void ErrorsMentioningTheToken_ShouldBeRedacted()
    {
        var leaky = new HttpRequestException("GET wss://api.example/hubs/bots?id=1&access_token=ibot_secret failed",
            new IOException("closed ibot_secret"));

        var redacted = SignalRBotGatewayConnection.Redact(leaky, "ibot_secret");

        redacted.ToString().Should().NotContain("ibot_secret").And.Contain("access_token=***");
        redacted.Should().BeOfType<BotApiException>().Which.StatusCode.Should().Be(StatusCode.Unavailable);
        var clean = new HttpRequestException("connection refused");
        SignalRBotGatewayConnection.Redact(clean, "ibot_secret").Should().BeSameAs(clean);
    }

    [Fact]
    public async Task AForbiddenBot_ShouldFailWithoutRetrying()
    {
        await using var server = await HubServer.StartAsync();
        await using var client = BotClient.Create(new BotClientOptions { BotToken = "forbidden", Endpoint = server.Address });
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var act = () => client.StartGatewayAsync(cts.Token);

        (await act.Should().ThrowAsync<BotApiException>()).Which.StatusCode.Should().Be(StatusCode.PermissionDenied);
    }

    [Fact]
    public async Task HostedService_ShouldNotHoldUpTheHostWhileTheServerIsUnreachable()
    {
        await using var client = BotClient.Create(new BotClientOptions { BotToken = "good", Endpoint = "http://127.0.0.1:1" });
        var hosted = new BotClientHostedService(
            client, new InteractionService(client), new CommandService(client), [], NullLogger<BotClientHostedService>.Instance);

        var start = hosted.StartAsync(CancellationToken.None);

        start.IsCompletedSuccessfully.Should().BeTrue();
        await hosted.StopAsync(CancellationToken.None);
        hosted.Connecting.IsCompleted.Should().BeTrue();
        hosted.Dispose();
    }

    [Fact]
    public async Task HostedService_ShouldStopTheApplicationWhenTheTokenIsRejected()
    {
        await using var server = await HubServer.StartAsync();
        await using var client = BotClient.Create(new BotClientOptions { BotToken = "bad", Endpoint = server.Address });
        var lifetime = new RecordingLifetime();
        var hosted = new BotClientHostedService(
            client, new InteractionService(client), new CommandService(client), [], NullLogger<BotClientHostedService>.Instance, lifetime);

        await hosted.StartAsync(CancellationToken.None);
        await hosted.Connecting;

        lifetime.Stopped.Should().BeTrue();
        hosted.Dispose();
    }

    private sealed class RecordingLifetime : IHostApplicationLifetime
    {
        public bool Stopped { get; private set; }
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() => Stopped = true;
    }
}

public class MemberCacheTests
{
    [Fact]
    public async Task MemberEvents_ShouldNotReloadTheWholeListUntilItIsNeeded()
    {
        await using var server = new FakeVoiceServer();
        var cache = server.Client.Cache;
        (await cache.GetMemberAsync(server.GroupId, server.UserId)).Should().NotBeNull();

        await server.MemberLeaveAsync(server.UserId);
        (await cache.GetMemberAsync(server.GroupId, server.UserId)).Should().BeNull();
        var newcomer = server.AddUser("newcomer");
        await server.MemberJoinAsync(newcomer);
        (await cache.GetMemberAsync(server.GroupId, server.OwnerId)).Should().NotBeNull();
        server.Calls.Count(c => c == "GetGroupUsers").Should().Be(1);

        (await cache.GetMemberAsync(server.GroupId, newcomer)).Should().NotBeNull();
        server.Calls.Count(c => c == "GetGroupUsers").Should().Be(2);
    }

    [Fact]
    public async Task ProfileUpdates_ShouldBeWrittenIntoTheCachedMember()
    {
        await using var server = new FakeVoiceServer();
        var cache = server.Client.Cache;
        await cache.GetMemberAsync(server.GroupId, server.UserId);

        await server.SendEventAsync("group-events", "UserProfileUpdated", server.GroupId, new { UserId = server.UserId, DisplayName = "Neo" });

        (await cache.GetMemberAsync(server.GroupId, server.UserId))!.User.DisplayName.Should().Be("Neo");
        server.Calls.Count(c => c == "GetGroupUsers").Should().Be(1);
    }
}

public class TestingKitFidelityTests
{
    [Fact]
    public async Task TheEchoOfABotMessage_ShouldArriveAfterTheAnswer()
    {
        await using var server = new FakeVoiceServer();
        var seen = new ConcurrentQueue<string>();
        Task<Message>? sending = null;
        server.Client.MessageCreated += _ =>
        {
            seen.Enqueue(sending is { IsCompleted: true } ? "after the answer" : "before the answer");
            return Task.CompletedTask;
        };

        sending = server.Client.SendMessageAsync(server.ChannelId, "hi");
        await sending;
        await server.WhenIdleAsync();

        seen.Should().Equal("after the answer");
    }

    [Fact]
    public async Task RoleAndModerationCalls_ShouldSendTheirGatewayEvents()
    {
        await using var server = new FakeVoiceServer();
        var events = new ConcurrentQueue<string>();
        server.Client.GroupStructureChanged += e => { events.Enqueue(e.Type); return Task.CompletedTask; };
        server.Client.MemberKicked += e => { events.Enqueue($"kicked {(e.UserId == server.UserId ? "user" : "?")}"); return Task.CompletedTask; };

        var role = await server.Client.CreateRoleAsync(server.GroupId, "Mods");
        await server.Client.AssignRoleAsync(server.GroupId, server.UserId, role.Id);
        await server.Client.RemoveRoleAsync(server.GroupId, server.UserId, role.Id);
        await server.Client.BanUserAsync(server.GroupId, server.UserId, "spam");
        await server.WhenIdleAsync();

        events.Should().Equal("RoleCreated", "RoleAssigned", "RoleRemoved", "kicked user");
    }

    [Fact]
    public async Task EventsWhileDisconnected_ShouldBeLostUnlessQueued()
    {
        await using var server = new FakeVoiceServer();
        var seen = new ConcurrentQueue<string>();
        server.Client.MessageCreated += e => { seen.Enqueue(e.Message.Content); return Task.CompletedTask; };

        await server.DisconnectAsync();
        await server.SendMessageAsync("lost");
        server.QueueWhileDisconnected = true;
        await server.SendMessageAsync("kept");
        await server.ConnectAsync();
        await server.SendMessageAsync("live");

        seen.Should().Equal("kept", "live");
    }

    [Fact]
    public async Task CallsWithoutAToken_ShouldBeRejected()
    {
        await using var server = new FakeVoiceServer(o => o.BotToken = "");

        var act = () => server.Client.GetMeAsync();

        (await act.Should().ThrowAsync<BotApiException>()).Which.StatusCode.Should().Be(StatusCode.Unauthenticated);
    }
}
