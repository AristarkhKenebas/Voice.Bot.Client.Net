namespace Voice.Bot.Client.Net.Interactions;

/// <summary>
/// Marks a method as the handler for a message component (button/select) whose <c>CustomId</c> matches
/// <paramref name="customIdPattern"/>. Only trailing-wildcard patterns are supported (e.g.
/// <c>"delete:*"</c> matches any <c>CustomId</c> starting with <c>"delete:"</c>) - not full glob. If
/// the handler method declares a single <see cref="string"/> parameter, it receives the matched suffix.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ComponentInteractionAttribute(string customIdPattern) : Attribute
{
    public string CustomIdPattern { get; } = customIdPattern;
}
