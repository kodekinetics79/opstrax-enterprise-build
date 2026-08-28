using Npgsql;
using Opstrax.Telematics.Gateway.Security.Replay;
using Xunit.Abstractions;

namespace Opstrax.Telematics.IntegrationTests;

/// <summary>
/// CERTIFICATION of migration <c>telematics/007_replay_session_epoch.sql</c> and of the durable
/// replay subsystem that depends on it, against a disposable Postgres schema.
/// </summary>
/// <remarks>
/// The migration files are applied VERBATIM from the repository rather than re-typed as inline
/// DDL. A migration certified against a hand-written approximation of itself is not certified.
/// </remarks>
public sealed class CertificationMigrationTests
{
    private readonly ITestOutputHelper _out;

    public CertificationMigrationTests(ITestOutputHelper output) => _out = output;

    private const long Gt06Modulus = 65_536;

    // ── Schema shape, idempotency and compatibility ───────────────────────────

    [Fact]
    public async Task M1_Migration_007_is_additive_idempotent_and_creates_exactly_what_it_claims()
    {
        await using var db = await Schema.CreateAsync();
        await db.ApplyMigrationAsync("005_replay_guard.sql");

        string before = await db.ColumnsAsync("telemetry_replay_device_state");
        _out.WriteLine($"columns before 007 : {before}");

        await db.ApplyMigrationAsync("007_replay_session_epoch.sql");
        string after = await db.ColumnsAsync("telemetry_replay_device_state");
        _out.WriteLine($"columns after  007 : {after}");

        // Additive only: every pre-existing column survives, exactly two are added.
        foreach (string col in before.Split(", ", StringSplitOptions.RemoveEmptyEntries))
            Assert.Contains(col, after.Split(", ", StringSplitOptions.RemoveEmptyEntries));
        Assert.Contains("pending_epoch_base", after);
        Assert.Contains("epoch_floor", after);
        Assert.Equal(before.Split(", ").Length + 2, after.Split(", ").Length);

        // Nullability and defaults as designed: the floor must never be null.
        Assert.Equal("YES", await db.ScalarAsync(
            "SELECT is_nullable FROM information_schema.columns WHERE table_schema=current_schema() " +
            "AND table_name='telemetry_replay_device_state' AND column_name='pending_epoch_base'"));
        Assert.Equal("NO", await db.ScalarAsync(
            "SELECT is_nullable FROM information_schema.columns WHERE table_schema=current_schema() " +
            "AND table_name='telemetry_replay_device_state' AND column_name='epoch_floor'"));

        // The cross-epoch lookup index exists and is NOT unique.
        Assert.Equal("false", await db.ScalarAsync(
            "SELECT indisunique::text FROM pg_index WHERE indexrelid = " +
            "(current_schema()||'.idx_telemetry_replay_seen_device_content')::regclass"));

        // Ledger row written once.
        Assert.Equal("1", await db.ScalarAsync(
            "SELECT count(*)::text FROM schema_migrations WHERE version='telematics_007_replay_session_epoch'"));

        // Idempotent: re-running changes nothing.
        await db.ApplyMigrationAsync("007_replay_session_epoch.sql");
        Assert.Equal(after, await db.ColumnsAsync("telemetry_replay_device_state"));
        Assert.Equal("1", await db.ScalarAsync(
            "SELECT count(*)::text FROM schema_migrations WHERE version='telematics_007_replay_session_epoch'"));
    }

