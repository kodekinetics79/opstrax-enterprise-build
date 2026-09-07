using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Opstrax.Api.Data;
using Opstrax.Api.Services;
using Xunit.Sdk;

namespace Opstrax.Tests;

[Trait("Category", "CameraProviderIngestPostgres")]
public sealed class CameraProviderIngestPostgresTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Intake_IsReplaySafeQuarantinesConflictsAndSupportsLateAssignment()
    {
        var connectionString = GuardedConnection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> {
                ["ConnectionStrings:DefaultConnection"] = connectionString,
                ["ConnectionStrings:SystemConnection"] = connectionString,
                ["Rls:EnforceTenantContext"] = "false"
            }).Build();
        var database = new Database(configuration);
        var service = new CameraProviderIngestService(
            database, new FixedTimeProvider(Now), NullLogger<CameraProviderIngestService>.Instance);
        var suffix = Guid.NewGuid().ToString("N");
        long companyId = 0, otherCompanyId = 0;

        try
        {
            companyId = await InsertId(database,
                "INSERT INTO companies(company_code,name,industry) VALUES(@code,'Camera provider test','Testing') RETURNING id",
                c => c.Parameters.AddWithValue("code", $"CAM-{suffix}"));
            otherCompanyId = await InsertId(database,
                "INSERT INTO companies(company_code,name,industry) VALUES(@code,'Other camera tenant','Testing') RETURNING id",
                c => c.Parameters.AddWithValue("code", $"OTHER-{suffix}"));
            var branchId = await InsertId(database,
                "INSERT INTO branches(company_id,branch_code,name) VALUES(@company,@code,'Camera test branch') RETURNING id",
                c => { c.Parameters.AddWithValue("company", companyId); c.Parameters.AddWithValue("code", $"B-{suffix}"); });
            var vehicleId = await InsertId(database,
                "INSERT INTO vehicles(company_id,branch_id,vehicle_code,type) VALUES(@company,@branch,@code,'Truck') RETURNING id",
                c => { c.Parameters.AddWithValue("company", companyId); c.Parameters.AddWithValue("branch", branchId); c.Parameters.AddWithValue("code", $"V-{suffix}"); });
            var driverId = await InsertId(database,
                "INSERT INTO drivers(company_id,branch_id,driver_code,full_name) VALUES(@company,@branch,@code,'Camera Test Driver') RETURNING id",
                c => { c.Parameters.AddWithValue("company", companyId); c.Parameters.AddWithValue("branch", branchId); c.Parameters.AddWithValue("code", $"D-{suffix}"); });
            var foreignVehicleId = await InsertId(database,
                "INSERT INTO vehicles(company_id,vehicle_code,type) VALUES(@company,@code,'Truck') RETURNING id",
                c => { c.Parameters.AddWithValue("company", otherCompanyId); c.Parameters.AddWithValue("code", $"FV-{suffix}"); });

            var payload = Encoding.UTF8.GetBytes("{\"provider\":\"exact-payload\"}");
            var first = await service.IngestAsync(companyId, Envelope($"event-{suffix}", branchId, vehicleId, null), payload);
            var replay = await service.IngestAsync(companyId, Envelope($"event-{suffix}", branchId, vehicleId, driverId), payload);

            Assert.Equal(CameraProviderIngestDisposition.Accepted, first.Disposition);
            Assert.Equal("Matched", first.ReconciliationStatus);
            Assert.Equal("ExternalHold", first.ProviderVerificationStatus);
            Assert.Equal(CameraProviderIngestDisposition.Replay, replay.Disposition);
            Assert.Equal(first.InboxId, replay.InboxId);
            Assert.Equal(2, replay.SeenCount);
            Assert.Equal(1, replay.MediaReferenceCount);

            var stored = await database.QuerySingleAsync("""
                SELECT payload_sha256,provider_verification_status,processing_status,reconciliation_status,
                       vehicle_id,driver_id,seen_count
                FROM camera_provider_event_inbox WHERE id=@id AND company_id=@company
                """, c => { c.Parameters.AddWithValue("id", first.InboxId); c.Parameters.AddWithValue("company", companyId); });
            Assert.NotNull(stored);
            Assert.Equal(first.PayloadSha256, stored["payloadSha256"]);
            Assert.Equal("ExternalHold", stored["providerVerificationStatus"]);
            Assert.Equal("PendingVerification", stored["processingStatus"]);
            Assert.Equal("Matched", stored["reconciliationStatus"]);
            Assert.Equal(vehicleId, stored["vehicleId"]);
            Assert.Equal(driverId, stored["driverId"]);
            Assert.Equal(2, stored["seenCount"]);

            var conflict = await service.IngestAsync(
                companyId, Envelope($"event-{suffix}", branchId, vehicleId, driverId), Encoding.UTF8.GetBytes("{\"provider\":\"changed-payload\"}"));
            Assert.Equal(CameraProviderIngestDisposition.Quarantined, conflict.Disposition);
            Assert.Equal("payload_identity_conflict", conflict.QuarantineReason);
            Assert.Equal(3, conflict.SeenCount);
            Assert.Equal(0, conflict.MediaReferenceCount);
            var conflictStored = await database.QuerySingleAsync("""
                SELECT payload_sha256,conflicting_payload_sha256,processing_status,provider_verification_status,seen_count
                FROM camera_provider_event_inbox WHERE id=@id
                """, c => c.Parameters.AddWithValue("id", first.InboxId));
            Assert.Equal(first.PayloadSha256, conflictStored!["payloadSha256"]);
            Assert.Equal(conflict.PayloadSha256, conflictStored["conflictingPayloadSha256"]);
            Assert.Equal("Quarantined", conflictStored["processingStatus"]);
            Assert.Equal("ExternalHold", conflictStored["providerVerificationStatus"]);

            var tenantMismatch = await service.IngestAsync(
                companyId, Envelope($"cross-tenant-{suffix}", null, foreignVehicleId, null), payload);
            Assert.Equal(CameraProviderIngestDisposition.Quarantined, tenantMismatch.Disposition);
            Assert.Equal("tenant_reference_mismatch", tenantMismatch.QuarantineReason);
            var mismatchStored = await database.QuerySingleAsync(
                "SELECT vehicle_id,processing_status FROM camera_provider_event_inbox WHERE id=@id",
                c => c.Parameters.AddWithValue("id", tenantMismatch.InboxId));
            Assert.Null(mismatchStored!["vehicleId"]);
            Assert.Equal("Quarantined", mismatchStored["processingStatus"]);

            var derivedConflict = await service.IngestAsync(
                companyId,
                Envelope($"derived-conflict-{suffix}", branchId, vehicleId, driverId),
                payload);
            var changedDerivedFields = await service.IngestAsync(
                companyId,
                Envelope($"derived-conflict-{suffix}", branchId, vehicleId, driverId) with { EventType = "Crash" },
                payload);
            Assert.Equal(CameraProviderIngestDisposition.Accepted, derivedConflict.Disposition);
            Assert.Equal(CameraProviderIngestDisposition.Quarantined, changedDerivedFields.Disposition);
            Assert.Equal("derived_payload_conflict", changedDerivedFields.QuarantineReason);
            Assert.Equal(0, changedDerivedFields.MediaReferenceCount);

            var mediaConflict = await service.IngestAsync(
                companyId,
                Envelope($"media-conflict-{suffix}", branchId, vehicleId, driverId),
                payload);
            var changedMediaEnvelope = Envelope($"media-conflict-{suffix}", branchId, vehicleId, driverId) with
            {
                MediaReferences = [new("RoadFacing", "Video", "video/mp4", "opaque-media-2", Now.AddMinutes(-2), 10_000, Now.AddHours(1), "Triggered", "Safety30Days", "privacy-v1")]
            };
            var changedMedia = await service.IngestAsync(companyId, changedMediaEnvelope, payload);
            Assert.Equal(CameraProviderIngestDisposition.Accepted, mediaConflict.Disposition);
            Assert.Equal(CameraProviderIngestDisposition.Quarantined, changedMedia.Disposition);
            Assert.Equal("derived_payload_conflict", changedMedia.QuarantineReason);
            Assert.Equal(1, await database.ScalarLongAsync(
                "SELECT COUNT(*) FROM camera_provider_media_references WHERE provider_event_inbox_id=@id",
                c => c.Parameters.AddWithValue("id", mediaConflict.InboxId)));

            var otherAccount = await service.IngestAsync(
                companyId,
                Envelope($"event-{suffix}", branchId, vehicleId, driverId) with { ProviderAccountReference = "account-test-2" },
                payload);
            Assert.Equal(CameraProviderIngestDisposition.Accepted, otherAccount.Disposition);
            Assert.NotEqual(first.InboxId, otherAccount.InboxId);

            var media = await database.QuerySingleAsync("""
                SELECT provider_media_id,retrieval_status,access_status
                FROM camera_provider_media_references WHERE provider_event_inbox_id=@id
                """, c => c.Parameters.AddWithValue("id", first.InboxId));
            Assert.Equal("opaque-media-1", media!["providerMediaId"]);
            Assert.Equal("ProviderPending", media["retrievalStatus"]);
            Assert.Equal("ExternalHold", media["accessStatus"]);

            var eventIdentityError = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteAsync(
                "UPDATE camera_provider_event_inbox SET payload_sha256=repeat('f',64) WHERE id=@id",
                c => c.Parameters.AddWithValue("id", first.InboxId)));
            Assert.Equal(PostgresErrorCodes.CheckViolation, eventIdentityError.SqlState);
            Assert.Equal("ck_camera_provider_event_identity_immutable", eventIdentityError.ConstraintName);
            var mediaIdentityError = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteAsync(
                "UPDATE camera_provider_media_references SET provider_media_id='replaced' WHERE provider_event_inbox_id=@id",
                c => c.Parameters.AddWithValue("id", first.InboxId)));
            Assert.Equal(PostgresErrorCodes.CheckViolation, mediaIdentityError.SqlState);
            Assert.Equal("ck_camera_provider_media_identity_immutable", mediaIdentityError.ConstraintName);
        }
        finally
        {
            if (companyId > 0 || otherCompanyId > 0)
            {
                await database.ExecuteAsync("""
                    DELETE FROM camera_provider_event_inbox WHERE company_id IN (@company,@other);
                    DELETE FROM drivers WHERE company_id IN (@company,@other);
                    DELETE FROM vehicles WHERE company_id IN (@company,@other);
                    DELETE FROM branches WHERE company_id IN (@company,@other);
                    DELETE FROM companies WHERE id IN (@company,@other)
                    """, c => { c.Parameters.AddWithValue("company", companyId); c.Parameters.AddWithValue("other", otherCompanyId); });
            }
        }
    }

    [Fact]
    public async Task ConcurrentIdenticalDelivery_HasOneIdentityAndExactSeenCount()
    {
        var connectionString = GuardedConnection();
        var database = new Database(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = connectionString }).Build());
        var service = new CameraProviderIngestService(
            database, new FixedTimeProvider(Now), NullLogger<CameraProviderIngestService>.Instance);
        var suffix = Guid.NewGuid().ToString("N");
        var companyId = await InsertId(database,
            "INSERT INTO companies(company_code,name,industry) VALUES(@code,'Camera concurrency test','Testing') RETURNING id",
            c => c.Parameters.AddWithValue("code", $"CON-{suffix}"));
        try
        {
            var envelope = Envelope($"concurrent-{suffix}", null, null, null);
            var payload = Encoding.UTF8.GetBytes("{\"provider\":\"concurrent-exact-payload\"}");
            var results = await Task.WhenAll(Enumerable.Range(0, 8)
                .Select(_ => service.IngestAsync(companyId, envelope, payload)));

            Assert.Single(results, result => result.Disposition == CameraProviderIngestDisposition.Accepted);
            Assert.Equal(7, results.Count(result => result.Disposition == CameraProviderIngestDisposition.Replay));
            Assert.Single(results.Select(result => result.InboxId).Distinct());
            Assert.Equal(8, await database.ScalarLongAsync(
                "SELECT seen_count FROM camera_provider_event_inbox WHERE company_id=@company AND provider_event_id=@event",
                c => { c.Parameters.AddWithValue("company", companyId); c.Parameters.AddWithValue("event", envelope.ProviderEventId); }));
        }
        finally
        {
            await database.ExecuteAsync("DELETE FROM camera_provider_event_inbox WHERE company_id=@company; DELETE FROM companies WHERE id=@company",
                c => c.Parameters.AddWithValue("company", companyId));
        }
    }

    private static CameraProviderEventEnvelope Envelope(string eventId, long? branchId, long? vehicleId, long? driverId) => new(
        "samsara", "account-test", eventId, "v1", "HarshBraking", Now.AddMinutes(-2), Now.AddMinutes(-1),
        branchId, vehicleId, driverId, null, "external-vehicle", null, null,
        [new("RoadFacing", "Video", "video/mp4", "opaque-media-1", Now.AddMinutes(-2), 10_000, Now.AddHours(1), "Triggered", "Safety30Days", "privacy-v1")]);

    private static async Task<long> InsertId(Database database, string sql, Action<NpgsqlCommand> bind)
    {
        var row = await database.QuerySingleAsync(sql, bind);
        return Convert.ToInt64(row!["id"]);
    }

    private static string GuardedConnection()
    {
        var configured = Environment.GetEnvironmentVariable("OPSTRAX_CAMERA_STAGE112_DB");
        if (string.IsNullOrWhiteSpace(configured))
            throw SkipException.ForSkip("Set OPSTRAX_CAMERA_STAGE112_DB to the dedicated disposable camera database.");
        var builder = new NpgsqlConnectionStringBuilder(configured);
        if (builder.Host is not ("127.0.0.1" or "::1") || builder.Port != 55443 || builder.Database != "opstrax_camera_stage112")
            throw new InvalidOperationException("Camera Stage112 tests require numeric loopback port 55443 and database opstrax_camera_stage112.");
        builder.Pooling = false;
        builder.Timeout = 5;
        builder.CommandTimeout = 15;
        builder.IncludeErrorDetail = false;
        builder.ApplicationName = "opstrax-camera-stage112-test";
        return builder.ConnectionString;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
