namespace Opstrax.Tests;

public sealed class BulkImportPerformanceContractTests
{
    [Fact]
    public void DriverImportsPreloadTenantIdentitiesInsteadOfQueryingInsideRowLoops()
    {
        var source = ReadSource("backend-dotnet", "Controllers", "EndpointMappings.cs");
        foreach (var method in new[] { "DriversImportPreview", "DriversImportCommit" })
        {
            var block = MethodBlock(source, method);
            Assert.Contains("LoadDriverImportIdentities(rows", block, StringComparison.Ordinal);
            Assert.DoesNotContain("ScalarLongAsync", block, StringComparison.Ordinal);
        }
        Assert.Contains("company_id=@companyId", MethodBlock(source, "LoadDriverImportIdentities"), StringComparison.Ordinal);
    }

    [Fact]
    public void DeviceImportsUseOneGlobalPreloadAndOneOrderedBatchLock()
    {
        var source = ReadSource("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var preview = MethodBlock(source, "DevicesImportPreview");
        var commit = MethodBlock(source, "DevicesImportCommit");
        var preload = MethodBlock(source, "LoadExistingDeviceImportIdentities");

        Assert.Contains("LoadExistingDeviceImportIdentities(db, rows", preview, StringComparison.Ordinal);
        Assert.Contains("LoadExistingDeviceImportIdentities", commit, StringComparison.Ordinal);
        Assert.Contains("unnest(@identities::TEXT[])", commit, StringComparison.Ordinal);
        Assert.DoesNotContain("company_id=", preload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ExistingDeviceIdentityCount", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LongRunningDriverAndDeviceCommitsHaveScopedTimeouts()
    {
        var drivers = ReadSource("frontend", "src", "services", "driversApi.ts");
        var devices = ReadSource("frontend", "src", "services", "telematicsService.ts");
        Assert.Contains("/api/drivers/import\", { rows }, { timeout: 120000 }", drivers, StringComparison.Ordinal);
        Assert.Contains("/api/telemetry/devices/import-commit\", { rows }, { timeout: 120000 }", devices, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] parts)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "backend-dotnet"))) root = root.Parent;
        Assert.NotNull(root);
        return File.ReadAllText(Path.Combine([root!.FullName, .. parts]));
    }

    private static string MethodBlock(string source, string name)
    {
        var start = source.IndexOf($" {name}(", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing method {name}");
        var next = source.IndexOf("\n    private static ", start + name.Length, StringComparison.Ordinal);
        return next < 0 ? source[start..] : source[start..next];
    }
}