    /// <summary>
    /// Migration 007 must be applyable AHEAD of the code that needs it, without breaking the code
    /// currently running. That is what makes a migration-first rollout safe.
    /// </summary>
    [Fact]
    public async Task M2_Code_predating_007_still_works_after_007_is_applied()
    {
        await using var db = await Schema.CreateAsync();
        await db.ApplyMigrationAsync("005_replay_guard.sql");
        await db.ApplyMigrationAsync("007_replay_session_epoch.sql");

        // Exactly the statements the pre-007 guard issued: a SELECT without the new columns, and
        // an INSERT that names only the old ones.
        await db.ExecuteAsync(
            "INSERT INTO telemetry_replay_device_state(device_id,last_raw_serial,high_water_unwrapped,updated_at) " +
            "VALUES('legacy-writer',10,10,NOW())");
        Assert.Equal("10", await db.ScalarAsync(
            "SELECT high_water_unwrapped::text FROM telemetry_replay_device_state WHERE device_id='legacy-writer'"));

        // The added column took its default rather than rejecting the legacy INSERT.
        Assert.Equal("0", await db.ScalarAsync(
            "SELECT epoch_floor::text FROM telemetry_replay_device_state WHERE device_id='legacy-writer'"));

        // And a legacy-shaped read still succeeds.
        Assert.Equal("10", await db.ScalarAsync(
            "SELECT last_raw_serial::text FROM telemetry_replay_device_state WHERE device_id='legacy-writer'"));
    }

    /// <summary>
    /// The candidate must FAIL CLOSED, not silently, when run against a database that has not had
    /// 007 applied. This is the deployment-ordering hazard.
    /// </summary>
    [Fact]
    public async Task M3_Candidate_code_fails_closed_without_migration_007()
    {
        await using var db = await Schema.CreateAsync();
        await db.ApplyMigrationAsync("005_replay_guard.sql");   // deliberately NOT 007

        using var guard = new PostgresReplayGuard(db.ConnectionString, serialModulus: Gt06Modulus);

        PostgresException ex = await Assert.ThrowsAsync<PostgresException>(
            () => guard.CheckAsync("dev-1", 1, "hash-1", DateTime.MinValue));

        // 42703 = undefined_column. Loud and specific, not a wrong answer.
        Assert.Equal("42703", ex.SqlState);
        _out.WriteLine($"without 007 the guard raises: {ex.SqlState} {ex.MessageText}");

        // And the startup readiness contract names the missing column, so this never reaches
        // the frame path in a real deployment.
        string readiness = await File.ReadAllTextAsync(Path.Combine(
            Schema.RepoRoot, "telematics", "src", "Opstrax.Telematics.Gateway",
            "Infrastructure", "ProductionStorageReadinessService.cs"));
        Assert.Contains("('telemetry_replay_device_state','pending_epoch_base')", readiness, StringComparison.Ordinal);
        Assert.Contains("('telemetry_replay_device_state','epoch_floor')", readiness, StringComparison.Ordinal);
    }

    // ── Full device lifecycle against the migrated schema ─────────────────────

