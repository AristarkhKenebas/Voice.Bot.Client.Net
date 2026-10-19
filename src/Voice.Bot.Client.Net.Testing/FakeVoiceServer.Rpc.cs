using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Vc.Backend.WebApi.Attachments.Grpc;
using Vc.Backend.WebApi.Groups.Grpc;
using Vc.Backend.WebApi.Interactions.Grpc;
using Vc.Backend.WebApi.Messages.Grpc;
using Vc.Backend.WebApi.Users.Grpc;
using Voice.Bot.Client.Net.Gateway;
using Voice.Bot.Client.Net.Models;
using WireAttachmentStatus = Vc.Backend.WebApi.Attachments.Grpc.AttachmentStatus;
using WireCategory = Vc.Backend.WebApi.Groups.Grpc.Category;
using WireChannel = Vc.Backend.WebApi.Groups.Grpc.Channel;
using WireGroup = Vc.Backend.WebApi.Groups.Grpc.Group;
using WireGroupBan = Vc.Backend.WebApi.Groups.Grpc.GroupBan;
using WireGroupDetails = Vc.Backend.WebApi.Groups.Grpc.GroupDetails;
using WireGroupMember = Vc.Backend.WebApi.Groups.Grpc.GroupMember;
using WireMessage = Vc.Backend.WebApi.Messages.Grpc.Message;
using WireRole = Vc.Backend.WebApi.Groups.Grpc.Role;

namespace Voice.Bot.Client.Net.Testing;

/// <summary>The fake API methods, answering from the in-memory world.</summary>
public sealed partial class FakeVoiceServer
{
    private const string Users = "users.UsersApi";
    private const string Groups = "groups.GroupsApi";
    private const string Messages = "messages.MessagesApi";
    private const string Attachments = "attachments.AttachmentsApi";
    private const string Interactions = "interactions.InteractionsApi";
    private const string StorageUrl = "https://storage.fake/";

    private readonly Dictionary<Guid, PendingUpload> _pending = [];
    private readonly Dictionary<string, byte[]> _stored = [];
    private readonly List<BotCommand> _commands = [];

    /// <summary>The slash commands the bot has registered.</summary>
    public IReadOnlyList<CommandDefinition> RegisteredCommands
    {
        get
        {
            lock (_lock) return _commands.Select(c => c.ToModel().ToDefinition()).ToList();
        }
    }

    private void RegisterHandlers(FakeCallInvoker invoker)
    {
        invoker.Handle<Empty, UserResponse>(Users, "GetMe", _ => Locked(() =>
        {
            var bot = _users[BotId];
            return new UserResponse { Id = BotId.ToString(), Username = bot.Username, IsBot = true };
        }));
        invoker.Handle<UpdateUserRequest, UserResponse>(Users, "UpdateUser", r => Locked(() =>
            new UserResponse { Id = BotId.ToString(), Username = _users[BotId].Username, IsBot = true, DisplayName = r.DisplayName, Status = r.Status }));
        invoker.Handle<UpdateUserStatusRequest, UserResponse>(Users, "UpdateUserStatus", r =>
            new UserResponse { Id = BotId.ToString(), IsBot = true, UserStatus = r.UserStatus });

        RegisterMessages(invoker);
        RegisterGroups(invoker);
        RegisterAttachments(invoker);
        RegisterInteractions(invoker);
    }

