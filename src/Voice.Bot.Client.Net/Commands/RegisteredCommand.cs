using System.Reflection;
using Voice.Bot.Client.Net.Internal;

namespace Voice.Bot.Client.Net.Commands;

/// <summary>A handler parameter: how it is shown in the usage line and bound from the text.</summary>
/// <param name="IsRemainder">The last <see cref="string"/> parameter takes the rest of the text as-is.</param>
internal sealed record CommandParameter(ParameterInfo Parameter, string Name, bool IsOptional, bool IsRemainder)
{
    public string Usage => IsRemainder
        ? IsOptional ? $"[{Name}...]" : $"<{Name}...>"
        : IsOptional ? $"[{Name}]" : $"<{Name}>";
}

internal sealed record RegisteredCommand(
    string Name,
    IReadOnlyList<string> Aliases,
    MethodInfo Method,
    Type ModuleType,
    string? Description,
    IReadOnlyList<CommandParameter> Parameters,
    CommandMetadata Metadata)
{
    public bool Matches(string commandName, StringComparison comparison = StringComparison.OrdinalIgnoreCase) =>
        string.Equals(Name, commandName, comparison) ||
        Aliases.Any(alias => string.Equals(alias, commandName, comparison));

    public string Usage(string prefix) =>
        string.Join(' ', Parameters.Select(p => p.Usage).Prepend(prefix + Name));
}
