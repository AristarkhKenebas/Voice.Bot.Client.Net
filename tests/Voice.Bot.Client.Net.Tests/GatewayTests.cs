using System.Text.Json;
using FluentAssertions;
using Voice.Bot.Client.Net.Events;
using Voice.Bot.Client.Net.Gateway;
using Voice.Bot.Client.Net.Models;
using Xunit;

namespace Voice.Bot.Client.Net.Tests;

public static class Envelopes
{
    public static string Make(string type, object? payload, object? extra = null)
    {
        var envelope = new Dictionary<string, object?>
        {
            ["Type"] = type,
            ["GroupId"] = Known.GroupId,
            ["PayloadJson"] = payload is string text ? text : JsonSerializer.Serialize(payload),
            ["Timestamp"] = 1_760_000_000_000,
        };
        if (extra is not null)
        {
            foreach (var property in extra.GetType().GetProperties()) envelope[property.Name] = property.GetValue(extra);
        }

        return JsonSerializer.Serialize(envelope);
    }

    public static T Parse<T>(string topic, string type, object? payload, object? extra = null)
    {
        var parsed = GatewayEventParser.Parse(topic, Make(type, payload, extra));
        parsed.Event.Should().BeOfType<T>();
        return (T)parsed.Event;
    }
}

public class GatewayEventParsingTests
{
    private static readonly string AuthorId = Guid.NewGuid().ToString();

