using System.Collections.Concurrent;
using FluentAssertions;
using Voice.Bot.Client.Net.Events;
using Voice.Bot.Client.Net.Models;
using Voice.Bot.Client.Net.Testing;
using Xunit;

namespace Voice.Bot.Client.Net.Tests;

public class EventHelperTests
{
    [Fact]
    public async Task OnMessage_ShouldSkipTheBotItselfAndOtherBotsByDefault()
    {
        await using var server = new FakeVoiceServer();
        var otherBot = server.AddUser("other-bot", isBot: true, server.GroupId);
        var seen = new List<string>();
        var all = new List<string>();
        server.Client.OnMessage(ctx => { seen.Add(ctx.Message.Content); return Task.CompletedTask; });
        server.Client.OnMessage(ctx => { all.Add(ctx.Message.Content); return Task.CompletedTask; }, new EventFilter(IgnoreBots: false));

        await server.SendMessageAsync("from a user");
        await server.SendMessageAsync("from a bot", otherBot);
        await server.Client.SendMessageAsync(server.ChannelId, "from myself");
        await server.WhenIdleAsync();

        seen.Should().Equal("from a user");
        all.Should().Equal("from a user", "from a bot");
    }

    [Fact]
    public async Task Filters_ShouldLimitChannelsAndGroups_AndDisposeShouldUnsubscribe()
    {
        await using var server = new FakeVoiceServer();
        var other = server.AddChannel("other");
        var seen = new List<string>();
        var subscription = server.Client.OnMessage(ctx => { seen.Add(ctx.Message.Content); return Task.CompletedTask; }, new EventFilter(ChannelIds: [other]));

        await server.SendMessageAsync("general");
        await server.SendMessageAsync("other", channelId: other);
        subscription.Dispose();
        await server.SendMessageAsync("after", channelId: other);

        seen.Should().Equal("other");
    }

    [Fact]
    public async Task MemberAndReactionShortcuts_ShouldFire()
    {
        await using var server = new FakeVoiceServer();
        var log = new ConcurrentQueue<string>();
        var newcomer = Guid.NewGuid();
        server.Client.OnMemberJoin(e => { log.Enqueue($"join {e.UserId == newcomer}"); return Task.CompletedTask; });
        server.Client.OnMemberLeave(e => { log.Enqueue($"leave {e.UserId == newcomer}"); return Task.CompletedTask; });
        server.Client.OnReactionAdd(e => { log.Enqueue($"add {e.Emoji}"); return Task.CompletedTask; });
        server.Client.OnReactionRemove(e => { log.Enqueue($"remove {e.Emoji}"); return Task.CompletedTask; });
        var message = await server.SendMessageAsync("react to me");

        await server.MemberJoinAsync(newcomer);
        await server.MemberLeaveAsync(newcomer);
        await server.AddReactionAsync(message.Id, "👍");
        await server.AddReactionAsync(message.Id, "🤖", server.BotId);
        await server.RemoveReactionAsync(message.Id, "👍");

        log.Should().Equal("join True", "leave True", "add 👍", "remove 👍");
    }

    [Fact]
    public async Task Ready_ShouldFireOnEveryConnectionWithTheProfile()
    {
        await using var server = new FakeVoiceServer();
        var names = new List<string>();
        server.Client.OnReady(me => { names.Add(me.Username); return Task.CompletedTask; });

        await server.ConnectAsync();
        await server.ConnectAsync();

        names.Should().Equal("test-bot", "test-bot");
    }
}

public class CacheTests
{
    [Fact]
    public async Task Lookups_ShouldBeRememberedUntilAnEventChangesThem()
    {
        await using var server = new FakeVoiceServer();
        var cache = server.Client.Cache;

        (await cache.GetChannelByNameAsync(server.GroupId, "#General"))!.Id.Should().Be(server.ChannelId);
        (await cache.GetChannelAsync(server.ChannelId))!.Name.Should().Be("general");
        (await cache.GetChannelGroupIdAsync(server.ChannelId)).Should().Be(server.GroupId);
        server.Calls.Count(c => c == "GetGroup").Should().Be(1);

        var news = server.AddChannel("news");
        (await cache.GetChannelByNameAsync(server.GroupId, "news")).Should().BeNull();
        await server.SendEventAsync("group-events", "ChannelCreated", server.GroupId, new { });
        (await cache.GetChannelByNameAsync(server.GroupId, "news"))!.Id.Should().Be(news);
        server.Calls.Count(c => c == "GetGroup").Should().Be(2);
    }

