using Voice.Bot.Client.Net.Models;

namespace Voice.Bot.Client.Net.Interactions;

/// <summary>
/// Base class for slash-command modules. <see cref="InteractionService"/> instantiates one of these per
/// incoming command it dispatches and sets <see cref="Context"/> before invoking the matching
/// <c>[SlashCommand]</c>-attributed method. Checks such as <c>[GroupOnly]</c> or <c>[Cooldown]</c> on the
/// class apply to all of its commands.
/// </summary>
public abstract class InteractionModuleBase
{
    public InteractionContext Context { get; internal set; } = null!;

    /// <summary>Posts a message to the channel the command was used in.</summary>
    protected Task<Message> ReplyAsync(string content) => Context.ReplyAsync(content);

    /// <summary>Posts a message with files, uploaded first, to the channel the command was used in.</summary>
    protected Task<Message> ReplyAsync(string content, IEnumerable<FileUpload> files) => Context.ReplyAsync(content, files);

    /// <summary>
    /// Called when one of this module's commands throws. Return <c>true</c> when it's handled - then the
    /// service neither raises <see cref="InteractionService.HandlerFailed"/> nor sends the default error
    /// reply. <see cref="BotContext.CommandName"/> tells which command failed.
    /// </summary>
    protected internal virtual Task<bool> OnErrorAsync(Exception error) => Task.FromResult(false);
}
