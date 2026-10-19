using System.Net;
using System.Net.Http;
using FluentAssertions;
using Grpc.Core;
using Vc.Backend.WebApi.Attachments.Grpc;
using Vc.Backend.WebApi.Channels.Grpc;
using Vc.Backend.WebApi.Groups.Grpc;
using Vc.Backend.WebApi.Interactions.Grpc;
using Vc.Backend.WebApi.Messages.Grpc;
using Vc.Backend.WebApi.Users.Grpc;
using Voice.Bot.Client.Net.Models;
using Xunit;
using AccessType = Voice.Bot.Client.Net.Models.AccessType;
using AttachmentStatus = Voice.Bot.Client.Net.Models.AttachmentStatus;
using CommandOptionType = Voice.Bot.Client.Net.Models.CommandOptionType;
using ChannelType = Voice.Bot.Client.Net.Models.ChannelType;
using UserStatus = Voice.Bot.Client.Net.Models.UserStatus;
using VerificationLevel = Voice.Bot.Client.Net.Models.VerificationLevel;

namespace Voice.Bot.Client.Net.Tests;

public class UsersApiTests
{
    [Fact]
    public async Task GetCachedMe_ShouldSendTokenAndAskOnce()
    {
        var users = new FakeUsers();
        await using var h = await TestHarness.StartAsync([users]);

        for (var i = 0; i < 3; i++)
        {
            var me = await h.Client.GetCachedMeAsync();
            me.Id.Should().Be(Guid.Parse(Known.Bot));
            me.IsBot.Should().BeTrue();
            me.Status.Should().Be(UserStatus.DoNotDisturb);
        }

        users.GetMeCalls.Should().Be(1);
        h.Recorder.Authorization.Should().Be("Bearer ibot_test_token");
    }

    [Fact]
    public async Task GetCachedMe_ShouldNotRememberFailures()
    {
        var users = new FakeUsers { Fail = true };
        await using var h = await TestHarness.StartAsync([users]);

        var act = () => h.Client.GetCachedMeAsync();
        (await act.Should().ThrowAsync<BotApiException>()).Which.StatusCode.Should().Be(StatusCode.Unauthenticated);

        users.Fail = false;
        (await h.Client.GetCachedMeAsync()).Id.Should().Be(Guid.Parse(Known.Bot));
    }

