namespace LocalWhale.Core.Persistence;

public sealed class LocalWhalePaths
{
    public const string InitialHarnessVersion = "0.1.0-rc.6";
    public const string NodeVersion = "24.18.1";
    public const string PnpmVersion = "11.7.0";

    public LocalWhalePaths(string? installDirectory = null, string? localDataDirectory = null)
    {
        InstallDirectory = Path.GetFullPath(installDirectory ?? AppContext.BaseDirectory);
        LocalDataDirectory = Path.GetFullPath(localDataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LocalWhale"));
    }

    public string InstallDirectory { get; }
    public string LocalDataDirectory { get; }
    public string SettingsFile => Path.Combine(LocalDataDirectory, "settings.json");
    public string RuntimeStateFile => Path.Combine(LocalDataDirectory, "runtime-state.json");
    public string HarnessRuntimesDirectory => Path.Combine(LocalDataDirectory, "runtimes", "harness");
    public string StagingDirectory => Path.Combine(LocalDataDirectory, "runtimes", "staging");
    public string WebView2Directory => Path.Combine(LocalDataDirectory, "webview2");
    public string LogsDirectory => Path.Combine(LocalDataDirectory, "logs");
    public string NodeExecutable => Path.Combine(InstallDirectory, "runtime", "node", "node.exe");
    public string PnpmScript => Path.Combine(InstallDirectory, "runtime", "pnpm", "bin", "pnpm.cjs");
    public string BundledHarnessDirectory(string version) => Path.Combine(InstallDirectory, "runtime", "harness", version);
    public string InstalledHarnessDirectory(string version) => Path.Combine(HarnessRuntimesDirectory, version);

    public string ResolveHarnessDirectory(string version)
    {
        var installed = InstalledHarnessDirectory(version);
        if (Directory.Exists(installed)) return installed;

        var bundled = BundledHarnessDirectory(version);
        if (string.Equals(version, InitialHarnessVersion, StringComparison.OrdinalIgnoreCase) && Directory.Exists(bundled)) return bundled;

        throw new DirectoryNotFoundException($"Harness runtime {version} is not installed.");
    }
}
