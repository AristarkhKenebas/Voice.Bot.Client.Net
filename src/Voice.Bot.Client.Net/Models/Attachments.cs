using Voice.Bot.Client.Net.Internal;
using Wire_ = Vc.Backend.WebApi.Attachments.Grpc;

namespace Voice.Bot.Client.Net.Models;

/// <summary>A link to upload a file to with HTTP PUT, then confirm with <c>ConfirmUploadAsync</c>.</summary>
public sealed record UploadTicket(Guid AttachmentId, string UploadUrl, DateTimeOffset? ExpiresAt);

/// <summary>An uploaded file.</summary>
/// <param name="UploaderId"><c>null</c> if the server didn't say who uploaded the file.</param>
public sealed record AttachmentInfo(
    Guid Id,
    Guid? UploaderId,
    string FileName,
    string ContentType,
    long SizeBytes,
    AttachmentStatus Status,
    bool IsMultipart,
    DateTimeOffset? CreatedAt);

internal static class AttachmentExtensions
{
    public static AttachmentInfo ToModel(this Wire_.AttachmentResponse wire) => new(
        Wire.Id(wire.Id),
        Wire.OptionalId(wire.UploaderId),
        wire.FileName,
        wire.ContentType,
        wire.SizeBytes,
        Enum.IsDefined((AttachmentStatus)(int)wire.Status) ? (AttachmentStatus)(int)wire.Status : AttachmentStatus.Unknown,
        wire.IsMultipart,
        Wire.Time(wire.CreatedAt));

    public static UploadTicket ToModel(this Wire_.InitiateUploadResponse wire) =>
        new(Wire.Id(wire.AttachmentId), wire.UploadUrl, Wire.Time(wire.ExpiresAt));
}