    [Fact]
    public async Task Calls_ShouldTimeOutWithDeadlineExceeded()
    {
        var users = new FakeUsers();
        await using var h = await TestHarness.StartAsync([users], timeout: TimeSpan.FromSeconds(1));
        await h.Client.GetMeAsync();
        users.Delay = TimeSpan.FromSeconds(3);

        var started = DateTime.UtcNow;
        using var noRetries = h.Client.WithRetries(RetryOptions.None);
        var act = () => h.Client.GetMeAsync();
        (await act.Should().ThrowAsync<BotApiException>()).Which.StatusCode.Should().Be(StatusCode.DeadlineExceeded);
        (DateTime.UtcNow - started).Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Profile_ShouldOnlySendGivenFields()
    {
        await using var h = await TestHarness.StartAsync([new FakeUsers()]);

        await h.Client.UpdateProfileAsync(displayName: "Weather", statusText: "");
        var request = h.Recorder.Last<UpdateUserRequest>("update")!;
        request.DisplayName.Should().Be("Weather");
        request.HasDescription.Should().BeFalse();
        request.HasStatus.Should().BeTrue();

        (await h.Client.SetStatusAsync(UserStatus.Invisible)).Status.Should().Be(UserStatus.Invisible);
        (await h.Client.GetAvatarUrlAsync(Guid.Parse(Known.UserId))).Should().Be("https://cdn/" + Known.UserId);
        (await h.Client.GetAvatarUrlAsync(Guid.Parse(Known.Missing))).Should().BeNull();
    }

    [Fact]
    public async Task AvatarAndBanner_ShouldUploadThenConfirm()
    {
        await using var h = await TestHarness.StartAsync([new FakeUsers()]);

        await h.Client.SetAvatarAsync("png"u8.ToArray(), "image/png");
        await h.Client.SetBannerAsync("jpg"u8.ToArray(), "image/jpeg");
        await h.Client.DeleteAvatarAsync();
        await h.Client.DeleteBannerAsync();

        h.Recorder.Last<InitiateAvatarUploadRequest>("avatar")!.SizeBytes.Should().Be(3);
        h.Recorder.Last<ConfirmAvatarUploadRequest>("confirm-avatar")!.AvatarId.Should().Be("av1");
        h.Recorder.Last<ConfirmBannerUploadRequest>("confirm-banner")!.BannerId.Should().Be("bn1");
        h.Storage.Uploads.Select(u => (u.Url, u.ContentType)).Should().Equal(("https://s3/avatar", "image/png"), ("https://s3/banner", "image/jpeg"));
    }

    [Fact]
    public async Task FailedAvatarUpload_ShouldNotBeConfirmed()
    {
        await using var h = await TestHarness.StartAsync([new FakeUsers()]);
        h.Storage.Respond = _ => new HttpResponseMessage(HttpStatusCode.Forbidden);

        var act = () => h.Client.SetAvatarAsync("png"u8.ToArray(), "image/png");
        (await act.Should().ThrowAsync<BotApiException>()).Which.UploadStatusCode.Should().Be(403);
        h.Recorder.Last<ConfirmAvatarUploadRequest>("confirm-avatar").Should().BeNull();
    }
}

public class GroupsApiTests
{
    [Fact]
    public async Task Groups_ShouldMapTreeAndSendRequests()
    {
        await using var h = await TestHarness.StartAsync([new FakeGroups()]);
        var client = h.Client;
        var groupId = Guid.Parse(Known.GroupId);

        (await client.GetMyGroupsAsync()).Single().AccessType.Should().Be(AccessType.InviteOnly);

        var group = await client.GetGroupAsync(groupId);
        group.IsOwner.Should().BeTrue();
        group.VerificationLevel.Should().Be(VerificationLevel.Medium);
        group.VoiceRegion.Should().BeNull();
        group.CreatedAt.Should().Be(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));
        var channels = group.Channels.ToList();
        channels[0].Type.Should().Be(ChannelType.Text);
        channels[0].UserLimit.Should().BeNull();
        channels[1].Type.Should().Be(ChannelType.Voice);
        channels[1].UserLimit.Should().Be(10);
        channels[1].VoiceParticipants.Single().MicEnabled.Should().BeTrue();

        var missing = () => client.GetGroupAsync(Guid.Parse(Known.Missing));
        (await missing.Should().ThrowAsync<BotApiException>()).Which.StatusCode.Should().Be(StatusCode.NotFound);

        await client.UpdateGroupAsync(groupId, "New", "About", AccessType.Open, VerificationLevel.Low);
        var update = h.Recorder.Last<UpdateGroupRequest>("update-group")!;
        update.Description.Should().Be("About");
        update.HasVoiceRegion.Should().BeFalse();
        await client.UpdateGroupAsync(groupId, "New", "", AccessType.Open, VerificationLevel.None, voiceRegion: "");
        h.Recorder.Last<UpdateGroupRequest>("update-group")!.HasVoiceRegion.Should().BeTrue();

        await client.LeaveGroupAsync(groupId);
        (await client.JoinByInviteAsync("abc")).Should().Be(groupId);

        var page = await client.GetGroupMembersAsync(groupId, pageSize: 2, pageToken: "t1");
        page.HasMore.Should().BeTrue();
        page.Items[0].RoleIds.Should().Equal(Guid.Parse(Known.RoleId));
        page.Items[1].User.Id.Should().Be(Guid.Empty);
        var members = h.Recorder.Last<GetGroupUsersRequest>("members")!;
        (members.PageSize, members.PageToken).Should().Be((2, "t1"));
        await client.SearchGroupMembersAsync(groupId, "ann");
        h.Recorder.Last<GetGroupUsersRequest>("members")!.Query.Should().Be("ann");

        var list = await client.GetChannelMembersAsync(groupId, Guid.Parse(Known.ChannelId), offset: 10, limit: 50);
        list.Total.Should().Be(7);
        list.Sections.Select(s => s.RoleId).Should().Equal(Guid.Parse(Known.RoleId), null);
        list.Items.Single().Member.User.Id.Should().Be(Guid.Empty);

        (await client.GetEffectivePermissionsAsync(groupId)).Should().Be(Permissions.ManageChannels | Permissions.ManageEmojisAndStickers);
        h.Recorder.Last<GetEffectivePermissionsRequest>("permissions")!.HasActorId.Should().BeFalse();

