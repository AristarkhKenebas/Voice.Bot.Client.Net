using Voice.Bot.Client.Net.Internal;
using Wire_ = Vc.Backend.WebApi.Groups.Grpc;
using WireVoiceUser = Vc.Backend.Contracts.Grpc.ChannelVoiceUser;

namespace Voice.Bot.Client.Net.Models;

/// <summary>Who can join a group.</summary>
public enum AccessType
{
    Open = 0,
    Closed = 1,
    InviteOnly = 2,
    Hidden = 3,
}

/// <summary>How strictly a group checks new members.</summary>
public enum VerificationLevel
{
    None = 0,
    Low = 1,
    Medium = 2,
}

/// <summary>
/// Kind of channel. The wire format is a plain <c>int32</c>, so a value this version doesn't know yet
/// round-trips as an out-of-range <see cref="ChannelType"/> instead of throwing.
/// </summary>
public enum ChannelType
{
    Text = 0,
    Voice = 1,
    DirectMessage = 2,
    GroupMessage = 3,
    Tribune = 4,
    Posts = 5,
    Forum = 6,
    Events = 7,
    Board = 8,
}

/// <summary>A group the bot belongs to, as listed by <c>GetMyGroupsAsync</c>.</summary>
public sealed record Group(
    Guid Id,
    string Name,
    Guid OwnerId,
    string? AvatarUrl,
    string? BannerUrl,
    AccessType AccessType,
    DateTimeOffset? CreatedAt);

/// <summary>A group with its whole category and channel tree, as returned by <c>GetGroupAsync</c>.</summary>
/// <param name="VoiceRegion">Voice region id, <c>null</c> for automatic selection.</param>
/// <param name="IsOwner">Whether the bot owns this group.</param>
public sealed record GroupDetails(
    Guid Id,
    string Name,
    Guid OwnerId,
    string? AvatarUrl,
    string? BannerUrl,
    AccessType AccessType,
    VerificationLevel VerificationLevel,
    string? VoiceRegion,
    bool IsOwner,
    DateTimeOffset? CreatedAt,
    IReadOnlyList<Category> Categories)
{
    /// <summary>Every channel of every category, in order.</summary>
    public IEnumerable<Channel> Channels => Categories.SelectMany(c => c.Channels);
}

public sealed record Category(Guid Id, string Name, string Position, IReadOnlyList<Channel> Channels);

/// <param name="UserLimit">How many people fit into a voice channel, <c>null</c> for no limit.</param>
/// <param name="Bitrate">Voice channel bitrate, <c>null</c> for the default.</param>
/// <param name="SyncsCategoryPermissions">The channel inherits its permissions from the category.</param>
/// <param name="VoiceParticipants">Who is in the voice channel right now.</param>
public sealed record Channel(
    Guid Id,
    string Name,
    ChannelType Type,
    Guid? CategoryId,
    string Position,
    int? UserLimit,
    int? Bitrate,
    bool SyncsCategoryPermissions,
    IReadOnlyList<VoiceParticipant> VoiceParticipants);

public sealed record VoiceParticipant(User User, bool MicEnabled, bool CamEnabled, bool Deafened);

/// <summary>LiveKit address and token for joining a voice channel with a LiveKit client.</summary>
public sealed record VoiceConnection(string ServerUrl, string Token);

public sealed record GroupMember(User User, IReadOnlyList<Guid> RoleIds);

/// <summary>Members who can see a channel, split into sections the way the members panel shows them.</summary>
/// <param name="OfflineHidden">Offline members were left out of the list.</param>
public sealed record ChannelMemberList(
    IReadOnlyList<MemberListSection> Sections, IReadOnlyList<MemberListItem> Items, int Total, bool OfflineHidden);

/// <summary>A section of the member list - "online", "offline" or a hoisted role.</summary>
/// <param name="Count">How many members the whole section has.</param>
public sealed record MemberListSection(string Key, Guid? RoleId, int Count);

public sealed record MemberListItem(GroupMember Member, string SectionKey);

/// <param name="ExpiresAt"><c>null</c> for an invite that never expires.</param>
/// <param name="MaxUses">0 means unlimited.</param>
public sealed record Invite(
    Guid Id, Guid GroupId, Guid OwnerId, string Code, DateTimeOffset? CreatedAt, DateTimeOffset? ExpiresAt, long Uses, long MaxUses);

public sealed record GroupBan(User User, string? Reason, Guid? BannedByUserId, DateTimeOffset? BannedAt);

