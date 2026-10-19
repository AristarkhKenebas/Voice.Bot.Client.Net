using System.Text.RegularExpressions;

namespace Voice.Bot.Client.Net;

/// <summary>Builds and parses mentions in message text.</summary>
/// <example>
/// <code>
/// await ctx.ReplyAsync($"Welcome, {Mention.User(userId)}! Read {Mention.Channel(rulesId)} first.");
/// var mentioned = Mention.ParseUsers(message.Content);
/// </code>
/// </example>
public static partial class Mention
{
    /// <summary>Mentions every member of the group.</summary>
    public const string Everyone = "@everyone";

    /// <summary>Mentions every online member who can see the channel.</summary>
    public const string Here = "@here";

    /// <summary><c>&lt;@id&gt;</c> - mentions a user.</summary>
    public static string User(Guid userId) => $"<@{userId}>";

    /// <summary><c>&lt;#id&gt;</c> - links a channel.</summary>
    public static string Channel(Guid channelId) => $"<#{channelId}>";

    /// <summary><c>&lt;@&amp;id&gt;</c> - mentions everyone with a role.</summary>
    public static string Role(Guid roleId) => $"<@&{roleId}>";

    /// <summary>The ids of every user mentioned in <paramref name="text"/>, in order, without repeats.</summary>
    public static IReadOnlyList<Guid> ParseUsers(string text) => Parse(UserPattern(), text);

    /// <summary>The ids of every channel mentioned in <paramref name="text"/>, in order, without repeats.</summary>
    public static IReadOnlyList<Guid> ParseChannels(string text) => Parse(ChannelPattern(), text);

    /// <summary>The ids of every role mentioned in <paramref name="text"/>, in order, without repeats.</summary>
    public static IReadOnlyList<Guid> ParseRoles(string text) => Parse(RolePattern(), text);

    /// <summary>Reads a text that is exactly one user mention (or a bare id).</summary>
    public static bool TryParseUser(string text, out Guid userId) => TryParseExact(UserPattern(), text, out userId);

    /// <summary>Reads a text that is exactly one channel mention (or a bare id).</summary>
    public static bool TryParseChannel(string text, out Guid channelId) => TryParseExact(ChannelPattern(), text, out channelId);

    /// <summary>Reads a text that is exactly one role mention (or a bare id).</summary>
    public static bool TryParseRole(string text, out Guid roleId) => TryParseExact(RolePattern(), text, out roleId);

    /// <summary>Reads any kind of mention, or a bare id.</summary>
    internal static bool TryParseAny(string text, out Guid id) =>
        TryParseUser(text, out id) || TryParseChannel(text, out id) || TryParseRole(text, out id);

    private static List<Guid> Parse(Regex pattern, string text) =>
        pattern.Matches(text).Select(m => Guid.Parse(m.Groups["id"].ValueSpan)).Distinct().ToList();

    private static bool TryParseExact(Regex pattern, string text, out Guid id)
    {
        var trimmed = text.Trim();
        if (Guid.TryParse(trimmed, out id)) return true;
        var match = pattern.Match(trimmed);
        if (match.Success && match.Index == 0 && match.Length == trimmed.Length) return Guid.TryParse(match.Groups["id"].ValueSpan, out id);
        id = Guid.Empty;
        return false;
    }

    private const string Id = "(?<id>[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})";

    [GeneratedRegex("<@!?" + Id + ">")]
    private static partial Regex UserPattern();

    [GeneratedRegex("<#" + Id + ">")]
    private static partial Regex ChannelPattern();

    [GeneratedRegex("<@&" + Id + ">")]
    private static partial Regex RolePattern();
}

/// <summary>Text helpers for messages.</summary>
public static class Format
{
    /// <summary>
    /// The longest message the helpers produce by default. The server's real limit isn't documented
    /// yet; 2000 characters is a safe guess.
    /// </summary>
    public const int DefaultMessageLimit = 2000;

    /// <summary>
    /// Cuts <paramref name="text"/> into messages of at most <paramref name="limit"/> characters, at line
    /// breaks where possible, then at spaces, and only mid-word when a word alone is too long. A
    /// <c>```</c> code block cut in two is closed at the end of one part and opened again (with its
    /// language) at the start of the next, so both still show as code. A surrogate pair (an emoji, say)
    /// is never cut in half, so with a <paramref name="limit"/> of 1 such a part has 2 characters.
    /// </summary>
    /// <example>
    /// <code>
    /// foreach (var part in Format.Split(longReport))
    ///     await ctx.SendAsync(part);
    /// </code>
    /// </example>
    public static IReadOnlyList<string> Split(string text, int limit = DefaultMessageLimit)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        var parts = new List<string>();
        var rest = text;
        string? fence = null;
        while (true)
        {
            // Fences are only kept when they leave most of each part for the text itself.
            var reopen = fence is not null && (fence.Length + Fence.Length + 2) * 2 <= limit ? fence + "\n" : "";
            var room = limit - reopen.Length;
            if (rest.Length <= room)
            {
                if (rest.Length > 0 || parts.Count == 0) parts.Add(reopen + rest);
                return parts;
            }

            var (part, next) = Cut(rest, room);
            var open = FenceAfter(fence, part);
            if (open is not null && (open.Length + Fence.Length + 2) * 2 <= limit)
            {
                // Make room for the closing fence and cut again; the shorter part may close the block itself.
                (part, next) = Cut(rest, room - Fence.Length - 1);
                open = FenceAfter(fence, part);
                if (open is not null) part += "\n" + Fence;
            }

            if (part.Length > 0) parts.Add(reopen + part);
            fence = open;
            rest = next;
        }
    }

    private const string Fence = "```";

    /// <summary>The first part of <paramref name="text"/> that fits in <paramref name="room"/> characters, and what is left after it.</summary>
    private static (string Part, string Left) Cut(string text, int room)
    {
        room = Math.Max(1, room);
        var window = text.AsSpan(0, Math.Min(text.Length, room + 1));
        var cut = window.LastIndexOf('\n');
        if (cut <= 0) cut = window.LastIndexOf(' ');
        if (cut > 0) return (text[..cut].TrimEnd(), text[(cut + 1)..]);

        cut = Math.Min(room, text.Length);
        if (cut < text.Length && char.IsHighSurrogate(text[cut - 1]) && char.IsLowSurrogate(text[cut])) cut = cut > 1 ? cut - 1 : cut + 1;
        return (text[..cut], text[cut..]);
    }

    /// <summary>The fence line (<c>```lang</c>) still open after <paramref name="part"/>, given the one open before it; <c>null</c> when none is.</summary>
    private static string? FenceAfter(string? open, string part)
    {
        foreach (var line in part.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith(Fence, StringComparison.Ordinal)) continue;
            open = open is null ? trimmed : null;
        }

        return open;
    }

    /// <summary>A fenced code block, e.g. <c>Format.CodeBlock(json, "json")</c>.</summary>
    public static string CodeBlock(string code, string? language = null) =>
        $"```{language}\n{code.Replace("```", "`​``", StringComparison.Ordinal)}\n```";

    /// <summary>Inline code: <c>`text`</c>.</summary>
    public static string Code(string text) => $"`{text.Replace("`", "ˋ", StringComparison.Ordinal)}`";
}