        await client.KickUserAsync(groupId, Guid.Parse(Known.UserId));
        await client.BanUserAsync(groupId, Guid.Parse(Known.UserId), "spam");
        await client.UnbanUserAsync(groupId, Guid.Parse(Known.UserId));
        h.Recorder.Last<BanUserRequest>("ban")!.Reason.Should().Be("spam");
        (await client.GetBansAsync(groupId)).Items.Select(b => b.Reason).Should().Equal("spam", null);
        (await client.GetInvitesAsync(groupId)).Items.Single().MaxUses.Should().Be(5);

        var expires = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);
        (await client.CreateInviteAsync(groupId, expires, 3)).ExpiresAt.Should().Be(expires);
        await client.CreateInviteAsync(groupId);
        h.Recorder.Last<CreateInviteRequest>("create-invite")!.ExpiresAt.Should().BeNull();
        await client.UpdateInviteAsync(Guid.NewGuid(), 9);
        await client.DeleteInviteAsync(Guid.NewGuid());
        h.Recorder.Last<UpdateInviteRequest>("update-invite")!.MaxUses.Should().Be(9);

        var log = await client.GetAuditLogAsync(groupId);
        log.Select(e => e.TargetId).Should().Equal("target", null);
        h.Recorder.Last<GetGroupAuditLogRequest>("audit")!.Limit.Should().Be(50);
    }

    [Fact]
    public async Task RolesAndChannels_ShouldSendOptionalFieldsOnlyWhenGiven()
    {
        await using var h = await TestHarness.StartAsync([new FakeGroups(), new FakeChannels()]);
        var client = h.Client;
        var groupId = Guid.Parse(Known.GroupId);

        var roles = await client.GetRolesAsync(groupId, pageSize: 10);
        roles.Items.Single().Color.Should().Be("#ff0000");
        (await client.CreateRoleAsync(groupId, "vip", Permissions.ViewAuditLog, hoist: true)).Hoist.Should().BeTrue();
        h.Recorder.Last<CreateRoleRequest>("create-role")!.HasColor.Should().BeFalse();
        await client.UpdateRoleAsync(Guid.Parse(Known.RoleId), "vip+", Permissions.None, color: "#00ff00");
        h.Recorder.Last<UpdateRoleRequest>("update-role")!.HasHoist.Should().BeFalse();
        await client.MoveRoleAsync(Guid.Parse(Known.RoleId), previousRoleId: Guid.Parse(Known.RoleId));
        h.Recorder.Last<MoveRoleRequest>("move-role")!.HasNextRoleId.Should().BeFalse();
        await client.DeleteRoleAsync(Guid.Parse(Known.RoleId));
        await client.AssignRoleAsync(groupId, Guid.Parse(Known.UserId), Guid.Parse(Known.RoleId));
        await client.RemoveRoleAsync(groupId, Guid.Parse(Known.UserId), Guid.Parse(Known.RoleId));
        h.Recorder.Last<RoleAssignmentRequest>("assign")!.TargetUserId.Should().Be(Known.UserId);

        (await client.CreateCategoryAsync(groupId, "News")).Name.Should().Be("News");
        (await client.UpdateCategoryAsync(Guid.Parse(Known.CategoryId), "Announcements")).Name.Should().Be("Announcements");
        await client.DeleteCategoryAsync(Guid.Parse(Known.CategoryId));
        await client.MoveCategoryAsync(Guid.Parse(Known.CategoryId), nextCategoryId: Guid.Parse(Known.CategoryId));

        foreach (var (type, code) in new[] { (ChannelType.Text, 0), (ChannelType.Voice, 1), (ChannelType.Board, 8) })
        {
            (await client.CreateChannelAsync(Guid.Parse(Known.CategoryId), "x", type)).Type.Should().Be(type);
            h.Recorder.Last<CreateChannelRequest>("create-channel")!.Type.Should().Be(code);
        }

        await client.UpdateChannelAsync(Guid.Parse(Known.ChannelId), name: "voice", userLimit: 0);
        var update = h.Recorder.Last<UpdateChannelRequest>("update-channel")!;
        (update.HasName, update.HasUserLimit, update.HasBitrate, update.HasDescription).Should().Be((true, true, false, false));
        await client.DeleteChannelAsync(Guid.Parse(Known.ChannelId));
        (await client.MoveChannelAsync(Guid.Parse(Known.ChannelId), targetCategoryId: Guid.Parse(Known.CategoryId))).CategoryId
            .Should().Be(Guid.Parse(Known.CategoryId));
        (await client.SetChannelCategorySyncAsync(Guid.Parse(Known.ChannelId), true)).SyncsCategoryPermissions.Should().BeTrue();

        (await client.GetChannelPermissionOverwritesAsync(Guid.Parse(Known.ChannelId))).Single()
            .Should().Be(new PermissionOverwrite(Guid.Parse(Known.RoleId), true, Permissions.ViewChannels, Permissions.ManageChannels));
        await client.SetChannelPermissionOverwriteAsync(Guid.Parse(Known.ChannelId), new PermissionOverwrite(Guid.Parse(Known.UserId), false, Permissions.ViewChannels | Permissions.ManageChannels, Permissions.ManageRoles));
        h.Recorder.Last<SetChannelPermissionOverwriteRequest>("set-overwrite")!.Deny.Should().Be(4);
        await client.DeleteChannelPermissionOverwriteAsync(Guid.Parse(Known.ChannelId), Guid.Parse(Known.RoleId), isRole: true);
        (await client.GetCategoryPermissionOverwritesAsync(Guid.Parse(Known.CategoryId))).Single().Allow.Should().Be(Permissions.ManageRoles);
        await client.SetCategoryPermissionOverwriteAsync(Guid.Parse(Known.CategoryId), new PermissionOverwrite(Guid.Parse(Known.RoleId), true));
        await client.DeleteCategoryPermissionOverwriteAsync(Guid.Parse(Known.CategoryId), Guid.Parse(Known.RoleId), isRole: true);

        var participants = await client.GetVoiceParticipantsAsync(Guid.Parse(Known.ChannelId));
        participants[0].Deafened.Should().BeTrue();
        participants[1].User.Id.Should().Be(Guid.Empty);
        (await client.JoinVoiceChannelAsync(Guid.Parse(Known.ChannelId))).Should().Be(new VoiceConnection("wss://livekit", "lk-" + Known.ChannelId));
    }
}

