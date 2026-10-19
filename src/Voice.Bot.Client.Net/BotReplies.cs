using System.Globalization;

namespace Voice.Bot.Client.Net;

/// <summary>
/// The texts the bot answers with on its own, from <see cref="BotClientOptions.Replies"/>. A <c>null</c>
/// text means "stay silent".
/// </summary>
/// <example>
/// <code>
/// services.AddVoiceBotClient(o =&gt;
/// {
///     o.Replies.Error = "Oops, that didn't work.";
///     o.Replies.Cooldown = left =&gt; $"Wait {BotReplies.FormatDuration(left)} more.";
/// });
/// </code>
/// </example>
public sealed class BotReplies
{
    /// <summary>Answer to an unhandled error in a command.</summary>
    public string? Error { get; set; } = "Something went wrong.";

    /// <summary>Answer when the server kept rate-limiting the bot and retries did not help.</summary>
    public string? RateLimited { get; set; } = "I'm being rate limited, please try again in a moment.";

    /// <summary>Answer when the bot lacked a permission for what the command did (<see cref="Grpc.Core.StatusCode.PermissionDenied"/>).</summary>
    public string? Forbidden { get; set; } = "I don't have permission to do that.";

    /// <summary>Answer when a command is used too often; the argument is how long is left to wait.</summary>
    public Func<TimeSpan, string?> Cooldown { get; set; } = left => $"Slow down! Try again in {FormatDuration(left)}.";

    /// <summary>Answer when the member lacks permissions for a command; the argument is what is missing.</summary>
    public Func<Permissions, string?> MissingPermissions { get; set; } = missing => $"You need these permissions: {missing}.";

    /// <summary>Answer when the bot lacks permissions for a command; the argument is what is missing.</summary>
    public Func<Permissions, string?> BotMissingPermissions { get; set; } = missing => $"I need these permissions: {missing}.";

    /// <summary>Answer to a group-only command used in direct messages.</summary>
    public string? GroupOnly { get; set; } = "This command works only in a group.";

    /// <summary>Answer to a direct-messages-only command used in a group.</summary>
    public string? DmOnly { get; set; } = "This command works only in direct messages.";

    /// <summary>Answer to an owner-only command used by someone else.</summary>
    public string? OwnerOnly { get; set; } = "Only the group owner can use this command.";

    /// <summary>Answer when a <c>[Users]</c>, <c>[Roles]</c> or custom check fails without its own message.</summary>
    public string? NotAllowed { get; set; } = "You can't use this command.";

    /// <summary>Answer to a missing required argument; the argument is its name.</summary>
    public Func<string, string?> MissingArgument { get; set; } = name => $"`{name}` is required.";

    /// <summary>
    /// Answer to a text command given more arguments than it takes; the argument is the first extra one.
    /// <c>null</c> ignores extra arguments instead.
    /// </summary>
    public Func<string, string?>? UnexpectedArgument { get; set; } = argument => $"Unexpected argument: `{argument}`.";

    /// <summary>
    /// Answer to an argument that could not be converted; the arguments are its name and what it should be,
    /// e.g. <c>("days", "a whole number")</c>.
    /// </summary>
    public Func<string, string, string?> InvalidArgument { get; set; } = (name, expected) => $"`{name}` must be {expected}.";

    /// <summary>
    /// Line added under a text command's argument error; the argument is the usage generated from the
    /// declaration, e.g. <c>!add &lt;a&gt; [b]</c>. <c>null</c> leaves it out.
    /// </summary>
    public Func<string, string?>? Usage { get; set; } = usage => $"Usage: `{usage}`";

    /// <summary>A duration for people: <c>1h 5m</c>, <c>42s</c>, <c>0.5s</c>.</summary>
    public static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.FromSeconds(1))
        {
            return $"{Math.Max(0, duration.TotalSeconds).ToString("0.0", CultureInfo.InvariantCulture)}s";
        }

        var total = (long)Math.Ceiling(duration.TotalSeconds);
        var parts = new List<string>(3);
        if (total / 3600 > 0) parts.Add($"{total / 3600}h");
        if (total % 3600 / 60 > 0) parts.Add($"{total % 3600 / 60}m");
        if (total % 60 > 0 || parts.Count == 0) parts.Add($"{total % 60}s");
        return string.Join(' ', parts);
    }
}

/// <summary>Where a command handler runs.</summary>
public enum RunMode
{
    /// <summary>
    /// In the gateway's event order, like any event handler: the next event waits until it finishes - or
    /// until it starts waiting for an answer (<see cref="BotContext.AskAsync"/>), after which the next events
    /// are handled meanwhile.
    /// </summary>
    Sync = 0,

    /// <summary>In the background, so a long command doesn't hold up other events.</summary>
    Async = 1,
}
