namespace LocalWhale.Core.Updates;

public static class KnownLifecycleScripts
{
    public static LifecycleScriptPolicy Policy { get; } = new(Create());

    public static IReadOnlySet<string> PackageNames { get; } = new HashSet<string>(
        Create().Select(script => script.PackageName),
        StringComparer.Ordinal);

    // Entries pin the exact script text, not the package version: any future release whose
    // lifecycle scripts are byte-identical to an entry below is approved without a LocalWhale
    // update, while any changed or newly introduced script still fails validation.
    private static IReadOnlyList<AllowedLifecycleScript> Create() =>
    [
        Allow("@deepseek-ai/dsh-subprocess-local", "postinstall", "node scripts/ensure-spawn-helper.mjs"),
        Allow("koffi", "install", "node ./cnoke.cjs -P . -D src/koffi --prebuild --release"),
        Allow("node-pty", "install", "node scripts/prebuild.js || node-gyp rebuild"),
        Allow("node-pty", "postinstall", "node scripts/post-install.js"),
        Allow("protobufjs", "postinstall", "node scripts/postinstall"),
        Allow("@google/genai", "preinstall", "echo 'preinstall: no-op'")
    ];

    private static AllowedLifecycleScript Allow(string packageName, string scriptName, string script) =>
        new(packageName, scriptName, LifecycleScriptPolicy.ComputeScriptSha256(script));
}
