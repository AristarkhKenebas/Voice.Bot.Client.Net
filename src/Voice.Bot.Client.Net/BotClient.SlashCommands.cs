using Voice.Bot.Client.Net.Models;
using GrpcRegisterCommandRequest = Vc.Backend.WebApi.Interactions.Grpc.RegisterCommandRequest;
using GrpcUpdateCommandRequest = Vc.Backend.WebApi.Interactions.Grpc.UpdateCommandRequest;
using GrpcDeleteCommandRequest = Vc.Backend.WebApi.Interactions.Grpc.DeleteCommandRequest;
using GrpcGetBotCommandsRequest = Vc.Backend.WebApi.Interactions.Grpc.GetBotCommandsRequest;

namespace Voice.Bot.Client.Net;

/// <summary>
/// Owner-facing CRUD for the bot's own slash (application) commands - requires the bot's own
/// <see cref="Intents.BotIntents.ApplicationCommands"/> intent. Most callers should use
/// <see cref="Interactions.InteractionService.RegisterCommandsAsync"/> instead of these directly - it
/// builds requests from a module's <c>[SlashCommand]</c>/<c>[SlashCommandOption]</c> attributes and
/// reconciles against what's already registered.
/// </summary>
public sealed partial class BotClient
{
    public async Task<BotCommandDefinition> RegisterCommandAsync(
        Guid botId, string name, string description, IReadOnlyList<CommandOptionDefinition>? options = null,
        ulong defaultMemberPermissions = 0, CancellationToken ct = default)
    {
        var request = new GrpcRegisterCommandRequest { BotId = botId.ToString(), Name = name, Description = description, DefaultMemberPermissions = defaultMemberPermissions };
        if (options is not null) request.Options.AddRange(options.Select(o => o.ToWire()));

        return (await _interactionsAdmin.RegisterCommandAsync(request, ct)).ToModel();
    }

    public async Task<BotCommandDefinition> UpdateCommandAsync(
        Guid botId, Guid commandId, string name, string description, IReadOnlyList<CommandOptionDefinition>? options = null,
        ulong defaultMemberPermissions = 0, CancellationToken ct = default)
    {
        var request = new GrpcUpdateCommandRequest
        {
            BotId = botId.ToString(), CommandId = commandId.ToString(), Name = name, Description = description, DefaultMemberPermissions = defaultMemberPermissions,
        };
        if (options is not null) request.Options.AddRange(options.Select(o => o.ToWire()));

        return (await _interactionsAdmin.UpdateCommandAsync(request, ct)).ToModel();
    }

    public Task DeleteCommandAsync(Guid botId, Guid commandId, CancellationToken ct = default) =>
        _interactionsAdmin.DeleteCommandAsync(new GrpcDeleteCommandRequest { BotId = botId.ToString(), CommandId = commandId.ToString() }, ct);

    public async Task<IReadOnlyList<BotCommandDefinition>> GetBotCommandsAsync(Guid botId, CancellationToken ct = default) =>
        (await _interactionsAdmin.GetBotCommandsAsync(new GrpcGetBotCommandsRequest { BotId = botId.ToString() }, ct)).Commands.Select(c => c.ToModel()).ToList();
}
