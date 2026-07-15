using Microsoft.Extensions.Configuration;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

// TEL-P1-REPLAY-005 — the required replay test matrix, exercised against a real Postgres via the
// durable guard (GpsGatewayReplayGuard.TryReserveDurableAsync + the gps_gateway_replay table).
// These are Integration tests: they need a Postgres at the local connection string (as the other
// *Postgres* suites do) and are run in CI / locally, not in the no-DB sandbox. The DB-free wiring
// (canonical key, in-tx reserve, ProbeError->503) is covered by TelemetryGatewayReplayTests.
[Trait("Category", "Integration")]
public class TelemetryGatewayReplayPostgresTests
{
    private static readonly string LocalConnectionString = TestDb.ConnectionString;

    private const string Gw = GpsGatewayReplayGuard.DefaultGatewayId;

    // Unique per run so persisted rows never collide across re-runs.
    private static string FreshSig() => Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task First_valid_is_accepted_and_exact_duplicate_is_rejected()
    {
        var db = await ReadyDbAsync();
        var sig = FreshSig();

        Assert.True(await GpsGatewayReplayGuard.TryReserveDurableAsync(db, Gw, sig, Now(), 1, 100));
        Assert.False(await GpsGatewayReplayGuard.TryReserveDurableAsync(db, Gw, sig, Now(), 1, 100));
    }

    [Fact]
    public async Task Concurrent_duplicates_allow_exactly_one()
    {
        var db = await ReadyDbAsync();
        var sig = FreshSig();

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            GpsGatewayReplayGuard.TryReserveDurableAsync(CreateDatabase(), Gw, sig, Now(), 1, 100)));

        Assert.Equal(1, results.Count(accepted => accepted));
    }

    [Fact]
    public async Task Replay_across_two_instances_is_rejected()
    {
        // Two Database instances = two app instances against the same DB. Durability lives in the
        // DB, so instance B must reject a signature instance A already reserved. This also covers
        // "replay after restart" — a fresh process/Database sees the committed row.
        var sig = FreshSig();
        var instanceA = await ReadyDbAsync();
        var instanceB = CreateDatabase();

        Assert.True(await GpsGatewayReplayGuard.TryReserveDurableAsync(instanceA, Gw, sig, Now(), 1, 100));
        Assert.False(await GpsGatewayReplayGuard.TryReserveDurableAsync(instanceB, Gw, sig, Now(), 1, 100));
    }

    [Fact]
    public async Task Same_signature_from_a_different_gateway_scope_is_accepted()
    {
        var db = await ReadyDbAsync();
        var sig = FreshSig();

        Assert.True(await GpsGatewayReplayGuard.TryReserveDurableAsync(db, "gw-A", sig, Now(), 1, 100));
        Assert.True(await GpsGatewayReplayGuard.TryReserveDurableAsync(db, "gw-B", sig, Now(), 1, 100));
    }

    [Fact]
    public async Task Cross_tenant_replay_of_same_signature_is_rejected()
    {
        // A captured signed message maps to exactly one payload -> one device/tenant. Re-presenting
        // that same signature while claiming a different device/company must NOT be re-accepted: the
        // unique key is (gateway_id, signature), independent of the descriptive tenant columns.
        var db = await ReadyDbAsync();
        var sig = FreshSig();

        Assert.True(await GpsGatewayReplayGuard.TryReserveDurableAsync(db, Gw, sig, Now(), deviceId: 1, companyId: 100));
        Assert.False(await GpsGatewayReplayGuard.TryReserveDurableAsync(db, Gw, sig, Now(), deviceId: 2, companyId: 200));
    }

    [Fact]
    public async Task Expired_records_are_pruned_and_fresh_records_are_kept()
    {
        var db = await ReadyDbAsync();
        var oldSig = FreshSig();
        var freshSig = FreshSig();

        // A row well past the 24h retention (>> the 300s freshness window, so pruning it can't
        // reopen a replay), and a fresh one.
        await db.ExecuteAsync(
            @"INSERT INTO gps_gateway_replay (gateway_id, signature, signed_at, received_at)
              VALUES (@gw, @sig, NOW(), NOW() - INTERVAL '25 hours')",
            c => { c.Parameters.AddWithValue("@gw", Gw); c.Parameters.AddWithValue("@sig", oldSig); });
        Assert.True(await GpsGatewayReplayGuard.TryReserveDurableAsync(db, Gw, freshSig, Now(), 1, 100));

        await db.ExecuteAsync("DELETE FROM gps_gateway_replay WHERE received_at < NOW() - INTERVAL '24 hours'");

        Assert.Equal(0, await CountSig(db, oldSig));   // pruned
        Assert.Equal(1, await CountSig(db, freshSig)); // kept
    }

    // ── helpers ──────────────────────────────────────────────────────────────
    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private static async Task<long> CountSig(Database db, string sig) =>
        await db.ScalarLongAsync("SELECT COUNT(*) FROM gps_gateway_replay WHERE signature=@s",
            c => c.Parameters.AddWithValue("@s", sig));

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

    // Ensure the durable table exists (idempotent) exactly as the migration / schema-ensure defines it.
    private static async Task<Database> ReadyDbAsync()
    {
        var db = CreateDatabase();
        await db.ExecuteAsync(
            @"CREATE TABLE IF NOT EXISTS gps_gateway_replay (
                id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                gateway_id VARCHAR(120) NOT NULL DEFAULT 'default',
                signature VARCHAR(256) NOT NULL,
                signed_at TIMESTAMPTZ NOT NULL,
                device_id BIGINT NULL,
                company_id BIGINT NULL,
                received_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                UNIQUE (gateway_id, signature))");
        return db;
    }
}
