using LocalWhale.Core.Runtime;

namespace LocalWhale.Core.Tests;

public sealed class BridgeOverlayMaterializerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "LocalWhale.OverlayTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Create_replaces_the_package_placeholder_with_an_absolute_module_url()
    {
        var template = Path.Combine(_root, "runtime", "desktop-bridge.yml");
        var module = Path.Combine(_root, "runtime", "node_modules", "@localwhale", "dsh-desktop-bridge", "src", "index.js");
        Directory.CreateDirectory(Path.GetDirectoryName(module)!);
        File.WriteAllText(module, "export function apply() {}\n");
        File.WriteAllText(template, "- insert:\n  - id: localwhale-desktop-bridge\n    name: '@localwhale/dsh-desktop-bridge'\n");

        using var overlay = BridgeOverlayMaterializer.Create(template, module);

        var materialized = File.ReadAllText(overlay.Path);
        Assert.Contains(new Uri(module).AbsoluteUri, materialized);
        Assert.DoesNotContain("name: '@localwhale/dsh-desktop-bridge'", materialized);
        Assert.True(File.Exists(overlay.Path));
    }

    [Fact]
    public void Dispose_removes_the_launch_specific_overlay()
    {
        var template = Path.Combine(_root, "runtime", "desktop-bridge.yml");
        var module = Path.Combine(_root, "runtime", "index.js");
        Directory.CreateDirectory(Path.GetDirectoryName(module)!);
        File.WriteAllText(module, "export function apply() {}\n");
        File.WriteAllText(template, "name: '@localwhale/dsh-desktop-bridge'\n");
        var overlay = BridgeOverlayMaterializer.Create(template, module);
        var generatedPath = overlay.Path;

        overlay.Dispose();

        Assert.False(File.Exists(generatedPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
