using Voice.Bot.Client.Net.Commands;

namespace Voice.Bot.Client.Sample;

/// <summary>
/// Prefix ("text") commands - the classic <c>!ping</c> alternative to slash commands.
/// Triggered by messages starting with <see cref="Net.BotClientOptions.TextCommandPrefix"/> (default
/// <c>"!"</c>), matched by <see cref="CommandService"/> instead of the Interactions API. See
/// <see cref="PingModule"/> for the equivalent slash-command module.
/// </summary>
public sealed class TextCommandsModule : ModuleBase
{
    [Command("test")]
    public Task Test() => ReplyAsync("test received!");

    [Command("ping")]
    [Alias("p")]
    public Task Ping() => ReplyAsync("pong");

    // Trailing string parameters are "greedy" - !echo hello world binds text to "hello world", not just "hello".
    [Command("echo")]
    public Task Echo(string text) => ReplyAsync(text);

    [Command("add")]
    public Task Add(int a, int b) => ReplyAsync($"{a} + {b} = {a + b}");
}
