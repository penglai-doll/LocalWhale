using System.Text.Json;

namespace LocalWhale.Core.Updates;

public sealed record NpmPackageVersion(string Version, string Integrity, Uri Tarball);

public interface INpmRegistryClient
{
    Task<NpmPackageVersion> GetLatestAsync(CancellationToken cancellationToken);
}

public sealed class NpmRegistryClient(
    HttpClient httpClient,
    Uri? registryBaseUri = null,
    string packageName = "@deepseek-ai/dsh") : INpmRegistryClient
{
    private readonly Uri _registryBaseUri = registryBaseUri ?? new Uri("https://registry.npmjs.org/");

    public async Task<NpmPackageVersion> GetLatestAsync(CancellationToken cancellationToken)
    {
        var escapedPackage = packageName.Replace("/", "%2F", StringComparison.Ordinal);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_registryBaseUri, $"{escapedPackage}/latest"));
        request.Headers.UserAgent.ParseAdd("LocalWhale/0.1");
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var version = root.GetProperty("version").GetString();
        var dist = root.GetProperty("dist");
        var integrity = dist.GetProperty("integrity").GetString();
        var tarball = dist.GetProperty("tarball").GetString();
        if (string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(integrity) || !Uri.TryCreate(tarball, UriKind.Absolute, out var tarballUri))
        {
            throw new InvalidDataException("npm registry returned an incomplete @deepseek-ai/dsh version document.");
        }

        return new NpmPackageVersion(version, integrity, tarballUri);
    }
}
