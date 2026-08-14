using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using LocalWhale.Core.Logging;
using LocalWhale.Core.Models;

namespace LocalWhale.Core.Runtime;

public sealed record HarnessUnexpectedExit(string Version, int ProcessId, int ExitCode, TimeSpan Uptime);

public sealed class HarnessRuntimeManager : IHarnessRuntimeManager, IAsyncDisposable
{
    private readonly Func<string, string, HarnessLaunchSpec> _launchSpecFactory;
    private readonly TimeSpan _startupTimeout;
    private readonly Action<string>? _log;
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(2) };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _process;
    private ProcessJob? _job;
    private CancellationTokenSource? _lifetimeCancellation;
    private Task? _standardOutputPump;
    private Task? _standardErrorPump;
    private BridgeClient? _bridge;
    private BridgeOverlayMaterializer? _bridgeOverlay;
    private HarnessRuntimeInfo? _runtime;
    private string? _activeVersion;
    private int _stopping;

    public HarnessRuntimeManager(
        Func<string, string, HarnessLaunchSpec> launchSpecFactory,
        TimeSpan startupTimeout,
        Action<string>? log = null)
    {
        _launchSpecFactory = launchSpecFactory;
        _startupTimeout = startupTimeout;
        _log = log;
    }

    public bool IsRunning => _process is { HasExited: false };
    public HarnessRuntimeInfo? CurrentRuntime => _runtime;
    public event EventHandler<HarnessUnexpectedExit>? UnexpectedExit;

    public async Task<HarnessRuntimeInfo> StartAsync(string version, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsRunning && string.Equals(_activeVersion, version, StringComparison.Ordinal)) return _runtime!;
            if (_process is not null) await StopCoreAsync(cancellationToken).ConfigureAwait(false);

            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            var launchSpec = _launchSpecFactory(version, token);
            BridgeOverlayMaterializer? bridgeOverlay = null;
            if (!string.IsNullOrWhiteSpace(launchSpec.BridgeModulePath))
            {
                bridgeOverlay = BridgeOverlayMaterializer.Create(launchSpec.PatchPath, launchSpec.BridgeModulePath);
                launchSpec = launchSpec with { PatchPath = bridgeOverlay.Path };
            }
            var startInfo = HarnessCommandBuilder.Build(launchSpec);
            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            var ready = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
            var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var startupOutput = new ConcurrentQueue<string>();
            var startedAt = DateTimeOffset.UtcNow;
            var becameReady = 0;
            process.Exited += (_, _) =>
            {
                exited.TrySetResult();
                if (Volatile.Read(ref becameReady) == 0 || Volatile.Read(ref _stopping) != 0 || !ReferenceEquals(_process, process)) return;
                var uptime = DateTimeOffset.UtcNow - startedAt;
                UnexpectedExit?.Invoke(this, new HarnessUnexpectedExit(version, process.Id, process.ExitCode, uptime));
            };
            var job = new ProcessJob();
            var lifetimeCancellation = new CancellationTokenSource();

            try
            {
                if (!process.Start()) throw new InvalidOperationException("Harness process did not start.");
                job.Assign(process);
                _standardOutputPump = PumpAsync(process.StandardOutput, ready, startupOutput, lifetimeCancellation.Token);
                _standardErrorPump = PumpAsync(process.StandardError, null, startupOutput, lifetimeCancellation.Token);

                using var startupCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                startupCancellation.CancelAfter(_startupTimeout);
                var first = await Task.WhenAny(ready.Task, exited.Task).WaitAsync(startupCancellation.Token).ConfigureAwait(false);
                if (first == exited.Task)
                {
                    await ObservePumpAsync(_standardOutputPump).ConfigureAwait(false);
                    await ObservePumpAsync(_standardErrorPump).ConfigureAwait(false);
                    throw new HarnessStartupException(process.ExitCode, Array.AsReadOnly(startupOutput.ToArray()));
                }

                var baseUri = await ready.Task.ConfigureAwait(false);
                var bridge = new BridgeClient(_httpClient, token);
                var health = await WaitForHealthAsync(bridge, baseUri, version, process.Id, startupCancellation.Token).ConfigureAwait(false);
                _process = process;
                _job = job;
                _lifetimeCancellation = lifetimeCancellation;
                _bridge = bridge;
                _bridgeOverlay = bridgeOverlay;
                bridgeOverlay = null;
                _activeVersion = version;
                _runtime = new HarnessRuntimeInfo(health.HarnessVersion, baseUri, process.Id, startedAt);
                Volatile.Write(ref becameReady, 1);
                Log($"Harness {version} ready at {baseUri} (pid {process.Id}).");
                return _runtime;
            }
            catch
            {
                lifetimeCancellation.Cancel();
                job.Dispose();
                process.Dispose();
                lifetimeCancellation.Dispose();
                bridgeOverlay?.Dispose();
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<HarnessRuntimeInfo> RestartAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var version = _activeVersion ?? throw new InvalidOperationException("Harness has not been started.");
            await StopCoreAsync(cancellationToken).ConfigureAwait(false);
            return await StartCoreAfterRestartAsync(version, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        _httpClient.Dispose();
        _gate.Dispose();
    }

    private async Task<HarnessRuntimeInfo> StartCoreAfterRestartAsync(string version, CancellationToken cancellationToken)
    {
        _gate.Release();
        try
        {
            return await StartAsync(version, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task StopCoreAsync(CancellationToken cancellationToken)
    {
        var process = _process;
        if (process is null) return;
        Interlocked.Exchange(ref _stopping, 1);
        var job = _job;
        var lifetimeCancellation = _lifetimeCancellation;
        try
        {
            if (!process.HasExited && _bridge is not null && _runtime is not null)
            {
                using var gracefulCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                gracefulCancellation.CancelAfter(TimeSpan.FromSeconds(5));
                try
                {
                    await _bridge.ShutdownAsync(_runtime.BaseUri, gracefulCancellation.Token).ConfigureAwait(false);
                    await process.WaitForExitAsync(gracefulCancellation.Token).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
                {
                    Log($"Graceful Harness shutdown did not complete: {exception.Message}");
                }
            }
        }
        finally
        {
            if (!process.HasExited) job?.Dispose();
            if (!process.HasExited)
            {
                using var forceCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                forceCancellation.CancelAfter(TimeSpan.FromSeconds(2));
                try { await process.WaitForExitAsync(forceCancellation.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
            }
            lifetimeCancellation?.Cancel();
            await ObservePumpAsync(_standardOutputPump).ConfigureAwait(false);
            await ObservePumpAsync(_standardErrorPump).ConfigureAwait(false);
            job?.Dispose();
            process.Dispose();
            lifetimeCancellation?.Dispose();
            _process = null;
            _job = null;
            _lifetimeCancellation = null;
            _standardOutputPump = null;
            _standardErrorPump = null;
            _bridge = null;
            _bridgeOverlay?.Dispose();
            _bridgeOverlay = null;
            _runtime = null;
            Interlocked.Exchange(ref _stopping, 0);
            Log("Harness stopped.");
        }
    }

    private async Task PumpAsync(
        StreamReader reader,
        TaskCompletionSource<Uri>? ready,
        ConcurrentQueue<string> startupOutput,
        CancellationToken cancellationToken)
    {
        try
        {
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                var redacted = LogRedactor.Redact(line);
                _log?.Invoke(redacted);
                startupOutput.Enqueue(redacted);
                while (startupOutput.Count > HarnessStartupException.MaximumCapturedLines)
                {
                    startupOutput.TryDequeue(out _);
                }
                if (ready is not null && HarnessOutputParser.TryParseReadyUri(line, out var uri)) ready.TrySetResult(uri!);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static async Task<BridgeHealth> WaitForHealthAsync(
        BridgeClient bridge,
        Uri baseUri,
        string expectedVersion,
        int expectedPid,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var health = await bridge.GetHealthAsync(baseUri, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(health.HarnessVersion, expectedVersion, StringComparison.Ordinal) || health.Pid != expectedPid)
                {
                    throw new InvalidDataException("Bridge health identity did not match the launched Harness process.");
                }

                return health;
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                lastError = exception;
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new TimeoutException("Harness bridge health check timed out.", lastError);
    }

    private static async Task ObservePumpAsync(Task? task)
    {
        if (task is null) return;
        try { await task.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
    }

    private void Log(string value) => _log?.Invoke(LogRedactor.Redact(value));
}
