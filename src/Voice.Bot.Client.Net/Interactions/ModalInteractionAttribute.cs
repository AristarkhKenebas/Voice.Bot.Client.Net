namespace Voice.Bot.Client.Net.Interactions;

/// <summary>
/// Marks a method as the handler for a modal submit whose <c>CustomId</c> matches
/// <paramref name="customIdPattern"/> (trailing-wildcard only, see <see cref="ComponentInteractionAttribute"/>).
/// Parameters are bound from <c>Data.ModalFields</c> by name first, falling back to the wildcard capture.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ModalInteractionAttribute(string customIdPattern) : Attribute
{
    public string CustomIdPattern { get; } = customIdPattern;
}
