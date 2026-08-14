using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace LocalWhale.Core.Persistence;

public sealed class AtomicJsonFile<T>
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonTypeInfo<T> _typeInfo;

    public AtomicJsonFile(string path, JsonTypeInfo<T>? typeInfo = null)
    {
        Path = System.IO.Path.GetFullPath(path);
        _typeInfo = typeInfo ?? LocalWhaleJsonContext.Default.GetTypeInfo(typeof(T)) as JsonTypeInfo<T>
            ?? throw new NotSupportedException($"No source-generated JSON metadata is registered for {typeof(T).FullName}.");
    }

    public string Path { get; }

    public async Task<T?> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(Path)) return default;
            await using var stream = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await JsonSerializer.DeserializeAsync(stream, _typeInfo, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(T value, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var directory = System.IO.Path.GetDirectoryName(Path) ?? throw new InvalidOperationException("JSON file must have a parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = System.IO.Path.Combine(directory, $".{System.IO.Path.GetFileName(Path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, value, _typeInfo, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, Path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            _gate.Release();
        }
    }
}
