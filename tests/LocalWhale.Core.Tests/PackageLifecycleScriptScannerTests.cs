using LocalWhale.Core.Updates;

namespace LocalWhale.Core.Tests;

public sealed class PackageLifecycleScriptScannerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "LocalWhale.PackageTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Scan_returns_only_install_lifecycle_scripts_with_package_identity()
    {
        var packageDirectory = Path.Combine(_directory, "node_modules", "example");
        Directory.CreateDirectory(packageDirectory);
        File.WriteAllText(Path.Combine(packageDirectory, "package.json"), """
        {
          "name": "example",
          "version": "1.2.3",
          "scripts": {
            "test": "node test.js",
            "preinstall": "node pre.js",
            "postinstall": "node post.js"
          }
        }
        """);

        var scripts = PackageLifecycleScriptScanner.Scan(Path.Combine(_directory, "node_modules"));

        Assert.Equal(2, scripts.Count);
        Assert.Contains(new PackageLifecycleScript("example", "1.2.3", "preinstall", "node pre.js"), scripts);
        Assert.Contains(new PackageLifecycleScript("example", "1.2.3", "postinstall", "node post.js"), scripts);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
