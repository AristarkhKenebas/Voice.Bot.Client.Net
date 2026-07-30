using Google.Protobuf.WellKnownTypes;
using FluentAssertions;
using Moq;
using Voice.Bot.Client.Gateway;
using Voice.Bot.Client.Interactions;
using Voice.Bot.Client.Transport;
using Xunit;
using GrpcBot = Vc.Backend.Contracts.Grpc.Bot;
using GrpcUserStatus = Vc.Backend.Contracts.Grpc.UserStatus;
using GrpcBotCommand = Vc.Backend.WebApi.Interactions.Grpc.BotCommand;
using GrpcCommandOption = Vc.Backend.WebApi.Interactions.Grpc.CommandOption;
using GrpcCommandOptionType = Vc.Backend.WebApi.Interactions.Grpc.CommandOptionType;
using GrpcGetBotCommandsResponse = Vc.Backend.WebApi.Interactions.Grpc.GetBotCommandsResponse;
using GrpcRegisterCommandRequest = Vc.Backend.WebApi.Interactions.Grpc.RegisterCommandRequest;
using GrpcUpdateCommandRequest = Vc.Backend.WebApi.Interactions.Grpc.UpdateCommandRequest;
using GrpcDeleteCommandRequest = Vc.Backend.WebApi.Interactions.Grpc.DeleteCommandRequest;

namespace Voice.Bot.Client.Tests;

public sealed class RegistrationTestModule : InteractionModuleBase
{
    [SlashCommand("weather", "Get the weather")]
    public Task Weather([SlashCommandOption("City name", Required = true)] string city) => RespondAsync("sunny");
}

public class InteractionServiceRegistrationTests
{
    private static readonly Guid BotId = Guid.NewGuid();

    private readonly Mock<IBotsApiTransport> _transportMock = new();
    private readonly Mock<IBotGatewayConnection> _gatewayMock = new();
    private readonly Mock<IInteractionsAdminTransport> _interactionsAdminMock = new();
    private readonly BotClient _client;
    private readonly InteractionService _interactions;

    public InteractionServiceRegistrationTests()
    {
        _client = new BotClient(_transportMock.Object, _gatewayMock.Object, _interactionsAdminMock.Object);
        _interactions = new InteractionService(_client);
        _interactions.AddModule<RegistrationTestModule>();

        _transportMock.Setup(x => x.GetMeAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new GrpcBot
        {
            Id = BotId.ToString(),
            OwnerId = Guid.NewGuid().ToString(),
            Username = "weather_bot",
            UserStatus = GrpcUserStatus.Online,
            IsEnabled = true,
            CreatedAt = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
            UpdatedAt = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
        });
    }

    private static GrpcBotCommand CreateExisting(Guid id, string name, string description, params GrpcCommandOption[] options)
    {
        var command = new GrpcBotCommand
        {
            Id = id.ToString(),
            BotId = BotId.ToString(),
            Name = name,
            Description = description,
            CreatedAt = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
            UpdatedAt = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
        };
        command.Options.AddRange(options);
        return command;
    }

