using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Http.Connections.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vc.Backend.WebApi.Attachments.Grpc;
using Vc.Backend.WebApi.Groups.Grpc;
using Vc.Backend.WebApi.Messages.Grpc;
using Voice.Bot.Client.Net.Commands;
using Voice.Bot.Client.Net.Events;
using Voice.Bot.Client.Net.Extensions;
using Voice.Bot.Client.Net.Gateway;
using Voice.Bot.Client.Net.Interactions;
using Voice.Bot.Client.Net.Internal;
using Xunit;

namespace Voice.Bot.Client.Net.Tests;

public class ErrorMappingTests
{
    [Theory]
    [InlineData(StatusCode.ResourceExhausted, "too many requests", true)]
    [InlineData(StatusCode.ResourceExhausted, "grpc: received message larger than max (5 vs. 4)", false)]
    [InlineData(StatusCode.ResourceExhausted, "Received message exceeds the maximum configured message size.", false)]
    [InlineData(StatusCode.ResourceExhausted, "gRPC message exceeds maximum size 67108864: 70000000", false)]
    [InlineData(StatusCode.Unavailable, "Bad gRPC response. HTTP status code: 429", true)]
    [InlineData(StatusCode.Unavailable, "unexpected HTTP status code received from server: 429 (Too Many Requests)", true)]
    [InlineData(StatusCode.Unavailable, "Received http2 header with status: 429", true)]
    [InlineData(StatusCode.Unavailable, "status: 4290", false)]
    [InlineData(StatusCode.Unavailable, "connect to 10.0.0.1:4290 failed", false)]
    public void IsRateLimited_ShouldOnlyMatchRealRateLimits(StatusCode code, string message, bool expected) =>
        new BotApiException(code, message).IsRateLimited.Should().Be(expected);

    [Fact]
    public async Task BadIdsFromTheServer_ShouldBeBotApiExceptions()
    {
        await using var h = await TestHarness.StartAsync([new OddMessages(), new OddGroups()]);

        var sent = await h.Client.SendMessageAsync(Guid.Parse(Known.ChannelId), "dm");
        sent.MentionedChannels.Single().GroupId.Should().BeNull();
        sent.Attachments.Single().UploaderId.Should().BeNull();

        var bad = () => h.Client.SendMessageAsync(Guid.Parse(Known.ChannelId), "broken");
        (await bad.Should().ThrowAsync<BotApiException>()).Which.StatusCode.Should().Be(StatusCode.Internal);

        var noGroup = () => h.Client.GetGroupAsync(Guid.NewGuid());
        (await noGroup.Should().ThrowAsync<BotApiException>()).Which.StatusCode.Should().Be(StatusCode.Internal);
    }

    [Fact]
    public async Task RefusedJoin_ShouldThrow()
    {
        await using var h = await TestHarness.StartAsync([new FakeGroups()]);
        var act = () => h.Client.JoinByInviteAsync("refused");
        (await act.Should().ThrowAsync<BotApiException>()).Which.StatusCode.Should().Be(StatusCode.FailedPrecondition);
    }

    [Fact]
    public async Task Cancellation_ShouldThrowOperationCanceled()
    {
        var users = new FakeUsers { Delay = TimeSpan.FromSeconds(5) };
        await using var h = await TestHarness.StartAsync([users, new FakeAttachments()]);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var slow = () => h.Client.GetMeAsync(cts.Token);
        await slow.Should().ThrowAsync<OperationCanceledException>();

        var cancelled = () => h.Client.GetMeAsync(new CancellationToken(true));
        await cancelled.Should().ThrowAsync<OperationCanceledException>();

        h.Storage.Delay = TimeSpan.FromSeconds(5);
        using var uploadCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var upload = () => h.Client.UploadAsync("a", "x"u8.ToArray(), "x/y", uploadCts.Token);
        await upload.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(long.MaxValue)]
    public async Task InfiniteTimeouts_ShouldMeanNoLimit(long ticks)
    {
        var timeout = ticks < 0 ? Timeout.InfiniteTimeSpan : TimeSpan.FromTicks(ticks);
        await using var h = await TestHarness.StartAsync([new FakeUsers(), new FakeAttachments()], timeout, timeout);

        (await h.Client.GetMeAsync()).Id.Should().Be(Guid.Parse(Known.Bot));
        (await h.Client.UploadAsync("a", "x"u8.ToArray(), "x/y")).Status.Should().Be(Models.AttachmentStatus.Uploaded);
    }

