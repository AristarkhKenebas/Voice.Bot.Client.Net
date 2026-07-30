using Wire = Vc.Backend.WebApi.Bots.Apis.Grpc.MessageInfo;

namespace Voice.Bot.Client.Models;

/// <summary>Mirrors <c>bots.MessageInfo</c> - returned by SendMessage/UpdateMessage/AddReaction/RemoveReaction.</summary>
public sealed record MessageInfo(
    Guid Id,
    Guid ChannelId,
    string Content,
    Guid AuthorId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool IsEdited,
    bool IsDeleted,
    Guid? ReplyTo,
    IReadOnlyDictionary<string, IReadOnlyList<Guid>> Reactions,
    string? ComponentsJson = null);

internal static class MessageInfoExtensions
{
    public static MessageInfo ToModel(this Wire grpc) => new(
        Guid.Parse(grpc.Id),
        Guid.Parse(grpc.ChannelId),
        grpc.Content,
        Guid.Parse(grpc.AuthorId),
        grpc.CreatedAt.ToDateTimeOffset(),
        grpc.UpdatedAt.ToDateTimeOffset(),
        grpc.IsEdited,
        grpc.IsDeleted,
        string.IsNullOrEmpty(grpc.ReplyTo) ? null : Guid.Parse(grpc.ReplyTo),
        grpc.Reactions.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<Guid>)kv.Value.UserIds.Select(Guid.Parse).ToList()),
        grpc.HasComponentsJson ? grpc.ComponentsJson : null);
}
