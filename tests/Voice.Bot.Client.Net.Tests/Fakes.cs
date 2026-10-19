using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Vc.Backend.Contracts.Grpc;
using Vc.Backend.WebApi.Attachments.Grpc;
using Vc.Backend.WebApi.Channels.Grpc;
using Vc.Backend.WebApi.Groups.Grpc;
using Vc.Backend.WebApi.Interactions.Grpc;
using Vc.Backend.WebApi.Messages.Grpc;
using Vc.Backend.WebApi.Users.Grpc;
using Channel = Vc.Backend.WebApi.Groups.Grpc.Channel;
using Group = Vc.Backend.WebApi.Groups.Grpc.Group;
using Message = Vc.Backend.WebApi.Messages.Grpc.Message;
using User = Vc.Backend.Contracts.Grpc.User;

namespace Voice.Bot.Client.Net.Tests;

public static class Known
{
    public static readonly string Bot = Guid.NewGuid().ToString();
    public static readonly string GroupId = Guid.NewGuid().ToString();
    public static readonly string ChannelId = Guid.NewGuid().ToString();
    public static readonly string CategoryId = Guid.NewGuid().ToString();
    public static readonly string UserId = Guid.NewGuid().ToString();
    public static readonly string RoleId = Guid.NewGuid().ToString();
    public static readonly string MessageId = Guid.NewGuid().ToString();
    public static readonly string Missing = Guid.NewGuid().ToString();
}

internal sealed class FakeUsers : UsersApi.UsersApiBase, FakeService
{
    public Recorder Recorder { get; set; } = null!;
    public int GetMeCalls;
    public bool Fail;
    public TimeSpan Delay;

    public override async Task<UserResponse> GetMe(Empty request, ServerCallContext context)
    {
        Interlocked.Increment(ref GetMeCalls);
        Recorder.Record("me", request, context);
        if (Delay > TimeSpan.Zero) await Task.Delay(Delay);
        if (Fail) throw new RpcException(new Status(StatusCode.Unauthenticated, "token revoked"));
        return new UserResponse { Id = Known.Bot, Username = "weather", IsBot = true, UserStatus = Vc.Backend.WebApi.Users.Grpc.UserStatus.DoNotDisturb };
    }

    public override Task<UserResponse> UpdateUser(UpdateUserRequest request, ServerCallContext context)
    {
        Recorder.Record("update", request, context);
        return Task.FromResult(new UserResponse { Id = Known.Bot, DisplayName = request.DisplayName });
    }

    public override Task<UserResponse> UpdateUserStatus(UpdateUserStatusRequest request, ServerCallContext context)
    {
        Recorder.Record("status", request, context);
        return Task.FromResult(new UserResponse { Id = Known.Bot, UserStatus = request.UserStatus });
    }

    public override Task<GetUserAvatarResponse> GetUserAvatar(GetUserAvatarRequest request, ServerCallContext context) =>
        Task.FromResult(new GetUserAvatarResponse { AvatarUrl = request.UserId == Known.Missing ? "" : "https://cdn/" + request.UserId });

    public override Task<InitiateAvatarUploadResponse> InitiateAvatarUpload(InitiateAvatarUploadRequest request, ServerCallContext context)
    {
        Recorder.Record("avatar", request, context);
        return Task.FromResult(new InitiateAvatarUploadResponse { AvatarId = "av1", UploadUrl = "https://s3/avatar" });
    }

    public override Task<UserResponse> ConfirmAvatarUpload(ConfirmAvatarUploadRequest request, ServerCallContext context)
    {
        Recorder.Record("confirm-avatar", request, context);
        return Task.FromResult(new UserResponse { Id = Known.Bot, AvatarUrl = "https://cdn/av1" });
    }

    public override Task<InitiateBannerUploadResponse> InitiateBannerUpload(InitiateBannerUploadRequest request, ServerCallContext context) =>
        Task.FromResult(new InitiateBannerUploadResponse { BannerId = "bn1", UploadUrl = "https://s3/banner" });

    public override Task<UserResponse> ConfirmBannerUpload(ConfirmBannerUploadRequest request, ServerCallContext context)
    {
        Recorder.Record("confirm-banner", request, context);
        return Task.FromResult(new UserResponse { Id = Known.Bot });
    }

    public override Task<UserResponse> DeleteAvatar(Empty request, ServerCallContext context) => Task.FromResult(new UserResponse { Id = Known.Bot });

