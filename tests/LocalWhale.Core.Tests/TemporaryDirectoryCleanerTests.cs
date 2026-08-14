using LocalWhale.Core.Runtime;

namespace LocalWhale.Core.Tests;

public sealed class TemporaryDirectoryCleanerTests : IDisposable
{
    private readonly string _allowedRoot = Path.Combine(Path.GetTempPath(), "LocalWhale.CleanupTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void DeleteTreeWithin_removes_a_nested_tree()
    {
        var target = Path.Combine(_allowedRoot, "candidate");
        Directory.CreateDirectory(Path.Combine(target, "nested"));
        File.WriteAllText(Path.Combine(target, "nested", "payload.txt"), "temporary");

        TemporaryDirectoryCleaner.DeleteTreeWithin(_allowedRoot, target);

        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public void DeleteTreeWithin_refuses_the_allowlisted_root_itself()
    {
        Directory.CreateDirectory(_allowedRoot);

        var error = Assert.Throws<InvalidOperationException>(() =>
            TemporaryDirectoryCleaner.DeleteTreeWithin(_allowedRoot, _allowedRoot));

        Assert.Contains("outside its allowed parent", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeleteTreeWithin_refuses_a_sibling_with_a_common_prefix()
    {
        var sibling = _allowedRoot + "-sibling";

        Assert.Throws<InvalidOperationException>(() =>
            TemporaryDirectoryCleaner.DeleteTreeWithin(_allowedRoot, sibling));
    }

    public void Dispose()
    {
        if (Directory.Exists(_allowedRoot)) Directory.Delete(_allowedRoot, recursive: true);
    }
}
