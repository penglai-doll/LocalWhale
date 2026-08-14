using LocalWhale.Core.Models;
using LocalWhale.Core.Persistence;

namespace LocalWhale.Core.Updates;

public sealed class HarnessUpdateService : IHarnessUpdateService
{
    private static readonly TimeSpan AutomaticCheckInterval = TimeSpan.FromHours(24);
    private readonly INpmRegistryClient _registry;
    private readonly IRuntimeInstaller _installer;
    private readonly AppSettingsStore _settingsStore;
    private readonly AtomicJsonFile<RuntimeState> _runtimeStateStore;

    public HarnessUpdateService(
        INpmRegistryClient registry,
        IRuntimeInstaller installer,
        AppSettingsStore settingsStore,
        AtomicJsonFile<RuntimeState> runtimeStateStore)
    {
        _registry = registry;
        _installer = installer;
        _settingsStore = settingsStore;
        _runtimeStateStore = runtimeStateStore;
    }

    public Task<HarnessUpdate?> CheckAsync(CancellationToken cancellationToken) => CheckAsync(force: false, cancellationToken);

    public async Task<HarnessUpdate?> CheckAsync(bool force, CancellationToken cancellationToken)
    {
        var settings = await _settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (!force && settings.LastUpdateCheckUtc is { } lastCheck && DateTimeOffset.UtcNow - lastCheck < AutomaticCheckInterval) return null;
        var state = await _runtimeStateStore.LoadAsync(cancellationToken).ConfigureAwait(false)
            ?? new RuntimeState(LocalWhalePaths.InitialHarnessVersion, null, new Dictionary<string, int>());
        var latest = await _registry.GetLatestAsync(cancellationToken).ConfigureAwait(false);
        settings = settings with { LastUpdateCheckUtc = DateTimeOffset.UtcNow };
        await _settingsStore.SaveAsync(settings, cancellationToken).ConfigureAwait(false);
        if (settings.IgnoredHarnessVersions.Contains(latest.Version, StringComparer.OrdinalIgnoreCase)) return null;
        if (SemVersion.Parse(latest.Version) <= SemVersion.Parse(state.ActiveVersion)) return null;
        return new HarnessUpdate(state.ActiveVersion, latest.Version);
    }

    public Task<StagedRuntime> StageAndValidateAsync(string version, CancellationToken cancellationToken) =>
        _installer.StageAndValidateAsync(version, cancellationToken);

    public async Task ActivateOnRestartAsync(StagedRuntime runtime, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(runtime.DirectoryPath)) throw new DirectoryNotFoundException(runtime.DirectoryPath);
        var state = await _runtimeStateStore.LoadAsync(cancellationToken).ConfigureAwait(false)
            ?? new RuntimeState(LocalWhalePaths.InitialHarnessVersion, null, new Dictionary<string, int>());
        var failures = new Dictionary<string, int>(state.FailedStarts, StringComparer.OrdinalIgnoreCase)
        {
            [runtime.Version] = 0
        };
        await _runtimeStateStore.SaveAsync(
            new RuntimeState(runtime.Version, state.ActiveVersion, failures),
            cancellationToken).ConfigureAwait(false);
    }
}