    [Fact]
    public async Task MembersAndRoles_ShouldRefreshAfterMemberEvents()
    {
        await using var server = new FakeVoiceServer();
        var mods = server.AddRole("Mods", Permissions.KickMembers);
        server.GiveRole(server.UserId, mods);
        var cache = server.Client.Cache;

        (await cache.GetMemberAsync(server.GroupId, server.UserId))!.RoleIds.Should().Equal(mods);
        (await cache.GetRoleByNameAsync(server.GroupId, "@mods"))!.Permissions.Should().Be(Permissions.KickMembers);
        (await cache.GetRoleAsync(server.GroupId, mods))!.Name.Should().Be("Mods");
        var newcomer = server.AddUser("newcomer");
        (await cache.GetMemberAsync(server.GroupId, newcomer)).Should().BeNull();

        await server.MemberJoinAsync(newcomer);

        (await cache.GetMemberAsync(server.GroupId, newcomer)).Should().NotBeNull();
        (await cache.GetMembersAsync(server.GroupId)).Should().HaveCount(4);
        server.Calls.Count(c => c == "GetGroupRoles").Should().Be(1);
    }

    [Fact]
    public async Task DisabledCache_ShouldAskTheServerEveryTime()
    {
        await using var server = new FakeVoiceServer(o => o.EnableCache = false);

        await server.Client.Cache.GetGroupAsync(server.GroupId);
        await server.Client.Cache.GetGroupAsync(server.GroupId);

        server.Calls.Count(c => c == "GetGroup").Should().Be(2);
    }

    [Fact]
    public async Task FailedLookups_ShouldNotBeRemembered()
    {
        await using var server = new FakeVoiceServer();
        server.FailNext("GetGroup", Grpc.Core.StatusCode.Internal);

        var first = () => server.Client.Cache.GetGroupAsync(server.GroupId);
        await first.Should().ThrowAsync<BotApiException>();
        (await server.Client.Cache.GetGroupAsync(server.GroupId)).Name.Should().Be("Test group");
    }
}

public class PaginationTests
{
    [Fact]
    public async Task Messages_ShouldComeNewestFirstAcrossPages()
    {
        await using var server = new FakeVoiceServer();
        for (var i = 1; i <= 7; i++) await server.SendMessageAsync($"m{i}");

        var all = new List<string>();
        await foreach (var message in server.Client.EnumerateMessagesAsync(server.ChannelId, pageSize: 3)) all.Add(message.Content);
        var some = new List<string>();
        await foreach (var message in server.Client.EnumerateMessagesAsync(server.ChannelId, limit: 4, pageSize: 3)) some.Add(message.Content);

        all.Should().Equal("m7", "m6", "m5", "m4", "m3", "m2", "m1");
        some.Should().Equal("m7", "m6", "m5", "m4");
        server.Calls.Count(c => c == "GetChannelMessages").Should().Be(4 + 2);
    }

    [Fact]
    public async Task MembersAndBans_ShouldBeReadPageByPage()
    {
        await using var server = new FakeVoiceServer();
        for (var i = 0; i < 5; i++) server.AddUser($"user{i}", groupId: server.GroupId);
        await server.Client.BanUserAsync(server.GroupId, server.UserId, "spam");

        var members = new List<GroupMember>();
        await foreach (var member in server.Client.EnumerateGroupMembersAsync(server.GroupId, pageSize: 2)) members.Add(member);
        var bans = new List<GroupBan>();
        await foreach (var ban in server.Client.EnumerateBansAsync(server.GroupId)) bans.Add(ban);

        members.Should().HaveCount(7);
        server.Calls.Count(c => c == "GetGroupUsers").Should().Be(4);
        bans.Single().Reason.Should().Be("spam");
        server.Moderation.Single().Should().Be(new ModerationAction("ban", server.GroupId, server.UserId, "spam"));
    }

    [Fact]
    public async Task Errors_ShouldSurfaceFromTheEnumeration()
    {
        await using var server = new FakeVoiceServer();
        server.FailNext("GetGroupRoles", Grpc.Core.StatusCode.PermissionDenied);

        var act = async () => { await foreach (var _ in server.Client.EnumerateRolesAsync(server.GroupId)) { } };

        (await act.Should().ThrowAsync<BotApiException>()).Which.StatusCode.Should().Be(Grpc.Core.StatusCode.PermissionDenied);
    }
}

public class FileAndTypingTests
{
    [Fact]
    public async Task SendFile_ShouldUploadAndPostInOneCall()
    {
        await using var server = new FakeVoiceServer();
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path, "notes");
        try
        {
            await server.Client.SendFileAsync(server.ChannelId, FileUpload.FromBytes("chart.png", new byte[] { 1, 2, 3 }), "Chart");
            await server.Client.SendFilesAsync(server.ChannelId, [FileUpload.FromPath(path, "notes.txt"), FileUpload.FromBytes("data.bin", new byte[] { 9 })]);
        }
        finally
        {
            File.Delete(path);
        }