    /// <summary>
    /// The sequence a real device produces, end to end on the migrated schema:
    /// login → location → heartbeat → alarm → duplicate → power-cycle reset → stale replay →
    /// disconnect → reconnect. Database state is asserted at each step.
    /// </summary>
    [Fact]
    public async Task M4_Full_device_lifecycle_on_the_migrated_schema()
    {
        await using var db = await Schema.CreateAsync();
        await db.ApplyMigrationAsync("005_replay_guard.sql");
        await db.ApplyMigrationAsync("007_replay_session_epoch.sql");

        using var guard = new PostgresReplayGuard(db.ConnectionString, serialModulus: Gt06Modulus);
        const string device = "cert-device-A";
        DateTime t = new(2024, 1, 15, 10, 20, 30, DateTimeKind.Utc);

        // 1. Login opens an epoch. First-ever login is a no-op: no state row yet.
        await guard.BeginSessionEpochAsync(device);
        Assert.Equal("0", await db.ScalarAsync(
            $"SELECT count(*)::text FROM telemetry_replay_device_state WHERE device_id='{device}'"));

        // 2. Location, 3. heartbeat, 4. alarm — all novel, all accepted.
        ReplayDecision loc = await guard.CheckAsync(device, 100, "loc-100", t);
        ReplayDecision hb = await guard.CheckAsync(device, 101, "hb-101", t.AddSeconds(10));
        ReplayDecision alarm = await guard.CheckAsync(device, 102, "alarm-102", t.AddSeconds(20));
        Assert.All(new[] { loc, hb, alarm }, d => Assert.Equal(ReplayOutcome.Accept, d.Outcome));
        Assert.Equal("3", await db.ScalarAsync(
            $"SELECT count(*)::text FROM telemetry_replay_seen WHERE device_id='{device}'"));

        // 5. Duplicate retransmission keeps the stored identity and creates no second row.
        ReplayDecision dup = await guard.CheckAsync(device, 100, "loc-100", t);
        Assert.Equal(ReplayOutcome.DuplicateReplay, dup.Outcome);
        Assert.Equal(loc.EventId, dup.EventId);
        Assert.Equal("3", await db.ScalarAsync(
            $"SELECT count(*)::text FROM telemetry_replay_seen WHERE device_id='{device}'"));

        long highWaterBefore = long.Parse(await db.ScalarAsync(
            $"SELECT high_water_unwrapped::text FROM telemetry_replay_device_state WHERE device_id='{device}'"));

        // 6. Power cycle: disconnect, reconnect, authenticate. The counter restarts at 1.
        await guard.BeginSessionEpochAsync(device);
        Assert.Equal(((highWaterBefore / Gt06Modulus) + 1) * Gt06Modulus, long.Parse(await db.ScalarAsync(
            $"SELECT epoch_floor::text FROM telemetry_replay_device_state WHERE device_id='{device}'")));

        for (int serial = 1; serial <= 25; serial++)
            Assert.Equal(ReplayOutcome.Accept,
                (await guard.CheckAsync(device, serial, $"post-reboot-{serial}", t.AddMinutes(serial))).Outcome);

        // History preserved: nothing truncated, mark only moved forward.
        Assert.Equal("28", await db.ScalarAsync(
            $"SELECT count(*)::text FROM telemetry_replay_seen WHERE device_id='{device}'"));
        Assert.True(long.Parse(await db.ScalarAsync(
            $"SELECT high_water_unwrapped::text FROM telemetry_replay_device_state WHERE device_id='{device}'"))
            > highWaterBefore);
        Assert.Equal("", await db.ScalarAsync(
            $"SELECT coalesce(pending_epoch_base::text,'') FROM telemetry_replay_device_state WHERE device_id='{device}'"));

        // 7. A frame captured BEFORE the power cycle, replayed after it. Its serial is "ahead" of
        //    the new epoch's mark, so only the content ledger can reject it.
        ReplayDecision stale = await guard.CheckAsync(device, 100, "loc-100", t);
        Assert.Equal(ReplayOutcome.DuplicateReplay, stale.Outcome);
        Assert.Equal(loc.EventId, stale.EventId);
        Assert.Equal("28", await db.ScalarAsync(
            $"SELECT count(*)::text FROM telemetry_replay_seen WHERE device_id='{device}'"));

        _out.WriteLine("lifecycle: login/location/heartbeat/alarm/duplicate/reboot/stale-replay all as specified");
    }

    /// <summary>Two devices must not be able to see or disturb each other's replay state.</summary>
    [Fact]
    public async Task M5_Replay_state_is_isolated_per_device()
    {
        await using var db = await Schema.CreateAsync();
        await db.ApplyMigrationAsync("005_replay_guard.sql");
        await db.ApplyMigrationAsync("007_replay_session_epoch.sql");

        using var guard = new PostgresReplayGuard(db.ConnectionString, serialModulus: Gt06Modulus);
        DateTime t = new(2024, 1, 15, 10, 20, 30, DateTimeKind.Utc);

        await guard.CheckAsync("device-A", 500, "shared-looking-hash-A", t);
        await guard.CheckAsync("device-B", 500, "shared-looking-hash-B", t);

        // The SAME content hash for a different device is a different occurrence, never a duplicate.
        Assert.Equal(ReplayOutcome.Accept,
            (await guard.CheckAsync("device-B", 501, "shared-looking-hash-A", t)).Outcome);

        // An epoch on A does not touch B.
        await guard.BeginSessionEpochAsync("device-A");
        Assert.Equal("0", await db.ScalarAsync(
            "SELECT epoch_floor::text FROM telemetry_replay_device_state WHERE device_id='device-B'"));
        Assert.NotEqual("0", await db.ScalarAsync(
            "SELECT epoch_floor::text FROM telemetry_replay_device_state WHERE device_id='device-A'"));

        // B's counter reset is still (correctly) out of order — it never rebooted.
        Assert.Equal(ReplayOutcome.OutOfOrder,
            (await guard.CheckAsync("device-B", 1, "b-post", t)).Outcome);
    }

