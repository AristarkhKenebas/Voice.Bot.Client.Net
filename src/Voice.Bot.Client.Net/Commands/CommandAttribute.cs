namespace Voice.Bot.Client.Net.Commands;

/// <summary>
/// Marks a method as the handler for a prefix ("text") command, e.g. <c>[Command("test")]</c> matches
/// a message like <c>!test</c> once <see cref="CommandService"/> strips its configured prefix. Method parameters are
/// bound positionally from the remaining text - see <see cref="CommandService"/> for the binding rules.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class CommandAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
