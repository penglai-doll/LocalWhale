using LocalWhale.Core.Plugins;

namespace LocalWhale.Core.Tests;

public sealed class DshPluginManifestTests
{
    private const string ValidManifest = """
        {
          "$schema": "https://dsh.community/schemas/dsh-plugin-0.15.json",
          "id": "com.example.echo",
          "name": "Echo Plugin",
          "version": "0.1.0",
          "manifestVersion": "0.15",
          "facets": { "host": { "entry": "dist/main.js", "apiVersion": "v1alpha1" } },
          "requires": { "contracts": [
            { "apiVersion": "cordis.dsh/v1", "kind": "Plugin" },
            { "apiVersion": "localwhale.bridge/v1", "kind": "DesktopBridge", "optional": true, "fallback": "run without bridge endpoints" }
          ] },
          "permissions": [{ "name": "plugin.load", "scope": "anything", "reason": "load the plugin" }],
          "subscriptions": ["messages.observe"]
        }
        """;

    [Fact]
    public void Parse_reads_the_spec_v0_15_fixture_shape()
    {
        var manifest = DshPluginManifest.Parse(ValidManifest, "dsh-plugin.json");

        Assert.Equal("com.example.echo", manifest.Id);
        Assert.Equal("Echo Plugin", manifest.Name);
        Assert.Equal("0.1.0", manifest.PluginVersion);
        Assert.Equal("0.15", manifest.ManifestVersion);
        Assert.Equal("v1alpha1", manifest.HostFacetApiVersion);
        Assert.Equal("dist/main.js", manifest.HostFacetEntry);
        Assert.Equal(2, manifest.Contracts.Count);
        Assert.False(manifest.Contracts[0].Optional);
        Assert.True(manifest.Contracts[1].Optional);
        Assert.Equal("run without bridge endpoints", manifest.Contracts[1].Fallback);
        Assert.Equal(new[] { "plugin.load" }, manifest.PermissionNames);
        Assert.Equal(new[] { "messages.observe" }, manifest.Subscriptions);
    }

    [Fact]
    public void Parse_accepts_a_manifest_without_requires_or_permissions()
    {
        const string minimal = """
            { "id": "com.example.min", "version": "1.0.0", "manifestVersion": "0.15",
              "facets": { "host": { "apiVersion": "v1alpha1" } } }
            """;

        var manifest = DshPluginManifest.Parse(minimal, "dsh-plugin.json");

        Assert.Empty(manifest.Contracts);
        Assert.Empty(manifest.PermissionNames);
        Assert.Null(manifest.HostFacetEntry);
    }

    [Theory]
    [InlineData("""{ "version": "1.0.0", "manifestVersion": "0.15", "facets": { "host": { "apiVersion": "v1alpha1" } } }""")]
    [InlineData("""{ "id": "a", "version": "1.0.0", "manifestVersion": "0.15" }""")]
    [InlineData("""{ "id": "a", "version": "1.0.0", "manifestVersion": "0.15", "facets": { "host": {} } }""")]
    public void Parse_rejects_manifests_missing_required_fields(string json)
    {
        Assert.Throws<FormatException>(() => DshPluginManifest.Parse(json, "dsh-plugin.json"));
    }
}

public sealed class PluginAdmissionDeciderTests
{
    private static readonly DshHostDescriptor Host = new(
        "localwhale",
        "0.1.1-rc.2",
        ["v1alpha1"],
        [
            new DshHostContract("cordis.dsh/v1", "Plugin", ["plugin.load"]),
            new DshHostContract("localwhale.bridge/v1", "DesktopBridge", ["bridge.health.read"])
        ]);

    private static DshPluginManifest Manifest(
        string manifestVersion = "0.15",
        string facetApiVersion = "v1alpha1",
        IReadOnlyList<DshPluginContractRequirement>? contracts = null,
        IReadOnlyList<string>? permissions = null) =>
        new("com.example.test", null, "1.0.0", manifestVersion, facetApiVersion, null,
            contracts ?? [], permissions ?? [], [], "dsh-plugin.json");

