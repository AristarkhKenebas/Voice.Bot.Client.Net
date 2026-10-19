using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Voice.Bot.Client.Net.Events;

namespace Voice.Bot.Client.Net;

/// <summary>Waiting for gateway events: <see cref="WaitForAsync{T}"/> and <see cref="CollectReactionsAsync"/>.</summary>
public sealed partial class BotClient
{
    private readonly object _waitersLock = new();
    private readonly List<Waiter> _waiters = [];
    private readonly ConditionalWeakTable<MessageCreatedEvent, object> _answers = new();

    /// <summary>
    /// Waits for the next gateway event of type <typeparamref name="T"/> that matches
    /// <paramref name="predicate"/>. Events are matched as they arrive, so this works inside an event or
    /// command handler too, without waiting for that handler to finish - and from the moment it starts
    /// waiting, that handler no longer holds up the events after it. The bot's own messages and reactions
    /// never match, like in the <c>On...</c> helpers.
    /// </summary>
    /// <typeparam name="T">
    /// An event record, e.g. <see cref="MessageCreatedEvent"/> or <see cref="ReactionEvent"/> (added and
    /// removed reactions alike).
    /// </typeparam>
    /// <param name="predicate">Which event to take; <c>null</c> for the first one.</param>
    /// <param name="timeout">How long to wait; <c>null</c> for no limit.</param>
    /// <exception cref="TimeoutException">No matching event within <paramref name="timeout"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled or the client was disposed.</exception>
    /// <example>
    /// <code>
    /// var answer = await client.WaitForAsync&lt;MessageCreatedEvent&gt;(
    ///     e =&gt; e.Message.Author.Id == userId, TimeSpan.FromSeconds(30));
    /// </code>
    /// </example>
    public Task<T> WaitForAsync<T>(Func<T, bool>? predicate = null, TimeSpan? timeout = null, CancellationToken ct = default)
        where T : class =>
        WaitForEventAsync(null, predicate, timeout, ct);

    /// <param name="kind">Only events of this kind - tells added reactions from removed ones; <c>null</c> for any.</param>
    /// <param name="taken">Called with the event when it ended the wait, before any handler sees it.</param>
    internal async Task<T> WaitForEventAsync<T>(
        GatewayEventKind? kind, Func<T, bool>? predicate, TimeSpan? timeout, CancellationToken ct, Action<T>? taken = null)
        where T : class
    {
        var found = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiter = new Waiter((eventKind, evt) =>
        {
            if (evt is not T typed || kind is not null && eventKind != kind) return false;
            try
            {
                if (predicate is not null && !predicate(typed)) return false;
                if (found.TrySetResult(typed)) taken?.Invoke(typed);
            }
            catch (Exception ex)
            {
                found.TrySetException(ex);
            }

            return true;
        }, () => found.TrySetCanceled());

        AddWaiter(waiter);
        ReleaseDispatch();
        try
        {
            return timeout is { } limit && limit != Timeout.InfiniteTimeSpan
                ? await found.Task.WaitAsync(limit, ct).ConfigureAwait(false)
                : await found.Task.WaitAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            RemoveWaiter(waiter);
        }
    }

    /// <summary>
    /// Every reaction added to a message during <paramref name="duration"/>, as it arrives - not the bot's
    /// own. Like <see cref="WaitForAsync{T}"/>, a handler collecting reactions doesn't hold up other events.
    /// </summary>
    /// <example>
    /// <code>
    /// await foreach (var reaction in client.CollectReactionsAsync(poll.Id, TimeSpan.FromMinutes(1)))
    ///     votes[reaction.Emoji] = votes.GetValueOrDefault(reaction.Emoji) + 1;
    /// </code>
    /// </example>
    public async IAsyncEnumerable<ReactionEvent> CollectReactionsAsync(
        Guid messageId, TimeSpan duration, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var collected = Channel.CreateUnbounded<ReactionEvent>();
        var waiter = new Waiter((kind, evt) =>
        {
            if (kind == GatewayEventKind.ReactionAdded && evt is ReactionEvent reaction && reaction.MessageId == messageId)
            {
                collected.Writer.TryWrite(reaction);
            }

            return false;
        }, () => collected.Writer.TryComplete());

        AddWaiter(waiter);
        ReleaseDispatch();
        using var timer = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timer.CancelAfter(duration);
        using var stop = timer.Token.Register(() => collected.Writer.TryComplete());
        try
        {
            await foreach (var reaction in collected.Reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
            {
                yield return reaction;
            }
        }
        finally
        {
            RemoveWaiter(waiter);
        }

        ct.ThrowIfCancellationRequested();
    }

    /// <summary>Marks a message as the answer to <see cref="BotContext.AskAsync"/>, so it doesn't also run as a text command.</summary>
    internal void MarkAnswer(MessageCreatedEvent created) => _answers.AddOrUpdate(created, created);

    /// <summary>Whether <see cref="BotContext.AskAsync"/> took this message as its answer.</summary>
    internal bool IsAnswer(MessageCreatedEvent created) => _answers.TryGetValue(created, out _);

    /// <summary>How many waits are registered - for tests.</summary>
    internal int PendingWaits
    {
        get
        {
            lock (_waitersLock) return _waiters.Count;
        }
    }

    private void AddWaiter(Waiter waiter)
    {
        lock (_waitersLock)
        {
            if (_disposed == 1)
            {
                waiter.Cancel();
                return;
            }

            _waiters.Add(waiter);
        }

        if (CachedBotId == Guid.Empty) _ = LearnBotIdAsync();
    }

    /// <summary>Fetches the bot's profile so <see cref="IsOwnEvent"/> can tell its own events apart.</summary>
    private async Task LearnBotIdAsync()
    {
        try
        {
            await GetCachedMeAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is BotApiException or OperationCanceledException or ObjectDisposedException)
        {
            _logger.LogDebug(ex, "Could not fetch the bot's profile");
        }
    }

    /// <summary>The bot's own messages and reactions, which waiters never get (the same rule as the <c>On...</c> helpers).</summary>
    private bool IsOwnEvent(object evt)
    {
        var me = CachedBotId;
        return me != Guid.Empty && evt switch
        {
            MessageCreatedEvent created => created.Message.Author.Id == me,
            MessageUpdatedEvent updated => updated.Message.Author.Id == me,
            ReactionEvent reaction => reaction.UserId == me,
            _ => false,
        };
    }

    private void RemoveWaiter(Waiter waiter)
    {
        lock (_waitersLock) _waiters.Remove(waiter);
    }

    private void OfferToWaiters(ParsedEvent parsed)
    {
        Waiter[] current;
        lock (_waitersLock)
        {
            if (_waiters.Count == 0) return;
            current = [.. _waiters];
        }

        if (IsOwnEvent(parsed.Event)) return;
        foreach (var waiter in current)
        {
            if (waiter.Offer(parsed.Kind, parsed.Event)) RemoveWaiter(waiter);
        }
    }

    private void CancelWaiters()
    {
        Waiter[] current;
        lock (_waitersLock)
        {
            current = [.. _waiters];
            _waiters.Clear();
        }

        foreach (var waiter in current) waiter.Cancel();
    }

    /// <param name="Offer">Looks at an event; returns <c>true</c> when the waiter is done.</param>
    /// <param name="Cancel">Ends the wait because the client is going away.</param>
    private sealed record Waiter(Func<GatewayEventKind, object, bool> Offer, Action Cancel);
}