    private void SetExistingCommands(params GrpcBotCommand[] commands)
    {
        var response = new GrpcGetBotCommandsResponse();
        response.Commands.AddRange(commands);
        _interactionsAdminMock.Setup(x => x.GetBotCommandsAsync(It.IsAny<Vc.Backend.WebApi.Interactions.Grpc.GetBotCommandsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
    }

    [Fact]
    public async Task RegisterCommandsAsync_ShouldRegisterNewCommand_WhenNoneExist()
    {
        SetExistingCommands();
        _interactionsAdminMock.Setup(x => x.RegisterCommandAsync(It.IsAny<GrpcRegisterCommandRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateExisting(Guid.NewGuid(), "weather", "Get the weather"));

        await _interactions.RegisterCommandsAsync();

        _interactionsAdminMock.Verify(x => x.RegisterCommandAsync(
            It.Is<GrpcRegisterCommandRequest>(r =>
                r.BotId == BotId.ToString() &&
                r.Name == "weather" &&
                r.Description == "Get the weather" &&
                r.Options.Count == 1 &&
                r.Options[0].Name == "city" &&
                r.Options[0].Description == "City name" &&
                r.Options[0].Required &&
                r.Options[0].Type == GrpcCommandOptionType.String),
            It.IsAny<CancellationToken>()), Times.Once);
        _interactionsAdminMock.Verify(x => x.UpdateCommandAsync(It.IsAny<GrpcUpdateCommandRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _interactionsAdminMock.Verify(x => x.DeleteCommandAsync(It.IsAny<GrpcDeleteCommandRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RegisterCommandsAsync_ShouldUpdateCommand_WhenDescriptionChanged()
    {
        var existingId = Guid.NewGuid();
        var option = new GrpcCommandOption { Name = "city", Description = "City name", Type = GrpcCommandOptionType.String, Required = true };
        SetExistingCommands(CreateExisting(existingId, "weather", "Old description", option));
        _interactionsAdminMock.Setup(x => x.UpdateCommandAsync(It.IsAny<GrpcUpdateCommandRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateExisting(existingId, "weather", "Get the weather", option));

        await _interactions.RegisterCommandsAsync();

        _interactionsAdminMock.Verify(x => x.UpdateCommandAsync(
            It.Is<GrpcUpdateCommandRequest>(r => r.CommandId == existingId.ToString() && r.Description == "Get the weather"),
            It.IsAny<CancellationToken>()), Times.Once);
        _interactionsAdminMock.Verify(x => x.RegisterCommandAsync(It.IsAny<GrpcRegisterCommandRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RegisterCommandsAsync_ShouldSkipUpdate_WhenDefinitionUnchanged()
    {
        var option = new GrpcCommandOption { Name = "city", Description = "City name", Type = GrpcCommandOptionType.String, Required = true };
        SetExistingCommands(CreateExisting(Guid.NewGuid(), "weather", "Get the weather", option));

        await _interactions.RegisterCommandsAsync();

        _interactionsAdminMock.Verify(x => x.UpdateCommandAsync(It.IsAny<GrpcUpdateCommandRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _interactionsAdminMock.Verify(x => x.RegisterCommandAsync(It.IsAny<GrpcRegisterCommandRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RegisterCommandsAsync_ShouldDeleteStaleCommand_WhenDeleteMissingTrue()
    {
        var option = new GrpcCommandOption { Name = "city", Description = "City name", Type = GrpcCommandOptionType.String, Required = true };
        var staleId = Guid.NewGuid();
        SetExistingCommands(
            CreateExisting(Guid.NewGuid(), "weather", "Get the weather", option),
            CreateExisting(staleId, "old-command", "No longer declared"));
        _interactionsAdminMock.Setup(x => x.DeleteCommandAsync(It.IsAny<GrpcDeleteCommandRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _interactions.RegisterCommandsAsync(deleteMissing: true);

        _interactionsAdminMock.Verify(x => x.DeleteCommandAsync(
            It.Is<GrpcDeleteCommandRequest>(r => r.CommandId == staleId.ToString()), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RegisterCommandsAsync_ShouldNotDeleteStaleCommand_WhenDeleteMissingFalse()
    {
        var option = new GrpcCommandOption { Name = "city", Description = "City name", Type = GrpcCommandOptionType.String, Required = true };
        SetExistingCommands(
            CreateExisting(Guid.NewGuid(), "weather", "Get the weather", option),
            CreateExisting(Guid.NewGuid(), "old-command", "No longer declared"));

        await _interactions.RegisterCommandsAsync(deleteMissing: false);

        _interactionsAdminMock.Verify(x => x.DeleteCommandAsync(It.IsAny<GrpcDeleteCommandRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
