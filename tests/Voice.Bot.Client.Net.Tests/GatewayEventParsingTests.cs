using System.Text.Json;
using FluentAssertions;
using Voice.Bot.Client.Net.Events;
using Voice.Bot.Client.Net.Gateway;
using Xunit;

namespace Voice.Bot.Client.Net.Tests;

public class GatewayEventParsingTests
{
    private static string BuildEnvelopeJson(
        string type, object payload, Guid groupId = default, Guid chatId = default, Guid userId = default, Guid messageId = default)
    {
        var envelope = new
        {
            Type = type,
            PayloadJson = JsonSerializer.Serialize(payload),
            Timestamp = 1_700_000_000_000L,
            GroupId = groupId,
            ChatId = chatId,
            UserId = userId,
            MessageId = messageId,
        };
        return JsonSerializer.Serialize(envelope);
    }

    private static object BuildAuthor(Guid id, string username, bool isBot) => new
    {
        Id = id,
        Username = username,
        Email = $"{username}@example.com",
        DisplayName = (string?)null,
        Description = (string?)null,
        Status = (string?)null,
        PreferredLanguage = (string?)null,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        LastActivity = DateTimeOffset.UtcNow,
        PresenceStatus = 0,
        IsConfirmed = true,
        IsBot = isBot,
        SubscriptionType = "None",
        AvatarId = (Guid?)null,
        BannerId = (Guid?)null,
    };

    [Fact]
    public void Parse_ChatEvents_MessageCreated_ShouldReturnMappedGatewayMessage()
    {
        var messageId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var authorId = Guid.NewGuid();

        var payload = new
        {
            Id = messageId,
            ChannelId = channelId,
            Author = BuildAuthor(authorId, "my_bot", true),
            Content = "hello",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            IsEdited = false,
            IsDeleted = false,
            ReplyTo = (Guid?)null,
            Attachments = Array.Empty<object>(),
            Reactions = new Dictionary<string, List<Guid>>(),
        };
        var json = BuildEnvelopeJson("MessageCreated", payload);

        var result = GatewayEventParser.Parse(GatewayTopics.ChatEvents, json);

        var created = result.Should().BeOfType<GatewayParseResult.MessageCreated>().Subject;
        created.Message.Id.Should().Be(messageId);
        created.Message.Content.Should().Be("hello");
        created.Message.Author.Id.Should().Be(authorId);
        created.Message.Author.IsBot.Should().BeTrue();
    }

    [Fact]
    public void Parse_ChatEvents_MessageUpdated_ShouldReturnMappedGatewayMessage()
    {
        var payload = new
        {
            Id = Guid.NewGuid(),
            ChannelId = Guid.NewGuid(),
            Author = BuildAuthor(Guid.NewGuid(), "my_bot", true),
            Content = "edited",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            IsEdited = true,
            IsDeleted = false,
            ReplyTo = (Guid?)null,
            Attachments = Array.Empty<object>(),
            Reactions = new Dictionary<string, List<Guid>>(),
        };
        var json = BuildEnvelopeJson("MessageUpdated", payload);

        var result = GatewayEventParser.Parse(GatewayTopics.ChatEvents, json);

        var updated = result.Should().BeOfType<GatewayParseResult.MessageUpdated>().Subject;
        updated.Message.Content.Should().Be("edited");
        updated.Message.IsEdited.Should().BeTrue();
    }

    [Fact]
    public void Parse_ChatEvents_MessageDeleted_ShouldReturnIdAndSuccess()
    {
        var messageId = Guid.NewGuid();
        var json = BuildEnvelopeJson("MessageDeleted", new { Id = messageId, Success = true });

        var result = GatewayEventParser.Parse(GatewayTopics.ChatEvents, json);

        var deleted = result.Should().BeOfType<GatewayParseResult.MessageDeleted>().Subject;
        deleted.MessageId.Should().Be(messageId);
        deleted.Success.Should().BeTrue();
    }

