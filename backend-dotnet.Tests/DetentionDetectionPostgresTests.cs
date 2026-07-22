using Microsoft.Extensions.Configuration;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

// Detention Recovery — detection core (Phases A/B/C, consultant-signed spec). Locks the two
// consultant BLOCKERS: (1) a bounce re-entry can never seed a duplicate dwell (consumed-event
// ledger), and (2) a merged/reopened dwell can never re-close on its superseded Exit. Plus the
// timeout close and the fail-closed fence gate (only customer_site fences with a customer open dwells).
[Trait("Category", "Integration")]
public class DetentionDetectionPostgresTests
{
    [Fact]
    public async Task Entry_Then_Exit_Creates_One_Closed_Dwell_With_Consumed_Events()
    {
        var db = CreateDatabase();
        await new DetentionSchemaService(db).EnsureAsync();
        var (cid, vid, fenceId) = await SeedAsync(db);
        try
        {
            var e1 = await SeedEventAsync(db, cid, fenceId, vid, "Entry", DateTime.UtcNow.AddHours(-3));
            await DetentionService.DetectAsync(db);
            var x1 = await SeedEventAsync(db, cid, fenceId, vid, "Exit", DateTime.UtcNow.AddHours(-1));
            await DetentionService.DetectAsync(db);

            var dwell = (await db.QuerySingleAsync(
                "SELECT id, status, close_reason FROM detention_dwells WHERE company_id=@c AND vehicle_id=@v",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@v", vid); }))!;
            Assert.Equal("closed", dwell["status"]?.ToString());
            Assert.Equal("exit_event", dwell["closeReason"]?.ToString());

            // Both events consumed exactly once, with the right roles.
            var roles = await db.QueryAsync(
                "SELECT geofence_event_id, consume_role FROM detention_dwell_events WHERE company_id=@c ORDER BY event_time",
                c => c.Parameters.AddWithValue("@c", cid));
            Assert.Equal(2, roles.Count);
            Assert.Equal("open", roles[0]["consumeRole"]?.ToString());
            Assert.Equal("close", roles[1]["consumeRole"]?.ToString());

            // Re-running detection is a no-op (idempotent — everything consumed).
            await DetentionService.DetectAsync(db);
            var dwells = await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM detention_dwells WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", cid));
            Assert.Equal(1, dwells);
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task Bounce_ReEntry_Merges_And_Superseded_Exit_Never_ReCloses()
    {
        var db = CreateDatabase();
        await new DetentionSchemaService(db).EnsureAsync();
        var (cid, vid, fenceId) = await SeedAsync(db);
        try
        {
            var t0 = DateTime.UtcNow.AddHours(-4);
            // Tick 1: Entry -> open. Tick 2: Exit -> closed.
            await SeedEventAsync(db, cid, fenceId, vid, "Entry", t0);
            await DetentionService.DetectAsync(db);
            var x1 = await SeedEventAsync(db, cid, fenceId, vid, "Exit", t0.AddMinutes(90));
            await DetentionService.DetectAsync(db);
            // Tick 3: GPS-jitter re-entry 5 minutes later (inside the 10-min merge gap) -> REOPEN, not a new dwell.
            await SeedEventAsync(db, cid, fenceId, vid, "Entry", t0.AddMinutes(95));
            await DetentionService.DetectAsync(db);

            var afterMerge = (await db.QuerySingleAsync(
                "SELECT status, exited_at FROM detention_dwells WHERE company_id=@c AND vehicle_id=@v",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@v", vid); }))!;
            Assert.Equal("open", afterMerge["status"]?.ToString());   // reopened, exit cleared
            Assert.True(afterMerge["exitedAt"] is null or DBNull);

            // The cleared Exit is ledgered as superseded — the dwell must NOT re-close on it.
            var superseded = await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM detention_dwell_events WHERE company_id=@c AND geofence_event_id=@ge AND consume_role='superseded_exit'",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@ge", x1); });
            Assert.Equal(1, superseded);
            await DetentionService.DetectAsync(db);   // extra tick: must stay open (blocker #2 regression)
            var stillOpen = (await db.QuerySingleAsync(
                "SELECT status FROM detention_dwells WHERE company_id=@c AND vehicle_id=@v",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@v", vid); }))!["status"]?.ToString();
            Assert.Equal("open", stillOpen);

            // Tick 4: the real final Exit closes the merged dwell at ITS time.
            var x2Time = t0.AddMinutes(200);
            await SeedEventAsync(db, cid, fenceId, vid, "Exit", x2Time);
            await DetentionService.DetectAsync(db);

            var final = (await db.QuerySingleAsync(
                "SELECT status, billed_to_at FROM detention_dwells WHERE company_id=@c AND vehicle_id=@v",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@v", vid); }))!;
            Assert.Equal("closed", final["status"]?.ToString());
            Assert.Equal(x2Time, ((DateTime)final["billedToAt"]!).ToUniversalTime(), TimeSpan.FromSeconds(2));

            // ONE dwell total — the bounce never fragmented into a second dwell (blocker #1 regression).
            var dwells = await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM detention_dwells WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", cid));
            Assert.Equal(1, dwells);
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task Entry_Inside_A_Charged_Dwells_Interval_Is_Absorbed_Not_A_New_Dwell()
    {
        var db = CreateDatabase();
        await new DetentionSchemaService(db).EnsureAsync();
        var (cid, vid, fenceId) = await SeedAsync(db);
        try
        {
            var t0 = DateTime.UtcNow.AddHours(-6);
            await SeedEventAsync(db, cid, fenceId, vid, "Entry", t0);
            await DetentionService.DetectAsync(db);
            await SeedEventAsync(db, cid, fenceId, vid, "Exit", t0.AddHours(3));
            await DetentionService.DetectAsync(db);

            // Simulate the dwell advancing past merge-eligibility (priced -> charged path).
            await db.ExecuteAsync("UPDATE detention_dwells SET status='priced_pending_review' WHERE company_id=@c",
                c => c.Parameters.AddWithValue("@c", cid));

            // A late-arriving duplicate Entry INSIDE the dwell's interval (the exact blocker-#1 scenario).
            await SeedEventAsync(db, cid, fenceId, vid, "Entry", t0.AddHours(1));
            await DetentionService.DetectAsync(db);

            var dwells = await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM detention_dwells WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", cid));
            Assert.Equal(1, dwells);   // no duplicate dwell, no double-charge path
            var absorbed = await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM detention_dwell_events WHERE company_id=@c AND consume_role='absorbed_post_close'",
                c => c.Parameters.AddWithValue("@c", cid));
            Assert.Equal(1, absorbed);
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task NonCustomer_Fence_Never_Opens_A_Dwell_And_Timeout_Forces_Review()
    {
        var db = CreateDatabase();
        await new DetentionSchemaService(db).EnsureAsync();
        var (cid, vid, fenceId) = await SeedAsync(db);
        try
        {
            // A plain fence (no site_role/customer) must never produce a dwell — fail-closed.
            var plainFence = await db.InsertAsync(
                @"INSERT INTO geofences (company_id, name, geofence_type, center_lat, center_lng, radius_meters, status)
                  VALUES (@c, 'Plain', 'circular', 1, 1, 100, 'Active') RETURNING id",
                c => c.Parameters.AddWithValue("@c", cid));
            await SeedEventAsync(db, cid, plainFence, vid, "Entry", DateTime.UtcNow.AddHours(-2));
            await DetentionService.DetectAsync(db);
            Assert.Equal(0, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM detention_dwells WHERE company_id=@c AND geofence_id=@g",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@g", plainFence); }));

            // Timeout: a dwell open past max_dwell_hours closes with forced review.
            await SeedEventAsync(db, cid, fenceId, vid, "Entry", DateTime.UtcNow.AddHours(-30));
            await DetentionService.DetectAsync(db);
            var d = (await db.QuerySingleAsync(
                "SELECT status, close_reason, review_required FROM detention_dwells WHERE company_id=@c AND geofence_id=@g",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@g", fenceId); }))!;
            Assert.Equal("closed", d["status"]?.ToString());
            Assert.Equal("timeout", d["closeReason"]?.ToString());
            Assert.True((bool)d["reviewRequired"]!);
        }
        finally { await CleanupAsync(db, cid); }
    }

    // ── helpers ───────────────────────────────────────────────────────────────
    private static async Task<(long cid, long vid, long fenceId)> SeedAsync(Database db)
    {
        var cid = await db.InsertAsync("INSERT INTO companies (company_code, name, industry) VALUES (@code, 'DW Co', 'logistics') RETURNING id",
            c => c.Parameters.AddWithValue("@code", $"DW-{Guid.NewGuid():N}".Substring(0, 15)));
        var custId = await db.InsertAsync(
            "INSERT INTO customers (company_id, customer_code, name) VALUES (@c, @code, 'DW Cust') RETURNING id",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@code", $"DWC-{Guid.NewGuid():N}".Substring(0, 14)); });
        var vid = await db.InsertAsync(
            "INSERT INTO vehicles (company_id, vehicle_code, type) VALUES (@c, @code, 'truck') RETURNING id",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@code", $"DWV-{Guid.NewGuid():N}".Substring(0, 12)); });
        var fenceId = await db.InsertAsync(
            @"INSERT INTO geofences (company_id, name, geofence_type, center_lat, center_lng, radius_meters, status, customer_id, site_role)
              VALUES (@c, 'Customer DC', 'circular', 34.05, -118.24, 300, 'Active', @cust, 'customer_site') RETURNING id",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@cust", custId); });
        return (cid, vid, fenceId);
    }

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
        foreach (var t in new[] { "detention_dwell_events", "detention_dwells", "detention_rule_cards",
                                  "geofence_events", "geofences", "vehicles", "customers" })
            await db.ExecuteAsync($"DELETE FROM {t} WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", cid));
        await db.ExecuteAsync("DELETE FROM companies WHERE id=@c", c => c.Parameters.AddWithValue("@c", cid));
    }

    private static Database CreateDatabase() =>
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString }).Build());
}
