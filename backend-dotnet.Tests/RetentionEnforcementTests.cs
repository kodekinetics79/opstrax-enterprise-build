using Microsoft.Extensions.Configuration;
using Npgsql;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

public sealed class RetentionEnforcementTests
{
    [Fact]
    public void ProductionReadiness_TreatsRetentionAsCritical()
    {
        Assert.Contains("RetentionEnforcementService",
            FleetProductionReadinessService.CriticalWorkerNames);
        Assert.Equal(1, FleetProductionReadinessService.CriticalWorkerFailureThreshold(
            "RetentionEnforcementService"));
        Assert.Equal(3, FleetProductionReadinessService.CriticalWorkerFailureThreshold(
            "TelemetryBackgroundService"));
        Assert.Equal(10, RetentionEnforcementBackgroundService.MaxBatchesPerCategoryPerCycle);
    }

    [Fact]
    public void CycleFailure_ReportsOnlyBoundedCategoryAndErrorCodes()
    {
        var failures = new[]
        {
            new RetentionPurgeFailure("location_events", "PostgresException"),
            new RetentionPurgeFailure("notifications", "TimeoutException"),
        };

        var exception = new RetentionPurgeCycleException(failures);

        Assert.Equal(failures, exception.Failures);
        Assert.Contains("2 category operation(s)", exception.Message, StringComparison.Ordinal);
        Assert.Contains("location_events:PostgresException", exception.Message, StringComparison.Ordinal);
        Assert.Contains("notifications:TimeoutException", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Host=", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Purge_DeletesOnlyExpiredRowsForTheSelectedTenant_AndPropagatesSchemaFailure()
    {
        var table = "retention_worker_test_" + Guid.NewGuid().ToString("N");
        var db = Db();
        var tenantA = 970_000_000L + Random.Shared.Next(1, 100_000);
        var tenantB = tenantA + 100_000;
        var heldTenant = tenantA + 200_000;
        try
        {
            await db.ExecuteAsync($"CREATE TABLE {table} (company_id BIGINT NOT NULL, received_at TIMESTAMPTZ NOT NULL)");
            await db.ExecuteAsync(
                @"INSERT INTO data_retention_policies(company_id,legal_hold_active)
                  VALUES (@a,false),(@b,false),(@held,true)", c =>
                {
                    c.Parameters.AddWithValue("@a", tenantA);
                    c.Parameters.AddWithValue("@b", tenantB);
                    c.Parameters.AddWithValue("@held", heldTenant);
                });
            await db.ExecuteAsync($@"INSERT INTO {table}(company_id,received_at) VALUES
                (@a,NOW()-INTERVAL '31 days'),
                (@a,NOW()-INTERVAL '29 days'),
                (@b,NOW()-INTERVAL '31 days'),
                (@held,NOW()-INTERVAL '31 days')", c =>
            {
                c.Parameters.AddWithValue("@a", tenantA);
                c.Parameters.AddWithValue("@b", tenantB);
                c.Parameters.AddWithValue("@held", heldTenant);
            });

            var deleted = await RetentionEnforcementBackgroundService.PurgeAsync(
                db, table, "received_at", 30, tenantA, CancellationToken.None);

            Assert.Equal(1, deleted);
            Assert.Equal(1, await CountRows(db, table, tenantA));
            Assert.Equal(1, await CountRows(db, table, tenantB));

            Assert.Equal(0, await RetentionEnforcementBackgroundService.PurgeAsync(
                db, table, "received_at", 30, heldTenant, CancellationToken.None));
            Assert.Equal(1, await CountRows(db, table, heldTenant));

            await Assert.ThrowsAsync<PostgresException>(() =>
                RetentionEnforcementBackgroundService.PurgeAsync(
                    db, table + "_missing", "received_at", 30, tenantA, CancellationToken.None));
        }
        finally
        {
            await db.ExecuteAsync($"DROP TABLE IF EXISTS {table}");
            await db.ExecuteAsync(
                "DELETE FROM data_retention_policies WHERE company_id IN (@a,@b,@held)", c =>
                {
                    c.Parameters.AddWithValue("@a", tenantA);
                    c.Parameters.AddWithValue("@b", tenantB);
                    c.Parameters.AddWithValue("@held", heldTenant);
                });
        }
    }

    private static Task<long> CountRows(Database db, string table, long companyId) =>
        db.ScalarLongAsync($"SELECT COUNT(*) FROM {table} WHERE company_id=@cid",
            c => c.Parameters.AddWithValue("@cid", companyId));

    private static Database Db() => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString,
            ["Rls:EnforceTenantContext"] = "false",
        }).Build());
}
