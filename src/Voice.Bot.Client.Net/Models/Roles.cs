using Voice.Bot.Client.Net.Internal;
using Wire_ = Vc.Backend.WebApi.Groups.Grpc;

namespace Voice.Bot.Client.Net.Models;

/// <param name="Color">Color as <c>"#rrggbb"</c>, <c>null</c> for none.</param>
/// <param name="Permissions">What members with this role may do.</param>
/// <param name="Hoist">Members with this role are shown as a separate section.</param>
public sealed record Role(
    Guid Id, Guid GroupId, string Name, string Position, string? Color, Permissions Permissions, bool Hoist, bool Mentionable);

/// <summary>Channel- or category-level permissions for a role or a single user.</summary>
/// <param name="IsRole"><see cref="TargetId"/> is a role id; otherwise it's a user id.</param>
/// <param name="Allow">Permissions granted on top of the roles.</param>
/// <param name="Deny">Permissions taken away.</param>
public sealed record PermissionOverwrite(Guid TargetId, bool IsRole, Permissions Allow = Permissions.None, Permissions Deny = Permissions.None);

internal static class RoleExtensions
{
    public static Role ToModel(this Wire_.Role wire) => new(
        Wire.Id(wire.Id),
        Wire.Id(wire.GroupId),
        wire.Name,
        wire.Position,
        Wire.Text(wire.Color),
        (Permissions)(ulong)wire.Permissions,
        wire.Hoist,
        wire.Mentionable);

    public static PermissionOverwrite ToModel(this Wire_.ChannelPermissionOverwrite wire) =>
        new(Wire.Id(wire.TargetId), wire.IsRoleTarget, (Permissions)wire.Allow, (Permissions)wire.Deny);

    public static PermissionOverwrite ToModel(this Wire_.CategoryPermissionOverwrite wire) =>
        new(Wire.Id(wire.TargetId), wire.IsRoleTarget, (Permissions)wire.Allow, (Permissions)wire.Deny);
}
