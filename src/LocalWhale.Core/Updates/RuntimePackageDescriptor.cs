using System.Text.Json;
using System.Text.Json.Serialization;
using LocalWhale.Core.Persistence;

namespace LocalWhale.Core.Updates;

public static class RuntimePackageDescriptor
{
    public static string CreateJson(string harnessVersion)
    {
        _ = SemVersion.Parse(harnessVersion);
        var descriptor = new RuntimePackageJson(
            "localwhale-harness-runtime",
            "1.0.0",
            true,
            "pnpm@11.7.0",
            new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["@deepseek-ai/dsh"] = harnessVersion,
                ["@localwhale/dsh-desktop-bridge"] = "file:./packages/bridge"
            });
        return JsonSerializer.Serialize(descriptor, LocalWhaleJsonContext.Default.RuntimePackageJson);
    }

    public static string CreateWorkspaceYaml() => """
        minimumReleaseAge: 0
        strictDepBuilds: true
        nodeLinker: hoisted
        packageImportMethod: copy
        allowBuilds:
          '@deepseek-ai/dsh-subprocess-local@0.1.0-rc.6': true
          '@google/genai@1.52.0': true
          'koffi@3.1.4': true
          'koffi@3.1.5': true
          'node-pty@1.1.0': true
          'protobufjs@7.6.5': true
        """;
}

public sealed record RuntimePackageJson(
    string Name,
    string Version,
    [property: JsonPropertyName("private")] bool Private,
    string PackageManager,
    SortedDictionary<string, string> Dependencies);
