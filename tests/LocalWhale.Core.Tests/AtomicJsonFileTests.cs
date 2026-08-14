using LocalWhale.Core.Models;
using LocalWhale.Core.Persistence;

namespace LocalWhale.Core.Tests;

public sealed class AtomicJsonFileTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "LocalWhale.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveAsync_replaces_existing_json_without_leaving_a_temp_file()
    {
        var path = Path.Combine(_directory, "settings.json");
        var store = new AtomicJsonFile<RuntimeManifest>(path);
        var first = new RuntimeManifest("0.1.0", "24.18.1", "11.7.0", 1, "aaa", DateTimeOffset.UnixEpoch);
        var second = new RuntimeManifest("0.2.0", "24.18.1", "11.7.0", 1, "bbb", DateTimeOffset.UnixEpoch.AddDays(1));

        await store.SaveAsync(first, TestContext.Current.CancellationToken);
        await store.SaveAsync(second, TestContext.Current.CancellationToken);

        Assert.Equal(second, await store.LoadAsync(TestContext.Current.CancellationToken));
        Assert.Empty(Directory.EnumerateFiles(_directory, "*.tmp"));
    }

    [Fact]
    public async Task LoadAsync_returns_default_value_when_file_does_not_exist()
    {
        var store = new AtomicJsonFile<RuntimeManifest>(Path.Combine(_directory, "missing.json"));

        Assert.Null(await store.LoadAsync(TestContext.Current.CancellationToken));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
