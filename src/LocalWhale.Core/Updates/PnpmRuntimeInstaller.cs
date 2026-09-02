using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using LocalWhale.Core.Logging;
using LocalWhale.Core.Models;
using LocalWhale.Core.Persistence;
using LocalWhale.Core.Runtime;

namespace LocalWhale.Core.Updates;

public interface IRuntimeInstaller
{
    Task<StagedRuntime> StageAndValidateAsync(string version, CancellationToken cancellationToken);
}

public sealed record PnpmInstallStep(IReadOnlyList<string> Arguments);

public sealed class PnpmRuntimeInstaller : IRuntimeInstaller
{
    private readonly LocalWhalePaths _paths;
    private readonly FileLogger _logger;
    private readonly Func<Uri, CancellationToken, Task>? _browserSmoke;

    public PnpmRuntimeInstaller(LocalWhalePaths paths, FileLogger logger, Func<Uri, CancellationToken, Task>? browserSmoke = null)
    {
        _paths = paths;
        _logger = logger;
        _browserSmoke = browserSmoke;
    }

    public async Task<StagedRuntime> StageAndValidateAsync(string version, CancellationToken cancellationToken)
    {
        _ = SemVersion.Parse(version);
        var finalDirectory = _paths.InstalledHarnessDirectory(version);
        var existingManifestPath = Path.Combine(finalDirectory, "runtime-manifest.json");
        if (File.Exists(existingManifestPath))
        {
            var existing = await new AtomicJsonFile<RuntimeManifest>(existingManifestPath).LoadAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("Existing runtime manifest was empty.");
            return new StagedRuntime(version, finalDirectory, existing);
        }

        Directory.CreateDirectory(_paths.StagingDirectory);
        var staging = Path.Combine(_paths.StagingDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            var bridgeSource = Path.Combine(_paths.InstallDirectory, "bridge");
            await RuntimeBootstrapper.EnsureSeededAsync(bridgeSource, Path.Combine(staging, "packages", "bridge"), cancellationToken).ConfigureAwait(false);
            await WriteTextAtomicallyAsync(Path.Combine(staging, "package.json"), RuntimePackageDescriptor.CreateJson(version), cancellationToken).ConfigureAwait(false);
            await WriteTextAtomicallyAsync(Path.Combine(staging, "pnpm-workspace.yaml"), RuntimePackageDescriptor.CreateWorkspaceYaml(), cancellationToken).ConfigureAwait(false);
            File.Copy(Path.Combine(bridgeSource, "desktop-bridge.yml"), Path.Combine(staging, "desktop-bridge.yml"));

            var storeDirectory = Path.Combine(_paths.LocalDataDirectory, "runtimes", "pnpm-store");
            Directory.CreateDirectory(storeDirectory);
            var preValidationPlan = CreatePreValidationPlan(storeDirectory);
            await RunPnpmAsync(staging, preValidationPlan[0].Arguments, cancellationToken).ConfigureAwait(false);
            var lockfilePath = Path.Combine(staging, "pnpm-lock.yaml");
            var lockfile = await File.ReadAllTextAsync(lockfilePath, cancellationToken).ConfigureAwait(false);
            if (!lockfile.Contains("integrity:", StringComparison.Ordinal))
            {
                throw new InvalidDataException("pnpm lockfile contains no registry integrity records.");
            }

            await RunPnpmAsync(staging, preValidationPlan[1].Arguments, cancellationToken).ConfigureAwait(false);
            await RunPnpmAsync(staging, preValidationPlan[2].Arguments, cancellationToken).ConfigureAwait(false);

            var lifecycleScripts = PackageLifecycleScriptScanner.Scan(Path.Combine(staging, "node_modules"));
            var scriptValidation = KnownLifecycleScripts.Policy.Validate(lifecycleScripts);
            if (!scriptValidation.IsCompatible)
            {
                var unknown = string.Join(", ", scriptValidation.UnknownScripts.Select(script => $"{script.PackageName}@{script.Version}:{script.ScriptName}"));
                throw new InvalidDataException($"Candidate runtime contains unapproved lifecycle scripts: {unknown}");
            }

            await RunPnpmAsync(staging, ["rebuild", "--pending", "--store-dir", storeDirectory], cancellationToken).ConfigureAwait(false);
            await SmokeTestAsync(staging, version, cancellationToken).ConfigureAwait(false);

            var manifest = new RuntimeManifest(
                version,
                LocalWhalePaths.NodeVersion,
                LocalWhalePaths.PnpmVersion,
                BridgeClient.SupportedProtocolVersion,
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(lockfile))).ToLowerInvariant(),
                DateTimeOffset.UtcNow);
            await new AtomicJsonFile<RuntimeManifest>(Path.Combine(staging, "runtime-manifest.json")).SaveAsync(manifest, cancellationToken).ConfigureAwait(false);

