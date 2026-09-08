using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Xunit.Sdk;

namespace Opstrax.Tests;

[Trait("Category", "DeviceCompatibilityRegistryPostgres")]
public sealed class DeviceCompatibilityRegistryPostgresTests
{
    [Fact]
    public void BulkImportRejectsOversizedHardwareIdentityFields()
    {
        var method = typeof(EndpointMappings).GetMethod(
            "ValidateDeviceImportRow",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var row = new Dictionary<string, object?>
        {
            ["deviceSerial"] = "GPS-TEST-0001",
            ["deviceCategory"] = "GPS",
            ["manufacturer"] = new string('m', 121),
            ["hardwareRevision"] = new string('h', 121),
            ["firmwareVersion"] = new string('f', 121),
        };

        var errors = Assert.IsType<List<string>>(method!.Invoke(
            null, [row, new HashSet<string>(StringComparer.OrdinalIgnoreCase)]));

        Assert.Contains("manufacturer must be 120 characters or fewer.", errors);
        Assert.Contains("hardwareRevision must be 120 characters or fewer.", errors);
        Assert.Contains("firmwareVersion must be 120 characters or fewer.", errors);
    }

    [Fact]
    public async Task IncompleteDeviceIdentityFailsClosedWithoutCandidateClaim()
    {
        var payload = await Project(new Dictionary<string, object?>
        {
            ["deviceModel"] = "PT40",
            ["firmwareVersion"] = "1.2.3",
        });

        Assert.Equal("ExternalHold", payload.GetProperty("certificationStatus").GetString());
        Assert.Equal("Unverified", payload.GetProperty("maximumTier").GetString());
        Assert.False(payload.GetProperty("certificationClaim").GetBoolean());
        Assert.False(payload.GetProperty("exactTupleComplete").GetBoolean());
        var missing = payload.GetProperty("missingIdentityFields").EnumerateArray().Select(item => item.GetString()).ToArray();
        Assert.Contains("manufacturer", missing);
        Assert.Contains("hardware revision", missing);
    }

    [Fact]
    public async Task ExactTupleFindsOnlyFrozenCandidateAndStillReportsExternalHold()
    {
        var database = Db();
        var suffix = Guid.NewGuid().ToString("N");
        var sha = suffix.PadRight(40, 'a')[..40];
        try
        {
            await database.ExecuteAsync(
                @"INSERT INTO device_compatibility_candidates
                    (manufacturer,device_model,hardware_revision,firmware_version,software_candidate_sha,external_hold_reason)
                  VALUES (@manufacturer,@model,@hardware,@firmware,@sha,@reason)",
                command =>
                {
                    command.Parameters.AddWithValue("@manufacturer", $"Pacific Track {suffix}");
                    command.Parameters.AddWithValue("@model", "PT40");
                    command.Parameters.AddWithValue("@hardware", "Rev-A");
                    command.Parameters.AddWithValue("@firmware", "1.2.3");
                    command.Parameters.AddWithValue("@sha", sha);
                    command.Parameters.AddWithValue("@reason", "Physical bench, route, recovery and soak evidence has not been supplied.");
                });

            var payload = await Project(new Dictionary<string, object?>
            {
                ["manufacturer"] = $" pacific track {suffix} ",
                ["deviceModel"] = "pt40",
                ["hardwareRevision"] = "rev-a",
                ["firmwareVersion"] = "1.2.3",
            });

            Assert.True(payload.GetProperty("exactTupleComplete").GetBoolean());
            Assert.Equal("Candidate", payload.GetProperty("registryStatus").GetString());
            Assert.Equal("ExternalHold", payload.GetProperty("certificationStatus").GetString());
            Assert.Equal("Unverified", payload.GetProperty("maximumTier").GetString());
            Assert.Equal(sha, payload.GetProperty("candidateSha").GetString());
            Assert.Equal("NotRecorded", payload.GetProperty("capabilityDeclarationStatus").GetString());
            Assert.Empty(payload.GetProperty("protocols").EnumerateArray());
            Assert.Null(payload.GetProperty("certificationReference").GetString());
            Assert.False(payload.GetProperty("physicalEvidenceClaim").GetBoolean());
            Assert.False(payload.GetProperty("providerEvidenceClaim").GetBoolean());
            Assert.False(payload.GetProperty("certificationClaim").GetBoolean());
        }
        finally
        {
            await database.ExecuteAsync(
                "DELETE FROM device_compatibility_candidates WHERE software_candidate_sha=@sha",
                command => command.Parameters.AddWithValue("@sha", sha));
        }
    }

    [Fact]
    public async Task ExactTupleProjectsEngineeringDeclarationWithoutEvidenceOrCertificationClaims()
    {
        var database = Db();
        var suffix = Guid.NewGuid().ToString("N");
        var sha = suffix.PadRight(40, 'c')[..40];
        try
        {
            await database.ExecuteAsync(
                @"INSERT INTO device_compatibility_candidates
                    (manufacturer,device_model,hardware_revision,firmware_version,software_candidate_sha,
                     external_hold_reason,capability_declaration_status,protocol_names,supported_fields,
                     supported_events,supported_commands,known_limitations,declaration_source_reference,declared_at)
                  VALUES (@manufacturer,'PT40','Rev-B','2.4.1',@sha,@reason,
                          'EngineeringDeclaredUnverified',ARRAY['J1939','HTTPS'],
                          ARRAY['latitude','longitude','engineHours'],ARRAY['position','diagnostic'],
                          ARRAY['RequestPosition'],'Remote restart is not declared for this tuple.',
                          'engineering://device-catalog/PT40-Rev-B-2.4.1','2026-09-08T12:00:00Z')",
                command =>
                {
                    command.Parameters.AddWithValue("@manufacturer", $"Pacific Track {suffix}");
                    command.Parameters.AddWithValue("@sha", sha);
                    command.Parameters.AddWithValue("@reason", "Physical and provider evidence is pending.");
                });

            var payload = await Project(new Dictionary<string, object?>
            {
                ["manufacturer"] = $"Pacific Track {suffix}",
                ["deviceModel"] = "PT40",
                ["hardwareRevision"] = "Rev-B",
                ["firmwareVersion"] = "2.4.1",
            });

            Assert.Equal("EngineeringDeclaredUnverified", payload.GetProperty("capabilityDeclarationStatus").GetString());
            Assert.Equal(["J1939", "HTTPS"], payload.GetProperty("protocols").EnumerateArray().Select(item => item.GetString()!).ToArray());
            Assert.Equal(["latitude", "longitude", "engineHours"], payload.GetProperty("supportedFields").EnumerateArray().Select(item => item.GetString()!).ToArray());
            Assert.Equal(["position", "diagnostic"], payload.GetProperty("supportedEvents").EnumerateArray().Select(item => item.GetString()!).ToArray());
            Assert.Equal(["RequestPosition"], payload.GetProperty("supportedCommands").EnumerateArray().Select(item => item.GetString()!).ToArray());
            Assert.Equal("Remote restart is not declared for this tuple.", payload.GetProperty("knownLimitations").GetString());
            Assert.Equal("Unverified", payload.GetProperty("catalogSupportTier").GetString());
            Assert.Null(payload.GetProperty("certificationReference").GetString());
            Assert.Null(payload.GetProperty("certificationDate").GetString());
            Assert.False(payload.GetProperty("physicalEvidenceClaim").GetBoolean());
            Assert.False(payload.GetProperty("providerEvidenceClaim").GetBoolean());
            Assert.False(payload.GetProperty("certificationClaim").GetBoolean());
            Assert.Equal("ExternalHold", payload.GetProperty("certificationStatus").GetString());
        }
        finally
        {
            await database.ExecuteAsync(
                "DELETE FROM device_compatibility_candidates WHERE software_candidate_sha=@sha",
                command => command.Parameters.AddWithValue("@sha", sha));
        }
    }

    [Fact]
    public async Task DatabasePermitsOneDeclarationThenRefusesMutationAndEvidenceClaims()
    {
        var database = Db();
        var suffix = Guid.NewGuid().ToString("N");
        var sha = suffix.PadRight(40, 'd')[..40];
        try
        {
            var id = await database.InsertAsync(
                @"INSERT INTO device_compatibility_candidates
                    (manufacturer,device_model,hardware_revision,firmware_version,software_candidate_sha,external_hold_reason)
                  VALUES (@manufacturer,'PT40','Rev-C','3.0.0',@sha,'External evidence pending')",
                command =>
                {
                    command.Parameters.AddWithValue("@manufacturer", $"Pacific Track {suffix}");
                    command.Parameters.AddWithValue("@sha", sha);
                });

            await database.ExecuteAsync(
                @"UPDATE device_compatibility_candidates SET
                     capability_declaration_status='EngineeringDeclaredUnverified',
                     protocol_names=ARRAY['HTTPS'],supported_fields=ARRAY['latitude'],
                     supported_events=ARRAY['position'],supported_commands=ARRAY[]::TEXT[],
                     known_limitations='No remote commands are declared.',
                     declaration_source_reference='engineering://catalog-review',declared_at=NOW()
                   WHERE id=@id",
                command => command.Parameters.AddWithValue("@id", id));

            var mutation = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteAsync(
                "UPDATE device_compatibility_candidates SET supported_commands=ARRAY['RestartDevice'] WHERE id=@id",
                command => command.Parameters.AddWithValue("@id", id)));
            Assert.Equal(PostgresErrorCodes.CheckViolation, mutation.SqlState);

            var claim = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteAsync(
                "UPDATE device_compatibility_candidates SET certification_claim=TRUE WHERE id=@id",
                command => command.Parameters.AddWithValue("@id", id)));
            Assert.Equal(PostgresErrorCodes.CheckViolation, claim.SqlState);

            var reference = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteAsync(
                "UPDATE device_compatibility_candidates SET certification_reference='CERT-FAKE' WHERE id=@id",
                command => command.Parameters.AddWithValue("@id", id)));
            Assert.Equal(PostgresErrorCodes.CheckViolation, reference.SqlState);
        }
        finally
        {
            await database.ExecuteAsync(
                "DELETE FROM device_compatibility_candidates WHERE software_candidate_sha=@sha",
                command => command.Parameters.AddWithValue("@sha", sha));
        }
    }

    [Fact]
    public async Task DatabaseRefusesCertificationPromotionAndTupleDrift()
    {
        var database = Db();
        var suffix = Guid.NewGuid().ToString("N");
        var sha = suffix.PadRight(40, 'b')[..40];
        try
        {
            var id = await database.InsertAsync(
                @"INSERT INTO device_compatibility_candidates
                    (manufacturer,device_model,hardware_revision,firmware_version,software_candidate_sha,external_hold_reason)
                  VALUES (@manufacturer,'PT40','Rev-A','1.2.3',@sha,'External evidence pending')",
                command =>
                {
                    command.Parameters.AddWithValue("@manufacturer", $"Pacific Track {suffix}");
                    command.Parameters.AddWithValue("@sha", sha);
                });

            var promotion = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteAsync(
                "UPDATE device_compatibility_candidates SET certification_status='CertifiedCompatible' WHERE id=@id",
                command => command.Parameters.AddWithValue("@id", id)));
            Assert.Equal(PostgresErrorCodes.CheckViolation, promotion.SqlState);

            var tupleDrift = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteAsync(
                "UPDATE device_compatibility_candidates SET firmware_version='9.9.9' WHERE id=@id",
                command => command.Parameters.AddWithValue("@id", id)));
            Assert.Equal(PostgresErrorCodes.CheckViolation, tupleDrift.SqlState);

            var stored = await database.QuerySingleAsync(
                "SELECT certification_status,firmware_version FROM device_compatibility_candidates WHERE id=@id",
                command => command.Parameters.AddWithValue("@id", id));
            Assert.NotNull(stored);
            Assert.Equal("ExternalHold", stored!["certificationStatus"]?.ToString());
            Assert.Equal("1.2.3", stored["firmwareVersion"]?.ToString());
        }
        finally
        {
            await database.ExecuteAsync(
                "DELETE FROM device_compatibility_candidates WHERE software_candidate_sha=@sha",
                command => command.Parameters.AddWithValue("@sha", sha));
        }
    }

    private static async Task<JsonElement> Project(Dictionary<string, object?> device)
    {
        var method = typeof(EndpointMappings).GetMethod(
            "DeviceCompatibilityProjection",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var task = Assert.IsAssignableFrom<Task<object>>(method!.Invoke(null, [Db(), device, CancellationToken.None]));
        return JsonSerializer.SerializeToElement(await task);
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
        var configured = Environment.GetEnvironmentVariable("OPSTRAX_DEVICEOPS_STAGE128_DB");
        if (string.IsNullOrWhiteSpace(configured))
            throw SkipException.ForSkip("Set OPSTRAX_DEVICEOPS_STAGE128_DB to the dedicated disposable DeviceOps database.");
        var builder = new NpgsqlConnectionStringBuilder(configured);
        if (builder.Host is not ("127.0.0.1" or "::1") || builder.Port != 55454 ||
            builder.Database != "opstrax_deviceops_stage128")
            throw new InvalidOperationException("DeviceOps integration tests require the dedicated local Stage128 database on port 55454.");
        return builder.ConnectionString;
    }
}
