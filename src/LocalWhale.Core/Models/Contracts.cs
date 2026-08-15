namespace LocalWhale.Core.Models;

public enum CloseBehavior
{
    Exit,
    MinimizeToTray
}

public sealed record AppSettings(
    CloseBehavior CloseBehavior,
    IReadOnlyList<string> IgnoredHarnessVersions,
    DateTimeOffset? LastUpdateCheckUtc,
    VisualTheme VisualTheme,
    bool ShellUpdateCheckEnabled = true,
    DateTimeOffset? LastShellUpdateCheckUtc = null,
    IReadOnlyList<string>? IgnoredShellVersions = null)
{
    public static AppSettings Default { get; } =
        new(CloseBehavior.Exit, Array.Empty<string>(), null, VisualTheme.Original);
}

public sealed record RuntimeManifest(
    string HarnessVersion,
    string NodeVersion,
    string PnpmVersion,
    int BridgeProtocolVersion,
    string LockfileSha256,
    DateTimeOffset InstalledAtUtc);

public sealed record RuntimeState(
    string ActiveVersion,
    string? PreviousVersion,
    IReadOnlyDictionary<string, int> FailedStarts);

public sealed record HarnessRuntimeInfo(
    string Version,
    Uri BaseUri,
    int ProcessId,
    DateTimeOffset StartedAtUtc);

public sealed record HarnessUpdate(string CurrentVersion, string AvailableVersion);

public sealed record StagedRuntime(string Version, string DirectoryPath, RuntimeManifest Manifest);

public sealed record ShellUpdate(
    string CurrentVersion,
    string AvailableVersion,
    Uri SetupDownloadUrl,
    Uri Sha256SumsDownloadUrl,
    long SetupSizeBytes);

public sealed record StagedShellUpdate(
    string Version,
    string SetupPath,
    string SetupSha256,
    DateTimeOffset StagedAtUtc);

public interface IHarnessRuntimeManager
{
    Task<HarnessRuntimeInfo> StartAsync(string version, CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
    Task<HarnessRuntimeInfo> RestartAsync(CancellationToken cancellationToken);
}

public interface IHarnessUpdateService
{
    Task<HarnessUpdate?> CheckAsync(CancellationToken cancellationToken);
    Task<StagedRuntime> StageAndValidateAsync(string version, CancellationToken cancellationToken);
    Task ActivateOnRestartAsync(StagedRuntime runtime, CancellationToken cancellationToken);
}

public interface IShellUpdateService
{
    Task<ShellUpdate?> CheckAsync(bool force, CancellationToken cancellationToken);
    Task<StagedShellUpdate> DownloadAndStageAsync(ShellUpdate update, IProgress<double>? progress, CancellationToken cancellationToken);
}
