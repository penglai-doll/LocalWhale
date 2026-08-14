using System.Diagnostics;

namespace LocalWhale.Core.Runtime;

public static class TemporaryDirectoryCleaner
{
    public static void DeleteTreeWithin(string allowedRoot, string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(allowedRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);

        var fullRoot = Path.GetFullPath(allowedRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullTarget = Path.GetFullPath(target)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var requiredPrefix = fullRoot + Path.DirectorySeparatorChar;
        if (!fullTarget.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Cleanup target '{fullTarget}' is outside its allowed parent '{fullRoot}'.");
        }

        if (!Directory.Exists(fullTarget)) return;

        try
        {
            Directory.Delete(fullTarget, recursive: true);
            return;
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException)
        {
            ResetWindowsAccessControl(fullTarget);
        }

        ClearReadOnlyAttributes(fullTarget);
        Directory.Delete(fullTarget, recursive: true);
    }

    private static void ResetWindowsAccessControl(string path)
    {
        if (!OperatingSystem.IsWindows()) return;

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "icacls.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            ArgumentList = { path, "/reset", "/T", "/C", "/Q" }
        }) ?? throw new InvalidOperationException("Could not start icacls.exe for temporary-directory cleanup.");

        if (!process.WaitForExit(TimeSpan.FromSeconds(30)))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"Timed out while resetting access control on '{path}'.");
        }

        if (process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd().Trim();
            throw new IOException($"icacls.exe could not reset access control on '{path}' (exit {process.ExitCode}): {error}");
        }
    }

    private static void ClearReadOnlyAttributes(string root)
    {
        foreach (var path in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories))
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            }
        }

        var rootAttributes = File.GetAttributes(root);
        if ((rootAttributes & FileAttributes.ReadOnly) != 0)
        {
            File.SetAttributes(root, rootAttributes & ~FileAttributes.ReadOnly);
        }
    }
}
