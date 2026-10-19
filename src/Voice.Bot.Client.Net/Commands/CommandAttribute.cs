namespace Voice.Bot.Client.Net.Commands;

/// <summary>
/// Marks a method as the handler for a prefix ("text") command, e.g. <c>[Command("test")]</c> matches
/// a message like <c>!test</c> once <see cref="CommandService"/> strips its configured prefix. Method parameters are
/// bound positionally from the remaining text - see <see cref="CommandService"/> for the binding rules.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class CommandAttribute(string name) : Attribute
{
    private RunMode? _runMode;

    public string Name { get; } = name;

    /// <summary>What the command does, shown by the built-in <c>help</c> command.</summary>
    public string? Description { get; set; }

    /// <summary>Where the handler runs; <see cref="BotClientOptions.DefaultRunMode"/> when not set.</summary>
    public RunMode RunMode
    {
        get => _runMode ?? RunMode.Sync;
        set => _runMode = value;
    }

    internal RunMode? ExplicitRunMode => _runMode;
}
