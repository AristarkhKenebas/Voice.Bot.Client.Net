using Microsoft.Extensions.DependencyInjection;

namespace Voice.Bot.Client.Net.Internal;

/// <summary>
/// Creates a command module for one call and cleans up after it. With a service provider, every call
/// gets its own DI scope, so modules can depend on scoped services, and the scope disposes them.
/// </summary>
internal sealed class ModuleActivator(IServiceProvider? services, Func<Type, object>? factory)
{
    public async Task RunAsync<TModule>(Type moduleType, Func<TModule, Task> run)
    {
        if (services is not null)
        {
            var scope = services.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                var resolved = scope.ServiceProvider.GetService(moduleType);
                var module = resolved ?? ActivatorUtilities.CreateInstance(scope.ServiceProvider, moduleType);
                try
                {
                    await run((TModule)module).ConfigureAwait(false);
                }
                finally
                {
                    if (resolved is null) await DisposeAsync(module).ConfigureAwait(false);
                }
            }

            return;
        }

        if (factory is not null)
        {
            await run((TModule)factory(moduleType)).ConfigureAwait(false);
            return;
        }

        var created = Activator.CreateInstance(moduleType)!;
        try
        {
            await run((TModule)created).ConfigureAwait(false);
        }
        finally
        {
            await DisposeAsync(created).ConfigureAwait(false);
        }
    }

    private static async ValueTask DisposeAsync(object module)
    {
        if (module is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        else if (module is IDisposable disposable) disposable.Dispose();
    }
}