    [Fact]
    public void Uploads_ShouldNotUseHttpClientsOwnTimeout()
    {
        var http = new HttpClient();
        using var uploader = new Uploader(http, TimeSpan.FromMinutes(5));
        http.Timeout.Should().Be(Timeout.InfiniteTimeSpan);
    }

    [Fact]
    public async Task BadContentType_ShouldFailBeforeAnythingIsCreated()
    {
        await using var h = await TestHarness.StartAsync([new FakeAttachments(), new FakeUsers()]);

        var upload = () => h.Client.UploadAsync("a", "x"u8.ToArray(), "not a mime");
        await upload.Should().ThrowAsync<ArgumentException>();
        var avatar = () => h.Client.SetAvatarAsync("x"u8.ToArray(), "not a mime");
        await avatar.Should().ThrowAsync<ArgumentException>();

        h.Recorder.Last<InitiateUploadRequest>("start").Should().BeNull();
        h.Storage.Uploads.Should().BeEmpty();
    }

    [Fact]
    public async Task UnusableUploadUrl_ShouldBeBotApiException()
    {
        await using var h = await TestHarness.StartAsync([new FakeAttachments { UploadUrl = "not a url" }]);
        var act = () => h.Client.UploadAsync("a", "x"u8.ToArray(), "x/y");
        (await act.Should().ThrowAsync<BotApiException>()).Which.StatusCode.Should().Be(StatusCode.Internal);
    }

    private sealed class OddMessages : MessagesApi.MessagesApiBase
    {
        public override Task<Message> CreateMessage(CreateMessageRequest request, ServerCallContext context)
        {
            var message = FakeMessages.Echo(Known.MessageId, request.ChannelId, request.Content);
            if (request.Content == "broken") message.Id = "not-a-guid";
            message.MentionChannels.Add(new MentionedChannel { Id = Ids.New(), Name = "dm", GroupId = "" });
            message.Attachments.Add(new Attachment { Id = Ids.New(), FileName = "a.txt" });
            return Task.FromResult(message);
        }
    }

    private sealed class OddGroups : GroupsApi.GroupsApiBase
    {
        public override Task<GetGroupResponse> GetGroup(GetGroupRequest request, ServerCallContext context) =>
            Task.FromResult(new GetGroupResponse());
    }
}

public class DispatchOrderTests
{
    [Fact]
    public async Task Events_ShouldBeHandledOneAtATimeInOrder()
    {
        await using var h = await TestHarness.StartAsync([]);
        var order = new ConcurrentQueue<string>();
        var done = new TaskCompletionSource();
        h.Client.MessageCreated += async _ =>
        {
            await Task.Delay(200);
            order.Enqueue("created");
        };
        h.Client.MessageDeleted += _ =>
        {
            order.Enqueue("deleted");
            done.TrySetResult();
            return Task.CompletedTask;
        };

        h.Gateway.Deliver(GatewayTopics.ChatEvents, Envelopes.Make("MessageCreated", new { Id = Known.MessageId }));
        h.Gateway.Deliver(GatewayTopics.ChatEvents, Envelopes.Make("MessageDeleted", new { Id = Known.MessageId }));

        await done.Task.WaitAsync(TimeSpan.FromSeconds(3));
        order.Should().Equal("created", "deleted");
    }

    [Fact]
    public async Task DisposingFromAHandler_ShouldNotDeadlock()
    {
        var h = await TestHarness.StartAsync([]);
        var disposed = new TaskCompletionSource();
        h.Client.MessageCreated += async _ =>
        {
            await h.Client.DisposeAsync();
            disposed.TrySetResult();
        };

        h.Gateway.Deliver(GatewayTopics.ChatEvents, Envelopes.Make("MessageCreated", new { }));

        await disposed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await h.DisposeAsync();
    }
}

public sealed class ScopedCounter
{
    private static int _next;
    public int Id { get; } = Interlocked.Increment(ref _next);
}

public sealed class ScopedModule(ScopedCounter counter) : ModuleBase, IDisposable
{
    public static readonly ConcurrentQueue<int> Seen = new();
    public static int Disposed;

    [Command("scoped")]
    public Task Run()
    {
        Seen.Enqueue(counter.Id);
        return Task.CompletedTask;
    }

    public void Dispose() => Interlocked.Increment(ref Disposed);
}

