using Vc.Backend.WebApi.Interactions.Grpc;

namespace Voice.Bot.Client.Transport;

/// <summary>
/// The 4 <c>InteractionsApi</c> slash-command CRUD RPCs, authenticated with the bot's own bot-token
/// (the backend also accepts an owner Keycloak JWT for these same RPCs, but that path isn't part of
/// this library - see <see cref="BotClientOptions"/>'s doc comment). Same shape as
/// <see cref="IBotsApiTransport"/> - kept separate because it talks to a different backend
/// microservice/endpoint (<c>Vc.Backend.WebApi.Interactions</c>, not <c>Bots.Apis</c>).
/// </summary>
internal interface IInteractionsAdminTransport
{
    Task<BotCommand> RegisterCommandAsync(RegisterCommandRequest request, CancellationToken ct);
    Task<BotCommand> UpdateCommandAsync(UpdateCommandRequest request, CancellationToken ct);
    Task DeleteCommandAsync(DeleteCommandRequest request, CancellationToken ct);
    Task<GetBotCommandsResponse> GetBotCommandsAsync(GetBotCommandsRequest request, CancellationToken ct);
}
