using Microsoft.Extensions.Configuration;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

// Detention Recovery — clock + pricing + notice (Phases B½/C½/D/E). Locks the business mandates:
// the billable clock starts at LATER-OF(appointment, arrival) so early arrival never accrues;
// billable minutes round DOWN; no appointment and no attestation -> 'needs_appointment' (never
// priced); the pre-expiry 'meter running' notice is stamped once and logged for evidence.
[Trait("Category", "Integration")]
public class DetentionPricingPostgresTests
{
    [Fact]
    public async Task Appointment_Anchored_Clock_Prices_Correctly_With_Round_Down()
    {
        var db = CreateDatabase();
        await new DetentionSchemaService(db).EnsureAsync();
        var (cid, custId, vid, fenceId) = await SeedAsync(db);
        try
        {
            await SeedRuleCardAsync(db, cid, custId, freeMinutes: 120, ratePerHour: 60m, incrementMinutes: 15);

            // Attribution target: an assignment whose planned pickup (the appointment) is ONE HOUR
            // AFTER the truck arrives — the early hour must not accrue.
            var t0 = DateTime.UtcNow.AddHours(-6);           // arrival
            var appointment = t0.AddHours(1);                 // appointment (later than arrival)
            var jobId = await db.InsertAsync(
                @"INSERT INTO jobs (company_id, customer_id, job_code, job_type, pickup_latitude, pickup_longitude)
                  VALUES (@c, @cust, @code, 'freight', 34.05, -118.24) RETURNING id",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@cust", custId); c.Parameters.AddWithValue("@code", $"DJ-{Guid.NewGuid():N}".Substring(0, 12)); });
            await db.ExecuteAsync(
                @"INSERT INTO dispatch_assignments (company_id, job_id, vehicle_id, driver_id, assigned_at, planned_pickup_at)
                  VALUES (@c, @j, @v, 77, @assigned, @appt)",
                c =>
                {
                    c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@j", jobId);
                    c.Parameters.AddWithValue("@v", vid); c.Parameters.AddWithValue("@assigned", t0.AddHours(-1));
                    c.Parameters.AddWithValue("@appt", appointment);
                });

            await SeedEventAsync(db, cid, fenceId, vid, "Entry", t0);
            await DetentionService.DetectAsync(db);
            // Exit 4h10m after the appointment -> dwell(from clock)=250min, free=120 -> billable=130,
            // round DOWN to 15-min increment => 120min = 2.0h @ 60 = $120 (not 130min/$130 — mandate).
            await SeedEventAsync(db, cid, fenceId, vid, "Exit", appointment.AddMinutes(250));
            await DetentionService.DetectAsync(db);

            var d = (await db.QuerySingleAsync(
                "SELECT status, job_id, stop_role, clock_start_at, clock_rule, billable_minutes, quantity_hours, amount, claim_deadline_at FROM detention_dwells WHERE company_id=@c",
                c => c.Parameters.AddWithValue("@c", cid)))!;
            Assert.Equal("priced_pending_review", d["status"]?.ToString());
            Assert.Equal(jobId, Convert.ToInt64(d["jobId"]));                       // attributed
            Assert.Equal("pickup", d["stopRole"]?.ToString());
            Assert.Equal("later_of_appointment_arrival_v1", d["clockRule"]?.ToString());
            Assert.Equal(appointment, ((DateTime)d["clockStartAt"]!).ToUniversalTime(), TimeSpan.FromSeconds(2));  // early arrival excluded
            Assert.Equal(120, Convert.ToInt32(d["billableMinutes"]));               // rounded DOWN from 130
            Assert.Equal(2.0m, Convert.ToDecimal(d["quantityHours"]));
            Assert.Equal(120m, Convert.ToDecimal(d["amount"]));
            Assert.False(d["claimDeadlineAt"] is null or DBNull);                    // claim window armed
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task No_Appointment_Means_Detected_But_Never_Priced()
    {
        var db = CreateDatabase();
        await new DetentionSchemaService(db).EnsureAsync();
        var (cid, custId, vid, fenceId) = await SeedAsync(db);
        try
        {
            await SeedRuleCardAsync(db, cid, custId, 60, 50m, 15);
            var t0 = DateTime.UtcNow.AddHours(-5);
            await SeedEventAsync(db, cid, fenceId, vid, "Entry", t0);
            await DetentionService.DetectAsync(db);
            await SeedEventAsync(db, cid, fenceId, vid, "Exit", t0.AddHours(4));
            await DetentionService.DetectAsync(db);

            var status = (await db.QuerySingleAsync(
                "SELECT status, amount FROM detention_dwells WHERE company_id=@c",
                c => c.Parameters.AddWithValue("@c", cid)))!;
            Assert.Equal("needs_appointment", status["status"]?.ToString());
            Assert.True(status["amount"] is null or DBNull);   // fail-closed: no money computed
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task PreExpiry_Notice_Fires_Once_And_Is_Logged_For_Evidence()
    {
        var db = CreateDatabase();
        await new DetentionSchemaService(db).EnsureAsync();
        var (cid, custId, vid, fenceId) = await SeedAsync(db);
        try
        {
            await SeedRuleCardAsync(db, cid, custId, freeMinutes: 120, ratePerHour: 60m, incrementMinutes: 15); // notice at 75% = 90min

            // Truck has been inside for 2h — past the 90-minute notice threshold, still on site.
            await SeedEventAsync(db, cid, fenceId, vid, "Entry", DateTime.UtcNow.AddHours(-2));
            await DetentionService.DetectAsync(db);
            await DetentionService.DetectAsync(db);   // second tick must NOT duplicate the notice

            var d = (await db.QuerySingleAsync(
                "SELECT status, warning_notified_at FROM detention_dwells WHERE company_id=@c",
                c => c.Parameters.AddWithValue("@c", cid)))!;
            Assert.Equal("open", d["status"]?.ToString());
            Assert.False(d["warningNotifiedAt"] is null or DBNull);   // stamped

            var notices = await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM detention_notices WHERE company_id=@c AND notice_type='customer_meter_running'",
                c => c.Parameters.AddWithValue("@c", cid));
            Assert.Equal(1, notices);                                  // exactly one, with body snapshot
            var body = (await db.QuerySingleAsync(
                "SELECT body_snapshot, delivery_status FROM detention_notices WHERE company_id=@c",
                c => c.Parameters.AddWithValue("@c", cid)))!;
            Assert.Contains("expires", body["bodySnapshot"]?.ToString());
            Assert.Equal("logged", body["deliveryStatus"]?.ToString());

            var dispatchAlerts = await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM notifications WHERE company_id=@c AND event_type='detention.warning'",
                c => c.Parameters.AddWithValue("@c", cid));
            Assert.Equal(1, dispatchAlerts);
        }
        finally { await CleanupAsync(db, cid); }
    }

