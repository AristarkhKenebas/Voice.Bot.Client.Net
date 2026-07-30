using Grpc.Core;

namespace Voice.Bot.Client.Net;

/// <summary>
/// Wraps a failed <c>BotsApi</c> call. <see cref="StatusCode"/> preserves the original gRPC status
/// (e.g. <see cref="Grpc.Core.StatusCode.PermissionDenied"/> when the bot's owner hasn't granted the
/// <see cref="Intents.BotIntents"/> the call requires) so callers can branch on it instead of parsing
/// the message string.
/// </summary>
public sealed class BotApiException : Exception
{
    public StatusCode StatusCode { get; }

    public BotApiException(StatusCode statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    internal static BotApiException FromRpcException(RpcException ex) =>
        new(ex.StatusCode, ex.Status.Detail, ex);
}
