using Microsoft.Extensions.Configuration;
using Npgsql;
using Opstrax.Api;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

[Trait("Category", "Integration")]
public sealed class DiagnosticsIntegrityPostgresTests
{
    [Fact]
    public async Task ConcurrentSourceEventRetry_PersistsEachDtcExactlyOnce()
    {
        var db = CreateDatabase();
        await new MaintenanceSchemaService(db).EnsureAsync();
        var company = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var device = $"diag-concurrency-{Guid.NewGuid():N}";
        var source = $"frame-{Guid.NewGuid():N}";
        try
        {
            var attempts = Enumerable.Range(0, 12).SelectMany(_ => new[]
            {
                InsertOccurrenceAsync(CreateDatabase(), company, device, source, 0, "J1939:ENGINE:SPN:1:FMI:0"),
                InsertOccurrenceAsync(CreateDatabase(), company, device, source, 1, "J1939:ENGINE:SPN:2:FMI:1")
            });
            var results = await Task.WhenAll(attempts);
            Assert.Equal(2, results.Sum());
            Assert.Equal(2, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM fault_occurrences WHERE company_id=@c AND device_id=@d AND source_event_id=@s",
                c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@d", device); c.Parameters.AddWithValue("@s", source); }));
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM fault_occurrences WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", company));
        }
    }

    [Fact]
    public async Task Projection_IsMonotonic_AndEqualTimeUsesSourceEventTieBreak()
    {
        var db = CreateDatabase();
        await new MaintenanceSchemaService(db).EnsureAsync();
        var company = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 1;
        var device = $"diag-order-{Guid.NewGuid():N}";
        const string canonical = "J1939:ENGINE:SPN:100:FMI:4";
        var at = DateTimeOffset.UtcNow.AddMinutes(-2);
        try
        {
            Assert.NotNull(await UpsertProjectionAsync(db, company, device, canonical, "evt-b", at, "Critical"));
            Assert.Null(await UpsertProjectionAsync(db, company, device, canonical, "evt-a", at, "Info"));
            Assert.Null(await UpsertProjectionAsync(db, company, device, canonical, "evt-z-old", at.AddSeconds(-1), "Info"));
            Assert.NotNull(await UpsertProjectionAsync(db, company, device, canonical, "evt-c", at, "Warning"));

            var row = await db.QuerySingleAsync(
                "SELECT severity,last_source_event_id,last_observed_at FROM fault_codes WHERE company_id=@c AND device_id=@d AND canonical_identity=@i",
                c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@d", device); c.Parameters.AddWithValue("@i", canonical); });
            Assert.Equal("Warning", row!["severity"]);
            Assert.Equal("evt-c", row["lastSourceEventId"]);
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM fault_codes WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", company));
        }
    }

    [Fact]
    public async Task ConcurrentAlteredPayload_WithSameSourceEvent_AcceptsOneAtomicBatch()
    {
        var db = CreateDatabase();
        await new MaintenanceSchemaService(db).EnsureAsync();
        var company = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 2;
        var device = $"diag-event-lock-{Guid.NewGuid():N}";
        var source = $"frame-{Guid.NewGuid():N}";
        try
        {
            var aIdentities = new[] { "J1939:ENGINE:SPN:10:FMI:1", "J1939:ENGINE:SPN:11:FMI:2" };
            var bIdentities = new[] { "J1939:ENGINE:SPN:99:FMI:9", "J1939:ENGINE:SPN:98:FMI:8" };
            var a = AcceptBatchAsync(CreateDatabase(), company, device, source, aIdentities);
            var b = AcceptBatchAsync(CreateDatabase(), company, device, source, bIdentities);
            var decisions = await Task.WhenAll(a, b);
            Assert.Equal(1, decisions.Count(value => value == TelemetryPayloadReplayDecision.NewObservation));
            Assert.Equal(1, decisions.Count(value => value == TelemetryPayloadReplayDecision.Conflict));
            Assert.Equal(2, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM fault_occurrences WHERE company_id=@c AND device_id=@d AND source_event_id=@s",
                c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@d", device); c.Parameters.AddWithValue("@s", source); }));

            var acceptedIdentities = decisions[0] == TelemetryPayloadReplayDecision.NewObservation
                ? aIdentities : bIdentities;
            Assert.Equal(TelemetryPayloadReplayDecision.IdenticalReplay,
                await AcceptBatchAsync(CreateDatabase(), company, device, source, acceptedIdentities));
            Assert.Equal(TelemetryPayloadReplayDecision.NewObservation,
                await AcceptBatchAsync(CreateDatabase(), company + 1, device, source, acceptedIdentities));
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM fault_occurrences WHERE company_id IN (@c,@other)", c =>
            {
                c.Parameters.AddWithValue("@c", company);
                c.Parameters.AddWithValue("@other", company + 1);
            });
        }
    }

    private static Task<int> InsertOccurrenceAsync(Database db, long company, string device, string source, int ordinal, string canonical) =>
        db.ExecuteAsync(
            @"INSERT INTO fault_occurrences
                (company_id,device_id,source_event_id,dtc_ordinal,canonical_dtc,observed_at,protocol,code,spn,fmi)
              VALUES (@c,@d,@s,@o,@i,NOW(),'J1939',@code,@spn,@fmi)
              ON CONFLICT (company_id,device_id,source_event_id,dtc_ordinal,canonical_dtc) DO NOTHING",
            c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@d", device);
                   c.Parameters.AddWithValue("@s", source); c.Parameters.AddWithValue("@o", ordinal);
                   c.Parameters.AddWithValue("@i", canonical); c.Parameters.AddWithValue("@code", ordinal == 0 ? "SPN-1-FMI-0" : "SPN-2-FMI-1");
                   c.Parameters.AddWithValue("@spn", ordinal + 1); c.Parameters.AddWithValue("@fmi", ordinal); });

    private static async Task<object?> UpsertProjectionAsync(Database db, long company, string device,
        string canonical, string source, DateTimeOffset observed, string severity)
    {
        return await db.QuerySingleAsync(
            @"INSERT INTO fault_codes
                (company_id,device_id,source_event_id,canonical_identity,last_source_event_id,code_type,protocol,code,
                 severity,observed_at,last_observed_at,occurrence_count,status,first_seen_at,last_seen_at)
              VALUES (@c,@d,@s,@i,@s,'J1939','J1939','SPN-100-FMI-4',@severity,@at,@at,1,'active',@at,@at)
              ON CONFLICT (company_id,device_id,protocol,canonical_identity) DO UPDATE SET
                source_event_id=EXCLUDED.source_event_id,last_source_event_id=EXCLUDED.last_source_event_id,
                observed_at=EXCLUDED.observed_at,last_observed_at=EXCLUDED.last_observed_at,severity=EXCLUDED.severity
              WHERE fault_codes.last_observed_at < EXCLUDED.last_observed_at
                 OR (fault_codes.last_observed_at = EXCLUDED.last_observed_at
                     AND fault_codes.last_source_event_id < EXCLUDED.last_source_event_id)
              RETURNING id",
            c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@d", device);
                   c.Parameters.AddWithValue("@s", source); c.Parameters.AddWithValue("@i", canonical);
                   c.Parameters.AddWithValue("@severity", severity); c.Parameters.AddWithValue("@at", observed); });
    }