    [Fact]
    public void MessageCreated_ShouldMapEverything()
    {
        var reactor = Guid.NewGuid();
        var mentioned = Guid.NewGuid();
        var created = Envelopes.Parse<MessageCreatedEvent>(GatewayTopics.ChatEvents, "MessageCreated", new
        {
            Id = Known.MessageId, ChannelId = Known.ChannelId, Content = "Hi @bot", CreatedAt = "2026-05-01T10:00:00Z",
            UpdatedAt = "2026-05-01T10:05:00.1234567", IsEdited = true, ReplyTo = Known.MessageId, ReplyToAuthorId = "",
            Author = new { Id = AuthorId, DisplayName = "Anna", PresenceStatus = 0 },
            Reactions = new Dictionary<string, string[]> { ["👍"] = [reactor.ToString(), "not-a-guid"] },
            Attachments = new[] { new { Id = Guid.NewGuid(), FileName = "cat.png", SizeBytes = 10, Status = "Uploaded" } },
            Mentions = new[] { new { Id = mentioned, IsBot = true } },
            MentionChannels = new[] { new { Id = Guid.NewGuid(), Type = 1, GroupId = Known.GroupId } },
            MentionRoles = new[] { new { Id = Guid.NewGuid(), Color = 255 } },
            MentionEveryone = true, InteractionId = Guid.NewGuid(), InteractionCommandName = "ping",
        });

        created.GroupId.Should().Be(Guid.Parse(Known.GroupId));
        var message = created.Message;
        message.Content.Should().Be("Hi @bot");
        message.IsEdited.Should().BeTrue();
        message.ReplyToAuthorId.Should().BeNull();
        message.Author.DisplayName.Should().Be("Anna");
        message.Author.Status.Should().Be(UserStatus.Online);
        message.CreatedAt.Should().Be(new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero));
        message.UpdatedAt.Should().Be(new DateTimeOffset(2026, 5, 1, 10, 5, 0, TimeSpan.Zero).AddTicks(1234567));
        message.Reactions["👍"].Should().Equal(reactor);
        message.Attachments.Single().Status.Should().Be(AttachmentStatus.Uploaded);
        message.Mentions.Single().IsBot.Should().BeTrue();
        message.MentionedChannels.Single().Type.Should().Be(ChannelType.Voice);
        message.MentionedRoles.Single().Color.Should().Be(255);
        message.MentionsEveryone.Should().BeTrue();
        message.Interaction!.CommandName.Should().Be("ping");
    }

    [Fact]
    public void Parsing_ShouldIgnoreCaseAndSurviveMissingFields()
    {
        var updated = Envelopes.Parse<MessageUpdatedEvent>(GatewayTopics.ChatEvents, "MessageUpdated",
            new { id = Known.MessageId, content = "x", author = new { isBot = true } });
        updated.Message.Id.Should().Be(Guid.Parse(Known.MessageId));
        updated.Message.Author.IsBot.Should().BeTrue();

        var empty = Envelopes.Parse<MessageCreatedEvent>(GatewayTopics.ChatEvents, "MessageCreated", new { }).Message;
        empty.Id.Should().Be(Guid.Empty);
        empty.CreatedAt.Should().BeNull();
        empty.Interaction.Should().BeNull();
        Envelopes.Parse<MessageCreatedEvent>(GatewayTopics.ChatEvents, "MessageCreated", "[1,2]").Message.Id.Should().Be(Guid.Empty);
        Envelopes.Parse<MessageCreatedEvent>(GatewayTopics.ChatEvents, "MessageCreated", new { CreatedAt = "yesterday" }).Message.CreatedAt.Should().BeNull();
        Envelopes.Parse<MessageCreatedEvent>(GatewayTopics.ChatEvents, "MessageCreated", new { InteractionId = "" }).Message.Interaction.Should().BeNull();
    }

    [Fact]
    public void ChatEvents_ShouldParse()
    {
        Envelopes.Parse<MessageDeletedEvent>(GatewayTopics.ChatEvents, "MessageDeleted", new { Id = Known.MessageId }, new { ChatId = Known.ChannelId })
            .Should().Be(new MessageDeletedEvent(Guid.Parse(Known.GroupId), Guid.Parse(Known.ChannelId), Guid.Parse(Known.MessageId)));
        Envelopes.Parse<MessageDeletedEvent>(GatewayTopics.ChatEvents, "MessageDeleted", "", new { MessageId = Known.MessageId })
            .MessageId.Should().Be(Guid.Parse(Known.MessageId));

        var added = GatewayEventParser.Parse(GatewayTopics.ChatEvents, Envelopes.Make("MessageReactionAdded", new { Emoji = "🔥" }));
        added.Kind.Should().Be(GatewayEventKind.ReactionAdded);
        GatewayEventParser.Parse(GatewayTopics.ChatEvents, Envelopes.Make("MessageReactionRemoved", new { Emoji = "🔥" })).Kind
            .Should().Be(GatewayEventKind.ReactionRemoved);
        Envelopes.Parse<TypingEvent>(GatewayTopics.ChatEvents, "UserTyping", new { IsTyping = true }).IsTyping.Should().BeTrue();

        var bot = Envelopes.Parse<BotTypingEvent>(GatewayTopics.ChatEvents, "BotTyping", new { State = "Re_Search", IsActive = true });
        bot.State.Should().Be(BotTypingState.ReSearch);
        bot.IsActive.Should().BeTrue();
        Envelopes.Parse<BotTypingEvent>(GatewayTopics.ChatEvents, "BotTyping", new { State = 1 }).State.Should().Be(BotTypingState.Explored);
        Envelopes.Parse<BotTypingEvent>(GatewayTopics.ChatEvents, "BotTyping", new { State = 99 }).State.Should().Be(BotTypingState.Unknown);
    }

    [Fact]
    public void GroupEvents_ShouldParse()
    {
        var changed = Envelopes.Parse<GroupStructureChangedEvent>(GatewayTopics.GroupEvents, "ChannelCreated", new
        {
            Id = Known.GroupId, Name = "Club", OwnerId = Known.UserId, AccessType = "InviteOnly", VerificationLevel = 2,
            CreatedAt = "2026-01-01T00:00:00+03:00",
            Categories = new[] { new { Id = Known.CategoryId, Channels = new[] { new { Id = Known.ChannelId, Type = 1, Users = new[] { new { Id = AuthorId, Username = "bob" } } } } } },
        });
        changed.Type.Should().Be("ChannelCreated");
        changed.Group!.AccessType.Should().Be(AccessType.InviteOnly);
        changed.Group.VerificationLevel.Should().Be(VerificationLevel.Medium);
        changed.Group.CreatedAt.Should().Be(new DateTimeOffset(2025, 12, 31, 21, 0, 0, TimeSpan.Zero));
        changed.Group.Categories.Single().Channels.Single().Users.Single().Username.Should().Be("bob");

        var joined = GatewayEventParser.Parse(GatewayTopics.GroupEvents, Envelopes.Make("UserJoined", new { Id = Known.GroupId, Name = "Club" }, new { UserId = Known.UserId }));
        joined.Kind.Should().Be(GatewayEventKind.MemberJoined);
        ((MemberEvent)joined.Event).UserId.Should().Be(Guid.Parse(Known.UserId));
        GatewayEventParser.Parse(GatewayTopics.GroupEvents, Envelopes.Make("UserLeft", "", new { UserId = Known.UserId })).Kind.Should().Be(GatewayEventKind.MemberLeft);
        GatewayEventParser.Parse(GatewayTopics.GroupEvents, Envelopes.Make("UserKicked", new { UserId = Known.UserId })).Kind.Should().Be(GatewayEventKind.MemberKicked);

        var profile = Envelopes.Parse<UserProfileUpdatedEvent>(GatewayTopics.GroupEvents, "UserProfileUpdated",
            new { DisplayName = "New", PresenceStatus = "DoNotDisturb", LastActivity = "2026-02-02T12:00:00" }, new { UserId = Known.UserId });
        (profile.DisplayName, profile.Description, profile.Status).Should().Be(("New", null, UserStatus.DoNotDisturb));
        profile.LastActivity.Should().Be(new DateTimeOffset(2026, 2, 2, 12, 0, 0, TimeSpan.Zero));

        var status = Envelopes.Parse<UserStatusChangedEvent>(GatewayTopics.GroupEvents, "UserStatusChanged",
            new { PresenceStatus = 1, Status = "lunch" }, new { UserId = Known.UserId });
        (status.Status, status.StatusText, status.LastActivity).Should().Be((UserStatus.Offline, "lunch", null));
    }

    [Fact]
    public void VoiceAndInteractionEvents_ShouldParse()
    {
        var connected = GatewayEventParser.Parse(GatewayTopics.ChannelEvents, Envelopes.Make("UserConnected", new { Username = "anna" }, new { ChatId = Known.ChannelId, UserId = Known.UserId }));
        connected.Kind.Should().Be(GatewayEventKind.VoiceConnected);
        ((VoiceEvent)connected.Event).User.Should().Match<GatewayUser>(u => u.Id == Guid.Parse(Known.UserId) && u.Username == "anna");
        GatewayEventParser.Parse(GatewayTopics.ChannelEvents, Envelopes.Make("UserDisconnected", new { Id = Known.UserId }, new { ChatId = Known.ChannelId }))
            .Kind.Should().Be(GatewayEventKind.VoiceDisconnected);

        var command = Envelopes.Parse<BotInteractionEvent>(GatewayTopics.InteractionEvents, "InteractionCreated", new
        {
            InteractionId = Guid.NewGuid(), Type = 0, InvokingUserId = Known.UserId, ChannelId = Known.ChannelId, GroupId = Known.GroupId,
            Data = new { CommandName = "weather", Options = new Dictionary<string, object> { ["city"] = "ber", ["days"] = 3 } },
        });
        command.Type.Should().Be(InteractionType.ApplicationCommand);
        command.Data.Options.Should().BeEquivalentTo(new Dictionary<string, string> { ["city"] = "ber", ["days"] = "3" });

        var autocomplete = Envelopes.Parse<BotInteractionEvent>(GatewayTopics.InteractionEvents, "InteractionCreated", new
        {
            interactionId = Guid.NewGuid(), type = "ApplicationCommandAutocomplete",
            data = new { options = new[] { new { name = "city", value = "Be" } }, focusedOption = new { name = "city" } },
        });
        autocomplete.Type.Should().Be(InteractionType.ApplicationCommandAutocomplete);
        autocomplete.Data.Options["city"].Should().Be("Be");
        autocomplete.Data.FocusedOption.Should().Be("city");

        Envelopes.Parse<BotInteractionEvent>(GatewayTopics.InteractionEvents, "InteractionCreated", new { Type = "MessageComponent", Data = new { Values = new[] { "a", null, "b" } } })
            .Data.Values.Should().Equal("a", "b");
        Envelopes.Parse<BotInteractionEvent>(GatewayTopics.InteractionEvents, "InteractionCreated", new { Type = 42 }).Type.Should().Be(InteractionType.Unknown);
    }

    [Fact]
    public void UnknownAndBrokenMessages_ShouldNotThrow()
    {
        Envelopes.Parse<UnknownGatewayEvent>(GatewayTopics.ChannelEvents, "ChannelPermissionsChanged", "{\"x\":1}")
            .Should().Be(new UnknownGatewayEvent(GatewayTopics.ChannelEvents, "ChannelPermissionsChanged", Guid.Parse(Known.GroupId), "{\"x\":1}"));
        Envelopes.Parse<UnknownGatewayEvent>("other-topic", "Anything", new { });

        GatewayEventParser.Parse(GatewayTopics.ChatEvents, "not json").Event.Should().BeOfType<GatewayParseException>();
        GatewayEventParser.Parse(GatewayTopics.ChatEvents, Envelopes.Make("MessageCreated", "{broken")).Event.Should().BeOfType<GatewayParseException>()
            .Which.Type.Should().Be("MessageCreated");
        Envelopes.Parse<MessageCreatedEvent>(GatewayTopics.ChatEvents, "MessageCreated", "{\"Attachments\":[{\"SizeBytes\":1e400}]}")
            .Message.Attachments.Single().SizeBytes.Should().Be(0);
    }
}

