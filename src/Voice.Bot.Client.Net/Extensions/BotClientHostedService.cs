using Microsoft.Extensions.Hosting;
using Voice.Bot.Client.Net.Commands;
using Voice.Bot.Client.Net.Interactions;

namespace Voice.Bot.Client.Net.Extensions;

/// <summary>
/// Drives <see cref="BotClient"/>'s lifecycle from the host - the piece that makes
/// <c>AddIoptaBotClient</c> work under both <c>Host.CreateApplicationBuilder</c> (console) and
/// <c>WebApplication.CreateBuilder</c> (ASP.NET Core), since both share <see cref="IHostedService"/>.
/// </summary>
internal sealed class BotClientHostedService(
    BotClient client,
    InteractionService interactions,
    CommandService commands,
    IEnumerable<InteractionModuleAssemblyRegistration> interactionModuleAssemblies,
    IEnumerable<TextCommandModuleAssemblyRegistration> textCommandModuleAssemblies) : IHostedService
{
    public Task StartAsync(CancellationToken ct)
    {
        foreach (var registration in interactionModuleAssemblies)
        {
            interactions.AddModulesFromAssembly(registration.Assembly);
        }

        foreach (var registration in textCommandModuleAssemblies)
        {
            commands.AddModulesFromAssembly(registration.Assembly);
        }

        interactions.Initialize();
        commands.Initialize();
        return client.StartGatewayAsync(ct);
    }

    public Task StopAsync(CancellationToken ct) => client.StopGatewayAsync();
}
