using Opstrax.Api.Controllers;

namespace Opstrax.Tests;

public sealed class Module1ImportBranchOwnershipTests
{
    [Fact]
    public void BranchResolutionIsFailClosedAndPreservesScopedDefault()
    {
        IReadOnlyDictionary<string, long> branches = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
        {
            ["CL-HQ"] = 11,
            ["NE-HUB"] = 12,
        };

        Assert.Equal((11L, (string?)null), EndpointMappings.ResolveImportBranch(null, 11, branches));
        Assert.Equal((12L, (string?)null), EndpointMappings.ResolveImportBranch("ne-hub", null, branches));

        var tenantWideMissing = EndpointMappings.ResolveImportBranch(null, null, branches);
        Assert.Null(tenantWideMissing.BranchId);
        Assert.Contains("required for tenant-wide", tenantWideMissing.Error, StringComparison.Ordinal);

        var outsideScope = EndpointMappings.ResolveImportBranch("NE-HUB", 11, branches);
        Assert.Null(outsideScope.BranchId);
        Assert.Contains("outside the authorized branch", outsideScope.Error, StringComparison.Ordinal);

        var inactiveOrForeign = EndpointMappings.ResolveImportBranch("UNKNOWN", null, branches);
        Assert.Null(inactiveOrForeign.BranchId);
        Assert.Contains("not an active branch in this tenant", inactiveOrForeign.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void BranchCatalogIsOneTenantScopedSetQuery()
    {
        var source = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var loader = Block(source, "internal static async Task<IReadOnlyDictionary<string, long>> LoadActiveImportBranchMap(", "internal static (long? BranchId, string? Error) ResolveImportBranch(");

        Assert.Contains("company_id=@companyId", loader, StringComparison.Ordinal);
        Assert.Contains("deleted_at IS NULL", loader, StringComparison.Ordinal);
        Assert.Contains("status='Active'", loader, StringComparison.Ordinal);
        Assert.Contains("ANY(@codes)", loader, StringComparison.Ordinal);
        Assert.Equal(1, Count(loader, "QueryAsync("));
    }

    [Fact]
    public void VehicleDriverAndDeviceImportsResolveAndPersistEachRowsBranch()
    {
        var source = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        foreach (var method in new[]
                 {
                     "VehiclesImportPreview", "VehiclesImportCommit",
                     "DriversImportPreview", "DriversImportCommit",
                     "DevicesImportPreview", "DevicesImportCommit"
                 })
        {
            var block = MethodBlock(source, method);
            Assert.Contains("LoadActiveImportBranchMap", block, StringComparison.Ordinal);
            Assert.Contains("ResolveImportBranch", block, StringComparison.Ordinal);
        }

        Assert.Contains("vehicleCode,branchCode,", MethodBlock(source, "VehiclesImportTemplate"), StringComparison.Ordinal);
        Assert.Contains("driverCode,branchCode,", MethodBlock(source, "DriversImportTemplate"), StringComparison.Ordinal);
        Assert.Contains("deviceSerial,branchCode,", MethodBlock(source, "DevicesImportTemplate"), StringComparison.Ordinal);
        Assert.Contains("rowBranchId!.Value", MethodBlock(source, "VehiclesImportCommit"), StringComparison.Ordinal);
        Assert.Contains("rowBranchId!.Value", MethodBlock(source, "DriversImportCommit"), StringComparison.Ordinal);
        Assert.Contains("candidate.BranchId!.Value", MethodBlock(source, "DevicesImportCommit"), StringComparison.Ordinal);
    }

    [Fact]
    public void ReturnableAssetImportUsesBranchAwareSetLookups()
    {
        var source = Read("backend-dotnet", "Controllers", "FleetTmsColdChainEndpoints.cs");
        var loader = MethodBlock(source, "LoadAssetImportLookups");
        var preview = MethodBlock(source, "AssetsImportPreview");
        var commit = MethodBlock(source, "AssetsImportCommit");

        Assert.Contains("assetTag,branchCode,", MethodBlock(source, "AssetsImportTemplate"), StringComparison.Ordinal);
        Assert.Contains("LoadActiveImportBranchMap", loader, StringComparison.Ordinal);
        Assert.Contains("lower(btrim(code)) = ANY(@codes)", loader, StringComparison.Ordinal);
        Assert.Contains("lower(btrim(asset_tag)) = ANY(@tags)", loader, StringComparison.Ordinal);
        Assert.Contains("ResolveImportBranch", preview, StringComparison.Ordinal);
        Assert.Contains("ResolveImportBranch", commit, StringComparison.Ordinal);
        Assert.Contains("rowBranchId!.Value", commit, StringComparison.Ordinal);
        Assert.DoesNotContain("ScalarLongAsync", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("ScalarLongAsync", commit, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationFleetMasterCsvsCarryBranchOwnership()
    {
        var inputRoot = Path.Combine(RepoRoot, "artifacts", "cert-large-20260825", "input");
        var files = new[] { "vehicles", "drivers", "devices", "assets" }
            .SelectMany(prefix => Directory.GetFiles(inputRoot, $"{prefix}_*.csv"))
            .Where(path => !Path.GetFileName(path).Contains("_b1313ed5", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(28, files.Length);
        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file);
            Assert.True(lines.Length > 1, $"{file} must contain data rows");
            Assert.Equal("branchCode", lines[0].Split(',')[1]);
            Assert.All(lines.Skip(1), line => Assert.False(
                string.IsNullOrWhiteSpace(line.Split(',')[1]),
                $"{file} contains a row without branchCode"));
        }
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        for (var offset = 0; (offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0; offset += value.Length)
            count++;
        return count;
    }

    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([RepoRoot, .. parts]));

    private static string MethodBlock(string source, string name)
    {
        var start = source.IndexOf($" {name}(", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing method {name}");
        var next = source.IndexOf("\n    private static ", start + name.Length, StringComparison.Ordinal);
        return next < 0 ? source[start..] : source[start..next];
    }

    private static string Block(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return source[start..end];
    }
}
