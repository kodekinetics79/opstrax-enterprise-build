using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

// C2 — harsh-driving/crash detection. A device-reported harsh event lands as a telemetry_alert; the
// safety background service converts it into a safety_event using the DASHBOARD vocabulary (Title-Case),
// so the previously-empty "Harsh Braking" tile becomes real and the driver score is deducted.
[Trait("Category", "Integration")]
public class SafetyHarshEventPostgresTests
{
    [Fact]
    public async Task HarshBraking_Alert_Converts_To_Dashboard_SafetyEvent()
    {
        var db = CreateDatabase();
        await ResetSequencesAsync(db);
        var cid = await SeedCompanyAsync(db);
        try
        {
            var evId = await db.InsertAsync(
                "INSERT INTO location_events (company_id, lat, lng, speed_mph, event_type, source, event_time, received_at) VALUES (@c, 34.05, -118.24, 45, 'ping', 'gps-tracker', NOW(), NOW()) RETURNING id",
                c => c.Parameters.AddWithValue("@c", cid));
            var alertId = await db.InsertAsync(
                @"INSERT INTO telemetry_alerts (company_id, alert_type, severity, message, source_event_id, status, created_at)
                  VALUES (@c, 'harsh_braking', 'High', 'Device-reported harsh_braking (magnitude 0.62)', @src, 'open', NOW()) RETURNING id",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@src", evId); });

            await InvokeConversionAsync(db);

            var evType = (await db.QuerySingleAsync(
                "SELECT event_type FROM safety_events WHERE company_id=@c AND source_telemetry_alert_id=@a",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@a", alertId); }))?["eventType"]?.ToString();
            Assert.Equal("Harsh Braking", evType);   // dashboard vocabulary, not raw 'harsh_braking'
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task Crash_Alert_Converts_With_Crash_Vocabulary()
    {
        var db = CreateDatabase();
        await ResetSequencesAsync(db);
        var cid = await SeedCompanyAsync(db);
        try
        {
            var alertId = await db.InsertAsync(
                "INSERT INTO telemetry_alerts (company_id, alert_type, severity, message, status, created_at) VALUES (@c, 'crash', 'Critical', 'Device-reported crash', 'open', NOW()) RETURNING id",
                c => c.Parameters.AddWithValue("@c", cid));

            await InvokeConversionAsync(db);

            var row = (await db.QuerySingleAsync(
                "SELECT event_type, severity FROM safety_events WHERE company_id=@c AND source_telemetry_alert_id=@a",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@a", alertId); }))!;
            Assert.Equal("Crash", row["eventType"]?.ToString());
            Assert.Equal("Critical", row["severity"]?.ToString());
        }
        finally { await CleanupAsync(db, cid); }
    }

    private static async Task InvokeConversionAsync(Database db)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(db);
        services.AddSingleton<ServiceRunTracker>();
        var provider = services.BuildServiceProvider();
        var svc = new SafetyBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SafetyBackgroundService>.Instance,
            provider.GetRequiredService<ServiceRunTracker>());
        var m = typeof(SafetyBackgroundService).GetMethod("ProcessTelemetryAlertsAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        // The conversion runs to completion even though the AI-recommendation service is unwired in this
        // minimal DI: SafetyBackgroundService now isolates that best-effort enrichment per-alert, so it never
        // aborts the batch. That resilience is exactly what keeps this assertion deterministic in the full suite.
        await (Task)m.Invoke(svc, new object[] { CancellationToken.None })!;
    }

    private static async Task<long> SeedCompanyAsync(Database db) =>
        await db.InsertAsync("INSERT INTO companies (company_code, name, industry) VALUES (@code, 'Harsh Co', 'logistics') RETURNING id",
            c => c.Parameters.AddWithValue("@code", $"HZ-{Guid.NewGuid():N}".Substring(0, 16)));

    private static async Task CleanupAsync(Database db, long cid)
    {
        foreach (var t in new[] { "safety_events", "telemetry_alerts", "location_events" })
            await db.ExecuteAsync($"DELETE FROM {t} WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", cid));
        await db.ExecuteAsync("DELETE FROM companies WHERE id=@c", c => c.Parameters.AddWithValue("@c", cid));
    }

    private static Database CreateDatabase() =>
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString }).Build());

    private static async Task ResetSequencesAsync(Database db)
    {
        foreach (var table in new[] { "companies", "location_events", "telemetry_alerts", "safety_events" })
            await db.ExecuteAsync($"SELECT setval(pg_get_serial_sequence('{table}', 'id'), (SELECT COALESCE(MAX(id), 1) FROM {table}))");
    }
}
