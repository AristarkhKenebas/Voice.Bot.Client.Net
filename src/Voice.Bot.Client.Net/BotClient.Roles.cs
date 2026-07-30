using Voice.Bot.Client.Net.Models;
using GrpcCreateRoleRequest = Vc.Backend.WebApi.Bots.Apis.Grpc.CreateRoleRequest;
using GrpcUpdateRoleRequest = Vc.Backend.WebApi.Bots.Apis.Grpc.UpdateRoleRequest;
using GrpcDeleteRoleRequest = Vc.Backend.WebApi.Bots.Apis.Grpc.DeleteRoleRequest;
using GrpcRoleAssignmentRequest = Vc.Backend.WebApi.Bots.Apis.Grpc.RoleAssignmentRequest;

namespace Voice.Bot.Client.Net;

/// <summary>Role management - requires the <c>Guilds</c> intent plus <c>ManageRoles</c> in the bot's managed role for the group.</summary>
public sealed partial class BotClient
{
    /// <summary>Creates a role in a group this bot belongs to.</summary>
    public async Task<BotRole> CreateRoleAsync(
        Guid groupId, string name, string? color = null, IReadOnlyCollection<long>? permissions = null, CancellationToken ct = default)
    {
        var request = new GrpcCreateRoleRequest { GroupId = groupId.ToString(), Name = name };
        if (color is not null) request.Color = color;
        if (permissions is not null) request.Permissions.AddRange(permissions);

        return (await _transport.CreateRoleAsync(request, ct)).ToModel();
    }

    /// <summary>Updates a role's name/permissions/color. Requires role-hierarchy above the target role.</summary>
    public async Task<BotRole> UpdateRoleAsync(Guid roleId, string name, long permissions, string? color = null, CancellationToken ct = default)
    {
        var request = new GrpcUpdateRoleRequest { RoleId = roleId.ToString(), Name = name, Permissions = permissions };
        if (color is not null) request.Color = color;

        return (await _transport.UpdateRoleAsync(request, ct)).ToModel();
    }

    /// <summary>Deletes a role. The <c>@everyone</c> role and managed (bot) roles cannot be deleted this way.</summary>
    public Task DeleteRoleAsync(Guid roleId, CancellationToken ct = default) =>
        _transport.DeleteRoleAsync(new GrpcDeleteRoleRequest { RoleId = roleId.ToString() }, ct);

    /// <summary>Grants a role to a group member. Requires role-hierarchy above the target role.</summary>
    public Task AssignRoleAsync(Guid groupId, Guid targetUserId, Guid roleId, CancellationToken ct = default) =>
        _transport.AssignRoleAsync(
            new GrpcRoleAssignmentRequest { GroupId = groupId.ToString(), TargetUserId = targetUserId.ToString(), RoleId = roleId.ToString() }, ct);

    /// <summary>Revokes a role from a group member. Requires role-hierarchy above the target role.</summary>
    public Task RemoveRoleAsync(Guid groupId, Guid targetUserId, Guid roleId, CancellationToken ct = default) =>
        _transport.RemoveRoleAsync(
            new GrpcRoleAssignmentRequest { GroupId = groupId.ToString(), TargetUserId = targetUserId.ToString(), RoleId = roleId.ToString() }, ct);
}
