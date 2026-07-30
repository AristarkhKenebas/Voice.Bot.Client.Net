using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Voice.Bot.Client.Net;
using Voice.Bot.Client.Net.Commands;
using Voice.Bot.Client.Net.Extensions;
using Voice.Bot.Client.Net.Interactions;
using Voice.Bot.Client.Net.Models;

// End-to-end usage of Voice.Bot.Client.Net: token -> GetMeAsync/GetMyGroupsAsync -> gateway subscribe ->
// module dispatch (see PingModule for slash commands, TextCommandsModule for "!ping"-style prefix
// commands) -> SendMessageAsync/RegisterCommandsAsync. Needs a bot with the "Guilds"/
// "GuildMessages" intents granted by its owner to see/echo messages, and "ApplicationCommands" to
// register its own slash commands (see BotIntents) - without them the calls below throw
// BotApiException(PermissionDenied). Text commands need no intent beyond "GuildMessages" - they're
// plain messages, not the Interactions API.
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddIoptaBotClient(opts =>
{
    opts.BotToken = builder.Configuration["BotToken"] is { Length: > 0 } token
        ? token
        : throw new InvalidOperationException("Set BotToken in appsettings.json (or the BotToken environment variable) before running the sample.");
    opts.BotsApiEndpoint = builder.Configuration["BotsApiEndpoint"] ?? opts.BotsApiEndpoint;
    opts.AllowUntrustedCertificates = builder.Configuration.GetValue("AllowUntrustedCertificates", false);
    opts.TextCommandPrefix = builder.Configuration["TextCommandPrefix"] ?? opts.TextCommandPrefix;
});

builder.Services.AddInteractionModules();
builder.Services.AddTextCommandModules();

var host = builder.Build();

var client = host.Services.GetRequiredService<BotClient>();
var interactions = host.Services.GetRequiredService<InteractionService>();
var commands = host.Services.GetRequiredService<CommandService>();

commands.CommandExecutionFailed += (ex, message) =>
{
    Console.Error.WriteLine($"[text-command error] {message.Content}: {ex}");
    return Task.CompletedTask;
};

client.MessageCreated += async message =>
{
    if (message.Author.IsBot) return;
    
    Console.WriteLine($"[message-created] {message.ChannelId} {message.Content}");
};

client.InteractionCreated += interactionEvent =>
{
    Console.WriteLine($"[interaction-created] {interactionEvent.InteractionId}");
    return Task.CompletedTask;
};

client.UserJoinedGroup += joined =>
{
    Console.WriteLine($"[group-events] joined group {joined.Group.Name} ({joined.GroupId})");
    return Task.CompletedTask;
};

client.UserStatusChanged += changed =>
{
    Console.WriteLine($"[group-events] {changed.UserId} is now {changed.PresenceStatus} (group {changed.GroupId})");
    return Task.CompletedTask;
};

client.BotTyping += typing =>
{
    Console.WriteLine(typing.IsActive
        ? $"[chat-events] {typing.Username} is {typing.State} in {typing.ChannelId}"
        : $"[chat-events] {typing.Username} stopped in {typing.ChannelId}");
    return Task.CompletedTask;
};

client.UnhandledEvent += evt =>
{
    Console.WriteLine($"[unhandled] {evt.Topic}/{evt.Type}");
    return Task.CompletedTask;
};

client.ConnectionError += ex =>
{
    Console.Error.WriteLine($"[gateway error] {ex}");
    return Task.CompletedTask;
};

await host.StartAsync();

// Bulk registration: builds definitions from PingModule's [SlashCommand]/
// [SlashCommandOption] attributes and reconciles them against what's already registered (deleteMissing
// defaults to true, so removing a [SlashCommand] method and restarting also deletes it on the backend).
await interactions.RegisterCommandsAsync();

// Live presence, broadcast to every group this bot is in - no intent required, same as GetMeAsync.

var me = await client.GetMeAsync();

Console.WriteLine($"Logged in as {me.Username} ({me.Id})");

foreach (var group in await client.GetMyGroupsAsync())
{
    Console.WriteLine($"  in group: {group.Name} ({group.Id})");
}
await client.SetStatusAsync(UserStatus.DoNotDisturb);
await host.WaitForShutdownAsync();