    public override Task<UserResponse> DeleteBanner(Empty request, ServerCallContext context) => Task.FromResult(new UserResponse { Id = Known.Bot });
}

internal sealed class FakeGroups : GroupsApi.GroupsApiBase, FakeService
{
    public Recorder Recorder { get; set; } = null!;

    private static User Person(string id) => new() { Id = id, Username = "user" };

    public override Task<GetUserGroupsResponse> GetUserGroups(GetUserGroupsRequest request, ServerCallContext context) =>
        Task.FromResult(new GetUserGroupsResponse
        {
            Groups = { new Group { Id = Known.GroupId, OwnerId = Known.UserId, Name = "Club", AccessType = Vc.Backend.WebApi.Groups.Grpc.AccessType.InviteOnly } },
        });

    public override Task<GetGroupResponse> GetGroup(GetGroupRequest request, ServerCallContext context)
    {
        if (request.Id == Known.Missing) throw new RpcException(new Status(StatusCode.NotFound, "no such group"));
        var details = new GroupDetails
        {
            Id = request.Id, OwnerId = Known.Bot, Name = "Club", VerificationLevel = Vc.Backend.WebApi.Groups.Grpc.VerificationLevel.Medium, IAmIsOwner = true,
            CreatedAt = Timestamp.FromDateTimeOffset(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero)),
        };
        details.Categories.Add(new Category { Id = Known.CategoryId, Name = "General", Channels = { new Channel { Id = Known.ChannelId, Name = "chat", Type = 0, CategoryId = Known.CategoryId } } });
        var voice = new Channel { Id = Ids.New(), Name = "voice", Type = 1, UserLimit = 10 };
        voice.Users.Add(new ChannelVoiceUser { User = Person(Known.UserId), MicIsEnabled = true });
        details.Categories.Add(new Category { Id = Ids.New(), Name = "Voice", Channels = { voice } });
        return Task.FromResult(new GetGroupResponse { Group = details });
    }

    public override Task<Group> UpdateGroup(UpdateGroupRequest request, ServerCallContext context)
    {
        Recorder.Record("update-group", request, context);
        return Task.FromResult(new Group { Id = request.Id, OwnerId = Known.Bot, Name = request.Name, AccessType = request.AccessType });
    }

    public override Task<LeaveGroupResponse> LeaveGroup(LeaveGroupRequest request, ServerCallContext context)
    {
        Recorder.Record("leave", request, context);
        return Task.FromResult(new LeaveGroupResponse { Success = true });
    }

    public override Task<JoinResponse> JoinByInvite(JoinByInviteRequest request, ServerCallContext context) =>
        Task.FromResult(request.InviteCode == "refused" ? new JoinResponse() : new JoinResponse { GroupId = Known.GroupId });

    public override Task<GetGroupUsersResponse> GetGroupUsers(GetGroupUsersRequest request, ServerCallContext context)
    {
        Recorder.Record("members", request, context);
        return Task.FromResult(new GetGroupUsersResponse
        {
            Members = { new GroupMember { User = Person(Known.UserId), RoleIds = { Known.RoleId } }, new GroupMember() },
            NextPageToken = "next",
        });
    }

    public override Task<GetChannelMemberListResponse> GetChannelMemberList(GetChannelMemberListRequest request, ServerCallContext context)
    {
        Recorder.Record("channel-members", request, context);
        return Task.FromResult(new GetChannelMemberListResponse
        {
            Sections = { new MemberListSection { Key = "role", RoleId = Known.RoleId, Count = 2 }, new MemberListSection { Key = "online", Count = 5 } },
            Items = { new MemberListItem { SectionKey = "online" } },
            Total = 7,
            OfflineHidden = true,
        });
    }

    public override Task<GetEffectivePermissionsResponse> GetEffectivePermissions(GetEffectivePermissionsRequest request, ServerCallContext context)
    {
        Recorder.Record("permissions", request, context);
        return Task.FromResult(new GetEffectivePermissionsResponse { Permissions = 10 });
    }

    public override Task<KickUserResponse> KickUser(KickUserRequest request, ServerCallContext context)
    {
        Recorder.Record("kick", request, context);
        return Task.FromResult(new KickUserResponse { Success = true });
    }

    public override Task<BanUserResponse> BanUser(BanUserRequest request, ServerCallContext context)
    {
        Recorder.Record("ban", request, context);
        return Task.FromResult(new BanUserResponse { Success = true });
    }

    public override Task<UnbanUserResponse> UnbanUser(UnbanUserRequest request, ServerCallContext context)
    {
        Recorder.Record("unban", request, context);
        return Task.FromResult(new UnbanUserResponse { Success = true });
    }

    public override Task<GetGroupBansResponse> GetGroupBans(GetGroupBansRequest request, ServerCallContext context) =>
        Task.FromResult(new GetGroupBansResponse { Bans = { new GroupBan { User = Person(Known.UserId), Reason = "spam" }, new GroupBan() } });

    public override Task<GetGroupInvitesResponse> GetGroupInvites(GetGroupInvitesRequest request, ServerCallContext context) =>
        Task.FromResult(new GetGroupInvitesResponse
        {
            Invites = { new Invite { Id = Ids.New(), GroupId = Known.GroupId, OwnerId = Known.Bot, InviteCode = "abc", MaxUseAmount = 5 } },
        });

    public override Task<Invite> CreateInvite(CreateInviteRequest request, ServerCallContext context)
    {
        Recorder.Record("create-invite", request, context);
        return Task.FromResult(new Invite { Id = Ids.New(), GroupId = request.GroupId, OwnerId = Known.Bot, ExpiresAt = request.ExpiresAt, MaxUseAmount = request.MaxUses });
    }

    public override Task<Invite> UpdateInvite(UpdateInviteRequest request, ServerCallContext context)
    {
        Recorder.Record("update-invite", request, context);
        return Task.FromResult(new Invite { Id = request.Id, GroupId = Known.GroupId, OwnerId = Known.Bot, MaxUseAmount = request.MaxUses });
    }

    public override Task<Empty> DeleteInvite(DeleteInviteRequest request, ServerCallContext context)
    {
        Recorder.Record("delete-invite", request, context);
        return Task.FromResult(new Empty());
    }

    public override Task<GetGroupAuditLogResponse> GetGroupAuditLog(GetGroupAuditLogRequest request, ServerCallContext context)
    {
        Recorder.Record("audit", request, context);
        return Task.FromResult(new GetGroupAuditLogResponse
        {
            Entries = { new GroupAuditLogEntry { Id = Ids.New(), ActorId = Known.UserId, TargetId = "target" }, new GroupAuditLogEntry { Id = Ids.New() } },
        });
    }

    public override Task<GetGroupRolesResponse> GetGroupRoles(GetGroupRolesRequest request, ServerCallContext context)
    {
        Recorder.Record("roles", request, context);
        return Task.FromResult(new GetGroupRolesResponse { Roles = { new Role { Id = Known.RoleId, GroupId = Known.GroupId, Name = "mods", Permissions = 8, Color = "#ff0000" } } });
    }

    public override Task<Role> CreateRole(CreateRoleRequest request, ServerCallContext context)
    {
        Recorder.Record("create-role", request, context);
        return Task.FromResult(new Role { Id = Ids.New(), GroupId = request.GroupId, Name = request.Name, Permissions = request.Permissions, Hoist = request.Hoist });
    }

    public override Task<Role> UpdateRole(UpdateRoleRequest request, ServerCallContext context)
    {
        Recorder.Record("update-role", request, context);
        return Task.FromResult(new Role { Id = request.RoleId, GroupId = Known.GroupId, Name = request.Name });
    }

    public override Task<Role> MoveRole(MoveRoleRequest request, ServerCallContext context)
    {
        Recorder.Record("move-role", request, context);
        return Task.FromResult(new Role { Id = request.RoleId, GroupId = Known.GroupId });
    }

    public override Task<DeleteRoleResponse> DeleteRole(DeleteRoleRequest request, ServerCallContext context)
    {
        Recorder.Record("delete-role", request, context);
        return Task.FromResult(new DeleteRoleResponse { Success = true });
    }

    public override Task<Empty> AssignRole(RoleAssignmentRequest request, ServerCallContext context)
    {
        Recorder.Record("assign", request, context);
        return Task.FromResult(new Empty());
    }

    public override Task<Empty> RemoveRole(RoleAssignmentRequest request, ServerCallContext context)
    {
        Recorder.Record("remove", request, context);
        return Task.FromResult(new Empty());
    }

    public override Task<Category> CreateCategory(CreateCategoryRequest request, ServerCallContext context) =>
        Task.FromResult(new Category { Id = Known.CategoryId, Name = request.Name });

    public override Task<Category> UpdateCategory(UpdateCategoryRequest request, ServerCallContext context) =>
        Task.FromResult(new Category { Id = request.Id, Name = request.Name });

    public override Task<DeleteCategoryResponse> DeleteCategory(DeleteCategoryRequest request, ServerCallContext context)
    {
        Recorder.Record("delete-category", request, context);
        return Task.FromResult(new DeleteCategoryResponse { Success = true });
    }

    public override Task<Category> MoveCategory(MoveCategoryRequest request, ServerCallContext context)
    {
        Recorder.Record("move-category", request, context);
        return Task.FromResult(new Category { Id = request.CategoryId });
    }

    public override Task<Channel> CreateChannel(CreateChannelRequest request, ServerCallContext context)
    {
        Recorder.Record("create-channel", request, context);
        return Task.FromResult(new Channel { Id = Known.ChannelId, Name = request.Name, Type = request.Type });
    }

    public override Task<DeleteChannelResponse> DeleteChannel(DeleteChannelRequest request, ServerCallContext context)
    {
        Recorder.Record("delete-channel", request, context);
        return Task.FromResult(new DeleteChannelResponse { Success = true });
    }

    public override Task<Channel> MoveChannel(MoveChannelRequest request, ServerCallContext context)
    {
        Recorder.Record("move-channel", request, context);
        return Task.FromResult(new Channel { Id = request.ChannelId, CategoryId = request.TargetCategoryId });
    }

    public override Task<Channel> SetChannelCategorySync(SetChannelCategorySyncRequest request, ServerCallContext context) =>
        Task.FromResult(new Channel { Id = request.ChannelId, IsCategoryPermissionsSync = request.IsSynced });

    public override Task<GetChannelPermissionOverwritesResponse> GetChannelPermissionOverwrites(GetChannelPermissionOverwritesRequest request, ServerCallContext context) =>
        Task.FromResult(new GetChannelPermissionOverwritesResponse { Overwrites = { new ChannelPermissionOverwrite { TargetId = Known.RoleId, IsRoleTarget = true, Allow = 1, Deny = 2 } } });

    public override Task<Empty> SetChannelPermissionOverwrite(SetChannelPermissionOverwriteRequest request, ServerCallContext context)
    {
        Recorder.Record("set-overwrite", request, context);
        return Task.FromResult(new Empty());
    }

    public override Task<Empty> DeleteChannelPermissionOverwrite(DeleteChannelPermissionOverwriteRequest request, ServerCallContext context)
    {
        Recorder.Record("delete-overwrite", request, context);
        return Task.FromResult(new Empty());
    }

    public override Task<GetCategoryPermissionOverwritesResponse> GetCategoryPermissionOverwrites(GetCategoryPermissionOverwritesRequest request, ServerCallContext context) =>
        Task.FromResult(new GetCategoryPermissionOverwritesResponse { Overwrites = { new CategoryPermissionOverwrite { TargetId = Known.UserId, Allow = 4 } } });

    public override Task<Empty> SetCategoryPermissionOverwrite(SetCategoryPermissionOverwriteRequest request, ServerCallContext context)
    {
        Recorder.Record("set-category-overwrite", request, context);
        return Task.FromResult(new Empty());
    }

    public override Task<Empty> DeleteCategoryPermissionOverwrite(DeleteCategoryPermissionOverwriteRequest request, ServerCallContext context)
    {
        Recorder.Record("delete-category-overwrite", request, context);
        return Task.FromResult(new Empty());
    }
}

