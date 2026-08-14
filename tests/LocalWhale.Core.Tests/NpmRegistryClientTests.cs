using System.Net;
using System.Net.Sockets;
using LocalWhale.Core.Updates;

namespace LocalWhale.Core.Tests;

public sealed class NpmRegistryClientTests
{
    [Fact]
    public async Task GetLatestAsync_reads_version_and_integrity_from_the_official_registry_shape()
    {
        var port = ReservePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        var server = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            Assert.Equal("/@deepseek-ai%2Fdsh/latest", context.Request.RawUrl);
            var payload = """{"version":"0.2.0-rc.1","dist":{"integrity":"sha512-test","tarball":"https://registry.npmjs.org/example.tgz"}}"""u8.ToArray();
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = payload.Length;
            await context.Response.OutputStream.WriteAsync(payload, TestContext.Current.CancellationToken);
            context.Response.Close();
        }, TestContext.Current.CancellationToken);
        using var httpClient = new HttpClient();
        var registry = new NpmRegistryClient(httpClient, new Uri($"http://127.0.0.1:{port}/"));

        var package = await registry.GetLatestAsync(TestContext.Current.CancellationToken);

        await server;
        Assert.Equal("0.2.0-rc.1", package.Version);
        Assert.Equal("sha512-test", package.Integrity);
    }

    private static int ReservePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
