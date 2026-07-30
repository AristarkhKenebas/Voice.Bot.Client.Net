namespace Voice.Bot.Client.Interactions;

/// <summary>
/// Marks a method as the autocomplete handler for a specific <paramref name="optionName"/> on
/// <paramref name="commandName"/>. Like every other handler kind, it must respond itself (via
/// <c>RespondWithAutocompleteAsync</c>) within the backend's ack window - there's no separate
/// return-value convention.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AutocompleteHandlerAttribute(string commandName, string optionName) : Attribute
{
    public string CommandName { get; } = commandName;
    public string OptionName { get; } = optionName;
}
