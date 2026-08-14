using LocalWhale.Core.Updates;

namespace LocalWhale.Core.Tests;

public sealed class SemVersionTests
{
    [Theory]
    [InlineData("0.1.0-rc.6", "0.1.0-rc.5")]
    [InlineData("0.1.0", "0.1.0-rc.99")]
    [InlineData("1.0.0-beta.11", "1.0.0-beta.2")]
    public void Compare_orders_newer_versions_after_older_versions(string newer, string older)
    {
        Assert.True(SemVersion.Parse(newer) > SemVersion.Parse(older));
    }

    [Fact]
    public void Parse_rejects_versions_with_missing_patch_component()
    {
        Assert.Throws<FormatException>(() => SemVersion.Parse("1.2"));
    }
}
