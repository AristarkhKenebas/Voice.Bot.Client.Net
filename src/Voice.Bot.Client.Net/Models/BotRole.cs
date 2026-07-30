using Wire = Vc.Backend.WebApi.Bots.Apis.Grpc.BotRole;

namespace Voice.Bot.Client.Net.Models;

/// <summary>Mirrors <c>bots.BotRole</c> - returned by CreateRole/UpdateRole.</summary>
public sealed record BotRole(
    Guid Id,
    Guid GroupId,
    string Name,
    string Position,
    string Color,
    IReadOnlyList<long> Permissions);

internal static class BotRoleExtensions
{
    public static BotRole ToModel(this Wire grpc) => new(
        Guid.Parse(grpc.Id),
        Guid.Parse(grpc.GroupId),
        grpc.Name,
        grpc.Position,
        grpc.Color,
        grpc.Permissions.ToList());
}
