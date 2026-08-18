using LocalWhale.Core.Updates;

namespace LocalWhale.Core.Tests;

public sealed class KnownLifecycleScriptsTests
{
    [Fact]
    public void InitialHarness_lifecycle_scripts_match_the_embedded_exact_allowlist()
    {
        var scripts = new[]
        {
            new PackageLifecycleScript("@deepseek-ai/dsh-subprocess-local", "0.1.0-rc.6", "postinstall", "node scripts/ensure-spawn-helper.mjs"),
            new PackageLifecycleScript("@deepseek-ai/dsh-subprocess-local", "0.1.0-rc.7", "postinstall", "node scripts/ensure-spawn-helper.mjs"),
            new PackageLifecycleScript("koffi", "3.1.4", "install", "node ./cnoke.cjs -P . -D src/koffi --prebuild --release"),
            new PackageLifecycleScript("koffi", "3.1.5", "install", "node ./cnoke.cjs -P . -D src/koffi --prebuild --release"),
            new PackageLifecycleScript("node-pty", "1.1.0", "install", "node scripts/prebuild.js || node-gyp rebuild"),
            new PackageLifecycleScript("node-pty", "1.1.0", "postinstall", "node scripts/post-install.js"),
            new PackageLifecycleScript("node-pty", "1.2.0-beta.15", "install", "node scripts/prebuild.js || node-gyp rebuild"),
            new PackageLifecycleScript("node-pty", "1.2.0-beta.15", "postinstall", "node scripts/post-install.js"),
            new PackageLifecycleScript("protobufjs", "7.6.5", "postinstall", "node scripts/postinstall"),
            new PackageLifecycleScript("@google/genai", "1.52.0", "preinstall", "echo 'preinstall: no-op'")
        };

        var result = KnownLifecycleScripts.Policy.Validate(scripts);

        Assert.True(result.IsCompatible);
    }
}
