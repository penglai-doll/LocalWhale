namespace LocalWhale.Core.Logging;

public sealed class FileLogger
{
    private const long MaximumTotalBytes = 50L * 1024 * 1024;
    private readonly string _directory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileLogger(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
        LogFileMaintenance.Enforce(directory, DateTimeOffset.UtcNow, TimeSpan.FromDays(14), MaximumTotalBytes);
    }

    public void Write(string message) => WriteAsync(message, CancellationToken.None).GetAwaiter().GetResult();

    public async Task WriteAsync(string message, CancellationToken cancellationToken = default)
    {
        var line = $"{DateTimeOffset.UtcNow:O} {LogRedactor.Redact(message)}{Environment.NewLine}";
        var path = Path.Combine(_directory, $"localwhale-{DateTimeOffset.UtcNow:yyyyMMdd}.log");
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await File.AppendAllTextAsync(path, line, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }
}