public class MessagesApiTests
{
    [Fact]
    public async Task Messages_ShouldSendAndMap()
    {
        await using var h = await TestHarness.StartAsync([new FakeMessages()]);
        var client = h.Client;
        var channelId = Guid.Parse(Known.ChannelId);

        var sent = await client.SendMessageAsync(channelId, "Hello");
        sent.Content.Should().Be("Hello");
        sent.Author.IsBot.Should().BeTrue();
        sent.Interaction.Should().BeNull();
        h.Recorder.Last<CreateMessageRequest>("create")!.ReplyTo.Should().BeEmpty();
        var reply = Guid.NewGuid();
        var files = new[] { Guid.NewGuid(), Guid.NewGuid() };
        await client.SendMessageAsync(channelId, "Files", reply, files);
        var create = h.Recorder.Last<CreateMessageRequest>("create")!;
        create.ReplyTo.Should().Be(reply.ToString());
        create.AttachmentIds.Should().Equal(files.Select(f => f.ToString()));

        (await client.UpdateMessageAsync(Guid.Parse(Known.MessageId), "Fixed")).IsEdited.Should().BeTrue();
        await client.DeleteMessageAsync(Guid.Parse(Known.MessageId));
        (await client.GetMessageAsync(Guid.Parse(Known.MessageId))).Id.Should().Be(Guid.Parse(Known.MessageId));
        var missing = () => client.GetMessageAsync(Guid.Parse(Known.Missing));
        (await missing.Should().ThrowAsync<BotApiException>()).Which.StatusCode.Should().Be(StatusCode.NotFound);

        (await client.GetChannelMessagesAsync(channelId)).Should().HaveCount(2);
        var history = h.Recorder.Last<GetChannelMessagesRequest>("history")!;
        (history.Limit, history.BeforeId).Should().Be((50, ""));

        var around = await client.GetMessagesAroundAsync(channelId, Guid.Parse(Known.MessageId), after: 4);
        around.TargetFound.Should().BeTrue();
        (h.Recorder.Last<GetMessagesAroundRequest>("around")!.Before, h.Recorder.Last<GetMessagesAroundRequest>("around")!.After).Should().Be((25, 4));

        var results = await client.SearchMessagesAsync(Guid.Parse(Known.GroupId), new MessageSearch(
            "hello", Has: [SearchFilter.Image, SearchFilter.Link, SearchFilter.Image], After: "2026-01-01", Sort: SearchSort.Newest));
        results.Hits.Should().ContainSingle().Which.Message.Id.Should().Be(Guid.Parse(Known.MessageId));
        results.NextPageToken.Should().Be("p2");
        var search = h.Recorder.Last<SearchMessagesRequest>("search")!;
        search.Sort.Should().Be(SearchMessagesSort.Newest);
        search.Has.Should().Equal(SearchMessagesHas.Image, SearchMessagesHas.Link);
        var badSort = () => client.SearchMessagesAsync(Guid.Parse(Known.GroupId), new MessageSearch(Sort: (SearchSort)42));
        await badSort.Should().ThrowAsync<ArgumentException>();

        var forwarded = await client.ForwardMessageAsync(Guid.Parse(Known.MessageId), [channelId, Guid.Parse(Known.Missing)], "key-1");
        forwarded[0].Success.Should().BeTrue();
        forwarded[1].Error.Should().Be("no permission");
        forwarded[1].Message.Should().BeNull();
        h.Recorder.Last<ForwardMessageRequest>("forward")!.IdempotencyKey.Should().Be("key-1");
        await client.ForwardMessageAsync(Guid.Parse(Known.MessageId), [channelId]);
        h.Recorder.Last<ForwardMessageRequest>("forward")!.IdempotencyKey.Should().NotBeEmpty();

        (await client.AddReactionAsync(Guid.Parse(Known.MessageId), "👍")).Reactions["👍"].Should().Equal(Guid.Parse(Known.Bot));
        await client.RemoveReactionAsync(Guid.Parse(Known.MessageId), "👍");
        await client.TypingAsync(channelId, isTyping: false);
        h.Recorder.Last<TypingRequest>("typing")!.IsTyping.Should().BeFalse();
    }
}

