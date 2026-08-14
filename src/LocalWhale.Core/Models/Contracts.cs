namespace LocalWhale.Core.Models;

public enum CloseBehavior
{
    Exit,
    MinimizeToTray
}

public sealed record AppSettings(
    CloseBehavior CloseBehavior,
    IReadOnlyList<string> IgnoredHarnessVersions,
    DateTimeOffset? LastUpdateCheckUtc)
{
    public static AppSettings Default { get; } = new(CloseBehavior.Exit, Array.Empty<string>(), null);
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
