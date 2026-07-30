using Voice.Bot.Client.Net.Models;

namespace Voice.Bot.Client.Net.Events;

/// <summary>
/// Channel within <see cref="GroupCategorySnapshot"/>. <see cref="Users"/> mirrors the same defensive
/// raw-domain-model caveat as <see cref="UserSnapshot"/> - see there.
/// </summary>
public sealed record GroupChannelSnapshot(
    Guid Id,
    Guid? CategoryId,
    string Name,
    ChannelType Type,
    string Position,
    IReadOnlyList<UserSnapshot> Users);

public sealed record GroupCategorySnapshot(
    Guid Id,
    string Name,
    string Position,
    IReadOnlyList<GroupChannelSnapshot> Channels);

/// <summary>Mirrors the backend's <c>GroupDetailsEvent</c> - the <c>group-events</c> payload for <c>"UserJoined"</c>.</summary>
public sealed record GroupSnapshot(
    Guid Id,
    Guid OwnerId,
    string Name,
    DateTimeOffset CreatedAt,
    Guid? AvatarId,
    Guid? BannerId,
    IReadOnlyList<GroupCategorySnapshot> Categories);

/// <summary>
/// <c>group-events</c> payload wrapper for <c>"UserJoined"</c> (the only confirmed <c>Type</c> on this
/// topic today - any other <c>Type</c> degrades to <see cref="UnknownGatewayEvent"/> instead of
/// failing). <see cref="UserId"/> is the joining user/bot's id from the event envelope.
/// </summary>
public sealed record GroupJoinedEvent(Guid GroupId, Guid UserId, GroupSnapshot Group);
