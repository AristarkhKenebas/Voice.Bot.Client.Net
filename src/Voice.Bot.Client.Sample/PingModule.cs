using Voice.Bot.Client.Interactions;

namespace Voice.Bot.Client.Sample;

/// <summary>
/// Demonstrates the interaction module framework end-to-end - registered via
/// <c>AddInteractionModules()</c>, pushed to the backend by <c>InteractionService.RegisterCommandsAsync</c>.
/// </summary>
public sealed class PingModule : InteractionModuleBase
{
    [SlashCommand("ping", "Replies with pong")]
    public async Task Ping()
    {
        await Task.Delay(1000);
        await RespondAsync("pong");
    }

    [SlashCommand("echo", "Repeats back whatever you say")]
    public Task Echo([SlashCommandOption("What to repeat", Required = true)] string message) => RespondAsync(message);

    [SlashCommand("testcommand", "Test command")]
    public async Task TestCommand([SlashCommandOption("ping", Required = true)] string ping, [SlashCommandOption("pong", Required = false)] string? pong, [SlashCommandOption("pang", Required = false)] string? pang)
    {
        await RespondAsync($"{ping} {pong} {pang}");
    }
}