    [Fact]
    public void Parse_ChatEvents_MessageReactionAdded_ShouldReturnMappedReaction()
    {
        var messageId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var payload = new
        {
            MessageId = messageId,
            ChannelId = Guid.NewGuid(),
            UserId = userId,
            Emoji = "👍",
            Added = true,
            Reactions = new Dictionary<string, List<Guid>> { ["👍"] = [userId] },
        };
        var json = BuildEnvelopeJson("MessageReactionAdded", payload);

        var result = GatewayEventParser.Parse(GatewayTopics.ChatEvents, json);

        var added = result.Should().BeOfType<GatewayParseResult.ReactionAdded>().Subject;
        added.Reaction.Emoji.Should().Be("👍");
        added.Reaction.Reactions.Should().ContainKey("👍").WhoseValue.Should().Contain(userId);
    }

    [Fact]
    public void Parse_ChatEvents_MessageReactionRemoved_ShouldReturnMappedReaction()
    {
        var payload = new
        {
            MessageId = Guid.NewGuid(),
            ChannelId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Emoji = "👍",
            Added = false,
            Reactions = new Dictionary<string, List<Guid>>(),
        };
        var json = BuildEnvelopeJson("MessageReactionRemoved", payload);

        var result = GatewayEventParser.Parse(GatewayTopics.ChatEvents, json);

        result.Should().BeOfType<GatewayParseResult.ReactionRemoved>();
    }

    [Fact]
    public void Parse_ChatEvents_UserTyping_ShouldReturnMappedTyping()
    {
        var payload = new { ChannelId = Guid.NewGuid(), UserId = Guid.NewGuid(), Username = "member1", IsTyping = true };
        var json = BuildEnvelopeJson("UserTyping", payload);

        var result = GatewayEventParser.Parse(GatewayTopics.ChatEvents, json);

        var typing = result.Should().BeOfType<GatewayParseResult.UserTyping>().Subject;
        typing.Typing.Username.Should().Be("member1");
        typing.Typing.IsTyping.Should().BeTrue();
    }

    [Fact]
    public void Parse_ChatEvents_UnknownType_ShouldReturnUnknown_NotThrow()
    {
        var json = BuildEnvelopeJson("SomeFutureChatEventType", new { Whatever = "value" });

        var result = GatewayEventParser.Parse(GatewayTopics.ChatEvents, json);

        var unknown = result.Should().BeOfType<GatewayParseResult.Unknown>().Subject;
        unknown.Event.Topic.Should().Be(GatewayTopics.ChatEvents);
        unknown.Event.Type.Should().Be("SomeFutureChatEventType");
    }

    [Fact]
    public void Parse_ChannelEvents_UserConnected_ShouldDefensivelyParseSnapshot_IgnoringExtraFields()
    {
        var groupId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        // Simulates the backend's raw internal-domain-model payload: extra fields this library
        // doesn't (and shouldn't) know about must not break deserialization.
        var rawUserJson = $$"""
        {
          "Id": "{{userId}}",
          "Username": "member1",
          "DisplayName": "Member One",
          "AvatarId": null,
          "IsBot": false,
          "PasswordHash": "should-be-ignored",
          "InternalFlags": 42,
          "Email": "member1@example.com"
        }
        """;
        var envelope = new
        {
            Type = "UserConnected",
            PayloadJson = rawUserJson,
            Timestamp = 1_700_000_000_000L,
            GroupId = groupId,
            ChatId = channelId,
            UserId = userId,
            MessageId = Guid.Empty,
        };
        var json = JsonSerializer.Serialize(envelope);

        var result = GatewayEventParser.Parse(GatewayTopics.ChannelEvents, json);

        var connected = result.Should().BeOfType<GatewayParseResult.UserConnected>().Subject;
        connected.Presence.GroupId.Should().Be(groupId);
        connected.Presence.ChannelId.Should().Be(channelId);
        connected.Presence.UserId.Should().Be(userId);
        connected.Presence.User.Username.Should().Be("member1");
        connected.Presence.User.DisplayName.Should().Be("Member One");
        connected.Presence.User.IsBot.Should().BeFalse();
    }

