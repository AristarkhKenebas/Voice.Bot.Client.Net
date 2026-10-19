using Voice.Bot.Client.Net.Events;
using Voice.Bot.Client.Net.Models;

namespace Voice.Bot.Client.Net;

/// <summary>
/// In-memory cache of the bot's groups (with their channels), roles and members, at
/// <see cref="BotClient.Cache"/>. Everything is fetched on first use (lookups running at the same time
/// share one fetch) and dropped as soon as a gateway event says it changed - before any handler sees
/// that event, so a lookup never returns data from before it. A <see cref="GroupStructureChangedEvent"/>
/// drops the group; member events only touch the member they are about: someone leaving is removed from
/// the remembered list, someone joining (or changing their avatar) makes the list load again only when
/// that member or the whole list is asked for, and other profile changes are written into it. Every
/// gateway reconnection clears everything, since events were missed meanwhile. With
/// <see cref="BotClientOptions.EnableCache"/> off, every lookup asks the server.
/// </summary>
/// <example>
/// <code>
/// var general = await client.Cache.GetChannelByNameAsync(groupId, "general");
/// var mods = await client.Cache.GetRoleByNameAsync(groupId, "Moderators");
/// var member = await client.Cache.GetMemberAsync(groupId, userId);
/// </code>
/// </example>
public sealed class BotCache
{
    private readonly BotClient _client;
    private readonly object _lock = new();
    private readonly Dictionary<Guid, Task<GroupDetails>> _groups = [];
    private readonly Dictionary<Guid, Task<IReadOnlyList<Role>>> _roles = [];
    private readonly Dictionary<Guid, Task<IReadOnlyDictionary<Guid, GroupMember>>> _members = [];
    private readonly Dictionary<Guid, HashSet<Guid>> _staleMembers = [];
    private Task<IReadOnlyList<Group>>? _myGroups;

    internal BotCache(BotClient client, bool enabled)
    {
        _client = client;
        Enabled = enabled;
    }

    /// <summary>Whether lookups are remembered (<see cref="BotClientOptions.EnableCache"/>).</summary>
    public bool Enabled { get; }

    /// <summary>The groups the bot belongs to.</summary>
    public Task<IReadOnlyList<Group>> GetGroupsAsync(CancellationToken ct = default)
    {
        if (!Enabled) return _client.GetMyGroupsAsync(ct);
        Task<IReadOnlyList<Group>> task;
        lock (_lock)
        {
            if (_myGroups is null)
            {
                var created = _myGroups = _client.GetMyGroupsAsync(CancellationToken.None);
                ForgetOnFailure(created, () =>
                {
                    if (_myGroups == created) _myGroups = null;
                });
            }

            task = _myGroups;
        }

        return task.WaitAsync(ct);
    }

    /// <summary>A group with its categories and channels.</summary>
    public Task<GroupDetails> GetGroupAsync(Guid groupId, CancellationToken ct = default) =>
        Get(_groups, groupId, () => _client.GetGroupAsync(groupId, CancellationToken.None), ct);

    /// <summary>A channel of any of the bot's groups, <c>null</c> if the bot doesn't see it.</summary>
    public async Task<Channel?> GetChannelAsync(Guid channelId, CancellationToken ct = default) =>
        (await FindChannelAsync(channelId, ct).ConfigureAwait(false))?.Channel;

    /// <summary>The group a channel belongs to, <c>null</c> if the bot doesn't see the channel.</summary>
    public async Task<Guid?> GetChannelGroupIdAsync(Guid channelId, CancellationToken ct = default) =>
        (await FindChannelAsync(channelId, ct).ConfigureAwait(false))?.GroupId;

