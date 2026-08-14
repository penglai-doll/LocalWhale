using System.Net;
using System.Net.Sockets;
using LocalWhale.Core.Runtime;

namespace LocalWhale.Core.Tests;

public sealed class BridgeClientTests
{
    [Fact]
    public async Task GetHealthAsync_sends_the_bridge_token_and_parses_the_protocol_payload()
    {
        var port = ReservePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        var server = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            Assert.Equal("bridge-secret", context.Request.Headers["X-LocalWhale-Token"]);
            var payload = """{"status":"ok","protocolVersion":1,"harnessVersion":"0.1.0-rc.6","pid":1234}"""u8.ToArray();
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = payload.Length;
            await context.Response.OutputStream.WriteAsync(payload, TestContext.Current.CancellationToken);
            context.Response.Close();
        }, TestContext.Current.CancellationToken);
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var client = new BridgeClient(httpClient, "bridge-secret");

        var health = await client.GetHealthAsync(new Uri($"http://127.0.0.1:{port}/"), TestContext.Current.CancellationToken);

        await server;
        Assert.Equal(1, health.ProtocolVersion);
        Assert.Equal("0.1.0-rc.6", health.HarnessVersion);
        Assert.Equal(1234, health.Pid);
    }

    [Fact]
    public async Task ShutdownAsync_posts_to_the_bridge_shutdown_route()
    {
        var port = ReservePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        var server = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            Assert.Equal("POST", context.Request.HttpMethod);
            Assert.Equal("/__localwhale/v1/shutdown", context.Request.Url?.AbsolutePath);
            context.Response.StatusCode = 202;
            context.Response.Close();
        }, TestContext.Current.CancellationToken);
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var client = new BridgeClient(httpClient, "bridge-secret");

        await client.ShutdownAsync(new Uri($"http://127.0.0.1:{port}/"), TestContext.Current.CancellationToken);

        await server;
    }

    private static int ReservePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
