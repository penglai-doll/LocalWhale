using LocalWhale.Core.Models;

namespace LocalWhale.Core.Persistence;

public sealed class AppSettingsStore(string path)
{
    private readonly AtomicJsonFile<AppSettings> _file = new(path);

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _file.LoadAsync(cancellationToken).ConfigureAwait(false) ?? AppSettings.Default;
        // Older settings files predate the shell-update fields; treat them as enabled with no ignored versions.
        return settings.IgnoredShellVersions is null
            ? settings with { IgnoredShellVersions = Array.Empty<string>() }
            : settings;
    }

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) =>
        _file.SaveAsync(settings, cancellationToken);
}
