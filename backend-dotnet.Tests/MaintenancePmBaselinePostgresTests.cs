using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

// P1 fix — preventive-maintenance service baseline. The engine-hours PM branch used a literal that was
// always exactly `interval` (nextDueHrs = 0 + interval), so once a machine crossed the interval it was
// flagged overdue forever. The evaluator now reads the engine-hours recorded on the last COMPLETED
// maintenance_item as the baseline; nextDue = baseline + interval. These tests drive the REAL evaluator.
[Trait("Category", "Integration")]
public class MaintenancePmBaselinePostgresTests
{
    [Fact]
    public async Task EngineHours_Recorded_Baseline_Arms_Next_Interval_Not_Perpetually_Overdue()
    {
        var db = CreateDatabase();
        var (cid, vid) = await SeedVehicleAsync(db, engineHours: 600m);   // machine at 600 hrs
        try
        {
            await SeedRuleAsync(db, cid, serviceType: $"pm_eng_{cid}", intervalHours: 250);
            // A prior service was completed AT 600 engine hours -> baseline recorded on the closed item.
            await SeedCompletedServiceAsync(db, cid, vid, serviceType: $"pm_eng_{cid}", engineHours: 600m);

            await InvokeEvaluateAsync(db);

            // nextDue = 600 + 250 = 850 > current 600  => NOT due. Before the fix, nextDue was 250 and the
            // machine (600 hrs) was flagged Overdue. So an open item here would mean the bug is still present.
            var open = await OpenItemsAsync(db, cid, vid, $"pm_eng_{cid}");
            Assert.Equal(0, open);
        }
        finally { await CleanupAsync(db, cid, vid); }
    }

    [Fact]
    public async Task EngineHours_Past_Baseline_Plus_Interval_Is_Flagged_Overdue()
    {
        var db = CreateDatabase();
        var (cid, vid) = await SeedVehicleAsync(db, engineHours: 900m);   // machine now at 900 hrs
        try
        {
            await SeedRuleAsync(db, cid, serviceType: $"pm_eng_{cid}", intervalHours: 250);
            await SeedCompletedServiceAsync(db, cid, vid, serviceType: $"pm_eng_{cid}", engineHours: 600m);

            await InvokeEvaluateAsync(db);

            // nextDue = 600 + 250 = 850, current 900 >= 850 => genuinely due. The evaluator must still fire.
            var open = await OpenItemsAsync(db, cid, vid, $"pm_eng_{cid}");
            Assert.True(open >= 1, "a genuinely-due engine-hours service should create a maintenance item");
        }
        finally { await CleanupAsync(db, cid, vid); }
    }

    // ── invoke the real private evaluator with minimal DI (AI enrichment is now guarded, so it can't abort) ──
    private static async Task InvokeEvaluateAsync(Database db)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(db);
        services.AddSingleton<ServiceRunTracker>();
        var provider = services.BuildServiceProvider();
        var svc = new MaintenanceBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(), db,
            NullLogger<MaintenanceBackgroundService>.Instance,
            provider.GetRequiredService<ServiceRunTracker>());
        var m = typeof(MaintenanceBackgroundService).GetMethod("EvaluatePmRulesAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)m.Invoke(svc, new object[] { CancellationToken.None })!;
    }

    private static async Task<long> OpenItemsAsync(Database db, long cid, long vid, string serviceType) =>
        await db.ScalarLongAsync(
            @"SELECT COUNT(*) FROM maintenance_items
              WHERE company_id=@c AND vehicle_id=@v AND service_type=@s
                AND status NOT IN ('Completed','Cancelled','Closed','Deleted')",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@v", vid); c.Parameters.AddWithValue("@s", serviceType); });

    private static async Task<(long cid, long vid)> SeedVehicleAsync(Database db, decimal engineHours)
    {
        var cid = await db.InsertAsync(
            "INSERT INTO companies (company_code, name, industry) VALUES (@code, 'PM Co', 'logistics') RETURNING id",
            c => c.Parameters.AddWithValue("@code", $"PM-{Guid.NewGuid():N}".Substring(0, 15)));
        var vid = await db.InsertAsync(
            @"INSERT INTO vehicles (company_id, vehicle_code, type, odometer_miles, engine_hours)
              VALUES (@cid, @code, 'reefer', 50000, @hrs) RETURNING id",
            c => { c.Parameters.AddWithValue("@cid", cid); c.Parameters.AddWithValue("@code", $"V-{Guid.NewGuid():N}".Substring(0, 12)); c.Parameters.AddWithValue("@hrs", engineHours); });
        return (cid, vid);
    }

    private static async Task SeedRuleAsync(Database db, long cid, string serviceType, int intervalHours) =>
        await db.ExecuteAsync(
            @"INSERT INTO maintenance_pm_rules (company_id, rule_name, service_type, trigger_type,
                interval_engine_hours, warning_threshold_pct, priority, enabled)
              VALUES (@cid, @name, @stype, 'engine_hours', @interval, 10, 'High', TRUE)",
            c => { c.Parameters.AddWithValue("@cid", cid); c.Parameters.AddWithValue("@name", serviceType);
                   c.Parameters.AddWithValue("@stype", serviceType); c.Parameters.AddWithValue("@interval", intervalHours); });

    private static async Task SeedCompletedServiceAsync(Database db, long cid, long vid, string serviceType, decimal engineHours) =>
        await db.ExecuteAsync(
            @"INSERT INTO maintenance_items (company_id, vehicle_id, service_type, title, category, status, engine_hours, risk_score)
              VALUES (@cid, @vid, @stype, @stype, 'Preventive Maintenance', 'Completed', @hrs, 10)",
            c => { c.Parameters.AddWithValue("@cid", cid); c.Parameters.AddWithValue("@vid", vid);
                   c.Parameters.AddWithValue("@stype", serviceType); c.Parameters.AddWithValue("@hrs", engineHours); });

    private static async Task CleanupAsync(Database db, long cid, long vid)
    {
        await db.ExecuteAsync("DELETE FROM ai_recommendations WHERE tenant_id=@c", c => c.Parameters.AddWithValue("@c", cid));
        await db.ExecuteAsync("DELETE FROM maintenance_items WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", cid));
        await db.ExecuteAsync("DELETE FROM maintenance_pm_rules WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", cid));
        await db.ExecuteAsync("DELETE FROM vehicles WHERE id=@v", c => c.Parameters.AddWithValue("@v", vid));
        await db.ExecuteAsync("DELETE FROM companies WHERE id=@c", c => c.Parameters.AddWithValue("@c", cid));
    }

    private static Database CreateDatabase() =>
        new(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString }).Build());
}
