# Voice.Bot.Client.Net

[Русский](README.md) · **English**

The official C#/.NET library for writing bots for the **Voice** messenger: a typed client for the new
Voice API (a bot is a regular user with its own token), slash-command and text (`!ping`) command
modules declared with attributes, checks and cooldowns, realtime events over the gateway connection,
a cache, scheduled tasks and a package for unit-testing a bot without a network.

The full protocol description (authentication, permissions, gRPC methods, event format) lives in the
Bot API documentation: `docs/bots/overview` in [Voice-LLC](https://github.com/Voice-LLC). This library is
the reference .NET implementation of that protocol; if you write a bot in another language, the protocol
documentation gives you everything you need for your own implementation.

## Requirements

- .NET 10 SDK
- A bot token (`ibot_...`) — issued to the bot's owner when the bot is created in Voice

## Installation

The package is not on NuGet yet — reference the sources directly:

```bash
git clone https://github.com/Voice-LLC/Voice.Bot.Client.Net.git
```

and add a project reference:

```xml
<ItemGroup>
  <ProjectReference Include="..\Voice.Bot.Client.Net\src\Voice.Bot.Client.Net\Voice.Bot.Client.Net.csproj" />
  <!-- for the Generic Host from the quick start: -->
  <PackageReference Include="Microsoft.Extensions.Hosting" Version="10.0.*" />
</ItemGroup>
```

Bot tests have their own project, `src/Voice.Bot.Client.Net.Testing` (see [Testing a bot](#testing-a-bot)).

## Quick start

The simplest way is `Microsoft.Extensions.Hosting` with the built-in DI integration:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Voice.Bot.Client.Net;
using Voice.Bot.Client.Net.Extensions;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddVoiceBotClient(opts =>
    {
        // An empty token is read from the VOICE_BOT_TOKEN environment variable
        opts.BotToken = builder.Configuration["BotToken"] ?? "";
        opts.Endpoint = builder.Configuration["Endpoint"] ?? opts.Endpoint;
    })
    .AddInteractionModules()  // slash commands from [SlashCommand] modules
    .AddTextCommandModules(); // text commands from [Command] modules

var host = builder.Build();
var client = host.Services.GetRequiredService<BotClient>();

client.OnReady(me =>
{
    Console.WriteLine($"Bot {me.Username} connected");
    return Task.CompletedTask;
});

client.OnMessage(ctx =>
{
    Console.WriteLine($"{ctx.Author.Username}: {ctx.Message.Content}");
    return Task.CompletedTask;
});

await host.RunAsync(); // connects the gateway, syncs the slash commands, waits for Ctrl+C / SIGTERM
```

`AddVoiceBotClient` registers `BotClient`, `InteractionService`, `CommandService` and a hosted service.
When the host starts, the service installs the modules (`AddBotModule<T>`) and connects to the gateway in
the background (while the server is unreachable it keeps retrying; the host doesn't wait), then syncs the
slash commands and logs which ones it created, updated and deleted. A token the gateway refuses stops
the application.

> `AddIoptaBotClient` is an obsolete (`[Obsolete]`) alias of `AddVoiceBotClient`, kept for compatibility.

Without DI, build the client directly with `BotClient.Create` and the command services by hand:

```csharp
using Voice.Bot.Client.Net;
using Voice.Bot.Client.Net.Commands;
using Voice.Bot.Client.Net.Interactions;

await using var client = BotClient.Create(BotClientOptions.FromEnvironment()); // VOICE_BOT_TOKEN

var interactions = new InteractionService(client);
interactions.AddModule<PingModule>();
interactions.Initialize();

var commands = new CommandService(client) { Interactions = interactions }; // !help lists slash commands too
commands.AddModule<TextCommandsModule>();
commands.Initialize();

await client.StartGatewayAsync();
await interactions.RegisterCommandsAsync();

var stop = new TaskCompletionSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.TrySetResult(); };
await stop.Task;
```

A complete working example is the console bot in `src/Voice.Bot.Client.Sample`.

## Configuration

The token and the server address are convenient to keep in `appsettings.json` (as the sample does):

```json
{
  "BotToken": "",
  "Endpoint": "https://api.iopta.org",
  "AllowUntrustedCertificates": false
}
```

Environment variables the library reads by itself:

| Variable | What it sets |
|---|---|
| `VOICE_BOT_TOKEN` | The bot token when `BotToken` is empty (in `AddVoiceBotClient`, `BotClient.Create` and `BotClientOptions.FromEnvironment()`) |
| `VOICE_PREFIX` | The default text command prefix instead of `!` |

Everything else is a `BotClientOptions` property:

| Property | Default | Purpose |
|---|---|---|
| `BotToken` | — | The bot token (`ibot_...`) |
| `Endpoint` | `https://api.iopta.org` | Voice API address, used by both gRPC and the gateway (`/hubs/bots`) |
| `AllowUntrustedCertificates` | `false` | Accept a self-signed certificate (local backend) |
| `ConfigureHttpHandler` | `null` | Customize the `SocketsHttpHandler` (proxies, client certificates) |
| `RequestTimeout` | 30 s | Limit for one API call; `null` for no limit |
| `UploadTimeout` | 5 min | Limit for uploading a file (or one part of it) |
| `TextCommandPrefix` / `TextCommandPrefixes` | `!` | One or several text command prefixes |
| `MentionPrefix` | `true` | A mention of the bot works as a prefix too: `@bot ping` |
| `CaseInsensitiveCommands` | `true` | `!PING` runs `ping` |
| `IgnoreBots` | `true` | Ignore messages from other bots (the bot's own are always ignored) |
| `SyncCommands` | `true` | Sync the slash commands when the host starts |
| `HelpCommand` | `true` | The built-in `help` command |
| `EnableCache` | `true` | Cache of groups, channels, roles and members (`client.Cache`) |
| `DefaultRunMode` | `RunMode.Sync` | Where commands run by default |
| `Replies` | English texts | What the bot answers on its own: errors, checks, cooldowns, arguments |
| `Retry` | 3 retries | Retries of failed calls; `RetryOptions.None` for none |
| `Throttle` | `null` | Client-side request rate limit (`new ThrottleOptions()` — 300 per 10 s) |

```csharp
builder.Services.AddVoiceBotClient(o =>
{
    o.TextCommandPrefixes = ["!", "?"];
    o.Retry = new RetryOptions { MaxRetries = 5 };
    o.Throttle = new ThrottleOptions();
    o.Replies.Error = "Oops, something went wrong.";
    o.Replies.Cooldown = left => $"Slow down! Try again in {BotReplies.FormatDuration(left)}.";
});
```

The client logs through the container's `ILoggerFactory` (or the one passed as the second argument of `BotClient.Create`).

## Events

Gateway events are ordinary C# events on `BotClient` (`MessageCreated`, `MessageUpdated`, `MessageDeleted`,
`ReactionAdded`, `ReactionRemoved`, `UserTyping`, `MemberJoined`, `MemberLeft`, `MemberKicked`,
`GroupStructureChanged`, `UserProfileUpdated`, `UserStatusChanged`, `VoiceConnected`, `VoiceDisconnected`,
`InteractionCreated`, `Ready`, `ConnectionError` and others). On top of them there are short `On...`
subscriptions: they skip the bot's own messages and reactions, can filter by channels and groups, and
return an `IDisposable` to unsubscribe:

```csharp
client.OnMessage(async ctx =>
{
    if (ctx.Message.Content.Contains("hello", StringComparison.OrdinalIgnoreCase)) await ctx.ReactAsync("👋");
});

client.OnMemberJoin(async e =>
    await client.SendMessageAsync(channelId, $"Welcome, {Mention.User(e.UserId)}!"));

var subscription = client.OnReactionAdd(
    e => Task.CompletedTask,
    new EventFilter(ChannelIds: [channelId]));
subscription.Dispose(); // unsubscribe

client.MessageDeleted += e =>
{
    Console.WriteLine($"Message {e.MessageId} was deleted");
    return Task.CompletedTask;
};

client.ConnectionError += ex =>
{
    Console.WriteLine($"Gateway or handler error: {ex.Message}");
    return Task.CompletedTask;
};
```

Events are handled one at a time, in arrival order: the handler for a message's creation finishes before
the handler for its deletion starts. So move long work to `Task.Run` or `RunMode.Async`. The exception is
a handler that waits for an answer (`AskAsync`, `ConfirmAsync`, `WaitForAsync`, `CollectReactionsAsync`):
from the moment it starts waiting, the following events are handled meanwhile. The connection reconnects
by itself with growing pauses; `Ready` is raised after every connection.

Waiting for a specific event or collecting reactions:

```csharp
var answer = await client.WaitForAsync<MessageCreatedEvent>(
    e => e.Message.Author.Id == userId, TimeSpan.FromSeconds(30)); // TimeoutException if nothing came

await foreach (var reaction in client.CollectReactionsAsync(messageId, TimeSpan.FromMinutes(1)))
{
    Console.WriteLine($"{reaction.UserId} reacted with {reaction.Emoji}");
}
```

## Slash commands

A slash-command module is a class derived from `InteractionModuleBase` with methods marked `[SlashCommand]`:

```csharp
using Voice.Bot.Client.Net;
using Voice.Bot.Client.Net.Interactions;
using Voice.Bot.Client.Net.Models;

public sealed class PingModule : InteractionModuleBase
{
    [SlashCommand("ping", "Replies with pong")]
    public Task Ping() => ReplyAsync("pong");

    [SlashCommand("echo", "Repeats what you say")]
    public Task Echo([SlashCommandOption("What to repeat", Required = true)] string message) =>
        ReplyAsync(message);

    [SlashCommand("add", "Adds two numbers")]
    public Task Add(
        [SlashCommandOption("First number", Required = true)] int a,
        [SlashCommandOption("Second number")] int b = 0) => ReplyAsync($"{a} + {b} = {a + b}");

    [SlashCommand("whois", "Shows a member", DefaultMemberPermissions = Permissions.ManageRoles)]
    public Task WhoIs([SlashCommandOption("Who", Required = true, Type = CommandOptionType.User)] Guid user) =>
        ReplyAsync($"That's {Mention.User(user)}");
}
```

The option type is inferred from the parameter type: `string` — String, `int`/`long` — Integer, `bool` —
Boolean, `double`/`decimal` — Number (nullable versions too). For a user, channel or role set
`Type = CommandOptionType.User/Channel/Role` — the value arrives in a `Guid` parameter. The option name
defaults to the parameter name in lower case (`Name = "..."` changes it). An optional option left empty
gets the parameter's default value; a value that doesn't convert is rejected by the bot itself
("`` `b` must be a whole number. ``") and the handler is not called. Raw values are in `Context.Options`.

On the new API a slash command **answers with a regular message** in the same channel: `ReplyAsync` in a
slash module posts a message (not an interaction response — the API has no such call), and
`Context.MessageId` is `null`.

`InteractionService.RegisterCommandsAsync()` compares the commands declared in code with those already
registered on the backend and syncs the difference (creates new ones, updates changed ones, deletes the
rest) — declaring a command in code is enough, there is no separate "publish in the console" step. With
the Generic Host this happens on start (`BotClientOptions.SyncCommands`). Without modules, a list of
commands can be synced directly:

```csharp
var result = await client.SyncCommandsAsync(
[
    new CommandDefinition("ping", "Replies with pong"),
    new CommandDefinition("roll", "Rolls a die",
        [new CommandOptionDefinition("sides", "How many sides", CommandOptionType.Integer)]),
]);
Console.WriteLine($"created {result.Created.Count}, updated {result.Updated.Count}, deleted {result.Deleted.Count}");
```

Two commands with the same name throw `ArgumentException` from `AddModule` (instead of failing the sync on start).

## Text commands (`!ping`)

Besides slash commands, the library supports classic prefix commands: the module derives from
`ModuleBase`, the methods are marked `[Command]`:

```csharp
using Voice.Bot.Client.Net;
using Voice.Bot.Client.Net.Commands;

public sealed class TextCommandsModule : ModuleBase
{
    [Command("ping", Description = "Replies with pong")]
    [Alias("p")]
    public Task Ping() => ReplyAsync("pong");

    // The last string parameter takes the rest of the text as typed - !echo hello world gives "hello world".
    [Command("echo", Description = "Repeats what you say")]
    public Task Echo(string text) => ReplyAsync(text);

    // A parameter with a default value (or a nullable one) is optional: "!add 2" answers 2.
    [Command("add", Description = "Adds two numbers")]
    public Task Add(int a, int b = 0) => ReplyAsync($"{a + b}");

    [Command("color", Description = "Picks a color")]
    public Task Color(ConsoleColor color) => ReplyAsync($"Picked {color}");

    // A Guid can be passed as a mention: !avatar @user
    [Command("avatar", Description = "Avatar link")]
    public async Task Avatar(Guid user) =>
        await ReplyAsync(await Context.Client.GetAvatarUrlAsync(user) ?? "No avatar.");
}
```

By default commands are triggered by the `!` prefix (the message `!ping`/`!p` calls `Ping()` above) —
change it with `BotClientOptions.TextCommandPrefix(es)` or the `VOICE_PREFIX` variable; a mention of the
bot (`@bot ping`) works as a prefix too. Arguments are split shell-style: single and double quotes,
backslash escapes. Supported types are `string`, whole and decimal numbers, `bool`, `Guid` (also from a
mention) and enums. When an argument is missing, doesn't convert or is extra, the bot answers with the
problem and a usage line such as `` Usage: `!add <a> [b]` ``. The built-in `!help` command lists the text
commands with their descriptions and the slash commands (`BotClientOptions.HelpCommand`; a module's own
`help` command wins). The bot never reacts to its own messages, and by default not to other bots'
messages either — a built-in guard against loops.

Without DI the same is wired by hand:

```csharp
var commands = new CommandService(client); // prefixes come from BotClientOptions
commands.AddModule<TextCommandsModule>();
commands.Initialize();
```

Modules are created anew for every command (with DI — in their own scope), so they can take
dependencies through the constructor; there's no need to register them in the container yourself.

## Checks, cooldowns and middleware

Checks are attributes on a method or on a whole module, the same for text and slash commands. When a
check fails, the bot answers with the text from `BotClientOptions.Replies` (or the attribute's
`ErrorMessage`) and the handler is not called:

```csharp
using Voice.Bot.Client.Net;
using Voice.Bot.Client.Net.Interactions;
using Voice.Bot.Client.Net.Models;

[GroupOnly]
public sealed class ModerationModule(BotClient client) : InteractionModuleBase
{
    [SlashCommand("kick", "Removes a member from the group")]
    [HasPermissions(Permissions.KickMembers)]    // the member's permissions in this channel
    [BotHasPermissions(Permissions.KickMembers)] // and the bot's own
    public async Task Kick([SlashCommandOption("Who", Required = true, Type = CommandOptionType.User)] Guid user)
    {
        await client.KickUserAsync(Context.GroupId!.Value, user);
        await ReplyAsync($"{Mention.User(user)} was kicked.");
    }

    [SlashCommand("announce", "An announcement from the bot")]
    [Roles("Moderator", "Admin")]
    [Cooldown(1, 60, CooldownScope.Group)] // once a minute per group
    public Task Announce([SlashCommandOption("Text", Required = true)] string text) => Context.SendAsync(text);

    // A command's error can be handled in the module; true means handled, no default reply needed.
    protected override async Task<bool> OnErrorAsync(Exception error)
    {
        await ReplyAsync($"Command {Context.CommandName} failed: {error.Message}");
        return true;
    }
}

public sealed class WeekendOnlyAttribute : CheckAttribute
{
    public override Task<CheckResult> CheckAsync(BotContext context, IServiceProvider? services) =>
        Task.FromResult(DateTime.Today.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
            ? CheckResult.Success
            : Fail("Only on weekends!"));
}
```

Built-in checks: `[GroupOnly]`, `[DmOnly]`, `[OwnerOnly]`, `[Users("<guid>", ...)]`,
`[Roles("name or id", ...)]`, `[HasPermissions(...)]`, `[BotHasPermissions(...)]`.
`[Cooldown(uses, seconds, scope)]` limits the rate per user (the default), channel, group or globally;
only uses that passed the checks and argument parsing count.

Middleware wraps every command (checks, arguments and the handler); without calling `next()` the command
doesn't run:

```csharp
using System.Diagnostics;

var commands = host.Services.GetRequiredService<CommandService>();
commands.Use(async (ctx, next) =>
{
    var started = Stopwatch.GetTimestamp();
    await next();
    Console.WriteLine($"{ctx.CommandName} took {Stopwatch.GetElapsedTime(started)}");
});

var interactions = host.Services.GetRequiredService<InteractionService>();
interactions.Use((ctx, next) => ctx.IsDirect ? ctx.ReplyAsync("Not in DMs.") : next());
```

A long command can run in the background so it doesn't hold up other events:
`[Command("report", RunMode = RunMode.Async)]` / `[SlashCommand(..., RunMode = RunMode.Async)]` (or
`BotClientOptions.DefaultRunMode`). An unhandled error in a command is logged, the bot answers with
`Replies.Error` ("Something went wrong."; rate limits and missing permissions have their own texts), and
`CommandService.CommandExecutionFailed` / `InteractionService.HandlerFailed` are raised.

## Command context: replies, files, typing, questions

`Context` in both kinds of modules (and `ctx` in `OnMessage`) derives from `BotContext`: who (`UserId`),
where (`ChannelId`, `GroupId`, `IsDirect`), which command (`CommandName`, `IsSlash`), and the actions to
answer:

```csharp
using Voice.Bot.Client.Net;
using Voice.Bot.Client.Net.Commands;

public sealed class ProfileModule : ModuleBase
{
    [Command("profile", RunMode = RunMode.Async)]
    public async Task Profile()
    {
        var name = await Context.AskAsync("What is your name?", TimeSpan.FromSeconds(30));
        if (name is null) { await ReplyAsync("Too slow!"); return; }

        if (!await Context.ConfirmAsync($"Save the name {name.Content}?")) return; // ✅ / ❌ reactions

        await using (Context.BeginTyping()) // "typing..." while the work goes on
        {
            await Context.ReplyAsync("Preparing the report...");
            await Context.EditAsync("The report is ready:");   // changes the last message sent
            await ReplyAsync("Here it is", [FileUpload.FromPath("report.pdf")]);
        }

        await Context.ReactAsync("👍"); // reacts to the command message (text commands only)
    }
}
```

- `ReplyAsync` replies to the command message (for a slash command — a regular message in the channel);
  `SendAsync` posts to the channel without replying; both have an overload with files.
- `ReactAsync` and `DeleteAsync` act on the command message; a slash command has none, so they throw
  `InvalidOperationException`.
- `AskAsync` waits for the next message from the same user in the same channel (one minute by default;
  `null` when nobody answered). The answer is not run as a text command, even if it looks like one.
- `ConfirmAsync` adds ✅ and ❌ under the question and returns `true` only for ✅.

Files outside commands:

```csharp
await client.SendFileAsync(channelId, FileUpload.FromPath("chart.png"), "Today's chart");
await client.SendFilesAsync(channelId, [FileUpload.FromBytes("hello.txt", "hello"u8.ToArray())]);

// A large file - in parts; on failure the upload is aborted on the server
var video = await client.UploadFileAsync("video.mp4", "video/mp4");
await client.SendMessageAsync(channelId, "Video", attachmentIds: [video.Id]);
```

`FileUpload` guesses the content type from the extension (`FileUpload.GuessContentType`).

Mentions and formatting:

```csharp
var text = $"{Mention.User(userId)}, have a look at {Mention.Channel(channelId)}. {Format.Code("!help")}";
var mentioned = Mention.ParseUsers("<@...> and <@...>");               // Guids from the text
foreach (var part in Format.Split(longReport))                         // at most 2000 characters each
    await client.SendMessageAsync(channelId, part);
```

`Format.Split` never cuts an emoji in half, and closes and reopens a code block it has to cut.

## Message history and pagination

Gives the bot access to the channel's context, not only to new events from the Gateway:

```csharp
// The last 50 messages of a channel, oldest to newest
var history = await client.GetChannelMessagesAsync(channelId, limit: 50);

// Paging backwards - the id of the oldest message received so far in beforeId
var older = await client.GetChannelMessagesAsync(channelId, limit: 50, beforeId: history[0].Id);

// A single message by id
var message = await client.GetMessageAsync(messageId);
```

The `Enumerate...Async` methods return an `IAsyncEnumerable` and walk the pages for you: members, bans,
invites, roles, the audit log, message history (newest to oldest) and search results:

```csharp
await foreach (var member in client.EnumerateGroupMembersAsync(groupId))
    Console.WriteLine(member.User.Username);

await foreach (var old in client.EnumerateMessagesAsync(channelId, limit: 500))
    if (old.Author.Id == userId) await client.DeleteMessageAsync(old.Id);
```

## Cache

`client.Cache` lazily remembers groups, channels, roles and members and clears them on gateway events (and
entirely on every reconnection). Turn it off with `BotClientOptions.EnableCache = false`:

```csharp
var general = await client.Cache.GetChannelByNameAsync(groupId, "general");
var mods = await client.Cache.GetRoleByNameAsync(groupId, "Moderators");
var member = await client.Cache.GetMemberAsync(groupId, userId);
var groups = await client.Cache.GetGroupsAsync();
```

## Managing roles and group structure

A bot with the `ManageChannels`/`ManageRoles` permissions in a group can create categories/channels and
manage roles:

```csharp
var category = await client.CreateCategoryAsync(groupId, "Bot Demo");
var channel = await client.CreateChannelAsync(category.Id, "bot-demo", ChannelType.Text);
var role = await client.CreateRoleAsync(groupId, "Muted", color: "#5865F2");
await client.AssignRoleAsync(groupId, userId, role.Id);
```

Rename/move/delete categories and channels with `UpdateCategoryAsync`/`MoveCategoryAsync`/
`DeleteCategoryAsync` and `UpdateChannelAsync`/`MoveChannelAsync`/`DeleteChannelAsync`; `GetGroupAsync`
returns the whole group structure. A delete that finds nothing to delete on a retry counts as done:

```csharp
await client.UpdateChannelAsync(channelId, name: "bot-demo-renamed");
await client.DeleteChannelAsync(channelId);
```

Moderation — `KickUserAsync` (requires `Permissions.KickMembers`), `BanUserAsync`/`UnbanUserAsync`;
invites — `CreateInviteAsync`/`GetInvitesAsync`; the audit log — `GetAuditLogAsync`:

```csharp
await client.KickUserAsync(groupId, userId);
await client.BanUserAsync(groupId, userId, reason: "spam");
var invite = await client.CreateInviteAsync(groupId, expiresAt: DateTimeOffset.UtcNow.AddDays(1), maxUses: 10);
```

## Permissions

Permission bit masks are the `Permissions` flags enum everywhere (roles, channel overwrites, effective
permissions, default permissions of slash commands). `Has` treats `Administrator` as every permission:

```csharp
var moderator = Permissions.KickMembers | Permissions.BanMembers | Permissions.ManageMessages;
var effective = await client.GetEffectivePermissionsAsync(groupId, userId, channelId);
if (!effective.Has(moderator))
    Console.WriteLine($"Missing: {effective.Missing(moderator)}");

// without userId - the bot's own permissions
var mine = await client.GetEffectivePermissionsAsync(groupId, channelId: channelId);

await client.SetChannelPermissionOverwriteAsync(channelId,
    new PermissionOverwrite(role.Id, IsRole: true, Deny: Permissions.SendMessages));
```

## Scheduled tasks and modules

`Every` and `At` start tasks after the first `Ready`, survive `StopGatewayAsync` and stop when the client
or the returned `IDisposable` is disposed. A failed run is logged and reported to `ConnectionError`; the
schedule goes on:

```csharp
client.Every(TimeSpan.FromMinutes(5), ct => client.UpdateProfileAsync(statusText: "Working", ct: ct));
client.At(new TimeOnly(9, 0), ct => client.SendMessageAsync(channelId, "Good morning!", ct: ct)); // every day at 9:00
```

`Every` keeps a pause between the end of one run and the start of the next, so runs never overlap. `At`
runs once a day in local time (or in the `TimeZoneInfo` passed as the third argument), across
daylight-saving changes too.

Handlers and tasks that belong to one feature fit nicely into an `IBotModule`:

```csharp
using Voice.Bot.Client.Net;

public sealed class WelcomeModule : IBotModule
{
    public void Install(BotClient client)
    {
        client.OnMemberJoin(async joined =>
        {
            var channel = await client.Cache.GetChannelByNameAsync(joined.GroupId, "welcome");
            if (channel is not null)
                await client.SendMessageAsync(channel.Id, $"Welcome, {Mention.User(joined.UserId)}!");
        });
    }
}
```

With DI — `builder.Services.AddBotModule<WelcomeModule>()` (the module can take constructor
dependencies); without DI — `client.Install(new WelcomeModule())`.

## Retries and error handling

Any failed API call throws `BotApiException` (never a raw `RpcException`) with a `StatusCode` that makes it
easy to tell missing permissions (`PermissionDenied`) from invalid request data (`InvalidArgument`), a
missing object (`NotFound`) and so on. `IsRateLimited` says the server is rate-limiting the bot,
`RetryAfter` how long it asks to wait. When the `CancellationToken` passed to a method is cancelled, the
method throws `OperationCanceledException`, not `BotApiException`:

```csharp
using Grpc.Core;

try
{
    await client.SendMessageAsync(channelId, "hello", ct: ct);
}
catch (BotApiException ex) when (ex.IsRateLimited)
{
    await Task.Delay(ex.RetryAfter ?? TimeSpan.FromSeconds(5));
}
catch (BotApiException ex) when (ex.StatusCode == StatusCode.PermissionDenied)
{
    Console.WriteLine("The bot lacks permissions in this channel.");
}
catch (OperationCanceledException)
{
    // we were asked to stop
}
```

Failed calls are retried automatically (`BotClientOptions.Retry`, 3 times by default with pauses of
0.5 s, 1 s, 2 s...): rate limits for every method, network errors and timeouts only for safe calls (reads
and changes that can be repeated). Creating messages, uploads, roles, channels, invites and commands is
not retried after a network error, so a lost response never produces a duplicate; a cancelled call is
never retried. The rules can be changed for a block of code:

```csharp
using (client.WithRetries(RetryOptions.None))
{
    await client.SendMessageAsync(channelId, "no retries");
}
```

## Testing a bot

The `Voice.Bot.Client.Net.Testing` project provides `FakeVoiceServer`, an in-memory Voice server: a
`BotClient` without a network, a small world (a group with an owner, a member and a `#general` channel),
methods that simulate what users do, and a record of everything the bot did. Each simulation method
returns once the bot's handlers have finished:

```csharp
using Voice.Bot.Client.Net.Commands;
using Voice.Bot.Client.Net.Interactions;
using Voice.Bot.Client.Net.Testing;
using Xunit;

public class BotTests
{
    [Fact]
    public async Task Ping_RepliesPong()
    {
        await using var server = new FakeVoiceServer();
        var commands = new CommandService(server.Client);
        commands.AddModule<TextCommandsModule>();
        commands.Initialize();

        await server.SendMessageAsync("!ping");

        Assert.Equal("pong", server.LastSentMessage?.Content);
    }

    [Fact]
    public async Task SlashAdd_UsesDefaultForMissingOption()
    {
        await using var server = new FakeVoiceServer(o => o.Replies.Error = "Error");
        var interactions = new InteractionService(server.Client);
        interactions.AddModule<PingModule>();
        interactions.Initialize();

        await server.InvokeSlashCommandAsync("add", new Dictionary<string, string> { ["a"] = "2" });

        Assert.Equal("2 + 0 = 2", server.LastSentMessage?.Content);
    }
}
```

Besides messages and slash commands you can simulate reactions (`AddReactionAsync`), members joining and
leaving (`MemberJoinAsync`/`MemberLeaveAsync`), a lost connection (`DisconnectAsync`/`ConnectAsync`), and
add users, channels, roles and permissions (`AddUser`, `AddChannel`, `AddRole`, `SetPermissions`). What
the bot did is in `SentMessages`, `Reactions`, `EditedMessages`, `DeletedMessages`, `Uploads`,
`Moderation` and `Calls`. `FailNext("CreateMessage", StatusCode.Unavailable)` makes the next call fail
(retries are off in the fake; turn them on with `o.Retry` in the constructor). For `RunMode.Async` and
questions (`AskAsync`) use `WaitForSentMessageAsync`.

## What the new API doesn't support

The new Voice API doesn't have the following, so neither does the library (older versions did):

- **interaction responses** (`RespondAsync`, deferred responses, followup messages) — a slash command
  answers with a regular message in the channel via `ReplyAsync`/`SendAsync`;
- **message components** — buttons, menus and their handlers;
- **modals**;
- **autocomplete** for options;
- **intents** — event categories can't be subscribed to selectively;
- **subcommands and command groups** — slash command options can't nest;
- leaving a voice channel.

## Project structure

```
src/Voice.Bot.Client.Net/
  BotClient.cs, BotClient.*.cs   - facade over the Voice API, one partial file per group of methods
                                    (Users/Groups/Channels/Roles/Messages/Attachments/Files/SlashCommands/
                                    Gateway/EventHelpers/Waiting/Pagination/Schedule)
  BotContext.cs, BotReplies.cs   - the shared command context and the automatic reply texts
  Checks.cs                      - check attributes and [Cooldown]
  BotCache.cs, Mention.cs, FileUpload.cs, Permissions.cs, RetryOptions.cs, IBotModule.cs
  Commands/                      - text ("!ping") commands: CommandService, ModuleBase, [Command]/[Alias]
  Interactions/                  - slash commands: InteractionService, InteractionModuleBase, attributes
  Gateway/                       - SignalR connection to /hubs/bots
  Models/, Events/               - models and gateway events
  Extensions/                    - DI integration (AddVoiceBotClient, AddInteractionModules,
                                    AddTextCommandModules, AddBotModule) and the hosted service
  Internal/                      - internal utilities (API calls, retries, file uploads, argument binding)
src/Voice.Bot.Client.Net.Testing/ - FakeVoiceServer for bot unit tests
src/Voice.Bot.Client.Sample/     - sample console bot (slash and text commands, moderation, greetings)
tests/Voice.Bot.Client.Net.Tests/ - unit tests (fake server, no real network)
protos/                          - Protobuf contracts (shared with the Voice backend)
```

Changes between versions are in [CHANGELOG.md](CHANGELOG.md).

## Tests

```bash
dotnet test
```

## Links

- The [Voice-LLC](https://github.com/Voice-LLC) organization — all official Voice projects.
- The full Bot API documentation (for those writing their own library in another language) — the
  "Bots" section of the Voice documentation.
