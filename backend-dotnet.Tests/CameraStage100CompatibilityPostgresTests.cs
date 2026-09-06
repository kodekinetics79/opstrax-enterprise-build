using System.Security.Cryptography;
using System.Text;
using Npgsql;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Opstrax.Tests;

// SQL-shape characterization only: not registered API/audit, provider, footage or RLS evidence.
// Every database object belongs to a generated schema; the full migration is never applied.
[Trait("Category", "Integration")]
public sealed class CameraStage100CompatibilityPostgresTests(ITestOutputHelper output)
{
    private const string MigrationName = "2026_09_03_stage100_dashcam_provider_media_truth.sql";
    private const string VersionStatement = "  NEW.row_version := COALESCE(NEW.row_version,0) + 1;";
    private const string ConfidenceStatement = "    NEW.ai_confidence := NULL;";

    [Fact]
    public void ActualSourceExtractionRefusesMissingOrAmbiguousAnchors()
    {
        var source = ReadSource();
        var extracted = Extract(source);
        Assert.Contains(VersionStatement, extracted.FunctionAndTrigger, StringComparison.Ordinal);
        Assert.Contains("VALIDATE CONSTRAINT ck_dashcam_ready_media_reference;", extracted.Constraints, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => Extract(source.Replace("DO $stage100$", "DO $changed$", StringComparison.Ordinal)));
        Assert.Throws<InvalidOperationException>(() => Extract(source + "\nDO $stage100$"));
        Assert.Throws<InvalidOperationException>(() => Extract(source + "\n" + extracted.FunctionAndTrigger));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Host=remote.example.test;Port=5433;Database=opstrax_local")]
    [InlineData("Host=127.0.0.1,remote.example.test;Port=5433;Database=opstrax_local")]
    [InlineData("Host=localhost;Port=5433;Database=opstrax_local")]
    [InlineData("Host=127.0.0.1;Port=5432;Database=opstrax_local")]
    [InlineData("Host=127.0.0.1;Port=5433;Database=production")]
    public void ConnectionGuardRefusesUnapprovedTargetsWithoutConnecting(string? configured)
        => Assert.Throws<InvalidOperationException>(() => LocalConnection(configured));

    [Fact]
    public async Task DefaultInsertAndOrdinaryUpdateReturnActualStoredVersions()
    {
        await using var fixture = await Fixture.CreateAsync(output);
        var inserted = await fixture.VersionAsync("INSERT INTO dashcam_events(title) VALUES('synthetic manual') RETURNING row_version");
        Assert.Equal(1L, inserted);
        Assert.Equal(inserted, await fixture.VersionAsync("SELECT row_version FROM dashcam_events"));
        var updated = await fixture.VersionAsync("UPDATE dashcam_events SET title='synthetic changed' WHERE id=1 RETURNING row_version");
        Assert.Equal(2L, updated);
        Assert.Equal(updated, await fixture.VersionAsync("SELECT row_version FROM dashcam_events"));
    }

    [Fact]
    public async Task HistoricalExplicitIncrementIsAnIncompatibilityControlNotTheNewWriter()
    {
        await using var fixture = await Fixture.CreateAsync(output);
        Assert.Equal(1L, await fixture.VersionAsync("INSERT INTO dashcam_events(row_version) VALUES(0) RETURNING row_version"));
        var historical = await fixture.VersionAsync("UPDATE dashcam_events SET row_version=row_version+1 WHERE id=1 RETURNING row_version");
        Assert.Equal(3L, historical);
        Assert.ThrowsAny<XunitException>(() => Assert.Equal(2L, historical));
        output.WriteLine("CONTROL: historical application increment advances twice with unchanged Stage 100; not a production RED.");
    }

    [Fact]
    public async Task ExpectedVersionPredicateHasOneConcurrentWinnerAndStaleNonWinner()
    {
        await using var fixture = await Fixture.CreateAsync(output);
        await fixture.ExecuteAsync("INSERT INTO dashcam_events(title) VALUES('before')");
        await using var first = await fixture.OpenScopedAsync();
        await using var second = await fixture.OpenScopedAsync();
        static async Task<object?> Update(NpgsqlConnection connection, string title)
        {
            await using var command = new NpgsqlCommand("UPDATE dashcam_events SET title=@title WHERE id=1 AND row_version=1 RETURNING row_version", connection);
            command.Parameters.AddWithValue("title", title);
            return await command.ExecuteScalarAsync();
        }
        var results = await Task.WhenAll(Update(first, "winner A"), Update(second, "winner B"));
        Assert.Single(results, value => value is long version && version == 2);
        Assert.Single(results, value => value is null);
        Assert.Equal(2L, await fixture.VersionAsync("SELECT row_version FROM dashcam_events"));
        Assert.Null(await fixture.ScalarAsync("UPDATE dashcam_events SET title='stale' WHERE id=1 AND row_version=1 RETURNING row_version"));
        Assert.Equal(2L, await fixture.VersionAsync("SELECT row_version FROM dashcam_events"));
        Assert.NotEqual("stale", await fixture.ScalarAsync("SELECT title FROM dashcam_events"));
    }

    [Fact]
    public async Task RolledBackUpdateAndInsertLeaveNoRowOrVersionChange()
    {
        await using var fixture = await Fixture.CreateAsync(output);
        await fixture.ExecuteAsync("INSERT INTO dashcam_events(title) VALUES('before')");
        await using (var transaction = await fixture.Connection.BeginTransactionAsync())
        {
            Assert.Equal(2L, await fixture.VersionAsync("UPDATE dashcam_events SET title='rolled back' WHERE id=1 AND row_version=1 RETURNING row_version", transaction));
            Assert.Equal(1L, await fixture.VersionAsync("INSERT INTO dashcam_events(title) VALUES('rolled back insert') RETURNING row_version", transaction));
            await transaction.RollbackAsync();
        }
        Assert.Equal(1L, await fixture.VersionAsync("SELECT COUNT(*) FROM dashcam_events"));
        Assert.Equal(1L, await fixture.VersionAsync("SELECT row_version FROM dashcam_events"));
        Assert.Equal("before", await fixture.ScalarAsync("SELECT title FROM dashcam_events"));
    }

    [Theory]
    [InlineData("LegacyUnverified", "LegacyUnverified", "Unavailable")]
    [InlineData("ProviderPending", "ProviderPending", "ProviderPending")]
    [InlineData("unknown", "LegacyUnverified", "Unavailable")]
    [InlineData("", "LegacyUnverified", "Unavailable")]
    public async Task NonAuthoritativeInsertAndUpdateWithholdActualMediaAndConfidenceFields(string requested, string authority, string media)
    {
        await using var fixture = await Fixture.CreateAsync(output);
        await fixture.InsertUnverifiedAsync(requested);
        await AssertWithheld(fixture, authority, media, 1);
        await fixture.ExecuteAsync("""
            UPDATE dashcam_events SET road_facing_clip_url='https://synthetic.invalid/road',
              driver_facing_clip_url='https://synthetic.invalid/driver', thumbnail_url='https://synthetic.invalid/thumbnail',
              road_facing_media_ref='synthetic-road', driver_facing_media_ref='synthetic-driver',
              ai_confidence=99, provider_event_id='synthetic-event', provider_received_at=NOW(),
              media_expires_at=NOW(), provider_payload_hash=repeat('a',64), media_status='Ready',
              video_provider='OpsTrax Placeholder'
            WHERE id=1
            """);
        await AssertWithheld(fixture, authority, media, 2);
    }

    [Fact]
    public async Task SyntheticAuthoritativeReadyShapePreservesFieldsAndAdvancesOnce()
    {
        await using var fixture = await Fixture.CreateAsync(output);
        await fixture.ExecuteAsync(AuthoritativeInsert());
        Assert.Equal(1L, await fixture.VersionAsync("SELECT row_version FROM dashcam_events"));
        Assert.Equal("synthetic-provider", await fixture.ScalarAsync("SELECT video_provider FROM dashcam_events"));
        Assert.Equal("synthetic-road-ref", await fixture.ScalarAsync("SELECT road_facing_media_ref FROM dashcam_events"));
        Assert.Equal("Authoritative", await fixture.ScalarAsync("SELECT source_authority FROM dashcam_events"));
        Assert.Equal("Ready", await fixture.ScalarAsync("SELECT media_status FROM dashcam_events"));
        Assert.Equal(72m, await fixture.ScalarAsync("SELECT ai_confidence FROM dashcam_events"));
        Assert.Equal(2L, await fixture.VersionAsync("UPDATE dashcam_events SET title='synthetic annotation shape only' WHERE id=1 RETURNING row_version"));
        Assert.Equal("synthetic-road-ref", await fixture.ScalarAsync("SELECT road_facing_media_ref FROM dashcam_events"));
        output.WriteLine("Synthetic schema fixture only; no provider, media, human annotation authority or footage attestation.");
    }

    [Theory]
    [InlineData("provider", "ck_dashcam_source_authority")]
    [InlineData("event", "ck_dashcam_source_authority")]
    [InlineData("received", "ck_dashcam_source_authority")]
    [InlineData("hash", "ck_dashcam_source_authority")]
    [InlineData("status", "ck_dashcam_source_authority")]
    [InlineData("ready-ref", "ck_dashcam_ready_media_reference")]
    public async Task MalformedSyntheticAuthoritativeOrReadyShapeFailsActualConstraint(string defect, string constraint)
    {
        await using var fixture = await Fixture.CreateAsync(output);
        await AssertConstraintRefusal(fixture, AuthoritativeInsert(defect), constraint);
        Assert.Equal(0L, await fixture.VersionAsync("SELECT COUNT(*) FROM dashcam_events"));
    }

    [Fact]
    public async Task VersionEnforcementNegativeControlFailsTheSameInitialVersionOracle()
    {
        await using var fixture = await Fixture.CreateAsync(output);
        await fixture.ReplaceFunctionForNegativeControlAsync(VersionStatement);
        var actual = await fixture.VersionAsync("INSERT INTO dashcam_events DEFAULT VALUES RETURNING row_version");
        Assert.Equal(0L, actual);
        Assert.ThrowsAny<XunitException>(() => Assert.Equal(1L, actual));
    }

    [Fact]
    public async Task MediaEnforcementNegativeControlFailsTheSameWithholdingOracle()
    {
        await using var fixture = await Fixture.CreateAsync(output);
        await fixture.ReplaceFunctionForNegativeControlAsync(ConfidenceStatement);
        await fixture.InsertUnverifiedAsync("LegacyUnverified");
        Assert.Equal(99m, await fixture.ScalarAsync("SELECT ai_confidence FROM dashcam_events"));
        await Assert.ThrowsAnyAsync<XunitException>(() => AssertWithheld(fixture, "LegacyUnverified", "Unavailable", 1));
    }

    [Theory]
    [InlineData("ck_dashcam_source_authority", "hash")]
    [InlineData("ck_dashcam_ready_media_reference", "ready-ref")]
    public async Task ConstraintRemovalNegativeControlFailsTheSameRefusalOracle(string constraint, string defect)
    {
        await using var fixture = await Fixture.CreateAsync(output);
        // Only this fixture's actual constraint is removed; no shared/public relation is touched.
        await fixture.ExecuteAsync($"ALTER TABLE dashcam_events DROP CONSTRAINT {constraint}");
        await Assert.ThrowsAnyAsync<XunitException>(() => AssertConstraintRefusal(fixture, AuthoritativeInsert(defect), constraint));
        Assert.Equal(1L, await fixture.VersionAsync("SELECT COUNT(*) FROM dashcam_events"));
    }

    private static async Task AssertConstraintRefusal(Fixture fixture, string sql, string constraint)
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() => fixture.ExecuteAsync(sql));
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        Assert.Equal(constraint, error.ConstraintName);
    }

    private static async Task AssertWithheld(Fixture fixture, string authority, string media, long version)
    {
        foreach (var column in new[] { "road_facing_clip_url", "driver_facing_clip_url", "thumbnail_url", "road_facing_media_ref", "driver_facing_media_ref", "ai_confidence", "video_provider", "provider_event_id", "provider_received_at", "media_expires_at", "provider_payload_hash" })
            Assert.Equal(DBNull.Value, await fixture.ScalarAsync($"SELECT {column} FROM dashcam_events WHERE id=1"));
        Assert.Equal(authority, await fixture.ScalarAsync("SELECT source_authority FROM dashcam_events WHERE id=1"));
        Assert.Equal(media, await fixture.ScalarAsync("SELECT media_status FROM dashcam_events WHERE id=1"));
        Assert.Equal(version, await fixture.VersionAsync("SELECT row_version FROM dashcam_events WHERE id=1"));
    }

    private static string AuthoritativeInsert(string? defect = null)
    {
        var provider = defect == "provider" ? "NULL" : "'synthetic-provider'";
        var eventId = defect == "event" ? "NULL" : "'synthetic-event'";
        var received = defect == "received" ? "NULL" : "NOW()";
        var hash = defect == "hash" ? "'not-a-hash'" : "repeat('a',64)";
        var status = defect == "status" ? "'invented-status'" : "'Ready'";
        var reference = defect == "ready-ref" ? "NULL" : "'synthetic-road-ref'";
        return $"""
            INSERT INTO dashcam_events(source_authority,video_provider,provider_event_id,provider_received_at,
              provider_payload_hash,media_status,road_facing_media_ref,ai_confidence)
            VALUES('Authoritative',{provider},{eventId},{received},{hash},{status},{reference},72)
            """;
    }

    private static string ReadSource() => File.ReadAllText(Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "../../../../database/migrations", MigrationName)));

    private sealed record SourceParts(string Columns, string FunctionAndTrigger, string Constraints);

    private static SourceParts Extract(string source) => new(
        ExactSlice(source, "ALTER TABLE dashcam_events\n  ADD COLUMN IF NOT EXISTS branch_id", "  ALTER COLUMN ai_confidence DROP NOT NULL;"),
        ExactSlice(source, "CREATE OR REPLACE FUNCTION stage100_enforce_dashcam_provider_truth()", "FOR EACH ROW EXECUTE FUNCTION stage100_enforce_dashcam_provider_truth();"),
        ExactSlice(source, "DO $stage100$", "ALTER TABLE dashcam_events VALIDATE CONSTRAINT ck_dashcam_ready_media_reference;"));

    private static string ExactSlice(string source, string start, string end)
    {
        var first = source.IndexOf(start, StringComparison.Ordinal);
        var last = source.IndexOf(end, StringComparison.Ordinal);
        if (first < 0 || last < first || source.IndexOf(start, first + start.Length, StringComparison.Ordinal) >= 0 || source.IndexOf(end, last + end.Length, StringComparison.Ordinal) >= 0)
            throw new InvalidOperationException("Stage 100 extraction requires unique, ordered exact source anchors.");
        return source[first..(last + end.Length)];
    }

    private static NpgsqlConnectionStringBuilder LocalConnection(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured)) throw new InvalidOperationException("Explicit disposable-local OPSTRAX_TEST_DB is required.");
        var builder = new NpgsqlConnectionStringBuilder(configured);
        if (builder.Host is not ("127.0.0.1" or "::1") || builder.Port != 5433 || builder.Database != "opstrax_local")
            throw new InvalidOperationException("Camera compatibility tests require the approved numeric-loopback, port 5433, opstrax_local target.");
        builder.Pooling = false;
        builder.Timeout = 5;
        builder.CommandTimeout = 10;
        builder.IncludeErrorDetail = false;
        builder.ApplicationName = "opstrax-camera-stage100-compatibility";
        return builder;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _schema = $"camera_stage100_{Guid.NewGuid():N}";
        private readonly NpgsqlConnectionStringBuilder _settings;
        private readonly ITestOutputHelper _output;
        private readonly SourceParts _source;
        private bool _schemaCreated;
        public NpgsqlConnection Connection { get; }

        private Fixture(NpgsqlConnectionStringBuilder settings, ITestOutputHelper testOutput)
        {
            _settings = settings;
            _settings.SearchPath = $"{_schema},pg_catalog";
            _output = testOutput;
            _source = Extract(ReadSource());
            Connection = new NpgsqlConnection(_settings.ConnectionString);
        }

        public static async Task<Fixture> CreateAsync(ITestOutputHelper output)
        {
            var fixture = new Fixture(LocalConnection(Environment.GetEnvironmentVariable("OPSTRAX_TEST_DB")), output);
            try
            {
                await fixture.Connection.OpenAsync();
                await fixture.ExecuteAsync($"CREATE SCHEMA {fixture._schema}");
                fixture._schemaCreated = true;
                Assert.Equal(fixture._schema, await fixture.ScalarAsync("SELECT current_schema()"));
                await fixture.ExecuteAsync("SET lock_timeout='3s'; SET statement_timeout='10s';");
                await fixture.ExecuteAsync("""
                    CREATE TABLE dashcam_events(
                      id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY, title TEXT NULL,
                      video_provider VARCHAR(120) NOT NULL DEFAULT 'OpsTrax Placeholder',
                      ai_confidence DECIMAL(6,2) NOT NULL DEFAULT 84,
                      road_facing_clip_url TEXT NULL, driver_facing_clip_url TEXT NULL, thumbnail_url TEXT NULL)
                    """);
                await fixture.ExecuteAsync(fixture._source.Columns);
                await fixture.ExecuteAsync(fixture._source.FunctionAndTrigger);
                await fixture.ExecuteAsync(fixture._source.Constraints);
                await fixture.VerifyCatalogOwnershipAsync();
                output.WriteLine($"FIXTURE {fixture._schema}; exact migration SHA256 {Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ReadSource()))).ToLowerInvariant()}; synthetic rows only.");
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        public async Task<NpgsqlConnection> OpenScopedAsync()
        {
            var connection = new NpgsqlConnection(_settings.ConnectionString);
            try
            {
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand("SET lock_timeout='3s'; SET statement_timeout='10s';", connection);
                await command.ExecuteNonQueryAsync();
                return connection;
            }
            catch { await connection.DisposeAsync(); throw; }
        }

        private async Task VerifyCatalogOwnershipAsync()
        {
            Assert.Equal(_schema, await ScalarAsync("SELECT n.nspname FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE c.oid='dashcam_events'::regclass"));
            Assert.Equal(_schema, await ScalarAsync("SELECT n.nspname FROM pg_trigger t JOIN pg_proc p ON p.oid=t.tgfoid JOIN pg_namespace n ON n.oid=p.pronamespace WHERE t.tgrelid='dashcam_events'::regclass AND t.tgname='trg_stage100_enforce_dashcam_provider_truth'"));
            Assert.Equal(2L, await VersionAsync("SELECT COUNT(*) FROM pg_constraint WHERE conrelid='dashcam_events'::regclass AND convalidated AND conname IN ('ck_dashcam_source_authority','ck_dashcam_ready_media_reference')"));
        }

        public async Task InsertUnverifiedAsync(string authority)
        {
            await using var command = new NpgsqlCommand("""
                INSERT INTO dashcam_events(source_authority,video_provider,road_facing_clip_url,driver_facing_clip_url,
                  thumbnail_url,road_facing_media_ref,driver_facing_media_ref,ai_confidence,provider_event_id,
                  provider_received_at,media_expires_at,provider_payload_hash,media_status)
                VALUES(@authority,'OpsTrax Placeholder','https://synthetic.invalid/road','https://synthetic.invalid/driver',
                  'https://synthetic.invalid/thumbnail','synthetic-road','synthetic-driver',99,'synthetic-event',
                  NOW(),NOW(),repeat('a',64),'Ready')
                """, Connection);
            command.Parameters.AddWithValue("authority", authority);
            await command.ExecuteNonQueryAsync();
        }

        public async Task ReplaceFunctionForNegativeControlAsync(string statement)
        {
            // Derived from actual source, not a hand-coded replacement implementation.
            var function = ExactSlice(_source.FunctionAndTrigger, "CREATE OR REPLACE FUNCTION stage100_enforce_dashcam_provider_truth()", "$fn$;");
            Assert.Equal(1, function.Split(statement, StringSplitOptions.None).Length - 1);
            await ExecuteAsync(function.Replace(statement, "", StringComparison.Ordinal));
            await VerifyCatalogOwnershipAsync();
            _output.WriteLine($"NEGATIVE CONTROL: removed one exact statement in {_schema} function only: {statement.Trim()}");
        }

        public async Task ExecuteAsync(string sql)
        {
            await using var command = new NpgsqlCommand(sql, Connection);
            await command.ExecuteNonQueryAsync();
        }

        public async Task<object?> ScalarAsync(string sql, NpgsqlTransaction? transaction = null)
        {
            await using var command = new NpgsqlCommand(sql, Connection, transaction);
            return await command.ExecuteScalarAsync();
        }

        public async Task<long> VersionAsync(string sql, NpgsqlTransaction? transaction = null)
            => Assert.IsType<long>(await ScalarAsync(sql, transaction));

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_schemaCreated)
                {
                    if (Connection.State != System.Data.ConnectionState.Open) await Connection.OpenAsync();
                    await ExecuteAsync("ROLLBACK; SET search_path TO pg_catalog;");
                    // _schema is generated here, never taken from connection configuration or caller input.
                    await ExecuteAsync($"DROP SCHEMA {_schema} CASCADE");
                    await using var command = new NpgsqlCommand("SELECT COUNT(*) FROM pg_namespace WHERE nspname=@schema", Connection);
                    command.Parameters.AddWithValue("schema", _schema);
                    Assert.Equal(0L, await command.ExecuteScalarAsync());
                    _schemaCreated = false;
                    _output.WriteLine($"CLEANUP {_schema}: zero schema/table/function/constraint residue; no pooled connections.");
                }
            }
            finally { await Connection.DisposeAsync(); }
        }
    }
}
