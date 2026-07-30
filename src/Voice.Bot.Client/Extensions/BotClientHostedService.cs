using Microsoft.Extensions.Hosting;
using Voice.Bot.Client.Interactions;

namespace Voice.Bot.Client.Extensions;

/// <summary>
/// Drives <see cref="BotClient"/>'s lifecycle from the host - the piece that makes
/// <c>AddIoptaBotClient</c> work under both <c>Host.CreateApplicationBuilder</c> (console) and
/// <c>WebApplication.CreateBuilder</c> (ASP.NET Core), since both share <see cref="IHostedService"/>.
/// </summary>
internal sealed class BotClientHostedService(
    BotClient client,
    InteractionService interactions,
    IEnumerable<ModuleAssemblyRegistration> moduleAssemblies) : IHostedService
{
    public Task StartAsync(CancellationToken ct)
    {
        foreach (var registration in moduleAssemblies)
        {
            interactions.AddModulesFromAssembly(registration.Assembly);
        }

        interactions.Initialize();
        return client.StartGatewayAsync(ct);
    }

    public Task StopAsync(CancellationToken ct) => client.StopGatewayAsync();
}