public class DependencyInjectionTests
{
    private static string Text(string content) =>
        Envelopes.Make("MessageCreated", new { Id = Known.MessageId, ChannelId = Known.ChannelId, Content = content, Author = new { Id = Known.UserId } });

    [Fact]
    public async Task EveryCommand_ShouldGetItsOwnScopeAndDisposeTheModule()
    {
        await using var h = await TestHarness.StartAsync([]);
        await using var services = new ServiceCollection().AddScoped<ScopedCounter>().BuildServiceProvider(validateScopes: true);
        var commands = new CommandService(h.Client, "!", services);
        commands.AddModule<ScopedModule>();
        commands.Initialize();
        var before = ScopedModule.Disposed;

        h.Gateway.Deliver(GatewayTopics.ChatEvents, Text("!scoped"));
        h.Gateway.Deliver(GatewayTopics.ChatEvents, Text("!scoped"));

        await CommandServiceTests.WaitFor(() => ScopedModule.Disposed - before == 2);
        ScopedModule.Seen.Distinct().Count().Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task FailingModuleFactory_ShouldRaiseCommandExecutionFailed()
    {
        await using var h = await TestHarness.StartAsync([]);
        var commands = new CommandService(h.Client, "!", _ => throw new InvalidOperationException("no module"));
        commands.AddModule<TestCommandModule>();
        var failed = new TaskCompletionSource<Exception>();
        commands.CommandExecutionFailed += (ex, _) => { failed.TrySetResult(ex); return Task.CompletedTask; };
        commands.Initialize();

        h.Gateway.Deliver(GatewayTopics.ChatEvents, Text("!ping"));

        (await failed.Task.WaitAsync(TimeSpan.FromSeconds(3))).Message.Should().Be("no module");
    }

    [Fact]
    public async Task RegisterCommands_ShouldWorkBeforeTheHostStarts()
    {
        var fake = new FakeInteractions();
        await using var h = await TestHarness.StartAsync([new FakeUsers(), fake]);
        var services = new ServiceCollection();
        services.AddVoiceBotClient(o =>
        {
            o.BotToken = "ibot_test_token";
            o.Endpoint = h.Address;
        });
        services.AddInteractionModules(typeof(TestSlashModule).Assembly);
        await using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<InteractionService>().RegisterCommandsAsync();

        result.Created.Select(c => c.Name).Should().Contain(["weather", "boom"]);
    }

    [Fact]
    public async Task RegisterCommands_WithNothingRegistered_ShouldRefuseToDeleteEverything()
    {
        await using var h = await TestHarness.StartAsync([new FakeUsers(), new FakeInteractions()]);
        var interactions = new InteractionService(h.Client);

        var act = () => interactions.RegisterCommandsAsync();
        await act.Should().ThrowAsync<InvalidOperationException>();
        (await interactions.RegisterCommandsAsync(deleteMissing: false)).Created.Should().BeEmpty();
    }

    [Fact]
    public void SyncDispose_ShouldWork()
    {
        var services = new ServiceCollection();
        services.AddVoiceBotClient(o => o.BotToken = "ibot_test_token");
        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<BotClient>();

        provider.Invoking(p => p.Dispose()).Should().NotThrow();
    }
}

/// <summary>A real SignalR hub at <c>/hubs/bots</c> that only lets the token <c>good</c> in (and answers <c>forbidden</c> with 403).</summary>
public sealed class HubServer : IAsyncDisposable
{
    private readonly IHost _host;

    public string Address { get; }
    public ConcurrentQueue<HubCallerContext> Connections { get; } = new();
    public ConcurrentQueue<string> UserAgents { get; } = new();
    public ConcurrentQueue<HttpTransportType> Transports { get; } = new();
    public IHubContext<BotsHub> Hub => _host.Services.GetRequiredService<IHubContext<BotsHub>>();

    private HubServer(IHost host)
    {
        _host = host;
        Address = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    }

