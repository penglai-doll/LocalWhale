namespace LocalWhale.Core.Runtime;

public static class HarnessStartupRetryPolicy
{
    private const string ModuleNotFoundCode = "ERR_MODULE_NOT_FOUND";
    private const string ImportedFromMarker = "imported from ";

    public static bool ShouldRetryProfileResolution(
        HarnessStartupException exception,
        string dshHomeDirectory)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentException.ThrowIfNullOrWhiteSpace(dshHomeDirectory);

        if (!exception.Output.Any(line => line.Contains(ModuleNotFoundCode, StringComparison.Ordinal)))
        {
            return false;
        }

        var profilesRoot = AppendDirectorySeparator(Path.GetFullPath(Path.Combine(dshHomeDirectory, "profiles")));
        foreach (var line in exception.Output)
        {
            var marker = line.IndexOf(ImportedFromMarker, StringComparison.OrdinalIgnoreCase);
            if (marker < 0) continue;

            var importedFrom = line[(marker + ImportedFromMarker.Length)..].Trim().Trim('"', '\'');
            try
            {
                var importedPath = AppendDirectorySeparator(Path.GetFullPath(importedFrom));
                if (importedPath.StartsWith(profilesRoot, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
            {
            }
        }

        return false;
    }

    private static string AppendDirectorySeparator(string path) =>
        Path.TrimEndingDirectorySeparator(path) + Path.DirectorySeparatorChar;
}