    [Fact]
    public void Decide_reports_compatible_for_a_fully_supported_plugin()
    {
        var manifest = Manifest(
            contracts: [new DshPluginContractRequirement("cordis.dsh/v1", "Plugin", false, null)],
            permissions: ["plugin.load"]);

        var result = PluginAdmissionDecider.Decide(manifest, Host);

        Assert.Equal(PluginAdmissionState.Compatible, result.State);
        Assert.Equal("COMPATIBLE", result.ReasonCode);
    }

    [Fact]
    public void Decide_reports_degraded_when_an_optional_contract_is_missing()
    {
        var host = Host with { Contracts = [Host.Contracts[0]] };
        var manifest = Manifest(
            contracts: [new DshPluginContractRequirement("localwhale.bridge/v1", "DesktopBridge", true, "run headless")]);

        var result = PluginAdmissionDecider.Decide(manifest, host);

        Assert.Equal(PluginAdmissionState.CompatibleDegraded, result.State);
        Assert.Equal("OPTIONAL_PROTOCOL_MISSING", result.ReasonCode);
        Assert.Equal(new[] { "localwhale.bridge/v1#DesktopBridge" }, result.MissingOptionalContracts);
    }

    [Fact]
    public void Decide_rejects_when_a_required_contract_is_missing()
    {
        var host = Host with { Contracts = [Host.Contracts[1]] };
        var manifest = Manifest(
            contracts: [new DshPluginContractRequirement("cordis.dsh/v1", "Plugin", false, null)]);

        var result = PluginAdmissionDecider.Decide(manifest, host);

        Assert.Equal(PluginAdmissionState.Rejected, result.State);
        Assert.Equal("REQUIRED_PROTOCOL_UNAVAILABLE", result.ReasonCode);
    }

    [Fact]
    public void Decide_rejects_an_unavailable_facet_api_version()
    {
        var manifest = Manifest(facetApiVersion: "v2alpha1");

        var result = PluginAdmissionDecider.Decide(manifest, Host);

        Assert.Equal(PluginAdmissionState.Rejected, result.State);
        Assert.Equal("FACET_API_VERSION_UNAVAILABLE", result.ReasonCode);
    }

    [Fact]
    public void Decide_waits_for_authorization_when_a_permission_is_not_offered()
    {
        var manifest = Manifest(permissions: ["fs.write"]);

        var result = PluginAdmissionDecider.Decide(manifest, Host);

        Assert.Equal(PluginAdmissionState.WaitingAuthorization, result.State);
        Assert.Equal("PERMISSION_NOT_GRANTED", result.ReasonCode);
        Assert.Equal(new[] { "fs.write" }, result.DeniedPermissions);
    }

    [Fact]
    public void Decide_reports_unknown_for_a_coordinate_outside_the_known_profile()
    {
        var manifest = Manifest(
            contracts: [new DshPluginContractRequirement("commands.dsh/v9", "Command", false, null)]);

        var result = PluginAdmissionDecider.Decide(manifest, Host);

        Assert.Equal(PluginAdmissionState.Unknown, result.State);
        Assert.Equal("UNKNOWN_PROTOCOL_VERSION", result.ReasonCode);
    }

    [Fact]
    public void Decide_reports_unknown_for_a_future_manifest_version()
    {
        var manifest = Manifest(manifestVersion: "0.16");

        var result = PluginAdmissionDecider.Decide(manifest, Host);

        Assert.Equal(PluginAdmissionState.Unknown, result.State);
        Assert.Equal("UNSUPPORTED_MANIFEST_VERSION", result.ReasonCode);
    }
}

public sealed class HarnessContractProfileTests
{
    [Fact]
    public void Resolve_returns_the_exact_entry_for_a_known_version()
    {
        var entry = HarnessContractProfile.Resolve("0.1.1-rc.2");

        Assert.Equal("0.1.1-rc.2", entry.HarnessVersion);
        Assert.Equal("localwhale", entry.ToHostDescriptor().HostId);
    }

