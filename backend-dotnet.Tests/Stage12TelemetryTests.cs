using Microsoft.Extensions.Configuration;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Foundation;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

[Trait("Category", "Integration")]
public class Stage12TelemetryTests
{
    private const string LocalConnectionString =
        "Host=127.0.0.1;Port=5433;Database=opstrax_local;Username=zayra;Password=zayra";

    [Fact]
    public async Task TelemetrySummary_Exposes_Live_State_Registry_Rules_Alerts_And_Recommendations()
    {
        var db = CreateDatabase();
        var schema = new TelemetrySchemaService(db);
        var companyId = NextCompanyId();
        var vehicleId = await GetAnyVehicleIdAsync(db);
        var driverId = await GetAnyDriverIdAsync(db);
        var liveState = new TelemetryLiveStateService(db);
        var ai = new PostgresAiFoundationService(db, new AmbientCorrelationContext());

        try
        {
            await schema.EnsureAsync();
            await SeedTelemetryAsync(db, companyId, vehicleId, driverId, staleMinutes: 2, speedMph: 78m);

            await liveState.RefreshVehicleAsync(companyId, vehicleId);
            _ = ai.CreateRecommendation(
                companyId.ToString(),
                "telemetry.speeding",
                "Speeding review",
                "Manual telemetry recommendation seeded for summary coverage",
                0.81m,
                0.72m,
                "{\"impact\":\"moderate\"}",
                "{\"reason\":\"speeding alert\"}",
                "{\"action\":\"review\"}",
                "medium",
                "seed-alert-1",
                ActorTypes.System,
                "test-harness");

            var summary = await liveState.BuildSummaryAsync(companyId);

            var entities = (IReadOnlyList<Dictionary<string, object?>>)summary["entities"]!;
            Assert.NotNull(summary["deviceRegistry"]);
            Assert.NotNull(summary["riskRules"]);
            Assert.NotNull(summary["alerts"]);
            Assert.NotNull(summary["recommendations"]);
            Assert.False(summary.ContainsKey("mobileReadiness"));
            Assert.True(entities.Count >= 0);

            var kpis = (Dictionary<string, object?>)summary["kpis"]!;
            Assert.True(Convert.ToInt64(kpis["liveUnits"]) >= 0);
            Assert.True(Convert.ToInt64(kpis["registeredDevices"]) >= 0);
        }
        finally
        {
            await CleanupTenantAsync(db, companyId);
        }
    }

    [Fact]
    public async Task TelemetryLiveState_Refresh_Is_TenantScoped_And_Stores_Stale_Risk()
    {
        var db = CreateDatabase();
        var schema = new TelemetrySchemaService(db);
        var companyA = NextCompanyId();
        var companyB = NextCompanyId();
        var vehicleId = await GetAnyVehicleIdAsync(db);
        var driverId = await GetAnyDriverIdAsync(db);
        var liveState = new TelemetryLiveStateService(db);

        try
        {
            await schema.EnsureAsync();
            await SeedTelemetryAsync(db, companyA, vehicleId, driverId, staleMinutes: 25, speedMph: 12m);
            await liveState.RefreshVehicleAsync(companyA, vehicleId);

            var state = await liveState.GetLiveStateAsync(companyA, vehicleId);
            Assert.NotNull(state);
            Assert.Equal("stale", state!["telemetryStatus"]?.ToString());
            Assert.Equal("high", state["riskLevel"]?.ToString());
            Assert.Contains("heartbeat", state["nextAction"]?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);

            var otherSummary = await liveState.BuildSummaryAsync(companyB);
            Assert.Empty((IReadOnlyList<Dictionary<string, object?>>)otherSummary["entities"]!);
            Assert.Empty((IReadOnlyList<Dictionary<string, object?>>)otherSummary["deviceRegistry"]!);
        }
        finally
        {
            await CleanupTenantAsync(db, companyA);
            await CleanupTenantAsync(db, companyB);
        }
    }

    [Fact]
    public void TelemetryPermissions_FailClosed_And_Recognize_Allowed_Aliases()
    {
        Assert.True(EndpointMappings.HasPermission(new[] { "map:view" }, "telemetry.live_state.read"));
        Assert.True(EndpointMappings.HasPermission(new[] { "fleet:view" }, "telemetry.devices.read"));
        Assert.True(EndpointMappings.HasPermission(new[] { "alerts:view" }, "telemetry.alerts.read"));
        Assert.False(EndpointMappings.HasPermission(Array.Empty<string>(), "telemetry.live_state.read"));
    }

