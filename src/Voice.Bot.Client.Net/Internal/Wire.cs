using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Voice.Bot.Client.Net.Internal;

/// <summary>Small conversions shared by every proto-to-model mapper.</summary>
internal static class Wire
{
    public static Guid Id(string value) =>
        Guid.TryParse(value, out var id) ? id : throw new BotApiException(StatusCode.Internal, $"The server returned an invalid id '{value}'.");

    public static Guid? OptionalId(string? value) => string.IsNullOrEmpty(value) ? null : Id(value);

    public static T Required<T>(T? value, string what) where T : class =>
        value ?? throw new BotApiException(StatusCode.Internal, $"The server response has no {what}.");

    public static string? Text(string? value) => string.IsNullOrEmpty(value) ? null : value;

    public static DateTimeOffset? Time(Timestamp? value)
    {
        if (value is null) return null;
        try
        {
            return value.ToDateTimeOffset();
        }
        catch (ArgumentOutOfRangeException)
        {
            return value.Seconds > 0 ? DateTimeOffset.MaxValue : DateTimeOffset.MinValue;
        }
    }

    public static Timestamp Timestamp(DateTimeOffset value) => Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(value);
}
