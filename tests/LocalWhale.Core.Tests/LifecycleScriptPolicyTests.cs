using LocalWhale.Core.Updates;

namespace LocalWhale.Core.Tests;

public sealed class LifecycleScriptPolicyTests
{
    [Fact]
    public void Validate_accepts_an_exact_package_script_and_hash_match()
    {
        const string script = "node scripts/ensure-spawn-helper.mjs";
        var policy = new LifecycleScriptPolicy([
            new AllowedLifecycleScript("@deepseek-ai/dsh-subprocess-local", "postinstall", LifecycleScriptPolicy.ComputeScriptSha256(script))
        ]);

        var result = policy.Validate([new PackageLifecycleScript("@deepseek-ai/dsh-subprocess-local", "0.1.0-rc.6", "postinstall", script)]);

        Assert.True(result.IsCompatible);
        Assert.Empty(result.UnknownScripts);
    }

    [Fact]
    public void Validate_accepts_a_new_package_version_with_the_same_script()
    {
        const string script = "node scripts/ensure-spawn-helper.mjs";
        var policy = new LifecycleScriptPolicy([
            new AllowedLifecycleScript("@deepseek-ai/dsh-subprocess-local", "postinstall", LifecycleScriptPolicy.ComputeScriptSha256(script))
        ]);

        var result = policy.Validate([new PackageLifecycleScript("@deepseek-ai/dsh-subprocess-local", "9.9.9", "postinstall", script)]);

        Assert.True(result.IsCompatible);
        Assert.Empty(result.UnknownScripts);
    }

    [Fact]
    public void Validate_rejects_a_changed_script_even_when_package_matches()
    {
        var policy = new LifecycleScriptPolicy([
            new AllowedLifecycleScript("node-pty", "install", LifecycleScriptPolicy.ComputeScriptSha256("node scripts/install.js"))
        ]);
        var changed = new PackageLifecycleScript("node-pty", "1.1.0", "install", "node scripts/install.js --download arbitrary-url");

        var result = policy.Validate([changed]);

        Assert.False(result.IsCompatible);
        Assert.Equal(changed, Assert.Single(result.UnknownScripts));
    }
}