public class AttachmentsApiTests
{
    private static string TempFile(int size, out byte[] data)
    {
        data = new byte[size];
        new Random(size).NextBytes(data);
        var path = Path.Combine(Path.GetTempPath(), $"voice-{Guid.NewGuid()}.bin");
        File.WriteAllBytes(path, data);
        return path;
    }

    [Fact]
    public async Task Upload_ShouldStartPutAndConfirm()
    {
        await using var h = await TestHarness.StartAsync([new FakeAttachments()]);
        var info = await h.Client.UploadAsync("hello.txt", "hello"u8.ToArray(), "text/plain");

        info.Status.Should().Be(AttachmentStatus.Uploaded);
        h.Recorder.Last<InitiateUploadRequest>("start")!.SizeBytes.Should().Be(5);
        var upload = h.Storage.Uploads.Single();
        (upload.Url, upload.ContentType).Should().Be(("https://s3/put/small", "text/plain"));
        upload.Body.Should().Equal("hello"u8.ToArray());
        h.Recorder.Last<ConfirmUploadRequest>("confirm")!.AttachmentId.Should().Be(FakeAttachments.Small);
    }

    [Fact]
    public async Task ManualSteps_ShouldWork()
    {
        await using var h = await TestHarness.StartAsync([new FakeAttachments()]);
        var ticket = await h.Client.StartUploadAsync("big.bin", 1_000_000, "application/octet-stream");
        ticket.AttachmentId.Should().Be(Guid.Parse(FakeAttachments.Small));
        await h.Client.ConfirmUploadAsync(ticket.AttachmentId);
        (await h.Client.GetAttachmentAsync(ticket.AttachmentId)).Status.Should().Be(AttachmentStatus.Pending);
        (await h.Client.GetDownloadUrlAsync(ticket.AttachmentId)).Should().Be("https://cdn/" + FakeAttachments.Small);
        await h.Client.DeleteAttachmentAsync(ticket.AttachmentId);
        h.Storage.Uploads.Should().BeEmpty();
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(BotClient.MinPartSize)]
    public async Task SmallFile_ShouldUseSimpleUpload(int size)
    {
        await using var h = await TestHarness.StartAsync([new FakeAttachments()]);
        var path = TempFile(size, out var data);
        await h.Client.UploadFileAsync(path, "x/y", partSize: BotClient.MinPartSize);

        h.Recorder.Last<InitiateMultipartUploadRequest>("multipart").Should().BeNull();
        h.Recorder.Last<InitiateUploadRequest>("start")!.FileName.Should().Be(Path.GetFileName(path));
        h.Storage.Uploads.Single().Body.Should().Equal(data);
    }