/// <param name="Changes">JSON with the changed fields, if the server sent any.</param>
public sealed record AuditLogEntry(
    Guid Id,
    Guid? ActorId,
    string ActorType,
    string ActorDisplayName,
    string ActionType,
    string? TargetId,
    string? TargetDisplayName,
    string? Changes,
    DateTimeOffset? CreatedAt);

/// <summary>One page of a long list. Pass <see cref="NextPageToken"/> back to the same method for the next page.</summary>
/// <param name="NextPageToken"><c>null</c> when there are no more pages.</param>
public sealed record Page<T>(IReadOnlyList<T> Items, string? NextPageToken)
{
    public bool HasMore => NextPageToken is not null;
}

internal static class GroupExtensions
{
    public static Group ToModel(this Wire_.Group wire) => new(
        Wire.Id(wire.Id),
        wire.Name,
        Wire.Id(wire.OwnerId),
        Wire.Text(wire.AvatarUrl),
        Wire.Text(wire.BannerUrl),
        (AccessType)(int)wire.AccessType,
        Wire.Time(wire.CreatedAt));

    public static GroupDetails ToModel(this Wire_.GroupDetails wire) => new(
        Wire.Id(wire.Id),
        wire.Name,
        Wire.Id(wire.OwnerId),
        Wire.Text(wire.AvatarUrl),
        Wire.Text(wire.BannerUrl),
        (AccessType)(int)wire.AccessType,
        (VerificationLevel)(int)wire.VerificationLevel,
        Wire.Text(wire.VoiceRegion),
        wire.IAmIsOwner,
        Wire.Time(wire.CreatedAt),
        wire.Categories.Select(c => c.ToModel()).ToList());

    public static Category ToModel(this Wire_.Category wire) =>
        new(Wire.Id(wire.Id), wire.Name, wire.Position, wire.Channels.Select(c => c.ToModel()).ToList());

    public static Channel ToModel(this Wire_.Channel wire) => new(
        Wire.Id(wire.Id),
        wire.Name,
        (ChannelType)wire.Type,
        Wire.OptionalId(wire.CategoryId),
        wire.Position,
        wire.HasUserLimit ? wire.UserLimit : null,
        wire.HasBitrate ? wire.Bitrate : null,
        wire.IsCategoryPermissionsSync,
        wire.Users.Select(u => u.ToModel()).ToList());

    public static VoiceParticipant ToModel(this WireVoiceUser wire) =>
        new(wire.User.ToModelOrEmpty(), wire.MicIsEnabled, wire.CamIsEnabled, wire.IsDeafened);

    public static GroupMember ToModel(this Wire_.GroupMember? wire) =>
        wire is null
            ? new GroupMember(((Vc.Backend.Contracts.Grpc.User?)null).ToModelOrEmpty(), [])
            : new GroupMember(wire.User.ToModelOrEmpty(), wire.RoleIds.Select(Wire.Id).ToList());

    public static ChannelMemberList ToModel(this Wire_.GetChannelMemberListResponse wire) => new(
        wire.Sections.Select(s => new MemberListSection(s.Key, s.HasRoleId ? Wire.OptionalId(s.RoleId) : null, s.Count)).ToList(),
        wire.Items.Select(i => new MemberListItem(i.Member.ToModel(), i.SectionKey)).ToList(),
        wire.Total,
        wire.OfflineHidden);

    public static Invite ToModel(this Wire_.Invite wire) => new(
        Wire.Id(wire.Id),
        Wire.Id(wire.GroupId),
        Wire.Id(wire.OwnerId),
        wire.InviteCode,
        Wire.Time(wire.CreatedAt),
        Wire.Time(wire.ExpiresAt),
        wire.UsesAmount,
        wire.MaxUseAmount);

    public static GroupBan ToModel(this Wire_.GroupBan wire) =>
        new(wire.User.ToModelOrEmpty(), Wire.Text(wire.Reason), Wire.OptionalId(wire.BannedByUserId), Wire.Time(wire.BannedAt));

    public static AuditLogEntry ToModel(this Wire_.GroupAuditLogEntry wire) => new(
        Wire.Id(wire.Id),
        Wire.OptionalId(wire.ActorId),
        wire.ActorType,
        wire.ActorDisplayName,
        wire.ActionType,
        wire.HasTargetId ? wire.TargetId : null,
        wire.HasTargetDisplayName ? wire.TargetDisplayName : null,
        wire.HasChanges ? wire.Changes : null,
        Wire.Time(wire.CreatedAt));
}
