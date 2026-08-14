using LocalWhale.Core.Runtime;

namespace LocalWhale.Core.Tests;

public sealed class RuntimeBootstrapperTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "LocalWhale.BootstrapTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task EnsureSeededAsync_copies_the_bundled_runtime_once_without_overwriting_user_runtime_files()
    {
        var source = Path.Combine(_directory, "source");
        var destination = Path.Combine(_directory, "destination");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "manifest.json"), "bundled", TestContext.Current.CancellationToken);
        await RuntimeBootstrapper.EnsureSeededAsync(source, destination, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(destination, "manifest.json"), "user-runtime", TestContext.Current.CancellationToken);

        await RuntimeBootstrapper.EnsureSeededAsync(source, destination, TestContext.Current.CancellationToken);

        Assert.Equal("user-runtime", await File.ReadAllTextAsync(Path.Combine(destination, "manifest.json"), TestContext.Current.CancellationToken));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