    [Fact]
    public async Task BigFile_ShouldGoInPartsInOrder()
    {
        await using var h = await TestHarness.StartAsync([new FakeAttachments()]);
        var path = TempFile(BotClient.MinPartSize * 2 + 100, out var data);
        var info = await h.Client.UploadFileAsync(path, "video/mp4", "clip.mp4", BotClient.MinPartSize);

        info.IsMultipart.Should().BeTrue();
        var started = h.Recorder.Last<InitiateMultipartUploadRequest>("multipart")!;
        (started.PartCount, started.SizeBytes, started.FileName).Should().Be((3, data.Length, "clip.mp4"));
        h.Storage.Uploads.Select(u => u.Url).Should().Equal("https://s3/part/1", "https://s3/part/2", "https://s3/part/3");
        h.Storage.Uploads.SelectMany(u => u.Body).Should().Equal(data);
        h.Recorder.Last<CompleteMultipartUploadRequest>("complete")!.Parts.Select(p => (p.PartNumber, p.Etag))
            .Should().Equal((1, "\"etag-1\""), (2, "\"etag-2\""), (3, "\"etag-3\""));
        h.Recorder.Last<AbortMultipartUploadRequest>("abort").Should().BeNull();
    }

    [Fact]
    public async Task MissingPartUrls_ShouldBeRequested()
    {
        var attachments = new FakeAttachments { UrlsAtStart = 1 };
        await using var h = await TestHarness.StartAsync([attachments]);
        await h.Client.UploadFileAsync(TempFile(BotClient.MinPartSize * 2 + 1, out _), "x/y", partSize: BotClient.MinPartSize);

        h.Recorder.Last<GetMultipartPartUrlsRequest>("part-urls")!.PartNumbers.Should().Equal(2, 3);
        h.Storage.Uploads.Should().HaveCount(3);
    }

    [Theory]
    [InlineData("part")]
    [InlineData("etag")]
    [InlineData("shrink")]
    [InlineData("grow")]
    public async Task BrokenMultipart_ShouldBeAborted(string failure)
    {
        await using var h = await TestHarness.StartAsync([new FakeAttachments()]);
        var path = TempFile(BotClient.MinPartSize * 2 + 10, out _);
        h.Storage.Respond = n =>
        {
            switch (failure)
            {
                case "part" when n == 2:
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError);
                case "etag":
                    return new HttpResponseMessage(HttpStatusCode.OK);
                case "shrink" when n == 1:
                    File.WriteAllBytes(path, new byte[BotClient.MinPartSize + 5]);
                    break;
                case "grow" when n == 1:
                    using (var file = new FileStream(path, FileMode.Append)) file.Write(new byte[10]);
                    break;
            }

            return null;
        };

        var act = () => h.Client.UploadFileAsync(path, "x/y", partSize: BotClient.MinPartSize);
        await act.Should().ThrowAsync<BotApiException>();
        h.Recorder.Last<AbortMultipartUploadRequest>("abort")!.AttachmentId.Should().Be(FakeAttachments.Big);
        h.Recorder.Last<CompleteMultipartUploadRequest>("complete").Should().BeNull();
    }

    [Fact]
    public async Task CancelledMultipart_ShouldStillBeAborted()
    {
        await using var h = await TestHarness.StartAsync([new FakeAttachments()]);
        h.Storage.Delay = TimeSpan.FromSeconds(10);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var act = () => h.Client.UploadFileAsync(TempFile(BotClient.MinPartSize * 2 + 10, out _), "x/y", partSize: BotClient.MinPartSize, ct: cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
        h.Recorder.Last<AbortMultipartUploadRequest>("abort")!.AttachmentId.Should().Be(FakeAttachments.Big);
    }

    [Fact]
    public async Task UploadTimeout_ShouldFailWithDeadlineExceeded()
    {
        await using var h = await TestHarness.StartAsync([new FakeAttachments()], uploadTimeout: TimeSpan.FromMilliseconds(100));
        h.Storage.Delay = TimeSpan.FromSeconds(5);

        var act = () => h.Client.UploadAsync("a", "x"u8.ToArray(), "x/y");
        (await act.Should().ThrowAsync<BotApiException>()).Which.StatusCode.Should().Be(StatusCode.DeadlineExceeded);
        h.Recorder.Last<ConfirmUploadRequest>("confirm").Should().BeNull();
    }

    [Fact]
    public async Task TooSmallPartSize_ShouldBeRejected()
    {
        await using var h = await TestHarness.StartAsync([new FakeAttachments()]);
        var act = () => h.Client.UploadFileAsync(TempFile(10, out _), "x/y", partSize: 1024);
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }
}

