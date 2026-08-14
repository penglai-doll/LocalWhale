using LocalWhale.Core.Runtime;

namespace LocalWhale.Core.Tests;

public sealed class HarnessStartupRetryPolicyTests
{
    [Theory]
    [InlineData(true, "ERR_MODULE_NOT_FOUND", "profiles\\web")]
    [InlineData(false, "ERR_MODULE_NOT_FOUND", "unrelated\\plugins")]
    [InlineData(false, "EADDRINUSE", "profiles\\web")]
    [InlineData(false, "ERR_MODULE_NOT_FOUND", "profiles-evil\\web")]
    public void ShouldRetryProfileResolution_matches_only_the_known_profile_failure(
        bool expected,
        string error,
        string importRoot)
    {
        var dshHome = Path.Combine(Path.GetTempPath(), "LocalWhale.Policy.User", ".dsh");
        var line = $"{error}: imported from {Path.Combine(dshHome, importRoot)}";
        var exception = new HarnessStartupException(1, new[] { line });

        Assert.Equal(expected, HarnessStartupRetryPolicy.ShouldRetryProfileResolution(exception, dshHome));
    }

    [Fact]
    public void ShouldRetryProfileResolution_accepts_diagnostic_and_import_path_on_separate_lines()
    {
        var dshHome = Path.Combine(Path.GetTempPath(), "LocalWhale.Policy.Split", ".dsh");
        var exception = new HarnessStartupException(1, new[]
        {
            "Error [ERR_MODULE_NOT_FOUND]: Cannot find package '@deepseek-ai/dsh-client-ui-plan'",
            $"imported from {Path.Combine(dshHome, "profiles", "web")}",
        });

        Assert.True(HarnessStartupRetryPolicy.ShouldRetryProfileResolution(exception, dshHome));
    }
}
