using System.Reflection;

namespace Voice.Bot.Client.Net.Commands;

/// <summary>
/// Prefix ("text") commands - the classic <c>!ping</c> alternative to slash commands. Discovers
/// <see cref="ModuleBase"/> subclasses (via <see cref="AddModule{T}"/>/<see cref="AddModulesFromAssembly"/>), then matches incoming
/// <c>BotClient.MessageCreated</c> events whose content starts with <see cref="Prefix"/> to the
/// matching <c>[Command]</c>-attributed method. Call <see cref="Initialize"/> once, after registering
/// every module. See <see cref="Interactions.InteractionService"/> for the slash-command equivalent.
/// </summary>
/// <remarks>
/// Split into two partial files: this one (construction and module registration) and
/// <c>CommandService.Dispatch.cs</c> (matching an incoming message to a handler, binding its
/// arguments, and invoking it).
/// </remarks>
public sealed partial class CommandService
{
    private readonly BotClient _client;
    private readonly Func<Type, object>? _moduleFactory;
    private readonly List<RegisteredCommand> _commands = [];
    private bool _initialized;

    /// <summary>The prefix a message must start with to be considered a command invocation, e.g. <c>"!"</c>.</summary>
    public string Prefix { get; }

    /// <summary>Raised when a matched command's handler throws - keeps a bad command from taking down the gateway's message dispatch loop.</summary>
    public event Func<Exception, Events.GatewayMessage, Task>? CommandExecutionFailed;

    /// <param name="client">The client whose <c>MessageCreated</c> event this service dispatches.</param>
    /// <param name="prefix">The prefix a message must start with, e.g. <c>"!"</c> (default).</param>
    /// <param name="moduleFactory">
    /// Resolves a module instance for a given module <see cref="Type"/> - defaults to
    /// <see cref="Activator.CreateInstance(Type)"/> (parameterless constructor) when omitted. Pass a
    /// DI resolver (e.g. <c>IServiceProvider.GetRequiredService</c>) to let modules take constructor-injected services.
    /// </param>
    public CommandService(BotClient client, string prefix = "!", Func<Type, object>? moduleFactory = null)
    {
        _client = client;
        Prefix = prefix;
        _moduleFactory = moduleFactory;
    }

    public void AddModule<T>() where T : ModuleBase => AddModule(typeof(T));

    public void AddModule(Type moduleType)
    {
        if (!typeof(ModuleBase).IsAssignableFrom(moduleType) || moduleType.IsAbstract)
        {
            throw new ArgumentException($"{moduleType} must be a non-abstract subclass of ModuleBase.", nameof(moduleType));
        }

        foreach (var method in moduleType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (method.GetCustomAttribute<CommandAttribute>() is not { } command) continue;

            var aliases = method.GetCustomAttribute<AliasAttribute>()?.Aliases ?? [];
            _commands.Add(new RegisteredCommand(command.Name, aliases, method, moduleType));
        }
    }

    /// <summary>Registers every non-abstract <see cref="ModuleBase"/> subclass found in <paramref name="assembly"/>.</summary>
    public void AddModulesFromAssembly(Assembly assembly)
    {
        foreach (var type in assembly.GetTypes())
        {
            if (typeof(ModuleBase).IsAssignableFrom(type) && !type.IsAbstract)
            {
                AddModule(type);
            }
        }
    }

    /// <summary>Subscribes to <c>BotClient.MessageCreated</c>. Safe to call more than once - later calls are no-ops.</summary>
    public void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        _client.MessageCreated += DispatchAsync;
    }
}
