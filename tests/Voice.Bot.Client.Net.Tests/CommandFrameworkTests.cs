using FluentAssertions;
using Vc.Backend.WebApi.Messages.Grpc;
using Voice.Bot.Client.Net.Commands;
using Voice.Bot.Client.Net.Events;
using Voice.Bot.Client.Net.Gateway;
using Voice.Bot.Client.Net.Interactions;
using Xunit;

namespace Voice.Bot.Client.Net.Tests;

public sealed class TestCommandModule : ModuleBase
{
    public readonly TaskCompletionSource<string> Called = new();

    [Command("ping")]
    [Alias("p")]
    public Task Ping()
    {
        Called.TrySetResult("ping");
        return ReplyAsync("pong");
    }

    [Command("add")]
    public Task Add(int a, int? b)
    {
        Called.TrySetResult($"{a}+{b?.ToString() ?? "null"}");
        return Task.CompletedTask;
    }

    [Command("echo")]
    public Task Echo(string text)
    {
        Called.TrySetResult(text);
        return Task.CompletedTask;
    }
}

public sealed class TestSlashModule : InteractionModuleBase
{
    public readonly TaskCompletionSource<string> Called = new();

    [SlashCommand("weather", "Show the weather", DefaultMemberPermissions = Permissions.ManageEmojisAndStickers)]
    public Task Weather(
        [SlashCommandOption("Where", Required = true)] string city,
        [SlashCommandOption("How many days")] int? days)
    {
        Called.TrySetResult($"{city}:{days?.ToString() ?? "null"}");
        return ReplyAsync("sunny");
    }

    [SlashCommand("boom", "Fails")]
    public Task Boom() => throw new InvalidOperationException("kaboom");
}

public class CommandServiceTests
{
    private static string Text(string content, bool bot = false) =>
        Envelopes.Make("MessageCreated", new { Id = Known.MessageId, ChannelId = Known.ChannelId, Content = content, Author = new { Id = Known.UserId, IsBot = bot } });

    [Fact]
    public async Task PrefixCommand_ShouldRunAndReplyToTheMessage()
    {
        await using var h = await TestHarness.StartAsync([new FakeMessages()]);
        var module = new TestCommandModule();
        var commands = new CommandService(h.Client, "!", _ => module);
        commands.AddModule<TestCommandModule>();
        commands.Initialize();

        h.Gateway.Deliver(GatewayTopics.ChatEvents, Text("!p"));

        (await module.Called.Task.WaitAsync(TimeSpan.FromSeconds(3))).Should().Be("ping");
        await WaitFor(() => h.Recorder.Last<CreateMessageRequest>("create") is not null);
        var reply = h.Recorder.Last<CreateMessageRequest>("create")!;
        (reply.ChannelId, reply.Content, reply.ReplyTo).Should().Be((Known.ChannelId, "pong", Known.MessageId));
    }

    [Theory]
    [InlineData("!add 2 3", "2+3")]
    [InlineData("!add 2", "2+null")]
    [InlineData("!echo hello world", "hello world")]
    public async Task Arguments_ShouldBind(string content, string expected)
    {
        await using var h = await TestHarness.StartAsync([]);
        var module = new TestCommandModule();
        var commands = new CommandService(h.Client, "!", _ => module);
        commands.AddModule<TestCommandModule>();
        commands.Initialize();

        h.Gateway.Deliver(GatewayTopics.ChatEvents, Text(content));
        (await module.Called.Task.WaitAsync(TimeSpan.FromSeconds(3))).Should().Be(expected);
    }