    private void RegisterMessages(FakeCallInvoker invoker)
    {
        invoker.Handle<CreateMessageRequest, WireMessage>(Messages, "CreateMessage", r =>
        {
            WireMessage wire;
            Guid? groupId;
            lock (_lock)
            {
                var channelId = ParseId(r.ChannelId);
                groupId = GroupOf(channelId);
                wire = NewMessage(BotId, channelId, r.Content, string.IsNullOrEmpty(r.ReplyTo) ? null : ParseId(r.ReplyTo));
                foreach (var id in r.AttachmentIds)
                {
                    var file = _uploads.FirstOrDefault(u => u.AttachmentId.ToString() == id)
                        ?? throw new RpcException(new Status(StatusCode.NotFound, $"No uploaded attachment {id}."));
                    wire.Attachments.Add(new Vc.Backend.WebApi.Messages.Grpc.Attachment
                    {
                        Id = id, UploaderId = BotId.ToString(), FileName = file.FileName, ContentType = file.ContentType,
                        SizeBytes = file.Data.Length, Status = "Uploaded", DownloadUrl = StorageUrl + id,
                    });
                }

                _sent.Add(wire.ToModel());
            }

            if (EchoBotMessages)
            {
                var payload = MessagePayload(wire);
                FakeCallInvoker.AfterResponse(() => Deliver(GatewayTopics.ChatEvents, "MessageCreated", groupId, payload));
            }

            return wire.Clone();
        });
        invoker.Handle<UpdateMessageRequest, WireMessage>(Messages, "UpdateMessage", r => Locked(() =>
        {
            var message = Message(r.Id);
            if (message.Author.Id != BotId.ToString()) throw new RpcException(new Status(StatusCode.PermissionDenied, "Only the author can edit a message."));
            message.Content = r.Content;
            message.IsEdited = true;
            _edited.Add(new EditedMessage(ParseId(r.Id), r.Content));
            return message.Clone();
        }));
        invoker.Handle<DeleteMessageRequest, DeleteMessageResponse>(Messages, "DeleteMessage", r => Locked(() =>
        {
            Message(r.Id);
            _messages.Remove(ParseId(r.Id));
            _deleted.Add(ParseId(r.Id));
            return new DeleteMessageResponse { Id = r.Id, Success = true };
        }));
        invoker.Handle<GetMessageRequest, WireMessage>(Messages, "GetMessage", r => Locked(() => Message(r.MessageId).Clone()));
        invoker.Handle<GetChannelMessagesRequest, GetChannelMessagesResponse>(Messages, "GetChannelMessages", r => Locked(() =>
        {
            var inChannel = _messageOrder.Where(_messages.ContainsKey).Select(id => _messages[id]).Where(m => m.ChannelId == r.ChannelId).ToList();
            if (!string.IsNullOrEmpty(r.BeforeId))
            {
                var index = inChannel.FindIndex(m => m.Id == r.BeforeId);
                inChannel = index < 0 ? [] : inChannel[..index];
            }

            var limit = r.Limit > 0 ? r.Limit : 50;
            var response = new GetChannelMessagesResponse();
            response.Messages.AddRange(inChannel.Skip(Math.Max(0, inChannel.Count - limit)).Select(m => m.Clone()));
            return response;
        }));
        invoker.Handle<ReactionRequest, WireMessage>(Messages, "AddReaction", r => Locked(() =>
        {
            var message = Message(r.MessageId);
            Toggle(message, r.Emoji, BotId, added: true);
            _reactions.Add(new SentReaction(ParseId(r.MessageId), r.Emoji));
            return message.Clone();
        }));
        invoker.Handle<ReactionRequest, WireMessage>(Messages, "RemoveReaction", r => Locked(() =>
        {
            var message = Message(r.MessageId);
            Toggle(message, r.Emoji, BotId, added: false);
            _removedReactions.Add(new SentReaction(ParseId(r.MessageId), r.Emoji));
            return message.Clone();
        }));
        invoker.Handle<TypingRequest, TypingResponse>(Messages, "Typing", r => Locked(() =>
        {
            _typing.Add(new TypingSignal(ParseId(r.ChannelId), r.IsTyping));
            return new TypingResponse { Success = true };
        }));
    }

