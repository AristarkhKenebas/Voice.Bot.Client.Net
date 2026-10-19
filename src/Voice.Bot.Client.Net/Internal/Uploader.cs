using System.Net.Http;
using System.Net.Http.Headers;
using Grpc.Core;

namespace Voice.Bot.Client.Net.Internal;

/// <summary>Uploads file bytes to the pre-signed storage URLs the Attachments and Users APIs hand out.</summary>
internal sealed class Uploader : IDisposable
{
    private readonly HttpClient _http;
    private readonly TimeSpan? _timeout;
    private readonly Retrier? _retrier;

    /// <param name="retrier">Retries an upload that failed on the network (a PUT can be repeated safely); <c>null</c> for none.</param>
    public Uploader(HttpClient http, TimeSpan? timeout, Retrier? retrier = null)
    {
        _http = http;
        _http.Timeout = Timeout.InfiniteTimeSpan;
        _timeout = VoiceApi.Limit(timeout);
        _retrier = retrier;
    }

    /// <summary>Checks the content type before anything is created on the server.</summary>
    public static MediaTypeHeaderValue ParseContentType(string contentType) =>
        MediaTypeHeaderValue.TryParse(contentType, out var parsed)
            ? parsed
            : throw new ArgumentException($"'{contentType}' is not a valid content type.", nameof(contentType));

    public Task<string?> PutAsync(string url, string contentType, ReadOnlyMemory<byte> data, CancellationToken ct) =>
        _retrier is null
            ? PutOnceAsync(url, contentType, data, ct)
            : _retrier.RunAsync(() => PutOnceAsync(url, contentType, data, ct), Idempotency.Safe, "Upload", ct, throttled: false);

    private async Task<string?> PutOnceAsync(string url, string contentType, ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (_timeout is { } limit) timeoutSource.CancelAfter(limit);

        using var content = new ReadOnlyMemoryContent(data);
        content.Headers.ContentType = ParseContentType(contentType);
        HttpResponseMessage response;
        try
        {
            response = await _http.PutAsync(url, content, timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException(ex.Message, ex, ct);
        }
        catch (OperationCanceledException ex)
        {
            throw new BotApiException(StatusCode.DeadlineExceeded, $"Storage did not respond within {_timeout}.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new BotApiException(StatusCode.Unavailable, $"Storage is unreachable: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is InvalidOperationException or UriFormatException or ArgumentException)
        {
            throw new BotApiException(StatusCode.Internal, $"The server returned an unusable upload URL: {ex.Message}", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new BotApiException(
                    StatusCode.Unknown, $"Storage rejected the upload with HTTP {(int)response.StatusCode}.", uploadStatusCode: (int)response.StatusCode);
            }

            return response.Headers.ETag?.ToString();
        }
    }

    public void Dispose() => _http.Dispose();
}
