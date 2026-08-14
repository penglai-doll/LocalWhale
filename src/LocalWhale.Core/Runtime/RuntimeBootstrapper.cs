namespace LocalWhale.Core.Runtime;

public static class RuntimeBootstrapper
{
    public static async Task EnsureSeededAsync(string sourceDirectory, string destinationDirectory, CancellationToken cancellationToken)
    {
        if (Directory.Exists(destinationDirectory)) return;
        if (!Directory.Exists(sourceDirectory)) throw new DirectoryNotFoundException($"Bundled Harness runtime was not found: {sourceDirectory}");
        var parent = Path.GetDirectoryName(destinationDirectory) ?? throw new InvalidOperationException("Runtime destination must have a parent directory.");
        Directory.CreateDirectory(parent);
        var temporary = Path.Combine(parent, $".{Path.GetFileName(destinationDirectory)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await CopyDirectoryAsync(sourceDirectory, temporary, cancellationToken).ConfigureAwait(false);
            try
            {
                Directory.Move(temporary, destinationDirectory);
            }
            catch (IOException) when (Directory.Exists(destinationDirectory))
            {
            }
        }
        finally
        {
            if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true);
        }
    }

    private static async Task CopyDirectoryAsync(string source, string destination, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destinationFile = Path.Combine(destination, Path.GetFileName(file));
            await using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var output = new FileStream(destinationFile, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            var attributes = File.GetAttributes(directory);
            if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
            await CopyDirectoryAsync(directory, Path.Combine(destination, Path.GetFileName(directory)), cancellationToken).ConfigureAwait(false);
        }
    }
}
