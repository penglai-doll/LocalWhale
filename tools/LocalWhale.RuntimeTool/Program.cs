using System.Security.Cryptography;
using LocalWhale.Core.Models;
using LocalWhale.Core.Persistence;
using LocalWhale.Core.Runtime;
using LocalWhale.Core.Updates;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: LocalWhale.RuntimeTool validate-lifecycle <node_modules> | write-manifest <runtime-dir> <harness-version> | smoke-runtime <runtime-dir> <node.exe> <harness-version>");
    return 64;
}

switch (args[0])
{
    case "validate-lifecycle":
    {
        var scripts = PackageLifecycleScriptScanner.Scan(args[1]);
        var result = KnownLifecycleScripts.Policy.Validate(scripts);
        foreach (var script in scripts)
        {
            Console.WriteLine($"{script.PackageName}@{script.Version}:{script.ScriptName} {LifecycleScriptPolicy.ComputeScriptSha256(script.Script)}");
        }

        if (result.IsCompatible) return 0;
        foreach (var script in result.UnknownScripts)
        {
            Console.Error.WriteLine($"Unapproved lifecycle script: {script.PackageName}@{script.Version}:{script.ScriptName}");
        }
        return 2;
    }
    case "write-manifest" when args.Length >= 3:
    {
        var runtimeDirectory = Path.GetFullPath(args[1]);
        var lockfile = Path.Combine(runtimeDirectory, "pnpm-lock.yaml");
        var manifest = new RuntimeManifest(
            args[2],
            LocalWhalePaths.NodeVersion,
            LocalWhalePaths.PnpmVersion,
            BridgeClient.SupportedProtocolVersion,
            Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(lockfile))).ToLowerInvariant(),
            DateTimeOffset.UtcNow);
        await new AtomicJsonFile<RuntimeManifest>(Path.Combine(runtimeDirectory, "runtime-manifest.json")).SaveAsync(manifest);
        return 0;
    }
    case "smoke-runtime" when args.Length >= 4:
    {
        var runtimeDirectory = Path.GetFullPath(args[1]);
        var nodeExecutable = Path.GetFullPath(args[2]);
        var version = args[3];
        var smokeRoot = Path.Combine(Path.GetTempPath(), "LocalWhale.RuntimeSmoke", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(smokeRoot, "home"));
        Directory.CreateDirectory(Path.Combine(smokeRoot, "workspace"));
        await using var manager = new HarnessRuntimeManager(
            (_, token) => new HarnessLaunchSpec(
                nodeExecutable,
                Path.Combine(runtimeDirectory, "node_modules", "@deepseek-ai", "dsh", "lib", "bin.js"),
                Path.Combine(runtimeDirectory, "desktop-bridge.yml"),
                Path.Combine(smokeRoot, "workspace"),
                token,
                Path.Combine(smokeRoot, "home"),
                version,
                Path.Combine(runtimeDirectory, "node_modules", "@localwhale", "dsh-desktop-bridge", "src", "index.js")),
            TimeSpan.FromSeconds(30),
            Console.WriteLine);
        try
        {
            var runtime = await manager.StartAsync(version, CancellationToken.None);
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var html = await client.GetStringAsync(runtime.BaseUri);
            if (!html.Contains("window.__DSH_BOOT__", StringComparison.Ordinal)) throw new InvalidDataException("Harness boot manifest missing.");
            Console.WriteLine($"Smoke passed at {runtime.BaseUri} with pid {runtime.ProcessId}.");
            await manager.StopAsync(CancellationToken.None);
            return 0;
        }
        finally
        {
            await manager.StopAsync(CancellationToken.None);
            TemporaryDirectoryCleaner.DeleteTreeWithin(
                Path.Combine(Path.GetTempPath(), "LocalWhale.RuntimeSmoke"),
                smokeRoot);
        }
    }
    default:
        Console.Error.WriteLine($"Unknown command: {args[0]}");
        return 64;
}
