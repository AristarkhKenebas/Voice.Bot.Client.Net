namespace Voice.Bot.Client.Net;

/// <summary>
/// How <see cref="BotClient"/> repeats API calls that failed because of a rate limit or a network problem.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>A rate-limited call (<see cref="BotApiException.IsRateLimited"/>) was not processed by the server,
/// so it is retried for every method. The client waits <see cref="BotApiException.RetryAfter"/> when the
/// server sent it, otherwise the backoff pause. Over gRPC a rate limit usually arrives as
/// <see cref="Grpc.Core.StatusCode.Unavailable"/> carrying HTTP 429 without a hint.</item>
/// <item><see cref="Grpc.Core.StatusCode.Unavailable"/> and <see cref="Grpc.Core.StatusCode.DeadlineExceeded"/>
/// are retried only for safe calls - reads and changes that can be repeated without harm. Calls that create
/// something (a message, an upload, a role, ...) or that can't simply be repeated (joining by invite,
/// starting a voice connection) are not, so a lost response never produces a duplicate. A delete that
/// finds nothing to delete on a retry (<see cref="Grpc.Core.StatusCode.NotFound"/>) counts as done: the
/// attempt whose answer was lost deleted it.</item>
/// <item>Pauses are <see cref="BaseDelay"/>, then doubled each time (0.5 s, 1 s, 2 s, 4 s ...) up to
/// <see cref="MaxDelay"/>, plus up to <see cref="Jitter"/> random extra.</item>
/// <item>Retries never outlive the caller's <see cref="CancellationToken"/>, and a call the caller
/// cancelled is never retried. Every attempt has its own deadline, <see cref="BotClientOptions.RequestTimeout"/>.</item>
/// </list>
/// Set it for the whole client with <see cref="BotClientOptions.Retry"/>, or for a block of code with
/// <see cref="BotClient.WithRetries"/>.
/// </remarks>
/// <example>
/// <code>
/// services.AddVoiceBotClient(o => o.Retry = new RetryOptions { MaxRetries = 5 });
///
/// using (client.WithRetries(RetryOptions.None))
/// {
///     await client.SendMessageAsync(channelId, "no retries for this one");
/// }
/// </code>
/// </example>
public sealed record RetryOptions
{
    /// <summary>No retries: the first failure goes straight to the caller.</summary>
    public static RetryOptions None { get; } = new() { MaxRetries = 0 };

    /// <summary>How many times to repeat a call after the first failure; 0 disables retries. Default 3.</summary>
    public int MaxRetries { get; init; } = 3;

    /// <summary>Pause before the first retry; it doubles for each next one. Default 0.5 s.</summary>
    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// The longest pause. When the server asks to wait longer than this, the error goes straight to the
    /// caller instead. Default 30 s.
    /// </summary>
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Random extra added to each pause, as a share of it: 0.2 means up to +20%. Default 0.2.</summary>
    public double Jitter { get; init; } = 0.2;

    /// <summary>The pause before retry number <paramref name="attempt"/> (0 for the first retry), ignoring any server hint.</summary>
    public TimeSpan Backoff(int attempt)
    {
        var exponent = Math.Clamp(attempt, 0, 30);
        var raw = Math.Min(BaseDelay.TotalMilliseconds * (1L << exponent), MaxDelay.TotalMilliseconds);
        var jittered = raw * (1 + Random.Shared.NextDouble() * Math.Clamp(Jitter, 0, 1));
        return TimeSpan.FromMilliseconds(Math.Min(jittered, MaxDelay.TotalMilliseconds));
    }
}

/// <summary>
/// Client-side request throttle (a token bucket), off by default. The server allows 300 requests per
/// 10 seconds from one IP address; with a throttle the client slows itself down before hitting the limit
/// instead of being rate-limited.
/// </summary>
/// <example><c>services.AddVoiceBotClient(o =&gt; o.Throttle = new ThrottleOptions());</c></example>
public sealed record ThrottleOptions
{
    /// <summary>How many requests may be made per <see cref="Per"/>. Default 300.</summary>
    public int Permits { get; init; } = 300;

    /// <summary>The window in which all <see cref="Permits"/> come back. Default 10 s.</summary>
    public TimeSpan Per { get; init; } = TimeSpan.FromSeconds(10);
}