    private void RegisterGroups(FakeCallInvoker invoker)
    {
        invoker.Handle<GetUserGroupsRequest, GetUserGroupsResponse>(Groups, "GetUserGroups", _ => Locked(() =>
        {
            var response = new GetUserGroupsResponse();
            response.Groups.AddRange(_groups.Values.Where(g => g.Members.ContainsKey(BotId))
                .Select(g => new WireGroup { Id = g.Id.ToString(), Name = g.Name, OwnerId = g.OwnerId.ToString() }));
            return response;
        }));
        invoker.Handle<GetGroupRequest, GetGroupResponse>(Groups, "GetGroup", r => Locked(() =>
        {
            var group = GroupById(r.Id);
            var category = new WireCategory { Id = group.CategoryId.ToString(), Name = "Channels", Position = "a" };
            category.Channels.AddRange(group.Channels.Select((c, i) => new WireChannel
            {
                Id = c.Id.ToString(), Name = c.Name, Type = (int)c.Type, CategoryId = group.CategoryId.ToString(), Position = i.ToString("D4", null),
            }));
            var details = new WireGroupDetails { Id = group.Id.ToString(), Name = group.Name, OwnerId = group.OwnerId.ToString(), IAmIsOwner = group.OwnerId == BotId };
            details.Categories.Add(category);
            return new GetGroupResponse { Group = details };
        }));
        invoker.Handle<GetGroupUsersRequest, GetGroupUsersResponse>(Groups, "GetGroupUsers", r => Locked(() =>
        {
            var members = GroupById(r.GroupId).Members
                .Where(m => string.IsNullOrEmpty(r.Query) || _users.TryGetValue(m.Key, out var u) && u.Username.Contains(r.Query, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var offset = int.TryParse(r.PageToken, out var token) ? token : 0;
            var size = r.PageSize > 0 ? r.PageSize : 50;
            var response = new GetGroupUsersResponse { NextPageToken = offset + size < members.Count ? (offset + size).ToString(null, null) : "" };
            response.Members.AddRange(members.Skip(offset).Take(size).Select(m =>
            {
                var member = new WireGroupMember { User = WireUserOf(m.Key) };
                member.RoleIds.AddRange(m.Value.Select(id => id.ToString()));
                return member;
            }));
            return response;
        }));
        invoker.Handle<GetGroupRolesRequest, GetGroupRolesResponse>(Groups, "GetGroupRoles", r => Locked(() =>
        {
            var group = GroupById(r.GroupId);
            var response = new GetGroupRolesResponse();
            response.Roles.AddRange(group.Roles.Select((role, i) => WireRoleOf(group, role, i)));
            return response;
        }));
        invoker.Handle<CreateRoleRequest, WireRole>(Groups, "CreateRole", r => Locked(() =>
        {
            var group = GroupById(r.GroupId);
            var role = new FakeRole(Guid.NewGuid(), r.Name, (Permissions)(ulong)r.Permissions);
            group.Roles.Add(role);
            SendLater(GatewayTopics.GroupEvents, "RoleCreated", group.Id, new { });
            return WireRoleOf(group, role, group.Roles.Count - 1);
        }));
        invoker.Handle<RoleAssignmentRequest, Empty>(Groups, "AssignRole", r => Locked(() =>
        {
            GiveRole(ParseId(r.TargetUserId), ParseId(r.RoleId), ParseId(r.GroupId));
            SendLater(GatewayTopics.GroupEvents, "RoleAssigned", ParseId(r.GroupId), new { });
            return new Empty();
        }));
        invoker.Handle<RoleAssignmentRequest, Empty>(Groups, "RemoveRole", r => Locked(() =>
        {
            if (GroupById(r.GroupId).Members.TryGetValue(ParseId(r.TargetUserId), out var roles)) roles.Remove(ParseId(r.RoleId));
            SendLater(GatewayTopics.GroupEvents, "RoleRemoved", ParseId(r.GroupId), new { });
            return new Empty();
        }));
        invoker.Handle<GetEffectivePermissionsRequest, GetEffectivePermissionsResponse>(Groups, "GetEffectivePermissions", r => Locked(() =>
        {
            var group = GroupById(r.GroupId);
            var userId = r.HasActorId ? ParseId(r.ActorId) : BotId;
            return new GetEffectivePermissionsResponse { Permissions = (ulong)EffectivePermissions(group, userId) };
        }));
        invoker.Handle<KickUserRequest, KickUserResponse>(Groups, "KickUser", r => Locked(() =>
        {
            if (GroupById(r.GroupId).Members.Remove(ParseId(r.UserId)))
            {
                SendLater(GatewayTopics.GroupEvents, "UserKicked", ParseId(r.GroupId), new { UserId = ParseId(r.UserId) });
            }

            _moderation.Add(new ModerationAction("kick", ParseId(r.GroupId), ParseId(r.UserId), null));
            return new KickUserResponse { Success = true };
        }));
        invoker.Handle<BanUserRequest, BanUserResponse>(Groups, "BanUser", r => Locked(() =>
        {
            var group = GroupById(r.GroupId);
            if (group.Members.Remove(ParseId(r.UserId)))
            {
                SendLater(GatewayTopics.GroupEvents, "UserKicked", group.Id, new { UserId = ParseId(r.UserId) });
            }

            group.Bans.Add(new FakeBan(ParseId(r.UserId), r.Reason, BotId));
            _moderation.Add(new ModerationAction("ban", group.Id, ParseId(r.UserId), r.Reason));
            return new BanUserResponse { Success = true };
        }));
        invoker.Handle<UnbanUserRequest, UnbanUserResponse>(Groups, "UnbanUser", r => Locked(() =>
        {
            GroupById(r.GroupId).Bans.RemoveAll(b => b.UserId == ParseId(r.UserId));
            _moderation.Add(new ModerationAction("unban", ParseId(r.GroupId), ParseId(r.UserId), null));
            return new UnbanUserResponse { Success = true };
        }));
        invoker.Handle<GetGroupBansRequest, GetGroupBansResponse>(Groups, "GetGroupBans", r => Locked(() =>
        {
            var response = new GetGroupBansResponse();
            response.Bans.AddRange(GroupById(r.GroupId).Bans.Select(b => new WireGroupBan { User = WireUserOf(b.UserId), Reason = b.Reason, BannedByUserId = b.BannedBy.ToString() }));
            return response;
        }));
    }

    private void RegisterAttachments(FakeCallInvoker invoker)
    {
        invoker.Handle<InitiateUploadRequest, InitiateUploadResponse>(Attachments, "InitiateUpload", r => Locked(() =>
        {
            var id = Guid.NewGuid();
            _pending[id] = new PendingUpload(r.FileName, r.ContentType, 0);
            return new InitiateUploadResponse { AttachmentId = id.ToString(), UploadUrl = StorageUrl + id };
        }));
        invoker.Handle<ConfirmUploadRequest, AttachmentResponse>(Attachments, "ConfirmUpload", r => Locked(() =>
            Confirm(ParseId(r.AttachmentId), _stored.GetValueOrDefault(StorageUrl + r.AttachmentId) ?? [], multipart: false)));
        invoker.Handle<InitiateMultipartUploadRequest, InitiateMultipartUploadResponse>(Attachments, "InitiateMultipartUpload", r => Locked(() =>
        {
            var id = Guid.NewGuid();
            _pending[id] = new PendingUpload(r.FileName, r.ContentType, r.PartCount);
            var response = new InitiateMultipartUploadResponse { AttachmentId = id.ToString() };
            response.PartUrls.AddRange(Enumerable.Range(1, r.PartCount).Select(n => new MultipartPartUrl { PartNumber = n, Url = $"{StorageUrl}{id}/{n}" }));
            return response;
        }));
        invoker.Handle<GetMultipartPartUrlsRequest, GetMultipartPartUrlsResponse>(Attachments, "GetMultipartPartUrls", r =>
        {
            var response = new GetMultipartPartUrlsResponse();
            response.PartUrls.AddRange(r.PartNumbers.Select(n => new MultipartPartUrl { PartNumber = n, Url = $"{StorageUrl}{r.AttachmentId}/{n}" }));
            return response;
        });
        invoker.Handle<CompleteMultipartUploadRequest, AttachmentResponse>(Attachments, "CompleteMultipartUpload", r => Locked(() =>
        {
            var data = r.Parts.OrderBy(p => p.PartNumber).SelectMany(p => _stored.GetValueOrDefault($"{StorageUrl}{r.AttachmentId}/{p.PartNumber}") ?? []).ToArray();
            return Confirm(ParseId(r.AttachmentId), data, multipart: true);
        }));
        invoker.Handle<AbortMultipartUploadRequest, Empty>(Attachments, "AbortMultipartUpload", r => Locked(() =>
        {
            _pending.Remove(ParseId(r.AttachmentId));
            return new Empty();
        }));
    }

    private void RegisterInteractions(FakeCallInvoker invoker)
    {
        invoker.Handle<GetBotCommandsRequest, GetBotCommandsResponse>(Interactions, "GetBotCommands", _ => Locked(() =>
        {
            var response = new GetBotCommandsResponse();
            response.Commands.AddRange(_commands.Select(c => c.Clone()));
            return response;
        }));
        invoker.Handle<RegisterCommandRequest, BotCommand>(Interactions, "RegisterCommand", r => Locked(() =>
        {
            var command = new BotCommand
            {
                Id = Guid.NewGuid().ToString(), BotId = r.BotId, Name = r.Name, Description = r.Description,
                Options = { r.Options }, DefaultMemberPermissions = r.DefaultMemberPermissions,
            };
            _commands.Add(command);
            return command.Clone();
        }));
        invoker.Handle<UpdateCommandRequest, BotCommand>(Interactions, "UpdateCommand", r => Locked(() =>
        {
            var command = _commands.FirstOrDefault(c => c.Id == r.CommandId) ?? throw new RpcException(new Status(StatusCode.NotFound, "No such command."));
            command.Name = r.Name;
            command.Description = r.Description;
            command.Options.Clear();
            command.Options.AddRange(r.Options);
            command.DefaultMemberPermissions = r.DefaultMemberPermissions;
            return command.Clone();
        }));
        invoker.Handle<DeleteCommandRequest, Empty>(Interactions, "DeleteCommand", r => Locked(() =>
        {
            _commands.RemoveAll(c => c.Id == r.CommandId);
            return new Empty();
        }));
    }

    /// <summary>From inside a call handler: sends a gateway event once the call has answered, like the real server.</summary>
    private void SendLater(string topic, string type, Guid? groupId, object payload) =>
        FakeCallInvoker.AfterResponse(() => Deliver(topic, type, groupId, payload));

    private Permissions EffectivePermissions(FakeGroup group, Guid userId)
    {
        if (group.Permissions.TryGetValue(userId, out var explicitPermissions)) return explicitPermissions;
        if (group.OwnerId == userId) return Permissions.Administrator;
        var roles = group.Members.GetValueOrDefault(userId) ?? [];
        return group.Roles.Where(r => roles.Contains(r.Id)).Aggregate(DefaultPermissions, (all, role) => all | role.Permissions);
    }

    private AttachmentResponse Confirm(Guid id, byte[] data, bool multipart)
    {
        if (!_pending.Remove(id, out var pending)) throw new RpcException(new Status(StatusCode.NotFound, $"No upload {id}."));
        _uploads.Add(new UploadedFile(id, pending.FileName, pending.ContentType, data));
        return new AttachmentResponse
        {
            Id = id.ToString(), UploaderId = BotId.ToString(), FileName = pending.FileName, ContentType = pending.ContentType,
            SizeBytes = data.Length, Status = WireAttachmentStatus.Uploaded, IsMultipart = multipart,
        };
    }

    private void StoreUpload(string url, byte[] data)
    {
        lock (_lock) _stored[url] = data;
    }

    private static WireRole WireRoleOf(FakeGroup group, FakeRole role, int index) => new()
    {
        Id = role.Id.ToString(), GroupId = group.Id.ToString(), Name = role.Name, Position = index.ToString("D4", null),
        Permissions = (long)(ulong)role.Permissions,
    };

    private WireMessage Message(string id) =>
        _messages.TryGetValue(ParseId(id), out var message) ? message : throw new RpcException(new Status(StatusCode.NotFound, $"No message {id}."));

    private FakeGroup GroupById(string id) =>
        _groups.TryGetValue(ParseId(id), out var group) ? group : throw new RpcException(new Status(StatusCode.NotFound, $"No group {id}."));

    private static Guid ParseId(string id) =>
        Guid.TryParse(id, out var parsed) ? parsed : throw new RpcException(new Status(StatusCode.InvalidArgument, $"'{id}' is not an id."));

    private T Locked<T>(Func<T> handler)
    {
        lock (_lock) return handler();
    }

    private sealed record PendingUpload(string FileName, string ContentType, int Parts);
}
