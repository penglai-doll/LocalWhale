namespace LocalWhale.Core.Logging;

public static class LogFileMaintenance
{
    public static void Enforce(string directory, DateTimeOffset now, TimeSpan retention, long maxTotalBytes)
    {
        if (!Directory.Exists(directory)) return;
        var files = new DirectoryInfo(directory).EnumerateFiles("*.log", SearchOption.TopDirectoryOnly).ToList();
        var expiration = now.UtcDateTime - retention;
        foreach (var expired in files.Where(file => file.LastWriteTimeUtc < expiration).ToArray())
        {
            expired.Delete();
            files.Remove(expired);
        }

        var totalBytes = files.Sum(file => file.Length);
        foreach (var file in files.OrderBy(file => file.LastWriteTimeUtc))
        {
            if (totalBytes <= maxTotalBytes) break;
            totalBytes -= file.Length;
            file.Delete();
        }
    }
}
