namespace Voice.Bot.Client.Net.Interactions;

/// <summary>
/// Marks a method on an <see cref="InteractionModuleBase"/> as the handler for a slash command by name.
/// <paramref name="description"/> is required - the backend rejects an empty command description, so
/// this catches the mistake at the call site instead of at <c>RegisterCommandsAsync</c> time. Decorate
/// parameters with <see cref="SlashCommandOptionAttribute"/> to declare the command's options.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class SlashCommandAttribute(string name, string description) : Attribute
{
    private RunMode? _runMode;

    public string Name { get; } = name;
    public string Description { get; } = description;

    /// <summary>Permissions a member needs to see the command; <see cref="Permissions.None"/> (default) for everyone.</summary>
    public Permissions DefaultMemberPermissions { get; set; }

    /// <summary>Where the handler runs; <see cref="BotClientOptions.DefaultRunMode"/> when not set.</summary>
    public RunMode RunMode
    {
        get => _runMode ?? RunMode.Sync;
        set => _runMode = value;
    }

    internal RunMode? ExplicitRunMode => _runMode;
}
