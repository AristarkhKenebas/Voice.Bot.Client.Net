using Voice.Bot.Client.Net.Models;

namespace Voice.Bot.Client.Net;

/// <summary>Sending files in one call, and the typing indicator as a scope.</summary>
public sealed partial class BotClient
{
    /// <summary>Uploads a file from disk or memory; files on disk larger than one part go up in parts.</summary>
    public Task<AttachmentInfo> UploadAsync(FileUpload file, CancellationToken ct = default) =>
        file.Path is { } path
            ? UploadFileAsync(path, file.ContentType, file.FileName, ct: ct)
            : UploadAsync(file.FileName, file.Data, file.ContentType, ct);

    /// <summary>Uploads <paramref name="file"/> and posts it to a channel with an optional text.</summary>
    /// <example><c>await client.SendFileAsync(channelId, FileUpload.FromPath("chart.png"), "Today's chart");</c></example>
    public Task<Message> SendFileAsync(Guid channelId, FileUpload file, string content = "", Guid? replyTo = null, CancellationToken ct = default) =>
        SendFilesAsync(channelId, [file], content, replyTo, ct);

    /// <summary>Uploads every file, one after another, and posts them to a channel in one message.</summary>
    public async Task<Message> SendFilesAsync(
        Guid channelId, IEnumerable<FileUpload> files, string content = "", Guid? replyTo = null, CancellationToken ct = default)
    {
        var ids = new List<Guid>();
        foreach (var file in files)
        {
            ids.Add((await UploadAsync(file, ct).ConfigureAwait(false)).Id);
        }

        return await SendMessageAsync(channelId, content, replyTo, ids, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Shows "typing..." in a channel until the returned scope is disposed, refreshing it every few
    /// seconds. Indicator errors are ignored - they never break the work inside the scope.
    /// </summary>
    /// <example>
    /// <code>
    /// await using (client.BeginTyping(channelId))
    /// {
    ///     var report = await BuildReportAsync();
    /// }
    /// </code>
    /// </example>
    public TypingScope BeginTyping(Guid channelId) => new(this, channelId);
}

/// <summary>Keeps the typing indicator on while it is not disposed. Created by <see cref="BotClient.BeginTyping"/>.</summary>
public sealed class TypingScope : IAsyncDisposable, IDisposable
{
    /// <summary>How often the indicator is refreshed.</summary>
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);

    private readonly BotClient _client;
    private readonly Guid _channelId;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    private int _disposed;

    internal TypingScope(BotClient client, Guid channelId)
    {
        _client = client;
        _channelId = channelId;
        _loop = Task.Run(KeepTypingAsync);
    }

    private async Task KeepTypingAsync()
    {
        using var timer = new PeriodicTimer(RefreshInterval);
        do
        {
            try
            {
                using (_client.WithRetries(RetryOptions.None))
                {
                    await _client.TypingAsync(_channelId, true, _stop.Token).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is BotApiException or OperationCanceledException)
            {
            }
        }
        while (await WaitAsync(timer).ConfigureAwait(false));
    }

    private async Task<bool> WaitAsync(PeriodicTimer timer)
    {
        try
        {
            return await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>Stops refreshing and hides the indicator.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        await _stop.CancelAsync().ConfigureAwait(false);
        await _loop.ConfigureAwait(false);
        _stop.Dispose();
        try
        {
            using (_client.WithRetries(RetryOptions.None))
            {
                await _client.TypingAsync(_channelId, false).ConfigureAwait(false);
            }
        }
        catch (BotApiException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    /// <summary>Same as <see cref="DisposeAsync"/>, for a plain <c>using</c>.</summary>
    public void Dispose() => Task.Run(() => DisposeAsync().AsTask()).GetAwaiter().GetResult();
}