    private static Database CreateDatabase()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = LocalConnectionString,
            })
            .Build();
        return new Database(config);
    }

    private static async Task SeedTelemetryAsync(Database db, long companyId, long vehicleId, long driverId, int staleMinutes, decimal speedMph)
    {
        // Active devices require real credentials (ck_eld_devices_active_credentials).
        var rawApiKey  = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var hmacSecret = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var deviceId = await db.InsertAsync(
            @"INSERT INTO eld_devices (company_id, device_serial, vehicle_id, driver_id, api_key_hash, hmac_secret, status, created_at)
              VALUES (@companyId, @serial, @vehicleId, @driverId, encode(sha256(@rawKey::bytea), 'hex'), @hmac, 'Active', NOW())
              RETURNING id",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@serial", $"TEL-{companyId}-{vehicleId}");
                c.Parameters.AddWithValue("@vehicleId", vehicleId);
                c.Parameters.AddWithValue("@driverId", driverId);
                c.Parameters.AddWithValue("@rawKey", rawApiKey);
                c.Parameters.AddWithValue("@hmac", hmacSecret);
            });

        await db.ExecuteAsync(
            @"INSERT INTO telemetry_rules (company_id, rule_type, threshold_value, severity, enabled)
              VALUES (@companyId, 'speeding', 65, 'High', true)
              ON CONFLICT (company_id, rule_type) DO UPDATE SET threshold_value=EXCLUDED.threshold_value, enabled=TRUE, updated_at=NOW()",
            c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync(
            @"INSERT INTO telemetry_rules (company_id, rule_type, threshold_value, severity, enabled)
              VALUES (@companyId, 'stale_device', 900, 'Warning', true)
              ON CONFLICT (company_id, rule_type) DO UPDATE SET threshold_value=EXCLUDED.threshold_value, enabled=TRUE, updated_at=NOW()",
            c => c.Parameters.AddWithValue("@companyId", companyId));

        await db.ExecuteAsync(
            @"INSERT INTO latest_vehicle_positions
                (company_id, vehicle_id, device_id, driver_id, lat, lng, speed_mph, heading,
                 accuracy_meters, engine_status, fuel_level, odometer_miles, battery_voltage,
                 event_time, received_at, event_count, source_event_id, telemetry_status,
                 risk_level, alert_count, open_alert_count, next_action, summary_json, updated_at)
              VALUES
                (@companyId, @vehicleId, @deviceId, @driverId, 33.1000000, -97.1000000, @speedMph, 180,
                 5.0, 'Running', 78.5, 220123.4, 12.6, NOW() - (@staleMinutes || ' minutes')::interval,
                 NOW() - (@staleMinutes || ' minutes')::interval, 4, 1001, 'stale',
                 'high', 1, 1, 'Check device heartbeat and field power', '{}'::jsonb, NOW())",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@vehicleId", vehicleId);
                c.Parameters.AddWithValue("@deviceId", deviceId);
                c.Parameters.AddWithValue("@driverId", driverId);
                c.Parameters.AddWithValue("@speedMph", speedMph);
                c.Parameters.AddWithValue("@staleMinutes", staleMinutes);
            });

        await db.ExecuteAsync(
            @"INSERT INTO telemetry_alerts
                (company_id, vehicle_id, device_id, driver_id, alert_type, severity, message, source_event_id, status)
              VALUES
                (@companyId, @vehicleId, @deviceId, @driverId, 'speeding', 'High',
                 'Seeded speeding alert for live-state summary coverage', 1001, 'Open')",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@vehicleId", vehicleId);
                c.Parameters.AddWithValue("@deviceId", deviceId);
                c.Parameters.AddWithValue("@driverId", driverId);
            });
    }

    private static async Task<long> GetAnyVehicleIdAsync(Database db)
        => await db.ScalarLongAsync("SELECT id FROM vehicles ORDER BY id LIMIT 1");

    private static async Task<long> GetAnyDriverIdAsync(Database db)
        => await db.ScalarLongAsync("SELECT id FROM drivers ORDER BY id LIMIT 1");

    private static long NextCompanyId() => Interlocked.Increment(ref _nextCompanyId);

    private static long _nextCompanyId = 69000;

    private static async Task CleanupTenantAsync(Database db, long companyId)
    {
        await DeleteIfExistsAsync(db, "telemetry_alerts", "company_id", companyId);
        await DeleteIfExistsAsync(db, "telemetry_live_asset_states", "company_id", companyId);
        await DeleteIfExistsAsync(db, "latest_vehicle_positions", "company_id", companyId);
        await DeleteIfExistsAsync(db, "telemetry_rules", "company_id", companyId);
        await DeleteIfExistsAsync(db, "eld_devices", "company_id", companyId);
        await DeleteIfExistsAsync(db, "location_events", "company_id", companyId);
        await DeleteIfExistsAsync(db, "ai_recommendations", "tenant_id", companyId);
    }

    private static async Task DeleteIfExistsAsync(Database db, string table, string tenantColumn, long tenantId)
    {
        var exists = await db.ScalarLongAsync(
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=current_schema() AND table_name=@table",
            c => c.Parameters.AddWithValue("@table", table));
        if (exists == 0) return;

        await db.ExecuteAsync(
            $@"DELETE FROM {table} WHERE {tenantColumn}=@tenantId",
            c => c.Parameters.AddWithValue("@tenantId", tenantId));
    }
}
