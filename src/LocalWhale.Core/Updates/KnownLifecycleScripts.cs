namespace LocalWhale.Core.Updates;

public static class KnownLifecycleScripts
{
    public static LifecycleScriptPolicy Policy { get; } = new(Create());

    public static IReadOnlySet<string> PackageNames { get; } = new HashSet<string>(
        Create().Select(script => script.PackageName),
        StringComparer.Ordinal);

    private static IReadOnlyList<AllowedLifecycleScript> Create() =>
    [
        Allow("@deepseek-ai/dsh-subprocess-local", "0.1.0-rc.6", "postinstall", "node scripts/ensure-spawn-helper.mjs"),
        Allow("@deepseek-ai/dsh-subprocess-local", "0.1.0-rc.7", "postinstall", "node scripts/ensure-spawn-helper.mjs"),
        Allow("koffi", "3.1.4", "install", "node ./cnoke.cjs -P . -D src/koffi --prebuild --release"),
        Allow("koffi", "3.1.5", "install", "node ./cnoke.cjs -P . -D src/koffi --prebuild --release"),
        Allow("node-pty", "1.1.0", "install", "node scripts/prebuild.js || node-gyp rebuild"),
        Allow("node-pty", "1.1.0", "postinstall", "node scripts/post-install.js"),
        Allow("node-pty", "1.2.0-beta.15", "install", "node scripts/prebuild.js || node-gyp rebuild"),
        Allow("node-pty", "1.2.0-beta.15", "postinstall", "node scripts/post-install.js"),
        Allow("protobufjs", "7.6.5", "postinstall", "node scripts/postinstall"),
        Allow("@google/genai", "1.52.0", "preinstall", "echo 'preinstall: no-op'")
    ];

    private static AllowedLifecycleScript Allow(string packageName, string version, string scriptName, string script) =>
        new(packageName, version, scriptName, LifecycleScriptPolicy.ComputeScriptSha256(script));
}