internal sealed class FakeChannels : ChannelsApi.ChannelsApiBase, FakeService
{
    public Recorder Recorder { get; set; } = null!;

    public override Task<UpdateChannelResponse> UpdateChannel(UpdateChannelRequest request, ServerCallContext context)
    {
        Recorder.Record("update-channel", request, context);
        return Task.FromResult(new UpdateChannelResponse { Success = true });
    }

    public override Task<GetChannelUsersResponse> GetChannelUsers(GetChannelUsersRequest request, ServerCallContext context) =>
        Task.FromResult(new GetChannelUsersResponse { Users = { new ChannelVoiceUser { User = new User { Id = Known.UserId }, IsDeafened = true }, new ChannelVoiceUser() } });

    public override Task<StartChannelConnectionResponse> StartChannelConnection(StartChannelConnectionRequest request, ServerCallContext context) =>
        Task.FromResult(new StartChannelConnectionResponse { ServerUrl = "wss://livekit", Token = "lk-" + request.ChannelId });
}

internal sealed class FakeMessages : MessagesApi.MessagesApiBase, FakeService
{
    public Recorder Recorder { get; set; } = null!;

    public static Message Echo(string id, string? channelId = null, string content = "") =>
        new() { Id = id, ChannelId = channelId ?? Known.ChannelId, Content = content, Author = new User { Id = Known.Bot, IsBot = true } };

