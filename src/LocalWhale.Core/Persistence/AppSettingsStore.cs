using LocalWhale.Core.Models;

namespace LocalWhale.Core.Persistence;

public sealed class AppSettingsStore(string path)
{
    private readonly AtomicJsonFile<AppSettings> _file = new(path);

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) =>
        await _file.LoadAsync(cancellationToken).ConfigureAwait(false) ?? AppSettings.Default;

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) =>
        _file.SaveAsync(settings, cancellationToken);
}
