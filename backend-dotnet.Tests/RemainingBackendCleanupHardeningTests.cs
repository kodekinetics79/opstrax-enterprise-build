using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Opstrax.Api.Controllers;

namespace Opstrax.Tests;

public sealed class RemainingBackendCleanupHardeningTests
{
    private static readonly string[] WrapperGuardedHandlers =
    [
        "CreateCustomer", "UpdateCustomer", "CreateAsset", "UpdateAsset", "AssignAsset",
        "CreateMaintenance", "UpdateMaintenance", "MaintenanceSchedule", "MaintenanceDefer",
        "MaintenanceCreateWorkOrder", "CreateWorkOrder", "UpdateWorkOrder", "WorkOrderAssign",
        "WorkOrderStatus", "WorkOrderAddLabor", "WorkOrderAddPart", "WorkOrderComplete",
        "WorkOrderApproveCost", "CreateDashcamEvent", "UpdateDashcamEvent", "SafetyCreateIncident",
        "CreateIncident", "UpdateIncident", "IncidentStatus", "IncidentAttachEvidence",
        "IncidentCreateInsuranceReport", "CreateEvidencePackage", "UpdateEvidencePackage",
        "DeleteEvidencePackage", "EvidenceExport", "EvidenceLock", "CreateFuelTransaction",
        "UpdateFuelTransaction", "CreateIdlingEvent", "UpdateIdlingEvent", "FuelAnomalyReview",
        "CustomerVisibilityShipments", "CustomerVisibilityShipmentDetail", "CustomerVisibilityInsights",
        "DriverMe", "DriverAssignments", "DriverCurrentAssignment", "DriverCoachingTasks", "DriverHos",
        "DriverEarnings", "DriverStatementDetail",
    ];

    [Fact]
    public void WrapperGuardedHandlers_AlsoEnforceAuthorizationInsideTheHandler()
    {
        var source = Endpoints();

        foreach (var handler in WrapperGuardedHandlers)
        {
            var body = MethodBody(source, handler);
            Assert.True(
                body.Contains("RequirePermission(http", StringComparison.Ordinal)
                || body.Contains("RequireAnyDirectPermission(http", StringComparison.Ordinal),
                $"{handler} must enforce authorization locally instead of relying only on route mapping.");
        }

        foreach (var handler in new[] { "CustomerVisibilityShipments", "CustomerVisibilityShipmentDetail", "CustomerVisibilityInsights" })
            Assert.Contains("RequireInternalUser(http", MethodBody(source, handler), StringComparison.Ordinal);
    }

    [Fact]
    public void ModuleRegistry_NoLongerCarriesDormantUnscopedReadSql()
    {
        var source = Endpoints();
        var start = source.IndexOf("Dictionary<string, ModuleDefinition> ModuleDefinitions", StringComparison.Ordinal);
        var end = source.IndexOf("Dictionary<string, string> ModuleReadPermissionByKey", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var registry = source[start..end];

        Assert.DoesNotContain("ListSql", registry, StringComparison.Ordinal);
        Assert.DoesNotContain("DetailSql", registry, StringComparison.Ordinal);
        Assert.DoesNotContain("SummarySql", registry, StringComparison.Ordinal);
        Assert.DoesNotContain("RequiresModuleKey", registry, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT ", registry, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AuthenticatedUserId_IsFailClosedAndHasNoNumericFallback()
    {
        var source = Endpoints();
        Assert.DoesNotMatch(new Regex(@"AuthUserIdItemKey\]\s*\?\?\s*(?:0L?|1L?)"), source);
        Assert.DoesNotMatch(new Regex(@"AuthUserIdItemKey[^\n]*:\s*0L?"), source);

        var missing = new DefaultHttpContext();
        Assert.Throws<InvalidOperationException>(() => EndpointMappings.GetUserId(missing));

        var valid = new DefaultHttpContext();
        valid.Items[EndpointMappings.AuthUserIdItemKey] = 42L;
        Assert.Equal(42L, EndpointMappings.GetUserId(valid));
    }

    [Fact]
    public void RetiredHosEldHandlers_AreRemovedWhileActivePilotRoutesRemain()
    {
        var source = Endpoints();
        Assert.DoesNotContain("Task<IResult> HosSummary(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Task<IResult> HosCertify(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Task<IResult> EldMarkMalfunction(", source, StringComparison.Ordinal);
        Assert.Contains("MapGet(\"/api/hos/summary\", HosSummaryPilot)", source, StringComparison.Ordinal);
        Assert.Contains("MapPost(\"/api/hos/logs/{id:long}/certify\", HosCertifyPilot)", source, StringComparison.Ordinal);
        Assert.Contains("MapPost(\"/api/eld/devices/{id:long}/mark-malfunction\", EldMarkMalfunctionPilot)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DeploymentUsesCanonicalBackendAndMigrationPathsOnly()
    {
        var root = RepoRoot();
        Assert.False(Directory.Exists(Path.Combine(root, "api-dotnet")));
        Assert.False(Directory.Exists(Path.Combine(root, "db")));

        var compose = File.ReadAllText(Path.Combine(root, "docker-compose.yml"));
        Assert.Contains("dockerfile: backend-dotnet/Dockerfile", compose, StringComparison.Ordinal);
        Assert.DoesNotContain("context: ./api-dotnet", compose, StringComparison.Ordinal);

        var workflowText = string.Join('\n', Directory
            .EnumerateFiles(Path.Combine(root, ".github", "workflows"), "*.yml", SearchOption.TopDirectoryOnly)
            .Select(File.ReadAllText));
        Assert.DoesNotMatch(new Regex(@"(?:working-directory:|dotnet\s+(?:build|test|restore))[^\n]*api-dotnet", RegexOptions.IgnoreCase), workflowText);

        var productionSources = string.Join('\n', Directory
            .EnumerateFiles(Path.Combine(root, "backend-dotnet"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Select(File.ReadAllText));
        Assert.DoesNotContain("db/init", productionSources, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("database/migrations/2026_08_21_stage84_driver_hos_runtime_contract.sql", productionSources, StringComparison.Ordinal);
    }

    private static string MethodBody(string source, string methodName)
    {
        var signature = Regex.Match(source,
            $@"private static (?:async )?[^\n]+\b{Regex.Escape(methodName)}\([^\n]*\)\s*\{{");
        Assert.True(signature.Success, $"Handler {methodName} was not found.");

        var next = source.IndexOf("\n    private static ", signature.Index + signature.Length, StringComparison.Ordinal);
        return next < 0 ? source[signature.Index..] : source[signature.Index..next];
    }

    private static string Endpoints() => File.ReadAllText(Path.Combine(
        RepoRoot(), "backend-dotnet", "Controllers", "EndpointMappings.cs"));

    private static string RepoRoot() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
}
