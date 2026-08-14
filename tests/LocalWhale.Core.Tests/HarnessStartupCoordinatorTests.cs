using LocalWhale.Core.Runtime;

namespace LocalWhale.Core.Tests;

public sealed class HarnessStartupCoordinatorTests
{
    [Fact]
    public async Task StartAsync_retries_profile_resolution_failure_once_in_a_new_process()
    {
        using var workspace = new TemporaryTestDirectory();
        var dshHome = Directory.CreateDirectory(Path.Combine(workspace.Path, ".dsh")).FullName;
        var profile = Directory.CreateDirectory(Path.Combine(dshHome, "profiles", "web")).FullName;
        var launches = 0;
        await using var manager = CreateManager("fake-harness-profile-race.mjs", profile, () => launches++);
        var log = new List<string>();

        var runtime = await HarnessStartupCoordinator.StartAsync(
            manager,
            "0.1.0-rc.6",
            dshHome,
            log.Add,
            TestContext.Current.CancellationToken);

        Assert.True(manager.IsRunning);
        Assert.True(runtime.ProcessId > 0);
        Assert.Equal(2, launches);
        Assert.Single(log);
        Assert.Contains("retrying once", log[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StartAsync_does_not_retry_an_unrelated_startup_failure()
    {
        using var workspace = new TemporaryTestDirectory();
        var dshHome = Directory.CreateDirectory(Path.Combine(workspace.Path, ".dsh")).FullName;
        var unrelated = Directory.CreateDirectory(Path.Combine(workspace.Path, "unrelated")).FullName;
        var launches = 0;
        await using var manager = CreateManager("fake-harness-startup-failure.mjs", unrelated, () => launches++);

        await Assert.ThrowsAsync<HarnessStartupException>(() => HarnessStartupCoordinator.StartAsync(
            manager,
            "0.1.0-rc.6",
            dshHome,
            null,
            TestContext.Current.CancellationToken));

        Assert.Equal(1, launches);
    }

    [Fact]
    public async Task StartAsync_does_not_launch_a_third_process_when_the_retry_also_fails()
    {
        using var workspace = new TemporaryTestDirectory();
        var dshHome = Directory.CreateDirectory(Path.Combine(workspace.Path, ".dsh")).FullName;
        var profile = Directory.CreateDirectory(Path.Combine(dshHome, "profiles", "web")).FullName;
        var launches = 0;
        await using var manager = CreateManager("fake-harness-startup-failure.mjs", profile, () => launches++);
        var log = new List<string>();

        await Assert.ThrowsAsync<HarnessStartupException>(() => HarnessStartupCoordinator.StartAsync(
            manager,
            "0.1.0-rc.6",
            dshHome,
            log.Add,
            TestContext.Current.CancellationToken));

        Assert.Equal(2, launches);
        Assert.Single(log);
    }

    private static HarnessRuntimeManager CreateManager(
        string fixtureName,
        string workingDirectory,
        Action onLaunch)
    {
        var script = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixtureName);
        return new HarnessRuntimeManager(
            (version, token) =>
            {
                onLaunch();
                return new HarnessLaunchSpec(
                    ResolveNodeExecutable(),
                    script,
                    script,
                    workingDirectory,
                    token,
                    HarnessVersion: version);
            },
            TimeSpan.FromSeconds(10));
    }

    private static string ResolveNodeExecutable()
    {
        var configured = Environment.GetEnvironmentVariable("LOCALWHALE_TEST_NODE");
        if (File.Exists(configured)) return configured;
        var programFilesNode = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "nodejs",
            "node.exe");
        if (File.Exists(programFilesNode)) return programFilesNode;
        throw new FileNotFoundException("Node.js is required for the Harness runtime integration test.");
    }
}
