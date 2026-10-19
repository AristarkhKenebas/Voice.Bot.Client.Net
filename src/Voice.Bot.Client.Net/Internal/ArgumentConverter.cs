using System.Globalization;
using System.Reflection;

namespace Voice.Bot.Client.Net.Internal;

/// <summary>
/// Converts a single raw string value (a slash-command option or a positional text-command token) into
/// the CLR type a handler method parameter declares. Shared by <see cref="Interactions.InteractionService"/>
/// and <see cref="Commands.CommandService"/> so both argument-binding paths agree on the same supported types.
/// A missing value becomes <c>null</c> (or the type's default); a value that doesn't parse throws, so the
/// handler failure is reported instead of silently running with a 0 or <see cref="Guid.Empty"/>.
/// </summary>
/// <remarks>
/// <see cref="Guid"/> accepts a bare id as well as a user, channel or role mention (<c>&lt;@id&gt;</c>,
/// <c>&lt;#id&gt;</c>, <c>&lt;@&amp;id&gt;</c>); <see cref="bool"/> also accepts yes/no, on/off and 1/0;
/// enums accept their names, ignoring case.
/// </remarks>
internal static class ArgumentConverter
{
    public static object? Convert(string? raw, Type targetType)
    {
        var underlying = Nullable.GetUnderlyingType(targetType);
        if (raw is null)
        {
            return underlying is null && targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
        }

        var type = underlying ?? targetType;
        if (type == typeof(string)) return raw;
        if (type == typeof(Guid)) return Mention.TryParseAny(raw, out var g) ? g : throw Invalid(raw, type);
        if (type == typeof(bool)) return ParseBool(raw) ?? throw Invalid(raw, type);
        if (type == typeof(int)) return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : throw Invalid(raw, type);
        if (type == typeof(long)) return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : throw Invalid(raw, type);
        if (type == typeof(double)) return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : throw Invalid(raw, type);
        if (type == typeof(float)) return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : throw Invalid(raw, type);
        if (type == typeof(decimal)) return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var m) ? m : throw Invalid(raw, type);
        if (type.IsEnum) return Enum.TryParse(type, raw, ignoreCase: true, out var e) && Enum.IsDefined(type, e!) ? e : throw Invalid(raw, type);
        try
        {
            return System.Convert.ChangeType(raw, type, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is InvalidCastException or OverflowException)
        {
            throw new FormatException($"'{raw}' is not a valid {type.Name}.", ex);
        }
    }

    /// <summary>What a value of <paramref name="type"/> should look like, for "`days` must be a whole number".</summary>
    public static string Describe(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte)) return "a whole number";
        if (type == typeof(double) || type == typeof(float) || type == typeof(decimal)) return "a number";
        if (type == typeof(bool)) return "yes or no";
        if (type == typeof(Guid)) return "a mention or an id";
        if (type.IsEnum) return "one of: " + string.Join(", ", Enum.GetNames(type).Select(n => n.ToLowerInvariant()));
        return $"a valid {type.Name}";
    }

    /// <summary>Whether a handler parameter may be left out: it has a default value or a nullable type.</summary>
    public static bool IsOptional(ParameterInfo parameter) =>
        parameter.HasDefaultValue ||
        Nullable.GetUnderlyingType(parameter.ParameterType) is not null ||
        !parameter.ParameterType.IsValueType && new NullabilityInfoContext().Create(parameter).WriteState == NullabilityState.Nullable;

    /// <summary>The value for a parameter that was left out: its declared default, else <c>null</c>/<c>default</c>.</summary>
    public static object? Missing(ParameterInfo parameter)
    {
        if (parameter.HasDefaultValue && parameter.DefaultValue is { } value && value is not DBNull && value != Type.Missing)
        {
            var type = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;
            return type.IsEnum && value.GetType() != type ? Enum.ToObject(type, value) : value;
        }

        return Convert(null, parameter.ParameterType);
    }

    private static bool? ParseBool(string raw) => raw.ToLowerInvariant() switch
    {
        "true" or "yes" or "y" or "on" or "1" => true,
        "false" or "no" or "n" or "off" or "0" => false,
        _ => null,
    };

    private static FormatException Invalid(string raw, Type type) => new($"'{raw}' is not a valid {type.Name}.");
}
