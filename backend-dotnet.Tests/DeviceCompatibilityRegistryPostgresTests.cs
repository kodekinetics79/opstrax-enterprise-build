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
        var configured = Environment.GetEnvironmentVariable("OPSTRAX_DEVICEOPS_STAGE115_DB");
        if (string.IsNullOrWhiteSpace(configured))
            throw SkipException.ForSkip("Set OPSTRAX_DEVICEOPS_STAGE115_DB to the dedicated disposable DeviceOps database.");
        var builder = new NpgsqlConnectionStringBuilder(configured);
        if (builder.Host is not ("127.0.0.1" or "::1") || builder.Port != 55443 ||
            builder.Database != "opstrax_deviceops_stage115")
            throw new InvalidOperationException("DeviceOps integration tests require the dedicated local database on port 55443.");
        return builder.ConnectionString;
    }
}