    public override Task<Message> CreateMessage(CreateMessageRequest request, ServerCallContext context)
    {
        Recorder.Record("create", request, context);
        return Task.FromResult(Echo(Known.MessageId, request.ChannelId, request.Content));
    }

    public override Task<Message> UpdateMessage(UpdateMessageRequest request, ServerCallContext context)
    {
        var message = Echo(request.Id, content: request.Content);
        message.IsEdited = true;
        return Task.FromResult(message);
    }

    public override Task<DeleteMessageResponse> DeleteMessage(DeleteMessageRequest request, ServerCallContext context)
    {
        Recorder.Record("delete", request, context);
        return Task.FromResult(new DeleteMessageResponse { Success = true });
    }

    public override Task<Message> GetMessage(GetMessageRequest request, ServerCallContext context) =>
        request.MessageId == Known.Missing
            ? throw new RpcException(new Status(StatusCode.NotFound, "no such message"))
            : Task.FromResult(Echo(request.MessageId));

    public override Task<GetChannelMessagesResponse> GetChannelMessages(GetChannelMessagesRequest request, ServerCallContext context)
    {
        Recorder.Record("history", request, context);
        return Task.FromResult(new GetChannelMessagesResponse { Messages = { Echo(Ids.New()), Echo(Ids.New()) } });
    }

