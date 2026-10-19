using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Voice.Bot.Client.Net.Commands;
using Voice.Bot.Client.Net.Extensions;
using Xunit;

namespace Voice.Bot.Client.Net.Tests;

[CollectionDefinition(nameof(EnvironmentCollection), DisableParallelization = true)]
public sealed class EnvironmentCollection;

[Collection(nameof(EnvironmentCollection))]
public class ConfigurationTests
{
    [Fact]
    public void TokenAndPrefix_ShouldComeFromTheEnvironment()
    {
        Environment.SetEnvironmentVariable(BotClientOptions.TokenVariable, "ibot_from_env");
        Environment.SetEnvironmentVariable(BotClientOptions.PrefixVariable, "$");
        try
        {
            var options = BotClientOptions.FromEnvironment();
            (options.BotToken, options.TextCommandPrefix).Should().Be(("ibot_from_env", "$"));

            using var provider = new ServiceCollection().AddVoiceBotClient().BuildServiceProvider();
            provider.GetRequiredService<BotClientOptions>().BotToken.Should().Be("ibot_from_env");
            provider.GetRequiredService<CommandService>().Prefixes.Should().Equal("$");
            provider.GetRequiredService<CommandService>().Interactions.Should().NotBeNull();
        }
        finally
        {
            Environment.SetEnvironmentVariable(BotClientOptions.TokenVariable, null);
            Environment.SetEnvironmentVariable(BotClientOptions.PrefixVariable, null);
        }
    }

    [Fact]
    public void MissingToken_ShouldBeExplained()
    {
        Environment.SetEnvironmentVariable(BotClientOptions.TokenVariable, null);

        var act = () => BotClientOptions.FromEnvironment();

        act.Should().Throw<InvalidOperationException>().WithMessage("*VOICE_BOT_TOKEN*");
    }

    [Fact]
    public void Defaults_ShouldMatchTheSpec()
    {
        var options = new BotClientOptions { BotToken = "x" };

        options.TextCommandPrefixes.Should().Equal("!");
        (options.MentionPrefix, options.CaseInsensitiveCommands, options.IgnoreBots, options.SyncCommands, options.HelpCommand, options.EnableCache)
            .Should().Be((true, true, true, true, true, true));
        (options.Retry.MaxRetries, options.Retry.BaseDelay, options.Retry.MaxDelay).Should().Be((3, TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(30)));
        options.Throttle.Should().BeNull();
        options.TextCommandPrefix = "?";
        options.TextCommandPrefixes.Should().Equal("?");
    }

    [Fact]
    public async Task Modules_ShouldBeRegisteredForTheHostedService()
    {
        var services = new ServiceCollection().AddLogging().AddVoiceBotClient(o => o.BotToken = "ibot_x").AddBotModule<NoopModule>();
        await using var provider = services.BuildServiceProvider();

        provider.GetServices<IBotModule>().Should().ContainSingle().Which.Should().BeSameAs(provider.GetRequiredService<NoopModule>());
    }

    public sealed class NoopModule : IBotModule
    {
        public void Install(BotClient client)
        {
        }
    }
}

public class UserAgentTests
{
    [Fact]
    public async Task ApiCalls_ShouldIdentifyTheSdk()
    {
        await using var h = await TestHarness.StartAsync([new FakeUsers()]);
        await using var client = BotClient.Create(new BotClientOptions { BotToken = "ibot_test_token", Endpoint = h.Address });

        await client.GetMeAsync();

        h.Recorder.UserAgent.Should().MatchRegex(@"^voice-dotnet-sdk/\d+\.\d+\.\d+");
    }

    [Fact]
    public async Task Gateway_ShouldIdentifyTheSdk()
    {
        await using var server = await HubServer.StartAsync();
        await using var client = BotClient.Create(new BotClientOptions { BotToken = "good", Endpoint = server.Address });

        await client.StartGatewayAsync();
        await CommandServiceTests.WaitFor(() => server.Connections.Count == 1);

        server.UserAgents.Should().NotBeEmpty().And.OnlyContain(ua => ua.StartsWith("voice-dotnet-sdk/", StringComparison.Ordinal));
        await client.StopGatewayAsync();
    }
}
