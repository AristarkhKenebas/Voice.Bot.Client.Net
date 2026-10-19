using System.Diagnostics;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace Voice.Bot.Client.Net.Internal;

/// <summary>Whether repeating a call after a lost response is harmless.</summary>
internal enum Idempotency
{
    /// <summary>Reads and changes that end in the same state when repeated - retried on network errors too.</summary>
    Safe,

    /// <summary>Calls that create something - retried only when rate-limited, since then the server did nothing.</summary>
    Unsafe,

    /// <summary>
    /// Deletes - retried like <see cref="Safe"/>; <see cref="StatusCode.NotFound"/> after a retry means the
    /// lost attempt got through, so it counts as done.
    /// </summary>
    Delete,
}

/// <summary>
/// Runs a call with <see cref="RetryOptions"/> and the optional <see cref="ThrottleOptions"/> token bucket.
/// <see cref="BotClient.WithRetries"/> overrides the options for the current async flow.
/// </summary>
internal sealed class Retrier(RetryOptions options, ThrottleOptions? throttle, ILogger logger)
{
    private static readonly AsyncLocal<(Retrier Owner, RetryOptions Options)?> Override = new();

    private readonly TokenBucket? _bucket = throttle is null ? null : new TokenBucket(throttle.Permits, throttle.Per);

    public RetryOptions Options => Override.Value is { } value && ReferenceEquals(value.Owner, this) ? value.Options : options;

    public IDisposable Use(RetryOptions retry)
    {
        var previous = Override.Value;
        Override.Value = (this, retry);
        return new Restore(previous);
    }

    /// <param name="throttled">Whether the call counts against <see cref="ThrottleOptions"/> - API calls do, storage uploads don't.</param>
    public async Task<T> RunAsync<T>(Func<Task<T>> call, Idempotency idempotency, string what, CancellationToken ct, bool throttled = true)
    {
        var retry = Options;
        var lostResponse = false;
        for (var attempt = 0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            if (throttled && _bucket is not null) await _bucket.AcquireAsync(ct).ConfigureAwait(false);
            try
            {
                return await call().ConfigureAwait(false);
            }
            catch (BotApiException ex) when (idempotency == Idempotency.Delete && lostResponse && ex.StatusCode == StatusCode.NotFound)
            {
                logger.LogDebug("{Call} found nothing to delete after a retry; the earlier attempt deleted it", what);
                return default!;
            }
            catch (BotApiException ex) when (Delay(ex, idempotency, attempt, retry) is { } delay)
            {
                lostResponse |= !ex.IsRateLimited;
                logger.LogWarning("{Call} failed with {Status} ({Message}); retry {Attempt} of {MaxRetries} in {Delay} ms",
                    what, ex.StatusCode, ex.Message, attempt + 1, retry.MaxRetries, (int)delay.TotalMilliseconds);
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>The pause before the next attempt, or <c>null</c> when the error should reach the caller.</summary>
    internal static TimeSpan? Delay(BotApiException error, Idempotency idempotency, int attempt, RetryOptions retry)
    {
        if (attempt >= retry.MaxRetries) return null;
        if (error.IsRateLimited)
        {
            if (error.RetryAfter is { } hint) return hint <= retry.MaxDelay ? hint : null;
            return retry.Backoff(attempt);
        }

        var transient = error.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded;
        return transient && idempotency != Idempotency.Unsafe ? retry.Backoff(attempt) : null;
    }

    private sealed class Restore((Retrier, RetryOptions)? previous) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) Override.Value = previous;
        }
    }
}

/// <summary>Token bucket: <c>permits</c> tokens refilled evenly over <c>per</c>; each call takes one.</summary>
internal sealed class TokenBucket
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly double _capacity;
    private readonly double _ticksPerToken;
    private double _tokens;
    private long _last = Stopwatch.GetTimestamp();

    public TokenBucket(int permits, TimeSpan per)
    {
        if (permits <= 0) throw new ArgumentOutOfRangeException(nameof(permits), "Permits must be positive.");
        if (per <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(per), "The window must be positive.");
        _capacity = _tokens = permits;
        _ticksPerToken = per.TotalSeconds * Stopwatch.Frequency / permits;
    }

    public async Task AcquireAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Refill();
            if (_tokens < 1)
            {
                var wait = TimeSpan.FromSeconds((1 - _tokens) * _ticksPerToken / Stopwatch.Frequency);
                await Task.Delay(wait, ct).ConfigureAwait(false);
                Refill();
            }

            _tokens = Math.Max(0, _tokens - 1);
        }
        finally
        {
            _lock.Release();
        }
    }

    private void Refill()
    {
        var now = Stopwatch.GetTimestamp();
        _tokens = Math.Min(_capacity, _tokens + (now - _last) / _ticksPerToken);
        _last = now;
    }
}
