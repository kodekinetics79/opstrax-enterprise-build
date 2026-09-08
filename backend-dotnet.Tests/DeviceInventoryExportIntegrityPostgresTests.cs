using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Xunit.Sdk;

namespace Opstrax.Tests;

[Trait("Category", "DeviceInventoryExportIntegrityPostgres")]
public sealed class DeviceInventoryExportIntegrityPostgresTests
{
    private static readonly string[] ExpectedColumns =
    [
        "deviceSerial", "imei", "deviceCategory", "manufacturer", "deviceModel",
        "hardwareRevision", "provider", "firmwareVersion", "status", "deviceState",
        "branchCode", "vehicleCode", "driverName", "currentInstallationStatus",
        "currentInstallationRole", "currentInstallationPrimary", "exactDeviceTupleComplete",
        "currentInstallationRecorded", "currentConnectivityProfileRecorded",
        "currentTelemetryObserved", "softwareLifecycleClear", "openRmaCount",
        "highestOpenRmaSeverity", "nextSupportResponseDueAt", "sparePoolName",
        "sparePoolState", "spareInventoryAssuranceStatus", "sparePhysicalPossessionClaim",
        "spareConditionVerifiedClaim", "spareCompatibilityClaim", "spareCertificationClaim",
        "supportAssignmentState", "supportTier", "supportCoverageWindow",
        "supportRoutingResponseTargetMinutes", "supportRecordStatus",
        "supportCommercialEntitlementVerifiedClaim", "supportProviderClaim",
        "supportHardwareSupportabilityClaim", "supportCertificationClaim",
        "compatibilityRegistryStatus", "compatibilityCandidateSha",
        "capabilityDeclarationStatus", "compatibilityProtocols",
        "compatibilitySupportedFields", "compatibilitySupportedEvents",
        "compatibilitySupportedCommands", "compatibilityKnownLimitations",
        "compatibilityCatalogSupportTier", "compatibilityCertificationReference",
        "compatibilityCertificationDate", "compatibilityPhysicalEvidenceClaim",
        "compatibilityProviderEvidenceClaim", "compatibilityCertificationClaim",
        "deviceOpsGapCount", "lastSeenAt", "revokedAt", "retiredAt", "createdAt",
        "evidenceBoundary", "certificationClaim"
    ];

    [Fact]
    public async Task OneThousandDeviceExportIsCompleteOrderedScopedAndTruthPreserving()
    {
        var db = Db();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var company = await Company(db, $"DX-{suffix}");
        var foreignCompany = await Company(db, $"DXF-{suffix}");
        var branchA = await Branch(db, company, $"XA-{suffix}");
        var branchB = await Branch(db, company, $"XB-{suffix}");
        var foreignBranch = await Branch(db, foreignCompany, $"XF-{suffix}");
        var actor = await User(db, company, branchA, suffix);

        try
        {
            await db.ExecuteAsync(
                @"INSERT INTO eld_devices
                    (company_id,branch_id,device_serial,device_category,manufacturer,device_model,
                     hardware_revision,provider,firmware_version,status,device_state,last_seen_at)
                  SELECT @company,CASE WHEN sequence<=500 THEN @branchA ELSE @branchB END,
                         'EXPORT-' || @suffix || '-' || LPAD(sequence::TEXT,4,'0'),'GPS',
                         CASE WHEN sequence=1 THEN '=HYPERLINK' ELSE 'Observed Manufacturer' END,
                         'Exact Model','Rev A','Observed Provider','1.2.3','Provisioning','Registered',NOW()
                    FROM generate_series(1,1000) sequence",
                command =>
                {
                    command.Parameters.AddWithValue("@company", company);
                    command.Parameters.AddWithValue("@branchA", branchA);
                    command.Parameters.AddWithValue("@branchB", branchB);
                    command.Parameters.AddWithValue("@suffix", suffix);
                });
            await db.ExecuteAsync(
                @"INSERT INTO eld_devices
                    (company_id,branch_id,device_serial,status,device_state)
                  VALUES(@company,@branch,@serial,'Provisioning','Registered')",
                command =>
                {
                    command.Parameters.AddWithValue("@company", foreignCompany);
                    command.Parameters.AddWithValue("@branch", foreignBranch);
                    command.Parameters.AddWithValue("@serial", $"EXPORT-{suffix}-FOREIGN");
                });

            var firstDevice = await db.ScalarLongAsync(
                "SELECT id FROM eld_devices WHERE company_id=@company AND device_serial=@serial",
                command =>
                {
                    command.Parameters.AddWithValue("@company", company);
                    command.Parameters.AddWithValue("@serial", $"EXPORT-{suffix}-0001");
                });
            await AddSpareProjection(db, company, branchA, actor, firstDevice, suffix);
            await AddSupportProjection(db, company, branchA, actor, firstDevice, suffix);
            var candidateSha = await AddCompatibilityProjection(db, suffix);

            var all = Lines(Csv(await Invoke(Principal(company, null), db)));
            Assert.Equal(string.Join(',', ExpectedColumns), all[0]);
            Assert.Equal(1001, all.Length);
            Assert.StartsWith($"EXPORT-{suffix}-0001,,GPS,'=HYPERLINK,", all[1], StringComparison.Ordinal);
            Assert.StartsWith($"EXPORT-{suffix}-1000,", all[^1], StringComparison.Ordinal);
            Assert.Contains(",Operations Spares,Available,OperatorRecordedUnverified,False,False,False,False,", all[1], StringComparison.Ordinal);
            Assert.Contains(",Assigned,Priority,AlwaysOn,30,OperatorRecordedUnverified,False,False,False,False,", all[1], StringComparison.Ordinal);
            Assert.Contains($",Candidate,{candidateSha},EngineeringDeclaredUnverified,J1939 | HTTPS,latitude | longitude,position | diagnostic,RequestPosition,No remote restart declared,Unverified,,,False,False,False,", all[1], StringComparison.Ordinal);
            Assert.EndsWith(",OperationalRecordOnly,False", all[1], StringComparison.Ordinal);
            Assert.DoesNotContain("FOREIGN", string.Join('\n', all), StringComparison.Ordinal);

            var branchOnly = Lines(Csv(await Invoke(Principal(company, branchB), db)));
            Assert.Equal(string.Join(',', ExpectedColumns), branchOnly[0]);
            Assert.Equal(501, branchOnly.Length);
            Assert.StartsWith($"EXPORT-{suffix}-0501,", branchOnly[1], StringComparison.Ordinal);
            Assert.StartsWith($"EXPORT-{suffix}-1000,", branchOnly[^1], StringComparison.Ordinal);
            Assert.DoesNotContain($"EXPORT-{suffix}-0500", string.Join('\n', branchOnly), StringComparison.Ordinal);
        }
        finally
        {
            // This suite runs on a disposable database. Stage 125/126 history is
            // deliberately append-only, so test cleanup must not weaken or bypass
            // those production guards. The database/container lifecycle removes it.
        }
    }