    /// <summary>Concurrency across instances is unchanged by 007: one winner, one identity.</summary>
    [Fact]
    public async Task M6_Concurrent_duplicates_still_converge_after_007()
    {
        await using var db = await Schema.CreateAsync();
        await db.ApplyMigrationAsync("005_replay_guard.sql");
        await db.ApplyMigrationAsync("007_replay_session_epoch.sql");

        using var a = new PostgresReplayGuard(db.ConnectionString, serialModulus: Gt06Modulus);
        using var b = new PostgresReplayGuard(db.ConnectionString, serialModulus: Gt06Modulus);

        ReplayDecision[] results = await Task.WhenAll(Enumerable.Range(0, 16)
            .Select(i => (i & 1) == 0 ? a : b)
            .Select(g => g.CheckAsync("race-device", 4242, "same-frame", DateTime.MinValue)));

        Assert.Single(results, r => r.Outcome == ReplayOutcome.Accept);
        Assert.Equal(15, results.Count(r => r.Outcome == ReplayOutcome.DuplicateReplay));
        Assert.Single(results.Select(r => r.EventId).Distinct());
        Assert.Equal("1", await db.ScalarAsync(
            "SELECT count(*)::text FROM telemetry_replay_seen WHERE device_id='race-device'"));
    }

    // ── Disposable schema helper ──────────────────────────────────────────────

    private sealed class Schema : IAsyncDisposable
    {
        public static string RepoRoot =>
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../"));

        private readonly string _admin;
        private readonly string _schema;
        public string ConnectionString { get; }

        private Schema(string admin, string schema)
        {
            _admin = admin;
            _schema = schema;
            ConnectionString = new NpgsqlConnectionStringBuilder(admin) { SearchPath = schema }.ConnectionString;
        }

        public static async Task<Schema> CreateAsync()
        {
            string admin = Environment.GetEnvironmentVariable("OPSTRAX_TEST_DB")
                ?? throw new InvalidOperationException("OPSTRAX_TEST_DB is required for certification DB tests.");
            string name = $"cert_{Guid.NewGuid():N}";
            await using var c = new NpgsqlConnection(admin);
            await c.OpenAsync();
            await using (var cmd = new NpgsqlCommand($"CREATE SCHEMA {name}", c)) await cmd.ExecuteNonQueryAsync();
            var s = new Schema(admin, name);
            // schema_migrations is a platform table the migrations write their ledger row into.
            await s.ExecuteAsync("CREATE TABLE schema_migrations(version text PRIMARY KEY, description text, applied_at timestamptz NOT NULL DEFAULT now())");
            return s;
        }

        /// <summary>Applies a migration FILE from the repository, unmodified.</summary>
        public async Task ApplyMigrationAsync(string fileName)
        {
            string sql = await File.ReadAllTextAsync(Path.Combine(
                RepoRoot, "database", "migrations", "telematics", fileName));
            await ExecuteAsync(sql);
        }

        public async Task ExecuteAsync(string sql)
        {
            await using var c = new NpgsqlConnection(ConnectionString);
            await c.OpenAsync();
            await using var cmd = new NpgsqlCommand(sql, c);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<string> ScalarAsync(string sql)
        {
            await using var c = new NpgsqlConnection(ConnectionString);
            await c.OpenAsync();
            await using var cmd = new NpgsqlCommand(sql, c);
            return Convert.ToString(await cmd.ExecuteScalarAsync()) ?? string.Empty;
        }

        public Task<string> ColumnsAsync(string table) => ScalarAsync(
            "SELECT string_agg(column_name, ', ' ORDER BY ordinal_position) FROM information_schema.columns " +
            $"WHERE table_schema=current_schema() AND table_name='{table}'");

        public async ValueTask DisposeAsync()
        {
            NpgsqlConnection.ClearAllPools();
            await using var c = new NpgsqlConnection(_admin);
            await c.OpenAsync();
            await using var cmd = new NpgsqlCommand($"DROP SCHEMA IF EXISTS {_schema} CASCADE", c);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
