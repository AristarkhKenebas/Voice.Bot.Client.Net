using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Voice.Bot.Client.Net.Commands;
using Voice.Bot.Client.Net.Interactions;

namespace Voice.Bot.Client.Net.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="BotClientOptions"/>, a singleton <see cref="BotClient"/> (built via
    /// <see cref="BotClient.Create"/> from it), a singleton <see cref="InteractionService"/> and
    /// <see cref="CommandService"/> both wired to resolve modules through this container, and an
    /// <see cref="Microsoft.Extensions.Hosting.IHostedService"/> that starts the gateway connection on
    /// host startup and stops it on shutdown.
    /// </summary>
    public static IServiceCollection AddIoptaBotClient(this IServiceCollection services, Action<BotClientOptions> configure)
    {
        services.AddSingleton(_ =>
        {
            var options = new BotClientOptions { BotToken = "" };
            configure(options);
            if (string.IsNullOrEmpty(options.BotToken))
            {
                throw new InvalidOperationException("BotClientOptions.BotToken must be set by the AddIoptaBotClient configure delegate.");
            }

            return options;
        });

        services.AddSingleton(sp => BotClient.Create(sp.GetRequiredService<BotClientOptions>()));
        services.AddSingleton(sp => new InteractionService(sp.GetRequiredService<BotClient>(), sp.GetRequiredService));
        services.AddSingleton(sp => new CommandService(
            sp.GetRequiredService<BotClient>(), sp.GetRequiredService<BotClientOptions>().TextCommandPrefix, sp.GetRequiredService));
        services.AddHostedService<BotClientHostedService>();

        return services;
    }

    /// <summary>
    /// Registers every non-abstract <see cref="InteractionModuleBase"/> subclass found in
    /// <paramref name="assembly"/> (defaults to the calling assembly) as a transient service, so
    /// modules can take constructor-injected dependencies - and records the assembly for
    /// <see cref="BotClientHostedService"/> to scan into <see cref="InteractionService"/> at startup.
    /// Call after <see cref="AddIoptaBotClient"/>.
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
    /// (defaults to the calling assembly) as a transient service, and records the assembly for
    /// <see cref="BotClientHostedService"/> to scan into <see cref="CommandService"/> at startup. Call
    /// after <see cref="AddIoptaBotClient"/>.
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
}
