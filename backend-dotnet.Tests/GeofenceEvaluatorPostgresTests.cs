using Microsoft.Extensions.Configuration;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

// P1/P2 fix — the geofence feature was dead: the dashboard reads geofence_events but nothing ever wrote
// them. This drives the new evaluator and asserts debounced entry/exit: a vehicle moving into a zone emits
// one Entry (not one per tick), and moving out emits one Exit.
[Trait("Category", "Integration")]
public class GeofenceEvaluatorPostgresTests
{
    [Fact]
    public async Task Vehicle_Entering_Then_Leaving_A_Zone_Emits_One_Entry_Then_One_Exit()
    {
        var db = CreateDatabase();
        var (cid, vid, gid) = await SeedAsync(db);
        try
        {
            // Position at the geofence centre -> inside.
            await UpsertPositionAsync(db, cid, vid, 34.0500, -118.2400);

            await GeofenceEvaluator.EvaluateAsync(db);
            await GeofenceEvaluator.EvaluateAsync(db); // second tick, still inside -> must NOT re-emit Entry

            Assert.Equal(1, await CountAsync(db, cid, gid, vid, "Entry"));
            Assert.Equal(0, await CountAsync(db, cid, gid, vid, "Exit"));

            // Move ~20km away -> outside -> one Exit.
            await UpsertPositionAsync(db, cid, vid, 34.2500, -118.5000);
            await GeofenceEvaluator.EvaluateAsync(db);
            await GeofenceEvaluator.EvaluateAsync(db); // still outside -> no duplicate Exit

            Assert.Equal(1, await CountAsync(db, cid, gid, vid, "Entry"));
            Assert.Equal(1, await CountAsync(db, cid, gid, vid, "Exit"));
        }
        finally { await CleanupAsync(db, cid, vid, gid); }
    }

    private static async Task<long> CountAsync(Database db, long cid, long gid, long vid, string type) =>
        await db.ScalarLongAsync(
            "SELECT COUNT(*) FROM geofence_events WHERE company_id=@c AND geofence_id=@g AND vehicle_id=@v AND event_type=@t",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@g", gid); c.Parameters.AddWithValue("@v", vid); c.Parameters.AddWithValue("@t", type); });

    private static async Task<(long cid, long vid, long gid)> SeedAsync(Database db)
    {
        var cid = await db.InsertAsync("INSERT INTO companies (company_code, name, industry) VALUES (@code, 'Geo Co', 'logistics') RETURNING id",
            c => c.Parameters.AddWithValue("@code", $"GEO-{Guid.NewGuid():N}".Substring(0, 14)));
        var vid = await db.InsertAsync(
            "INSERT INTO vehicles (company_id, vehicle_code, type) VALUES (@c, @code, 'truck') RETURNING id",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@code", $"GV-{Guid.NewGuid():N}".Substring(0, 12)); });
        var gid = await db.InsertAsync(
            @"INSERT INTO geofences (company_id, name, geofence_type, center_lat, center_lng, radius_meters, status)
              VALUES (@c, 'Depot', 'circular', 34.0500, -118.2400, 500, 'Active') RETURNING id",
            c => c.Parameters.AddWithValue("@c", cid));
        return (cid, vid, gid);
    }

    private static Task UpsertPositionAsync(Database db, long cid, long vid, double lat, double lng) =>
        db.ExecuteAsync(
            @"INSERT INTO latest_vehicle_positions (company_id, vehicle_id, lat, lng, event_time)
              VALUES (@c, @v, @lat, @lng, NOW())
              ON CONFLICT (company_id, vehicle_id) DO UPDATE SET lat=EXCLUDED.lat, lng=EXCLUDED.lng, event_time=NOW()",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@v", vid); c.Parameters.AddWithValue("@lat", lat); c.Parameters.AddWithValue("@lng", lng); });

    private static async Task CleanupAsync(Database db, long cid, long vid, long gid)
    {
        await db.ExecuteAsync("DELETE FROM geofence_events WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", cid));
        await db.ExecuteAsync("DELETE FROM latest_vehicle_positions WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", cid));
        await db.ExecuteAsync("DELETE FROM geofences WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", cid));
        await db.ExecuteAsync("DELETE FROM vehicles WHERE id=@v", c => c.Parameters.AddWithValue("@v", vid));
        await db.ExecuteAsync("DELETE FROM companies WHERE id=@c", c => c.Parameters.AddWithValue("@c", cid));
    }

    private static Database CreateDatabase() =>
        new(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString }).Build());
}
