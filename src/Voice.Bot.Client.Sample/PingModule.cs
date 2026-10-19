using Voice.Bot.Client.Net.Interactions;

namespace Voice.Bot.Client.Sample;

/// <summary>
/// Demonstrates the interaction module framework end-to-end - registered via
/// <c>AddInteractionModules()</c>, pushed to the backend by <c>InteractionService.RegisterCommandsAsync</c>.
/// </summary>
public sealed class PingModule : InteractionModuleBase
{
    [SlashCommand("ping", "Replies with pong")]
    public Task Ping() => ReplyAsync("pong");

    [SlashCommand("echo", "Repeats back whatever you say")]
    public Task Echo([SlashCommandOption("What to repeat", Required = true)] string message) => ReplyAsync(message);

    [SlashCommand("add", "Adds two numbers")]
    public Task Add(
        [SlashCommandOption("First number", Required = true)] int a,
        [SlashCommandOption("Second number")] int? b) => ReplyAsync($"{a} + {b ?? 0} = {a + (b ?? 0)}");
}
