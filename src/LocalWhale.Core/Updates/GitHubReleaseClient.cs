using System.Net;
using System.Text.Json;

namespace LocalWhale.Core.Updates;

public sealed record GitHubReleaseAsset(string Name, Uri BrowserDownloadUrl, long Size);

public sealed record GitHubLatestRelease(string TagName, IReadOnlyList<GitHubReleaseAsset> Assets);

public interface IGitHubReleaseClient
{
    Task<GitHubLatestRelease?> GetLatestAsync(CancellationToken cancellationToken);
}

public sealed class GitHubReleaseClient(
    HttpClient httpClient,
    Uri? apiBaseUri = null,
    string repositorySlug = "penglai-doll/LocalWhale") : IGitHubReleaseClient
{
    private readonly Uri _apiBaseUri = apiBaseUri ?? new Uri("https://api.github.com/");

    public async Task<GitHubLatestRelease?> GetLatestAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_apiBaseUri, $"repos/{repositorySlug}/releases/latest"));
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.UserAgent.ParseAdd("LocalWhale/0.1");
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        // GitHub answers 404 while a repository has no published release yet.
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        if (!root.TryGetProperty("tag_name", out var tagElement) ||
            string.IsNullOrWhiteSpace(tagElement.GetString()) ||
            !root.TryGetProperty("assets", out var assetsElement) ||
            assetsElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("GitHub returned an incomplete latest-release document.");
        }

        var assets = new List<GitHubReleaseAsset>();
        foreach (var element in assetsElement.EnumerateArray())
        {
            if (!element.TryGetProperty("name", out var nameElement) ||
                string.IsNullOrWhiteSpace(nameElement.GetString()) ||
                !element.TryGetProperty("browser_download_url", out var urlElement) ||
                !Uri.TryCreate(urlElement.GetString(), UriKind.Absolute, out var downloadUrl))
            {
                continue;
            }

            var size = element.TryGetProperty("size", out var sizeElement) &&
                sizeElement.ValueKind == JsonValueKind.Number &&
                sizeElement.TryGetInt64(out var parsedSize)
                    ? parsedSize
                    : -1;
            assets.Add(new GitHubReleaseAsset(nameElement.GetString()!, downloadUrl, size));
        }

        return new GitHubLatestRelease(tagElement.GetString()!, assets);
    }
}
