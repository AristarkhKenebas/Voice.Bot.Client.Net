using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Voice.Bot.Client.Net;
using Voice.Bot.Client.Net.Extensions;
using Voice.Bot.Client.Net.Models;
using Voice.Bot.Client.Sample;

// End-to-end usage of Voice.Bot.Client.Net on the generic host: the token comes from appsettings.json or
// the VOICE_BOT_TOKEN environment variable; starting the host connects the gateway, syncs the [SlashCommand]
// methods (PingModule, StatusModule, ModerationModule) and starts the prefix commands (TextCommandsModule).
// Ctrl+C / SIGTERM stop it. A bot without a required permission gets BotApiException(PermissionDenied)
// from the API - the command framework answers "I don't have permission to do that." for you.
var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddVoiceBotClient(opts =>
    {
        opts.BotToken = builder.Configuration["BotToken"] ?? "";
        opts.Endpoint = builder.Configuration["Endpoint"] ?? opts.Endpoint;
        opts.AllowUntrustedCertificates = builder.Configuration.GetValue("AllowUntrustedCertificates", false);
        if (builder.Configuration["TextCommandPrefix"] is { Length: > 0 } prefix) opts.TextCommandPrefix = prefix;
        opts.Replies.Error = "Oops, something went wrong.";
    })
    .AddInteractionModules()
    .AddTextCommandModules()
    .AddBotModule<WelcomeModule>();

var host = builder.Build();

var client = host.Services.GetRequiredService<BotClient>();
var logger = host.Services.GetRequiredService<ILogger<Program>>();

client.OnReady(async me =>
{
    logger.LogInformation("Logged in as {Username} ({Id})", me.Username, me.Id);
    await client.SetStatusAsync(UserStatus.Online);
    foreach (var group in await client.Cache.GetGroupsAsync())
    {
        logger.LogInformation("In group {Name} ({Id})", group.Name, group.Id);
    }
});

client.OnMessage(ctx =>
{
    logger.LogInformation("[{Channel}] {Author}: {Content}", ctx.ChannelId, ctx.Author.Username, ctx.Message.Content);
    return Task.CompletedTask;
});

await host.RunAsync();
