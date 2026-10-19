using Voice.Bot.Client.Net.Internal;
using Voice.Bot.Client.Net.Models;
using Vc.Backend.WebApi.Groups.Grpc;
using AccessType = Voice.Bot.Client.Net.Models.AccessType;
using Group = Voice.Bot.Client.Net.Models.Group;
using GroupBan = Voice.Bot.Client.Net.Models.GroupBan;
using GroupDetails = Voice.Bot.Client.Net.Models.GroupDetails;
using GroupMember = Voice.Bot.Client.Net.Models.GroupMember;
using Invite = Voice.Bot.Client.Net.Models.Invite;
using VerificationLevel = Voice.Bot.Client.Net.Models.VerificationLevel;
using WireAccessType = Vc.Backend.WebApi.Groups.Grpc.AccessType;
using WireVerificationLevel = Vc.Backend.WebApi.Groups.Grpc.VerificationLevel;

namespace Voice.Bot.Client.Net;

/// <summary>Groups the bot belongs to: settings, members, bans, invites and the audit log.</summary>
public sealed partial class BotClient
{
    public async Task<IReadOnlyList<Group>> GetMyGroupsAsync(CancellationToken ct = default) =>
        (await _api.CallAsync(o => _api.Groups.GetUserGroupsAsync(new GetUserGroupsRequest(), o), ct).ConfigureAwait(false)).Groups.Select(g => g.ToModel()).ToList();

    /// <summary>A group with its whole category and channel tree.</summary>
    public async Task<GroupDetails> GetGroupAsync(Guid groupId, CancellationToken ct = default) =>
        Wire.Required((await _api.CallAsync(o => _api.Groups.GetGroupAsync(new GetGroupRequest { Id = groupId.ToString() }, o), ct).ConfigureAwait(false)).Group, "group").ToModel();

    /// <summary>
    /// Changes the group's settings. The server replaces name, description, access type and verification
    /// level all at once, so start from the current values of <see cref="GetGroupAsync"/>. The API doesn't
    /// return the current description - pass the full text every time, an empty string erases it.
    /// </summary>
    /// <param name="voiceRegion"><c>null</c> leaves the region unchanged, an empty string switches to automatic selection.</param>
    public async Task<Group> UpdateGroupAsync(
        Guid groupId, string name, string description, AccessType accessType, VerificationLevel verificationLevel,
        string? voiceRegion = null, CancellationToken ct = default)
    {
        var request = new UpdateGroupRequest
        {
            Id = groupId.ToString(),
            Name = name,
            Description = description,
            AccessType = (WireAccessType)(int)accessType,
            VerificationLevel = (WireVerificationLevel)(int)verificationLevel,
        };
        if (voiceRegion is not null) request.VoiceRegion = voiceRegion;
        return (await _api.CallAsync(o => _api.Groups.UpdateGroupAsync(request, o), ct).ConfigureAwait(false)).ToModel();
    }

    public async Task LeaveGroupAsync(Guid groupId, CancellationToken ct = default) =>
        await _api.CallAsync(o => _api.Groups.LeaveGroupAsync(new LeaveGroupRequest { GroupId = groupId.ToString() }, o), ct).ConfigureAwait(false);

    /// <summary>Joins a group by invite code and returns the group's id.</summary>
    /// <exception cref="BotApiException">With <see cref="Grpc.Core.StatusCode.FailedPrecondition"/> when the server refused to join.</exception>
    public async Task<Guid> JoinByInviteAsync(string inviteCode, CancellationToken ct = default)
    {
        var response = await _api.CallAsync(o => _api.Groups.JoinByInviteAsync(new JoinByInviteRequest { InviteCode = inviteCode }, o), ct, Idempotency.Unsafe).ConfigureAwait(false);
        if (!response.Success && string.IsNullOrEmpty(response.GroupId)) throw new BotApiException(Grpc.Core.StatusCode.FailedPrecondition, $"Could not join by invite '{inviteCode}'.");
        return Wire.Id(response.GroupId);
    }

    /// <summary>One page of group members. Loop while <see cref="Page{T}.HasMore"/>, passing <see cref="Page{T}.NextPageToken"/> back.</summary>
    /// <param name="pageSize">0 for the server's default.</param>
    public async Task<Page<GroupMember>> GetGroupMembersAsync(Guid groupId, int pageSize = 0, string? pageToken = null, CancellationToken ct = default)
    {
        var request = new GetGroupUsersRequest { GroupId = groupId.ToString(), PageSize = pageSize, PageToken = pageToken ?? "" };
        var response = await _api.CallAsync(o => _api.Groups.GetGroupUsersAsync(request, o), ct).ConfigureAwait(false);
        return new Page<GroupMember>(response.Members.Select(m => m.ToModel()).ToList(), Wire.Text(response.NextPageToken));
    }

    /// <summary>Finds group members by name.</summary>
    public async Task<IReadOnlyList<GroupMember>> SearchGroupMembersAsync(Guid groupId, string query, int limit = 25, CancellationToken ct = default)
    {
        var request = new GetGroupUsersRequest { GroupId = groupId.ToString(), Query = query, PageSize = limit };
        return (await _api.CallAsync(o => _api.Groups.GetGroupUsersAsync(request, o), ct).ConfigureAwait(false)).Members.Select(m => m.ToModel()).ToList();
    }

    /// <summary>Members who can see a channel, split into sections the way the members panel shows them.</summary>
    /// <param name="limit">0 for the server's default.</param>
    public async Task<ChannelMemberList> GetChannelMembersAsync(Guid groupId, Guid channelId, int offset = 0, int limit = 0, CancellationToken ct = default)
    {
        var request = new GetChannelMemberListRequest { GroupId = groupId.ToString(), ChannelId = channelId.ToString(), Offset = offset, Limit = limit };
        return (await _api.CallAsync(o => _api.Groups.GetChannelMemberListAsync(request, o), ct).ConfigureAwait(false)).ToModel();
    }

