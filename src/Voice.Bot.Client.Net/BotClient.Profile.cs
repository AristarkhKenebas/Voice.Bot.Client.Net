using Voice.Bot.Client.Net.Models;
using GrpcSetStatusRequest = Vc.Backend.WebApi.Bots.Apis.Grpc.SetStatusRequest;

namespace Voice.Bot.Client.Net;

/// <summary>The bot's own profile and presence - <c>GetMe</c>/<c>SetStatus</c>, neither of which requires an intent.</summary>
public sealed partial class BotClient
{
    public async Task<Models.Bot> GetMeAsync(CancellationToken ct = default) =>
        (await _transport.GetMeAsync(ct)).ToModel();

    /// <summary>
    /// Sets the bot's own presence status. Broadcasts live (Discord-style) to every group the bot
    /// belongs to - see <see cref="UserStatusChanged"/> for the other side of that broadcast.
    /// </summary>
    public Task SetStatusAsync(UserStatus status, string? statusText = null, CancellationToken ct = default) =>
        _transport.SetStatusAsync(new GrpcSetStatusRequest { Status = status.ToGrpc(), StatusText = statusText ?? "" }, ct);
}
