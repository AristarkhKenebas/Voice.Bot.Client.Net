using Voice.Bot.Client.Net;
using Voice.Bot.Client.Net.Interactions;

namespace Voice.Bot.Client.Sample;

/// <summary>
/// Demonstrates the interaction module framework end-to-end - registered via
/// <c>AddInteractionModules()</c>, pushed to the backend by <c>InteractionService.RegisterCommandsAsync</c>.
/// </summary>
public sealed class PingModule(BotClient client) : InteractionModuleBase
{
    [SlashCommand("ping", "Replies with pong")]
    public async Task Ping()
    {
        await Task.Delay(1000);

        await RespondAsync("pong");
    }

    [SlashCommand("hi", "Test command create role")]
    public async Task Hi()
    {
        var createdRole = await client.CreateRoleAsync(Context.Interaction.GroupId, "hi", color: "#5865F2");
        await client.UpdateRoleAsync(createdRole.Id, "hi", 0, color: "#5865F2");
        
        await RespondAsync("hi");
    }

    [SlashCommand("echo", "Repeats back whatever you say")]
    public Task Echo([SlashCommandOption("What to repeat", Required = true)] string message) => RespondAsync(message);

    [SlashCommand("testcommand", "Test command")]
    public async Task TestCommand([SlashCommandOption("ping", Required = true)] string ping, [SlashCommandOption("pong", Required = false)] string? pong, [SlashCommandOption("pang", Required = false)] string? pang)
    {
        await RespondAsync($"{ping} {pong} {pang}");
    }
}