    [Fact]
    public void Parse_ChannelEvents_UserDisconnected_ShouldReturnMappedPresence()
    {
        var userId = Guid.NewGuid();
        var rawUserJson = $$"""{ "Id": "{{userId}}", "Username": "member1", "IsBot": false }""";
        var envelope = new
        {
            Type = "UserDisconnected",
            PayloadJson = rawUserJson,
            Timestamp = 1_700_000_000_000L,
            GroupId = Guid.NewGuid(),
            ChatId = Guid.NewGuid(),
            UserId = userId,
            MessageId = Guid.Empty,
        };

        var result = GatewayEventParser.Parse(GatewayTopics.ChannelEvents, JsonSerializer.Serialize(envelope));

        result.Should().BeOfType<GatewayParseResult.UserDisconnected>();
    }

    [Fact]
    public void Parse_ChannelEvents_UnknownType_ShouldReturnUnknown_NotThrow()
    {
        var json = BuildEnvelopeJson("SomeFutureChannelEventType", new { Whatever = "value" });

        var result = GatewayEventParser.Parse(GatewayTopics.ChannelEvents, json);

        result.Should().BeOfType<GatewayParseResult.Unknown>();
    }

    [Fact]
    public void Parse_GroupEvents_UserJoined_ShouldDefensivelyParseGroupSnapshot_IgnoringExtraFields()
    {
        var groupId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var memberId = Guid.NewGuid();

        var rawGroupJson = $$"""
        {
          "Id": "{{groupId}}",
          "OwnerId": "{{Guid.NewGuid()}}",
          "Name": "My Group",
          "CreatedAt": "2024-01-01T00:00:00+00:00",
          "AvatarId": null,
          "BannerId": null,
          "AccessType": 0,
          "VerificationLevel": 0,
          "Categories": [
            {
              "Id": "{{categoryId}}",
              "Name": "General",
              "GroupId": "{{groupId}}",
              "Position": "1",
              "Channels": [
                {
                  "Id": "{{channelId}}",
                  "CategoryId": "{{categoryId}}",
                  "GroupId": "{{groupId}}",
                  "Name": "general",
                  "Type": 0,
                  "Position": "1",
                  "Users": [
                    { "Id": "{{memberId}}", "Username": "member1", "IsBot": false, "SomeExtraDomainField": true }
                  ]
                }
              ]
            }
          ]
        }
        """;
        var envelope = new
        {
            Type = "UserJoined",
            PayloadJson = rawGroupJson,
            Timestamp = 1_700_000_000_000L,
            GroupId = groupId,
            ChatId = Guid.Empty,
            UserId = userId,
            MessageId = Guid.Empty,
        };

        var result = GatewayEventParser.Parse(GatewayTopics.GroupEvents, JsonSerializer.Serialize(envelope));

        var joined = result.Should().BeOfType<GatewayParseResult.UserJoinedGroup>().Subject.Joined;
        joined.GroupId.Should().Be(groupId);
        joined.UserId.Should().Be(userId);
        joined.Group.Name.Should().Be("My Group");
        joined.Group.Categories.Should().ContainSingle(c => c.Id == categoryId && c.Name == "General");
        joined.Group.Categories[0].Channels.Should().ContainSingle(c => c.Id == channelId);
        joined.Group.Categories[0].Channels[0].Users.Should().ContainSingle(u => u.Id == memberId && u.Username == "member1");
    }

