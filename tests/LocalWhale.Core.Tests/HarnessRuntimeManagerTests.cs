using LocalWhale.Core.Runtime;

namespace LocalWhale.Core.Tests;

public sealed class HarnessRuntimeManagerTests
{
    [Fact]
    public async Task StartAsync_preserves_bounded_output_when_process_exits_before_ready()
    {
        var node = ResolveNodeExecutable();
        var script = Path.Combine(AppContext.BaseDirectory, "Fixtures", "fake-harness-startup-failure.mjs");
        await using var manager = new HarnessRuntimeManager(
            (version, token) => new HarnessLaunchSpec(node, script, script, AppContext.BaseDirectory, token, HarnessVersion: version),
            TimeSpan.FromSeconds(10));

        var error = await Assert.ThrowsAsync<HarnessStartupException>(() =>
            manager.StartAsync("0.1.0-rc.6", TestContext.Current.CancellationToken));

        Assert.Equal(1, error.ExitCode);
        Assert.Contains(error.Output, line => line.Contains("ERR_MODULE_NOT_FOUND", StringComparison.Ordinal));
        Assert.InRange(error.Output.Count, 1, HarnessStartupException.MaximumCapturedLines);
    }

    [Fact]
    public async Task StartAndStop_use_random_port_authenticated_health_and_graceful_shutdown()
    {
        var node = ResolveNodeExecutable();
        var script = Path.Combine(AppContext.BaseDirectory, "Fixtures", "fake-harness.mjs");
        await using var manager = new HarnessRuntimeManager(
            (version, token) => new HarnessLaunchSpec(node, script, script, AppContext.BaseDirectory, token, HarnessVersion: version),
            TimeSpan.FromSeconds(10));

        var runtime = await manager.StartAsync("0.1.0-rc.6", TestContext.Current.CancellationToken);

        Assert.Equal("127.0.0.1", runtime.BaseUri.Host);
        Assert.NotEqual(3080, runtime.BaseUri.Port);
        Assert.True(runtime.ProcessId > 0);

        await manager.StopAsync(TestContext.Current.CancellationToken);
        Assert.False(manager.IsRunning);
    }

    [Fact]
    public async Task UnexpectedExit_reports_the_version_exit_code_and_short_uptime()
    {
        var node = ResolveNodeExecutable();
        var script = Path.Combine(AppContext.BaseDirectory, "Fixtures", "fake-harness-crash.mjs");
        await using var manager = new HarnessRuntimeManager(
            (version, token) => new HarnessLaunchSpec(node, script, script, AppContext.BaseDirectory, token, HarnessVersion: version),
            TimeSpan.FromSeconds(10));
        var exited = new TaskCompletionSource<HarnessUnexpectedExit>(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.UnexpectedExit += (_, args) => exited.TrySetResult(args);

        var runtime = await manager.StartAsync("0.2.0", TestContext.Current.CancellationToken);
        var crash = await exited.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal("0.2.0", crash.Version);
        Assert.Equal(runtime.ProcessId, crash.ProcessId);
        Assert.Equal(23, crash.ExitCode);
        Assert.InRange(crash.Uptime, TimeSpan.Zero, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public async Task Restart_stops_the_old_process_and_returns_a_new_ready_runtime()
    {
        var node = ResolveNodeExecutable();
        var script = Path.Combine(AppContext.BaseDirectory, "Fixtures", "fake-harness.mjs");
        await using var manager = new HarnessRuntimeManager(
            (version, token) => new HarnessLaunchSpec(node, script, script, AppContext.BaseDirectory, token, HarnessVersion: version),
            TimeSpan.FromSeconds(10));

        var first = await manager.StartAsync("0.1.0-rc.6", TestContext.Current.CancellationToken);
        var second = await manager.RestartAsync(TestContext.Current.CancellationToken);

        Assert.NotEqual(first.ProcessId, second.ProcessId);
        Assert.True(manager.IsRunning);
        Assert.Equal("0.1.0-rc.6", second.Version);
    }

    private static string ResolveNodeExecutable()
    {
        var configured = Environment.GetEnvironmentVariable("LOCALWHALE_TEST_NODE");
        if (File.Exists(configured)) return configured;
        var programFilesNode = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "node.exe");
        if (File.Exists(programFilesNode)) return programFilesNode;
        throw new FileNotFoundException("Node.js is required for the Harness runtime integration test.");
    }
}