    [Fact]
    public async Task EmptyExportUsesTheSameStableGovernedSchema()
    {
        var db = Db();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var company = await Company(db, $"DXE-{suffix}");
        try
        {
            var lines = Lines(Csv(await Invoke(Principal(company, null), db)));
            Assert.Single(lines);
            Assert.Equal(string.Join(',', ExpectedColumns), lines[0]);
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM companies WHERE id=@company", c => c.Parameters.AddWithValue("@company", company));
        }
    }

    private static async Task AddSpareProjection(Database db, long company, long branch, long actor, long device, string suffix)
    {
        var entry = await db.InsertAsync(
            @"INSERT INTO device_spare_pool_entries
                (company_id,branch_id,device_id,device_serial_snapshot,pool_name,entry_reason,
                 source_reference,idempotency_key,added_by)
              VALUES(@company,@branch,@device,@serial,'Operations Spares','Recorded for export integrity',
                     @source,@key,@actor)",
            command =>
            {
                command.Parameters.AddWithValue("@company", company); command.Parameters.AddWithValue("@branch", branch);
                command.Parameters.AddWithValue("@device", device); command.Parameters.AddWithValue("@serial", $"EXPORT-{suffix}-0001");
                command.Parameters.AddWithValue("@source", $"EXPORT-SPARE-{suffix}"); command.Parameters.AddWithValue("@key", Guid.NewGuid());
                command.Parameters.AddWithValue("@actor", actor);
            });
        await db.ExecuteAsync(
            @"INSERT INTO device_spare_pool_events
                (company_id,branch_id,entry_id,device_id,action_type,state_after,action_reason,
                 source_reference,effective_at,idempotency_key,recorded_by)
              VALUES(@company,@branch,@entry,@device,'Added','Available','Recorded for export integrity',
                     @source,NOW(),@key,@actor)",
            command =>
            {
                command.Parameters.AddWithValue("@company", company); command.Parameters.AddWithValue("@branch", branch);
                command.Parameters.AddWithValue("@entry", entry); command.Parameters.AddWithValue("@device", device);
                command.Parameters.AddWithValue("@source", $"EXPORT-SPARE-EVENT-{suffix}"); command.Parameters.AddWithValue("@key", Guid.NewGuid());
                command.Parameters.AddWithValue("@actor", actor);
            });
    }

