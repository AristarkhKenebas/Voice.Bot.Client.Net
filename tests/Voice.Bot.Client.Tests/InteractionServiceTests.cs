using System.Text.Json;
using FluentAssertions;
using Moq;
using Voice.Bot.Client.Gateway;
using Voice.Bot.Client.Interactions;
using Voice.Bot.Client.Transport;
using Xunit;
using GrpcRespondToInteractionRequest = Vc.Backend.WebApi.Bots.Apis.Grpc.RespondToInteractionRequest;

namespace Voice.Bot.Client.Tests;

public sealed class TestInteractionModule : InteractionModuleBase
{
    public int PingCallCount;
    public string? LastComponentCapture;
    public string? LastModalFieldValue;
    public bool AutocompleteCalled;

    [SlashCommand("ping", "Replies with pong")]
    public Task Ping()
    {
        PingCallCount++;
        return RespondAsync("pong");
    }

    [ComponentInteraction("delete:*")]
    public Task Delete(string id)
    {
        LastComponentCapture = id;
        return RespondAsync($"deleted {id}");
    }

    [ModalInteraction("feedback:*")]
    public Task SubmitFeedback(string comment)
    {
        LastModalFieldValue = comment;
        return RespondAsync("thanks");
    }

    [AutocompleteHandler("weather", "city")]
    public Task AutocompleteCity()
    {
        AutocompleteCalled = true;
        return RespondWithAutocompleteAsync([("Berlin", "berlin")]);
    }
}

public class InteractionServiceTests
{
    private readonly Mock<IBotsApiTransport> _transportMock = new();
    private readonly Mock<IBotGatewayConnection> _gatewayMock = new();
    private readonly Mock<IInteractionsAdminTransport> _interactionsAdminMock = new();
    private readonly TestInteractionModule _module = new();
    private readonly BotClient _client;

    public InteractionServiceTests()
    {
        _client = new BotClient(_transportMock.Object, _gatewayMock.Object, _interactionsAdminMock.Object);
        var interactions = new InteractionService(_client, _ => _module);
        interactions.AddModule<TestInteractionModule>();
        interactions.Initialize();
    }

    private static string BuildEnvelopeJson(object payload, Guid userId) => JsonSerializer.Serialize(new
    {
        Type = "InteractionCreated",
        PayloadJson = JsonSerializer.Serialize(payload),
        Timestamp = 1_700_000_000_000L,
        GroupId = Guid.Empty,
        ChatId = Guid.Empty,
        UserId = userId,
        MessageId = Guid.Empty,
    });

    private static object BuildInteractionPayload(
        Guid interactionId, Guid invokingUserId, Guid channelId, int type,
        string? commandName = null, Dictionary<string, string>? options = null,
        string? customId = null, string? focusedOption = null, Dictionary<string, string>? modalFields = null) => new
    {
        InteractionId = interactionId,
        InvokingUserId = invokingUserId,
        ChannelId = channelId,
        GroupId = (Guid?)null,
        Type = type,
        Data = new
        {
            CommandName = commandName,
            Options = options ?? new Dictionary<string, string>(),
            CustomId = customId,
            Values = (List<string>?)null,
            FocusedOption = focusedOption,
            ModalFields = modalFields,
        },
    };

    /// <summary>Raises a synthetic interaction-events message on the fake gateway and waits for the
    /// module's response to reach the (mocked) transport, instead of an arbitrary delay.</summary>
    private async Task RaiseAndWaitForRespondAsync(string json)
    {
        var tcs = new TaskCompletionSource();
        _transportMock.Setup(x => x.RespondToInteractionAsync(It.IsAny<GrpcRespondToInteractionRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback(() => tcs.TrySetResult());

        _gatewayMock.Raise(g => g.MessageReceived += null, GatewayTopics.InteractionEvents, json);

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Dispatch_SlashCommand_ShouldInvokeMatchingModuleMethod_AndRespond()
    {
        var interactionId = Guid.NewGuid();
        var payload = BuildInteractionPayload(interactionId, Guid.NewGuid(), Guid.NewGuid(), type: 0, commandName: "ping");

        await RaiseAndWaitForRespondAsync(BuildEnvelopeJson(payload, Guid.NewGuid()));

        _module.PingCallCount.Should().Be(1);
        _transportMock.Verify(x => x.RespondToInteractionAsync(
            It.Is<GrpcRespondToInteractionRequest>(r => r.InteractionId == interactionId.ToString() && r.Content == "pong"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Dispatch_UnknownCommandName_ShouldNotInvokeAnyModule()
    {
        var payload = BuildInteractionPayload(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), type: 0, commandName: "not-registered");

        _gatewayMock.Raise(g => g.MessageReceived += null, GatewayTopics.InteractionEvents, BuildEnvelopeJson(payload, Guid.NewGuid()));
        await Task.Delay(100);

        _module.PingCallCount.Should().Be(0);
        _transportMock.Verify(x => x.RespondToInteractionAsync(It.IsAny<GrpcRespondToInteractionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Dispatch_MessageComponent_ShouldMatchTrailingWildcard_AndPassCapture()
    {
        var payload = BuildInteractionPayload(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), type: 1, customId: "delete:item-42");

        await RaiseAndWaitForRespondAsync(BuildEnvelopeJson(payload, Guid.NewGuid()));

        _module.LastComponentCapture.Should().Be("item-42");
        _transportMock.Verify(x => x.RespondToInteractionAsync(
            It.Is<GrpcRespondToInteractionRequest>(r => r.Content == "deleted item-42"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Dispatch_ModalSubmit_ShouldBindFieldByParameterName()
    {
        var payload = BuildInteractionPayload(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), type: 2,
            customId: "feedback:general", modalFields: new Dictionary<string, string> { ["comment"] = "great bot" });

        await RaiseAndWaitForRespondAsync(BuildEnvelopeJson(payload, Guid.NewGuid()));

        _module.LastModalFieldValue.Should().Be("great bot");
    }

    [Fact]
    public async Task Dispatch_Autocomplete_ShouldMatchByCommandAndFocusedOption()
    {
        var payload = BuildInteractionPayload(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), type: 3,
            commandName: "weather", focusedOption: "city", options: new Dictionary<string, string> { ["city"] = "berl" });

        await RaiseAndWaitForRespondAsync(BuildEnvelopeJson(payload, Guid.NewGuid()));

        _module.AutocompleteCalled.Should().BeTrue();
        _transportMock.Verify(x => x.RespondToInteractionAsync(
            It.Is<GrpcRespondToInteractionRequest>(r =>
                r.Kind == Vc.Backend.WebApi.Bots.Apis.Grpc.InteractionResponseKind.AutocompleteResult &&
                r.AutocompleteChoices.Count == 1 && r.AutocompleteChoices[0].Value == "berlin"),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
