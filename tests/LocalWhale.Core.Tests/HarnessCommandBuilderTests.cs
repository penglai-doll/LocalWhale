using LocalWhale.Core.Runtime;

namespace LocalWhale.Core.Tests;

public sealed class HarnessCommandBuilderTests
{
    [Fact]
    public void Build_preserves_paths_with_spaces_and_chinese_characters_as_distinct_arguments()
    {
        var spec = new HarnessLaunchSpec(
            @"D:\应用 程序\node.exe",
            @"D:\鲸鱼 Runtime\node_modules\@deepseek-ai\dsh\lib\bin.js",
            @"D:\鲸鱼 Runtime\desktop-bridge.yml",
            @"D:\项目 空间",
            "secret");

        var startInfo = HarnessCommandBuilder.Build(spec);

        Assert.Equal(spec.NodeExecutable, startInfo.FileName);
        Assert.Equal(
            new[] { spec.DshBinPath, "web", "--patch", spec.PatchPath, "--host", "127.0.0.1", "--port", "0" },
            startInfo.ArgumentList);
        Assert.Equal(spec.WorkingDirectory, startInfo.WorkingDirectory);
        Assert.Equal("secret", startInfo.Environment[HarnessEnvironment.BridgeToken]);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.CreateNoWindow);
    }
}
