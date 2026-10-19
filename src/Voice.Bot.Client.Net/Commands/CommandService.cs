using System.Reflection;
using Microsoft.Extensions.Logging;
using Voice.Bot.Client.Net.Interactions;
using Voice.Bot.Client.Net.Internal;

namespace Voice.Bot.Client.Net.Commands;

/// <summary>
/// Prefix ("text") commands - the classic <c>!ping</c> alternative to slash commands. Discovers
/// <see cref="ModuleBase"/> subclasses (via <see cref="AddModule{T}"/>/<see cref="AddModulesFromAssembly"/>), then matches incoming
/// <c>BotClient.MessageCreated</c> events whose content starts with one of the prefixes (or a mention of the
/// bot) to the matching <c>[Command]</c>-attributed method. Call <see cref="Initialize"/> once, after registering
/// every module. See <see cref="Interactions.InteractionService"/> for the slash-command equivalent.
/// </summary>
/// <remarks>
/// <para>
/// Arguments are split shell-style (quotes, backslash escapes) and bound to the method's parameters in
/// order; the last <see cref="string"/> parameter takes the rest of the text exactly as typed - quotes,
/// line breaks and spacing included. Parameters with a default value or a nullable type are optional. A
/// missing, malformed or extra argument is answered with the problem and a usage line generated from the
/// method, e.g. <c>Usage: !add &lt;a&gt; [b]</c>.
/// </para>
/// <para>
/// Settings - prefixes, mention prefix, case, bots, <c>help</c>, replies - come from the client's
/// <see cref="BotClientOptions"/>. Unless a module defines its own, a built-in <c>help</c> command lists
/// the text commands and the slash commands of <see cref="Interactions"/>.
/// </para>
/// Split into two partial files: this one (construction and module registration) and
/// <c>CommandService.Dispatch.cs</c> (matching an incoming message to a handler, binding its
/// arguments, and invoking it).
/// </remarks>
public sealed partial class CommandService
{
    private readonly BotClient _client;
    private readonly ModuleActivator _modules;
    private readonly IServiceProvider? _services;
    private readonly List<RegisteredCommand> _commands = [];
    private readonly string? _prefix;
    private readonly CommandRunner _runner;
    private readonly ILogger _logger;
    private bool _initialized;

    /// <summary>
    /// The first prefix a message must start with to be considered a command invocation, e.g. <c>"!"</c>.
    /// See <see cref="Prefixes"/> for all of them.
    /// </summary>
    public string Prefix => Prefixes.FirstOrDefault() ?? "";

    /// <summary>Every prefix that triggers a command - the constructor's, or <see cref="BotClientOptions.TextCommandPrefixes"/>.</summary>
    public IReadOnlyList<string> Prefixes => _prefix is not null ? [_prefix] : [.. _client.Options.TextCommandPrefixes];

    /// <summary>The slash commands the built-in <c>help</c> lists alongside the text commands; set by <c>AddVoiceBotClient</c>.</summary>
    public InteractionService? Interactions { get; set; }

    /// <summary>Raised when a matched command's handler throws - keeps a bad command from taking down the gateway's message dispatch loop.</summary>
    public event Func<Exception, Models.Message, Task>? CommandExecutionFailed;

    /// <param name="client">The client whose <c>MessageCreated</c> event this service dispatches.</param>
    /// <param name="prefix">The prefix a message must start with, e.g. <c>"!"</c>; <c>null</c> (default) for <see cref="BotClientOptions.TextCommandPrefixes"/>.</param>
    /// <param name="moduleFactory">
    /// Resolves a module instance for a given module <see cref="Type"/> - defaults to
    /// <see cref="Activator.CreateInstance(Type)"/> (parameterless constructor) when omitted. To use dependency
    /// injection, use the constructor that takes an <see cref="IServiceProvider"/>.
    /// </param>
    public CommandService(BotClient client, string? prefix = null, Func<Type, object>? moduleFactory = null)
    {
        _client = client;
        _prefix = prefix;
        _modules = new ModuleActivator(null, moduleFactory);
        _logger = client.LoggerFactory.CreateLogger<CommandService>();
        _runner = new CommandRunner(client, _logger);
    }

    /// <param name="client">The client whose <c>MessageCreated</c> event this service dispatches.</param>
    /// <param name="prefix">The prefix a message must start with, e.g. <c>"!"</c>; <c>null</c> for <see cref="BotClientOptions.TextCommandPrefixes"/>.</param>
    /// <param name="services">
    /// Every command call gets its own DI scope: modules may depend on scoped services (a <c>DbContext</c>, say)
    /// and are disposed after the call. Modules don't have to be registered in the container.
    /// </param>
    public CommandService(BotClient client, string? prefix, IServiceProvider services)
    {
        _client = client;
        _prefix = prefix;
        _services = services;
        _modules = new ModuleActivator(services, null);
        _logger = client.LoggerFactory.CreateLogger<CommandService>();
        _runner = new CommandRunner(client, _logger);
    }

