using Voice.Bot.Client.Net;
using Voice.Bot.Client.Net.Commands;

namespace Voice.Bot.Client.Sample;

/// <summary>
/// Prefix ("text") commands - the classic <c>!ping</c> alternative to slash commands.
/// Triggered by messages starting with <see cref="Net.BotClientOptions.TextCommandPrefix"/> (default
/// <c>"!"</c>) or a mention of the bot, matched by <see cref="CommandService"/> instead of the Interactions
/// API. <c>!help</c> lists them all. See <see cref="PingModule"/> for the equivalent slash-command module.
/// </summary>
public sealed class TextCommandsModule : ModuleBase
{
    [Command("test", Description = "Checks that the bot is alive")]
    public Task Test() => ReplyAsync("test received!");

    [Command("ping", Description = "Replies with pong")]
    [Alias("p")]
    public Task Ping() => ReplyAsync("pong");

    // The last string parameter takes the rest of the text - !echo hello world binds text to "hello world".
    [Command("echo", Description = "Repeats what you say")]
    public Task Echo(string text) => ReplyAsync(text);

    // Typed and optional arguments: "!add 2" answers 2, "!add x" answers with the usage line.
    [Command("add", Description = "Adds two numbers")]
    public Task Add(int a, int b = 0) => ReplyAsync($"{a} + {b} = {a + b}");

    [Command("hello", Description = "Asks your name", RunMode = RunMode.Async)]
    [Cooldown(1, 10)]
    public async Task Hello()
    {
        var answer = await Context.AskAsync("What is your name?", TimeSpan.FromSeconds(30));
        await ReplyAsync(answer is null ? "Too slow!" : $"Nice to meet you, {answer.Content}!");
    }
}
