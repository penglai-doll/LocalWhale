using LocalWhale.Core.Persistence;

namespace LocalWhale.Core.Tests;

public sealed class LocalWhalePathsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "LocalWhale.PathsTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void ResolveHarnessDirectory_uses_the_portable_bundled_initial_runtime_without_copying_pnpm_links()
    {
        var paths = new LocalWhalePaths(Path.Combine(_root, "install"), Path.Combine(_root, "data"));
        Directory.CreateDirectory(paths.BundledHarnessDirectory(LocalWhalePaths.InitialHarnessVersion));

        var resolved = paths.ResolveHarnessDirectory(LocalWhalePaths.InitialHarnessVersion);

        Assert.Equal(paths.BundledHarnessDirectory(LocalWhalePaths.InitialHarnessVersion), resolved);
        Assert.False(Directory.Exists(paths.InstalledHarnessDirectory(LocalWhalePaths.InitialHarnessVersion)));
    }

    [Fact]
    public void ResolveHarnessDirectory_prefers_a_validated_local_runtime()
    {
        var paths = new LocalWhalePaths(Path.Combine(_root, "install"), Path.Combine(_root, "data"));
        Directory.CreateDirectory(paths.BundledHarnessDirectory(LocalWhalePaths.InitialHarnessVersion));
        Directory.CreateDirectory(paths.InstalledHarnessDirectory("0.2.0"));

        Assert.Equal(paths.InstalledHarnessDirectory("0.2.0"), paths.ResolveHarnessDirectory("0.2.0"));
    }

    [Fact]
    public void PnpmScript_points_to_the_javascript_distribution_that_shares_bundled_node()
    {
        var paths = new LocalWhalePaths(Path.Combine(_root, "install"), Path.Combine(_root, "data"));

        Assert.EndsWith(Path.Combine("runtime", "pnpm", "bin", "pnpm.cjs"), paths.PnpmScript, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