    [Fact]
    public void Resolve_falls_back_to_the_newest_entry_for_a_future_version()
    {
        var entry = HarnessContractProfile.Resolve("99.0.0");

        Assert.Equal("0.1.1-rc.2", entry.HarnessVersion);
    }

    [Fact]
    public void Resolve_falls_back_to_the_oldest_entry_for_a_version_older_than_all_entries()
    {
        var entry = HarnessContractProfile.Resolve("0.1.0-alpha.1");

        Assert.Equal("0.1.0-rc.6", entry.HarnessVersion);
    }
}

public sealed class PluginCatalogScannerTests : IDisposable
{
    private readonly TemporaryTestDirectory _directory = new();

    [Fact]
    public void Scan_collects_manifests_and_errors_and_skips_dependency_trees()
    {
        Directory.CreateDirectory(Path.Combine(_directory.Path, "plugins", "echo"));
        Directory.CreateDirectory(Path.Combine(_directory.Path, "node_modules", "dep"));
        File.WriteAllText(
            Path.Combine(_directory.Path, "plugins", "echo", DshPluginManifest.FileName),
            """{ "id": "com.example.echo", "version": "1.0.0", "manifestVersion": "0.15", "facets": { "host": { "apiVersion": "v1alpha1" } } }""");
        File.WriteAllText(
            Path.Combine(_directory.Path, "node_modules", "dep", DshPluginManifest.FileName),
            """{ "id": "should.be.skipped", "version": "1.0.0", "manifestVersion": "0.15", "facets": { "host": { "apiVersion": "v1alpha1" } } }""");
        File.WriteAllText(
            Path.Combine(_directory.Path, DshPluginManifest.FileName),
            "{ not valid json");

        var scan = PluginCatalogScanner.Scan(_directory.Path);

        var manifest = Assert.Single(scan.Manifests);
        Assert.Equal("com.example.echo", manifest.Id);
        var error = Assert.Single(scan.Errors);
        Assert.EndsWith(DshPluginManifest.FileName, error.SourcePath, StringComparison.Ordinal);
    }

    [Fact]
    public void Scan_returns_empty_for_a_missing_directory()
    {
        var scan = PluginCatalogScanner.Scan(Path.Combine(_directory.Path, "absent"));

        Assert.Empty(scan.Manifests);
        Assert.Empty(scan.Errors);
    }

    public void Dispose() => _directory.Dispose();
}

public sealed class PluginCompatibilityServiceTests
{
    [Fact]
    public void Evaluate_admits_a_compatible_installed_plugin()
    {
        using var directory = new TemporaryTestDirectory();
        Directory.CreateDirectory(Path.Combine(directory.Path, "plugins", "echo"));
        File.WriteAllText(
            Path.Combine(directory.Path, "plugins", "echo", DshPluginManifest.FileName),
            """
            { "id": "com.example.echo", "version": "1.0.0", "manifestVersion": "0.15",
              "facets": { "host": { "apiVersion": "v1alpha1" } },
              "requires": { "contracts": [{ "apiVersion": "cordis.dsh/v1", "kind": "Plugin" }] },
              "permissions": [{ "name": "plugin.load" }] }
            """);

        var compatibility = new PluginCompatibilityService().Evaluate("0.1.1-rc.2", directory.Path);

        Assert.Equal(1, compatibility.TotalPlugins);
        Assert.Equal(1, compatibility.CompatibleCount);
        var summary = PluginCompatibilityService.Describe(compatibility);
        Assert.NotNull(summary);
        Assert.Contains("1 兼容", summary);
    }

    [Fact]
    public void Describe_returns_null_when_no_manifests_exist()
    {
        using var directory = new TemporaryTestDirectory();

        var compatibility = new PluginCompatibilityService().Evaluate("0.1.1-rc.2", directory.Path);

        Assert.Null(PluginCompatibilityService.Describe(compatibility));
    }
}