    [Fact]
    public async Task BadArgumentsAndBots_ShouldNotRunTheCommand()
    {
        await using var h = await TestHarness.StartAsync([new FakeMessages()]);
        var module = new TestCommandModule();
        var commands = new CommandService(h.Client, "!", _ => module);
        commands.AddModule<TestCommandModule>();
        var failed = new TaskCompletionSource<Exception>();
        commands.CommandExecutionFailed += (ex, _) => { failed.TrySetResult(ex); return Task.CompletedTask; };
        commands.Initialize();

        h.Gateway.Deliver(GatewayTopics.ChatEvents, Text("!ping", bot: true));
        h.Gateway.Deliver(GatewayTopics.ChatEvents, Text("!add two 3"));

        await WaitFor(() => h.Recorder.Last<CreateMessageRequest>("create") is not null);
        h.Recorder.Last<CreateMessageRequest>("create")!.Content.Should().Be("`a` must be a whole number.\nUsage: `!add <a> [b]`");
        module.Called.Task.IsCompleted.Should().BeFalse();
        failed.Task.IsCompleted.Should().BeFalse();
    }

    internal static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("condition not met in 3 s");
            await Task.Delay(10);
        }
    }
}

public class InteractionServiceTests
{
    private static string Command(string name, object options) =>
        Envelopes.Make("InteractionCreated", new
        {
            InteractionId = Guid.NewGuid(), Type = 0, InvokingUserId = Known.UserId, ChannelId = Known.ChannelId, GroupId = Known.GroupId,
            Data = new { CommandName = name, Options = options },
        });

    [Fact]
    public async Task SlashCommand_ShouldBindOptionsAndReplyInTheChannel()
    {
        await using var h = await TestHarness.StartAsync([new FakeMessages()]);
        var module = new TestSlashModule();
        var interactions = new InteractionService(h.Client, _ => module);
        interactions.AddModule<TestSlashModule>();
        interactions.Initialize();

        h.Gateway.Deliver(GatewayTopics.InteractionEvents, Command("weather", new { city = "Berlin" }));

        (await module.Called.Task.WaitAsync(TimeSpan.FromSeconds(3))).Should().Be("Berlin:null");
        await CommandServiceTests.WaitFor(() => h.Recorder.Last<CreateMessageRequest>("create") is not null);
        var reply = h.Recorder.Last<CreateMessageRequest>("create")!;
        (reply.ChannelId, reply.Content).Should().Be((Known.ChannelId, "sunny"));
    }

    [Fact]
    public async Task FailingHandler_ShouldRaiseHandlerFailedAndKeepOthersWorking()
    {
        await using var h = await TestHarness.StartAsync([new FakeMessages()]);
        var module = new TestSlashModule();
        var interactions = new InteractionService(h.Client, _ => module);
        interactions.AddModule<TestSlashModule>();
        var failed = new TaskCompletionSource<Exception>();
        interactions.HandlerFailed += (ex, _) => { failed.TrySetResult(ex); return Task.CompletedTask; };
        var raw = new TaskCompletionSource<BotInteractionEvent>();
        h.Client.InteractionCreated += e => { if (e.Data.CommandName == "boom") raw.TrySetResult(e); return Task.CompletedTask; };
        interactions.Initialize();

        h.Gateway.Deliver(GatewayTopics.InteractionEvents, Command("boom", new { }));

        (await failed.Task.WaitAsync(TimeSpan.FromSeconds(3))).Message.Should().Be("kaboom");
        await raw.Task.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task RegisterCommands_ShouldSyncAttributesIncludingPermissions()
    {
        var fake = new FakeInteractions();
        await using var h = await TestHarness.StartAsync([new FakeUsers(), fake]);
        var interactions = new InteractionService(h.Client);
        interactions.AddModule<TestSlashModule>();

        var result = await interactions.RegisterCommandsAsync();

        result.Created.Select(c => c.Name).Should().BeEquivalentTo("weather", "boom");
        var weather = fake.Registered.Single(r => r.Name == "weather");
        weather.DefaultMemberPermissions.Should().Be(8);
        weather.Options.Select(o => (o.Name, o.Type, o.Required)).Should().Equal(
            ("city", Vc.Backend.WebApi.Interactions.Grpc.CommandOptionType.String, true),
            ("days", Vc.Backend.WebApi.Interactions.Grpc.CommandOptionType.Integer, false));

        var again = await interactions.RegisterCommandsAsync();
        (again.Created.Count + again.Updated.Count + again.Deleted.Count).Should().Be(0);
    }
}
