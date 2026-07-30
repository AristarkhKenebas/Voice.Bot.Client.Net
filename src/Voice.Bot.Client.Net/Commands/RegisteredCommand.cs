using System.Reflection;

namespace Voice.Bot.Client.Net.Commands;

internal sealed record RegisteredCommand(string Name, IReadOnlyList<string> Aliases, MethodInfo Method, Type ModuleType)
{
    public bool Matches(string commandName) =>
        string.Equals(Name, commandName, StringComparison.OrdinalIgnoreCase) ||
        Aliases.Any(alias => string.Equals(alias, commandName, StringComparison.OrdinalIgnoreCase));
}
