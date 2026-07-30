using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using FluentAssertions;
using Moq;
using Voice.Bot.Client.Net.Gateway;
using Voice.Bot.Client.Net.Models;
using Voice.Bot.Client.Net.Transport;
using Xunit;
using GrpcBot = Vc.Backend.Contracts.Grpc.Bot;
using GrpcUser = Vc.Backend.Contracts.Grpc.User;
using GrpcUserStatus = Vc.Backend.Contracts.Grpc.UserStatus;
using GrpcJoinVoiceChannelResponse = Vc.Backend.WebApi.Bots.Apis.Grpc.JoinVoiceChannelResponse;
using GrpcMessageInfo = Vc.Backend.WebApi.Bots.Apis.Grpc.MessageInfo;
using GrpcDeleteMessageResponse = Vc.Backend.WebApi.Bots.Apis.Grpc.DeleteMessageResponse;
using GrpcGetMyGroupsResponse = Vc.Backend.WebApi.Bots.Apis.Grpc.GetMyGroupsResponse;
using GrpcBotGroupInfo = Vc.Backend.WebApi.Bots.Apis.Grpc.BotGroupInfo;
using GrpcBotGroupDetails = Vc.Backend.WebApi.Bots.Apis.Grpc.BotGroupDetails;
using GrpcGetGroupUsersResponse = Vc.Backend.WebApi.Bots.Apis.Grpc.GetGroupUsersResponse;
using GrpcBotChannelDetails = Vc.Backend.WebApi.Bots.Apis.Grpc.BotChannelDetails;

namespace Voice.Bot.Client.Net.Tests;

public class BotClientTests
{
    private readonly Mock<IBotsApiTransport> _transportMock = new();
    private readonly Mock<IBotGatewayConnection> _gatewayMock = new();
    private readonly Mock<IInteractionsAdminTransport> _interactionsAdminMock = new();
    private readonly BotClient _client;

    public BotClientTests()
    {
        _client = new BotClient(_transportMock.Object, _gatewayMock.Object, _interactionsAdminMock.Object);
    }

    private static GrpcBot CreateGrpcBot(Guid id) => new()
    {
        Id = id.ToString(),
        OwnerId = Guid.NewGuid().ToString(),
        Username = "my_bot",
        DisplayName = "My Bot",
        UserStatus = GrpcUserStatus.Online,
        IsEnabled = true,
        CreatedAt = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
        UpdatedAt = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
        Intents = 3,
    };

    private static GrpcMessageInfo CreateGrpcMessageInfo(Guid id, Guid channelId, string content) => new()
    {
        Id = id.ToString(),
        ChannelId = channelId.ToString(),
        Content = content,
        AuthorId = Guid.NewGuid().ToString(),
        CreatedAt = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
        UpdatedAt = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
    };

    [Fact]
    public async Task GetMeAsync_ShouldReturnMappedBot()
    {
        var botId = Guid.NewGuid();
        _transportMock.Setup(x => x.GetMeAsync(It.IsAny<CancellationToken>())).ReturnsAsync(CreateGrpcBot(botId));

        var result = await _client.GetMeAsync();

        result.Id.Should().Be(botId);
        result.Username.Should().Be("my_bot");
        result.Intents.Should().Be((Intents.BotIntents)3);
    }

