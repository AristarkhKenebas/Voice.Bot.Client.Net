namespace Voice.Bot.Client.Net;

/// <summary>
/// A file to attach to a message, from disk or from memory. The content type is guessed from the file
/// extension unless given.
/// </summary>
/// <example>
/// <code>
/// await ctx.ReplyAsync("Here's the report", [FileUpload.FromPath("report.pdf")]);
/// await client.SendFileAsync(channelId, FileUpload.FromBytes("hello.txt", "hi"u8.ToArray()));
/// </code>
/// </example>
public sealed class FileUpload
{
    private FileUpload(string fileName, string contentType, string? path, ReadOnlyMemory<byte> data)
    {
        FileName = fileName;
        ContentType = contentType;
        Path = path;
        Data = data;
    }

    /// <summary>The name recipients see.</summary>
    public string FileName { get; }

    /// <summary>MIME type, e.g. <c>image/png</c>.</summary>
    public string ContentType { get; }

    /// <summary>Where the file is on disk, <c>null</c> for an in-memory file.</summary>
    public string? Path { get; }

    /// <summary>The bytes of an in-memory file; empty for a file on disk.</summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>A file on disk. Large files are uploaded in parts.</summary>
    /// <param name="fileName">Name shown to recipients; <c>null</c> for the name on disk.</param>
    /// <param name="contentType"><c>null</c> to guess from the extension.</param>
    public static FileUpload FromPath(string path, string? fileName = null, string? contentType = null)
    {
        fileName ??= System.IO.Path.GetFileName(path);
        return new FileUpload(fileName, contentType ?? GuessContentType(fileName), path, ReadOnlyMemory<byte>.Empty);
    }

    /// <summary>A file in memory.</summary>
    /// <param name="contentType"><c>null</c> to guess from the extension of <paramref name="fileName"/>.</param>
    public static FileUpload FromBytes(string fileName, ReadOnlyMemory<byte> data, string? contentType = null) =>
        new(fileName, contentType ?? GuessContentType(fileName), null, data);

    /// <summary>The MIME type for a file name's extension; <c>application/octet-stream</c> when unknown.</summary>
    public static string GuessContentType(string fileName) =>
        ContentTypes.TryGetValue(System.IO.Path.GetExtension(fileName), out var type) ? type : "application/octet-stream";

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".bmp"] = "image/bmp",
        [".svg"] = "image/svg+xml",
        [".ico"] = "image/x-icon",
        [".heic"] = "image/heic",
        [".avif"] = "image/avif",
        [".mp4"] = "video/mp4",
        [".webm"] = "video/webm",
        [".mov"] = "video/quicktime",
        [".mkv"] = "video/x-matroska",
        [".avi"] = "video/x-msvideo",
        [".mp3"] = "audio/mpeg",
        [".ogg"] = "audio/ogg",
        [".oga"] = "audio/ogg",
        [".opus"] = "audio/opus",
        [".wav"] = "audio/wav",
        [".flac"] = "audio/flac",
        [".m4a"] = "audio/mp4",
        [".txt"] = "text/plain",
        [".log"] = "text/plain",
        [".md"] = "text/markdown",
        [".csv"] = "text/csv",
        [".html"] = "text/html",
        [".htm"] = "text/html",
        [".css"] = "text/css",
        [".js"] = "text/javascript",
        [".json"] = "application/json",
        [".xml"] = "application/xml",
        [".yaml"] = "application/yaml",
        [".yml"] = "application/yaml",
        [".pdf"] = "application/pdf",
        [".zip"] = "application/zip",
        [".gz"] = "application/gzip",
        [".tar"] = "application/x-tar",
        [".7z"] = "application/x-7z-compressed",
        [".rar"] = "application/vnd.rar",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".ppt"] = "application/vnd.ms-powerpoint",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".odt"] = "application/vnd.oasis.opendocument.text",
    };
}