        server.Uploads.Select(u => (u.FileName, u.ContentType)).Should().Equal(
            ("chart.png", "image/png"), ("notes.txt", "text/plain"), ("data.bin", "application/octet-stream"));
        server.Uploads[1].Data.Should().Equal("notes"u8.ToArray());
        server.SentMessages[0].Content.Should().Be("Chart");
        server.SentMessages[1].Attachments.Select(a => a.FileName).Should().Equal("notes.txt", "data.bin");
    }

    [Theory]
    [InlineData("photo.JPG", "image/jpeg")]
    [InlineData("clip.mp4", "video/mp4")]
    [InlineData("README", "application/octet-stream")]
    public void ContentType_ShouldBeGuessedFromTheExtension(string name, string type) =>
        FileUpload.GuessContentType(name).Should().Be(type);

    [Fact]
    public async Task TypingScope_ShouldShowAndThenHideTheIndicator()
    {
        await using var server = new FakeVoiceServer();

        await using (server.Client.BeginTyping(server.ChannelId))
        {
            await Task.Delay(50);
        }

        server.Typing.First().Should().Be(new TypingSignal(server.ChannelId, true));
        server.Typing.Last().Should().Be(new TypingSignal(server.ChannelId, false));
    }
}

public class ScheduleAndModuleTests
{
    [Fact]
    public async Task Every_ShouldStartAfterReadyKeepGoingAfterErrorsAndStop()
    {
        await using var server = new FakeVoiceServer();
        var runs = 0;
        var errors = 0;
        server.Client.ConnectionError += _ => { Interlocked.Increment(ref errors); return Task.CompletedTask; };
        var schedule = server.Client.Every(TimeSpan.FromMilliseconds(20), _ =>
        {
            if (Interlocked.Increment(ref runs) == 1) throw new InvalidOperationException("first run fails");
            return Task.CompletedTask;
        });

        await Task.Delay(100);
        runs.Should().Be(0);

        await server.ConnectAsync();
        await CommandServiceTests.WaitFor(() => runs >= 3);
        errors.Should().BeGreaterThanOrEqualTo(1);

        schedule.Dispose();
        var stopped = runs;
        await Task.Delay(100);
        runs.Should().BeLessThanOrEqualTo(stopped + 1);
    }

    [Fact]
    public async Task StopGateway_ShouldKeepScheduledTasks_AndDisposeShouldStopThem()
    {
        var server = new FakeVoiceServer();
        var runs = 0;
        server.Client.Every(TimeSpan.FromMilliseconds(20), _ => { Interlocked.Increment(ref runs); return Task.CompletedTask; });
        await server.ConnectAsync();
        await CommandServiceTests.WaitFor(() => runs >= 1);

        await server.Client.StopGatewayAsync();
        await server.Client.StartGatewayAsync();
        var restarted = runs;
        await CommandServiceTests.WaitFor(() => runs >= restarted + 2);

        await server.DisposeAsync();
        var stopped = runs;
        await Task.Delay(100);
        runs.Should().BeLessThanOrEqualTo(stopped + 1);
    }

    [Theory]
    [InlineData("2026-10-19T10:00:00Z", "09:00", "2026-10-20T09:00:00Z")]
    [InlineData("2026-10-19T10:00:00Z", "11:30", "2026-10-19T11:30:00Z")]
    [InlineData("2026-10-19T09:00:00Z", "09:00", "2026-10-20T09:00:00Z")]
    public void At_ShouldPickTheNextDailyOccurrence(string now, string time, string expected) =>
        BotClient.NextOccurrence(DateTimeOffset.Parse(now), TimeOnly.Parse(time), TimeZoneInfo.Utc).Should().Be(DateTimeOffset.Parse(expected));

    [Fact]
    public async Task Modules_ShouldBundleHandlersAndTasks()
    {
        await using var server = new FakeVoiceServer();
        server.Client.Install(new WelcomeModule());

        var newcomer = server.AddUser("newcomer");
        await server.MemberJoinAsync(newcomer);

        server.LastSentMessage!.Content.Should().Be($"Welcome, {Mention.User(newcomer)}!");
    }

    private sealed class WelcomeModule : IBotModule
    {
        public void Install(BotClient client) =>
            client.OnMemberJoin(async e =>
            {
                var channel = await client.Cache.GetChannelByNameAsync(e.GroupId, "general");
                await client.SendMessageAsync(channel!.Id, $"Welcome, {Mention.User(e.UserId)}!");
            });
    }
}
