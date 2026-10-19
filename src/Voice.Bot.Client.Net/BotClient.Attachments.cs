using Grpc.Core;
using Voice.Bot.Client.Net.Internal;
using Voice.Bot.Client.Net.Models;
using Vc.Backend.WebApi.Attachments.Grpc;

namespace Voice.Bot.Client.Net;

/// <summary>Uploading and downloading files to attach to messages.</summary>
public sealed partial class BotClient
{
    /// <summary>Default part size for large files.</summary>
    public const int DefaultPartSize = 8 * 1024 * 1024;

    /// <summary>Storage rejects smaller parts.</summary>
    public const int MinPartSize = 5 * 1024 * 1024;

    /// <summary>Uploads a file from memory in one go.</summary>
    /// <exception cref="ArgumentException"><paramref name="contentType"/> is not a valid MIME type; nothing is sent.</exception>
    public async Task<AttachmentInfo> UploadAsync(string fileName, ReadOnlyMemory<byte> data, string contentType, CancellationToken ct = default)
    {
        Uploader.ParseContentType(contentType);
        var ticket = await StartUploadAsync(fileName, data.Length, contentType, ct).ConfigureAwait(false);
        await _uploader.PutAsync(ticket.UploadUrl, contentType, data, ct).ConfigureAwait(false);
        return await ConfirmUploadAsync(ticket.AttachmentId, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Uploads a file from disk. Files larger than one part are uploaded in parts; if anything goes
    /// wrong - including cancellation - the upload is aborted on the server.
    /// </summary>
    /// <param name="fileName">Name shown to recipients; <c>null</c> for the name on disk.</param>
    /// <exception cref="ArgumentException"><paramref name="contentType"/> is not a valid MIME type; nothing is sent.</exception>
    public async Task<AttachmentInfo> UploadFileAsync(
        string path, string contentType, string? fileName = null, int partSize = DefaultPartSize, CancellationToken ct = default)
    {
        if (partSize < MinPartSize) throw new ArgumentOutOfRangeException(nameof(partSize), $"Parts must be at least {MinPartSize} bytes.");
        Uploader.ParseContentType(contentType);

        fileName ??= Path.GetFileName(path);
        var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        await using var closeFile = file.ConfigureAwait(false);
        var size = file.Length;
        if (size <= partSize)
        {
            var data = new byte[size];
            if (await file.ReadAtLeastAsync(data, data.Length, throwOnEndOfStream: false, ct).ConfigureAwait(false) < data.Length)
            {
                throw UploadError($"The file got shorter while reading it; expected {size} bytes.");
            }

            return await UploadAsync(fileName, data, contentType, ct).ConfigureAwait(false);
        }

        var partCount = (int)((size + partSize - 1) / partSize);
        var started = await _api.CallAsync(o => _api.Attachments.InitiateMultipartUploadAsync(new InitiateMultipartUploadRequest
        {
            FileName = fileName, ContentType = contentType, SizeBytes = size, PartCount = partCount,
        }, o), ct, Idempotency.Unsafe).ConfigureAwait(false);
        var attachmentId = started.AttachmentId;
        var completed = false;
        try
        {
            var urls = started.PartUrls.ToDictionary(p => p.PartNumber, p => p.Url);
            var missing = Enumerable.Range(1, partCount).Where(n => !urls.ContainsKey(n)).ToList();
            if (missing.Count > 0)
            {
                var request = new GetMultipartPartUrlsRequest { AttachmentId = attachmentId };
                request.PartNumbers.AddRange(missing);
                foreach (var part in (await _api.CallAsync(o => _api.Attachments.GetMultipartPartUrlsAsync(request, o), ct).ConfigureAwait(false)).PartUrls)
                {
                    urls[part.PartNumber] = part.Url;
                }
            }

            var complete = new CompleteMultipartUploadRequest { AttachmentId = attachmentId };
            var buffer = new byte[partSize];
            for (var number = 1; number <= partCount; number++)
            {
                if (!urls.TryGetValue(number, out var url)) throw UploadError($"The server gave no URL for part {number}.");

                var length = (int)Math.Min(partSize, size - (long)(number - 1) * partSize);
                var read = await file.ReadAtLeastAsync(buffer.AsMemory(0, length), length, throwOnEndOfStream: false, ct).ConfigureAwait(false);
                if (read < length) throw UploadError($"The file got shorter during the upload; expected {size} bytes.");

                var etag = await _uploader.PutAsync(url, contentType, buffer.AsMemory(0, length), ct).ConfigureAwait(false);
                if (string.IsNullOrEmpty(etag)) throw UploadError($"Storage returned no ETag for part {number}.");
                complete.Parts.Add(new CompletedPart { PartNumber = number, Etag = etag });
            }

            if (await file.ReadAsync(buffer.AsMemory(0, 1), ct).ConfigureAwait(false) > 0) throw UploadError($"The file got longer during the upload; expected {size} bytes.");

            var result = await _api.CallAsync(o => _api.Attachments.CompleteMultipartUploadAsync(complete, o), ct, Idempotency.Unsafe).ConfigureAwait(false);
            completed = true;
            return result.ToModel();
        }
        finally
        {
            if (!completed)
            {
                try
                {
                    await _api.CallAsync(o => _api.Attachments.AbortMultipartUploadAsync(new AbortMultipartUploadRequest { AttachmentId = attachmentId }, o), CancellationToken.None).ConfigureAwait(false);
                }
                catch (BotApiException)
                {
                }
            }
        }
    }

    /// <summary>
    /// First step of a manual upload: a URL to PUT the file to, after which call <see cref="ConfirmUploadAsync"/>.
    /// <see cref="UploadAsync(string, ReadOnlyMemory{byte}, string, CancellationToken)"/> does all of it in one call.
    /// </summary>
    public async Task<UploadTicket> StartUploadAsync(string fileName, long sizeBytes, string contentType, CancellationToken ct = default) =>
        (await _api.CallAsync(o => _api.Attachments.InitiateUploadAsync(
            new InitiateUploadRequest { FileName = fileName, ContentType = contentType, SizeBytes = sizeBytes }, o), ct, Idempotency.Unsafe).ConfigureAwait(false)).ToModel();

    public async Task<AttachmentInfo> ConfirmUploadAsync(Guid attachmentId, CancellationToken ct = default) =>
        (await _api.CallAsync(o => _api.Attachments.ConfirmUploadAsync(new ConfirmUploadRequest { AttachmentId = attachmentId.ToString() }, o), ct).ConfigureAwait(false)).ToModel();

    public async Task<AttachmentInfo> GetAttachmentAsync(Guid attachmentId, CancellationToken ct = default) =>
        (await _api.CallAsync(o => _api.Attachments.GetAttachmentAsync(new GetAttachmentRequest { AttachmentId = attachmentId.ToString() }, o), ct).ConfigureAwait(false)).ToModel();

    /// <summary>A temporary link to download the file.</summary>
    public async Task<string> GetDownloadUrlAsync(Guid attachmentId, CancellationToken ct = default) =>
        (await _api.CallAsync(o => _api.Attachments.GetDownloadUrlAsync(new GetDownloadUrlRequest { AttachmentId = attachmentId.ToString() }, o), ct).ConfigureAwait(false)).DownloadUrl;

    public async Task DeleteAttachmentAsync(Guid attachmentId, CancellationToken ct = default) =>
        await _api.DeleteAsync(o => _api.Attachments.DeleteAttachmentAsync(new DeleteAttachmentRequest { AttachmentId = attachmentId.ToString() }, o), ct).ConfigureAwait(false);

    private static BotApiException UploadError(string message) => new(StatusCode.Unknown, message);
}