            Directory.CreateDirectory(_paths.HarnessRuntimesDirectory);
            Directory.Move(staging, finalDirectory);
            return new StagedRuntime(version, finalDirectory, manifest);
        }
        catch
        {
            TemporaryDirectoryCleaner.DeleteTreeWithin(_paths.StagingDirectory, staging);
            throw;
        }
    }

    public static IReadOnlyList<PnpmInstallStep> CreatePreValidationPlan(string storeDirectory) =>
    [
        new(["install", "--lockfile-only", "--ignore-scripts", "--store-dir", storeDirectory]),
        new(["fetch", "--frozen-lockfile", "--ignore-scripts", "--store-dir", storeDirectory]),
        new(["install", "--offline", "--frozen-lockfile", "--ignore-scripts", "--store-dir", storeDirectory])
    ];

    private async Task SmokeTestAsync(string runtimeDirectory, string version, CancellationToken cancellationToken)
    {
        var smokeDirectory = Path.Combine(runtimeDirectory, ".smoke");
        var dshHome = Path.Combine(smokeDirectory, "dsh-home");
        var workspace = Path.Combine(smokeDirectory, "workspace");
        Directory.CreateDirectory(dshHome);
        Directory.CreateDirectory(workspace);
        await using var manager = new HarnessRuntimeManager(
            (_, token) => new HarnessLaunchSpec(
                _paths.NodeExecutable,
                Path.Combine(runtimeDirectory, "node_modules", "@deepseek-ai", "dsh", "lib", "bin.js"),
                Path.Combine(runtimeDirectory, "desktop-bridge.yml"),
                workspace,
                token,
                dshHome,
                version,
                Path.Combine(runtimeDirectory, "node_modules", "@localwhale", "dsh-desktop-bridge", "src", "index.js")),
            TimeSpan.FromSeconds(30),
            _logger.Write);
        try
        {
            var runtime = await manager.StartAsync(version, cancellationToken).ConfigureAwait(false);
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var html = await httpClient.GetStringAsync(runtime.BaseUri, cancellationToken).ConfigureAwait(false);
            ValidateFrontendBootHtml(html);
            if (_browserSmoke is not null) await _browserSmoke(runtime.BaseUri, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await manager.StopAsync(CancellationToken.None).ConfigureAwait(false);
            TemporaryDirectoryCleaner.DeleteTreeWithin(runtimeDirectory, smokeDirectory);
        }
    }

    /// <summary>
    /// Accepts either frontend generation: Harness ≤0.1.0 injects an inline
    /// `window.__DSH_BOOT__` manifest referencing `@deepseek-ai/dsh-client-runtime`, while
    /// 0.1.1+ serves the Vite app shell (`#root` plus a module script) whose boot manifest is
    /// fetched by the client at runtime. Anything else is not a Harness frontend page.
    /// </summary>
    public static void ValidateFrontendBootHtml(string html)
    {
        var hasLegacyBootManifest = html.Contains("window.__DSH_BOOT__", StringComparison.Ordinal)
            && html.Contains("@deepseek-ai/dsh-client-runtime", StringComparison.Ordinal);
        var hasAppShell = html.Contains("id=\"root\"", StringComparison.Ordinal)
            && html.Contains("type=\"module\"", StringComparison.Ordinal);
        if (!hasLegacyBootManifest && !hasAppShell)
        {
            throw new InvalidDataException("Candidate Harness frontend did not expose a boot manifest or an app shell.");
        }
    }

    private async Task RunPnpmAsync(string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        if (!File.Exists(_paths.NodeExecutable)) throw new FileNotFoundException("Bundled Node executable is missing.", _paths.NodeExecutable);
        if (!File.Exists(_paths.PnpmScript)) throw new FileNotFoundException("Bundled pnpm script is missing.", _paths.PnpmScript);
        var startInfo = PnpmCommandBuilder.Build(_paths, workingDirectory, arguments);
        using var process = new Process { StartInfo = startInfo };
        using var job = new ProcessJob();
        if (!process.Start()) throw new InvalidOperationException("Bundled pnpm did not start.");
        job.Assign(process);
        var stdout = PumpAsync(process.StandardOutput, cancellationToken);
        var stderr = PumpAsync(process.StandardError, cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
        if (process.ExitCode != 0) throw new InvalidOperationException($"pnpm exited with code {process.ExitCode}.");
    }

    private async Task PumpAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line) _logger.Write(line);
    }

    private static async Task WriteTextAtomicallyAsync(string path, string content, CancellationToken cancellationToken)
    {
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, content, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
