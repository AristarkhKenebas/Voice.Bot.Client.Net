namespace Voice.Bot.Client.Interactions;

/// <summary>
/// Declares a <see cref="SlashCommandAttribute"/> handler parameter as a command option. The wire
/// <c>CommandOptionType</c> is inferred from the parameter's CLR type (<see cref="string"/> -&gt;
/// String, <see cref="int"/>/<see cref="long"/> -&gt; Integer, <see cref="bool"/> -&gt; Boolean,
/// <see cref="double"/> -&gt; Number, anything else -&gt; String) - the same set of conversions
/// <c>InteractionService</c> already does in reverse when binding an incoming interaction's option
/// values back onto handler parameters.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class SlashCommandOptionAttribute(string description) : Attribute
{
    public string Description { get; } = description;

    /// <summary>Defaults to the parameter's own name (lowercased) when not set.</summary>
    public string? Name { get; set; }

    public bool Required { get; set; }

    public bool Autocomplete { get; set; }
}
