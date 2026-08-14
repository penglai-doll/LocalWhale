using LocalWhale.Core.Logging;

namespace LocalWhale.Core.Tests;

public sealed class LogFileMaintenanceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "LocalWhale.LogTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Enforce_deletes_expired_files_and_oldest_files_until_under_the_size_cap()
    {
        Directory.CreateDirectory(_directory);
        var now = new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);
        WriteLog("expired.log", 4, now.AddDays(-15));
        WriteLog("older.log", 7, now.AddDays(-2));
        WriteLog("newer.log", 7, now.AddDays(-1));

        LogFileMaintenance.Enforce(_directory, now, TimeSpan.FromDays(14), maxTotalBytes: 10);

        Assert.False(File.Exists(Path.Combine(_directory, "expired.log")));
        Assert.False(File.Exists(Path.Combine(_directory, "older.log")));
        Assert.True(File.Exists(Path.Combine(_directory, "newer.log")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private void WriteLog(string name, int bytes, DateTimeOffset lastWrite)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, new byte[bytes]);
        File.SetLastWriteTimeUtc(path, lastWrite.UtcDateTime);
    }
}
