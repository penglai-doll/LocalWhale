using System.Text.Json;
using LocalWhale.Core.Updates;

namespace LocalWhale.Core.Tests;

public sealed class RuntimePackageDescriptorTests
{
    [Fact]
    public void CreateJson_pins_harness_bridge_and_pnpm()
    {
        using var document = JsonDocument.Parse(RuntimePackageDescriptor.CreateJson("0.1.0-rc.6"));
        var root = document.RootElement;

        Assert.Equal("pnpm@11.7.0", root.GetProperty("packageManager").GetString());
        Assert.Equal("0.1.0-rc.6", root.GetProperty("dependencies").GetProperty("@deepseek-ai/dsh").GetString());
        Assert.Equal("file:./packages/bridge", root.GetProperty("dependencies").GetProperty("@localwhale/dsh-desktop-bridge").GetString());
    }

    [Fact]
    public void CreateWorkspaceYaml_uses_pnpm_11_exact_allowBuilds_matchers()
    {
        var workspace = RuntimePackageDescriptor.CreateWorkspaceYaml();

        Assert.Contains("'node-pty@1.1.0': true", workspace);
        Assert.Contains("'@deepseek-ai/dsh-subprocess-local@0.1.0-rc.6': true", workspace);
        Assert.Contains("'koffi@3.1.4': true", workspace);
        Assert.Contains("nodeLinker: hoisted", workspace);
        Assert.Contains("packageImportMethod: copy", workspace);
        Assert.DoesNotContain("onlyBuiltDependencies", workspace);
    }
}
