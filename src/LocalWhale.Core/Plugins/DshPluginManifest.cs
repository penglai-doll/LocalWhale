using System.Text.Json;

namespace LocalWhale.Core.Plugins;

/// <summary>A contract requirement declared by a dsh plugin manifest, following dsh-ecosystem-spec 0.15.</summary>
/// <param name="Optional">
/// Per the spec, an optional requirement must carry a <paramref name="Fallback"/> describing the degraded
/// behavior, which is what lets a plugin keep running across host versions that drop the contract.
/// </param>
public sealed record DshPluginContractRequirement(string ApiVersion, string Kind, bool Optional, string? Fallback);

/// <summary>A `dsh-plugin.json` manifest as defined by the dsh community ecosystem interoperability spec.</summary>
public sealed record DshPluginManifest(
    string Id,
    string? Name,
    string PluginVersion,
    string ManifestVersion,
    string HostFacetApiVersion,
    string? HostFacetEntry,
    IReadOnlyList<DshPluginContractRequirement> Contracts,
    IReadOnlyList<string> PermissionNames,
    IReadOnlyList<string> Subscriptions,
    string SourcePath)
{
    public const string FileName = "dsh-plugin.json";
    public const string SupportedManifestVersion = "0.15";

    public static DshPluginManifest Parse(string json, string sourcePath)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var id = RequireString(root, "id", sourcePath);
        var pluginVersion = RequireString(root, "version", sourcePath);
        var manifestVersion = RequireString(root, "manifestVersion", sourcePath);

        if (!root.TryGetProperty("facets", out var facets) ||
            !facets.TryGetProperty("host", out var hostFacet) ||
            !hostFacet.TryGetProperty("apiVersion", out var facetVersionElement))
        {
            throw new FormatException($"{sourcePath}: facets.host.apiVersion is required.");
        }

        var facetApiVersion = facetVersionElement.GetString();
        if (string.IsNullOrWhiteSpace(facetApiVersion))
            throw new FormatException($"{sourcePath}: facets.host.apiVersion must be a non-empty string.");
        string? facetEntry = hostFacet.TryGetProperty("entry", out var entryElement) ? entryElement.GetString() : null;

        var contracts = new List<DshPluginContractRequirement>();
        if (root.TryGetProperty("requires", out var requires) &&
            requires.TryGetProperty("contracts", out var contractsElement) &&
            contractsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in contractsElement.EnumerateArray())
            {
                var apiVersion = RequireString(item, "apiVersion", sourcePath);
                var kind = RequireString(item, "kind", sourcePath);
                var optional = item.TryGetProperty("optional", out var optionalElement) && optionalElement.ValueKind == JsonValueKind.True;
                string? fallback = item.TryGetProperty("fallback", out var fallbackElement) && fallbackElement.ValueKind == JsonValueKind.String
                    ? fallbackElement.GetString()
                    : null;
                contracts.Add(new DshPluginContractRequirement(apiVersion, kind, optional, fallback));
            }
        }

        var permissions = new List<string>();
        if (root.TryGetProperty("permissions", out var permissionsElement) && permissionsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in permissionsElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("name", out var nameElement))
                {
                    var permissionName = nameElement.GetString();
                    if (!string.IsNullOrWhiteSpace(permissionName)) permissions.Add(permissionName);
                }
            }
        }

        var subscriptions = new List<string>();
        if (root.TryGetProperty("subscriptions", out var subscriptionsElement) && subscriptionsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in subscriptionsElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var subscription = item.GetString();
                    if (!string.IsNullOrWhiteSpace(subscription)) subscriptions.Add(subscription);
                }
            }
        }

        var name = root.TryGetProperty("name", out var nameProperty) && nameProperty.ValueKind == JsonValueKind.String
            ? nameProperty.GetString()
            : null;
        return new DshPluginManifest(
            id,
            name,
            pluginVersion,
            manifestVersion,
            facetApiVersion,
            facetEntry,
            contracts,
            permissions,
            subscriptions,
            sourcePath);
    }

    private static string RequireString(JsonElement element, string property, string sourcePath)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
            throw new FormatException($"{sourcePath}: {property} is required and must be a string.");
        var result = value.GetString();
        if (string.IsNullOrWhiteSpace(result)) throw new FormatException($"{sourcePath}: {property} must be a non-empty string.");
        return result;
    }
}
