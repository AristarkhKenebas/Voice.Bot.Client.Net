using System.Diagnostics;
using FluentAssertions;
using Grpc.Core;
using Voice.Bot.Client.Net.Internal;
using Voice.Bot.Client.Net.Testing;
using Xunit;

namespace Voice.Bot.Client.Net.Tests;

public class RetryTests
{
    private const string Http429 = "Bad gRPC response. HTTP status code: 429";

    private static readonly RetryOptions Fast = new() { BaseDelay = TimeSpan.FromMilliseconds(1), MaxDelay = TimeSpan.FromMilliseconds(20) };

    private static FakeVoiceServer Server(RetryOptions? retry = null) => new(o => o.Retry = retry ?? Fast);

    [Fact]
    public async Task SafeCalls_ShouldBeRetriedOnUnavailable()
    {
        await using var server = Server();
        server.FailNext("GetMe", StatusCode.Unavailable, "connection reset", times: 2);

        (await server.Client.GetMeAsync()).Username.Should().Be("test-bot");
        server.Calls.Count(c => c == "GetMe").Should().Be(3);
    }

    [Fact]
    public async Task CreatingCalls_ShouldNotBeRetriedOnUnavailable_ButOnRateLimits()
    {
        await using var server = Server();
        server.FailNext("CreateMessage", StatusCode.Unavailable, "connection reset");

        var act = () => server.Client.SendMessageAsync(server.ChannelId, "once");
        (await act.Should().ThrowAsync<BotApiException>()).Which.StatusCode.Should().Be(StatusCode.Unavailable);
        server.Calls.Count(c => c == "CreateMessage").Should().Be(1);

        server.FailNext("CreateMessage", StatusCode.Unavailable, Http429, times: 2);
        (await server.Client.SendMessageAsync(server.ChannelId, "twice")).Content.Should().Be("twice");
        server.Calls.Count(c => c == "CreateMessage").Should().Be(4);
        server.SentMessages.Should().ContainSingle();
    }

    [Fact]
    public async Task Retries_ShouldStopAfterMaxRetries()
    {
        await using var server = Server();
        server.FailNext("GetMe", StatusCode.Unavailable, times: 10);

        var act = () => server.Client.GetMeAsync();

        await act.Should().ThrowAsync<BotApiException>();
        server.Calls.Count(c => c == "GetMe").Should().Be(4);
    }

    [Fact]
    public async Task OtherErrors_ShouldNotBeRetried()
    {
        await using var server = Server();
        server.FailNext("GetMe", StatusCode.PermissionDenied);

        var act = () => server.Client.GetMeAsync();

        await act.Should().ThrowAsync<BotApiException>();
        server.Calls.Count(c => c == "GetMe").Should().Be(1);
    }

    [Fact]
    public async Task WithRetries_ShouldOverrideTheOptionsForTheBlockOnly()
    {
        await using var server = Server();
        server.FailNext("GetMe", StatusCode.Unavailable, times: 2);

        using (server.Client.WithRetries(RetryOptions.None))
        {
            var act = () => server.Client.GetMeAsync();
            await act.Should().ThrowAsync<BotApiException>();
        }

        (await server.Client.GetMeAsync()).Should().NotBeNull();
        server.Calls.Count(c => c == "GetMe").Should().Be(3);
    }

    [Fact]
    public async Task Cancellation_ShouldEndTheWaitBetweenRetries()
    {
        await using var server = Server(new RetryOptions { BaseDelay = TimeSpan.FromSeconds(20), MaxDelay = TimeSpan.FromSeconds(20) });
        server.FailNext("GetMe", StatusCode.Unavailable);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var started = Stopwatch.StartNew();

        var act = () => server.Client.GetMeAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        started.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Delay_ShouldFollowHintsBackoffAndLimits()
    {
        var retry = new RetryOptions { Jitter = 0 };
        Retrier.Delay(new BotApiException(StatusCode.ResourceExhausted, "slow down", retryAfter: TimeSpan.FromSeconds(2)), Idempotency.Unsafe, 0, retry)
            .Should().Be(TimeSpan.FromSeconds(2));
        Retrier.Delay(new BotApiException(StatusCode.ResourceExhausted, "slow down", retryAfter: TimeSpan.FromMinutes(5)), Idempotency.Safe, 0, retry)
            .Should().BeNull();
        Retrier.Delay(new BotApiException(StatusCode.Unavailable, Http429), Idempotency.Unsafe, 2, retry).Should().Be(TimeSpan.FromSeconds(2));
        Retrier.Delay(new BotApiException(StatusCode.DeadlineExceeded, "timeout"), Idempotency.Safe, 1, retry).Should().Be(TimeSpan.FromSeconds(1));
        Retrier.Delay(new BotApiException(StatusCode.DeadlineExceeded, "timeout"), Idempotency.Unsafe, 1, retry).Should().BeNull();
        Retrier.Delay(new BotApiException(StatusCode.Unavailable, "down"), Idempotency.Safe, 3, retry).Should().BeNull();
        new RetryOptions { Jitter = 0 }.Backoff(10).Should().Be(TimeSpan.FromSeconds(30));
        new RetryOptions().Backoff(0).Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(500)).And.BeLessThanOrEqualTo(TimeSpan.FromMilliseconds(600));
    }

    [Fact]
    public async Task Throttle_ShouldSpaceOutRequestsOverTheLimit()
    {
        var bucket = new TokenBucket(5, TimeSpan.FromMilliseconds(500));
        var started = Stopwatch.StartNew();

        for (var i = 0; i < 7; i++) await bucket.AcquireAsync(CancellationToken.None);

        started.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(150));
    }

    [Fact]
    public async Task Throttle_ShouldApplyToApiCalls()
    {
        await using var server = new FakeVoiceServer(o => o.Throttle = new ThrottleOptions { Permits = 2, Per = TimeSpan.FromMilliseconds(400) });
        var started = Stopwatch.StartNew();

        for (var i = 0; i < 4; i++) await server.Client.GetMeAsync();

        started.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(300));
    }
}
