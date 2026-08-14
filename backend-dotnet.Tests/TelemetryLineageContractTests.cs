namespace Opstrax.Tests;

public sealed class TelemetryLineageContractTests
{
    [Fact]
    public void RouteTripDispatchAndBreadcrumbContractsPreserveCanonicalLineage()
    {
        var tripWorker = Read("backend-dotnet", "Services", "TripBackgroundService.cs");
        Assert.Contains("CASE WHEN COUNT(DISTINCT rs.job_id)=1 THEN MIN(rs.job_id) END job_id", tripWorker, StringComparison.Ordinal);
        Assert.Contains("(company_id, driver_id, vehicle_id, route_id, job_id", tripWorker, StringComparison.Ordinal);

        var endpoints = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        Assert.Contains("Route has ambiguous trip lineage", endpoints, StringComparison.Ordinal);
        Assert.Contains("LOWER(COALESCE(t.status,'')) IN ('planned','active','exception')", endpoints, StringComparison.Ordinal);
        Assert.Contains("Job does not match the route trip lineage", endpoints, StringComparison.Ordinal);
        Assert.Contains("route_id, trip_id, trailer_id", endpoints, StringComparison.Ordinal);
        Assert.Contains("@rid, @tripId, @tid", endpoints, StringComparison.Ordinal);
        Assert.Contains("SELECT id, assignment_id, trip_id, driver_id, lat, lng", endpoints, StringComparison.Ordinal);

        var migration = Read("database", "migrations", "2026_08_14_stage80_fleet_identity_backbone.sql");
        Assert.Contains("DROP INDEX IF EXISTS ux_trips_route", migration, StringComparison.Ordinal);
        Assert.Contains("CREATE UNIQUE INDEX IF NOT EXISTS ux_trips_current_route", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void ProtectedMigrationOwnsCustomerHealthRuntimeContract()
    {
        var migration = Read("database", "migrations", "2026_08_14_stage80_fleet_identity_backbone.sql");
        var readiness = Read("backend-dotnet", "Services", "FleetProductionReadinessService.cs");
        foreach (var column in new[] { "sla_health_score", "delivery_experience_score", "risk_score", "health_state", "health_computed_at" })
        {
            Assert.Contains(column, migration, StringComparison.Ordinal);
            Assert.Contains($"('customers','{column}'", readiness, StringComparison.Ordinal);
        }
    }

    private static string Read(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "backend-dotnet"))) directory = directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine([directory!.FullName, .. parts]));
    }
}
