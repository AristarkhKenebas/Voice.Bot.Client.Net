using Voice.Bot.Client.Net.Commands;
using Voice.Bot.Client.Net.Events;
using Voice.Bot.Client.Net.Models;

namespace Voice.Bot.Client.Net;

/// <summary>
/// Which events an <c>On...</c> helper passes on. Empty lists don't restrict anything. The bot's own
/// messages and reactions are always skipped.
/// </summary>
/// <param name="IgnoreBots">Skip messages from other bots; <c>null</c> for <see cref="BotClientOptions.IgnoreBots"/>.</param>
/// <param name="ChannelIds">Only these channels.</param>
/// <param name="GroupIds">Only these groups.</param>
public sealed record EventFilter(bool? IgnoreBots = null, IReadOnlyCollection<Guid>? ChannelIds = null, IReadOnlyCollection<Guid>? GroupIds = null)
{
    internal bool Allows(Guid? groupId, Guid? channelId) =>
        (ChannelIds is not { Count: > 0 } || channelId is { } channel && ChannelIds.Contains(channel)) &&
        (GroupIds is not { Count: > 0 } || groupId is { } group && GroupIds.Contains(group));
}

/// <summary>
/// Shortcuts over the typed events: filtered, without the bot's own activity, and returning an
/// <see cref="IDisposable"/> that unsubscribes. The typed events (<see cref="MessageCreated"/> ...) stay
/// available for everything else.
/// </summary>
public sealed partial class BotClient
{
    /// <summary>
    /// Calls <paramref name="handler"/> for every new message - not the bot's own, and by default not
    /// other bots' either (<see cref="BotClientOptions.IgnoreBots"/>).
    /// </summary>
    /// <example>
    /// <code>
    /// client.OnMessage(async ctx =&gt;
    /// {
    ///     if (ctx.Message.Content.Contains("hello", StringComparison.OrdinalIgnoreCase)) await ctx.ReactAsync("👋");
    /// });
    /// </code>
    /// </example>
    public IDisposable OnMessage(Func<CommandContext, Task> handler, EventFilter? filter = null)
    {
        filter ??= new EventFilter();
        return Subscribe<MessageCreatedEvent>(async created =>
        {
            var message = created.Message;
            if (!filter.Allows(created.GroupId, message.ChannelId)) return;
            if (message.Author.IsBot && ((filter.IgnoreBots ?? Options.IgnoreBots) || await IsMeAsync(message.Author.Id).ConfigureAwait(false))) return;
            await handler(new CommandContext(this, message, created.GroupId)).ConfigureAwait(false);
        }, h => MessageCreated += h, h => MessageCreated -= h);
    }

    /// <summary>Calls <paramref name="handler"/> when someone joins a group.</summary>
    public IDisposable OnMemberJoin(Func<MemberEvent, Task> handler, EventFilter? filter = null) =>
        Subscribe<MemberEvent>(Filtered(handler, filter, e => (e.GroupId, null)), h => MemberJoined += h, h => MemberJoined -= h);

    /// <summary>Calls <paramref name="handler"/> when someone leaves a group or is kicked from it.</summary>
    public IDisposable OnMemberLeave(Func<MemberEvent, Task> handler, EventFilter? filter = null)
    {
        var filtered = Filtered(handler, filter, e => (e.GroupId, null));
        var left = Subscribe<MemberEvent>(filtered, h => MemberLeft += h, h => MemberLeft -= h);
        var kicked = Subscribe<MemberEvent>(filtered, h => MemberKicked += h, h => MemberKicked -= h);
        return new Unsubscriber(() =>
        {
            left.Dispose();
            kicked.Dispose();
        });
    }

    /// <summary>Calls <paramref name="handler"/> when someone other than the bot adds a reaction.</summary>
    public IDisposable OnReactionAdd(Func<ReactionEvent, Task> handler, EventFilter? filter = null) =>
        Subscribe<ReactionEvent>(NotMine(Filtered(handler, filter, e => (e.GroupId, e.ChannelId))), h => ReactionAdded += h, h => ReactionAdded -= h);

    /// <summary>Calls <paramref name="handler"/> when someone other than the bot removes a reaction.</summary>
    public IDisposable OnReactionRemove(Func<ReactionEvent, Task> handler, EventFilter? filter = null) =>
        Subscribe<ReactionEvent>(NotMine(Filtered(handler, filter, e => (e.GroupId, e.ChannelId))), h => ReactionRemoved += h, h => ReactionRemoved -= h);

    /// <summary>Calls <paramref name="handler"/> on every successful (re)connection with the bot's profile. See <see cref="Ready"/>.</summary>
    public IDisposable OnReady(Func<User, Task> handler) => Subscribe(handler, h => Ready += h, h => Ready -= h);

    private async Task<bool> IsMeAsync(Guid userId) => userId == (await GetCachedMeAsync().ConfigureAwait(false)).Id;

    private static Func<T, Task> Filtered<T>(Func<T, Task> handler, EventFilter? filter, Func<T, (Guid? GroupId, Guid? ChannelId)> where) =>
        filter is null
            ? handler
            : evt =>
            {
                var (groupId, channelId) = where(evt);
                return filter.Allows(groupId, channelId) ? handler(evt) : Task.CompletedTask;
            };

    private Func<ReactionEvent, Task> NotMine(Func<ReactionEvent, Task> handler) => async reaction =>
    {
        if (!await IsMeAsync(reaction.UserId).ConfigureAwait(false)) await handler(reaction).ConfigureAwait(false);
    };

    private static Unsubscriber Subscribe<T>(Func<T, Task> handler, Action<Func<T, Task>> add, Action<Func<T, Task>> remove)
    {
        add(handler);
        return new Unsubscriber(() => remove(handler));
    }

    private sealed class Unsubscriber(Action unsubscribe) : IDisposable
    {
        private Action? _unsubscribe = unsubscribe;

        public void Dispose() => Interlocked.Exchange(ref _unsubscribe, null)?.Invoke();
    }
}
