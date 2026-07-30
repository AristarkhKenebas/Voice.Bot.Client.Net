using Voice.Bot.Client.Net.Events;

namespace Voice.Bot.Client.Net;

/// <summary>Gateway lifecycle (<see cref="StartGatewayAsync"/>/<see cref="StopGatewayAsync"/>) and event dispatch.</summary>
public sealed partial class BotClient
{
    public Task StartGatewayAsync(CancellationToken ct = default) => _gateway.StartAsync(ct);

    public Task StopGatewayAsync() => _gateway.StopAsync();

    private void OnGatewayMessageReceived(string topic, string json)
    {
        // Fire-and-forget from the gateway's synchronous callback - failures surface via ConnectionError,
        // never as an unobserved exception that could take down the connection's dispatch loop.
        _ = HandleGatewayMessageAsync(topic, json);
    }

    private async Task HandleGatewayMessageAsync(string topic, string json)
    {
        try
        {
            var result = GatewayEventParser.Parse(topic, json);
            await DispatchAsync(result).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await RaiseAsync(ConnectionError, ex).ConfigureAwait(false);
        }
    }

    private Task DispatchAsync(GatewayParseResult result) => result switch
    {
        GatewayParseResult.MessageCreated e => RaiseAsync(MessageCreated, e.Message),
        GatewayParseResult.MessageUpdated e => RaiseAsync(MessageUpdated, e.Message),
        GatewayParseResult.MessageDeleted e => RaiseAsync(MessageDeleted, e.MessageId, e.Success),
        GatewayParseResult.ReactionAdded e => RaiseAsync(ReactionAdded, e.Reaction),
        GatewayParseResult.ReactionRemoved e => RaiseAsync(ReactionRemoved, e.Reaction),
        GatewayParseResult.UserTyping e => RaiseAsync(UserTyping, e.Typing),
        GatewayParseResult.BotTyping e => RaiseAsync(BotTyping, e.Typing),
        GatewayParseResult.UserConnected e => RaiseAsync(UserConnected, e.Presence),
        GatewayParseResult.UserDisconnected e => RaiseAsync(UserDisconnected, e.Presence),
        GatewayParseResult.UserJoinedGroup e => RaiseAsync(UserJoinedGroup, e.Joined),
        GatewayParseResult.UserStatusChanged e => RaiseAsync(UserStatusChanged, e.StatusChanged),
        GatewayParseResult.InteractionCreated e => RaiseAsync(InteractionCreated, e.Interaction),
        GatewayParseResult.Unknown e => RaiseAsync(UnhandledEvent, e.Event),
        GatewayParseResult.ParseError e => RaiseAsync(ConnectionError, e.Exception),
        _ => Task.CompletedTask,
    };

    private void OnGatewayError(Exception ex) => _ = RaiseAsync(ConnectionError, ex);

    private static async Task RaiseAsync<T>(Func<T, Task>? handler, T arg)
    {
        if (handler is null) return;
        foreach (var d in handler.GetInvocationList())
        {
            await ((Func<T, Task>)d)(arg).ConfigureAwait(false);
        }
    }

    private static async Task RaiseAsync(Func<Guid, bool, Task>? handler, Guid messageId, bool success)
    {
        if (handler is null) return;
        foreach (var d in handler.GetInvocationList())
        {
            await ((Func<Guid, bool, Task>)d)(messageId, success).ConfigureAwait(false);
        }
    }
}
