namespace LocalWhale.Core.Plugins;

public enum PluginAdmissionState
{
    Compatible,
    CompatibleDegraded,
    WaitingAuthorization,
    Rejected,
    Unknown
}

public sealed record PluginAdmissionResult(
    DshPluginManifest Manifest,
    PluginAdmissionState State,
    string ReasonCode,
    IReadOnlyList<string> MissingOptionalContracts,
    IReadOnlyList<string> DeniedPermissions,
    IReadOnlyList<string> UnknownContracts);

/// <summary>
/// Port of the five-state admission decision from the dsh-ecosystem-spec conformance core
/// (conformance/tests/admission-core.js), reduced to the contract-coordinate and permission checks
/// LocalWhale performs offline: a plugin is compatible, compatible-degraded (optional contracts
/// missing, declared fallback applies), waiting for authorization (permissions the host does not
/// offer), rejected (facet or required contract unavailable), or unknown (version drift the host
/// cannot classify). The states let a Harness update describe its effect on installed plugins
/// instead of failing them silently.
/// </summary>
public static class PluginAdmissionDecider
{
    public static PluginAdmissionResult Decide(DshPluginManifest manifest, DshHostDescriptor host)
    {
        if (!string.Equals(manifest.ManifestVersion, DshPluginManifest.SupportedManifestVersion, StringComparison.Ordinal))
        {
            return new PluginAdmissionResult(
                manifest, PluginAdmissionState.Unknown, "UNSUPPORTED_MANIFEST_VERSION", [], [], []);
        }

        var unknownContracts = manifest.Contracts
            .Where(requirement => !HarnessContractProfile.KnownCoordinates.Contains((requirement.ApiVersion, requirement.Kind)))
            .Select(requirement => $"{requirement.ApiVersion}#{requirement.Kind}")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (unknownContracts.Length > 0)
        {
            return new PluginAdmissionResult(
                manifest, PluginAdmissionState.Unknown, "UNKNOWN_PROTOCOL_VERSION", [], [], unknownContracts);
        }

        if (!host.FacetApiVersions.Contains(manifest.HostFacetApiVersion, StringComparer.Ordinal))
        {
            return new PluginAdmissionResult(
                manifest, PluginAdmissionState.Rejected, "FACET_API_VERSION_UNAVAILABLE", [], [], []);
        }

        var missingRequired = manifest.Contracts
            .Where(requirement => !requirement.Optional && !host.Supports(requirement.ApiVersion, requirement.Kind))
            .Select(requirement => $"{requirement.ApiVersion}#{requirement.Kind}")
            .ToArray();
        if (missingRequired.Length > 0)
        {
            return new PluginAdmissionResult(
                manifest, PluginAdmissionState.Rejected, "REQUIRED_PROTOCOL_UNAVAILABLE", [], [], []);
        }

        var missingOptional = manifest.Contracts
            .Where(requirement => requirement.Optional && !host.Supports(requirement.ApiVersion, requirement.Kind))
            .Select(requirement => $"{requirement.ApiVersion}#{requirement.Kind}")
            .ToArray();

        var hostPermissions = new HashSet<string>(host.AllPermissions, StringComparer.Ordinal);
        var deniedPermissions = manifest.PermissionNames
            .Where(permission => !hostPermissions.Contains(permission))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (deniedPermissions.Length > 0)
        {
            return new PluginAdmissionResult(
                manifest, PluginAdmissionState.WaitingAuthorization, "PERMISSION_NOT_GRANTED", missingOptional, deniedPermissions, []);
        }

        return new PluginAdmissionResult(
            manifest,
            missingOptional.Length > 0 ? PluginAdmissionState.CompatibleDegraded : PluginAdmissionState.Compatible,
            missingOptional.Length > 0 ? "OPTIONAL_PROTOCOL_MISSING" : "COMPATIBLE",
            missingOptional,
            [],
            []);
    }
}
