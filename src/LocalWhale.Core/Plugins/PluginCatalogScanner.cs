using System.Text.Json;

namespace LocalWhale.Core.Plugins;

public sealed record DshPluginScanError(string SourcePath, string Message);

public sealed record PluginCatalogScan(IReadOnlyList<DshPluginManifest> Manifests, IReadOnlyList<DshPluginScanError> Errors);

/// <summary>
/// Discovers installed dsh plugins by locating `dsh-plugin.json` manifests under the user's dsh home
/// (DSH_HOME, default ~/.dsh). The scan is read-only, skips dependency checkouts and caches, and never
/// fails as a whole: unreadable or invalid manifests are reported individually.
/// </summary>
public static class PluginCatalogScanner
{
    private const int MaxManifests = 256;
    private static readonly string[] SkippedDirectoryNames = ["node_modules", ".git", ".cache", "webview2"];

    public static PluginCatalogScan Scan(string dshHomeDirectory)
    {
        if (!Directory.Exists(dshHomeDirectory)) return new PluginCatalogScan([], []);
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        var manifests = new List<DshPluginManifest>();
        var errors = new List<DshPluginScanError>();
        foreach (var path in Directory.EnumerateFiles(dshHomeDirectory, DshPluginManifest.FileName, options))
        {
            if (manifests.Count + errors.Count >= MaxManifests) break;
            if (ContainsSkippedSegment(path)) continue;
            try
            {
                manifests.Add(DshPluginManifest.Parse(File.ReadAllText(path), path));
            }
            catch (Exception exception) when (exception is JsonException or FormatException or IOException)
            {
                errors.Add(new DshPluginScanError(path, exception.Message));
            }
        }

        return new PluginCatalogScan(manifests, errors);
    }

    private static bool ContainsSkippedSegment(string path)
    {
        foreach (var segment in path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            foreach (var skipped in SkippedDirectoryNames)
            {
                if (string.Equals(segment, skipped, StringComparison.OrdinalIgnoreCase)) return true;
            }
        }

        return false;
    }
}
