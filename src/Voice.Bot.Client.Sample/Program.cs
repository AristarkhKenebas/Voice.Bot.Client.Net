using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenAI;
using OpenAI.Chat;
using Voice.Bot.Client;
using Voice.Bot.Client.Extensions;
using Voice.Bot.Client.Interactions;
using Voice.Bot.Client.Models;

// End-to-end usage of Voice.Bot.Client: token -> GetMeAsync/GetMyGroupsAsync -> gateway subscribe ->
// module dispatch (see PingModule) -> SendMessageAsync/RegisterCommandsAsync. Needs a bot with the
// "Guilds"/"GuildMessages" intents granted by its owner to see/echo messages, and "ApplicationCommands"
// to register its own slash commands (see BotIntents) - without them the calls below throw
// BotApiException(PermissionDenied).
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddIoptaBotClient(opts =>
{
    opts.BotToken = builder.Configuration["BotToken"] is { Length: > 0 } token
        ? token
        : throw new InvalidOperationException("Set BotToken in appsettings.json (or the BotToken environment variable) before running the sample.");
    opts.BotsApiEndpoint = builder.Configuration["BotsApiEndpoint"] ?? opts.BotsApiEndpoint;
    opts.GatewayEndpoint = builder.Configuration["GatewayEndpoint"] ?? opts.GatewayEndpoint;
    opts.InteractionsApiEndpoint = builder.Configuration["InteractionsApiEndpoint"] ?? opts.InteractionsApiEndpoint;
    opts.AllowUntrustedCertificates = builder.Configuration.GetValue("AllowUntrustedCertificates", false);
});

builder.Services.AddInteractionModules();

var host = builder.Build();

var client = host.Services.GetRequiredService<BotClient>();
var interactions = host.Services.GetRequiredService<InteractionService>();

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

// Discord.Net-style bulk registration: builds definitions from PingModule's [SlashCommand]/
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
