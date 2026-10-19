using Voice.Bot.Client.Net.Models;

namespace Voice.Bot.Client.Net.Commands;

/// <summary>
/// Base class for prefix-command modules. <see cref="CommandService"/> instantiates one of these per matched message and sets
/// <see cref="Context"/> before invoking the matching <c>[Command]</c>-attributed method. Checks such as
/// <c>[GroupOnly]</c> or <c>[Cooldown]</c> on the class apply to all of its commands.
/// </summary>
public abstract class ModuleBase
{
    public CommandContext Context { get; internal set; } = null!;

    protected Task<Message> ReplyAsync(string content) => Context.ReplyAsync(content);

    /// <summary>Replies with files, uploaded first.</summary>
    protected Task<Message> ReplyAsync(string content, IEnumerable<FileUpload> files) => Context.ReplyAsync(content, files);

    /// <summary>
    /// Called when one of this module's commands throws. Return <c>true</c> when it's handled - then the
    /// service neither raises <see cref="CommandService.CommandExecutionFailed"/> nor sends the default
    /// error reply. <see cref="BotContext.CommandName"/> tells which command failed.
    /// </summary>
    protected internal virtual Task<bool> OnErrorAsync(Exception error) => Task.FromResult(false);
}