    private static Task AddSupportProjection(Database db, long company, long branch, long actor, long device, string suffix) =>
        db.ExecuteAsync(
            @"INSERT INTO device_support_tier_events
                (company_id,branch_id,device_id,device_serial_snapshot,action_type,state_after,tier_code,
                 coverage_window,routing_response_target_minutes,escalation_policy_reference,commercial_reference,
                 action_reason,source_reference,effective_at,idempotency_key,recorded_by)
              VALUES(@company,@branch,@device,@serial,'Assigned','Assigned','Priority','AlwaysOn',30,
                     'ESC-EXPORT','ORDER-EXPORT','Recorded for export integrity',@source,NOW(),@key,@actor)",
            command =>
            {
                command.Parameters.AddWithValue("@company", company); command.Parameters.AddWithValue("@branch", branch);
                command.Parameters.AddWithValue("@device", device); command.Parameters.AddWithValue("@serial", $"EXPORT-{suffix}-0001");
                command.Parameters.AddWithValue("@source", $"EXPORT-SUPPORT-{suffix}"); command.Parameters.AddWithValue("@key", Guid.NewGuid());
                command.Parameters.AddWithValue("@actor", actor);
            });

    private static async Task<string> AddCompatibilityProjection(Database db, string suffix)
    {
        var sha = suffix.PadRight(40, 'e')[..40];
        await db.ExecuteAsync(
            @"INSERT INTO device_compatibility_candidates
                (manufacturer,device_model,hardware_revision,firmware_version,software_candidate_sha,
                 external_hold_reason,capability_declaration_status,protocol_names,supported_fields,
                 supported_events,supported_commands,known_limitations,declaration_source_reference,declared_at)
              VALUES('=HYPERLINK','Exact Model','Rev A','1.2.3',@sha,'External evidence pending',
                     'EngineeringDeclaredUnverified',ARRAY['J1939','HTTPS'],ARRAY['latitude','longitude'],
                     ARRAY['position','diagnostic'],ARRAY['RequestPosition'],'No remote restart declared',
                     'engineering://export-integrity',NOW())",
            command => command.Parameters.AddWithValue("@sha", sha));
        return sha;
    }

    private static DefaultHttpContext Principal(long company, long? branch)
    {
        var http = new DefaultHttpContext();
        http.Items[EndpointMappings.AuthUserIdItemKey] = 41L;
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = company;
        if (branch is not null) http.Items[EndpointMappings.AuthBranchIdItemKey] = branch.Value;
        http.Items[EndpointMappings.AuthRoleItemKey] = "Fleet Manager";
        http.Items[EndpointMappings.AuthPermissionsItemKey] = new[] { "telematics:devices:export" };
        return http;
    }

    private static async Task<IResult> Invoke(DefaultHttpContext http, Database db)
    {
        var method = typeof(EndpointMappings).GetMethod("TelemetryDeviceExport", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Missing TelemetryDeviceExport endpoint");
        return await ((Task<IResult>)method.Invoke(null, [http, db, CancellationToken.None])!);
    }

    private static string Csv(IResult result)
    {
        Assert.Equal("text/csv", Assert.IsAssignableFrom<IContentTypeHttpResult>(result).ContentType);
        var contents = result.GetType().GetProperty("FileContents")!.GetValue(result);
        var bytes = contents switch
        {
            byte[] value => value,
            ReadOnlyMemory<byte> value => value.ToArray(),
            _ => throw new Xunit.Sdk.XunitException($"Unexpected CSV content: {contents?.GetType().FullName ?? "null"}")
        };
        return Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string[] Lines(string csv) => csv.TrimEnd('\n').Split('\n');

    private static Task<long> Company(Database db, string code) => db.InsertAsync(
        "INSERT INTO companies(company_code,name,industry) VALUES(@code,'Device export integrity','Transportation')",
        command => command.Parameters.AddWithValue("@code", code));

    private static Task<long> Branch(Database db, long company, string code) => db.InsertAsync(
        "INSERT INTO branches(company_id,branch_code,name,status) VALUES(@company,@code,'Export branch','Active')",
        command => { command.Parameters.AddWithValue("@company", company); command.Parameters.AddWithValue("@code", code); });

    private static Task<long> User(Database db, long company, long branch, string suffix) => db.InsertAsync(
        @"INSERT INTO users(company_id,branch_id,full_name,email,role_name,status)
          VALUES(@company,@branch,'Export operator',@email,'Fleet Manager','Active')",
        command =>
        {
            command.Parameters.AddWithValue("@company", company); command.Parameters.AddWithValue("@branch", branch);
            command.Parameters.AddWithValue("@email", $"device-export-{suffix}@example.test");
        });

    private static Database Db() => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = GuardedConnection(),
            ["Rls:EnforceTenantContext"] = "false",
        }).Build(), new TenantScopeAccessor());

    private static string GuardedConnection()
    {
        var configured = Environment.GetEnvironmentVariable("OPSTRAX_DEVICEOPS_STAGE127_DB");
        if (string.IsNullOrWhiteSpace(configured))
            throw SkipException.ForSkip("Set OPSTRAX_DEVICEOPS_STAGE127_DB to the dedicated disposable export database.");
        var builder = new NpgsqlConnectionStringBuilder(configured);
        if (builder.Host is not ("127.0.0.1" or "::1") || builder.Port != 55453 ||
            builder.Database != "opstrax_deviceops_stage127")
            throw new InvalidOperationException("Device export tests require the dedicated local database on port 55453.");
        return builder.ConnectionString;
    }
}
