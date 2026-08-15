using System.Net;
using System.Net.Sockets;
using LocalWhale.Core.Updates;

namespace LocalWhale.Core.Tests;

public sealed class GitHubReleaseClientTests
{
    [Fact]
    public async Task GetLatestAsync_parses_tag_and_assets_from_the_github_shape()
    {
        var port = ReservePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        var server = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            Assert.Equal("/repos/penglai-doll/LocalWhale/releases/latest", context.Request.RawUrl);
            Assert.Contains(
                "LocalWhale/",
                context.Request.Headers["User-Agent"] ?? "",
                StringComparison.Ordinal);
            var payload = """
                {
                  "tag_name": "v0.1.3",
                  "assets": [
                    {
                      "name": "LocalWhale-Setup-x64.exe",
                      "browser_download_url": "https://example.invalid/LocalWhale-Setup-x64.exe",
                      "size": 414576640
                    },
                    {
                      "name": "SHA256SUMS.txt",
                      "browser_download_url": "https://example.invalid/SHA256SUMS.txt",
                      "size": 219
                    }
                  ]
                }
                """u8.ToArray();
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = payload.Length;
            await context.Response.OutputStream.WriteAsync(payload, TestContext.Current.CancellationToken);
            context.Response.Close();
        }, TestContext.Current.CancellationToken);
        using var httpClient = new HttpClient();
        var client = new GitHubReleaseClient(httpClient, new Uri($"http://127.0.0.1:{port}/"));

        var release = await client.GetLatestAsync(TestContext.Current.CancellationToken);

        await server;
        Assert.NotNull(release);
        Assert.Equal("v0.1.3", release.TagName);
        Assert.Equal(2, release.Assets.Count);
        var setup = release.Assets.Single(asset => asset.Name == "LocalWhale-Setup-x64.exe");
        Assert.Equal(new Uri("https://example.invalid/LocalWhale-Setup-x64.exe"), setup.BrowserDownloadUrl);
        Assert.Equal(414576640, setup.Size);
    }

    [Fact]
    public async Task GetLatestAsync_returns_null_when_no_release_exists()
    {
        var port = ReservePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        var server = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            context.Response.StatusCode = 404;
            context.Response.Close();
        }, TestContext.Current.CancellationToken);
        using var httpClient = new HttpClient();
        var client = new GitHubReleaseClient(httpClient, new Uri($"http://127.0.0.1:{port}/"));

        var release = await client.GetLatestAsync(TestContext.Current.CancellationToken);

        await server;
        Assert.Null(release);
    }

    [Fact]
    public async Task GetLatestAsync_throws_when_the_document_is_incomplete()
    {
        var port = ReservePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        var server = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            var payload = """{"assets": []}"""u8.ToArray();
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = payload.Length;
            await context.Response.OutputStream.WriteAsync(payload, TestContext.Current.CancellationToken);
            context.Response.Close();
        }, TestContext.Current.CancellationToken);
        using var httpClient = new HttpClient();
        var client = new GitHubReleaseClient(httpClient, new Uri($"http://127.0.0.1:{port}/"));

        await Assert.ThrowsAsync<InvalidDataException>(() => client.GetLatestAsync(TestContext.Current.CancellationToken));

        await server;
    }

    [Fact]
    public async Task GetLatestAsync_skips_assets_with_missing_download_urls()
    {
        var port = ReservePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        var server = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            var payload = """
                {
                  "tag_name": "v0.1.3",
                  "assets": [
                    { "name": "", "browser_download_url": "https://example.invalid/a", "size": 1 },
                    { "name": "SHA256SUMS.txt" }
                  ]
                }
                """u8.ToArray();
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = payload.Length;
            await context.Response.OutputStream.WriteAsync(payload, TestContext.Current.CancellationToken);
            context.Response.Close();
        }, TestContext.Current.CancellationToken);
        using var httpClient = new HttpClient();
        var client = new GitHubReleaseClient(httpClient, new Uri($"http://127.0.0.1:{port}/"));

        var release = await client.GetLatestAsync(TestContext.Current.CancellationToken);

        await server;
        Assert.NotNull(release);
        Assert.Empty(release.Assets);
    }

    private static int ReservePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
