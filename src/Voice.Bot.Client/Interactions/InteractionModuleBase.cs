using Voice.Bot.Client.Components;
using Voice.Bot.Client.Models;

namespace Voice.Bot.Client.Interactions;

/// <summary>
/// Base class for interaction modules, in the spirit of Discord.Net.Interactions'
/// <c>InteractionModuleBase</c>. <see cref="InteractionService"/> instantiates one of these per
/// incoming interaction it dispatches and sets <see cref="Context"/> before invoking the matching
/// attributed method.
/// </summary>
public abstract class InteractionModuleBase
{
    public InteractionContext Context { get; internal set; } = null!;

    protected Task RespondAsync(string content, bool ephemeral = false, ComponentBuilder? components = null) =>
        Context.RespondAsync(content, ephemeral, components);

    protected Task DeferAsync(bool ephemeral = false) => Context.DeferAsync(ephemeral);

    protected Task UpdateAsync(string content, ComponentBuilder? components = null) => Context.UpdateAsync(content, components);

    protected Task RespondWithModalAsync(string modalJson) => Context.RespondWithModalAsync(modalJson);

    protected Task RespondWithModalAsync(ModalBuilder modal) => Context.RespondWithModalAsync(modal);

    protected Task RespondWithAutocompleteAsync(IReadOnlyList<(string Name, string Value)> choices) =>
        Context.RespondWithAutocompleteAsync(choices);

    protected Task<MessageInfo> FollowupAsync(string content, ComponentBuilder? components = null) =>
        Context.FollowupAsync(content, components);
}
