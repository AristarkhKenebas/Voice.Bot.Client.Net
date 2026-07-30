using Voice.Bot.Client.Net.Components;
using Voice.Bot.Client.Net.Models;

namespace Voice.Bot.Client.Net.Commands;

/// <summary>
/// Base class for prefix-command modules. <see cref="CommandService"/> instantiates one of these per matched message and sets
/// <see cref="Context"/> before invoking the matching <c>[Command]</c>-attributed method.
/// </summary>
public abstract class ModuleBase
{
    public CommandContext Context { get; internal set; } = null!;

    protected Task<MessageInfo> ReplyAsync(string content, ComponentBuilder? components = null) =>
        Context.ReplyAsync(content, components);
}
