namespace LocalWhale.Core.Plugins;

/// <summary>A contract coordinate offered by a host, following dsh-ecosystem-spec 0.15 host descriptors.</summary>
public sealed record DshHostContract(string ApiVersion, string Kind, IReadOnlyList<string> Permissions);

/// <summary>
/// Describes what one LocalWhale-provided Harness build offers to dsh plugins. Host descriptors are the
/// host side of the dsh-ecosystem-spec admission handshake: a plugin's declared requirements are matched
/// against this descriptor, so a Harness version bump becomes a data change here instead of a breakage.
/// </summary>
public sealed record DshHostDescriptor(
    string HostId,
    string HostVersion,
    IReadOnlyList<string> FacetApiVersions,
    IReadOnlyList<DshHostContract> Contracts)
{
    public bool Supports(string apiVersion, string kind) =>
        Contracts.Any(contract =>
            string.Equals(contract.ApiVersion, apiVersion, StringComparison.Ordinal) &&
            string.Equals(contract.Kind, kind, StringComparison.Ordinal));

    public IEnumerable<string> AllPermissions => Contracts
        .SelectMany(contract => contract.Permissions)
        .Distinct(StringComparer.Ordinal);
}
