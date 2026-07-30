namespace Voice.Bot.Client.Net.Internal;

/// <summary>
/// Converts a single raw string value (a slash-command option, a modal field, a positional text-command
/// token) into the CLR type a handler method parameter declares. Shared by
/// <see cref="Interactions.InteractionService"/> and <see cref="Commands.CommandService"/> so both
/// argument-binding paths agree on the same small set of supported types.
/// </summary>
internal static class ArgumentConverter
{
    public static object? Convert(string? raw, Type targetType)
    {
        if (raw is null) return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
        if (targetType == typeof(string)) return raw;
        if (targetType == typeof(Guid)) return Guid.TryParse(raw, out var g) ? g : Guid.Empty;
        if (targetType == typeof(bool)) return bool.TryParse(raw, out var b) && b;
        if (targetType == typeof(int)) return int.TryParse(raw, out var i) ? i : 0;
        if (targetType == typeof(long)) return long.TryParse(raw, out var l) ? l : 0L;
        if (targetType == typeof(double)) return double.TryParse(raw, out var d) ? d : 0d;
        return System.Convert.ChangeType(raw, targetType);
    }
}
