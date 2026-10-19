using System.Runtime.CompilerServices;
using Voice.Bot.Client.Net.Models;

namespace Voice.Bot.Client.Net;

/// <summary>
/// Every page of a long list as one <see cref="IAsyncEnumerable{T}"/>: the next page is requested only
/// when the previous one has been read, and an error ends the enumeration by throwing from
/// <c>MoveNextAsync</c>. An empty page, or a page token (or "before" id) the server already gave, ends it
/// too, so a server that goes round in circles can't make it loop forever.
/// </summary>
public sealed partial class BotClient
{
    /// <summary>Every member of a group.</summary>
    /// <param name="pageSize">Members per request; 0 for the server's default.</param>
    /// <example><c>await foreach (var member in client.EnumerateGroupMembersAsync(groupId)) { ... }</c></example>
    public IAsyncEnumerable<GroupMember> EnumerateGroupMembersAsync(Guid groupId, int pageSize = 0, CancellationToken ct = default) =>
        EnumeratePagesAsync((token, c) => GetGroupMembersAsync(groupId, pageSize, token, c), ct);

    /// <summary>Every ban of a group.</summary>
    public IAsyncEnumerable<GroupBan> EnumerateBansAsync(Guid groupId, int pageSize = 0, CancellationToken ct = default) =>
        EnumeratePagesAsync((token, c) => GetBansAsync(groupId, pageSize, token, c), ct);

    /// <summary>Every invite of a group.</summary>
    public IAsyncEnumerable<Invite> EnumerateInvitesAsync(Guid groupId, int pageSize = 0, CancellationToken ct = default) =>
        EnumeratePagesAsync((token, c) => GetInvitesAsync(groupId, pageSize, token, c), ct);

    /// <summary>Every role of a group.</summary>
    public IAsyncEnumerable<Role> EnumerateRolesAsync(Guid groupId, int pageSize = 0, CancellationToken ct = default) =>
        EnumeratePagesAsync((token, c) => GetRolesAsync(groupId, pageSize, token, c), ct);

    /// <summary>The audit log of a group, newest entry first.</summary>
    /// <param name="limit">Stop after this many entries; <c>null</c> for all of them.</param>
    /// <param name="pageSize">Entries per request.</param>
    public async IAsyncEnumerable<AuditLogEntry> EnumerateAuditLogAsync(
        Guid groupId, int? limit = null, int pageSize = 50, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var count = 0;
        Guid? before = null;
        var seen = new HashSet<Guid>();
        while (limit is null || count < limit)
        {
            var page = await GetAuditLogAsync(groupId, PageSize(pageSize, limit, count), before, ct).ConfigureAwait(false);
            if (page.Count == 0) yield break;
            foreach (var entry in page)
            {
                yield return entry;
                if (++count == limit) yield break;
            }

            if (!seen.Add(page[^1].Id)) yield break;
            before = page[^1].Id;
        }
    }

    /// <summary>A channel's message history, newest message first.</summary>
    /// <param name="limit">Stop after this many messages; <c>null</c> for the whole history.</param>
    /// <param name="beforeId">Start below this message; <c>null</c> for the newest one.</param>
    /// <param name="pageSize">Messages per request.</param>
    /// <example>
    /// <code>
    /// await foreach (var message in client.EnumerateMessagesAsync(channelId, limit: 500))
    ///     if (message.Author.Id == spammerId) await client.DeleteMessageAsync(message.Id);
    /// </code>
    /// </example>
    public async IAsyncEnumerable<Message> EnumerateMessagesAsync(
        Guid channelId, int? limit = null, Guid? beforeId = null, int pageSize = 50, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var count = 0;
        var before = beforeId;
        var seen = new HashSet<Guid>();
        if (beforeId is { } start) seen.Add(start);
        while (limit is null || count < limit)
        {
            var page = await GetChannelMessagesAsync(channelId, PageSize(pageSize, limit, count), before, ct).ConfigureAwait(false);
            if (page.Count == 0) yield break;
            for (var i = page.Count - 1; i >= 0; i--)
            {
                yield return page[i];
                if (++count == limit) yield break;
            }

            if (!seen.Add(page[0].Id)) yield break;
            before = page[0].Id;
        }
    }

    /// <summary>Every hit of a message search, page after page.</summary>
    public async IAsyncEnumerable<SearchHit> EnumerateSearchAsync(
        Guid groupId, MessageSearch search, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var page = search;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (search.PageToken is { } first) seen.Add(first);
        while (true)
        {
            var results = await SearchMessagesAsync(groupId, page, ct).ConfigureAwait(false);
            foreach (var hit in results.Hits) yield return hit;
            if (results.NextPageToken is not { } next || results.Hits.Count == 0 || !seen.Add(next)) yield break;
            page = page with { PageToken = next };
        }
    }

    private static int PageSize(int pageSize, int? limit, int count) => limit is { } max ? Math.Min(pageSize, max - count) : pageSize;

    internal static async IAsyncEnumerable<T> EnumeratePagesAsync<T>(
        Func<string?, CancellationToken, Task<Page<T>>> fetch, [EnumeratorCancellation] CancellationToken ct = default)
    {
        string? token = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            var page = await fetch(token, ct).ConfigureAwait(false);
            foreach (var item in page.Items) yield return item;
            if (!page.HasMore || page.Items.Count == 0 || page.NextPageToken is not { } next || !seen.Add(next)) yield break;
            token = next;
        }
    }
}
