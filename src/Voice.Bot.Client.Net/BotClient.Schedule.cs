using Microsoft.Extensions.Logging;

namespace Voice.Bot.Client.Net;

/// <summary>Scheduled tasks: <see cref="Every"/> and <see cref="At"/>.</summary>
public sealed partial class BotClient
{
    /// <summary>
    /// Runs <paramref name="task"/> every <paramref name="interval"/>, the first time one interval after
    /// the first <see cref="Ready"/>. The interval is a pause between runs (fixed delay): it is counted from
    /// the end of one run to the start of the next, so a run that takes a while pushes the later ones back
    /// and runs never overlap. A failing run is logged and reported to <see cref="ConnectionError"/>; the
    /// schedule goes on. Stops on disposal or when the returned handle is disposed - not on
    /// <see cref="StopGatewayAsync"/>, so it survives a gateway restart.
    /// </summary>
    /// <param name="interval">Any positive length; more than about 49 days (the most one timer can wait) is fine too.</param>
    /// <example>
    /// <code>
    /// client.Every(TimeSpan.FromMinutes(5), async ct =&gt;
    ///     await client.UpdateProfileAsync(statusText: $"Up {uptime.Elapsed:hh\:mm}", ct: ct));
    /// </code>
    /// </example>
    public IDisposable Every(TimeSpan interval, Func<CancellationToken, Task> task)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(interval, MaxInterval);
        return Schedule(now => now + interval, task, $"every {interval}");
    }

    /// <summary>
    /// Runs <paramref name="task"/> every day at <paramref name="time"/>, starting after the first
    /// <see cref="Ready"/>. Each run is planned from the previous one's planned moment, one calendar day
    /// later in <paramref name="timeZone"/>, and waited for in UTC: a timer that fires a little early never
    /// runs the same day twice, and a daylight-saving change neither repeats nor skips a run (a time that
    /// doesn't exist that day runs an hour later). Same error and stop rules as <see cref="Every"/>.
    /// </summary>
    /// <param name="timeZone">The zone <paramref name="time"/> is in; <c>null</c> for the machine's local time.</param>
    /// <example><c>client.At(new TimeOnly(9, 0), ct =&gt; client.SendMessageAsync(newsId, "Good morning!", ct: ct));</c></example>
    public IDisposable At(TimeOnly time, Func<CancellationToken, Task> task, TimeZoneInfo? timeZone = null)
    {
        var zone = timeZone ?? TimeZoneInfo.Local;
        DateTimeOffset? planned = null;
        return Schedule(now => (planned = NextDailyRun(planned, now, time, zone)).Value, task, $"daily at {time}");
    }

    /// <summary>
    /// The run after the one planned for <paramref name="planned"/>: the next <paramref name="time"/> after
    /// that slot - never the same slot again, even if the clock says it hasn't come yet - or after
    /// <paramref name="now"/> when that is later (the first run, or after a long pause).
    /// </summary>
    internal static DateTimeOffset NextDailyRun(DateTimeOffset? planned, DateTimeOffset now, TimeOnly time, TimeZoneInfo zone) =>
        NextOccurrence(planned is { } previous && previous > now ? previous : now, time, zone);

    /// <summary>The longest <see cref="Every"/> interval: about a hundred years.</summary>
    private static readonly TimeSpan MaxInterval = TimeSpan.FromDays(36_500);

    /// <summary>The longest single wait; longer waits are done in steps, since a timer can't wait more than about 49 days.</summary>
    private static readonly TimeSpan MaxTimerStep = TimeSpan.FromDays(1);

    /// <summary>The first moment after <paramref name="now"/> when the clock in <paramref name="zone"/> shows <paramref name="time"/>.</summary>
    internal static DateTimeOffset NextOccurrence(DateTimeOffset now, TimeOnly time, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(now, zone);
        for (var day = 0; day < 3; day++)
        {
            var wall = local.Date.AddDays(day) + time.ToTimeSpan();
            if (zone.IsInvalidTime(wall)) wall = wall.AddHours(1);
            var candidate = new DateTimeOffset(wall, zone.GetUtcOffset(wall));
            if (candidate > now) return candidate;
        }

        return now.AddDays(1);
    }

    /// <param name="next">The moment of the next run, given the current UTC time.</param>
    private IDisposable Schedule(Func<DateTimeOffset, DateTimeOffset> next, Func<CancellationToken, Task> task, string description)
    {
        var stop = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _ = Task.Run(() => RunScheduleAsync(next, task, description, stop.Token));
        return new Unsubscriber(() =>
        {
            stop.Cancel();
            stop.Dispose();
        });
    }

    private async Task RunScheduleAsync(Func<DateTimeOffset, DateTimeOffset> next, Func<CancellationToken, Task> task, string description, CancellationToken ct)
    {
        try
        {
            await _firstReady.Task.WaitAsync(ct).ConfigureAwait(false);
            while (!ct.IsCancellationRequested)
            {
                await DelayUntilAsync(next(DateTimeOffset.UtcNow), ct).ConfigureAwait(false);
                try
                {
                    await task(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Scheduled task ({Schedule}) failed", description);
                    await RaiseErrorAsync(ex).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scheduled task ({Schedule}) stopped", description);
            await RaiseErrorAsync(ex).ConfigureAwait(false);
        }
    }

    /// <summary>Waits until the clock reaches <paramref name="target"/>, in steps a timer can take.</summary>
    private static async Task DelayUntilAsync(DateTimeOffset target, CancellationToken ct)
    {
        while (target - DateTimeOffset.UtcNow is var left && left > TimeSpan.Zero)
        {
            await Task.Delay(left < MaxTimerStep ? left : MaxTimerStep, ct).ConfigureAwait(false);
        }
    }
}
