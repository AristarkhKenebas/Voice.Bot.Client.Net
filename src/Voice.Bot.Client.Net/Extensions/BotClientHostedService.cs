using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Voice.Bot.Client.Net.Commands;
using Voice.Bot.Client.Net.Interactions;

namespace Voice.Bot.Client.Net.Extensions;

/// <summary>
/// Drives <see cref="BotClient"/>'s lifecycle from the host - the piece that makes
/// <c>AddVoiceBotClient</c> work under both <c>Host.CreateApplicationBuilder</c> (console) and
/// <c>WebApplication.CreateBuilder</c> (ASP.NET Core), since both share <see cref="IHostedService"/>.
/// Starting installs the modules and returns; connecting to the gateway (retrying while the server is
/// unreachable) and syncing the slash commands go on in the background, so an unreachable server doesn't
/// hold up the rest of the host. A token the gateway refuses stops the application. The host's own
/// Ctrl+C / SIGTERM handling stops it all.
/// </summary>
internal sealed class BotClientHostedService(
    BotClient client,
    InteractionService interactions,
    CommandService commands,
    IEnumerable<IBotModule> modules,
    ILogger<BotClientHostedService> logger,
    IHostApplicationLifetime? lifetime = null)
    : IHostedService, IDisposable
{
    private readonly CancellationTokenSource _stopping = new();
    private Task _connecting = Task.CompletedTask;

    /// <summary>The background connect-and-sync started by <see cref="StartAsync"/> - for tests.</summary>
    internal Task Connecting => _connecting;

    public Task StartAsync(CancellationToken ct)
    {
        foreach (var module in modules) client.Install(module);
        interactions.Initialize();
        commands.Initialize();
        _connecting = Task.Run(() => ConnectAsync(_stopping.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct)
    {
        await _stopping.CancelAsync().ConfigureAwait(false);
        await _connecting.WaitAsync(ct).ContinueWith(_ => { }, TaskScheduler.Default).ConfigureAwait(false);
        await client.StopGatewayAsync().ConfigureAwait(false);
    }

    public void Dispose() => _stopping.Dispose();

    /// <summary>Waits for the gateway, then syncs the slash commands. Ends quietly when the host stops first.</summary>
    internal async Task ConnectAsync(CancellationToken ct)
    {
        try
        {
            await client.StartGatewayAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return;
        }
        catch (BotApiException ex)
        {
            logger.LogCritical(ex, "The gateway refused the bot token ({Status}); stopping the application", ex.StatusCode);
            lifetime?.StopApplication();
            return;
        }

        await SyncCommandsAsync(ct).ConfigureAwait(false);
    }

    /// <summary>A failed sync is logged, not fatal: the bot still works with the commands registered before.</summary>
    private async Task SyncCommandsAsync(CancellationToken ct)
    {
        if (!client.Options.SyncCommands || interactions.Commands.Count == 0) return;
        try
        {
            var result = await interactions.RegisterCommandsAsync(ct: ct).ConfigureAwait(false);
            logger.LogInformation("Slash commands synced: created {Created}, updated {Updated}, deleted {Deleted}",
                Names(result.Created), Names(result.Updated), Names(result.Deleted));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (BotApiException ex)
        {
            logger.LogError(ex, "Could not sync the slash commands");
        }
    }

    private static string Names(IReadOnlyCollection<Models.BotCommandDefinition> commands) =>
        commands.Count == 0 ? "none" : string.Join(", ", commands.Select(c => "/" + c.Name));
}
