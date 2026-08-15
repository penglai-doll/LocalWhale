using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using LocalWhale.Core.Models;
using LocalWhale.Core.Persistence;
using LocalWhale.Core.Updates;

namespace LocalWhale.Core.Tests;

public sealed class ShellUpdateServiceTests : IDisposable
{
    private readonly TemporaryTestDirectory _directory = new();
    private string SettingsPath => Path.Combine(_directory.Path, "settings.json");
    private string UpdatesDirectory => Path.Combine(_directory.Path, "updates");

    [Fact]
    public async Task CheckAsync_skips_recent_non_forced_checks_without_calling_github()
    {
        var store = new AppSettingsStore(SettingsPath);
        await store.SaveAsync(
            AppSettings.Default with { LastShellUpdateCheckUtc = DateTimeOffset.UtcNow - TimeSpan.FromHours(1) },
            TestContext.Current.CancellationToken);
        var client = new FakeReleaseClient
        {
            Next = Release("v0.1.3", SetupAsset(), SumsAsset()),
        };
        var service = CreateService(client, store, "0.1.2");

        var update = await service.CheckAsync(force: false, TestContext.Current.CancellationToken);

        Assert.Null(update);
        Assert.Equal(0, client.Calls);
    }

    [Fact]
    public async Task CheckAsync_skips_when_shell_update_checks_are_disabled()
    {
        var store = new AppSettingsStore(SettingsPath);
        await store.SaveAsync(
            AppSettings.Default with { ShellUpdateCheckEnabled = false },
            TestContext.Current.CancellationToken);
        var client = new FakeReleaseClient
        {
            Next = Release("v0.1.3", SetupAsset(), SumsAsset()),
        };
        var service = CreateService(client, store, "0.1.2");

        var update = await service.CheckAsync(force: false, TestContext.Current.CancellationToken);

        Assert.Null(update);
        Assert.Equal(0, client.Calls);
    }

    [Fact]
    public async Task CheckAsync_force_bypasses_both_the_toggle_and_the_throttle()
    {
        var store = new AppSettingsStore(SettingsPath);
        await store.SaveAsync(
            AppSettings.Default with
            {
                ShellUpdateCheckEnabled = false,
                LastShellUpdateCheckUtc = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(1)
            },
            TestContext.Current.CancellationToken);
        var client = new FakeReleaseClient
        {
            Next = Release("v0.1.3", SetupAsset(), SumsAsset()),
        };
        var service = CreateService(client, store, "0.1.2");

        var update = await service.CheckAsync(force: true, TestContext.Current.CancellationToken);

        Assert.NotNull(update);
        Assert.Equal(1, client.Calls);
        Assert.Equal("0.1.3", update.AvailableVersion);
    }

    [Fact]
    public async Task CheckAsync_reports_newer_release_and_stamps_the_check_time()
    {
        var store = new AppSettingsStore(SettingsPath);
        var client = new FakeReleaseClient
        {
            Next = Release("v0.1.3", SetupAsset(), SumsAsset()),
        };
        var service = CreateService(client, store, "0.1.2");

        var update = await service.CheckAsync(force: true, TestContext.Current.CancellationToken);

        Assert.NotNull(update);
        Assert.Equal("0.1.2", update.CurrentVersion);
        Assert.Equal("0.1.3", update.AvailableVersion);
        Assert.Equal(new Uri("https://example.invalid/LocalWhale-Setup-x64.exe"), update.SetupDownloadUrl);
        Assert.Equal(new Uri("https://example.invalid/SHA256SUMS.txt"), update.Sha256SumsDownloadUrl);
        Assert.Equal(414576640, update.SetupSizeBytes);
        var settings = await store.LoadAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(settings.LastShellUpdateCheckUtc);
    }

