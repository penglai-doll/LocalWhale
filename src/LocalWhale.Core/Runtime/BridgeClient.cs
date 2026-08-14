using System.Net.Http.Json;
using System.Text.Json.Serialization;
using LocalWhale.Core.Persistence;

namespace LocalWhale.Core.Runtime;

public sealed record BridgeHealth(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("protocolVersion")] int ProtocolVersion,
    [property: JsonPropertyName("harnessVersion")] string HarnessVersion,
    [property: JsonPropertyName("pid")] int Pid);

public sealed class BridgeClient(HttpClient httpClient, string token)
{
    public const int SupportedProtocolVersion = 1;

    public async Task<BridgeHealth> GetHealthAsync(Uri baseUri, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, new Uri(baseUri, "/__localwhale/v1/health"));
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var health = await response.Content.ReadFromJsonAsync(LocalWhaleJsonContext.Default.BridgeHealth, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Bridge health response was empty.");
        if (!string.Equals(health.Status, "ok", StringComparison.Ordinal) || health.ProtocolVersion != SupportedProtocolVersion)
        {
            throw new InvalidDataException($"Unsupported bridge response: status={health.Status}, protocol={health.ProtocolVersion}.");
        }

        return health;
    }

    public async Task ShutdownAsync(Uri baseUri, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, new Uri(baseUri, "/__localwhale/v1/shutdown"));
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, Uri uri)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.TryAddWithoutValidation("X-LocalWhale-Token", token);
        return request;
    }
}
