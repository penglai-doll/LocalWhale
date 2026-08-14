using LocalWhale.Core.Persistence;
using LocalWhale.Core.Updates;

namespace LocalWhale.Core.Tests;

public sealed class PnpmCommandBuilderTests
{
    [Fact]
    public void Build_reuses_the_pinned_node_runtime_for_the_pnpm_javascript_distribution()
    {
        var paths = new LocalWhalePaths(@"D:\应用 LocalWhale", @"D:\数据 LocalWhale");

        var startInfo = PnpmCommandBuilder.Build(paths, @"D:\候选 Runtime", ["fetch", "--offline"]);

        Assert.Equal(paths.NodeExecutable, startInfo.FileName);
        Assert.Equal(new[] { paths.PnpmScript, "fetch", "--offline" }, startInfo.ArgumentList);
        Assert.StartsWith(Path.GetDirectoryName(paths.NodeExecutable)!, startInfo.Environment["PATH"], StringComparison.OrdinalIgnoreCase);
        Assert.True(startInfo.CreateNoWindow);
        Assert.False(startInfo.UseShellExecute);
    }
}