public class GatewayDispatchTests
{
    [Fact]
    public async Task Events_ShouldReachHandlers()
    {
        await using var h = await TestHarness.StartAsync([]);
        var created = new TaskCompletionSource<MessageCreatedEvent>();
        var joined = new TaskCompletionSource<MemberEvent>();
        var left = new TaskCompletionSource<MemberEvent>();
        h.Client.MessageCreated += e => { created.TrySetResult(e); return Task.CompletedTask; };
        h.Client.MemberJoined += e => { joined.TrySetResult(e); return Task.CompletedTask; };
        h.Client.MemberLeft += e => { left.TrySetResult(e); return Task.CompletedTask; };

        h.Gateway.Deliver(GatewayTopics.ChatEvents, Envelopes.Make("MessageCreated", new { Id = Known.MessageId }));
        h.Gateway.Deliver(GatewayTopics.GroupEvents, Envelopes.Make("UserJoined", "", new { UserId = Known.UserId }));
        h.Gateway.Deliver(GatewayTopics.GroupEvents, Envelopes.Make("UserLeft", "", new { UserId = Known.UserId }));

        (await created.Task.WaitAsync(TimeSpan.FromSeconds(3))).Message.Id.Should().Be(Guid.Parse(Known.MessageId));
        (await joined.Task.WaitAsync(TimeSpan.FromSeconds(3))).UserId.Should().Be(Guid.Parse(Known.UserId));
        await left.Task.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task FailingHandler_ShouldNotStopOtherSubscribers()
    {
        await using var h = await TestHarness.StartAsync([]);
        var errors = new TaskCompletionSource<Exception>();
        var second = new TaskCompletionSource<bool>();
        h.Client.ConnectionError += ex => { errors.TrySetResult(ex); return Task.CompletedTask; };
        h.Client.MessageCreated += _ => throw new InvalidOperationException("broken handler");
        h.Client.MessageCreated += _ => { second.TrySetResult(true); return Task.CompletedTask; };

        h.Gateway.Deliver(GatewayTopics.ChatEvents, Envelopes.Make("MessageCreated", new { }));

        (await second.Task.WaitAsync(TimeSpan.FromSeconds(3))).Should().BeTrue();
        (await errors.Task.WaitAsync(TimeSpan.FromSeconds(3))).Message.Should().Be("broken handler");
    }

    [Fact]
    public async Task BrokenDataAndConnectionErrors_ShouldGoToConnectionError()
    {
        await using var h = await TestHarness.StartAsync([]);
        var errors = new List<Exception>();
        var two = new TaskCompletionSource<bool>();
        h.Client.ConnectionError += ex =>
        {
            lock (errors)
            {
                errors.Add(ex);
                if (errors.Count == 2) two.TrySetResult(true);
            }

            return Task.CompletedTask;
        };

        h.Gateway.Deliver(GatewayTopics.ChatEvents, "not json");
        h.Gateway.Fail(new IOException("dropped"));

        await two.Task.WaitAsync(TimeSpan.FromSeconds(3));
        errors.Should().ContainSingle(e => e is GatewayParseException).And.ContainSingle(e => e is IOException);
    }
}
