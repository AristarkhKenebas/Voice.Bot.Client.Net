using System.Text.RegularExpressions;
using Grpc.Core;

namespace Voice.Bot.Client.Net;

/// <summary>
/// Wraps a failed API call. <see cref="StatusCode"/> preserves the original gRPC status (e.g.
/// <see cref="Grpc.Core.StatusCode.PermissionDenied"/> when the bot lacks a permission,
/// <see cref="Grpc.Core.StatusCode.NotFound"/> for a missing object) so callers can branch on it
/// instead of parsing the message string.
/// </summary>
/// <remarks>
/// The one exception that is not wrapped: when the <see cref="CancellationToken"/> passed to a method is
/// cancelled, the method throws <see cref="OperationCanceledException"/>.
/// </remarks>
public sealed partial class BotApiException : Exception
{
    public StatusCode StatusCode { get; }

    /// <summary>How long to wait before retrying, when the server sent a <c>retry-after</c> hint with a rate-limit error.</summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>HTTP status of the storage response for a failed file upload, 0 when there was no response.</summary>
    public int UploadStatusCode { get; }

    /// <summary>
    /// The server is rate-limiting this bot - wait <see cref="RetryAfter"/> (or a few seconds) and retry.
    /// A message that is too large is also reported as <see cref="Grpc.Core.StatusCode.ResourceExhausted"/>,
    /// but it is not a rate limit: retrying it won't help.
    /// </summary>
    public bool IsRateLimited =>
        StatusCode == StatusCode.ResourceExhausted && !TooLarge().IsMatch(Message) ||
        StatusCode == StatusCode.Unavailable && Http429().IsMatch(Message);

    [GeneratedRegex("larger than max|exceeds (?:the )?maximum", RegexOptions.IgnoreCase)]
    private static partial Regex TooLarge();

    [GeneratedRegex(@"status(?: code)?(?: received from server)?:? 429\b", RegexOptions.IgnoreCase)]
    private static partial Regex Http429();

    public BotApiException(StatusCode statusCode, string message, Exception? innerException = null, TimeSpan? retryAfter = null, int uploadStatusCode = 0)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        RetryAfter = retryAfter;
        UploadStatusCode = uploadStatusCode;
    }

    internal static BotApiException FromRpcException(RpcException ex)
    {
        TimeSpan? retryAfter = null;
        var hint = ex.Trailers.GetValue("retry-after");
        if (int.TryParse(hint?.Trim(), out var seconds) && seconds >= 0)
        {
            retryAfter = TimeSpan.FromSeconds(seconds);
        }

        return new BotApiException(ex.StatusCode, string.IsNullOrEmpty(ex.Status.Detail) ? ex.StatusCode.ToString() : ex.Status.Detail, ex, retryAfter);
    }
}