    // ── helpers ───────────────────────────────────────────────────────────────
    private static async Task<(long cid, long custId, long vid, long fenceId)> SeedAsync(Database db)
    {
        var cid = await db.InsertAsync("INSERT INTO companies (company_code, name, industry) VALUES (@code, 'DP Co', 'logistics') RETURNING id",
            c => c.Parameters.AddWithValue("@code", $"DP-{Guid.NewGuid():N}".Substring(0, 15)));
        var custId = await db.InsertAsync(
            "INSERT INTO customers (company_id, customer_code, name, contact_name, email) VALUES (@c, @code, 'DP Cust', 'AP Team', 'ap@cust.example') RETURNING id",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@code", $"DPC-{Guid.NewGuid():N}".Substring(0, 14)); });
        var vid = await db.InsertAsync(
            "INSERT INTO vehicles (company_id, vehicle_code, type) VALUES (@c, @code, 'truck') RETURNING id",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@code", $"DPV-{Guid.NewGuid():N}".Substring(0, 12)); });
        var fenceId = await db.InsertAsync(
            @"INSERT INTO geofences (company_id, name, geofence_type, center_lat, center_lng, radius_meters, status, customer_id, site_role)
              VALUES (@c, 'DP DC', 'circular', 34.05, -118.24, 300, 'Active', @cust, 'customer_site') RETURNING id",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@cust", custId); });
        return (cid, custId, vid, fenceId);
    }

    private static Task SeedRuleCardAsync(Database db, long cid, long custId, int freeMinutes, decimal ratePerHour, int incrementMinutes) =>
        db.ExecuteAsync(
            @"INSERT INTO detention_rule_cards (company_id, scope_type, scope_id, free_minutes, rate_per_hour, billing_increment_minutes)
              VALUES (@c, 'customer', @cust, @free, @rate, @inc)",
            c =>
            {
                c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@cust", custId);
                c.Parameters.AddWithValue("@free", freeMinutes); c.Parameters.AddWithValue("@rate", ratePerHour);
                c.Parameters.AddWithValue("@inc", incrementMinutes);
            });

    private static async Task<long> SeedEventAsync(Database db, long cid, long fenceId, long vid, string type, DateTime at) =>
        await db.InsertAsync(
            @"INSERT INTO geofence_events (company_id, geofence_id, vehicle_id, event_type, event_time)
              VALUES (@c, @g, @v, @t, @at) RETURNING id",
            c =>
            {
                c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@g", fenceId);
                c.Parameters.AddWithValue("@v", vid); c.Parameters.AddWithValue("@t", type);
                c.Parameters.AddWithValue("@at", at);
            });

    private static async Task CleanupAsync(Database db, long cid)
    {
        foreach (var t in new[] { "detention_notices", "detention_dwell_events", "detention_dwells", "detention_rule_cards",
                                  "notifications", "geofence_events", "geofences", "dispatch_assignments", "jobs", "vehicles", "customers" })
            await db.ExecuteAsync($"DELETE FROM {t} WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", cid));
        await db.ExecuteAsync("DELETE FROM companies WHERE id=@c", c => c.Parameters.AddWithValue("@c", cid));
    }

    private static Database CreateDatabase() =>
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString }).Build());
}
