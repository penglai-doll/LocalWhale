using LocalWhale.Core.Persistence;
using LocalWhale.Core.Updates;

namespace LocalWhale.Core.Plugins;

public sealed record HarnessContractProfileEntry(
    string HarnessVersion,
    IReadOnlyList<string> FacetApiVersions,
    IReadOnlyList<DshHostContract> Contracts)
{
    public DshHostDescriptor ToHostDescriptor() => new("localwhale", HarnessVersion, FacetApiVersions, Contracts);
}

/// <summary>
/// Adapter table mapping Harness (dsh) versions to the plugin-facing contract coordinates LocalWhale
/// mediates, in the grammar of the dsh-ecosystem-spec host descriptor (vendored under
/// third-party/dsh-ecosystem-spec). When a new Harness release changes the plugin surface, record the
/// delta here; unknown future versions resolve to the newest known entry so a fresh upstream release
/// never blocks plugin evaluation before the table is updated.
/// </summary>
public static class HarnessContractProfile
{
    private static readonly IReadOnlyList<string> FacetApiVersions = ["v1alpha1"];

    // The Cordis --patch loading surface every known Harness build exposes, plus the LocalWhale
    // desktop bridge endpoints (health/shutdown) shipped inside every staged runtime.
    private static readonly IReadOnlyList<DshHostContract> BaseContracts =
    [
        new DshHostContract("cordis.dsh/v1", "Plugin", ["plugin.load"]),
        new DshHostContract("localwhale.bridge/v1", "DesktopBridge", ["bridge.health.read", "bridge.shutdown.invoke"])
    ];

    private static readonly IReadOnlyList<HarnessContractProfileEntry> Entries =
    [
        Entry("0.1.0-rc.6"),
        Entry("0.1.0-rc.7"),
        Entry("0.1.1-rc.2")
    ];

    public static IReadOnlyList<HarnessContractProfileEntry> KnownEntries => Entries;

    /// <summary>All coordinates known to any profile entry; coordinates outside this set cannot be
    /// classified as version drift, so admission treats them as unsupported requirements.</summary>
    public static IReadOnlySet<(string ApiVersion, string Kind)> KnownCoordinates { get; } = new HashSet<(string, string)>(
        Entries.SelectMany(entry => entry.Contracts, (_, contract) => (contract.ApiVersion, contract.Kind)));

    public static HarnessContractProfileEntry Resolve(string harnessVersion)
    {
        var target = SemVersion.Parse(harnessVersion);
        HarnessContractProfileEntry? exact = null;
        HarnessContractProfileEntry? latest = null;
        var latestVersion = SemVersion.Parse(LocalWhalePaths.InitialHarnessVersion);
        foreach (var entry in Entries)
        {
            var version = SemVersion.Parse(entry.HarnessVersion);
            if (version == target) exact = entry;
            if (latest is null || version > latestVersion)
            {
                latest = entry;
                latestVersion = version;
            }
        }

        if (exact is not null) return exact;
        if (target > latestVersion) return latest!;
        return Entries[0];
    }

    private static HarnessContractProfileEntry Entry(string harnessVersion) =>
        new(harnessVersion, FacetApiVersions, BaseContracts);
}
