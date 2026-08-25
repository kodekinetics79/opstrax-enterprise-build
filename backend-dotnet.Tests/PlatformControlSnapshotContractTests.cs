namespace Opstrax.Tests;

public sealed class PlatformControlSnapshotContractTests
{
    [Fact]
    public void SnapshotRouteIsAuditedHashedRedactedAndCoversEnterpriseControlLayers()
    {
        var backend = Read("backend-dotnet", "Controllers", "PlatformEndpoints.cs");
        Assert.Contains("/api/platform/tenants/{id:long}/control-snapshot", backend, StringComparison.Ordinal);
        Assert.Contains("platform:tenants:view", backend, StringComparison.Ordinal);
        Assert.Contains("tenant.control_snapshot.captured", backend, StringComparison.Ordinal);
        Assert.Contains("snapshotSha256", backend, StringComparison.Ordinal);
        Assert.Contains("effectiveEntitlements", backend, StringComparison.Ordinal);
        Assert.Contains("marketPacks", backend, StringComparison.Ordinal);
        Assert.Contains("featureFlags", backend, StringComparison.Ordinal);
        Assert.Contains("branches", backend, StringComparison.Ordinal);
        Assert.Contains("personas", backend, StringComparison.Ordinal);
        Assert.Contains("effectiveRoleGrants", backend, StringComparison.Ordinal);
        Assert.Contains("userBranchBindings", backend, StringComparison.Ordinal);
        Assert.Contains("OpaqueControlRef", backend, StringComparison.Ordinal);
        Assert.Contains("effectiveGrantSha256", backend, StringComparison.Ordinal);
        Assert.Contains("integrations", backend, StringComparison.Ordinal);
        Assert.Contains("environmentControls", backend, StringComparison.Ordinal);
        Assert.Contains("semanticSha256", backend, StringComparison.Ordinal);
        Assert.Contains("WithoutVolatileControlFields", backend, StringComparison.Ordinal);
        Assert.DoesNotContain("governedUiModules = 45", backend, StringComparison.Ordinal);
        Assert.DoesNotContain("includedCoreUiModules = 46", backend, StringComparison.Ordinal);
        Assert.DoesNotContain("totalUiModules = 91", backend, StringComparison.Ordinal);
        var snapshot = backend[backend.IndexOf("TenantControlSnapshot", StringComparison.Ordinal)..];
        Assert.DoesNotContain("actor_email", snapshot[..snapshot.IndexOf("TenantDelete", StringComparison.Ordinal)], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SELECT config_json", backend[backend.IndexOf("TenantControlSnapshot", StringComparison.Ordinal)..], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PlatformUiOffersAuditedSnapshotDownloadWithDigest()
    {
        var service = Read("frontend", "src", "services", "platformApi.ts");
        var page = Read("frontend", "src", "pages", "platform", "PlatformTenantsPage.tsx");
        Assert.Contains("captureTenantControlSnapshot", service, StringComparison.Ordinal);
        Assert.Contains("Capture audited control snapshot", page, StringComparison.Ordinal);
        Assert.Contains("snapshotSha256", page, StringComparison.Ordinal);
        Assert.Contains("semanticSha256", page, StringComparison.Ordinal);
        Assert.Contains("No semantic control drift", page, StringComparison.Ordinal);
        Assert.Contains("Semantic control drift detected", page, StringComparison.Ordinal);
        Assert.Contains("sessionStorage", page, StringComparison.Ordinal);
        Assert.Contains("application/json", page, StringComparison.Ordinal);
        Assert.Contains("Secrets", page, StringComparison.Ordinal);
        Assert.Contains("actor/user PII are excluded", page, StringComparison.Ordinal);
    }

    [Fact]
    public void ServerCatalogMatchesTheAuthoritativeSpaCatalogAndDerivesOwnershipCounts()
    {
        var spa = Read("frontend", "src", "modules", "moduleConfig.ts");
        var catalog = Read("backend-dotnet", "Services", "PlatformTenantModuleCatalog.cs");
        var spaEntries = System.Text.RegularExpressions.Regex.Matches(spa,
                "\\{ key: \\\"(?<key>[^\\\"]+)\\\".*?route: \\\"[^\\\"]+\\\"(?<tail>.*)$",
                System.Text.RegularExpressions.RegexOptions.Multiline)
            .Select(match => (Key: match.Groups["key"].Value,
                Entitlement: System.Text.RegularExpressions.Regex.Match(match.Groups["tail"].Value,
                    "requiredEntitlement: \\\"(?<value>[^\\\"]+)\\\"").Groups["value"].Value))
            .ToArray();
        var serverEntries = System.Text.RegularExpressions.Regex.Matches(catalog,
                "new\\(\\\"(?<key>[^\\\"]+)\\\", (?:(?:\\\"(?<entitlement>[^\\\"]+)\\\")|null)\\)")
            .Select(match => (Key: match.Groups["key"].Value, Entitlement: match.Groups["entitlement"].Value))
            .ToArray();

        Assert.Equal(spaEntries, serverEntries);
        Assert.Equal(92, serverEntries.Length);
        Assert.Equal(45, serverEntries.Count(entry => entry.Entitlement.Length > 0));
        Assert.Equal(47, serverEntries.Count(entry => entry.Entitlement.Length == 0));
    }

    private static string Read(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "backend-dotnet")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray()));
    }
}