    [Fact]
    public void Parse_GroupEvents_UserStatusChanged_ShouldReturnMappedStatus()
    {
        var groupId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var lastActivity = DateTimeOffset.Parse("2024-01-01T00:00:00+00:00");

        var json = BuildEnvelopeJson(
            "UserStatusChanged",
            new { UserId = userId, PresenceStatus = 2, LastActivity = lastActivity }, // 2 = DoNotDisturb
            groupId: groupId);

        var result = GatewayEventParser.Parse(GatewayTopics.GroupEvents, json);

        var changed = result.Should().BeOfType<GatewayParseResult.UserStatusChanged>().Subject.StatusChanged;
        changed.GroupId.Should().Be(groupId);
        changed.UserId.Should().Be(userId);
        changed.PresenceStatus.Should().Be(Net.Models.UserStatus.DoNotDisturb);
        changed.LastActivity.Should().Be(lastActivity);
    }

    [Fact]
    public void Parse_GroupEvents_UnknownType_ShouldReturnUnknown_NotThrow()
    {
        var json = BuildEnvelopeJson("GroupUpdated", new { Whatever = "value" });

        var result = GatewayEventParser.Parse(GatewayTopics.GroupEvents, json);

        var unknown = result.Should().BeOfType<GatewayParseResult.Unknown>().Subject;
        unknown.Event.Type.Should().Be("GroupUpdated");
    }

    [Fact]
    public void Parse_InteractionEvents_InteractionCreated_ShouldReturnMappedInteraction()
    {
        var interactionId = Guid.NewGuid();
        var invokingUserId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var payload = new
        {
            InteractionId = interactionId,
            InvokingUserId = invokingUserId,
            ChannelId = channelId,
            GroupId = Guid.NewGuid(),
            Type = 0, // ApplicationCommand
            Data = new
            {
                CommandName = "ping",
                Options = new Dictionary<string, string>(),
                CustomId = (string?)null,
                Values = (List<string>?)null,
                FocusedOption = (string?)null,
                ModalFields = (Dictionary<string, string>?)null,
            },
        };
        var json = BuildEnvelopeJson("InteractionCreated", payload, userId: invokingUserId);

        var result = GatewayEventParser.Parse(GatewayTopics.InteractionEvents, json);

        var created = result.Should().BeOfType<GatewayParseResult.InteractionCreated>().Subject;
        created.Interaction.InteractionId.Should().Be(interactionId);
        created.Interaction.Type.Should().Be(InteractionType.ApplicationCommand);
        created.Interaction.Data.CommandName.Should().Be("ping");
    }

    [Fact]
    public void Parse_InteractionEvents_UnknownType_ShouldReturnUnknown_NotThrow()
    {
        var json = BuildEnvelopeJson("SomeFutureInteractionType", new { Whatever = "value" });

        var result = GatewayEventParser.Parse(GatewayTopics.InteractionEvents, json);

        result.Should().BeOfType<GatewayParseResult.Unknown>();
    }

    [Fact]
    public void Parse_UnrecognizedTopic_ShouldReturnUnknown_NotThrow()
    {
        var json = BuildEnvelopeJson("AnyType", new { Whatever = "value" });

        var result = GatewayEventParser.Parse("some-future-topic", json);

        var unknown = result.Should().BeOfType<GatewayParseResult.Unknown>().Subject;
        unknown.Event.Topic.Should().Be("some-future-topic");
    }

    [Fact]
    public void Parse_UnparseableEnvelope_ShouldReturnParseError_NotThrow()
    {
        var result = GatewayEventParser.Parse(GatewayTopics.ChatEvents, "not valid json at all");

        result.Should().BeOfType<GatewayParseResult.ParseError>();
    }

    [Fact]
    public void Parse_RecognizedTypeWithMalformedPayload_ShouldReturnParseError_NotThrow()
    {
        // Id is typed Guid - a JSON number for it is a genuine type mismatch (unlike a merely absent
        // field, which System.Text.Json tolerates), guaranteeing a JsonException here.
        var json = BuildEnvelopeJson("MessageCreated", new { Id = 12345 });

        var result = GatewayEventParser.Parse(GatewayTopics.ChatEvents, json);

        var error = result.Should().BeOfType<GatewayParseResult.ParseError>().Subject;
        error.Type.Should().Be("MessageCreated");
    }
}
