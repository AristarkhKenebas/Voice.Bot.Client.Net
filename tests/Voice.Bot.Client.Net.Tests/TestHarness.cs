using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vc.Backend.WebApi.Attachments.Grpc;
using Vc.Backend.WebApi.Channels.Grpc;
using Vc.Backend.WebApi.Groups.Grpc;
using Vc.Backend.WebApi.Interactions.Grpc;
using Vc.Backend.WebApi.Messages.Grpc;
using Vc.Backend.WebApi.Users.Grpc;
using Voice.Bot.Client.Net.Gateway;
using Voice.Bot.Client.Net.Internal;

namespace Voice.Bot.Client.Net.Tests;

/// <summary>Remembers the last request each fake RPC received, plus the authorization header.</summary>
public sealed class Recorder
{
    private readonly ConcurrentDictionary<string, object> _requests = new();

    public string? Authorization { get; private set; }

    public string? UserAgent { get; private set; }

    public void Record(string name, object request, ServerCallContext context)
    {
        _requests[name] = request;
        Authorization = context.RequestHeaders.GetValue("authorization");
        UserAgent = context.RequestHeaders.GetValue("user-agent");
    }

    public T? Last<T>(string name) where T : class => _requests.TryGetValue(name, out var value) ? (T)value : null;
}

/// <summary>Stands in for the storage the pre-signed upload URLs point to.</summary>
public sealed class FakeStorage : HttpMessageHandler
{
    public readonly List<(string Url, string ContentType, byte[] Body)> Uploads = [];

    /// <summary>Returns the response for the n-th upload (1-based); <c>null</c> means 200 with an ETag.</summary>
    public Func<int, HttpResponseMessage?>? Respond { get; set; }

    /// <summary>Delay before answering, to exercise timeouts and cancellation.</summary>
    public TimeSpan Delay { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(ct);
        int count;
        lock (Uploads)
        {
            Uploads.Add((request.RequestUri!.ToString(), request.Content?.Headers.ContentType?.ToString() ?? "", body));
            count = Uploads.Count;
        }

        if (Delay > TimeSpan.Zero) await Task.Delay(Delay, ct);
        if (Respond?.Invoke(count) is { } custom) return custom;

        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue($"\"etag-{count}\"");
        return response;
    }
}

/// <summary>A gateway connection the test drives by hand.</summary>
public sealed class FakeGateway : IBotGatewayConnection
{
    public event Action<string, string>? MessageReceived;
    public event Action<Exception>? Error;
    public event Action? Connected;

    public void Connect() => Connected?.Invoke();

    public void Deliver(string topic, string json) => MessageReceived?.Invoke(topic, json);

    public void Fail(Exception error) => Error?.Invoke(error);

    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;

    public Task StopAsync() => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>A <see cref="BotClient"/> wired to an in-process gRPC server with the given fake services.</summary>
public sealed class TestHarness : IAsyncDisposable
{
    private readonly IHost _host;
    private readonly GrpcChannel _channel;

    public BotClient Client { get; }
    public Recorder Recorder { get; }
    public string Address { get; }
    public FakeStorage Storage { get; } = new();
    public FakeGateway Gateway { get; } = new();

    private TestHarness(IHost host, Recorder recorder, TimeSpan? timeout, TimeSpan? uploadTimeout)
    {
        _host = host;
        Recorder = recorder;
        var address = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        Address = address;
        _channel = GrpcChannel.ForAddress(address);
        var api = new VoiceApi(_channel.CreateCallInvoker(), "ibot_test_token", timeout);
        Client = new BotClient(api, Gateway, new Uploader(new HttpClient(Storage), uploadTimeout));
    }

    public static async Task<TestHarness> StartAsync(
        object[] services, TimeSpan? timeout = null, TimeSpan? uploadTimeout = null)
    {
        var recorder = new Recorder();
        var host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseKestrel(k => k.Listen(IPAddress.Loopback, 0, l => l.Protocols = HttpProtocols.Http2))
                .ConfigureServices(s =>
                {
                    s.AddGrpc(o => o.EnableDetailedErrors = true);
                    s.AddSingleton(recorder);
                    foreach (var service in services) s.AddSingleton(service.GetType(), service);
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        foreach (var service in services) MapService(endpoints, service);
                    });
                }))
            .StartAsync();
        foreach (var service in services)
        {
            if (service is FakeService fake) fake.Recorder = recorder;
        }

        return new TestHarness(host, recorder, timeout ?? TimeSpan.FromSeconds(30), uploadTimeout ?? TimeSpan.FromMinutes(5));
    }

    private static void MapService(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints, object service)
    {
        var method = typeof(GrpcEndpointRouteBuilderExtensions).GetMethods()
            .Single(m => m.Name == nameof(GrpcEndpointRouteBuilderExtensions.MapGrpcService) && m.GetParameters().Length == 1);
        method.MakeGenericMethod(service.GetType()).Invoke(null, [endpoints]);
    }

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        _channel.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }
}

/// <summary>Marker for fakes that need the shared <see cref="Recorder"/>.</summary>
public interface FakeService
{
    Recorder Recorder { get; set; }
}

internal static class Ids
{
    public static string New() => Guid.NewGuid().ToString();
}
