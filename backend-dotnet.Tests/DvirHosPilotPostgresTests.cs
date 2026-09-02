using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

[Trait("Category", "Integration")]
public sealed class DvirHosPilotPostgresTests
{
    private const string HosAttestationText = "I certify that this daily HOS record is true and correct.";
    private const string DvirAttestationText = "I certify that this DVIR is true and correct and that I completed this inspection.";
    private const string RepairAttestationText = "I acknowledge that I reviewed the certified repairs for this DVIR.";
    [Fact]
    public async Task HosReadsRequireDirectPermissionAndHideOtherBranches()
    {
        var db = Db();
        var seed = await Seed(db);
        try
        {
            await InsertClock(db, seed.CompanyId, seed.BranchA, seed.DriverA, "Warning", 45);
            await InsertClock(db, seed.CompanyId, seed.BranchB, seed.DriverB, "Violation", 0);

            var denied = await Invoke("HosDriversPilot", Principal(seed.CompanyId, seed.BranchA, "shipments:view"), db, CancellationToken.None);
            Assert.Equal(StatusCodes.Status403Forbidden, Status(denied));

            var allowed = await Invoke("HosDriversPilot", Principal(seed.CompanyId, seed.BranchA, "compliance:view"), db, CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, Status(allowed));
            var json = JsonSerializer.Serialize(Assert.IsAssignableFrom<IValueHttpResult>(allowed).Value);
            Assert.Contains("DVIR/HOS Driver A", json, StringComparison.Ordinal);
            Assert.DoesNotContain("DVIR/HOS Driver B", json, StringComparison.Ordinal);
        }
        finally { await Cleanup(db, seed.CompanyId); }
    }

