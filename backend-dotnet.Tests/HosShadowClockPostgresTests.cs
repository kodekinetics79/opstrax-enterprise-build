using Microsoft.Extensions.Configuration;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

[Trait("Category", "Integration")]
public sealed class HosShadowClockPostgresTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 9, 3, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset DayStart = new(2026, 9, 3, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ApprovedPolicyAndVerifiedProviderLineageCreateIdempotentTrustedShadowSnapshot()
    {
        var db = CreateDatabase();
        var (companyId, driverId) = await SeedDriverAsync(db, "Approved");
        try
        {
            await SeedContinuousCanadaHistoryAsync(db, companyId, driverId, verified: true);
            var service = new HosShadowClockService(db);

            var first = await service.CalculateDriverAsync(companyId, driverId, AsOf);
            var second = await service.CalculateDriverAsync(companyId, driverId, AsOf.AddSeconds(42));

            Assert.True(first.Persisted);
            Assert.True(first.SnapshotId > 0);
            Assert.Equal(first.SnapshotId, second.SnapshotId);
            Assert.Equal(first.SourceWatermark, second.SourceWatermark);
            Assert.Equal(HosComplianceCalculator.CanadaSouth60, first.RuleProfileCode);
            Assert.True(first.DataComplete);
            Assert.False(first.ReviewRequired);
            Assert.True(first.CanDrive);
            Assert.Equal("OK", first.Status);
            Assert.Empty(first.Violations);
            Assert.Empty(first.ReviewFlags);

            var count = await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM hos_shadow_clock_snapshots WHERE company_id=@c AND driver_id=@d",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@d", driverId); });
            Assert.Equal(1, count);
        }
        finally
        {
            await CleanupCompanyAsync(db, companyId);
        }
    }

    [Fact]
    public async Task AnyUnverifiedSourceEventForcesUnverifiedShadowAndNeverGrantsCanDrive()
    {
        var db = CreateDatabase();
        var (companyId, driverId) = await SeedDriverAsync(db, "Approved");
        try
        {
            await SeedContinuousCanadaHistoryAsync(db, companyId, driverId, verified: false);
            var service = new HosShadowClockService(db);

            var result = await service.CalculateDriverAsync(companyId, driverId, AsOf);

            Assert.True(result.Persisted);
            Assert.False(result.DataComplete);
            Assert.True(result.ReviewRequired);
            Assert.False(result.CanDrive);
            Assert.Equal("Unverified", result.Status);
            Assert.Contains("HOS_SOURCE_PROVENANCE_UNVERIFIED", result.ReviewFlags);

            var row = await db.QuerySingleAsync(
                @"SELECT data_complete,review_required,can_drive,status
                    FROM hos_shadow_clock_snapshots
                   WHERE id=@id",
                c => c.Parameters.AddWithValue("@id", result.SnapshotId));
            Assert.NotNull(row);
            Assert.False(Convert.ToBoolean(row!["dataComplete"]));
            Assert.True(Convert.ToBoolean(row["reviewRequired"]));
            Assert.False(Convert.ToBoolean(row["canDrive"]));
            Assert.Equal("Unverified", Convert.ToString(row["status"]));
        }
        finally
        {
            await CleanupCompanyAsync(db, companyId);
        }
    }

    [Fact]
    public async Task NonApprovedPolicyCanCalculateForReviewButCannotBecomeTrustedOrGrantDriving()
    {
        var db = CreateDatabase();
        var (companyId, driverId) = await SeedDriverAsync(db, "Reviewed");
        try
        {
            await SeedContinuousCanadaHistoryAsync(db, companyId, driverId, verified: true);
            var service = new HosShadowClockService(db);

            var result = await service.CalculateDriverAsync(companyId, driverId, AsOf);

            Assert.True(result.Persisted);
            Assert.False(result.DataComplete);
            Assert.True(result.ReviewRequired);
            Assert.False(result.CanDrive);
            Assert.Equal("Unverified", result.Status);
            Assert.Contains("HOS_POLICY_NOT_APPROVED", result.ReviewFlags);
        }
        finally
        {
            await CleanupCompanyAsync(db, companyId);
        }
    }

    [Fact]
    public async Task MissingPolicyFailsClosedAndDoesNotCreateSnapshot()
    {
        var db = CreateDatabase();
        var companyId = await db.InsertAsync(
            "INSERT INTO companies(company_code,name,industry) VALUES(@code,'HOS Missing Policy','logistics') RETURNING id",
            c => c.Parameters.AddWithValue("@code", $"HMP-{Guid.NewGuid():N}"[..18]));
        var driverId = await db.InsertAsync(
            "INSERT INTO drivers(company_id,driver_code,full_name,status) VALUES(@c,@code,'Missing Policy Driver','Available') RETURNING id",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@code", $"D-{Guid.NewGuid():N}"[..14]); });
        try
        {
            var service = new HosShadowClockService(db);
            var result = await service.CalculateDriverAsync(companyId, driverId, AsOf);

            Assert.False(result.Persisted);
            Assert.Equal(0, result.SnapshotId);
            Assert.Equal("Unverified", result.Status);
            Assert.False(result.CanDrive);
            Assert.Contains("HOS_POLICY_ASSIGNMENT_MISSING", result.ReviewFlags);
        }
        finally
        {
            await CleanupCompanyAsync(db, companyId);
        }
    }

    private static async Task<(long companyId, long driverId)> SeedDriverAsync(Database db, string reviewStatus)
    {
        var companyId = await db.InsertAsync(
            "INSERT INTO companies(company_code,name,industry) VALUES(@code,'HOS Shadow Co','logistics') RETURNING id",
            c => c.Parameters.AddWithValue("@code", $"HOS-{Guid.NewGuid():N}"[..18]));
        var driverId = await db.InsertAsync(
            "INSERT INTO drivers(company_id,driver_code,full_name,status) VALUES(@c,@code,'Canada Shadow Driver','Available') RETURNING id",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@code", $"D-{Guid.NewGuid():N}"[..14]); });

        await db.ExecuteAsync(
            @"INSERT INTO driver_hos_policy_assignments
                (company_id,driver_id,jurisdiction_code,rule_profile_code,cycle_type,timezone,
                 day_start_local,effective_from,source,source_reference,review_status,reviewed_at)
              VALUES
                (@c,@d,'CA',@profile,'Cycle 1','UTC','00:00',@from,'test','postgres-shadow-test',@review,NOW())",
            c =>
            {
                c.Parameters.AddWithValue("@c", companyId);
                c.Parameters.AddWithValue("@d", driverId);
                c.Parameters.AddWithValue("@profile", HosComplianceCalculator.CanadaSouth60);
                c.Parameters.AddWithValue("@from", AsOf.AddDays(-30).UtcDateTime);
                c.Parameters.AddWithValue("@review", reviewStatus);
            });
        return (companyId, driverId);
    }

    private static async Task SeedContinuousCanadaHistoryAsync(Database db, long companyId, long driverId, bool verified)
    {
        var coverageStart = DayStart.AddDays(-15);
        await InsertLogAsync(db, companyId, driverId, coverageStart, DayStart, "Off Duty", "history", true);
        await InsertLogAsync(db, companyId, driverId, DayStart, DayStart.AddHours(4), "Driving", "drive", verified);
        await InsertLogAsync(db, companyId, driverId, DayStart.AddHours(4), AsOf, "Off Duty", "rest", true);
    }

    private static async Task InsertLogAsync(
        Database db,
        long companyId,
        long driverId,
        DateTimeOffset start,
        DateTimeOffset end,
        string status,
        string suffix,
        bool verified)
    {
        var eventId = $"hos-shadow-{suffix}-{Guid.NewGuid():N}";
        var minutes = (int)(end - start).TotalMinutes;
        await db.ExecuteAsync(
            @"INSERT INTO hos_logs
                (company_id,driver_id,log_date,country_code,status,start_time,end_time,duration_minutes,
                 driving_hours,on_duty_hours,source,source_event_id,source_provider,source_device_identifier,
                 source_received_at,source_payload_sha256,source_sequence,provenance_verified)
              VALUES
                (@c,@d,@date,'CA',@status,@start,@end,@minutes,@driving,@duty,'provider',@event,
                 'Motive','LBB-test',@received,@sha,@seq,@verified)",
            c =>
            {
                c.Parameters.AddWithValue("@c", companyId);
                c.Parameters.AddWithValue("@d", driverId);
                c.Parameters.AddWithValue("@date", DateOnly.FromDateTime(start.UtcDateTime));
                c.Parameters.AddWithValue("@status", status);
                c.Parameters.AddWithValue("@start", start.UtcDateTime);
                c.Parameters.AddWithValue("@end", end.UtcDateTime);
                c.Parameters.AddWithValue("@minutes", minutes);
                c.Parameters.AddWithValue("@driving", status == "Driving" ? minutes / 60m : 0m);
                c.Parameters.AddWithValue("@duty", status is "Driving" or "On Duty (Not Driving)" ? minutes / 60m : 0m);
                c.Parameters.AddWithValue("@event", eventId);
                c.Parameters.AddWithValue("@received", end.AddMinutes(1).UtcDateTime);
                c.Parameters.AddWithValue("@sha", new string('a', 64));
                c.Parameters.AddWithValue("@seq", eventId);
                c.Parameters.AddWithValue("@verified", verified);
            });
    }

    private static async Task CleanupCompanyAsync(Database db, long companyId)
    {
        // Stage103 permits controlled database-owner purge while runtime roles stay
        // append-only. This cleanup also exercises the retention/offboarding path.
        await db.ExecuteAsync("DELETE FROM companies WHERE id=@c", c => c.Parameters.AddWithValue("@c", companyId));
    }

    private static Database CreateDatabase() =>
        new(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString }).Build());
}