    /// <param name="certificate">Serves HTTPS with it; <c>null</c> for plain HTTP.</param>
    public static async Task<HubServer> StartAsync(X509Certificate2? certificate = null)
    {
        HubServer? server = null;
        var host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseKestrel(k => k.Listen(IPAddress.Loopback, 0, l =>
                {
                    l.Protocols = HttpProtocols.Http1;
                    if (certificate is not null) l.UseHttps(certificate);
                }))
                .ConfigureServices(s =>
                {
                    s.AddSignalR();
                    s.AddSingleton<Func<HubServer>>(() => server!);
                })
                .Configure(app =>
                {
                    app.Use(async (context, next) =>
                    {
                        server!.UserAgents.Enqueue(context.Request.Headers.UserAgent.ToString());
                        var token = context.Request.Query["access_token"].ToString();
                        if (token == "" && context.Request.Headers.Authorization.ToString() is { } header && header.StartsWith("Bearer "))
                        {
                            token = header["Bearer ".Length..];
                        }

                        if (token != "good")
                        {
                            context.Response.StatusCode = token == "forbidden" ? StatusCodes.Status403Forbidden : StatusCodes.Status401Unauthorized;
                            return;
                        }

                        await next();
                    });
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapHub<BotsHub>("/hubs/bots"));
                }))
            .StartAsync();
        server = new HubServer(host);
        return server;
    }

    public Task SendAsync(string topic, string json) =>
        Hub.Clients.All.SendAsync(topic, JsonDocument.Parse(json).RootElement);

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }
}

public sealed class BotsHub(Func<HubServer> server) : Hub
{
    public override Task OnConnectedAsync()
    {
        server().Connections.Enqueue(Context);
        if (Context.Features.Get<IHttpTransportFeature>() is { } transport) server().Transports.Enqueue(transport.TransportType);
        return Task.CompletedTask;
    }
}

public class SignalRGatewayTests
{
    [Fact]
    public async Task Gateway_ShouldDeliverEventsAndReconnectAfterADrop()
    {
        await using var server = await HubServer.StartAsync();
        await using var client = BotClient.Create(new BotClientOptions { BotToken = "good", Endpoint = server.Address });
        var received = new ConcurrentQueue<string>();
        client.MessageCreated += e => { received.Enqueue(e.Message.Content); return Task.CompletedTask; };

        await client.StartGatewayAsync();
        await CommandServiceTests.WaitFor(() => server.Connections.Count == 1);
        await server.SendAsync(GatewayTopics.ChatEvents, Envelopes.Make("MessageCreated", new { Content = "one" }));
        await CommandServiceTests.WaitFor(() => received.Count == 1);

        server.Connections.Single().Abort();
        await CommandServiceTests.WaitFor(() => server.Connections.Count == 2);
        await server.SendAsync(GatewayTopics.ChatEvents, Envelopes.Make("MessageCreated", new { Content = "two" }));
        await CommandServiceTests.WaitFor(() => received.Count == 2);

        received.Should().Equal("one", "two");
        await client.StopGatewayAsync();
    }

    [Fact]
    public async Task WrongToken_ShouldFailWithUnauthenticated()
    {
        await using var server = await HubServer.StartAsync();
        await using var client = BotClient.Create(new BotClientOptions { BotToken = "bad", Endpoint = server.Address });

        var act = () => client.StartGatewayAsync();
        (await act.Should().ThrowAsync<BotApiException>()).Which.StatusCode.Should().Be(StatusCode.Unauthenticated);
    }

    [Fact]
    public async Task UnreachableServer_ShouldBeRetriedUntilCancelled()
    {
        await using var client = BotClient.Create(new BotClientOptions { BotToken = "good", Endpoint = "http://127.0.0.1:1" });
        var errors = 0;
        client.ConnectionError += _ => { Interlocked.Increment(ref errors); return Task.CompletedTask; };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));

        var act = () => client.StartGatewayAsync(cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
        errors.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task UntrustedCertificates_WhenAllowed_ShouldWorkForTheWebSocketToo()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        using var certificate = X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pfx), null);
        await using var server = await HubServer.StartAsync(certificate);
        await using var client = BotClient.Create(new BotClientOptions { BotToken = "good", Endpoint = server.Address, AllowUntrustedCertificates = true });

        await client.StartGatewayAsync();
        await CommandServiceTests.WaitFor(() => server.Connections.Count == 1);

        server.Transports.Should().Equal(HttpTransportType.WebSockets);
        await client.StopGatewayAsync();
    }

    [Fact]
    public void RetryPolicy_ShouldRetryForeverWithCappedPauses()
    {
        var policy = new GatewayRetryPolicy();
        for (var count = 0; count < 100; count++)
        {
            var delay = policy.NextRetryDelay(new RetryContext { PreviousRetryCount = count });
            delay.Should().NotBeNull();
            delay!.Value.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(36));
        }

        policy.NextRetryDelay(new RetryContext { PreviousRetryCount = 0 }).Should().Be(TimeSpan.Zero);
    }
}
