using System.Net.Http;
using System.Reflection;

namespace Voice.Bot.Client.Net.Internal;

/// <summary>
/// Puts <c>voice-dotnet-sdk/&lt;version&gt;</c> first in the <c>User-Agent</c> of every request - gRPC calls,
/// the gateway and uploads. The server rejects requests without it with 403.
/// </summary>
internal sealed class UserAgentHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    public static readonly string Value = $"voice-dotnet-sdk/{Version()}";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Apply(request);
        return base.SendAsync(request, cancellationToken);
    }

    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Apply(request);
        return base.Send(request, cancellationToken);
    }

    private static void Apply(HttpRequestMessage request)
    {
        var others = request.Headers.TryGetValues("User-Agent", out var values)
            ? string.Join(' ', values).Replace(Value, "", StringComparison.Ordinal).Trim()
            : "";
        request.Headers.Remove("User-Agent");
        request.Headers.TryAddWithoutValidation("User-Agent", others.Length == 0 ? Value : $"{Value} {others}");
    }

    private static string Version()
    {
        var assembly = typeof(UserAgentHandler).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString(3)
            ?? "0.0.0";
        var metadata = version.IndexOf('+', StringComparison.Ordinal);
        return metadata < 0 ? version : version[..metadata];
    }
}
