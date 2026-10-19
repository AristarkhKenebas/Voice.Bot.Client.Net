using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Voice.Bot.Client.Net.Commands;
using Voice.Bot.Client.Net.Interactions;

namespace Voice.Bot.Client.Net.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="BotClientOptions"/>, a singleton <see cref="BotClient"/> (built via
    /// <see cref="BotClient.Create"/> from it, logging through the container's <see cref="ILoggerFactory"/>),
    /// a singleton <see cref="InteractionService"/> and <see cref="CommandService"/> that create modules in a
    /// fresh DI scope for every command, and an <see cref="Microsoft.Extensions.Hosting.IHostedService"/> that
    /// on host startup installs the <see cref="IBotModule"/>s and, in the background (the host doesn't wait
    /// for the server), connects the gateway and syncs the slash commands
    /// (<see cref="BotClientOptions.SyncCommands"/>); a token the gateway refuses stops the application. On
    /// shutdown it stops it all.
    /// </summary>
    /// <param name="configure">
    /// Sets the options. A token left empty is read from the <c>VOICE_BOT_TOKEN</c> environment variable, the
    /// default prefix from <c>VOICE_PREFIX</c>; <c>null</c> to take both from the environment.
    /// </param>
    /// <example>
    /// <code>
    /// var builder = Host.CreateApplicationBuilder(args);
    /// builder.Services.AddVoiceBotClient(o =&gt; o.TextCommandPrefixes = ["!", "?"])
    ///     .AddInteractionModules()
    ///     .AddTextCommandModules()
    ///     .AddBotModule&lt;WelcomeModule&gt;();
    /// await builder.Build().RunAsync(); // Ctrl+C / SIGTERM stop the bot
    /// </code>
    /// </example>
    public static IServiceCollection AddVoiceBotClient(this IServiceCollection services, Action<BotClientOptions>? configure = null)
    {
        services.AddSingleton(_ =>
        {
            var options = new BotClientOptions { BotToken = "" };
            configure?.Invoke(options);
            options.ResolveToken();
            return options;
        });

        services.AddSingleton(sp => BotClient.Create(sp.GetRequiredService<BotClientOptions>(), sp.GetService<ILoggerFactory>()));
        services.AddSingleton(sp =>
        {
            var interactions = new InteractionService(sp.GetRequiredService<BotClient>(), sp);
            foreach (var registration in sp.GetServices<InteractionModuleAssemblyRegistration>())
            {
                interactions.AddModulesFromAssembly(registration.Assembly);
            }

            return interactions;
        });
        services.AddSingleton(sp =>
        {
            var commands = new CommandService(sp.GetRequiredService<BotClient>(), null, sp)
            {
                Interactions = sp.GetRequiredService<InteractionService>(),
            };
            foreach (var registration in sp.GetServices<TextCommandModuleAssemblyRegistration>())
            {
                commands.AddModulesFromAssembly(registration.Assembly);
            }

            return commands;
        });
        services.AddHostedService<BotClientHostedService>();

        return services;
    }

    /// <summary>The old name of <see cref="AddVoiceBotClient"/>, kept so existing code still compiles.</summary>
    [Obsolete("Renamed to AddVoiceBotClient.")]
    public static IServiceCollection AddIoptaBotClient(this IServiceCollection services, Action<BotClientOptions>? configure = null) =>
        services.AddVoiceBotClient(configure);

    /// <summary>
    /// Registers every non-abstract <see cref="InteractionModuleBase"/> subclass found in
    /// <paramref name="assembly"/> (defaults to the calling assembly) as a transient service, so
    /// modules can take constructor-injected dependencies - and records the assembly so the
    /// <see cref="InteractionService"/> knows its commands as soon as it is resolved (before the host
    /// starts, too).
    /// Call after <see cref="AddVoiceBotClient"/>.
    /// </summary>
    public static IServiceCollection AddInteractionModules(this IServiceCollection services, Assembly? assembly = null)
    {
        assembly ??= Assembly.GetCallingAssembly();

        foreach (var type in assembly.GetTypes())
        {
            if (typeof(InteractionModuleBase).IsAssignableFrom(type) && !type.IsAbstract)
            {
                services.AddTransient(type);
            }
        }

        services.AddSingleton(new InteractionModuleAssemblyRegistration(assembly));

        return services;
    }

    /// <summary>
    /// Registers every non-abstract <see cref="ModuleBase"/> subclass found in <paramref name="assembly"/>
    /// (defaults to the calling assembly) as a transient service, and records the assembly so the
    /// <see cref="CommandService"/> knows its commands as soon as it is resolved. Call
    /// after <see cref="AddVoiceBotClient"/>.
    /// </summary>
    public static IServiceCollection AddTextCommandModules(this IServiceCollection services, Assembly? assembly = null)
    {
        assembly ??= Assembly.GetCallingAssembly();

        foreach (var type in assembly.GetTypes())
        {
            if (typeof(ModuleBase).IsAssignableFrom(type) && !type.IsAbstract)
            {
                services.AddTransient(type);
            }
        }

        services.AddSingleton(new TextCommandModuleAssemblyRegistration(assembly));

        return services;
    }

    /// <summary>
    /// Registers <typeparamref name="TModule"/> as a singleton (so it can take constructor-injected
    /// dependencies) that the hosted service installs on the <see cref="BotClient"/> at startup, before the
    /// gateway connects.
    /// </summary>
    public static IServiceCollection AddBotModule<TModule>(this IServiceCollection services)
        where TModule : class, IBotModule
    {
        services.AddSingleton<TModule>();
        services.AddSingleton<IBotModule>(sp => sp.GetRequiredService<TModule>());
        return services;
    }
}
