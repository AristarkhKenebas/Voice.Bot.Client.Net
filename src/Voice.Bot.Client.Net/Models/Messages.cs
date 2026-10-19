using Voice.Bot.Client.Net.Internal;
using Wire_ = Vc.Backend.WebApi.Messages.Grpc;

namespace Voice.Bot.Client.Net.Models;

/// <summary>State of an uploaded file.</summary>
public enum AttachmentStatus
{
    Unknown = 0,
    Pending = 1,
    Uploaded = 2,
    Failed = 3,
    Deleted = 4,
}

/// <summary>A message in a channel. Returned by the API and delivered by the gateway in the same shape.</summary>
/// <param name="UpdatedAt"><c>null</c> if the message was never edited.</param>
/// <param name="ReplyTo">Id of the message this one replies to.</param>
/// <param name="Reactions">Emoji and the ids of the users who reacted with it.</param>
/// <param name="ComponentsJson">Buttons and selects under the message, if it has any.</param>
/// <param name="Interaction">The slash command this message answers, <c>null</c> for a plain message.</param>
public sealed record Message(
    Guid Id,
    Guid ChannelId,
    User Author,
    string Content,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt,
    bool IsEdited,
    bool IsDeleted,
    Guid? ReplyTo,
    Guid? ReplyToAuthorId,
    Guid? ForwardedFrom,
    IReadOnlyDictionary<string, IReadOnlyList<Guid>> Reactions,
    IReadOnlyList<Attachment> Attachments,
    IReadOnlyList<MentionedUser> Mentions,
    IReadOnlyList<MentionedChannel> MentionedChannels,
    IReadOnlyList<MentionedRole> MentionedRoles,
    bool MentionsEveryone,
    bool MentionsHere,
    string? ComponentsJson,
    MessageInteraction? Interaction);

/// <param name="UploaderId"><c>null</c> if the server didn't say who uploaded the file.</param>
public sealed record Attachment(
    Guid Id, Guid? UploaderId, string FileName, string ContentType, long SizeBytes, AttachmentStatus Status, string? DownloadUrl);

public sealed record MentionedUser(Guid Id, string Username, string? DisplayName, string? AvatarUrl, bool IsBot);

/// <param name="GroupId"><c>null</c> for a channel outside any group, such as a direct message.</param>
public sealed record MentionedChannel(Guid Id, string Name, ChannelType Type, Guid? GroupId);

public sealed record MentionedRole(Guid Id, string Name, int Color);

public sealed record MessageInteraction(Guid Id, string? CommandName, Guid? UserId);

/// <summary>A message with its neighbours, oldest first.</summary>
/// <param name="TargetFound">The requested message itself exists.</param>
public sealed record MessagesAround(IReadOnlyList<Message> Messages, bool TargetFound);

/// <param name="NextPageToken"><c>null</c> when there are no more pages.</param>
public sealed record SearchResults(IReadOnlyList<SearchHit> Hits, long Total, string? NextPageToken);

/// <param name="Highlights">Text around each match; the match itself is wrapped in U+E000 and U+E001.</param>
public sealed record SearchHit(Message Message, IReadOnlyList<string> Highlights);

/// <summary>Result of forwarding a message into one channel.</summary>
public sealed record ForwardResult(Guid ChannelId, bool Success, string? Error, Message? Message);

/// <summary>What a message must contain for <c>SearchMessagesAsync</c>.</summary>
public enum SearchFilter
{
    Attachment = 1,
    Image = 2,
    Video = 3,
    File = 4,
    Link = 5,
}

/// <summary>Order of <c>SearchMessagesAsync</c> results.</summary>
public enum SearchSort
{
    Relevance = 0,
    Newest = 1,
    Oldest = 2,
}

/// <summary>Search parameters for <c>SearchMessagesAsync</c>. Empty fields don't restrict the search.</summary>
/// <param name="Before">ISO 8601 date, e.g. <c>"2026-01-01"</c>.</param>
/// <param name="After">ISO 8601 date.</param>
public sealed record MessageSearch(
    string Query = "",
    IReadOnlyList<Guid>? ChannelIds = null,
    IReadOnlyList<Guid>? AuthorIds = null,
    IReadOnlyList<SearchFilter>? Has = null,
    string? Before = null,
    string? After = null,
    SearchSort Sort = SearchSort.Relevance,
    int PageSize = 0,
    string? PageToken = null);

internal static class MessageExtensions
{
    public static Message ToModel(this Wire_.Message wire) => new(
        Wire.Id(wire.Id),
        Wire.Id(wire.ChannelId),
        wire.Author.ToModelOrEmpty(),
        wire.Content,
        Wire.Time(wire.CreatedAt),
        Wire.Time(wire.UpdatedAt),
        wire.IsEdited,
        wire.IsDeleted,
        Wire.OptionalId(wire.ReplyTo),
        Wire.OptionalId(wire.ReplyToAuthorId),
        Wire.OptionalId(wire.ForwardedFrom),
        wire.Reactions.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<Guid>)kv.Value.UserIds.Select(Wire.Id).ToList()),
        wire.Attachments.Select(a => new Attachment(
            Wire.Id(a.Id), Wire.OptionalId(a.UploaderId), a.FileName, a.ContentType, a.SizeBytes,
            AttachmentStatusText.Parse(a.Status), Wire.Text(a.DownloadUrl))).ToList(),
        wire.Mentions.Select(m => new MentionedUser(Wire.Id(m.Id), m.Username, Wire.Text(m.DisplayName), Wire.Text(m.AvatarUrl), m.IsBot)).ToList(),
        wire.MentionChannels.Select(c => new MentionedChannel(Wire.Id(c.Id), c.Name, (ChannelType)c.Type, Wire.OptionalId(c.GroupId))).ToList(),
        wire.MentionRoles.Select(r => new MentionedRole(Wire.Id(r.Id), r.Name, r.Color)).ToList(),
        wire.MentionEveryone,
        wire.MentionHere,
        Wire.Text(wire.ComponentsJson),
        string.IsNullOrEmpty(wire.InteractionId)
            ? null
            : new MessageInteraction(Wire.Id(wire.InteractionId), Wire.Text(wire.InteractionCommandName), Wire.OptionalId(wire.InteractionUserId)));

    public static SearchResults ToModel(this Wire_.SearchMessagesResponse wire) => new(
        wire.Hits.Where(h => h.Message is not null).Select(h => new SearchHit(h.Message.ToModel(), h.Highlights.ToList())).ToList(),
        wire.Total,
        Wire.Text(wire.NextPageToken));

    public static ForwardResult ToModel(this Wire_.ForwardMessageResult wire) =>
        new(Wire.Id(wire.ChannelId), wire.Success, Wire.Text(wire.ErrorMessage), wire.Message?.ToModel());
}

internal static class AttachmentStatusText
{
    public static AttachmentStatus Parse(string? text) =>
        Enum.TryParse<AttachmentStatus>(text, ignoreCase: true, out var status) && Enum.IsDefined(status) ? status : AttachmentStatus.Unknown;
}
