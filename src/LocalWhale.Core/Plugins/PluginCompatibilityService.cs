namespace LocalWhale.Core.Plugins;

public sealed record PluginCompatibility(
    string HarnessVersion,
    IReadOnlyList<PluginAdmissionResult> Results,
    IReadOnlyList<DshPluginScanError> ScanErrors)
{
    public int CompatibleCount => Results.Count(result => result.State == PluginAdmissionState.Compatible);
    public int DegradedCount => Results.Count(result => result.State == PluginAdmissionState.CompatibleDegraded);
    public int RejectedCount => Results.Count(result => result.State == PluginAdmissionState.Rejected);
    public int UnknownCount => Results.Count(result => result.State == PluginAdmissionState.Unknown);
    public int WaitingAuthorizationCount => Results.Count(result => result.State == PluginAdmissionState.WaitingAuthorization);
    public int TotalPlugins => Results.Count;
}

/// <summary>
/// Evaluates the installed dsh plugins in the user's dsh home against the host descriptor of a Harness
/// version, using the dsh-ecosystem-spec admission states. Used before/after Harness updates so version
/// bumps surface per-plugin compatibility (including graceful degradation via declared fallbacks)
/// instead of silent breakage. Read-only with respect to ~/.dsh.
/// </summary>
public sealed class PluginCompatibilityService
{
    public PluginCompatibility Evaluate(string harnessVersion, string dshHomeDirectory)
    {
        var scan = PluginCatalogScanner.Scan(dshHomeDirectory);
        var host = HarnessContractProfile.Resolve(harnessVersion).ToHostDescriptor();
        var results = scan.Manifests
            .Select(manifest => PluginAdmissionDecider.Decide(manifest, host))
            .OrderBy(result => result.Manifest.Id, StringComparer.Ordinal)
            .ToArray();
        return new PluginCompatibility(harnessVersion, results, scan.Errors);
    }

    /// <summary>One-line Chinese summary for update surfaces; null when no plugin manifests were found.</summary>
    public static string? Describe(PluginCompatibility compatibility)
    {
        if (compatibility.TotalPlugins == 0 && compatibility.ScanErrors.Count == 0) return null;
        var parts = new List<string>();
        if (compatibility.TotalPlugins > 0)
        {
            parts.Add($"已按 dsh-ecosystem-spec 检查 {compatibility.TotalPlugins} 个插件");
            var details = new List<string>();
            if (compatibility.CompatibleCount > 0) details.Add($"{compatibility.CompatibleCount} 兼容");
            if (compatibility.DegradedCount > 0) details.Add($"{compatibility.DegradedCount} 降级运行（可选能力缺失，按声明回退）");
            if (compatibility.RejectedCount > 0) details.Add($"{compatibility.RejectedCount} 不兼容");
            if (compatibility.WaitingAuthorizationCount > 0) details.Add($"{compatibility.WaitingAuthorizationCount} 需授权");
            if (compatibility.UnknownCount > 0) details.Add($"{compatibility.UnknownCount} 无法判定");
            if (details.Count > 0) parts.Add(string.Join("，", details));
        }

        if (compatibility.ScanErrors.Count > 0) parts.Add($"{compatibility.ScanErrors.Count} 个清单读取失败");
        return string.Join("：", parts) + "。";
    }
}
