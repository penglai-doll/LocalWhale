using LocalWhale.Core.Updates;

namespace LocalWhale.Core.Tests;

public sealed class PnpmRuntimeInstallerPlanTests
{
    [Fact]
    public void CreatePreValidationPlan_disables_scripts_for_lock_fetch_and_offline_install()
    {
        var plan = PnpmRuntimeInstaller.CreatePreValidationPlan(@"D:\pnpm store");

        Assert.Equal(new[] { "install", "fetch", "install" }, plan.Select(step => step.Arguments[0]));
        Assert.All(plan, step => Assert.Contains("--ignore-scripts", step.Arguments));
        Assert.Contains(plan, step => step.Arguments.Contains("--offline"));
        Assert.Contains(plan, step => step.Arguments.Contains("--lockfile-only"));
    }
}
