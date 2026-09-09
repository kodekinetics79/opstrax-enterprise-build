using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Opstrax.Api.Data;
using Opstrax.Api.Security;
using Opstrax.Api.Services;
using Xunit.Sdk;

namespace Opstrax.Tests;

[Trait("Category", "DeviceConnectivityObservationPostgres")]
[Trait("Lane", "DedicatedDatabase")]
public sealed class DeviceConnectivityObservationPostgresTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);
    [Fact]
    public async Task AppRoleReadsSafeProjectionButCannotMutateOrReadProviderIdentityHashes()
    {
        var db = Db();
        Assert.Equal(1, await db.ScalarLongAsync(
            "SELECT CASE WHEN has_column_privilege('opstrax_app','device_connectivity_observations','subscription_status','SELECT') THEN 1 ELSE 0 END"));
        foreach (var privilege in new[] { "INSERT", "UPDATE", "DELETE" })
            Assert.Equal(0, await db.ScalarLongAsync(
                "SELECT CASE WHEN has_table_privilege('opstrax_app','device_connectivity_observations',@privilege) THEN 1 ELSE 0 END",
                command => command.Parameters.AddWithValue("@privilege", privilege)));
        foreach (var column in new[] { "profile_iccid_bidx_snapshot", "source_account_bidx", "source_observation_bidx", "payload_sha256" })
            Assert.Equal(0, await db.ScalarLongAsync(
                "SELECT CASE WHEN has_column_privilege('opstrax_app','device_connectivity_observations',@column,'SELECT') THEN 1 ELSE 0 END",
                command => command.Parameters.AddWithValue("@column", column)));
    }

    [Fact]
    public async Task ExactCurrentProfileAdmitsReplayAndRejectsIdentityDrift()
    {
        var db = Db();
        var pii = new PiiProtectionService(new TestKeyProvider(), NullLogger<PiiProtectionService>.Instance);
        var service = new DeviceConnectivityObservationService(
            db, pii, new FixedTimeProvider(Now), NullLogger<DeviceConnectivityObservationService>.Instance);
        var suffix = Guid.NewGuid().ToString("N");
        var iccid = UniqueIccid();
        var companyId = await db.InsertAsync(
            "INSERT INTO companies(company_code,name,industry) VALUES(@code,'Connectivity observation test','Transportation')",
            command => command.Parameters.AddWithValue("@code", $"COT-{suffix[..10]}"));
        var deviceId = await db.InsertAsync(
            "INSERT INTO eld_devices(company_id,device_serial,status,device_state) VALUES(@company,@serial,'Provisioning','Registered')",
            command => { command.Parameters.AddWithValue("@company", companyId); command.Parameters.AddWithValue("@serial", $"COT-{suffix}"); });
        var envelope = Envelope($"observation-{suffix}", iccid);
        var payload = Encoding.UTF8.GetBytes("{\"subscription\":\"active\"}");

        await Assert.ThrowsAsync<DeviceConnectivityObservationValidationException>(() =>
            service.RecordAuthenticatedAsync(companyId, envelope, payload));

        var profileId = await db.InsertAsync(
            @"INSERT INTO device_connectivity_profiles
                (company_id,branch_id,device_id,profile_kind,carrier_name,iccid_encrypted,iccid_bidx,
                 iccid_last4,apn_configured,assignment_status,effective_from,source_reference,
                 change_reason,idempotency_key,created_by)
              VALUES(@company,NULL,@device,'PhysicalSIM','Carrier API',@encrypted,@bidx,'0720',FALSE,
                     'Assigned',@effective,'inventory-reference','Initial SIM assignment',@key,1)",
            command =>
            {
                command.Parameters.AddWithValue("@company", companyId);
                command.Parameters.AddWithValue("@device", deviceId);
                command.Parameters.AddWithValue("@encrypted", pii.Encrypt(iccid)!);
                command.Parameters.AddWithValue("@bidx", pii.BlindIndex(iccid)!);
                command.Parameters.AddWithValue("@effective", Now.AddDays(-1));
                command.Parameters.AddWithValue("@key", Guid.NewGuid());
            });

        var accepted = await service.RecordAuthenticatedAsync(companyId, envelope, payload);
        Assert.Equal(DeviceConnectivityObservationDisposition.Accepted, accepted.Disposition);
        Assert.Equal(deviceId, accepted.DeviceId);
        Assert.Equal(profileId, accepted.ConnectivityProfileId);
        Assert.False(accepted.ProviderVerifiedClaim);
        Assert.False(accepted.PhysicalConnectivityClaim);
        Assert.False(accepted.CertificationClaim);

        var replay = await service.RecordAuthenticatedAsync(companyId, envelope, payload);
        Assert.Equal(accepted.ObservationId, replay.ObservationId);
        Assert.Equal(DeviceConnectivityObservationDisposition.Replay, replay.Disposition);
        var caseDistinct = await service.RecordAuthenticatedAsync(companyId,
            envelope with { SourceObservationId = envelope.SourceObservationId.ToUpperInvariant() }, payload);
        Assert.Equal(DeviceConnectivityObservationDisposition.Accepted, caseDistinct.Disposition);
        Assert.NotEqual(accepted.ObservationId, caseDistinct.ObservationId);
        Assert.Equal(1, await db.ScalarLongAsync(
            "SELECT COUNT(*) FROM device_connectivity_observations WHERE company_id=@company AND id=@id",
            command => { command.Parameters.AddWithValue("@company", companyId); command.Parameters.AddWithValue("@id", accepted.ObservationId); }));

        await Assert.ThrowsAsync<DeviceConnectivityObservationValidationException>(() =>
            service.RecordAuthenticatedAsync(companyId,
                envelope with { SubscriptionStatus = "Suspended" }, payload));
        var stored = await db.QuerySingleAsync(
            @"SELECT source_account_bidx,source_observation_bidx,payload_sha256,
                     provider_verified_claim,physical_connectivity_claim,certification_claim
                FROM device_connectivity_observations WHERE id=@id",
            command => command.Parameters.AddWithValue("@id", accepted.ObservationId));
        Assert.NotNull(stored);
        Assert.DoesNotContain("account", stored!["sourceAccountBidx"]!.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("observation", stored["sourceObservationBidx"]!.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(false, stored["providerVerifiedClaim"]);
        Assert.Equal(false, stored["physicalConnectivityClaim"]);
        Assert.Equal(false, stored["certificationClaim"]);
    }

    [Fact]
    public async Task DatabaseRejectsCrossBranchProfileAndAllClaimPromotion()
    {
        var db = Db();
        var suffix = Guid.NewGuid().ToString("N");
        var companyId = await db.InsertAsync(
            "INSERT INTO companies(company_code,name,industry) VALUES(@code,'Connectivity database guard','Transportation')",
            command => command.Parameters.AddWithValue("@code", $"CDG-{suffix[..10]}"));
        var branchOne = await db.InsertAsync(
            "INSERT INTO branches(company_id,branch_code,name) VALUES(@company,@code,'Branch one')",
            command => { command.Parameters.AddWithValue("@company", companyId); command.Parameters.AddWithValue("@code", $"C1-{suffix}"); });
        var branchTwo = await db.InsertAsync(
            "INSERT INTO branches(company_id,branch_code,name) VALUES(@company,@code,'Branch two')",
            command => { command.Parameters.AddWithValue("@company", companyId); command.Parameters.AddWithValue("@code", $"C2-{suffix}"); });
        var deviceId = await db.InsertAsync(
            "INSERT INTO eld_devices(company_id,branch_id,device_serial,status,device_state) VALUES(@company,@branch,@serial,'Provisioning','Registered')",
            command => { command.Parameters.AddWithValue("@company", companyId); command.Parameters.AddWithValue("@branch", branchOne); command.Parameters.AddWithValue("@serial", $"CDG-{suffix}"); });
        var iccidHash = Hash($"{suffix}:profile");
        var profileId = await db.InsertAsync(
            @"INSERT INTO device_connectivity_profiles
                (company_id,branch_id,device_id,profile_kind,carrier_name,iccid_encrypted,iccid_bidx,
                 iccid_last4,apn_configured,assignment_status,effective_from,source_reference,
                 change_reason,idempotency_key,created_by)
              VALUES(@company,@branch,@device,'PhysicalSIM','Carrier API','enc:test',@hash,'1234',FALSE,
                     'Assigned',NOW()-INTERVAL '1 day','inventory-reference','Initial SIM assignment',@key,1)",
            command =>
            {
                command.Parameters.AddWithValue("@company", companyId);
                command.Parameters.AddWithValue("@branch", branchOne);
                command.Parameters.AddWithValue("@device", deviceId);
                command.Parameters.AddWithValue("@hash", iccidHash);
                command.Parameters.AddWithValue("@key", Guid.NewGuid());
            });

        Task<long> Insert(long branchId, string identityHash, bool providerClaim = false) => db.InsertAsync(
            @"INSERT INTO device_connectivity_observations
                (company_id,branch_id,device_id,connectivity_profile_id,profile_iccid_bidx_snapshot,
                 profile_iccid_last4,source_provider,source_account_bidx,source_observation_bidx,payload_sha256,
                 subscription_status,network_registration_status,data_session_status,observed_at,received_at,
                 provider_verified_claim)
              VALUES(@company,@branch,@device,@profile,@iccid,'1234','carrier-api',@identity,@identity,@identity,
                     'Active','Registered','Attached',NOW(),NOW(),@providerClaim)",
            command =>
            {
                command.Parameters.AddWithValue("@company", companyId);
                command.Parameters.AddWithValue("@branch", branchId);
                command.Parameters.AddWithValue("@device", deviceId);
                command.Parameters.AddWithValue("@profile", profileId);
                command.Parameters.AddWithValue("@iccid", iccidHash);
                command.Parameters.AddWithValue("@identity", identityHash);
                command.Parameters.AddWithValue("@providerClaim", providerClaim);
            });

        var crossBranch = await Assert.ThrowsAsync<PostgresException>(() =>
            Insert(branchTwo, Hash($"{suffix}:cross-branch")));
        Assert.Equal("ck_stage120_exact_current_profile", crossBranch.ConstraintName);

        var mismatchedDeviceId = await db.InsertAsync(
            "INSERT INTO eld_devices(company_id,branch_id,device_serial,status,device_state) VALUES(@company,@branch,@serial,'Provisioning','Registered')",
            command => { command.Parameters.AddWithValue("@company", companyId); command.Parameters.AddWithValue("@branch", branchOne); command.Parameters.AddWithValue("@serial", $"CDG-MISMATCH-{suffix}"); });
        var mismatchedProfileId = await db.InsertAsync(
            @"INSERT INTO device_connectivity_profiles
                (company_id,branch_id,device_id,profile_kind,carrier_name,iccid_encrypted,iccid_bidx,
                 iccid_last4,apn_configured,assignment_status,effective_from,source_reference,
                 change_reason,idempotency_key,created_by)
              VALUES(@company,@branch,@device,'PhysicalSIM','Carrier API','enc:test',@hash,'5678',FALSE,
                     'Assigned',NOW()-INTERVAL '1 day','inventory-reference','Mismatched branch fixture',@key,1)",
            command =>
            {
                command.Parameters.AddWithValue("@company", companyId);
                command.Parameters.AddWithValue("@branch", branchTwo);
                command.Parameters.AddWithValue("@device", mismatchedDeviceId);
                command.Parameters.AddWithValue("@hash", Hash($"{suffix}:mismatched-profile"));
                command.Parameters.AddWithValue("@key", Guid.NewGuid());
            });
        var mismatchedProfile = await Assert.ThrowsAsync<PostgresException>(() => db.InsertAsync(
            @"INSERT INTO device_connectivity_observations
                (company_id,branch_id,device_id,connectivity_profile_id,profile_iccid_bidx_snapshot,
                 profile_iccid_last4,source_provider,source_account_bidx,source_observation_bidx,payload_sha256,
                 subscription_status,network_registration_status,data_session_status,observed_at,received_at)
              VALUES(@company,@branch,@device,@profile,@iccid,'5678','carrier-api',@identity,@identity,@identity,
                     'Active','Registered','Attached',NOW(),NOW())",
            command =>
            {
                command.Parameters.AddWithValue("@company", companyId);
                command.Parameters.AddWithValue("@branch", branchTwo);
                command.Parameters.AddWithValue("@device", mismatchedDeviceId);
                command.Parameters.AddWithValue("@profile", mismatchedProfileId);
                command.Parameters.AddWithValue("@iccid", Hash($"{suffix}:mismatched-profile"));
                command.Parameters.AddWithValue("@identity", Hash($"{suffix}:mismatched-observation"));
            }));
        Assert.Equal("ck_stage120_exact_current_profile", mismatchedProfile.ConstraintName);

        var observationId = await Insert(branchOne, Hash($"{suffix}:valid-observation"));
        foreach (var statement in new[]
        {
            "UPDATE device_connectivity_observations SET subscription_status='Suspended' WHERE id=@id",
            "DELETE FROM device_connectivity_observations WHERE id=@id",
        })
        {
            var immutable = await Assert.ThrowsAsync<PostgresException>(() => db.ExecuteAsync(statement,
                command => command.Parameters.AddWithValue("@id", observationId)));
            Assert.Equal("ck_stage120_observation_immutable", immutable.ConstraintName);
        }

        var claim = await Assert.ThrowsAsync<PostgresException>(() =>
            Insert(branchOne, Hash($"{suffix}:claim-observation"), providerClaim: true));
        Assert.Equal(PostgresErrorCodes.CheckViolation, claim.SqlState);
        Assert.Equal("ck_stage120_no_provider_claim", claim.ConstraintName);
    }

    private static DeviceConnectivityObservationEnvelope Envelope(string id, string iccid) => new(
        "carrier-api", "account-boundary", id, iccid,
        "Active", "Registered", "Attached", 8192, false, Now.AddMinutes(-2));

    private static string UniqueIccid() =>
        $"89{Random.Shared.NextInt64(100000000000000000L, 999999999999999999L)}";

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static Database Db() => new(new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = GuardedConnection(),
            ["ConnectionStrings:SystemConnection"] = GuardedConnection(),
            ["Rls:EnforceTenantContext"] = "false",
        }).Build());

    private static string GuardedConnection()
    {
        var configured = Environment.GetEnvironmentVariable("OPSTRAX_DEVICEOPS_STAGE120_DB");
        if (string.IsNullOrWhiteSpace(configured))
            throw SkipException.ForSkip("Set OPSTRAX_DEVICEOPS_STAGE120_DB to the dedicated disposable connectivity-observation database.");
        var builder = new NpgsqlConnectionStringBuilder(configured);
        if (builder.Host is not ("127.0.0.1" or "::1") || builder.Port != 55445 ||
            builder.Database != "opstrax_deviceops_stage120")
            throw new InvalidOperationException("Connectivity observation tests require the dedicated local database on port 55445.");
        return builder.ConnectionString;
    }
}