public class SlashCommandApiTests
{
    private static readonly CommandDefinition Ping = new("ping", "Check the connection");

    private static readonly CommandDefinition Weather = new(
        "weather", "Show the weather",
        [new CommandOptionDefinition("city", "Where", CommandOptionType.String, Required: true, Choices: [new CommandChoice("Berlin", "ber")])],
        DefaultMemberPermissions: Permissions.ManageEmojisAndStickers);

    private static BotCommand Existing(string id, CommandDefinition definition)
    {
        var command = new BotCommand
        {
            Id = id, BotId = Known.Bot, Name = definition.Name, Description = definition.Description,
            DefaultMemberPermissions = (ulong)definition.DefaultMemberPermissions,
        };
        command.Options.AddRange((definition.Options ?? []).Select(o => new CommandOption
        {
            Name = o.Name, Description = o.Description, Type = (Vc.Backend.WebApi.Interactions.Grpc.CommandOptionType)(int)o.Type,
            Required = o.Required, Choices = { (o.Choices ?? []).Select(c => new CommandOptionChoice { Name = c.Name, Value = c.Value }) },
        }));
        return command;
    }

    [Fact]
    public async Task Crud_ShouldUseTheBotsOwnId()
    {
        var interactions = new FakeInteractions();
        await using var h = await TestHarness.StartAsync([new FakeUsers(), interactions]);

        var command = await h.Client.RegisterCommandAsync(Weather);
        interactions.Registered.Single().BotId.Should().Be(Known.Bot);
        interactions.Registered.Single().DefaultMemberPermissions.Should().Be(8);
        command.ToDefinition().Should().Be(Weather);

        (await h.Client.GetBotCommandsAsync()).Should().ContainSingle();
        (await h.Client.GetGroupCommandsAsync(Guid.Parse(Known.GroupId))).Single().Name.Should().Be("help");
        await h.Client.UpdateCommandAsync(command.Id, Ping with { Description = "Pong" });
        await h.Client.DeleteCommandAsync(command.Id);
        interactions.Updated.Single().Description.Should().Be("Pong");
        interactions.Deleted.Single().BotId.Should().Be(Known.Bot);
    }

    [Fact]
    public async Task Sync_ShouldCreateUpdateDeleteAndLeaveEqualAlone()
    {
        var interactions = new FakeInteractions();
        interactions.Commands.Add(Existing(Ids.New(), Weather));
        var oldId = Ids.New();
        interactions.Commands.Add(Existing(oldId, new CommandDefinition("old", "x")));
        var pingId = Ids.New();
        interactions.Commands.Add(Existing(pingId, Ping with { Description = "Outdated" }));
        await using var h = await TestHarness.StartAsync([new FakeUsers(), interactions]);

        var result = await h.Client.SyncCommandsAsync([Ping, Weather]);

        result.Created.Should().BeEmpty();
        result.Updated.Single().Id.Should().Be(Guid.Parse(pingId));
        result.Deleted.Single().Id.Should().Be(Guid.Parse(oldId));
        interactions.Registered.Should().BeEmpty();
        interactions.Updated.Should().ContainSingle();
    }

    [Fact]
    public async Task Sync_ShouldRefuseDuplicates()
    {
        await using var h = await TestHarness.StartAsync([new FakeUsers(), new FakeInteractions()]);
        var act = () => h.Client.SyncCommandsAsync([Ping, Ping]);
        await act.Should().ThrowAsync<ArgumentException>();
    }
}
