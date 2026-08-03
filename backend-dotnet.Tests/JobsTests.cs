using System.IO;
using Opstrax.Api.Controllers;
using Xunit;

namespace Opstrax.Tests;

// Jobs module contract checks.
// These tests document the live seed and RBAC baseline that the frontend now
// depends on after removing the demo fallback path.

public class JobsSeedPermissionTests
{
    [Fact]
    public void DispatcherSeed_Includes_JobsView_And_JobsManage()
    {
        var seed = ReadSeedSql();
        Assert.Contains("dispatch:view','dispatch:manage'", seed);
        Assert.Contains("jobs:view','jobs:manage'", seed);
    }

    [Fact]
    public void DriverSeed_Includes_JobsView_But_Not_JobsManage()
    {
        var seed = ReadSeedSql();
        Assert.Contains("driver:portal','jobs:view','dvir:manage'", seed);
        Assert.DoesNotContain("driver:portal','jobs:view','jobs:manage'", seed);
    }

    [Fact]
    public void JobsDeleteRoute_Requires_DispatchManage()
    {
        Assert.Contains("*", EndpointMappings.RolePermissionDefaults["Super Admin"]);
        Assert.Contains("*", EndpointMappings.RolePermissionDefaults["Company Admin"]);
        Assert.DoesNotContain("dispatch:manage", EndpointMappings.RolePermissionDefaults["Dispatcher"]);
    }

    [Theory]
    [InlineData("Unassigned", "Assigned")]
    [InlineData("Unassigned", "Cancelled")]
    [InlineData("Assigned", "En Route")]
    [InlineData("En Route", "At Stop")]
    [InlineData("At Stop", "Completed")]
    [InlineData("Completed", "Delivered")]
    [InlineData("Exception", "Cancelled")]
    public void JobLifecycle_AllowsDocumentedForwardAndRecoveryMoves(string from, string to)
        => Assert.True(EndpointMappings.IsValidJobTransition(from, to));

    [Theory]
    [InlineData("Unassigned", "Delivered")]
    [InlineData("Assigned", "Completed")]
    [InlineData("En Route", "Assigned")]
    [InlineData("Completed", "Cancelled")]
    [InlineData("Delivered", "Assigned")]
    [InlineData("Cancelled", "Assigned")]
    [InlineData("Assigned", "Assigned")]
    public void JobLifecycle_RejectsSkippedBackwardTerminalAndDuplicateMoves(string from, string to)
        => Assert.False(EndpointMappings.IsValidJobTransition(from, to));

    [Fact]
    public void JobsWriteSurfaces_ArePermissionGatedAndTransactional()
    {
        var source = ReadSource("backend-dotnet", "Controllers", "EndpointMappings.cs");
        foreach (var method in new[] { "AssignJob", "ChangeJobStatus", "SendEta", "CreateProofPlaceholder", "CaptureProof", "ArchiveJob" })
        {
            var block = MethodBlock(source, method);
            Assert.Contains("RequireAnyDirectPermission(http", block, StringComparison.Ordinal);
            Assert.Contains("RunInTenantTransactionAsync", block, StringComparison.Ordinal);
        }
        Assert.Contains("RequireAnyDirectPermission(http", MethodBlock(source, "JobsImportPreview"), StringComparison.Ordinal);
    }

    [Fact]
    public void JobsFrontend_UsesFullDatasetFiltersExportAndTerminalStateMachine()
    {
        var page = ReadSource("frontend", "src", "pages", "JobsPage.tsx");
        var api = ReadSource("frontend", "src", "services", "jobsApi.ts");
        var modules = ReadSource("frontend", "src", "modules", "moduleConfig.ts");
        Assert.Contains("status, priority, jobsOffset", page, StringComparison.Ordinal);
        Assert.Contains("downloadServerExport(jobsApi.exportPath", page, StringComparison.Ordinal);
        Assert.Contains("exportPath({ search: query, status, priority })", page, StringComparison.Ordinal);
        Assert.Contains("jobsApi.assign(id, payload)", page, StringComparison.Ordinal);
        Assert.Contains("Request authorized soft-rule override", page, StringComparison.Ordinal);
        Assert.Contains("return flow[current] ?? []", page, StringComparison.Ordinal);
        Assert.Contains("filters.set(\"status\"", api, StringComparison.Ordinal);
        Assert.Contains("exportPath: (opts?: JobListOptions)", api, StringComparison.Ordinal);
        Assert.Contains("requiredPermission: \"shipments:view\"", modules, StringComparison.Ordinal);
    }

    private static string ReadSeedSql()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "database", "init", "002_seed.sql")))
        {
            root = root.Parent;
        }

        if (root is null)
            throw new FileNotFoundException("Could not locate database/init/002_seed.sql from test output directory");

        var path = Path.Combine(root.FullName, "database", "init", "002_seed.sql");
        return File.ReadAllText(path);
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