    [Fact]
    public async Task SetStatusAsync_ShouldSendMappedStatus()
    {
        _transportMock.Setup(x => x.SetStatusAsync(It.IsAny<Vc.Backend.WebApi.Bots.Apis.Grpc.SetStatusRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _client.SetStatusAsync(UserStatus.DoNotDisturb);

        _transportMock.Verify(x => x.SetStatusAsync(
            It.Is<Vc.Backend.WebApi.Bots.Apis.Grpc.SetStatusRequest>(r => r.Status == GrpcUserStatus.DoNotDisturb),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task JoinVoiceChannelAsync_ShouldSendChannelId_AndReturnToken()
    {
        var channelId = Guid.NewGuid();
        _transportMock.Setup(x => x.JoinVoiceChannelAsync(It.IsAny<Vc.Backend.WebApi.Bots.Apis.Grpc.JoinVoiceChannelRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GrpcJoinVoiceChannelResponse { Token = "jwt-token", ServerUrl = "wss://livekit.example.com" });

        var result = await _client.JoinVoiceChannelAsync(channelId);

        result.Token.Should().Be("jwt-token");
        result.ServerUrl.Should().Be("wss://livekit.example.com");
        _transportMock.Verify(x => x.JoinVoiceChannelAsync(
            It.Is<Vc.Backend.WebApi.Bots.Apis.Grpc.JoinVoiceChannelRequest>(r => r.ChannelId == channelId.ToString()),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LeaveVoiceChannelAsync_ShouldSendChannelId()
    {
        var channelId = Guid.NewGuid();
        _transportMock.Setup(x => x.LeaveVoiceChannelAsync(It.IsAny<Vc.Backend.WebApi.Bots.Apis.Grpc.LeaveVoiceChannelRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _client.LeaveVoiceChannelAsync(channelId);

        _transportMock.Verify(x => x.LeaveVoiceChannelAsync(
            It.Is<Vc.Backend.WebApi.Bots.Apis.Grpc.LeaveVoiceChannelRequest>(r => r.ChannelId == channelId.ToString()),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendMessageAsync_ShouldMapOptionalFields_AndReturnMessage()
    {
        var channelId = Guid.NewGuid();
        var replyTo = Guid.NewGuid();
        var attachmentId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        _transportMock.Setup(x => x.SendMessageAsync(It.IsAny<Vc.Backend.WebApi.Bots.Apis.Grpc.SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateGrpcMessageInfo(messageId, channelId, "hello"));

        var result = await _client.SendMessageAsync(channelId, "hello", replyTo, [attachmentId]);

        result.Id.Should().Be(messageId);
        result.Content.Should().Be("hello");
        _transportMock.Verify(x => x.SendMessageAsync(
            It.Is<Vc.Backend.WebApi.Bots.Apis.Grpc.SendMessageRequest>(r =>
                r.ChannelId == channelId.ToString() &&
                r.Content == "hello" &&
                r.ReplyTo == replyTo.ToString() &&
                r.AttachmentIds.Contains(attachmentId.ToString())),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendMessageAsync_ShouldOmitReplyTo_WhenNotProvided()
    {
        var channelId = Guid.NewGuid();
        _transportMock.Setup(x => x.SendMessageAsync(It.IsAny<Vc.Backend.WebApi.Bots.Apis.Grpc.SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateGrpcMessageInfo(Guid.NewGuid(), channelId, "hi"));

        await _client.SendMessageAsync(channelId, "hi");

        _transportMock.Verify(x => x.SendMessageAsync(
            It.Is<Vc.Backend.WebApi.Bots.Apis.Grpc.SendMessageRequest>(r => !r.HasReplyTo),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateMessageAsync_ShouldSendMessageIdAndContent()
    {
        var messageId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        _transportMock.Setup(x => x.UpdateMessageAsync(It.IsAny<Vc.Backend.WebApi.Bots.Apis.Grpc.UpdateMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateGrpcMessageInfo(messageId, channelId, "edited"));

        var result = await _client.UpdateMessageAsync(messageId, "edited");

        result.Content.Should().Be("edited");
        _transportMock.Verify(x => x.UpdateMessageAsync(
            It.Is<Vc.Backend.WebApi.Bots.Apis.Grpc.UpdateMessageRequest>(r => r.MessageId == messageId.ToString() && r.Content == "edited"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteMessageAsync_ShouldReturnMappedResult()
    {
        var messageId = Guid.NewGuid();
        _transportMock.Setup(x => x.DeleteMessageAsync(It.IsAny<Vc.Backend.WebApi.Bots.Apis.Grpc.DeleteMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GrpcDeleteMessageResponse { Id = messageId.ToString(), Success = true });

        var result = await _client.DeleteMessageAsync(messageId);

        result.Id.Should().Be(messageId);
        result.Success.Should().BeTrue();
        _transportMock.Verify(x => x.DeleteMessageAsync(
            It.Is<Vc.Backend.WebApi.Bots.Apis.Grpc.DeleteMessageRequest>(r => r.MessageId == messageId.ToString()),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddReactionAsync_ShouldSendMessageIdAndEmoji()
    {
        var messageId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        _transportMock.Setup(x => x.AddReactionAsync(It.IsAny<Vc.Backend.WebApi.Bots.Apis.Grpc.ReactionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateGrpcMessageInfo(messageId, channelId, "hi"));

        await _client.AddReactionAsync(messageId, "👍");

        _transportMock.Verify(x => x.AddReactionAsync(
            It.Is<Vc.Backend.WebApi.Bots.Apis.Grpc.ReactionRequest>(r => r.MessageId == messageId.ToString() && r.Emoji == "👍"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveReactionAsync_ShouldSendMessageIdAndEmoji()
    {
        var messageId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        _transportMock.Setup(x => x.RemoveReactionAsync(It.IsAny<Vc.Backend.WebApi.Bots.Apis.Grpc.ReactionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateGrpcMessageInfo(messageId, channelId, "hi"));

        await _client.RemoveReactionAsync(messageId, "👍");

        _transportMock.Verify(x => x.RemoveReactionAsync(
            It.Is<Vc.Backend.WebApi.Bots.Apis.Grpc.ReactionRequest>(r => r.MessageId == messageId.ToString() && r.Emoji == "👍"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TypingAsync_ShouldSendChannelIdAndIsTyping()
    {
        var channelId = Guid.NewGuid();
        _transportMock.Setup(x => x.TypingAsync(It.IsAny<Vc.Backend.WebApi.Bots.Apis.Grpc.TypingRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _client.TypingAsync(channelId, true);

        _transportMock.Verify(x => x.TypingAsync(
            It.Is<Vc.Backend.WebApi.Bots.Apis.Grpc.TypingRequest>(r => r.ChannelId == channelId.ToString() && r.IsTyping),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetMyGroupsAsync_ShouldReturnMappedGroups()
    {
        var groupId = Guid.NewGuid();
        var response = new GrpcGetMyGroupsResponse();
        response.Groups.Add(new GrpcBotGroupInfo
        {
            Id = groupId.ToString(),
            OwnerId = Guid.NewGuid().ToString(),
            Name = "My Group",
            CreatedAt = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
        });
        _transportMock.Setup(x => x.GetMyGroupsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(response);

        var result = await _client.GetMyGroupsAsync();

        result.Should().ContainSingle(g => g.Id == groupId && g.Name == "My Group");
    }

    [Fact]
    public async Task GetGroupAsync_ShouldSendGroupId_AndReturnDetails()
    {
        var groupId = Guid.NewGuid();
        _transportMock.Setup(x => x.GetGroupAsync(It.IsAny<Vc.Backend.WebApi.Bots.Apis.Grpc.GetGroupRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GrpcBotGroupDetails
            {
                Id = groupId.ToString(),
                OwnerId = Guid.NewGuid().ToString(),
                Name = "Detailed Group",
                CreatedAt = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
            });

        var result = await _client.GetGroupAsync(groupId);

        result.Id.Should().Be(groupId);
        result.Name.Should().Be("Detailed Group");
        _transportMock.Verify(x => x.GetGroupAsync(
            It.Is<Vc.Backend.WebApi.Bots.Apis.Grpc.GetGroupRequest>(r => r.GroupId == groupId.ToString()),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetGroupUsersAsync_ShouldSendGroupId_AndReturnMappedUsers()
    {
        var groupId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var response = new GrpcGetGroupUsersResponse();
        response.Users.Add(new GrpcUser
        {
            Id = userId.ToString(),
            Username = "member1",
            Email = "member1@example.com",
            CreatedAt = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
            UpdatedAt = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
            LastActivity = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
            IsBot = true,
        });
        _transportMock.Setup(x => x.GetGroupUsersAsync(It.IsAny<Vc.Backend.WebApi.Bots.Apis.Grpc.GetGroupUsersRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await _client.GetGroupUsersAsync(groupId);

        result.Should().ContainSingle(u => u.Id == userId && u.Username == "member1" && u.IsBot);
        _transportMock.Verify(x => x.GetGroupUsersAsync(
            It.Is<Vc.Backend.WebApi.Bots.Apis.Grpc.GetGroupUsersRequest>(r => r.GroupId == groupId.ToString()),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetChannelAsync_ShouldSendChannelId_AndReturnDetails()
    {
        var channelId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        _transportMock.Setup(x => x.GetChannelAsync(It.IsAny<Vc.Backend.WebApi.Bots.Apis.Grpc.GetChannelRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GrpcBotChannelDetails
            {
                Id = channelId.ToString(),
                GroupId = groupId.ToString(),
                Name = "general",
                Type = 0,
                Position = "1",
            });

        var result = await _client.GetChannelAsync(channelId);

        result.Id.Should().Be(channelId);
        result.Type.Should().Be(ChannelType.Text);
        _transportMock.Verify(x => x.GetChannelAsync(
            It.Is<Vc.Backend.WebApi.Bots.Apis.Grpc.GetChannelRequest>(r => r.ChannelId == channelId.ToString()),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RespondToInteractionAsync_ShouldMapKindAndAutocompleteChoices()
    {
        var interactionId = Guid.NewGuid();
        _transportMock.Setup(x => x.RespondToInteractionAsync(It.IsAny<Vc.Backend.WebApi.Bots.Apis.Grpc.RespondToInteractionRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _client.RespondToInteractionAsync(interactionId, new InteractionResponse(
            InteractionResponseKind.AutocompleteResult,
            AutocompleteChoices: [("Berlin", "berlin")]));

        _transportMock.Verify(x => x.RespondToInteractionAsync(
            It.Is<Vc.Backend.WebApi.Bots.Apis.Grpc.RespondToInteractionRequest>(r =>
                r.InteractionId == interactionId.ToString() &&
                r.Kind == Vc.Backend.WebApi.Bots.Apis.Grpc.InteractionResponseKind.AutocompleteResult &&
                r.AutocompleteChoices.Count == 1 &&
                r.AutocompleteChoices[0].Name == "Berlin"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendInteractionFollowupAsync_ShouldSendInteractionIdAndContent()
    {
        var interactionId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        _transportMock.Setup(x => x.SendInteractionFollowupAsync(It.IsAny<Vc.Backend.WebApi.Bots.Apis.Grpc.SendInteractionFollowupRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateGrpcMessageInfo(Guid.NewGuid(), channelId, "follow up"));

        var result = await _client.SendInteractionFollowupAsync(interactionId, "follow up");

        result.Content.Should().Be("follow up");
        _transportMock.Verify(x => x.SendInteractionFollowupAsync(
            It.Is<Vc.Backend.WebApi.Bots.Apis.Grpc.SendInteractionFollowupRequest>(r => r.InteractionId == interactionId.ToString() && r.Content == "follow up"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendMessageAsync_ShouldSerializeComponentBuilder_WhenProvided()
    {
        var channelId = Guid.NewGuid();
        _transportMock.Setup(x => x.SendMessageAsync(It.IsAny<Vc.Backend.WebApi.Bots.Apis.Grpc.SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateGrpcMessageInfo(Guid.NewGuid(), channelId, "pick one"));

        var components = new Components.ComponentBuilder()
            .WithButton(b => b.WithCustomId("confirm").WithLabel("Confirm"));

        await _client.SendMessageAsync(channelId, "pick one", components: components);

        _transportMock.Verify(x => x.SendMessageAsync(
            It.Is<Vc.Backend.WebApi.Bots.Apis.Grpc.SendMessageRequest>(r =>
                r.HasComponentsJson && r.ComponentsJson.Contains("\"customId\":\"confirm\"")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateCategoryAsync_ShouldSendGroupIdAndName_AndReturnMappedCategory()
    {
        var groupId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        _transportMock.Setup(x => x.CreateCategoryAsync(It.IsAny<Vc.Backend.WebApi.Bots.Apis.Grpc.CreateCategoryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Vc.Backend.WebApi.Bots.Apis.Grpc.BotCategoryInfo { Id = categoryId.ToString(), Name = "Voice", Position = "a" });

        var result = await _client.CreateCategoryAsync(groupId, "Voice");

        result.Id.Should().Be(categoryId);
        result.Name.Should().Be("Voice");
        _transportMock.Verify(x => x.CreateCategoryAsync(
            It.Is<Vc.Backend.WebApi.Bots.Apis.Grpc.CreateCategoryRequest>(r => r.GroupId == groupId.ToString() && r.Name == "Voice"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateChannelAsync_ShouldSendCategoryIdNameAndType_AndReturnMappedChannel()
    {
        var categoryId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        _transportMock.Setup(x => x.CreateChannelAsync(It.IsAny<Vc.Backend.WebApi.Bots.Apis.Grpc.CreateChannelRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Vc.Backend.WebApi.Bots.Apis.Grpc.BotChannelInfo
            {
                Id = channelId.ToString(), CategoryId = categoryId.ToString(), Name = "general", Type = 0, Position = "a"
            });

        var result = await _client.CreateChannelAsync(categoryId, "general", ChannelType.Text);

        result.Id.Should().Be(channelId);
        result.Name.Should().Be("general");
        _transportMock.Verify(x => x.CreateChannelAsync(
            It.Is<Vc.Backend.WebApi.Bots.Apis.Grpc.CreateChannelRequest>(r =>
                r.CategoryId == categoryId.ToString() && r.Name == "general" && r.Type == 0),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateRoleAsync_ShouldSendGroupIdNameColorAndPermissions_AndReturnMappedRole()
    {
        var groupId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        _transportMock.Setup(x => x.CreateRoleAsync(It.IsAny<Vc.Backend.WebApi.Bots.Apis.Grpc.CreateRoleRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Vc.Backend.WebApi.Bots.Apis.Grpc.BotRole { Id = roleId.ToString(), GroupId = groupId.ToString(), Name = "Muted", Position = "a", Color = "#5865F2" });

        var result = await _client.CreateRoleAsync(groupId, "Muted", "#5865F2", [1]);

        result.Id.Should().Be(roleId);
        result.Name.Should().Be("Muted");
        _transportMock.Verify(x => x.CreateRoleAsync(
            It.Is<Vc.Backend.WebApi.Bots.Apis.Grpc.CreateRoleRequest>(r =>
                r.GroupId == groupId.ToString() && r.Name == "Muted" && r.Color == "#5865F2" && r.Permissions.Contains(1)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateRoleAsync_ShouldSendRoleIdNamePermissionsAndColor_AndReturnMappedRole()
    {
        var groupId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        _transportMock.Setup(x => x.UpdateRoleAsync(It.IsAny<Vc.Backend.WebApi.Bots.Apis.Grpc.UpdateRoleRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Vc.Backend.WebApi.Bots.Apis.Grpc.BotRole { Id = roleId.ToString(), GroupId = groupId.ToString(), Name = "Renamed", Position = "a", Color = "#FFFFFF" });

        var result = await _client.UpdateRoleAsync(roleId, "Renamed", 42, "#FFFFFF");

        result.Name.Should().Be("Renamed");
        _transportMock.Verify(x => x.UpdateRoleAsync(
            It.Is<Vc.Backend.WebApi.Bots.Apis.Grpc.UpdateRoleRequest>(r =>
                r.RoleId == roleId.ToString() && r.Name == "Renamed" && r.Permissions == 42 && r.Color == "#FFFFFF"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteRoleAsync_ShouldSendRoleId()
    {
        var roleId = Guid.NewGuid();
        _transportMock.Setup(x => x.DeleteRoleAsync(It.IsAny<Vc.Backend.WebApi.Bots.Apis.Grpc.DeleteRoleRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _client.DeleteRoleAsync(roleId);

        _transportMock.Verify(x => x.DeleteRoleAsync(
            It.Is<Vc.Backend.WebApi.Bots.Apis.Grpc.DeleteRoleRequest>(r => r.RoleId == roleId.ToString()),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AssignRoleAsync_ShouldSendGroupTargetAndRoleIds()
    {
        var groupId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        _transportMock.Setup(x => x.AssignRoleAsync(It.IsAny<Vc.Backend.WebApi.Bots.Apis.Grpc.RoleAssignmentRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _client.AssignRoleAsync(groupId, targetUserId, roleId);

        _transportMock.Verify(x => x.AssignRoleAsync(
            It.Is<Vc.Backend.WebApi.Bots.Apis.Grpc.RoleAssignmentRequest>(r =>
                r.GroupId == groupId.ToString() && r.TargetUserId == targetUserId.ToString() && r.RoleId == roleId.ToString()),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveRoleAsync_ShouldSendGroupTargetAndRoleIds()
    {
        var groupId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        _transportMock.Setup(x => x.RemoveRoleAsync(It.IsAny<Vc.Backend.WebApi.Bots.Apis.Grpc.RoleAssignmentRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _client.RemoveRoleAsync(groupId, targetUserId, roleId);

        _transportMock.Verify(x => x.RemoveRoleAsync(
            It.Is<Vc.Backend.WebApi.Bots.Apis.Grpc.RoleAssignmentRequest>(r =>
                r.GroupId == groupId.ToString() && r.TargetUserId == targetUserId.ToString() && r.RoleId == roleId.ToString()),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void BotApiException_FromRpcException_ShouldPreserveStatusCodeAndMessage()
    {
        var rpcException = new RpcException(new Status(StatusCode.PermissionDenied, "missing GuildMessages intent"));

        var result = BotApiException.FromRpcException(rpcException);

        result.StatusCode.Should().Be(StatusCode.PermissionDenied);
        result.Message.Should().Be("missing GuildMessages intent");
        result.InnerException.Should().BeSameAs(rpcException);
    }

    [Fact]
    public async Task GetMeAsync_ShouldPropagateBotApiException_WhenTransportFails()
    {
        _transportMock.Setup(x => x.GetMeAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(BotApiException.FromRpcException(new RpcException(new Status(StatusCode.PermissionDenied, "missing intent"))));

        Func<Task> act = () => _client.GetMeAsync();

        await act.Should().ThrowAsync<BotApiException>().Where(e => e.StatusCode == StatusCode.PermissionDenied);
    }
}
