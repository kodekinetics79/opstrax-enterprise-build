using System.Reflection;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

public sealed class TelemetryPolicyActivationIntegrityTests
{
    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));

    [Fact]
    public void AlertProducers_RequireApprovedTenantPolicyAndHaveNoHiddenThresholdFallbacks()
    {
        var telemetrySchema = Read("backend-dotnet", "Services", "TelemetrySchemaService.cs");
        var safetySchema = Read("backend-dotnet", "Services", "SafetySchemaService.cs");
        var endpoints = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var samsara = Read("backend-dotnet", "Services", "Connectors", "SamsaraSync.cs");
        var telemetryWorker = Read("backend-dotnet", "Services", "TelemetryBackgroundService.cs");
        var detector = Read("backend-dotnet", "Services", "OperationalAlertDetectionService.cs");
        var safetyWorker = Read("backend-dotnet", "Services", "SafetyBackgroundService.cs");

        Assert.Contains("'system_template', 'unapproved'", telemetrySchema);
        Assert.Contains("'system_template', 'unapproved'", safetySchema);
        Assert.DoesNotContain("'speeding', 65, 'High', true", telemetrySchema);
        Assert.DoesNotContain("'safety_weight_speeding', 15, 'High', true", safetySchema);

        foreach (var source in new[] { endpoints, samsara, telemetryWorker, detector, safetyWorker })
        {
            Assert.Contains("approval_status='approved'", source);
            Assert.Contains("approved_at IS NOT NULL", source);
        }

        Assert.DoesNotContain("?? 65m", endpoints);
        Assert.DoesNotContain("?? 65m", samsara);
        Assert.DoesNotContain("COALESCE(tr.threshold_value, 900)", telemetryWorker);
        Assert.DoesNotContain("COALESCE((SELECT tr.threshold_value", detector);
        Assert.DoesNotContain("SeverityWeights", safetyWorker);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UnapprovedRuleCannotActivateOrCreateFuelAlert_ButApprovedRuleCan()
    {
        var db = CreateDatabase();
        await using (var connection = new NpgsqlConnection(TestDb.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = Read("database", "migrations", "2026_09_08_telemetry_rule_activation_integrity.sql")
                + "\nALTER TABLE telemetry_alerts ADD COLUMN IF NOT EXISTS source_channel VARCHAR(40) NULL;"
                + "\nALTER TABLE location_events ADD COLUMN IF NOT EXISTS source VARCHAR(40) NULL, ADD COLUMN IF NOT EXISTS source_channel VARCHAR(40) NULL;";
            await command.ExecuteNonQueryAsync();
        }
        var companyId = await db.InsertAsync(
            "INSERT INTO companies(company_code,name,industry) VALUES(@code,'Policy Integrity','logistics') RETURNING id",
            c => c.Parameters.AddWithValue("@code", $"POL-{Guid.NewGuid():N}"[..20]));
        var vehicleId = companyId + 8_000_000;

        try
        {
            await db.ExecuteAsync(
                @"INSERT INTO telemetry_rules
                    (company_id,rule_type,threshold_value,severity,enabled,policy_origin,approval_status)
                  VALUES(@cid,'fuel_drop_pct',20,'High',FALSE,'system_template','unapproved')",
                c => c.Parameters.AddWithValue("@cid", companyId));

            await Assert.ThrowsAsync<PostgresException>(() => db.ExecuteAsync(
                "UPDATE telemetry_rules SET enabled=TRUE WHERE company_id=@cid AND rule_type='fuel_drop_pct'",
                c => c.Parameters.AddWithValue("@cid", companyId)));

            await InsertFuelFix(db, companyId, vehicleId, 80m, 20, "samsara", "samsara-api");
            await InsertFuelFix(db, companyId, vehicleId, 70m, 10, "samsara", "samsara-api");
            await InsertFuelFix(db, companyId, vehicleId, 40m, 1, "samsara", "samsara-api");

            var sql = (string)typeof(OperationalAlertDetectionService)
                .GetField("FuelAnomalySql", BindingFlags.NonPublic | BindingFlags.Static)!
                .GetRawConstantValue()!;

            Assert.Equal(0, await db.ExecuteAsync(sql));

            await db.ExecuteAsync(
                "DELETE FROM location_events WHERE company_id=@cid",
                c => c.Parameters.AddWithValue("@cid", companyId));
            await InsertFuelFix(db, companyId, vehicleId, 80m, 20, "demo-history", null);
            await InsertFuelFix(db, companyId, vehicleId, 70m, 10, "simulator", null);
            await InsertFuelFix(db, companyId, vehicleId, 40m, 1, "demo-history", null);

            await db.ExecuteAsync(
                @"UPDATE telemetry_rules
                     SET enabled=TRUE,created_by=1,policy_origin='user_workflow',approval_status='approved',
                         approved_by=1,approved_at=NOW()
                   WHERE company_id=@cid AND rule_type='fuel_drop_pct'",
                c => c.Parameters.AddWithValue("@cid", companyId));

            Assert.Equal(0, await db.ExecuteAsync(sql));

            await InsertFuelFix(db, companyId, vehicleId, 80m, 20, "device", "native-hmac");
            await InsertFuelFix(db, companyId, vehicleId, 70m, 10, "gps-tracker", "trusted-gateway");
            await InsertFuelFix(db, companyId, vehicleId, 40m, 1, "samsara", "samsara-api");
            Assert.Equal(1, await db.ExecuteAsync(sql));
            Assert.Equal(1, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM telemetry_alerts WHERE company_id=@cid AND alert_type='fuel_anomaly' AND source_channel='approved-policy-detector'",
                c => c.Parameters.AddWithValue("@cid", companyId)));
        }
        finally
        {
            foreach (var table in new[] { "telemetry_alerts", "location_events", "telemetry_rules" })
                await db.ExecuteAsync($"DELETE FROM {table} WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
            await db.ExecuteAsync("DELETE FROM companies WHERE id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
        }
    }

    private static async Task InsertFuelFix(
        Database db,
        long companyId,
        long vehicleId,
        decimal fuel,
        int minutesAgo,
        string source,
        string? sourceChannel)
    {
        await db.ExecuteAsync(
            @"INSERT INTO location_events
                (company_id,vehicle_id,lat,lng,speed_mph,fuel_level,event_type,event_time,source,source_channel)
              VALUES(@cid,@vid,38.8,-77.1,25,@fuel,'ping',NOW()-@minutes*INTERVAL '1 minute',@source,@sourceChannel)",
            c =>
            {
                c.Parameters.AddWithValue("@cid", companyId);
                c.Parameters.AddWithValue("@vid", vehicleId);
                c.Parameters.AddWithValue("@fuel", fuel);
                c.Parameters.AddWithValue("@minutes", minutesAgo);
                c.Parameters.AddWithValue("@source", source);
                c.Parameters.AddWithValue("@sourceChannel", (object?)sourceChannel ?? DBNull.Value);
            });
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { RepoRoot }.Concat(parts).ToArray()));

    private static Database CreateDatabase() =>
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString,
        }).Build());
}
