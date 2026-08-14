using System.Security.Cryptography;
using System.Text;

namespace LocalWhale.Core.Updates;

public sealed record PackageLifecycleScript(string PackageName, string Version, string ScriptName, string Script);

public sealed record AllowedLifecycleScript(string PackageName, string Version, string ScriptName, string ScriptSha256);

public sealed record LifecycleScriptValidation(bool IsCompatible, IReadOnlyList<PackageLifecycleScript> UnknownScripts);

public sealed class LifecycleScriptPolicy(IEnumerable<AllowedLifecycleScript> allowedScripts)
{
    private readonly HashSet<AllowedLifecycleScript> _allowed = new(
        allowedScripts.Select(Normalize),
        AllowedLifecycleScriptComparer.Instance);

    public LifecycleScriptValidation Validate(IEnumerable<PackageLifecycleScript> scripts)
    {
        var unknown = scripts
            .Where(script => !_allowed.Contains(new AllowedLifecycleScript(
                script.PackageName,
                script.Version,
                script.ScriptName,
                ComputeScriptSha256(script.Script))))
            .ToArray();
        return new LifecycleScriptValidation(unknown.Length == 0, unknown);
    }

    public static string ComputeScriptSha256(string script) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(script))).ToLowerInvariant();

    private static AllowedLifecycleScript Normalize(AllowedLifecycleScript script) =>
        script with { ScriptSha256 = script.ScriptSha256.ToLowerInvariant() };

    private sealed class AllowedLifecycleScriptComparer : IEqualityComparer<AllowedLifecycleScript>
    {
        public static AllowedLifecycleScriptComparer Instance { get; } = new();

        public bool Equals(AllowedLifecycleScript? left, AllowedLifecycleScript? right) =>
            left is not null && right is not null &&
            string.Equals(left.PackageName, right.PackageName, StringComparison.Ordinal) &&
            string.Equals(left.Version, right.Version, StringComparison.Ordinal) &&
            string.Equals(left.ScriptName, right.ScriptName, StringComparison.Ordinal) &&
            string.Equals(left.ScriptSha256, right.ScriptSha256, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode(AllowedLifecycleScript value) => HashCode.Combine(
            value.PackageName,
            value.Version,
            value.ScriptName,
            StringComparer.OrdinalIgnoreCase.GetHashCode(value.ScriptSha256));
    }
}
