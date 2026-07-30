using System.Text.Json;
using FluentAssertions;
using Moq;
using Voice.Bot.Client.Net.Commands;
using Voice.Bot.Client.Net.Gateway;
using Voice.Bot.Client.Net.Transport;
using Xunit;
using GrpcSendMessageRequest = Vc.Backend.WebApi.Bots.Apis.Grpc.SendMessageRequest;
using GrpcMessageInfo = Vc.Backend.WebApi.Bots.Apis.Grpc.MessageInfo;

namespace Voice.Bot.Client.Net.Tests;

public sealed class TestCommandModule : ModuleBase
{
    public int PingCallCount;
    public string? LastEchoText;
    public (int A, int B)? LastAddArgs;

    [Command("ping")]
    [Alias("p")]
    public Task Ping()
    {
        PingCallCount++;
        return ReplyAsync("pong");
    }

    [Command("echo")]
    public Task Echo(string text)
    {
        LastEchoText = text;
        return ReplyAsync(text);
    }

    [Command("add")]
    public Task Add(int a, int b)
    {
        LastAddArgs = (a, b);
        return ReplyAsync((a + b).ToString());
    }

    [Command("boom")]
    public Task Boom() => throw new InvalidOperationException("kaboom");
}

public class CommandServiceTests
{
    private readonly Mock<IBotsApiTransport> _transportMock = new();
    private readonly Mock<IBotGatewayConnection> _gatewayMock = new();
    private readonly Mock<IInteractionsAdminTransport> _interactionsAdminMock = new();
    private readonly TestCommandModule _module = new();
    private readonly BotClient _client;
    private readonly CommandService _commands;

    public CommandServiceTests()
    {
        _client = new BotClient(_transportMock.Object, _gatewayMock.Object, _interactionsAdminMock.Object);
        _commands = new CommandService(_client, "!", _ => _module);
        _commands.AddModule<TestCommandModule>();
        _commands.Initialize();
    }

    private static string BuildMessageEnvelopeJson(string content, bool isBot = false)
    {
        var payload = new
        {
            Id = Guid.NewGuid(),
            ChannelId = Guid.NewGuid(),
            Author = new
            {
                Id = Guid.NewGuid(),
                Username = "someone",
                Email = "someone@example.com",
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
            },
            Content = content,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            IsEdited = false,
            IsDeleted = false,
            ReplyTo = (Guid?)null,
            Attachments = Array.Empty<object>(),
            Reactions = new Dictionary<string, object>(),
        };

        return JsonSerializer.Serialize(new
        {
            Type = "MessageCreated",
            PayloadJson = JsonSerializer.Serialize(payload),
            Timestamp = 1_700_000_000_000L,
            GroupId = Guid.Empty,
            ChatId = Guid.Empty,
            UserId = Guid.Empty,
            MessageId = Guid.Empty,
        });
    }

    private async Task RaiseAndWaitForReplyAsync(string json)
    {
        var tcs = new TaskCompletionSource();
        _transportMock.Setup(x => x.SendMessageAsync(It.IsAny<GrpcSendMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GrpcMessageInfo { Id = Guid.NewGuid().ToString(), ChannelId = Guid.NewGuid().ToString() })
            .Callback(() => tcs.TrySetResult());

        _gatewayMock.Raise(g => g.MessageReceived += null, GatewayTopics.ChatEvents, json);

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Dispatch_PrefixedCommand_ShouldInvokeMatchingModuleMethod_AndReply()
    {
        await RaiseAndWaitForReplyAsync(BuildMessageEnvelopeJson("!ping"));

        _module.PingCallCount.Should().Be(1);
        _transportMock.Verify(x => x.SendMessageAsync(
            It.Is<GrpcSendMessageRequest>(r => r.Content == "pong"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Dispatch_Alias_ShouldMatchSameHandlerAsCommandName()
    {
        await RaiseAndWaitForReplyAsync(BuildMessageEnvelopeJson("!p"));

        _module.PingCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Dispatch_ShouldIgnoreMessagesFromBots()
    {
        _gatewayMock.Raise(g => g.MessageReceived += null, GatewayTopics.ChatEvents, BuildMessageEnvelopeJson("!ping", isBot: true));
        await Task.Delay(100);

        _module.PingCallCount.Should().Be(0);
        _transportMock.Verify(x => x.SendMessageAsync(It.IsAny<GrpcSendMessageRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Dispatch_ShouldIgnoreMessagesWithoutThePrefix()
    {
        _gatewayMock.Raise(g => g.MessageReceived += null, GatewayTopics.ChatEvents, BuildMessageEnvelopeJson("ping"));
        await Task.Delay(100);

        _module.PingCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Dispatch_UnknownCommandName_ShouldNotInvokeAnyModule()
    {
        _gatewayMock.Raise(g => g.MessageReceived += null, GatewayTopics.ChatEvents, BuildMessageEnvelopeJson("!not-registered"));
        await Task.Delay(100);

        _module.PingCallCount.Should().Be(0);
        _transportMock.Verify(x => x.SendMessageAsync(It.IsAny<GrpcSendMessageRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Dispatch_TrailingStringParameter_ShouldCaptureRemainderOfTheMessage()
    {
        await RaiseAndWaitForReplyAsync(BuildMessageEnvelopeJson("!echo hello there world"));

        _module.LastEchoText.Should().Be("hello there world");
    }

    [Fact]
    public async Task Dispatch_MultipleParameters_ShouldBindPositionally()
    {
        await RaiseAndWaitForReplyAsync(BuildMessageEnvelopeJson("!add 2 3"));

        _module.LastAddArgs.Should().Be((2, 3));
        _transportMock.Verify(x => x.SendMessageAsync(
            It.Is<GrpcSendMessageRequest>(r => r.Content == "5"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Dispatch_QuotedArgument_ShouldBeKeptAsOneToken()
    {
        await RaiseAndWaitForReplyAsync(BuildMessageEnvelopeJson("!echo \"hello world\""));

        _module.LastEchoText.Should().Be("hello world");
    }

    [Fact]
    public async Task Dispatch_HandlerThrows_ShouldRaiseCommandExecutionFailed_NotThrowIntoGateway()
    {
        Exception? captured = null;
        var tcs = new TaskCompletionSource();
        _commands.CommandExecutionFailed += (ex, _) =>
        {
            captured = ex;
            tcs.TrySetResult();
            return Task.CompletedTask;
        };

        _gatewayMock.Raise(g => g.MessageReceived += null, GatewayTopics.ChatEvents, BuildMessageEnvelopeJson("!boom"));
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        captured.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Be("kaboom");
    }
}