    /// <summary>
    /// What a member may do after roles and channel overwrites. Check it with
    /// <see cref="PermissionsExtensions.Has"/>, which knows that <see cref="Permissions.Administrator"/> allows everything.
    /// </summary>
    /// <param name="userId">Whose permissions; <c>null</c> for the bot itself.</param>
    /// <param name="channelId">In which channel; <c>null</c> for group-level permissions.</param>
    public async Task<Permissions> GetEffectivePermissionsAsync(Guid groupId, Guid? userId = null, Guid? channelId = null, CancellationToken ct = default)
    {
        var request = new GetEffectivePermissionsRequest { GroupId = groupId.ToString() };
        if (userId is { } user) request.ActorId = user.ToString();
        if (channelId is { } channel) request.ChannelId = channel.ToString();
        return (Permissions)(ulong)(await _api.CallAsync(o => _api.Groups.GetEffectivePermissionsAsync(request, o), ct).ConfigureAwait(false)).Permissions;
    }

    /// <summary>Removes a member from a group (requires <c>KickMembers</c>).</summary>
    public async Task KickUserAsync(Guid groupId, Guid userId, CancellationToken ct = default) =>
        await _api.CallAsync(o => _api.Groups.KickUserAsync(new KickUserRequest { GroupId = groupId.ToString(), UserId = userId.ToString() }, o), ct).ConfigureAwait(false);

    public async Task BanUserAsync(Guid groupId, Guid userId, string reason = "", CancellationToken ct = default) =>
        await _api.CallAsync(o => _api.Groups.BanUserAsync(new BanUserRequest { GroupId = groupId.ToString(), UserId = userId.ToString(), Reason = reason }, o), ct).ConfigureAwait(false);

    public async Task UnbanUserAsync(Guid groupId, Guid userId, CancellationToken ct = default) =>
        await _api.CallAsync(o => _api.Groups.UnbanUserAsync(new UnbanUserRequest { GroupId = groupId.ToString(), UserId = userId.ToString() }, o), ct).ConfigureAwait(false);

    public async Task<Page<GroupBan>> GetBansAsync(Guid groupId, int pageSize = 0, string? pageToken = null, CancellationToken ct = default)
    {
        var request = new GetGroupBansRequest { GroupId = groupId.ToString(), PageSize = pageSize, PageToken = pageToken ?? "" };
        var response = await _api.CallAsync(o => _api.Groups.GetGroupBansAsync(request, o), ct).ConfigureAwait(false);
        return new Page<GroupBan>(response.Bans.Select(b => b.ToModel()).ToList(), Wire.Text(response.NextPageToken));
    }

    public async Task<Page<Invite>> GetInvitesAsync(Guid groupId, int pageSize = 0, string? pageToken = null, CancellationToken ct = default)
    {
        var request = new GetGroupInvitesRequest { GroupId = groupId.ToString(), PageSize = pageSize, PageToken = pageToken ?? "" };
        var response = await _api.CallAsync(o => _api.Groups.GetGroupInvitesAsync(request, o), ct).ConfigureAwait(false);
        return new Page<Invite>(response.Invites.Select(i => i.ToModel()).ToList(), Wire.Text(response.NextPageToken));
    }

    /// <param name="expiresAt"><c>null</c> for an invite that never expires.</param>
    /// <param name="maxUses">0 for unlimited.</param>
    public async Task<Invite> CreateInviteAsync(Guid groupId, DateTimeOffset? expiresAt = null, long maxUses = 0, CancellationToken ct = default)
    {
        var request = new CreateInviteRequest { GroupId = groupId.ToString(), MaxUses = maxUses };
        if (expiresAt is { } expires) request.ExpiresAt = Wire.Timestamp(expires);
        return (await _api.CallAsync(o => _api.Groups.CreateInviteAsync(request, o), ct, Idempotency.Unsafe).ConfigureAwait(false)).ToModel();
    }

    public async Task<Invite> UpdateInviteAsync(Guid inviteId, long maxUses, DateTimeOffset? expiresAt = null, CancellationToken ct = default)
    {
        var request = new UpdateInviteRequest { Id = inviteId.ToString(), MaxUses = maxUses };
        if (expiresAt is { } expires) request.ExpiresAt = Wire.Timestamp(expires);
        return (await _api.CallAsync(o => _api.Groups.UpdateInviteAsync(request, o), ct).ConfigureAwait(false)).ToModel();
    }

    public async Task DeleteInviteAsync(Guid inviteId, CancellationToken ct = default) =>
        await _api.DeleteAsync(o => _api.Groups.DeleteInviteAsync(new DeleteInviteRequest { Id = inviteId.ToString() }, o), ct).ConfigureAwait(false);

    /// <summary>Most recent audit log entries.</summary>
    /// <param name="beforeId">Entries older than this one, to page back in time.</param>
    public async Task<IReadOnlyList<AuditLogEntry>> GetAuditLogAsync(Guid groupId, int limit = 50, Guid? beforeId = null, CancellationToken ct = default)
    {
        var request = new GetGroupAuditLogRequest { GroupId = groupId.ToString(), Limit = limit };
        if (beforeId is { } before) request.BeforeId = before.ToString();
        return (await _api.CallAsync(o => _api.Groups.GetGroupAuditLogAsync(request, o), ct).ConfigureAwait(false)).Entries.Select(e => e.ToModel()).ToList();
    }
}
