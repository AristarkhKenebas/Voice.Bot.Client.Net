using Voice.Bot.Client.Net.Internal;
using Voice.Bot.Client.Net.Models;
using Vc.Backend.WebApi.Groups.Grpc;
using Role = Voice.Bot.Client.Net.Models.Role;

namespace Voice.Bot.Client.Net;

/// <summary>Roles of a group and assigning them to members (requires <c>ManageRoles</c>).</summary>
public sealed partial class BotClient
{
    public async Task<Page<Role>> GetRolesAsync(Guid groupId, int pageSize = 0, string? pageToken = null, CancellationToken ct = default)
    {
        var request = new GetGroupRolesRequest { GroupId = groupId.ToString(), PageSize = pageSize, PageToken = pageToken ?? "" };
        var response = await _api.CallAsync(o => _api.Groups.GetGroupRolesAsync(request, o), ct).ConfigureAwait(false);
        return new Page<Role>(response.Roles.Select(r => r.ToModel()).ToList(), Wire.Text(response.NextPageToken));
    }

    /// <param name="color">Color as <c>"#rrggbb"</c>; <c>null</c> for none.</param>
    /// <param name="hoist">Show members with this role as a separate section; <c>null</c> for the default.</param>
    public async Task<Role> CreateRoleAsync(
        Guid groupId, string name, Permissions permissions = Permissions.None, string? color = null, bool? hoist = null, CancellationToken ct = default)
    {
        var request = new CreateRoleRequest { GroupId = groupId.ToString(), Name = name, Permissions = (long)(ulong)permissions };
        if (color is not null) request.Color = color;
        if (hoist is { } h) request.Hoist = h;
        return (await _api.CallAsync(o => _api.Groups.CreateRoleAsync(request, o), ct, Idempotency.Unsafe).ConfigureAwait(false)).ToModel();
    }

    /// <summary>Name and permissions are always replaced; <paramref name="color"/> and <paramref name="hoist"/> only when not <c>null</c>.</summary>
    public async Task<Role> UpdateRoleAsync(
        Guid roleId, string name, Permissions permissions, string? color = null, bool? hoist = null, CancellationToken ct = default)
    {
        var request = new UpdateRoleRequest { RoleId = roleId.ToString(), Name = name, Permissions = (long)(ulong)permissions };
        if (color is not null) request.Color = color;
        if (hoist is { } h) request.Hoist = h;
        return (await _api.CallAsync(o => _api.Groups.UpdateRoleAsync(request, o), ct).ConfigureAwait(false)).ToModel();
    }

    /// <summary>Moves a role between two neighbours; one of them is enough.</summary>
    public async Task<Role> MoveRoleAsync(Guid roleId, Guid? previousRoleId = null, Guid? nextRoleId = null, CancellationToken ct = default)
    {
        var request = new MoveRoleRequest { RoleId = roleId.ToString() };
        if (previousRoleId is { } previous) request.PreviousRoleId = previous.ToString();
        if (nextRoleId is { } next) request.NextRoleId = next.ToString();
        return (await _api.CallAsync(o => _api.Groups.MoveRoleAsync(request, o), ct).ConfigureAwait(false)).ToModel();
    }

    public async Task DeleteRoleAsync(Guid roleId, CancellationToken ct = default) =>
        await _api.DeleteAsync(o => _api.Groups.DeleteRoleAsync(new DeleteRoleRequest { RoleId = roleId.ToString() }, o), ct).ConfigureAwait(false);

    public async Task AssignRoleAsync(Guid groupId, Guid targetUserId, Guid roleId, CancellationToken ct = default) =>
        await _api.CallAsync(o => _api.Groups.AssignRoleAsync(
            new RoleAssignmentRequest { GroupId = groupId.ToString(), TargetUserId = targetUserId.ToString(), RoleId = roleId.ToString() }, o), ct).ConfigureAwait(false);

    public async Task RemoveRoleAsync(Guid groupId, Guid targetUserId, Guid roleId, CancellationToken ct = default) =>
        await _api.CallAsync(o => _api.Groups.RemoveRoleAsync(
            new RoleAssignmentRequest { GroupId = groupId.ToString(), TargetUserId = targetUserId.ToString(), RoleId = roleId.ToString() }, o), ct).ConfigureAwait(false);
}
