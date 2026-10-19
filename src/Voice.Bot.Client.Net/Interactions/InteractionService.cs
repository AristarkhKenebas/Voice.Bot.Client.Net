using System.Reflection;
using Microsoft.Extensions.Logging;
using Voice.Bot.Client.Net.Internal;
using Voice.Bot.Client.Net.Models;

namespace Voice.Bot.Client.Net.Interactions;

/// <summary>
/// Discovers <see cref="InteractionModuleBase"/> subclasses (via <see cref="AddModule{T}"/>/
/// <see cref="AddModulesFromAssembly"/>), then dispatches incoming slash-command calls
/// (<c>BotClient.InteractionCreated</c>) to the matching <c>[SlashCommand]</c> method by command name.
/// Call <see cref="Initialize"/> once, after registering every module.
/// </summary>
/// <remarks>
/// <para>
/// Option values are converted to the parameters' types; a value that doesn't convert is answered with
/// <see cref="BotReplies.InvalidArgument"/> (e.g. "`days` must be a whole number.") instead of running the
/// handler, and a missing optional value takes the parameter's default. Checks, cooldowns, middleware and
/// error replies work as for text commands. Subcommands and groups are not supported: the API's command
/// options can't nest.
/// </para>
/// Split into three partial files: this one (module registration), <c>InteractionService.Registration.cs</c>
/// (syncing <c>[SlashCommand]</c> declarations to the backend), and <c>InteractionService.Dispatch.cs</c>
/// (matching an incoming interaction to a handler and invoking it).
/// </remarks>
public sealed partial class InteractionService
{
    private readonly BotClient _client;
    private readonly ModuleActivator _modules;
    private readonly IServiceProvider? _services;
    private readonly List<RegisteredHandler> _handlers = [];
    private readonly CommandRunner _runner;
    private readonly ILogger _logger;
    private bool _initialized;

    /// <summary>Raised when a command handler throws - keeps a bad command from affecting other subscribers.</summary>
    public event Func<Exception, Events.BotInteractionEvent, Task>? HandlerFailed;

    /// <param name="client">The client whose <c>InteractionCreated</c> event this service dispatches.</param>
    /// <param name="moduleFactory">
    /// Resolves a module instance for a given module <see cref="Type"/> - defaults to
    /// <see cref="Activator.CreateInstance(Type)"/> (parameterless constructor) when omitted. To use dependency
    /// injection, use the constructor that takes an <see cref="IServiceProvider"/>.
    /// </param>
    public InteractionService(BotClient client, Func<Type, object>? moduleFactory = null)
    {
        _client = client;
        _modules = new ModuleActivator(null, moduleFactory);
        _logger = client.LoggerFactory.CreateLogger<InteractionService>();
        _runner = new CommandRunner(client, _logger);
    }

    /// <param name="client">The client whose <c>InteractionCreated</c> event this service dispatches.</param>
    /// <param name="services">
    /// Every command call gets its own DI scope: modules may depend on scoped services (a <c>DbContext</c>, say)
    /// and are disposed after the call. Modules don't have to be registered in the container.
    /// </param>
    public InteractionService(BotClient client, IServiceProvider services)
    {
        _client = client;
        _services = services;
        _modules = new ModuleActivator(services, null);
        _logger = client.LoggerFactory.CreateLogger<InteractionService>();
        _runner = new CommandRunner(client, _logger);
    }

    /// <summary>The commands the registered modules declare - what <see cref="RegisterCommandsAsync"/> syncs.</summary>
    public IReadOnlyList<CommandDefinition> Commands => _handlers.Select(BuildDefinition).ToList();

    /// <summary>
    /// Adds middleware that runs around every slash command - checks, option binding and the handler - in
    /// the order added. Call <c>next()</c> to continue; skip it to stop the command.
    /// </summary>
    public InteractionService Use(Func<BotContext, Func<Task>, Task> middleware)
    {
        _runner.Use(middleware);
        return this;
    }

    public void AddModule<T>() where T : InteractionModuleBase => AddModule(typeof(T));

    /// <exception cref="ArgumentException">
    /// The type isn't a module, or a slash command name is empty, contains whitespace or is already taken
    /// by another handler - caught here rather than when the commands are synced on start.
    /// </exception>
    public void AddModule(Type moduleType)
    {
        if (!typeof(InteractionModuleBase).IsAssignableFrom(moduleType) || moduleType.IsAbstract)
        {
            throw new ArgumentException($"{moduleType} must be a non-abstract subclass of InteractionModuleBase.", nameof(moduleType));
        }

        var added = new List<RegisteredHandler>();
        foreach (var method in moduleType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (method.GetCustomAttribute<SlashCommandAttribute>() is not { } slash) continue;

            var where = $"{moduleType.Name}.{method.Name}";
            if (string.IsNullOrEmpty(slash.Name) || slash.Name.Any(char.IsWhiteSpace))
            {
                throw new ArgumentException($"Slash command name '{slash.Name}' of {where} must be one word without spaces.", nameof(moduleType));
            }

            if (_handlers.Concat(added).FirstOrDefault(h => h.CommandName == slash.Name) is { } taken)
            {
                throw new ArgumentException(
                    $"Slash command name '{slash.Name}' of {where} is already used by {taken.ModuleType.Name}.{taken.Method.Name}.", nameof(moduleType));
            }

            added.Add(new RegisteredHandler(slash.Name, method, moduleType, new CommandMetadata(method, moduleType, slash.ExplicitRunMode)));
        }

        _handlers.AddRange(added);
    }

    /// <summary>Registers every non-abstract <see cref="InteractionModuleBase"/> subclass found in <paramref name="assembly"/>.</summary>
    public void AddModulesFromAssembly(Assembly assembly)
    {
        foreach (var type in assembly.GetTypes())
        {
            if (typeof(InteractionModuleBase).IsAssignableFrom(type) && !type.IsAbstract)
            {
                AddModule(type);
            }
        }
    }

    /// <summary>Subscribes to <c>BotClient.InteractionCreated</c>. Safe to call more than once - later calls are no-ops.</summary>
    public void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        _client.InteractionCreated += DispatchAsync;
    }
}