    public override Task<GetMessagesAroundResponse> GetMessagesAround(GetMessagesAroundRequest request, ServerCallContext context)
    {
        Recorder.Record("around", request, context);
        return Task.FromResult(new GetMessagesAroundResponse { Messages = { Echo(request.MessageId) }, TargetFound = true });
    }

    public override Task<SearchMessagesResponse> SearchMessages(SearchMessagesRequest request, ServerCallContext context)
    {
        Recorder.Record("search", request, context);
        return Task.FromResult(new SearchMessagesResponse
        {
            Hits = { new SearchMessageHit { Message = Echo(Known.MessageId, content: "x"), Highlights = { "x" } }, new SearchMessageHit() },
            Total = 1,
            NextPageToken = "p2",
        });
    }

    public override Task<ForwardMessageResponse> ForwardMessage(ForwardMessageRequest request, ServerCallContext context)
    {
        Recorder.Record("forward", request, context);
        var response = new ForwardMessageResponse();
        foreach (var channelId in request.DestinationChannelIds)
        {
            response.Results.Add(channelId == Known.Missing
                ? new ForwardMessageResult { ChannelId = channelId, ErrorMessage = "no permission" }
                : new ForwardMessageResult { ChannelId = channelId, Success = true, Message = Echo(Ids.New(), channelId) });
        }

        return Task.FromResult(response);
    }

    public override Task<Message> AddReaction(ReactionRequest request, ServerCallContext context)
    {
        var message = Echo(request.MessageId);
        message.Reactions[request.Emoji] = new ReactionUsers { UserIds = { Known.Bot } };
        return Task.FromResult(message);
    }

    public override Task<Message> RemoveReaction(ReactionRequest request, ServerCallContext context)
    {
        Recorder.Record("remove-reaction", request, context);
        return Task.FromResult(Echo(request.MessageId));
    }

    public override Task<TypingResponse> Typing(TypingRequest request, ServerCallContext context)
    {
        Recorder.Record("typing", request, context);
        return Task.FromResult(new TypingResponse { Success = true });
    }
}

internal sealed class FakeAttachments : AttachmentsApi.AttachmentsApiBase, FakeService
{
    public Recorder Recorder { get; set; } = null!;
    public static readonly string Small = Guid.NewGuid().ToString();
    public static readonly string Big = Guid.NewGuid().ToString();
    public int? UrlsAtStart;
    public string UploadUrl = "https://s3/put/small";

    public override Task<InitiateUploadResponse> InitiateUpload(InitiateUploadRequest request, ServerCallContext context)
    {
        Recorder.Record("start", request, context);
        return Task.FromResult(new InitiateUploadResponse { AttachmentId = Small, UploadUrl = UploadUrl });
    }

