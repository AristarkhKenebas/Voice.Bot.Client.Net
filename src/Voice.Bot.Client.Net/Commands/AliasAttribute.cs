namespace Voice.Bot.Client.Net.Commands;

/// <summary>
/// Additional names <see cref="CommandService"/> matches to the same <c>[Command]</c> handler, e.g.
/// <c>[Command("ping")] [Alias("p", "pong")]</c> triggers on <c>!ping</c>, <c>!p</c>, or <c>!pong</c> alike.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AliasAttribute(params string[] aliases) : Attribute
{
    public IReadOnlyList<string> Aliases { get; } = aliases;
}
