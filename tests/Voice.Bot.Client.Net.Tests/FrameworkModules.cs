using Grpc.Core;
using Voice.Bot.Client.Net.Commands;
using Voice.Bot.Client.Net.Interactions;
using Voice.Bot.Client.Net.Models;

namespace Voice.Bot.Client.Net.Tests;

public sealed class TextModule : ModuleBase
{
    [Command("ping", Description = "Replies with pong")]
    [Alias("p")]
    public Task Ping() => ReplyAsync("pong");

    [Command("add", Description = "Adds two numbers")]
    public Task Add(int a, int b = 10) => ReplyAsync($"{a + b}");

    [Command("say")]
    public Task Say(string text) => ReplyAsync(text);

    [Command("who")]
    public Task Who(Guid user, string? note = null) => ReplyAsync($"{user}|{note ?? "-"}");

    [Command("flag")]
    public Task Flag(bool on, double ratio, Level level = Level.Low) => ReplyAsync(FormattableString.Invariant($"{on}|{ratio}|{level}"));

    [Command("boom")]
    public Task Boom() => throw new InvalidOperationException("kaboom");

    [Command("denied")]
    public Task Denied() => throw new BotApiException(StatusCode.PermissionDenied, "missing ManageRoles");

    [Command("limited")]
    public Task Limited() => throw new BotApiException(StatusCode.Unavailable, "Bad gRPC response. HTTP status code: 429");

    [Command("name")]
    public async Task Name()
    {
        var answer = await Context.AskAsync("What is your name?", TimeSpan.FromSeconds(5));
        await ReplyAsync(answer is null ? "Too slow" : $"Hi {answer.Content}");
    }

    [Command("quick")]
    public async Task Quick()
    {
        var answer = await Context.AskAsync("Quick!", TimeSpan.FromMilliseconds(100));
        await ReplyAsync(answer is null ? "Too slow" : "Fast");
    }

    [Command("wipe")]
    public async Task Wipe() => await ReplyAsync(await Context.ConfirmAsync("Sure?", TimeSpan.FromSeconds(5)) ? "Wiped" : "Kept");

    [Command("background", RunMode = RunMode.Async)]
    public async Task Background()
    {
        await Task.Delay(50);
        await ReplyAsync("done later");
    }

    [Command("edit")]
    public async Task Edit()
    {
        await ReplyAsync("Counting...");
        await Context.EditAsync("Done: 42");
        await Context.ReactAsync("✅");
    }

    [Command("file")]
    public Task File() => ReplyAsync("here", [FileUpload.FromBytes("report.csv", "a,b"u8.ToArray())]);

    public enum Level
    {
        Low,
        High,
    }
}

public sealed class HandledErrorsModule : ModuleBase
{
    public static Exception? Handled;

    [Command("oops")]
    public Task Oops() => throw new InvalidOperationException("handled here");

    protected internal override Task<bool> OnErrorAsync(Exception error)
    {
        Handled = error;
        return Task.FromResult(true);
    }
}

public sealed class OwnHelpModule : ModuleBase
{
    [Command("help")]
    public Task Help() => ReplyAsync("my own help");
}

[GroupOnly]
public sealed class CheckedModule : ModuleBase
{
    [Command("owner")]
    [OwnerOnly]
    public Task Owner() => ReplyAsync("owner ok");

    [Command("mods")]
    [Roles("Moderators")]
    public Task Mods() => ReplyAsync("mods ok");

    [Command("kick")]
    [HasPermissions(Permissions.KickMembers)]
    public Task Kick() => ReplyAsync("kick ok");

    [Command("purge")]
    [BotHasPermissions(Permissions.ManageMessages)]
    public Task Purge() => ReplyAsync("purge ok");

    [Command("custom")]
    [HasPermissions(Permissions.BanMembers, ErrorMessage = "Mods only.")]
    public Task Custom() => ReplyAsync("custom ok");

    [Command("once")]
    [Cooldown(1, 60)]
    public Task Once() => ReplyAsync("once ok");
}

public sealed class DmModule : ModuleBase
{
    [Command("secret")]
    [DmOnly]
    public Task Secret() => ReplyAsync("psst");
}

public sealed class SlashModule : InteractionModuleBase
{
    [SlashCommand("days", "Counts days", DefaultMemberPermissions = Permissions.ManageMessages)]
    public Task Days(
        [SlashCommandOption("How many")] int n = 3,
        [SlashCommandOption("Who", Type = CommandOptionType.User)] Guid? user = null) => ReplyAsync($"{n}|{user?.ToString() ?? "-"}");

    [SlashCommand("guarded", "Only in groups")]
    [GroupOnly]
    [Cooldown(1, 30, CooldownScope.Channel)]
    public Task Guarded() => ReplyAsync("guarded ok");

    [SlashCommand("crash", "Fails")]
    public Task Crash() => throw new InvalidOperationException("slash kaboom");
}
