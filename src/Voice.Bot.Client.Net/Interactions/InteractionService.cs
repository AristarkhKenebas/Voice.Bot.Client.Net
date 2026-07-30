using System.Reflection;

namespace Voice.Bot.Client.Net.Interactions;

/// <summary>
/// Discovers <see cref="InteractionModuleBase"/> subclasses (via <see cref="AddModule{T}"/>/
/// <see cref="AddModulesFromAssembly"/>), then dispatches incoming <c>BotClient.InteractionCreated</c>
/// events to the matching attributed method by command name / <c>CustomId</c> pattern /
/// (command name, focused option). Call <see cref="Initialize"/> once, after registering every module.
/// </summary>
/// <remarks>
/// Split into three partial files: this one (module registration), <c>InteractionService.Registration.cs</c>
/// (syncing <c>[SlashCommand]</c> declarations to the backend), and <c>InteractionService.Dispatch.cs</c>
/// (matching an incoming interaction to a handler and invoking it).
/// </remarks>
public sealed partial class InteractionService
{
    private readonly BotClient _client;
    private readonly Func<Type, object>? _moduleFactory;
    private readonly List<RegisteredHandler> _handlers = [];
    private bool _initialized;

    /// <param name="client">The client whose <c>InteractionCreated</c> event this service dispatches.</param>
    /// <param name="moduleFactory">
    /// Resolves a module instance for a given module <see cref="Type"/> - defaults to
    /// <see cref="Activator.CreateInstance(Type)"/> (parameterless constructor) when omitted. Pass a
    /// DI resolver (e.g. <c>IServiceProvider.GetRequiredService</c>) to let modules take constructor-injected services.
    /// </param>
    public InteractionService(BotClient client, Func<Type, object>? moduleFactory = null)
    {
        _client = client;
        _moduleFactory = moduleFactory;
    }

    public void AddModule<T>() where T : InteractionModuleBase => AddModule(typeof(T));

    public void AddModule(Type moduleType)
    {
        if (!typeof(InteractionModuleBase).IsAssignableFrom(moduleType) || moduleType.IsAbstract)
        {
            throw new ArgumentException($"{moduleType} must be a non-abstract subclass of InteractionModuleBase.", nameof(moduleType));
        }

        foreach (var method in moduleType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (method.GetCustomAttribute<SlashCommandAttribute>() is { } slash)
                _handlers.Add(new RegisteredHandler(HandlerKind.SlashCommand, slash.Name, null, method, moduleType));

            if (method.GetCustomAttribute<ComponentInteractionAttribute>() is { } component)
                _handlers.Add(new RegisteredHandler(HandlerKind.Component, component.CustomIdPattern, null, method, moduleType));

            if (method.GetCustomAttribute<ModalInteractionAttribute>() is { } modal)
                _handlers.Add(new RegisteredHandler(HandlerKind.Modal, modal.CustomIdPattern, null, method, moduleType));

            if (method.GetCustomAttribute<AutocompleteHandlerAttribute>() is { } autocomplete)
                _handlers.Add(new RegisteredHandler(HandlerKind.Autocomplete, autocomplete.CommandName, autocomplete.OptionName, method, moduleType));
        }
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