    [Fact]
    public async Task HosDailyCertificationRejectsOpenSegmentsThenIsConcurrencyIdempotent()
    {
        var db = Db();
        var seed = await Seed(db);
        try
        {
            var logDay = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(-1), TimeSpan.Zero);
            var first = await InsertHosLog(db, seed.CompanyId, seed.BranchA, seed.DriverA,
                logDay.AddHours(1), logDay.AddHours(3), "Off Duty", "one");
            var second = await InsertHosLog(db, seed.CompanyId, seed.BranchA, seed.DriverA,
                logDay.AddHours(3), null, "Driving", "two");
            var body = HosAttestation();

            var backOffice = await Invoke("HosCertifyPilot", Principal(seed.CompanyId, seed.BranchA, "compliance:update"), first, body, db, new AuditService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status403Forbidden, Status(backOffice));
            var invalidAttestation = await Invoke("HosCertifyPilot", DriverPrincipal(seed.CompanyId, seed.BranchA), first,
                new Dictionary<string, object?> { ["attestationAccepted"] = true, ["attestation"] = "certify" }, db, new AuditService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status400BadRequest, Status(invalidAttestation));
            var open = await Invoke("HosCertifyPilot", DriverPrincipal(seed.CompanyId, seed.BranchA), first, body, db, new AuditService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status409Conflict, Status(open));
            await db.ExecuteAsync("UPDATE hos_logs SET end_time=@end,duration_minutes=120 WHERE id=@id",
                c => { c.Parameters.AddWithValue("@end", logDay.AddHours(5)); c.Parameters.AddWithValue("@id", second); });

            var dbA = Db();
            var dbB = Db();
            var concurrent = await Task.WhenAll(
                Invoke("HosCertifyPilot", DriverPrincipal(seed.CompanyId, seed.BranchA), first, body, dbA, new AuditService(dbA), CancellationToken.None),
                Invoke("HosCertifyPilot", DriverPrincipal(seed.CompanyId, seed.BranchA), second, body, dbB, new AuditService(dbB), CancellationToken.None));
            Assert.All(concurrent, result => Assert.Equal(StatusCodes.Status200OK, Status(result)));
            Assert.Equal(2, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM hos_logs WHERE company_id=@c AND driver_id=@d AND is_certified AND certified_by=@uid AND certified_at IS NOT NULL",
                c => { c.Parameters.AddWithValue("@c", seed.CompanyId); c.Parameters.AddWithValue("@d", seed.DriverA); c.Parameters.AddWithValue("@uid", DriverUserId(seed.CompanyId)); }));
            Assert.Equal(1, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM hos_certifications WHERE company_id=@c AND driver_id=@d AND certified_by=@uid AND attestation_text=@a",
                c => { c.Parameters.AddWithValue("@c", seed.CompanyId); c.Parameters.AddWithValue("@d", seed.DriverA); c.Parameters.AddWithValue("@uid", DriverUserId(seed.CompanyId)); c.Parameters.AddWithValue("@a", HosAttestationText); }));
            var certification = await db.QuerySingleAsync(
                "SELECT source_revision,source_snapshot::text source_snapshot FROM hos_certifications WHERE company_id=@c AND driver_id=@d",
                c => { c.Parameters.AddWithValue("@c", seed.CompanyId); c.Parameters.AddWithValue("@d", seed.DriverA); });
            Assert.Equal(64, certification!["sourceRevision"]?.ToString()?.Length);
            var snapshot = certification["sourceSnapshot"]?.ToString() ?? "";
            Assert.Contains("vehicleId", snapshot, StringComparison.Ordinal);
            Assert.Contains("countryCode", snapshot, StringComparison.Ordinal);
            Assert.Contains("profileId", snapshot, StringComparison.Ordinal);
            Assert.Contains("location", snapshot, StringComparison.Ordinal);
            Assert.Contains("sourceEventId", snapshot, StringComparison.Ordinal);

            await Assert.ThrowsAsync<PostgresException>(() => db.ExecuteAsync(
                "DELETE FROM hos_logs WHERE id=@id", c => c.Parameters.AddWithValue("@id", first)));
            await Assert.ThrowsAsync<PostgresException>(() => db.ExecuteAsync(
                "DELETE FROM hos_certifications WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", seed.CompanyId)));
            await db.ExecuteAsync("UPDATE hos_logs SET location='Corrected terminal' WHERE id=@id", c => c.Parameters.AddWithValue("@id", first));
            Assert.Equal(0, await db.ScalarLongAsync("SELECT COUNT(*) FROM hos_logs WHERE id=@id AND is_certified", c => c.Parameters.AddWithValue("@id", first)));
            Assert.Equal(0, await db.ScalarLongAsync("SELECT COUNT(*) FROM hos_logs WHERE id=@id AND is_certified", c => c.Parameters.AddWithValue("@id", second)));
            Assert.Equal(StatusCodes.Status200OK, Status(await Invoke("HosCertifyPilot", DriverPrincipal(seed.CompanyId, seed.BranchA), first, body, db, new AuditService(db), CancellationToken.None)));
            Assert.Equal(2, await db.ScalarLongAsync("SELECT COUNT(*) FROM hos_certifications WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", seed.CompanyId)));

            var otherBranch = await InsertHosLog(db, seed.CompanyId, seed.BranchB, seed.DriverA,
                logDay.AddHours(6), logDay.AddHours(7), "Off Duty", "other-branch-certified");
            await db.ExecuteAsync("UPDATE hos_logs SET is_certified=TRUE,certified_at=NOW(),certified_by=@uid WHERE id=@id",
                c => { c.Parameters.AddWithValue("@uid", DriverUserId(seed.CompanyId)); c.Parameters.AddWithValue("@id", otherBranch); });
            var insertedCorrection = await InsertHosLog(db, seed.CompanyId, seed.BranchA, seed.DriverA,
                logDay.AddHours(7), logDay.AddHours(8), "On Duty", "inserted-correction");
            Assert.Equal(0, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM hos_logs WHERE company_id=@c AND driver_id=@d AND log_date=@date AND branch_id=@b AND is_certified",
                c => { c.Parameters.AddWithValue("@c", seed.CompanyId); c.Parameters.AddWithValue("@d", seed.DriverA); c.Parameters.AddWithValue("@date", logDay.UtcDateTime.Date); c.Parameters.AddWithValue("@b", seed.BranchA); }));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM hos_logs WHERE id=@id AND is_certified", c => c.Parameters.AddWithValue("@id", otherBranch)));
            Assert.Equal(0, await db.ScalarLongAsync("SELECT COUNT(*) FROM hos_logs WHERE id=@id AND is_certified", c => c.Parameters.AddWithValue("@id", insertedCorrection)));

            await Assert.ThrowsAsync<PostgresException>(() => InsertHosLog(db, seed.CompanyId, seed.BranchA, seed.DriverA,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(-1), "Driving", "invalid-time"));
        }
        finally { await Cleanup(db, seed.CompanyId); }
    }

    [Fact]
    public async Task HosInsertWaitsForCertificationDayLockAndInvalidatesTheCompletedSnapshotDay()
    {
        var db = Db(); var seed = await Seed(db);
        var day = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(-6), TimeSpan.Zero);
        try
        {
            var original = await InsertHosLog(db, seed.CompanyId, seed.BranchA, seed.DriverA,
                day.AddHours(1), day.AddHours(2), "Off Duty", "lock-original");

            await using var lockConnection = new NpgsqlConnection(TestDb.ConnectionString);
            await lockConnection.OpenAsync();
            await using var lockTransaction = await lockConnection.BeginTransactionAsync();
            await using (var lockCommand = new NpgsqlCommand(
                "SELECT pg_advisory_xact_lock(stage65_hos_day_lock_key(@c,@d,@date::date,@b))",
                lockConnection, lockTransaction))
            {
                lockCommand.Parameters.AddWithValue("@c", seed.CompanyId);
                lockCommand.Parameters.AddWithValue("@d", seed.DriverA);
                lockCommand.Parameters.AddWithValue("@date", day.UtcDateTime.Date);
                lockCommand.Parameters.AddWithValue("@b", seed.BranchA);
                await lockCommand.ExecuteNonQueryAsync();
            }

            var certificationApp = $"hos-cert-insert-{Guid.NewGuid():N}";
            var certificationDb = Db(certificationApp);
            var certificationTask = Invoke("HosCertifyPilot", DriverPrincipal(seed.CompanyId, seed.BranchA), original,
                HosAttestation(), certificationDb, new AuditService(certificationDb), CancellationToken.None);
            await WaitForAdvisoryWaiter(db, certificationApp);
            var certificationWasBlocked = !certificationTask.IsCompleted;

            var insertApp = $"hos-insert-{Guid.NewGuid():N}";
            var insertDb = Db(insertApp);
            var insertTask = InsertHosLog(insertDb, seed.CompanyId, seed.BranchA, seed.DriverA,
                day.AddHours(2), day.AddHours(3), "Driving", "lock-concurrent-insert");
            await WaitForAdvisoryWaiter(db, insertApp);
            var insertWasBlocked = !insertTask.IsCompleted;

            // PostgreSQL advisory waiters are queued: certification entered first, snapshots
            // and commits the original day, then INSERT proceeds and invalidates that day.
            await lockTransaction.CommitAsync();
            var certification = await certificationTask.WaitAsync(TimeSpan.FromSeconds(10));
            var inserted = await insertTask.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.True(certificationWasBlocked);
            Assert.True(insertWasBlocked);
            Assert.Equal(StatusCodes.Status200OK, Status(certification));
            Assert.True(inserted > 0);
            Assert.Equal(0, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM hos_logs WHERE company_id=@c AND driver_id=@d AND log_date=@date AND branch_id=@b AND is_certified",
                c => { c.Parameters.AddWithValue("@c", seed.CompanyId); c.Parameters.AddWithValue("@d", seed.DriverA); c.Parameters.AddWithValue("@date", day.UtcDateTime.Date); c.Parameters.AddWithValue("@b", seed.BranchA); }));

            var persisted = await db.QuerySingleAsync(
                @"SELECT branch_id,jsonb_array_length(source_snapshot) snapshot_count,source_snapshot::text source_snapshot
                  FROM hos_certifications WHERE company_id=@c AND driver_id=@d AND log_date=@date
                  ORDER BY certified_at DESC LIMIT 1",
                c => { c.Parameters.AddWithValue("@c", seed.CompanyId); c.Parameters.AddWithValue("@d", seed.DriverA); c.Parameters.AddWithValue("@date", day.UtcDateTime.Date); });
            Assert.NotNull(persisted);
            Assert.Equal(seed.BranchA, Convert.ToInt64(persisted!["branchId"]));
            Assert.Equal(1, Convert.ToInt32(persisted["snapshotCount"]));
            Assert.DoesNotContain("lock-concurrent-insert", persisted["sourceSnapshot"]?.ToString(), StringComparison.Ordinal);
        }
        finally { await Cleanup(db, seed.CompanyId); }
    }

    [Fact]
    public async Task HosIdentityMoveIntoCertifyingDayFailsFastWithoutChangingTheCertifiedSnapshot()
    {
        var db = Db(); var seed = await Seed(db);
        var targetDay = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(-8), TimeSpan.Zero);
        var sourceDay = targetDay.AddDays(-1);
        try
        {
            var target = await InsertHosLog(db, seed.CompanyId, seed.BranchA, seed.DriverA,
                targetDay.AddHours(1), targetDay.AddHours(2), "Off Duty", "move-target");
            var moving = await InsertHosLog(db, seed.CompanyId, seed.BranchA, seed.DriverA,
                sourceDay.AddHours(2), sourceDay.AddHours(3), "Driving", "move-concurrent-source");

            await using var lockConnection = new NpgsqlConnection(TestDb.ConnectionString);
            await lockConnection.OpenAsync();
            await using var lockTransaction = await lockConnection.BeginTransactionAsync();
            await using (var lockCommand = new NpgsqlCommand(
                "SELECT pg_advisory_xact_lock(stage65_hos_day_lock_key(@c,@d,@date::date,@b))",
                lockConnection, lockTransaction))
            {
                lockCommand.Parameters.AddWithValue("@c", seed.CompanyId);
                lockCommand.Parameters.AddWithValue("@d", seed.DriverA);
                lockCommand.Parameters.AddWithValue("@date", targetDay.UtcDateTime.Date);
                lockCommand.Parameters.AddWithValue("@b", seed.BranchA);
                await lockCommand.ExecuteNonQueryAsync();
            }

            var certificationApp = $"hos-cert-move-{Guid.NewGuid():N}";
            var certificationDb = Db(certificationApp);
            var certificationTask = Invoke("HosCertifyPilot", DriverPrincipal(seed.CompanyId, seed.BranchA), target,
                HosAttestation(), certificationDb, new AuditService(certificationDb), CancellationToken.None);
            await WaitForAdvisoryWaiter(db, certificationApp);
            var certificationWasBlocked = !certificationTask.IsCompleted;

            var moveDb = Db($"hos-move-{Guid.NewGuid():N}");
            var moveTask = moveDb.ExecuteAsync(
                @"UPDATE hos_logs SET log_date=@date,start_time=@start,end_time=@end,duration_minutes=60
                  WHERE id=@id AND company_id=@c",
                c =>
                {
                    c.Parameters.AddWithValue("@date", targetDay.UtcDateTime.Date);
                    c.Parameters.AddWithValue("@start", targetDay.AddHours(2));
                    c.Parameters.AddWithValue("@end", targetDay.AddHours(3));
                    c.Parameters.AddWithValue("@id", moving); c.Parameters.AddWithValue("@c", seed.CompanyId);
                });
            var moveFailure = await Assert.ThrowsAsync<PostgresException>(
                () => moveTask.WaitAsync(TimeSpan.FromSeconds(5)));

            await lockTransaction.CommitAsync();
            var certification = await certificationTask.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.True(certificationWasBlocked);
            Assert.Equal("40001", moveFailure.SqlState);
            Assert.Equal(StatusCodes.Status200OK, Status(certification));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM hos_logs WHERE id=@id AND log_date=@date", c => { c.Parameters.AddWithValue("@id", moving); c.Parameters.AddWithValue("@date", sourceDay.UtcDateTime.Date); }));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM hos_logs WHERE id=@id AND is_certified", c => c.Parameters.AddWithValue("@id", target)));

            var persisted = await db.QuerySingleAsync(
                @"SELECT jsonb_array_length(source_snapshot) snapshot_count,source_snapshot::text source_snapshot
                  FROM hos_certifications WHERE company_id=@c AND driver_id=@d AND log_date=@date
                  ORDER BY certified_at DESC LIMIT 1",
                c => { c.Parameters.AddWithValue("@c", seed.CompanyId); c.Parameters.AddWithValue("@d", seed.DriverA); c.Parameters.AddWithValue("@date", targetDay.UtcDateTime.Date); });
            Assert.NotNull(persisted);
            Assert.Equal(1, Convert.ToInt32(persisted!["snapshotCount"]));
            Assert.DoesNotContain("move-concurrent-source", persisted["sourceSnapshot"]?.ToString(), StringComparison.Ordinal);
        }
        finally { await Cleanup(db, seed.CompanyId); }
    }

    [Fact]
    public async Task HosIdentityMoveOutOfCertifyingDayFailsFastWithoutTupleAdvisoryDeadlock()
    {
        var db = Db(); var seed = await Seed(db);
        var targetDay = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(-12), TimeSpan.Zero);
        var destinationDay = targetDay.AddDays(-1);
        try
        {
            var first = await InsertHosLog(db, seed.CompanyId, seed.BranchA, seed.DriverA,
                targetDay.AddHours(1), targetDay.AddHours(2), "Off Duty", "move-out-first");
            var moving = await InsertHosLog(db, seed.CompanyId, seed.BranchA, seed.DriverA,
                targetDay.AddHours(2), targetDay.AddHours(3), "Driving", "move-out-concurrent");

            await using var lockConnection = new NpgsqlConnection(TestDb.ConnectionString);
            await lockConnection.OpenAsync();
            await using var lockTransaction = await lockConnection.BeginTransactionAsync();
            await using (var lockCommand = new NpgsqlCommand(
                "SELECT pg_advisory_xact_lock(stage65_hos_day_lock_key(@c,@d,@date::date,@b))",
                lockConnection, lockTransaction))
            {
                lockCommand.Parameters.AddWithValue("@c", seed.CompanyId); lockCommand.Parameters.AddWithValue("@d", seed.DriverA);
                lockCommand.Parameters.AddWithValue("@date", targetDay.UtcDateTime.Date); lockCommand.Parameters.AddWithValue("@b", seed.BranchA);
                await lockCommand.ExecuteNonQueryAsync();
            }

            var certificationApp = $"hos-cert-move-out-{Guid.NewGuid():N}";
            var certificationDb = Db(certificationApp);
            var certificationTask = Invoke("HosCertifyPilot", DriverPrincipal(seed.CompanyId, seed.BranchA), first,
                HosAttestation(), certificationDb, new AuditService(certificationDb), CancellationToken.None);
            await WaitForAdvisoryWaiter(db, certificationApp);

            var moveTask = Db($"hos-move-out-{Guid.NewGuid():N}").ExecuteAsync(
                @"UPDATE hos_logs SET log_date=@date,start_time=@start,end_time=@end,duration_minutes=60 WHERE id=@id",
                c => { c.Parameters.AddWithValue("@date", destinationDay.UtcDateTime.Date); c.Parameters.AddWithValue("@start", destinationDay.AddHours(2)); c.Parameters.AddWithValue("@end", destinationDay.AddHours(3)); c.Parameters.AddWithValue("@id", moving); });
            var moveFailure = await Assert.ThrowsAsync<PostgresException>(() => moveTask.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal("40001", moveFailure.SqlState);

            await lockTransaction.CommitAsync();
            Assert.Equal(StatusCodes.Status200OK, Status(await certificationTask.WaitAsync(TimeSpan.FromSeconds(10))));
            Assert.Equal(2, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM hos_logs WHERE company_id=@c AND driver_id=@d AND log_date=@date AND branch_id=@b AND is_certified",
                c => { c.Parameters.AddWithValue("@c", seed.CompanyId); c.Parameters.AddWithValue("@d", seed.DriverA); c.Parameters.AddWithValue("@date", targetDay.UtcDateTime.Date); c.Parameters.AddWithValue("@b", seed.BranchA); }));
            Assert.Equal(2, await db.ScalarLongAsync(
                "SELECT jsonb_array_length(source_snapshot) FROM hos_certifications WHERE company_id=@c AND driver_id=@d AND log_date=@date",
                c => { c.Parameters.AddWithValue("@c", seed.CompanyId); c.Parameters.AddWithValue("@d", seed.DriverA); c.Parameters.AddWithValue("@date", targetDay.UtcDateTime.Date); }));
        }
        finally { await Cleanup(db, seed.CompanyId); }
    }

    [Fact]
    public async Task HosSoftDeleteActivationInvalidatesSequentiallyAndFailsFastDuringCertification()
    {
        var db = Db(); var seed = await Seed(db);
        var sequentialDay = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(-14), TimeSpan.Zero);
        var concurrentDay = sequentialDay.AddDays(-1);
        try
        {
            var sequentialActive = await InsertHosLog(db, seed.CompanyId, seed.BranchA, seed.DriverA,
                sequentialDay.AddHours(1), sequentialDay.AddHours(2), "Off Duty", "activate-sequential-active");
            var sequentialTombstone = await InsertDeletedHosLog(db, seed.CompanyId, seed.BranchA, seed.DriverA,
                sequentialDay.AddHours(2), sequentialDay.AddHours(3), "Driving", "activate-sequential-tombstone");
            Assert.Equal(StatusCodes.Status200OK, Status(await Invoke("HosCertifyPilot", DriverPrincipal(seed.CompanyId, seed.BranchA), sequentialActive,
                HosAttestation(), db, new AuditService(db), CancellationToken.None)));
            Assert.Equal(1, await db.ExecuteAsync("UPDATE hos_logs SET deleted_at=NULL WHERE id=@id", c => c.Parameters.AddWithValue("@id", sequentialTombstone)));
            Assert.Equal(0, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM hos_logs WHERE company_id=@c AND driver_id=@d AND log_date=@date AND branch_id=@b AND is_certified",
                c => { c.Parameters.AddWithValue("@c", seed.CompanyId); c.Parameters.AddWithValue("@d", seed.DriverA); c.Parameters.AddWithValue("@date", sequentialDay.UtcDateTime.Date); c.Parameters.AddWithValue("@b", seed.BranchA); }));

            var concurrentActive = await InsertHosLog(db, seed.CompanyId, seed.BranchA, seed.DriverA,
                concurrentDay.AddHours(1), concurrentDay.AddHours(2), "Off Duty", "activate-concurrent-active");
            var concurrentTombstone = await InsertDeletedHosLog(db, seed.CompanyId, seed.BranchA, seed.DriverA,
                concurrentDay.AddHours(2), concurrentDay.AddHours(3), "Driving", "activate-concurrent-tombstone");

            await using var lockConnection = new NpgsqlConnection(TestDb.ConnectionString);
            await lockConnection.OpenAsync();
            await using var lockTransaction = await lockConnection.BeginTransactionAsync();
            await using (var lockCommand = new NpgsqlCommand(
                "SELECT pg_advisory_xact_lock(stage65_hos_day_lock_key(@c,@d,@date::date,@b))",
                lockConnection, lockTransaction))
            {
                lockCommand.Parameters.AddWithValue("@c", seed.CompanyId); lockCommand.Parameters.AddWithValue("@d", seed.DriverA);
                lockCommand.Parameters.AddWithValue("@date", concurrentDay.UtcDateTime.Date); lockCommand.Parameters.AddWithValue("@b", seed.BranchA);
                await lockCommand.ExecuteNonQueryAsync();
            }

            var certificationApp = $"hos-cert-activate-{Guid.NewGuid():N}";
            var certificationDb = Db(certificationApp);
            var certificationTask = Invoke("HosCertifyPilot", DriverPrincipal(seed.CompanyId, seed.BranchA), concurrentActive,
                HosAttestation(), certificationDb, new AuditService(certificationDb), CancellationToken.None);
            await WaitForAdvisoryWaiter(db, certificationApp);
            var activationTask = Db($"hos-activate-{Guid.NewGuid():N}").ExecuteAsync(
                "UPDATE hos_logs SET deleted_at=NULL WHERE id=@id", c => c.Parameters.AddWithValue("@id", concurrentTombstone));
            var activationFailure = await Assert.ThrowsAsync<PostgresException>(() => activationTask.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal("40001", activationFailure.SqlState);

            await lockTransaction.CommitAsync();
            Assert.Equal(StatusCodes.Status200OK, Status(await certificationTask.WaitAsync(TimeSpan.FromSeconds(10))));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM hos_logs WHERE id=@id AND deleted_at IS NOT NULL", c => c.Parameters.AddWithValue("@id", concurrentTombstone)));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM hos_logs WHERE id=@id AND is_certified", c => c.Parameters.AddWithValue("@id", concurrentActive)));
            var snapshot = await db.QuerySingleAsync(
                "SELECT jsonb_array_length(source_snapshot) snapshot_count,source_snapshot::text source_snapshot FROM hos_certifications WHERE company_id=@c AND driver_id=@d AND log_date=@date",
                c => { c.Parameters.AddWithValue("@c", seed.CompanyId); c.Parameters.AddWithValue("@d", seed.DriverA); c.Parameters.AddWithValue("@date", concurrentDay.UtcDateTime.Date); });
            Assert.Equal(1, Convert.ToInt32(snapshot!["snapshotCount"]));
            Assert.DoesNotContain("activate-concurrent-tombstone", snapshot["sourceSnapshot"]?.ToString(), StringComparison.Ordinal);
        }
        finally { await Cleanup(db, seed.CompanyId); }
    }

    [Fact]
    public async Task BranchScopedHosRecommendationsFailClosedWithoutBranchMetadata()
    {
        var db = Db(); var seed = await Seed(db);
        try
        {
            var title = $"Other branch HOS narrative {seed.CompanyId}";
            await db.InsertAsync(
                @"INSERT INTO ai_recommendations(company_id,tenant_id,module_key,title,score)
                  VALUES(@c,@c,'hos-eld',@title,99)",
                c => { c.Parameters.AddWithValue("@c", seed.CompanyId); c.Parameters.AddWithValue("@title", title); });
            var branchResult = await Invoke("HosRecommendationsPilot", Principal(seed.CompanyId, seed.BranchA, "compliance:view"), db, CancellationToken.None);
            Assert.DoesNotContain(title, JsonSerializer.Serialize(Assert.IsAssignableFrom<IValueHttpResult>(branchResult).Value), StringComparison.Ordinal);
            var tenantResult = await Invoke("HosRecommendationsPilot", Principal(seed.CompanyId, null, "compliance:view"), db, CancellationToken.None);
            Assert.Contains(title, JsonSerializer.Serialize(Assert.IsAssignableFrom<IValueHttpResult>(tenantResult).Value), StringComparison.Ordinal);
        }
        finally { await Cleanup(db, seed.CompanyId); }
    }

    [Fact]
    public async Task Stage65RequiredHosTriggersFunctionsAndEldHistoryRlsExist()
    {
        var db = Db();
        var triggerNames = new[]
        {
            "trg_stage65_hos_insert_day_lock",
            "trg_stage65_hos_identity_day_lock",
            "trg_stage65_hos_identity_change_invalidates_days",
            "trg_stage65_hos_material_change_invalidates_certification",
            "trg_stage65_hos_segment_change_invalidates_day",
            "trg_stage65_hos_insert_invalidates_certified_day",
            "trg_stage65_prevent_certified_hos_log_delete",
            "trg_stage65_hos_certification_snapshot_immutable"
        };
        foreach (var name in triggerNames)
            Assert.Equal(1, await db.ScalarLongAsync(
                @"SELECT COUNT(*) FROM pg_trigger t JOIN pg_class c ON c.oid=t.tgrelid
                  WHERE t.tgname=@name AND NOT t.tgisinternal AND t.tgenabled<>'D'",
                c => c.Parameters.AddWithValue("@name", name)));
        foreach (var name in new[]
        {
            "stage65_hos_day_lock_key",
            "stage65_lock_hos_day_on_insert",
            "stage65_lock_hos_days_on_identity_change",
            "stage65_invalidate_hos_days_on_identity_change",
            "stage65_invalidate_hos_certification_on_material_change",
            "stage65_invalidate_hos_certified_day",
            "stage65_invalidate_hos_day_on_insert",
            "stage65_prevent_certified_hos_log_delete",
            "stage65_guard_hos_certification_snapshot"
        })
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM pg_proc WHERE proname=@name", c => c.Parameters.AddWithValue("@name", name)));

        var history = await db.QuerySingleAsync(
            @"SELECT c.relrowsecurity,c.relforcerowsecurity,
                     to_regclass('public.eld_malfunction_history') IS NOT NULL table_exists,
                     to_regclass('public.idx_eld_malfunction_history_company_device') IS NOT NULL index_exists
              FROM pg_class c WHERE c.oid='public.eld_malfunction_history'::regclass");
        Assert.True(Convert.ToBoolean(history!["relrowsecurity"]));
        Assert.True(Convert.ToBoolean(history["relforcerowsecurity"]));
        Assert.True(Convert.ToBoolean(history["tableExists"]));
        Assert.True(Convert.ToBoolean(history["indexExists"]));
        Assert.Equal(2, await db.ScalarLongAsync(
            "SELECT COUNT(*) FROM pg_policies WHERE schemaname='public' AND tablename='eld_malfunction_history' AND policyname IN ('tenant_ticket_app','system_control_plane')"));
    }

    [Fact]
    public async Task HosImmutabilityAllowsOnlyDualGatedSystemOffboardingDeletes()
    {
        var owner = Db(); var seed = await Seed(owner);
        var start = new DateTimeOffset(DateTime.UtcNow.Date.AddHours(7), TimeSpan.Zero);
        var logId = await InsertHosLog(owner, seed.CompanyId, seed.BranchA, seed.DriverA,
            start, start.AddHours(1), "Driving", "stage72-offboarding");
        await owner.ExecuteAsync("UPDATE hos_logs SET is_certified=TRUE,certified_at=NOW(),certified_by=@u WHERE id=@id",
            c => { c.Parameters.AddWithValue("@id", logId); c.Parameters.AddWithValue("@u", DriverUserId(seed.CompanyId)); });
        var certificationId = await owner.InsertAsync(
            @"INSERT INTO hos_certifications(company_id,branch_id,driver_id,log_date,attestation_text,attestation_hash,
                certified_by,source_revision,source_snapshot)
              VALUES(@c,@b,@d,@date,'Stage72 test',repeat('a',64),@u,repeat('b',64),'[]'::jsonb)",
            c => { c.Parameters.AddWithValue("@c", seed.CompanyId); c.Parameters.AddWithValue("@b", seed.BranchA); c.Parameters.AddWithValue("@d", seed.DriverA); c.Parameters.AddWithValue("@date", start.UtcDateTime.Date); c.Parameters.AddWithValue("@u", DriverUserId(seed.CompanyId)); });

        try
        {
            var runtime = RuntimeDb();
            await Assert.ThrowsAsync<PostgresException>(() => runtime.RunInTenantTransactionAsync<object?>(seed.CompanyId, async () =>
            {
                await runtime.ExecuteAsync("SELECT set_config('opstrax.offboarding','on',true)");
                await runtime.ExecuteAsync("DELETE FROM hos_certifications WHERE id=@id", c => c.Parameters.AddWithValue("@id", certificationId));
                return null;
            }));

            await using (var system = new NpgsqlConnection(TestDb.SystemConnectionString))
            {
                await system.OpenAsync();
                await using (var ordinaryLogDelete = await system.BeginTransactionAsync())
                {
                    await Assert.ThrowsAsync<PostgresException>(async () =>
                    {
                        await using var delete = new NpgsqlCommand("DELETE FROM hos_logs WHERE id=@id", system, ordinaryLogDelete);
                        delete.Parameters.AddWithValue("@id", logId);
                        await delete.ExecuteNonQueryAsync();
                    });
                    await ordinaryLogDelete.RollbackAsync();
                }
                await using (var ordinary = await system.BeginTransactionAsync())
                {
                    await Assert.ThrowsAsync<PostgresException>(async () =>
                    {
                        await using var delete = new NpgsqlCommand("DELETE FROM hos_certifications WHERE id=@id", system, ordinary);
                        delete.Parameters.AddWithValue("@id", certificationId);
                        await delete.ExecuteNonQueryAsync();
                    });
                    await ordinary.RollbackAsync();
                }
                await using (var updateAttempt = await system.BeginTransactionAsync())
                {
                    await SetLocal(system, updateAttempt, "opstrax.offboarding", "on");
                    await Assert.ThrowsAsync<PostgresException>(async () =>
                    {
                        await using var update = new NpgsqlCommand("UPDATE hos_certifications SET attestation_text='tampered' WHERE id=@id", system, updateAttempt);
                        update.Parameters.AddWithValue("@id", certificationId);
                        await update.ExecuteNonQueryAsync();
                    });
                    await updateAttempt.RollbackAsync();
                }
                await using (var offboarding = await system.BeginTransactionAsync())
                {
                    await SetLocal(system, offboarding, "opstrax.offboarding", "on");
                    await using var deleteCertification = new NpgsqlCommand("DELETE FROM hos_certifications WHERE id=@cert", system, offboarding);
                    deleteCertification.Parameters.AddWithValue("@cert", certificationId);
                    Assert.Equal(1, await deleteCertification.ExecuteNonQueryAsync());
                    await using var deleteLog = new NpgsqlCommand("DELETE FROM hos_logs WHERE id=@log", system, offboarding);
                    deleteLog.Parameters.AddWithValue("@log", logId);
                    Assert.Equal(1, await deleteLog.ExecuteNonQueryAsync());
                    await offboarding.CommitAsync();
                }
            }
        }
        finally { await Cleanup(owner, seed.CompanyId); }
    }

    [Fact]
    public async Task EldMalfunctionHistoryRlsUsesSignedTenantTicket()
    {
        var owner = Db(); var seedA = await Seed(owner); var seedB = await Seed(owner);
        var marker = $"rls-history-{Guid.NewGuid():N}";
        try
        {
            foreach (var seed in new[] { seedA, seedB })
                await owner.InsertAsync(
                    @"INSERT INTO eld_malfunction_history(company_id,branch_id,eld_device_id,event_type,from_status,to_status,
                        malfunction_code,malfunction_description,actor_user_id)
                      VALUES(@c,@b,999999,'RLS Verification','Active','Malfunction',@marker,'tenant isolation marker',42)",
                    c => { c.Parameters.AddWithValue("@c", seed.CompanyId); c.Parameters.AddWithValue("@b", seed.BranchA); c.Parameters.AddWithValue("@marker", marker); });

            var runtime = RuntimeDb();
            Assert.Equal(1, await runtime.RunInTenantScopeAsync(seedA.CompanyId,
                () => runtime.ScalarLongAsync("SELECT COUNT(*) FROM eld_malfunction_history WHERE malfunction_code=@marker", c => c.Parameters.AddWithValue("@marker", marker))));
            Assert.Equal(1, await runtime.RunInTenantScopeAsync(seedB.CompanyId,
                () => runtime.ScalarLongAsync("SELECT COUNT(*) FROM eld_malfunction_history WHERE malfunction_code=@marker", c => c.Parameters.AddWithValue("@marker", marker))));
            Assert.Equal(0, await runtime.ScalarLongAsync("SELECT COUNT(*) FROM eld_malfunction_history WHERE malfunction_code=@marker", c => c.Parameters.AddWithValue("@marker", marker)));
            Assert.Equal(2, await runtime.RunInSystemScopeAsync(
                () => runtime.ScalarLongAsync("SELECT COUNT(*) FROM eld_malfunction_history WHERE malfunction_code=@marker", c => c.Parameters.AddWithValue("@marker", marker))));
        }
        finally { await Cleanup(owner, seedA.CompanyId); await Cleanup(owner, seedB.CompanyId); }
    }

    [Fact]
    public async Task HosCertificationUsesOnlyAuthenticatedBranchAndRejectsCrossDateSegments()
    {
        var db = Db(); var seed = await Seed(db);
        try
        {
            var day = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(-4), TimeSpan.Zero);
            var branchA = await InsertHosLog(db, seed.CompanyId, seed.BranchA, seed.DriverA, day.AddHours(1), day.AddHours(2), "Off Duty", "branch-a");
            var branchB = await InsertHosLog(db, seed.CompanyId, seed.BranchB, seed.DriverA, day.AddHours(1), null, "Driving", "branch-b-open");
            Assert.Equal(StatusCodes.Status200OK, Status(await Invoke("HosCertifyPilot", DriverPrincipal(seed.CompanyId, seed.BranchA), branchA, HosAttestation(), db, new AuditService(db), CancellationToken.None)));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM hos_logs WHERE id=@id AND is_certified", c => c.Parameters.AddWithValue("@id", branchA)));
            Assert.Equal(0, await db.ScalarLongAsync("SELECT COUNT(*) FROM hos_logs WHERE id=@id AND is_certified", c => c.Parameters.AddWithValue("@id", branchB)));
            var read = await Invoke("DriverHosLogsPilot", DriverPrincipal(seed.CompanyId, seed.BranchA), db, CancellationToken.None);
            Assert.DoesNotContain($"\"id\":{branchB}", JsonSerializer.Serialize(Assert.IsAssignableFrom<IValueHttpResult>(read).Value), StringComparison.Ordinal);

            var crossDate = await InsertHosLog(db, seed.CompanyId, seed.BranchA, seed.DriverA,
                day.AddHours(23), day.AddDays(1).AddMinutes(30), "Sleeper Berth", "cross-date");
            Assert.Equal(StatusCodes.Status409Conflict, Status(await Invoke("HosCertifyPilot", DriverPrincipal(seed.CompanyId, seed.BranchA), crossDate, HosAttestation(), db, new AuditService(db), CancellationToken.None)));
        }
        finally { await Cleanup(db, seed.CompanyId); }
    }

    [Fact]
    public async Task HosDailyCertificationRejectsOverlapsAndDurationMismatchTransactionally()
    {
        var db = Db(); var seed = await Seed(db);
        try
        {
            var day = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(-2), TimeSpan.Zero);
            var first = await InsertHosLog(db, seed.CompanyId, seed.BranchA, seed.DriverA, day.AddHours(1), day.AddHours(4), "Off Duty", "integrity-one");
            var second = await InsertHosLog(db, seed.CompanyId, seed.BranchA, seed.DriverA, day.AddHours(3), day.AddHours(5), "Driving", "integrity-two");
            Assert.Equal(StatusCodes.Status409Conflict, Status(await Invoke("HosCertifyPilot", DriverPrincipal(seed.CompanyId, seed.BranchA), first, HosAttestation(), db, new AuditService(db), CancellationToken.None)));
            await db.ExecuteAsync("UPDATE hos_logs SET start_time=@start,end_time=@end,duration_minutes=10 WHERE id=@id",
                c => { c.Parameters.AddWithValue("@start", day.AddHours(4)); c.Parameters.AddWithValue("@end", day.AddHours(6)); c.Parameters.AddWithValue("@id", second); });
            Assert.Equal(StatusCodes.Status409Conflict, Status(await Invoke("HosCertifyPilot", DriverPrincipal(seed.CompanyId, seed.BranchA), first, HosAttestation(), db, new AuditService(db), CancellationToken.None)));
            Assert.Equal(0, await db.ScalarLongAsync("SELECT COUNT(*) FROM hos_certifications WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", seed.CompanyId)));
            await db.ExecuteAsync("UPDATE hos_logs SET duration_minutes=120 WHERE id=@id", c => c.Parameters.AddWithValue("@id", second));
            Assert.Equal(StatusCodes.Status200OK, Status(await Invoke("HosCertifyPilot", DriverPrincipal(seed.CompanyId, seed.BranchA), first, HosAttestation(), db, new AuditService(db), CancellationToken.None)));
        }
        finally { await Cleanup(db, seed.CompanyId); }
    }

    [Fact]
    public async Task DvirRepairCertificationCannotClearAnUnresolvedOutOfServiceDefect()
    {
        var db = Db();
        var seed = await Seed(db);
        try
        {
            var report = await db.InsertAsync(
                @"INSERT INTO dvir_reports(company_id,branch_id,report_number,driver_id,vehicle_id,inspection_type,
                    inspection_status,defects_found,safe_to_operate,mechanic_review_status,repair_certification_status)
                  VALUES(@c,@b,@n,@d,@v,'Pre-Trip','defect_found',1,FALSE,'Pending','Pending')",
                c => { c.Parameters.AddWithValue("@c", seed.CompanyId); c.Parameters.AddWithValue("@b", seed.BranchA); c.Parameters.AddWithValue("@n", $"DVIR-LIFE-{seed.CompanyId}"); c.Parameters.AddWithValue("@d", seed.DriverA); c.Parameters.AddWithValue("@v", seed.VehicleA); });
            var defect = await db.InsertAsync(
                @"INSERT INTO dvir_defects(company_id,branch_id,dvir_report_id,vehicle_id,driver_id,defect_category,
                    defect_description,severity,status,out_of_service)
                  VALUES(@c,@b,@r,@v,@d,'Brakes','Brake pressure loss','Critical','Open',TRUE)",
                c => { c.Parameters.AddWithValue("@c", seed.CompanyId); c.Parameters.AddWithValue("@b", seed.BranchA); c.Parameters.AddWithValue("@r", report); c.Parameters.AddWithValue("@v", seed.VehicleA); c.Parameters.AddWithValue("@d", seed.DriverA); });
            var http = Principal(seed.CompanyId, seed.BranchA, "maintenance:update", "maintenance:close");
            var audit = new AuditService(db);
            await db.ExecuteAsync("UPDATE vehicles SET out_of_service=TRUE,availability_status='out_of_service' WHERE id=@id", c => c.Parameters.AddWithValue("@id", seed.VehicleA));

            var beforeReview = await Invoke("DvirCertifyRepairPilot", http, report, Version(1), db, audit, CancellationToken.None);
            Assert.Equal(StatusCodes.Status409Conflict, Status(beforeReview));
            var rejectedBeforeReview = await db.QuerySingleAsync(
                "SELECT mechanic_review_status,repair_certification_status,safe_to_operate,row_version FROM dvir_reports WHERE id=@id",
                c => c.Parameters.AddWithValue("@id", report));
            Assert.Equal("Pending", rejectedBeforeReview!["mechanicReviewStatus"]);
            Assert.Equal("Pending", rejectedBeforeReview["repairCertificationStatus"]);
            Assert.False(Convert.ToBoolean(rejectedBeforeReview["safeToOperate"]));
            Assert.Equal(1L, Convert.ToInt64(rejectedBeforeReview["rowVersion"]));
            Assert.Equal(0, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM audit_logs WHERE company_id=@c AND entity_id=@id AND action_name IN ('dvir.repair.certified','dvir.repairs.driver_acknowledged')",
                c => { c.Parameters.AddWithValue("@c", seed.CompanyId); c.Parameters.AddWithValue("@id", report); }));
            Assert.Equal(StatusCodes.Status200OK, Status(await Invoke("DvirMechanicReviewPilot", http, report, Version(1), db, audit, CancellationToken.None)));
            var staleReview = await Invoke("DvirMechanicReviewPilot", http, report, Version(1), db, audit, CancellationToken.None);
            Assert.Equal(StatusCodes.Status409Conflict, Status(staleReview));
            var unresolved = await Invoke("DvirCertifyRepairPilot", http, report, Version(2), db, audit, CancellationToken.None);
            Assert.Equal(StatusCodes.Status409Conflict, Status(unresolved));
            var rejectedUnresolved = await db.QuerySingleAsync(
                "SELECT repair_certification_status,safe_to_operate,row_version FROM dvir_reports WHERE id=@id",
                c => c.Parameters.AddWithValue("@id", report));
            Assert.Equal("Pending", rejectedUnresolved!["repairCertificationStatus"]);
            Assert.False(Convert.ToBoolean(rejectedUnresolved["safeToOperate"]));
            Assert.Equal(2L, Convert.ToInt64(rejectedUnresolved["rowVersion"]));
            Assert.Equal(0, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM audit_logs WHERE company_id=@c AND entity_id=@id AND action_name='dvir.repair.certified'",
                c => { c.Parameters.AddWithValue("@c", seed.CompanyId); c.Parameters.AddWithValue("@id", report); }));

            Assert.Equal(StatusCodes.Status200OK, Status(await Invoke("DvirDefectResolvePilot", defect, http,
                new Dictionary<string, object?> { ["rowVersion"] = 2, ["notes"] = "Brake pressure repaired and tested" }, db, audit, CancellationToken.None)));
            await MaintenanceBackgroundService.UpdateVehicleAvailabilityAsync(db, CancellationToken.None);
            Assert.True(Convert.ToBoolean((await db.QuerySingleAsync("SELECT out_of_service FROM vehicles WHERE id=@id", c => c.Parameters.AddWithValue("@id", seed.VehicleA)))!["outOfService"]));
            Assert.Equal(StatusCodes.Status200OK, Status(await Invoke("DvirCertifyRepairPilot", http, report, Version(2), db, audit, CancellationToken.None)));
            var certified = await db.QuerySingleAsync("SELECT safe_to_operate,repair_certification_status,row_version FROM dvir_reports WHERE id=@id", c => c.Parameters.AddWithValue("@id", report));
            Assert.False(Convert.ToBoolean(certified!["safeToOperate"]));
            Assert.Equal("Certified", certified["repairCertificationStatus"]);
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM dvir_defects WHERE id=@id AND repair_certified_by=42 AND repair_certified_at IS NOT NULL", c => c.Parameters.AddWithValue("@id", defect)));
            Assert.Equal(StatusCodes.Status403Forbidden, Status(await Invoke("DvirDriverAcknowledgeRepairPilot", http, report, RepairAttestation(3), db, audit, CancellationToken.None)));
            Assert.Equal(0, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM audit_logs WHERE company_id=@c AND entity_id=@id AND action_name='dvir.repairs.driver_acknowledged'",
                c => { c.Parameters.AddWithValue("@c", seed.CompanyId); c.Parameters.AddWithValue("@id", report); }));
            Assert.Equal(StatusCodes.Status200OK, Status(await Invoke("DvirDriverAcknowledgeRepairPilot", DriverPrincipal(seed.CompanyId, seed.BranchA), report, RepairAttestation(3), db, audit, CancellationToken.None)));
            var released = await db.QuerySingleAsync("SELECT safe_to_operate,driver_repair_acknowledged_by FROM dvir_reports WHERE id=@id", c => c.Parameters.AddWithValue("@id", report));
            Assert.True(Convert.ToBoolean(released!["safeToOperate"]));
            Assert.Equal(DriverUserId(seed.CompanyId), Convert.ToInt64(released["driverRepairAcknowledgedBy"]));
            Assert.False(Convert.ToBoolean((await db.QuerySingleAsync("SELECT out_of_service FROM vehicles WHERE id=@id", c => c.Parameters.AddWithValue("@id", seed.VehicleA)))!["outOfService"]));
            Assert.Equal(1, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM audit_logs WHERE company_id=@c AND entity_id=@id AND action_name='dvir.repair.certified'",
                c => { c.Parameters.AddWithValue("@c", seed.CompanyId); c.Parameters.AddWithValue("@id", report); }));
            Assert.Equal(1, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM audit_logs WHERE company_id=@c AND entity_id=@id AND action_name='dvir.repairs.driver_acknowledged'",
                c => { c.Parameters.AddWithValue("@c", seed.CompanyId); c.Parameters.AddWithValue("@id", report); }));
        }
        finally { await Cleanup(db, seed.CompanyId); }
    }

    [Fact]
    public async Task DvirCreationIsConcurrentRetrySafeAndRejectsCrossBranchResources()
    {
        var db = Db();
        var seed = await Seed(db);
        try
        {
            var body = new Dictionary<string, object?>
            {
                ["driverId"] = seed.DriverA,
                ["vehicleId"] = seed.VehicleA,
                ["inspectionType"] = "Pre-Trip"
            };
            var dbA = Db(); var dbB = Db();
            var httpA = Principal(seed.CompanyId, seed.BranchA, "maintenance:create");
            var httpB = Principal(seed.CompanyId, seed.BranchA, "maintenance:create");
            httpA.Request.Headers["Idempotency-Key"] = "same-mobile-retry";
            httpB.Request.Headers["Idempotency-Key"] = "same-mobile-retry";
            var results = await Task.WhenAll(
                Invoke("CreateDvirReportPilot", httpA, body, dbA, new AuditService(dbA), CancellationToken.None),
                Invoke("CreateDvirReportPilot", httpB, body, dbB, new AuditService(dbB), CancellationToken.None));
            Assert.All(results, result => Assert.Contains(Status(result), new[] { StatusCodes.Status200OK, StatusCodes.Status201Created }));
            Assert.Equal(1, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM dvir_reports WHERE company_id=@c AND idempotency_key='same-mobile-retry'",
                c => c.Parameters.AddWithValue("@c", seed.CompanyId)));

            var changed = new Dictionary<string, object?>(body) { ["notes"] = "changed retry payload" };
            var changedHttp = Principal(seed.CompanyId, seed.BranchA, "maintenance:create");
            changedHttp.Request.Headers["Idempotency-Key"] = "same-mobile-retry";
            Assert.Equal(StatusCodes.Status409Conflict, Status(await Invoke("CreateDvirReportPilot", changedHttp, changed, db, new AuditService(db), CancellationToken.None)));

            var crossBranchBody = new Dictionary<string, object?>(body)
            {
                ["vehicleId"] = seed.VehicleB,
                ["reportNumber"] = $"DVIR-CROSS-{seed.CompanyId}"
            };
            var cross = await Invoke("CreateDvirReportPilot", Principal(seed.CompanyId, seed.BranchA, "maintenance:create"), crossBranchBody, db, new AuditService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status404NotFound, Status(cross));
        }
        finally { await Cleanup(db, seed.CompanyId); }
    }

    [Fact]
    public async Task EldMalfunctionLifecycleEnforcesPermissionTenantBranchAndState()
    {
        var db = Db();
        var seed = await Seed(db);
        try
        {
            var deviceA = await Device(db, seed.CompanyId, seed.BranchA, seed.VehicleA, "A");
            var deviceB = await Device(db, seed.CompanyId, seed.BranchB, seed.VehicleB, "B");
            var valid = new Dictionary<string, object?> { ["rowVersion"] = 1, ["malfunctionCode"] = "P1", ["malfunctionDescription"] = "Power compliance diagnostic" };

            var readOnly = await Invoke("EldMarkMalfunctionPilot", Principal(seed.CompanyId, seed.BranchA, "compliance:view"), deviceA, valid, db, new AuditService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status403Forbidden, Status(readOnly));
            var wrongBranch = await Invoke("EldMarkMalfunctionPilot", Principal(seed.CompanyId, seed.BranchA, "compliance:update"), deviceB, valid, db, new AuditService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status404NotFound, Status(wrongBranch));
            var invalid = await Invoke("EldMarkMalfunctionPilot", Principal(seed.CompanyId, seed.BranchA, "compliance:update"), deviceA,
                new Dictionary<string, object?> { ["malfunctionCode"] = "", ["malfunctionDescription"] = "" }, db, new AuditService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status400BadRequest, Status(invalid));
            var beforeAcceptedMutation = await db.QuerySingleAsync(
                "SELECT status,row_version FROM eld_devices WHERE id=@id",
                c => c.Parameters.AddWithValue("@id", deviceA));
            Assert.Equal("Diagnostic", beforeAcceptedMutation!["status"]);
            Assert.Equal(1L, Convert.ToInt64(beforeAcceptedMutation["rowVersion"]));
            Assert.Equal(0, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM eld_malfunction_history WHERE company_id=@c AND eld_device_id IN (@a,@b)",
                c => { c.Parameters.AddWithValue("@c", seed.CompanyId); c.Parameters.AddWithValue("@a", deviceA); c.Parameters.AddWithValue("@b", deviceB); }));
            Assert.Equal(0, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM audit_logs WHERE company_id=@c AND action_name='eld.malfunction' AND entity_id IN (@a,@b)",
                c => { c.Parameters.AddWithValue("@c", seed.CompanyId); c.Parameters.AddWithValue("@a", deviceA); c.Parameters.AddWithValue("@b", deviceB); }));
            Assert.Equal(StatusCodes.Status200OK, Status(await Invoke("EldMarkMalfunctionPilot", Principal(seed.CompanyId, seed.BranchA, "compliance:update"), deviceA, valid, db, new AuditService(db), CancellationToken.None)));
            Assert.Equal("Malfunction", (await db.QuerySingleAsync("SELECT status FROM eld_devices WHERE id=@id", c => c.Parameters.AddWithValue("@id", deviceA)))!["status"]);
            Assert.Equal(StatusCodes.Status409Conflict, Status(await Invoke("EldMarkMalfunctionPilot", Principal(seed.CompanyId, seed.BranchA, "compliance:update"), deviceA,
                new Dictionary<string, object?> { ["rowVersion"] = 2, ["malfunctionCode"] = "P2", ["malfunctionDescription"] = "duplicate overwrite" }, db, new AuditService(db), CancellationToken.None)));
            var afterRejectedOverwrite = await db.QuerySingleAsync(
                "SELECT status,malfunction_code,malfunction_description,row_version FROM eld_devices WHERE id=@id",
                c => c.Parameters.AddWithValue("@id", deviceA));
            Assert.Equal("Malfunction", afterRejectedOverwrite!["status"]);
            Assert.Equal("P1", afterRejectedOverwrite["malfunctionCode"]);
            Assert.Equal("Power compliance diagnostic", afterRejectedOverwrite["malfunctionDescription"]);
            Assert.Equal(2L, Convert.ToInt64(afterRejectedOverwrite["rowVersion"]));
            Assert.Equal(1, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM eld_malfunction_history WHERE company_id=@c AND eld_device_id=@id AND event_type='Malfunction Reported'",
                c => { c.Parameters.AddWithValue("@c", seed.CompanyId); c.Parameters.AddWithValue("@id", deviceA); }));
            Assert.Equal(1, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM audit_logs WHERE company_id=@c AND action_name='eld.malfunction' AND entity_id=@id",
                c => { c.Parameters.AddWithValue("@c", seed.CompanyId); c.Parameters.AddWithValue("@id", deviceA); }));
            var resolve = new Dictionary<string, object?> { ["rowVersion"] = 2, ["resolutionEvidence"] = "Provider diagnostic completed; device rebooted and inspected" };
            Assert.Equal(StatusCodes.Status200OK, Status(await Invoke("EldResolveMalfunctionPilot", Principal(seed.CompanyId, seed.BranchA, "compliance:update"), deviceA, resolve, db, new AuditService(db), CancellationToken.None)));
            var diagnostic = await db.QuerySingleAsync("SELECT status,malfunction_code,malfunction_description,resolution_evidence,malfunction_resolved_by,row_version FROM eld_devices WHERE id=@id", c => c.Parameters.AddWithValue("@id", deviceA));
            Assert.Equal("Diagnostic", diagnostic!["status"]);
            Assert.Equal("P1", diagnostic["malfunctionCode"]);
            Assert.NotNull(diagnostic["malfunctionDescription"]);
            Assert.NotNull(diagnostic["resolutionEvidence"]);
            Assert.Equal(42L, Convert.ToInt64(diagnostic["malfunctionResolvedBy"]));
            Assert.Equal(StatusCodes.Status409Conflict, Status(await Invoke("EldResolveMalfunctionPilot", Principal(seed.CompanyId, seed.BranchA, "compliance:update"), deviceA, resolve, db, new AuditService(db), CancellationToken.None)));

            await db.ExecuteAsync("UPDATE eld_devices SET last_sync_at=NOW(),provider_sync_status='Healthy',api_key_hash=@hash,hmac_secret=NULL,hmac_secret_encrypted=@secret WHERE id=@id",
                c => { c.Parameters.AddWithValue("@hash", new string('a', 64)); c.Parameters.AddWithValue("@secret", new string('s', 32)); c.Parameters.AddWithValue("@id", deviceA); });
            var verified = new Dictionary<string, object?> { ["rowVersion"] = 3, ["resolutionEvidence"] = "Provider shows healthy sync and operator verified status" };
            Assert.Equal(StatusCodes.Status200OK, Status(await Invoke("EldResolveMalfunctionPilot", Principal(seed.CompanyId, seed.BranchA, "compliance:update"), deviceA, verified, db, new AuditService(db), CancellationToken.None)));
            Assert.Equal("Active", (await db.QuerySingleAsync("SELECT status FROM eld_devices WHERE id=@id", c => c.Parameters.AddWithValue("@id", deviceA)))!["status"]);
            Assert.Equal(3, await db.ScalarLongAsync("SELECT COUNT(*) FROM eld_malfunction_history WHERE company_id=@c AND eld_device_id=@id", c => { c.Parameters.AddWithValue("@c", seed.CompanyId); c.Parameters.AddWithValue("@id", deviceA); }));
        }
        finally { await Cleanup(db, seed.CompanyId); }
    }

    [Fact]
    public async Task ConcurrentEldMalfunctionReportsHaveOneWinnerAndNoDuplicateHistoryOrAudit()
    {
        var owner = Db();
        var seed = await Seed(owner);
        try
        {
            var device = await Device(owner, seed.CompanyId, seed.BranchA, seed.VehicleA, "RACE");
            var request = new Dictionary<string, object?>
            {
                ["rowVersion"] = 1,
                ["malfunctionCode"] = "P1",
                ["malfunctionDescription"] = "Concurrent power-compliance report"
            };
            var dbA = Db("eld-malfunction-race-a");
            var dbB = Db("eld-malfunction-race-b");

            var results = await Task.WhenAll(
                Invoke("EldMarkMalfunctionPilot",
                    Principal(seed.CompanyId, seed.BranchA, "compliance:update"),
                    device, request, dbA, new AuditService(dbA), CancellationToken.None),
                Invoke("EldMarkMalfunctionPilot",
                    Principal(seed.CompanyId, seed.BranchA, "compliance:update"),
                    device, request, dbB, new AuditService(dbB), CancellationToken.None));

            Assert.Single(results, result => Status(result) == StatusCodes.Status200OK);
            Assert.Single(results, result => Status(result) == StatusCodes.Status409Conflict);

            var persisted = await owner.QuerySingleAsync(
                "SELECT status,malfunction_code,malfunction_description,row_version FROM eld_devices WHERE id=@id",
                c => c.Parameters.AddWithValue("@id", device));
            Assert.Equal("Malfunction", persisted!["status"]);
            Assert.Equal("P1", persisted["malfunctionCode"]);
            Assert.Equal("Concurrent power-compliance report", persisted["malfunctionDescription"]);
            Assert.Equal(2L, Convert.ToInt64(persisted["rowVersion"]));
            Assert.Equal(1, await owner.ScalarLongAsync(
                "SELECT COUNT(*) FROM eld_malfunction_history WHERE company_id=@c AND eld_device_id=@id AND event_type='Malfunction Reported'",
                c => { c.Parameters.AddWithValue("@c", seed.CompanyId); c.Parameters.AddWithValue("@id", device); }));
            Assert.Equal(1, await owner.ScalarLongAsync(
                "SELECT COUNT(*) FROM audit_logs WHERE company_id=@c AND action_name='eld.malfunction' AND entity_name='EldDevice' AND entity_id=@entityId",
                c => { c.Parameters.AddWithValue("@c", seed.CompanyId); c.Parameters.AddWithValue("@entityId", device); }));
        }
        finally { await Cleanup(owner, seed.CompanyId); }
    }

    [Fact]
    public async Task DiagnosticHoldResolveRequiresStructuredRepairEvidenceAndClosesFaultBeforeRelease()
    {
        var db = Db(); var seed = await Seed(db);
        try
        {
            var fault = await db.InsertAsync(
                @"INSERT INTO fault_codes(company_id,branch_id,device_id,vehicle_id,code_type,protocol,code,severity,status,
                     canonical_identity,last_observed_at,last_source_event_id,first_seen_at,last_seen_at)
                  VALUES(@c,@b,'diag-hold-test',@v,'J1939','J1939','SPN-123-FMI-4','Critical','active',
                     'J1939:ENGINE:SPN:123:FMI:4',NOW(),'hold-source',NOW(),NOW())",
                c => { c.Parameters.AddWithValue("@c", seed.CompanyId); c.Parameters.AddWithValue("@b", seed.BranchA); c.Parameters.AddWithValue("@v", seed.VehicleA); });
            var hold = await db.InsertAsync(
                @"INSERT INTO diagnostic_holds(company_id,branch_id,vehicle_id,device_id,fault_code_id,canonical_dtc,
                     severity,status,out_of_service,source,source_event_id,reason,first_observed_at,last_observed_at)
                  VALUES(@c,@b,@v,'diag-hold-test',@f,'J1939:ENGINE:SPN:123:FMI:4','Critical','active',true,
                     'machine_diagnostic','hold-source','Engine shutdown lamp',NOW(),NOW())",
                c => { c.Parameters.AddWithValue("@c", seed.CompanyId); c.Parameters.AddWithValue("@b", seed.BranchA); c.Parameters.AddWithValue("@v", seed.VehicleA); c.Parameters.AddWithValue("@f", fault); });
            await db.ExecuteAsync("UPDATE fault_codes SET diagnostic_hold_id=@h WHERE id=@f; UPDATE vehicles SET out_of_service=true,availability_status='out_of_service' WHERE id=@v",
                c => { c.Parameters.AddWithValue("@h", hold); c.Parameters.AddWithValue("@f", fault); c.Parameters.AddWithValue("@v", seed.VehicleA); });

            var http = Principal(seed.CompanyId, seed.BranchA, "maintenance:update");
            Assert.Equal(StatusCodes.Status400BadRequest, Status(await Invoke("DiagnosticHoldResolve", http, hold,
                new Dictionary<string, object?> { ["resolutionNote"] = "Repaired" }, db, new AuditService(db), CancellationToken.None)));
            var evidence = new Dictionary<string, object?>
            {
                ["resolutionNote"] = "Harness repaired and verified with an inactive-code scan",
                ["verificationType"] = "technician_scan",
                ["evidenceReference"] = "scan-report:TEST-123"
            };
            Assert.Equal(StatusCodes.Status200OK, Status(await Invoke("DiagnosticHoldResolve", http, hold, evidence, db, new AuditService(db), CancellationToken.None)));

            var resolved = await db.QuerySingleAsync(
                @"SELECT dh.status,dh.resolution_evidence_type,dh.resolution_evidence_reference,dh.verified_at,
                         fc.status fault_status,fc.clear_source,v.out_of_service
                    FROM diagnostic_holds dh JOIN fault_codes fc ON fc.id=dh.fault_code_id
                    JOIN vehicles v ON v.id=dh.vehicle_id WHERE dh.id=@h",
                c => c.Parameters.AddWithValue("@h", hold));
            Assert.Equal("resolved", resolved!["status"]);
            Assert.Equal("technician_scan", resolved["resolutionEvidenceType"]);
            Assert.Equal("scan-report:TEST-123", resolved["resolutionEvidenceReference"]);
            Assert.NotNull(resolved["verifiedAt"]);
            Assert.Equal("resolved", resolved["faultStatus"]);
            Assert.Equal("verified_maintenance", resolved["clearSource"]);
            Assert.False(Convert.ToBoolean(resolved["outOfService"]));
        }
        finally { await Cleanup(db, seed.CompanyId); }
    }

    [Fact]
    public async Task EldReadModelsNeverExposeProvisioningSecrets()
    {
        var db = Db(); var seed = await Seed(db);
        try
        {
            var device = await Device(db, seed.CompanyId, seed.BranchA, seed.VehicleA, "SECRET");
            await db.ExecuteAsync("UPDATE eld_devices SET api_key_hash=@hash,hmac_secret=NULL,hmac_secret_encrypted=@secret WHERE id=@id",
                c => { c.Parameters.AddWithValue("@hash", new string('f', 64)); c.Parameters.AddWithValue("@secret", "never-return-this-hmac-secret-value"); c.Parameters.AddWithValue("@id", device); });
            foreach (var result in new[]
            {
                await Invoke("EldDevicesPilot", Principal(seed.CompanyId, seed.BranchA, "compliance:view"), db, CancellationToken.None),
                await Invoke("EldDevicePilot", Principal(seed.CompanyId, seed.BranchA, "compliance:view"), device, db, CancellationToken.None)
            })
            {
                var json = JsonSerializer.Serialize(Assert.IsAssignableFrom<IValueHttpResult>(result).Value);
                Assert.DoesNotContain("apiKeyHash", json, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("hmacSecret", json, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("never-return-this-hmac-secret-value", json, StringComparison.Ordinal);
            }
        }
        finally { await Cleanup(db, seed.CompanyId); }
    }

    [Fact]
    public async Task BackOfficeUserCannotImpersonateDriverDvirSignature()
    {
        var db = Db();
        var seed = await Seed(db);
        try
        {
            var report = await db.InsertAsync(
                @"INSERT INTO dvir_reports(company_id,branch_id,report_number,driver_id,vehicle_id,inspection_type)
                  VALUES(@c,@b,@n,@d,@v,'Post-Trip')",
                c => { c.Parameters.AddWithValue("@c", seed.CompanyId); c.Parameters.AddWithValue("@b", seed.BranchA); c.Parameters.AddWithValue("@n", $"DVIR-SIGN-{seed.CompanyId}"); c.Parameters.AddWithValue("@d", seed.DriverA); c.Parameters.AddWithValue("@v", seed.VehicleA); });
            var result = await Invoke("DvirDriverSignPilot", Principal(seed.CompanyId, seed.BranchA, "maintenance:update"), report, DvirAttestation(1), db, new AuditService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status403Forbidden, Status(result));
            Assert.Equal(StatusCodes.Status400BadRequest, Status(await Invoke("DvirDriverSignPilot", DriverPrincipal(seed.CompanyId, seed.BranchA), report, Version(1), db, new AuditService(db), CancellationToken.None)));
            Assert.Equal(StatusCodes.Status200OK, Status(await Invoke("DvirDriverSignPilot", DriverPrincipal(seed.CompanyId, seed.BranchA), report, DvirAttestation(1), db, new AuditService(db), CancellationToken.None)));
            var signed = await db.QuerySingleAsync("SELECT driver_signature_status,signature_attestation_text,signature_hash,signed_at,signed_by FROM dvir_reports WHERE id=@id", c => c.Parameters.AddWithValue("@id", report));
            Assert.Equal("Signed", signed!["driverSignatureStatus"]);
            Assert.Equal(DvirAttestationText, signed["signatureAttestationText"]);
            Assert.Equal(64, signed["signatureHash"]?.ToString()?.Length);
            Assert.NotNull(signed["signedAt"]);
            Assert.Equal(DriverUserId(seed.CompanyId), Convert.ToInt64(signed["signedBy"]));
        }
        finally { await Cleanup(db, seed.CompanyId); }
    }

    [Fact]
    public async Task SubmittedDvirCannotBeEditedOrArchivedAndArchiveRequiresRowVersion()
    {
        var db = Db(); var seed = await Seed(db);
        try
        {
            var report = await db.InsertAsync(
                @"INSERT INTO dvir_reports(company_id,branch_id,report_number,driver_id,vehicle_id,inspection_type,inspection_status)
                  VALUES(@c,@b,@n,@d,@v,'Pre-Trip','Submitted')",
                c => { c.Parameters.AddWithValue("@c", seed.CompanyId); c.Parameters.AddWithValue("@b", seed.BranchA); c.Parameters.AddWithValue("@n", $"DVIR-IMMUTABLE-{seed.CompanyId}"); c.Parameters.AddWithValue("@d", seed.DriverA); c.Parameters.AddWithValue("@v", seed.VehicleA); });
            var http = Principal(seed.CompanyId, seed.BranchA, "maintenance:update");
            Assert.Equal(StatusCodes.Status400BadRequest, Status(await Invoke("DeleteDvirReportPilot", http, report, db, new AuditService(db), CancellationToken.None)));
            http.Request.QueryString = new QueryString("?rowVersion=1");
            Assert.Equal(StatusCodes.Status409Conflict, Status(await Invoke("DeleteDvirReportPilot", http, report, db, new AuditService(db), CancellationToken.None)));
            Assert.Equal(StatusCodes.Status409Conflict, Status(await Invoke("UpdateDvirReportPilot", http, report,
                new Dictionary<string, object?> { ["rowVersion"] = 1, ["notes"] = "attempted rewrite" }, db, new AuditService(db), CancellationToken.None)));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM dvir_reports WHERE id=@id AND deleted_at IS NULL AND notes IS NULL", c => c.Parameters.AddWithValue("@id", report)));
        }
        finally { await Cleanup(db, seed.CompanyId); }
    }

    [Fact]
    public async Task LegacyMaintenanceDvirReadsAndDefectAcknowledgmentEnforceBranchAndCas()
    {
        var db = Db(); var seed = await Seed(db);
        try
        {
            var reportA = await db.InsertAsync(
                @"INSERT INTO dvir_reports(company_id,branch_id,report_number,driver_id,vehicle_id,inspection_type)
                  VALUES(@c,@b,@n,@d,@v,'Pre-Trip')",
                c => { c.Parameters.AddWithValue("@c", seed.CompanyId); c.Parameters.AddWithValue("@b", seed.BranchA); c.Parameters.AddWithValue("@n", $"LEGACY-A-{seed.CompanyId}"); c.Parameters.AddWithValue("@d", seed.DriverA); c.Parameters.AddWithValue("@v", seed.VehicleA); });
            var reportB = await db.InsertAsync(
                @"INSERT INTO dvir_reports(company_id,branch_id,report_number,driver_id,vehicle_id,inspection_type)
                  VALUES(@c,@b,@n,@d,@v,'Post-Trip')",
                c => { c.Parameters.AddWithValue("@c", seed.CompanyId); c.Parameters.AddWithValue("@b", seed.BranchB); c.Parameters.AddWithValue("@n", $"LEGACY-B-{seed.CompanyId}"); c.Parameters.AddWithValue("@d", seed.DriverB); c.Parameters.AddWithValue("@v", seed.VehicleB); });
            var defectA = await db.InsertAsync(
                @"INSERT INTO dvir_defects(company_id,branch_id,dvir_report_id,vehicle_id,driver_id,defect_category,defect_description,severity,status)
                  VALUES(@c,@b,@r,@v,@d,'Lights','Lamp','Low','Open')",
                c => { c.Parameters.AddWithValue("@c", seed.CompanyId); c.Parameters.AddWithValue("@b", seed.BranchA); c.Parameters.AddWithValue("@r", reportA); c.Parameters.AddWithValue("@v", seed.VehicleA); c.Parameters.AddWithValue("@d", seed.DriverA); });
            var defectB = await db.InsertAsync(
                @"INSERT INTO dvir_defects(company_id,branch_id,dvir_report_id,vehicle_id,driver_id,defect_category,defect_description,severity,status)
                  VALUES(@c,@b,@r,@v,@d,'Brakes','Brake','Critical','Open')",
                c => { c.Parameters.AddWithValue("@c", seed.CompanyId); c.Parameters.AddWithValue("@b", seed.BranchB); c.Parameters.AddWithValue("@r", reportB); c.Parameters.AddWithValue("@v", seed.VehicleB); c.Parameters.AddWithValue("@d", seed.DriverB); });
            var http = Principal(seed.CompanyId, seed.BranchA, "maintenance:view", "maintenance:update");
            var list = await Invoke("MaintInspectionsList", http, db, CancellationToken.None);
            var listJson = JsonSerializer.Serialize(Assert.IsAssignableFrom<IValueHttpResult>(list).Value);
            Assert.Contains($"LEGACY-A-{seed.CompanyId}", listJson, StringComparison.Ordinal);
            Assert.DoesNotContain($"LEGACY-B-{seed.CompanyId}", listJson, StringComparison.Ordinal);
            Assert.Equal(StatusCodes.Status404NotFound, Status(await Invoke("MaintInspectionDetail", reportB, http, db, CancellationToken.None)));
            var defects = await Invoke("MaintDefectsList", http, db, CancellationToken.None);
            var defectsJson = JsonSerializer.Serialize(Assert.IsAssignableFrom<IValueHttpResult>(defects).Value);
            Assert.Contains($"LEGACY-A-{seed.CompanyId}", defectsJson, StringComparison.Ordinal);
            Assert.DoesNotContain($"LEGACY-B-{seed.CompanyId}", defectsJson, StringComparison.Ordinal);
            Assert.Equal(StatusCodes.Status409Conflict, Status(await Invoke("MaintDefectAcknowledge", defectB, http, Version(1), db, new AuditService(db), CancellationToken.None)));
            Assert.Equal(StatusCodes.Status409Conflict, Status(await Invoke("MaintDefectAcknowledge", defectA, http, Version(2), db, new AuditService(db), CancellationToken.None)));
            Assert.Equal(StatusCodes.Status200OK, Status(await Invoke("MaintDefectAcknowledge", defectA, http, Version(1), db, new AuditService(db), CancellationToken.None)));
            var acknowledged = await db.QuerySingleAsync("SELECT status,row_version FROM dvir_defects WHERE id=@id", c => c.Parameters.AddWithValue("@id", defectA));
            Assert.Equal("Acknowledged", acknowledged!["status"]);
            Assert.Equal(2L, Convert.ToInt64(acknowledged["rowVersion"]));
        }
        finally { await Cleanup(db, seed.CompanyId); }
    }

    private static async Task<IResult> Invoke(string method, params object[] args)
    {
        var target = typeof(EndpointMappings).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!;
        try { return await (Task<IResult>)target.Invoke(null, args)!; }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }

    private static int Status(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode ?? 200;

    private static Dictionary<string, object?> Version(long rowVersion) => new() { ["rowVersion"] = rowVersion };
    private static Dictionary<string, object?> HosAttestation() => new()
    { ["attestationAccepted"] = true, ["attestation"] = HosAttestationText };
    private static Dictionary<string, object?> RepairAttestation(long rowVersion) => new()
    { ["rowVersion"] = rowVersion, ["attestationAccepted"] = true, ["attestation"] = RepairAttestationText };
    private static Dictionary<string, object?> DvirAttestation(long rowVersion) => new()
    { ["rowVersion"] = rowVersion, ["attestationAccepted"] = true, ["attestation"] = DvirAttestationText };

    private static DefaultHttpContext DriverPrincipal(long company, long? branch)
    {
        var http = Principal(company, branch, "driver:self");
        http.Items[EndpointMappings.AuthUserIdItemKey] = DriverUserId(company);
        http.Items[EndpointMappings.AuthRoleItemKey] = "Driver";
        return http;
    }

    private static long DriverUserId(long company) => checked(company * 10 + 1);

    private static DefaultHttpContext Principal(long company, long? branch, params string[] permissions)
    {
        var http = new DefaultHttpContext();
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = company;
        if (branch.HasValue) http.Items[EndpointMappings.AuthBranchIdItemKey] = branch.Value;
        http.Items[EndpointMappings.AuthUserIdItemKey] = 42L;
        http.Items[EndpointMappings.AuthRoleItemKey] = "Tenant Admin";
        http.Items[EndpointMappings.AuthPermissionsItemKey] = permissions;
        return http;
    }

    private static Database Db(string? applicationName = null)
    {
        var connection = new NpgsqlConnectionStringBuilder(TestDb.ConnectionString);
        if (!string.IsNullOrWhiteSpace(applicationName)) connection.ApplicationName = applicationName;
        return new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:DefaultConnection"] = connection.ConnectionString, ["Rls:EnforceTenantContext"] = "false" }).Build());
    }

    private static Database RuntimeDb() => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:DefaultConnection"] = TestDb.AppConnectionString,
        ["ConnectionStrings:SystemConnection"] = TestDb.SystemConnectionString,
        ["Rls:EnforceTenantContext"] = "true",
        ["Rls:TenantTicketTtlSeconds"] = "120",
        ["ASPNETCORE_ENVIRONMENT"] = "Production"
    }).Build());

    private static async Task SetLocal(NpgsqlConnection connection, NpgsqlTransaction transaction, string key, string value)
    {
        await using var command = new NpgsqlCommand("SELECT set_config(@key,@value,true)", connection, transaction);
        command.Parameters.AddWithValue("@key", key);
        command.Parameters.AddWithValue("@value", value);
        await command.ExecuteScalarAsync();
    }

    private static async Task WaitForAdvisoryWaiter(Database db, string applicationName)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var count = await db.ScalarLongAsync(
                @"SELECT COUNT(*) FROM pg_stat_activity WHERE datname=current_database()
                    AND application_name=@applicationName AND wait_event_type='Lock' AND wait_event='advisory'",
                c => c.Parameters.AddWithValue("@applicationName", applicationName));
            if (count == 1) return;
            await Task.Delay(20);
        }
        throw new TimeoutException($"Expected PostgreSQL advisory lock waiter for {applicationName}.");
    }

    private static async Task<PilotSeed> Seed(Database db)
    {
        var company = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(1_000_000, 9_000_000);
        const long branchA = 66101;
        const long branchB = 66102;
        await db.ExecuteAsync(
            "INSERT INTO companies(id,company_code,name,industry) OVERRIDING SYSTEM VALUE VALUES(@id,@code,'DVIR HOS Pilot Test','Transportation')",
            c => { c.Parameters.AddWithValue("@id", company); c.Parameters.AddWithValue("@code", $"DH-{company}"); });
        var driverA = await Driver(db, company, branchA, "A", DriverUserId(company));
        var driverB = await Driver(db, company, branchB, "B", DriverUserId(company) + 1);
        var vehicleA = await Vehicle(db, company, branchA, "A");
        var vehicleB = await Vehicle(db, company, branchB, "B");
        return new(company, branchA, branchB, driverA, driverB, vehicleA, vehicleB);
    }

    private static Task<long> Driver(Database db, long company, long branch, string suffix, long userId) => db.InsertAsync(
        "INSERT INTO drivers(company_id,branch_id,driver_code,full_name,status,user_id) VALUES(@c,@b,@code,@name,'Available',@uid)",
        c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@b", branch); c.Parameters.AddWithValue("@code", $"DH-D-{suffix}-{company}"); c.Parameters.AddWithValue("@name", $"DVIR/HOS Driver {suffix}"); c.Parameters.AddWithValue("@uid", userId); });

    private static Task<long> Vehicle(Database db, long company, long branch, string suffix) => db.InsertAsync(
        "INSERT INTO vehicles(company_id,branch_id,vehicle_code,type,vin_exception_type,alternate_identifier,status) VALUES(@c,@b,@code,'Truck','legacy-fleet-identifier',@code,'Available')",
        c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@b", branch); c.Parameters.AddWithValue("@code", $"DH-V-{suffix}-{company}"); });

    private static Task InsertClock(Database db, long company, long branch, long driver, string status, int remaining) => db.ExecuteAsync(
        @"INSERT INTO hos_clocks(company_id,branch_id,driver_id,status,drive_time_remaining_minutes,
            shift_time_remaining_minutes,cycle_time_remaining_minutes)
          VALUES(@c,@b,@d,@s,@r,@r,@r)",
        c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@b", branch); c.Parameters.AddWithValue("@d", driver); c.Parameters.AddWithValue("@s", status); c.Parameters.AddWithValue("@r", remaining); });

    private static Task<long> InsertHosLog(Database db, long company, long branch, long driver,
        DateTimeOffset start, DateTimeOffset? end, string status, string sourceEvent) => db.InsertAsync(
        @"INSERT INTO hos_logs(company_id,branch_id,driver_id,log_date,driving_hours,on_duty_hours,cycle_hours_left,
            status,start_time,end_time,duration_minutes,source,source_event_id)
          VALUES(@c,@b,@d,@date,0,0,60,@status,@start,@end,@minutes,'manual',@event)",
        c =>
        {
            c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@b", branch); c.Parameters.AddWithValue("@d", driver);
            c.Parameters.AddWithValue("@date", start.UtcDateTime.Date); c.Parameters.AddWithValue("@status", status);
            c.Parameters.AddWithValue("@start", start); c.Parameters.AddWithValue("@end", (object?)end ?? DBNull.Value);
            c.Parameters.AddWithValue("@minutes", end.HasValue ? Math.Max(0, (int)(end.Value - start).TotalMinutes) : 0);
            c.Parameters.AddWithValue("@event", $"{sourceEvent}-{company}");
        });

    private static Task<long> InsertDeletedHosLog(Database db, long company, long branch, long driver,
        DateTimeOffset start, DateTimeOffset end, string status, string sourceEvent) => db.InsertAsync(
        @"INSERT INTO hos_logs(company_id,branch_id,driver_id,log_date,driving_hours,on_duty_hours,cycle_hours_left,
            status,start_time,end_time,duration_minutes,source,source_event_id,deleted_at)
          VALUES(@c,@b,@d,@date,0,0,60,@status,@start,@end,@minutes,'manual',@event,NOW())",
        c =>
        {
            c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@b", branch); c.Parameters.AddWithValue("@d", driver);
            c.Parameters.AddWithValue("@date", start.UtcDateTime.Date); c.Parameters.AddWithValue("@status", status);
            c.Parameters.AddWithValue("@start", start); c.Parameters.AddWithValue("@end", end);
            c.Parameters.AddWithValue("@minutes", Math.Max(0, (int)(end - start).TotalMinutes));
            c.Parameters.AddWithValue("@event", $"{sourceEvent}-{company}");
        });

    private static Task<long> Device(Database db, long company, long branch, long vehicle, string suffix) => db.InsertAsync(
        @"INSERT INTO eld_devices(company_id,branch_id,device_serial,vehicle_id,status)
          VALUES(@c,@b,@serial,@vehicle,'Diagnostic')",
        c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@b", branch); c.Parameters.AddWithValue("@serial", $"DH-ELD-{suffix}-{company}"); c.Parameters.AddWithValue("@vehicle", vehicle); });

    private static async Task Cleanup(Database db, long company)
    {
        foreach (var sql in new[]
        {
            "DELETE FROM audit_logs WHERE company_id=@c", "DELETE FROM dvir_defects WHERE company_id=@c",
            "DELETE FROM ai_recommendations WHERE company_id=@c",
            "DELETE FROM dvir_reports WHERE company_id=@c", "UPDATE hos_logs SET location=COALESCE(location,'') || '-cleanup' WHERE company_id=@c AND is_certified", "DELETE FROM hos_logs WHERE company_id=@c",
            "DELETE FROM hos_clocks WHERE company_id=@c", "DELETE FROM eld_malfunction_history WHERE company_id=@c",
            "DELETE FROM diagnostic_holds WHERE company_id=@c", "DELETE FROM fault_occurrences WHERE company_id=@c", "DELETE FROM fault_codes WHERE company_id=@c",
            "DELETE FROM eld_devices WHERE company_id=@c",
            "DELETE FROM vehicles WHERE company_id=@c", "DELETE FROM drivers WHERE company_id=@c",
            "DELETE FROM companies WHERE id=@c"
        }) await db.ExecuteAsync(sql, c => c.Parameters.AddWithValue("@c", company));
    }

    private sealed record PilotSeed(long CompanyId, long BranchA, long BranchB, long DriverA, long DriverB, long VehicleA, long VehicleB);
}
