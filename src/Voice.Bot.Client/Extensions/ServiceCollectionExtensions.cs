using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Voice.Bot.Client.Interactions;

namespace Voice.Bot.Client.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers a singleton <see cref="BotClient"/> (built via <see cref="BotClient.Create"/> from
    /// <paramref name="configure"/>), a singleton <see cref="InteractionService"/> wired to resolve
    /// modules through this container, and an <see cref="IHostedService"/> that starts the gateway
    /// connection on host startup and stops it on shutdown.
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

            return BotClient.Create(options);
        });

        services.AddSingleton(sp => new InteractionService(sp.GetRequiredService<BotClient>(), sp.GetRequiredService));
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

        services.AddSingleton(new ModuleAssemblyRegistration(assembly));

        return services;
    }
}