    /// <summary>The registered commands, in registration order.</summary>
    public IReadOnlyList<TextCommandInfo> Commands =>
        _commands.Select(c => new TextCommandInfo(c.Name, c.Aliases, c.Description, c.Usage(Prefix))).ToList();

    public void AddModule<T>() where T : ModuleBase => AddModule(typeof(T));

    /// <exception cref="ArgumentException">
    /// The type isn't a module, a command name or alias is empty or contains whitespace, or it is already
    /// taken by another command (ignoring case when <see cref="BotClientOptions.CaseInsensitiveCommands"/> is on).
    /// </exception>
    public void AddModule(Type moduleType)
    {
        if (!typeof(ModuleBase).IsAssignableFrom(moduleType) || moduleType.IsAbstract)
        {
            throw new ArgumentException($"{moduleType} must be a non-abstract subclass of ModuleBase.", nameof(moduleType));
        }

        var added = new List<RegisteredCommand>();
        foreach (var method in moduleType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (method.GetCustomAttribute<CommandAttribute>() is not { } command) continue;

            var aliases = method.GetCustomAttribute<AliasAttribute>()?.Aliases ?? [];
            if (CheckNames(command.Name, aliases, method, _commands.Concat(added)) is { } problem) throw new ArgumentException(problem, nameof(moduleType));
            var parameters = method.GetParameters();
            var bound = parameters
                .Select((p, i) => new CommandParameter(
                    p, p.Name ?? $"arg{i}", ArgumentConverter.IsOptional(p), i == parameters.Length - 1 && p.ParameterType == typeof(string)))
                .ToList();
            added.Add(new RegisteredCommand(
                command.Name, aliases, method, moduleType, command.Description, bound, new CommandMetadata(method, moduleType, command.ExplicitRunMode)));
        }

        _commands.AddRange(added);
    }

    /// <summary>Why a command can't have these names - one can't be typed as one word, or another command already answers to it; <c>null</c> when they're fine.</summary>
    private string? CheckNames(string name, IReadOnlyList<string> aliases, MethodInfo method, IEnumerable<RegisteredCommand> existing)
    {
        var where = $"{method.DeclaringType?.Name}.{method.Name}";
        var comparison = _client.Options.CaseInsensitiveCommands ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var names = aliases.Prepend(name).ToList();
        for (var i = 0; i < names.Count; i++)
        {
            var candidate = names[i];
            if (string.IsNullOrEmpty(candidate) || candidate.Any(char.IsWhiteSpace)) return $"Text command name '{candidate}' of {where} must be one word without spaces.";
            if (names.Take(i).Any(earlier => string.Equals(earlier, candidate, comparison))) return $"{where} lists the name '{candidate}' twice.";
            if (existing.FirstOrDefault(c => c.Matches(candidate, comparison)) is { } taken)
            {
                return $"Text command name '{candidate}' of {where} is already used by {taken.Method.DeclaringType?.Name}.{taken.Method.Name}.";
            }
        }

        return null;
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

    /// <summary>
    /// Adds middleware that runs around every command - checks, argument parsing and the handler - in the
    /// order added. Call <c>next()</c> to continue; skip it to stop the command.
    /// </summary>
    /// <example>
    /// <code>
    /// commands.Use(async (ctx, next) =&gt;
    /// {
    ///     var started = Stopwatch.GetTimestamp();
    ///     await next();
    ///     logger.LogInformation("{Command} took {Elapsed}", ctx.CommandName, Stopwatch.GetElapsedTime(started));
    /// });
    /// </code>
    /// </example>
    public CommandService Use(Func<BotContext, Func<Task>, Task> middleware)
    {
        _runner.Use(middleware);
        return this;
    }

    /// <summary>Subscribes to <c>BotClient.MessageCreated</c>. Safe to call more than once - later calls are no-ops.</summary>
    public void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        _client.MessageCreated += DispatchAsync;
    }
}

/// <summary>A registered text command, as listed by <see cref="CommandService.Commands"/>.</summary>
/// <param name="Usage">Generated from the method, e.g. <c>!add &lt;a&gt; [b]</c>.</param>
public sealed record TextCommandInfo(string Name, IReadOnlyList<string> Aliases, string? Description, string Usage);
