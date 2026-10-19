using Voice.Bot.Client.Net.Models;

namespace Voice.Bot.Client.Net.Interactions;

/// <summary>
/// Declares a <see cref="SlashCommandAttribute"/> handler parameter as a command option. The wire
/// <c>CommandOptionType</c> is inferred from the parameter's CLR type (<see cref="string"/> -&gt;
/// String, <see cref="int"/>/<see cref="long"/> -&gt; Integer, <see cref="bool"/> -&gt; Boolean,
/// <see cref="double"/>/<see cref="decimal"/> -&gt; Number (nullable versions too), anything else -&gt; String) - the same set of conversions
/// <c>InteractionService</c> already does in reverse when binding an incoming interaction's option
/// values back onto handler parameters. Set <see cref="Type"/> for users, channels and roles, which
/// bind to a <see cref="Guid"/> parameter. A parameter without this attribute is not declared as an
/// option, so users can't fill it - it always gets its default value.
/// </summary>
/// <example>
/// <code>
/// [SlashCommand("ban", "Bans a member")]
/// public Task Ban(
///     [SlashCommandOption("Who", Required = true, Type = CommandOptionType.User)] Guid user,
///     [SlashCommandOption("Days of messages to delete")] int days = 1) =&gt; ...
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class SlashCommandOptionAttribute(string description) : Attribute
{
    private CommandOptionType? _type;

    public string Description { get; } = description;

    /// <summary>Defaults to the parameter's own name (lowercased) when not set.</summary>
    public string? Name { get; set; }

    public bool Required { get; set; }

    /// <summary>The option's type; inferred from the parameter's CLR type when not set.</summary>
    public CommandOptionType Type
    {
        get => _type ?? CommandOptionType.String;
        set => _type = value;
    }

    internal CommandOptionType? ExplicitType => _type;
}