    private static Task<TelemetryPayloadReplayDecision> AcceptBatchAsync(
        Database db, long company, string device, string source, string[] identities) =>
        db.WithTransactionAsync(async (connection, transaction) =>
        {
            var fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(string.Join("\n", identities)))).ToLowerInvariant();
            await using (var advisory = new Npgsql.NpgsqlCommand(
                "SELECT pg_advisory_xact_lock(hashtextextended(@key,0))", connection, transaction))
            {
                advisory.Parameters.AddWithValue("@key", $"{company}:{device}:{source}");
                await advisory.ExecuteNonQueryAsync();
            }
            await using (var exists = new Npgsql.NpgsqlCommand(
                @"SELECT COUNT(*),MIN(payload_fingerprint),COUNT(payload_fingerprint),COUNT(DISTINCT payload_fingerprint)
                    FROM fault_occurrences WHERE company_id=@c AND device_id=@d AND source_event_id=@s",
                connection, transaction))
            {
                exists.Parameters.AddWithValue("@c", company);
                exists.Parameters.AddWithValue("@d", device);
                exists.Parameters.AddWithValue("@s", source);
                await using var reader = await exists.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                var count = reader.GetInt64(0);
                if (count > 0)
                    return TelemetryPayloadFingerprint.Decide(
                        count, reader.IsDBNull(1) ? null : reader.GetString(1),
                        reader.GetInt64(2), reader.GetInt64(3), fingerprint);
            }
            for (var ordinal = 0; ordinal < identities.Length; ordinal++)
            {
                await using var insert = new Npgsql.NpgsqlCommand(
                    @"INSERT INTO fault_occurrences
                        (company_id,device_id,source_event_id,dtc_ordinal,canonical_dtc,observed_at,protocol,code,spn,fmi,payload_fingerprint)
                      VALUES (@c,@d,@s,@o,@i,NOW(),'J1939',@i,@o,@o,@fingerprint)", connection, transaction);
                insert.Parameters.AddWithValue("@c", company);
                insert.Parameters.AddWithValue("@d", device);
                insert.Parameters.AddWithValue("@s", source);
                insert.Parameters.AddWithValue("@o", ordinal);
                insert.Parameters.AddWithValue("@i", identities[ordinal]);
                insert.Parameters.AddWithValue("@fingerprint", fingerprint);
                await insert.ExecuteNonQueryAsync();
            }
            return TelemetryPayloadReplayDecision.NewObservation;
        });

    private static Database CreateDatabase() => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString,
        }).Build());
}
