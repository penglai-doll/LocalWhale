using System.Text.Json;

namespace LocalWhale.Core.Updates;

public static class PackageLifecycleScriptScanner
{
    private static readonly string[] LifecycleScriptNames = ["preinstall", "install", "postinstall"];

    public static IReadOnlyList<PackageLifecycleScript> Scan(string nodeModulesDirectory)
    {
        if (!Directory.Exists(nodeModulesDirectory)) return Array.Empty<PackageLifecycleScript>();
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        var scripts = new HashSet<PackageLifecycleScript>();
        foreach (var packageJson in Directory.EnumerateFiles(nodeModulesDirectory, "package.json", options))
        {
            using var stream = File.OpenRead(packageJson);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            if (!root.TryGetProperty("name", out var nameElement) ||
                !root.TryGetProperty("version", out var versionElement) ||
                !root.TryGetProperty("scripts", out var scriptsElement) ||
                scriptsElement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var name = nameElement.GetString();
            var version = versionElement.GetString();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(version)) continue;
            foreach (var scriptName in LifecycleScriptNames)
            {
                if (scriptsElement.TryGetProperty(scriptName, out var scriptElement) && scriptElement.ValueKind == JsonValueKind.String)
                {
                    var script = scriptElement.GetString();
                    if (!string.IsNullOrWhiteSpace(script)) scripts.Add(new PackageLifecycleScript(name, version, scriptName, script));
                }
            }
        }

        return scripts.OrderBy(script => script.PackageName, StringComparer.Ordinal)
            .ThenBy(script => script.ScriptName, StringComparer.Ordinal)
            .ToArray();
    }
}