    public override Task<AttachmentResponse> ConfirmUpload(ConfirmUploadRequest request, ServerCallContext context)
    {
        Recorder.Record("confirm", request, context);
        return Task.FromResult(new AttachmentResponse { Id = request.AttachmentId, Status = AttachmentStatus.Uploaded });
    }

    public override Task<AttachmentResponse> GetAttachment(GetAttachmentRequest request, ServerCallContext context) =>
        Task.FromResult(new AttachmentResponse { Id = request.AttachmentId, Status = AttachmentStatus.Pending });

    public override Task<GetDownloadUrlResponse> GetDownloadUrl(GetDownloadUrlRequest request, ServerCallContext context) =>
        Task.FromResult(new GetDownloadUrlResponse { DownloadUrl = "https://cdn/" + request.AttachmentId });

    public override Task<Empty> DeleteAttachment(DeleteAttachmentRequest request, ServerCallContext context)
    {
        Recorder.Record("delete", request, context);
        return Task.FromResult(new Empty());
    }

    private static MultipartPartUrl Part(int n) => new() { PartNumber = n, Url = $"https://s3/part/{n}" };

    public override Task<InitiateMultipartUploadResponse> InitiateMultipartUpload(InitiateMultipartUploadRequest request, ServerCallContext context)
    {
        Recorder.Record("multipart", request, context);
        var response = new InitiateMultipartUploadResponse { AttachmentId = Big };
        response.PartUrls.AddRange(Enumerable.Range(1, Math.Min(request.PartCount, UrlsAtStart ?? request.PartCount)).Select(Part));
        return Task.FromResult(response);
    }

    public override Task<GetMultipartPartUrlsResponse> GetMultipartPartUrls(GetMultipartPartUrlsRequest request, ServerCallContext context)
    {
        Recorder.Record("part-urls", request, context);
        var response = new GetMultipartPartUrlsResponse();
        response.PartUrls.AddRange(request.PartNumbers.Select(Part));
        return Task.FromResult(response);
    }

    public override Task<AttachmentResponse> CompleteMultipartUpload(CompleteMultipartUploadRequest request, ServerCallContext context)
    {
        Recorder.Record("complete", request, context);
        return Task.FromResult(new AttachmentResponse { Id = request.AttachmentId, IsMultipart = true });
    }

    public override Task<Empty> AbortMultipartUpload(AbortMultipartUploadRequest request, ServerCallContext context)
    {
        Recorder.Record("abort", request, context);
        return Task.FromResult(new Empty());
    }
}

internal sealed class FakeInteractions : InteractionsApi.InteractionsApiBase, FakeService
{
    public Recorder Recorder { get; set; } = null!;
    public readonly List<BotCommand> Commands = [];
    public readonly List<RegisterCommandRequest> Registered = [];
    public readonly List<UpdateCommandRequest> Updated = [];
    public readonly List<DeleteCommandRequest> Deleted = [];

    public override Task<GetBotCommandsResponse> GetBotCommands(GetBotCommandsRequest request, ServerCallContext context)
    {
        Recorder.Record("list", request, context);
        return Task.FromResult(new GetBotCommandsResponse { Commands = { Commands } });
    }

    public override Task<GetBotCommandsResponse> GetGroupCommands(GetGroupCommandsRequest request, ServerCallContext context) =>
        Task.FromResult(new GetBotCommandsResponse { Commands = { new BotCommand { Id = Ids.New(), BotId = Known.UserId, Name = "help" } } });

    public override Task<BotCommand> RegisterCommand(RegisterCommandRequest request, ServerCallContext context)
    {
        lock (Registered) Registered.Add(request);
        var command = new BotCommand
        {
            Id = Ids.New(), BotId = request.BotId, Name = request.Name, Description = request.Description,
            Options = { request.Options }, DefaultMemberPermissions = request.DefaultMemberPermissions,
        };
        Commands.Add(command);
        return Task.FromResult(command);
    }

    public override Task<BotCommand> UpdateCommand(UpdateCommandRequest request, ServerCallContext context)
    {
        lock (Updated) Updated.Add(request);
        return Task.FromResult(new BotCommand
        {
            Id = request.CommandId, BotId = request.BotId, Name = request.Name, Description = request.Description,
            Options = { request.Options }, DefaultMemberPermissions = request.DefaultMemberPermissions,
        });
    }

    public override Task<Empty> DeleteCommand(DeleteCommandRequest request, ServerCallContext context)
    {
        lock (Deleted) Deleted.Add(request);
        return Task.FromResult(new Empty());
    }
}