    [Fact]
    public async Task CheckAsync_returns_null_when_the_latest_release_is_not_newer()
    {
        var client = new FakeReleaseClient
        {
            Next = Release("v0.1.2", SetupAsset(), SumsAsset()),
        };
        var service = CreateService(client, new AppSettingsStore(SettingsPath), "0.1.2");

        Assert.Null(await service.CheckAsync(force: true, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CheckAsync_returns_null_for_ignored_versions()
    {
        var store = new AppSettingsStore(SettingsPath);
        await store.SaveAsync(
            AppSettings.Default with { IgnoredShellVersions = ["0.1.3"] },
            TestContext.Current.CancellationToken);
        var client = new FakeReleaseClient
        {
            Next = Release("v0.1.3", SetupAsset(), SumsAsset()),
        };
        var service = CreateService(client, store, "0.1.2");

        var update = await service.CheckAsync(force: true, TestContext.Current.CancellationToken);

        Assert.Null(update);
        Assert.Equal(1, client.Calls);
    }

    [Fact]
    public async Task CheckAsync_returns_null_when_release_assets_are_missing()
    {
        var client = new FakeReleaseClient
        {
            Next = Release("v0.1.3", SumsAsset()),
        };
        var service = CreateService(client, new AppSettingsStore(SettingsPath), "0.1.2");

        Assert.Null(await service.CheckAsync(force: true, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CheckAsync_returns_null_when_the_tag_is_not_a_semver()
    {
        var client = new FakeReleaseClient
        {
            Next = Release("latest", SetupAsset(), SumsAsset()),
        };
        var service = CreateService(client, new AppSettingsStore(SettingsPath), "0.1.2");

        Assert.Null(await service.CheckAsync(force: true, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CheckAsync_returns_null_when_github_has_no_release()
    {
        var client = new FakeReleaseClient();
        var service = CreateService(client, new AppSettingsStore(SettingsPath), "0.1.2");

        Assert.Null(await service.CheckAsync(force: true, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DownloadAndStageAsync_verifies_the_hash_and_writes_the_staged_marker()
    {
        byte[] setupPayload = "localwhale-staged-setup-payload"u8.ToArray();
        var expectedSha = Convert.ToHexString(SHA256.HashData(setupPayload)).ToLowerInvariant();
        var (port, listener, server) = StartDownloadServer("/SHA256SUMS.txt", $"{expectedSha}  LocalWhale-Setup-x64.exe\n", "/LocalWhale-Setup-x64.exe", setupPayload);
        using var httpClient = new HttpClient();
        var service = CreateService(new FakeReleaseClient(), new AppSettingsStore(SettingsPath), "0.1.2", httpClient);
        var update = new ShellUpdate(
            "0.1.2",
            "0.1.3",
            new Uri($"http://127.0.0.1:{port}/LocalWhale-Setup-x64.exe"),
            new Uri($"http://127.0.0.1:{port}/SHA256SUMS.txt"),
            setupPayload.Length);
        var progress = new RecordingProgress();

        var staged = await service.DownloadAndStageAsync(update, progress, TestContext.Current.CancellationToken);

        await server;
        Assert.Equal("0.1.3", staged.Version);
        Assert.Equal(expectedSha, staged.SetupSha256);
        Assert.Equal(setupPayload, await File.ReadAllBytesAsync(staged.SetupPath, TestContext.Current.CancellationToken));
        Assert.EndsWith(ShellUpdateService.SetupAssetFileName, staged.SetupPath, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(UpdatesDirectory, "*.partial"));
        var marker = await StagedShellUpdateStore.LoadAsync(UpdatesDirectory, TestContext.Current.CancellationToken);
        Assert.NotNull(marker);
        Assert.Equal("0.1.3", marker.Version);
        Assert.Equal(expectedSha, marker.SetupSha256);
        Assert.NotEmpty(progress.Values);
        Assert.Equal(100, progress.Values[^1]);
        listener.Close();
    }

    [Fact]
    public async Task DownloadAndStageAsync_rejects_a_hash_mismatch_and_cleans_up()
    {
        byte[] setupPayload = "localwhale-staged-setup-payload"u8.ToArray();
        var wrongSha = new string('a', 64);
        var (port, listener, server) = StartDownloadServer("/SHA256SUMS.txt", $"{wrongSha}  LocalWhale-Setup-x64.exe\n", "/LocalWhale-Setup-x64.exe", setupPayload);
        using var httpClient = new HttpClient();
        var service = CreateService(new FakeReleaseClient(), new AppSettingsStore(SettingsPath), "0.1.2", httpClient);
        var update = new ShellUpdate(
            "0.1.2",
            "0.1.3",
            new Uri($"http://127.0.0.1:{port}/LocalWhale-Setup-x64.exe"),
            new Uri($"http://127.0.0.1:{port}/SHA256SUMS.txt"),
            setupPayload.Length);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => service.DownloadAndStageAsync(update, null, TestContext.Current.CancellationToken));

        await server;
        Assert.False(File.Exists(Path.Combine(UpdatesDirectory, ShellUpdateService.SetupAssetFileName)));
        Assert.Empty(Directory.GetFiles(UpdatesDirectory, "*.partial"));
        Assert.Null(await StagedShellUpdateStore.LoadAsync(UpdatesDirectory, TestContext.Current.CancellationToken));
        listener.Close();
    }

    [Fact]
    public async Task DownloadAndStageAsync_throws_when_the_sums_file_has_no_setup_entry()
    {
        var (port, listener, server) = StartDownloadServer("/SHA256SUMS.txt", "0000000000000000000000000000000000000000000000000000000000000000  other-file.zip\n", "/LocalWhale-Setup-x64.exe", "payload"u8.ToArray());
        using var httpClient = new HttpClient();
        var service = CreateService(new FakeReleaseClient(), new AppSettingsStore(SettingsPath), "0.1.2", httpClient);
        var update = new ShellUpdate(
            "0.1.2",
            "0.1.3",
            new Uri($"http://127.0.0.1:{port}/LocalWhale-Setup-x64.exe"),
            new Uri($"http://127.0.0.1:{port}/SHA256SUMS.txt"),
            7);

        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(
                () => service.DownloadAndStageAsync(update, null, TestContext.Current.CancellationToken));
        }
        finally
        {
            // The service aborts before requesting the setup payload; unblock the server.
            listener.Stop();
        }

        await server;
    }

    [Fact]
    public async Task ClearStagedUpdate_removes_the_marker_and_setup_file()
    {
        Directory.CreateDirectory(UpdatesDirectory);
        var setupPath = Path.Combine(UpdatesDirectory, ShellUpdateService.SetupAssetFileName);
        await File.WriteAllTextAsync(setupPath, "staged", TestContext.Current.CancellationToken);
        await StagedShellUpdateStore.SaveAsync(
            new StagedShellUpdate("0.1.3", setupPath, new string('b', 64), DateTimeOffset.UtcNow),
            UpdatesDirectory,
            TestContext.Current.CancellationToken);
        var service = CreateService(new FakeReleaseClient(), new AppSettingsStore(SettingsPath), "0.1.2");
        Assert.NotNull(await service.GetStagedUpdateAsync(TestContext.Current.CancellationToken));

        service.ClearStagedUpdate();

        Assert.False(File.Exists(setupPath));
        Assert.Null(await StagedShellUpdateStore.LoadAsync(UpdatesDirectory, TestContext.Current.CancellationToken));
        Assert.Null(await service.GetStagedUpdateAsync(TestContext.Current.CancellationToken));
    }

    private ShellUpdateService CreateService(
        IGitHubReleaseClient releaseClient,
        AppSettingsStore settingsStore,
        string currentVersion,
        HttpClient? downloadClient = null) =>
        new(releaseClient, downloadClient ?? new HttpClient(), settingsStore, currentVersion, UpdatesDirectory);

    private static GitHubLatestRelease Release(string tag, params GitHubReleaseAsset[] assets) => new(tag, assets);

    private static GitHubReleaseAsset SetupAsset() =>
        new(ShellUpdateService.SetupAssetFileName, new Uri($"https://example.invalid/{ShellUpdateService.SetupAssetFileName}"), 414576640);

    private static GitHubReleaseAsset SumsAsset() =>
        new(ShellUpdateService.Sha256SumsAssetFileName, new Uri($"https://example.invalid/{ShellUpdateService.Sha256SumsAssetFileName}"), 219);

    private static (int Port, HttpListener Listener, Task Server) StartDownloadServer(string sumsPath, string sumsContent, string setupPath, byte[] setupPayload)
    {
        var port = ReservePort();
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        var server = Task.Run(async () =>
        {
            try
            {
                var sums = await listener.GetContextAsync();
                Assert.Equal(sumsPath, sums.Request.RawUrl);
                var sumsBytes = Encoding.ASCII.GetBytes(sumsContent);
                sums.Response.ContentLength64 = sumsBytes.Length;
                await sums.Response.OutputStream.WriteAsync(sumsBytes, TestContext.Current.CancellationToken);
                sums.Response.Close();

                var setup = await listener.GetContextAsync();
                Assert.Equal(setupPath, setup.Request.RawUrl);
                setup.Response.ContentLength64 = setupPayload.Length;
                await setup.Response.OutputStream.WriteAsync(setupPayload, TestContext.Current.CancellationToken);
                setup.Response.Close();
            }
            catch (ObjectDisposedException)
            {
                // The listener was stopped before the setup request arrived.
            }
            catch (HttpListenerException)
            {
                // The listener was stopped before the setup request arrived.
            }
        }, TestContext.Current.CancellationToken);
        return (port, listener, server);
    }

    private static int ReservePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class FakeReleaseClient : IGitHubReleaseClient
    {
        public GitHubLatestRelease? Next { get; set; }

        public int Calls { get; private set; }

        public Task<GitHubLatestRelease?> GetLatestAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Next);
        }
    }

    private sealed class RecordingProgress : IProgress<double>
    {
        public List<double> Values { get; } = [];

        public void Report(double value) => Values.Add(value);
    }

    public void Dispose() => _directory.Dispose();
}