    /// <summary>A channel by name, ignoring case and a leading <c>#</c>; <c>null</c> if there is none.</summary>
    public async Task<Channel?> GetChannelByNameAsync(Guid groupId, string name, CancellationToken ct = default)
    {
        var wanted = name.TrimStart('#');
        var group = await GetGroupAsync(groupId, ct).ConfigureAwait(false);
        return group.Channels.FirstOrDefault(c => string.Equals(c.Name, wanted, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Every role of a group.</summary>
    public Task<IReadOnlyList<Role>> GetRolesAsync(Guid groupId, CancellationToken ct = default) =>
        Get(_roles, groupId, () => LoadRolesAsync(groupId), ct);

    /// <summary>A role by id, <c>null</c> if the group has no such role.</summary>
    public async Task<Role?> GetRoleAsync(Guid groupId, Guid roleId, CancellationToken ct = default) =>
        (await GetRolesAsync(groupId, ct).ConfigureAwait(false)).FirstOrDefault(r => r.Id == roleId);

    /// <summary>A role by name, ignoring case and a leading <c>@</c>; <c>null</c> if there is none.</summary>
    public async Task<Role?> GetRoleByNameAsync(Guid groupId, string name, CancellationToken ct = default)
    {
        var wanted = name.TrimStart('@');
        return (await GetRolesAsync(groupId, ct).ConfigureAwait(false))
            .FirstOrDefault(r => string.Equals(r.Name, wanted, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A member of a group with their roles, <c>null</c> if the user isn't a member.</summary>
    /// <remarks>The first lookup in a group loads its whole member list, page by page.</remarks>
    public async Task<GroupMember?> GetMemberAsync(Guid groupId, Guid userId, CancellationToken ct = default)
    {
        ForgetStaleMembers(groupId, userId);
        return (await Get(_members, groupId, () => LoadMembersAsync(groupId), ct).ConfigureAwait(false)).GetValueOrDefault(userId);
    }

    /// <summary>Every member of a group.</summary>
    public async Task<IReadOnlyCollection<GroupMember>> GetMembersAsync(Guid groupId, CancellationToken ct = default)
    {
        ForgetStaleMembers(groupId, null);
        return [.. (await Get(_members, groupId, () => LoadMembersAsync(groupId), ct).ConfigureAwait(false)).Values];
    }

    /// <summary>Forgets everything.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _groups.Clear();
            _roles.Clear();
            _members.Clear();
            _staleMembers.Clear();
            _myGroups = null;
        }
    }

    /// <summary>Drops (or updates) what <paramref name="evt"/> changed. Called for every gateway event before the handlers see it.</summary>
    internal void Invalidate(GatewayEventKind kind, object evt)
    {
        if (!Enabled) return;
        lock (_lock)
        {
            switch (evt)
            {
                case GroupStructureChangedEvent changed:
                    DropGroup(changed.GroupId);
                    break;
                case MemberEvent member when member.UserId == _client.CachedBotId:
                    DropGroup(member.GroupId);
                    break;
                case MemberEvent member when kind == GatewayEventKind.MemberJoined:
                    MarkStale(member.GroupId, member.UserId);
                    break;
                case MemberEvent member:
                    UpdateMember(member.GroupId, member.UserId, _ => null);
                    break;
                case UserProfileUpdatedEvent profile:
                    foreach (var groupId in profile.GroupId is { } one ? [one] : _members.Keys.ToList())
                    {
                        if (profile.AvatarId is not null || profile.BannerId is not null) MarkStale(groupId, profile.UserId);
                        else UpdateMember(groupId, profile.UserId, current => current with { User = Apply(current.User, profile) });
                    }

                    break;
            }
        }
    }

    private void DropGroup(Guid groupId)
    {
        _groups.Remove(groupId);
        _roles.Remove(groupId);
        _members.Remove(groupId);
        _staleMembers.Remove(groupId);
        _myGroups = null;
    }

    /// <summary>
    /// Changes one member in the remembered list, <c>null</c> from <paramref name="change"/> removing them. A
    /// list that is still loading may or may not have the change, so its member is marked stale instead.
    /// </summary>
    private void UpdateMember(Guid groupId, Guid userId, Func<GroupMember, GroupMember?> change)
    {
        if (!_members.TryGetValue(groupId, out var task)) return;
        if (!task.IsCompletedSuccessfully)
        {
            MarkStale(groupId, userId);
            return;
        }

        if (!task.Result.TryGetValue(userId, out var current)) return;
        var members = new Dictionary<Guid, GroupMember>(task.Result);
        if (change(current) is { } changed) members[userId] = changed;
        else members.Remove(userId);
        _members[groupId] = Task.FromResult<IReadOnlyDictionary<Guid, GroupMember>>(members);
    }

    /// <summary>Remembers that the list doesn't know <paramref name="userId"/> right; asking for them (or the whole list) loads it again.</summary>
    private void MarkStale(Guid groupId, Guid userId)
    {
        if (!_members.ContainsKey(groupId)) return;
        if (!_staleMembers.TryGetValue(groupId, out var stale)) _staleMembers[groupId] = stale = [];
        stale.Add(userId);
    }

    /// <param name="userId">The member asked for; <c>null</c> for the whole list.</param>
    private void ForgetStaleMembers(Guid groupId, Guid? userId)
    {
        if (!Enabled) return;
        lock (_lock)
        {
            if (!_staleMembers.TryGetValue(groupId, out var stale) || userId is { } one && !stale.Contains(one)) return;
            _staleMembers.Remove(groupId);
            _members.Remove(groupId);
        }
    }

    private static User Apply(User user, UserProfileUpdatedEvent profile) => user with
    {
        DisplayName = profile.DisplayName ?? user.DisplayName,
        Description = profile.Description ?? user.Description,
        StatusText = profile.StatusText ?? user.StatusText,
        Status = profile.Status ?? user.Status,
    };

    private async Task<(Channel Channel, Guid GroupId)?> FindChannelAsync(Guid channelId, CancellationToken ct)
    {
        foreach (var group in await GetGroupsAsync(ct).ConfigureAwait(false))
        {
            var details = await GetGroupAsync(group.Id, ct).ConfigureAwait(false);
            if (details.Channels.FirstOrDefault(c => c.Id == channelId) is { } channel) return (channel, group.Id);
        }

        return null;
    }

    /// <summary>
    /// The remembered task for <paramref name="key"/>, or a new fetch. A failed fetch is forgotten so the
    /// next lookup tries again; an invalidated one is replaced by the next lookup.
    /// </summary>
    private Task<T> Get<T>(Dictionary<Guid, Task<T>> store, Guid key, Func<Task<T>> fetch, CancellationToken ct)
    {
        if (!Enabled) return fetch().WaitAsync(ct);
        Task<T> task;
        lock (_lock)
        {
            if (!store.TryGetValue(key, out task!))
            {
                var created = store[key] = task = fetch();
                ForgetOnFailure(created, () =>
                {
                    if (store.TryGetValue(key, out var current) && current == created) store.Remove(key);
                });
            }
        }

        return task.WaitAsync(ct);
    }

    /// <summary>Runs <paramref name="forget"/> under the lock when <paramref name="task"/> fails. Call after storing the task.</summary>
    private void ForgetOnFailure(Task task, Action forget) =>
        task.ContinueWith(
            _ => { lock (_lock) forget(); },
            CancellationToken.None, TaskContinuationOptions.NotOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    private async Task<IReadOnlyList<Role>> LoadRolesAsync(Guid groupId)
    {
        var roles = new List<Role>();
        await foreach (var role in _client.EnumerateRolesAsync(groupId).ConfigureAwait(false)) roles.Add(role);
        return roles;
    }

    private async Task<IReadOnlyDictionary<Guid, GroupMember>> LoadMembersAsync(Guid groupId)
    {
        var members = new Dictionary<Guid, GroupMember>();
        await foreach (var member in _client.EnumerateGroupMembersAsync(groupId).ConfigureAwait(false))
        {
            if (member.User.Id != Guid.Empty) members[member.User.Id] = member;
        }

        return members;
    }
}
