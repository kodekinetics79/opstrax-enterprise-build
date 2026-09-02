using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Services;
using Xunit;

namespace Opstrax.Tests;

/// <summary>
/// DEF-023 (server half) — device revoke must write the same lifecycle evidence as its
/// suspend/activate siblings: a device_state_transitions ledger row and an audit row,
/// while clearing every one of the seven credential columns. Revoke previously killed
/// the credentials but left NO transition, so the device timeline showed a device that
/// silently stopped existing.
///
/// HARNESS NOTE (this suite is deliberately NOT owner-connected). Revoke is the only
/// device-lifecycle action that writes eld_devices CREDENTIAL columns, and the restricted
/// runtime role `opstrax_app` holds no UPDATE privilege on them — stage76
/// (2026_08_11_stage76_telematics_security_hardening.sql:283-284) RAISES if it ever does.
/// A suite connected as the superuser owner bypasses both RLS and column privileges, so it
/// is STRUCTURALLY BLIND to a handler that runs the credential UPDATE in the tenant lane:
/// it passes locally and 42501s in every protected environment. Every handler invocation
/// below therefore goes through the production posture — the restricted `opstrax_app`
/// connection with RLS enforced and a separate `opstrax_system` control-plane connection —
/// exactly like ComplianceEvidenceTenantBoundaryPostgresTests. The owner connection is used
/// ONLY for fixture setup, assertions and teardown.
/// </summary>
public class DeviceRevokeLifecyclePostgresTests
{
    /// <summary>
    /// The privilege boundary this suite exists to defend. If this fails, someone "fixed"
    /// revoke by granting `opstrax_app` UPDATE on the credential columns — which stage76
    /// rejects outright. The fix is to run the credential write as `opstrax_system`, never
    /// to widen the app role.
    /// </summary>
    [Fact]
    public async Task AppRole_NeverHoldsUpdateOnCredentialColumns_ButSystemRoleDoes()
    {
        await using var connection = new NpgsqlConnection(TestDb.ConnectionString);
        await connection.OpenAsync();
        foreach (var column in CredentialColumns)
        {
            await using var command = new NpgsqlCommand(
                "SELECT has_column_privilege('opstrax_app','eld_devices',@col,'UPDATE'), " +
                "       has_column_privilege('opstrax_system','eld_devices',@col,'UPDATE')", connection);
            command.Parameters.AddWithValue("@col", column);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.False(reader.GetBoolean(0),
                $"stage76 forbids opstrax_app holding UPDATE on eld_devices.{column} — revoke must run as opstrax_system instead.");
            Assert.True(reader.GetBoolean(1),
                $"opstrax_system must hold UPDATE on eld_devices.{column} or revoke cannot clear credentials at all.");
        }
    }

    [Fact]
    public async Task Revoke_WritesTransitionAndAudit_ClearsAllSevenCredentialColumns_AndReplaysIdempotently()
    {
        var owner = Db(TestDb.ConnectionString, false);
        var runtime = Db(TestDb.AppConnectionString, true, TestDb.SystemConnectionString);
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(7_000_000, 7_900_000);
        var otherCompanyId = companyId + 1;
        await Company(owner, companyId, "REVOKE-A");
        await Company(owner, otherCompanyId, "REVOKE-B");
        try
        {
            var device = await Device(owner, companyId, $"REVOKE-A-{companyId}");
            var otherDevice = await Device(owner, otherCompanyId, $"REVOKE-B-{companyId}");

            // Sanity: seeded with live credentials.
            var before = await owner.QuerySingleAsync(
                "SELECT api_key_hash, hmac_secret_encrypted FROM eld_devices WHERE id=@d",
                c => c.Parameters.AddWithValue("@d", device));
            Assert.False(before!["apiKeyHash"] is null or DBNull);

            // The kill switch, executed exactly as production executes it: restricted app
            // identity + RLS + a separate system control-plane identity.
            var revoke = await Invoke("DeviceRevoke", Principal(companyId, 42), device, runtime, new AuditService(runtime), CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, Status(revoke));

            // All 7 credential columns cleared; status flipped; revoked_at stamped.
            var after = await owner.QuerySingleAsync(
                @"SELECT status, device_state, revoked_at, credential_revoked_reason,
                         api_key_hash, hmac_secret, hmac_secret_encrypted,
                         api_key_previous_hash, api_key_previous_valid_until,
                         hmac_previous_secret_encrypted, hmac_previous_valid_until
                  FROM eld_devices WHERE id=@d",
                c => c.Parameters.AddWithValue("@d", device));
            Assert.NotNull(after);
            Assert.Equal("Revoked", after!["status"]);
            Assert.Equal("Decommissioned", after["deviceState"]);
            Assert.False(after["revokedAt"] is null or DBNull);
            Assert.Equal("operator_revoke", after["credentialRevokedReason"]);
            foreach (var credentialColumn in new[]
            {
                "apiKeyHash", "hmacSecret", "hmacSecretEncrypted",
                "apiKeyPreviousHash", "apiKeyPreviousValidUntil",
                "hmacPreviousSecretEncrypted", "hmacPreviousValidUntil",
            })
            {
                Assert.True(after[credentialColumn] is null or DBNull,
                    $"credential column '{credentialColumn}' must be cleared on revoke");
            }

            // Ledger + audit evidence, exactly once.
            Assert.Equal(1, await owner.ScalarLongAsync(
                @"SELECT COUNT(*) FROM device_state_transitions
                  WHERE company_id=@c AND device_id=@d AND to_state='Decommissioned' AND reason_code='operator_revoke'",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@d", device); }));
            Assert.Equal(1, await owner.ScalarLongAsync(
                "SELECT COUNT(*) FROM audit_logs WHERE company_id=@c AND entity_id=@d AND action_name='device.revoked'",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@d", device); }));
            // from_state carries the last physical device_state; neither CHECK vocabulary
            // has 'Revoked', so the revocation is encoded as Decommissioned + reason_code.
            var transition = await owner.QuerySingleAsync(
                "SELECT from_state FROM device_state_transitions WHERE company_id=@c AND device_id=@d",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@d", device); });
            Assert.Equal("Registered", transition!["fromState"]);

            // Idempotent replay: still 200, but no duplicate ledger/audit rows.
            var replay = await Invoke("DeviceRevoke", Principal(companyId, 42), device, runtime, new AuditService(runtime), CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, Status(replay));
            Assert.Equal(1, await owner.ScalarLongAsync(
                "SELECT COUNT(*) FROM device_state_transitions WHERE company_id=@c AND device_id=@d",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@d", device); }));
            Assert.Equal(1, await owner.ScalarLongAsync(
                "SELECT COUNT(*) FROM audit_logs WHERE company_id=@c AND entity_id=@d AND action_name='device.revoked'",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@d", device); }));

            // Tenant scoping stays intact even though the write runs on the system lane:
            // the locked read is explicitly company-scoped, so another tenant's device is
            // invisible and NOTHING is written for it.
            var crossTenant = await Invoke("DeviceRevoke", Principal(companyId, 42), otherDevice, runtime, new AuditService(runtime), CancellationToken.None);
            Assert.Equal(StatusCodes.Status404NotFound, Status(crossTenant));
            var otherAfter = await owner.QuerySingleAsync(
                "SELECT status, api_key_hash FROM eld_devices WHERE id=@d",
                c => c.Parameters.AddWithValue("@d", otherDevice));
            Assert.Equal("Active", otherAfter!["status"]);
            Assert.False(otherAfter["apiKeyHash"] is null or DBNull,
                "a cross-tenant revoke must not touch the other tenant's credentials");
            Assert.Equal(0, await owner.ScalarLongAsync(
                "SELECT COUNT(*) FROM device_state_transitions WHERE company_id=@c",
                c => c.Parameters.AddWithValue("@c", otherCompanyId)));

            // Permission gate stays intact.
            var readOnly = await Invoke("DeviceRevoke", Principal(companyId, 42, new[] { "telemetry.devices.read" }), device, runtime, new AuditService(runtime), CancellationToken.None);
            Assert.Equal(StatusCodes.Status403Forbidden, Status(readOnly));

            // Revoked devices stay dead: suspend refuses (and this path still runs in the
            // TENANT lane, proving the split did not move the non-credential siblings).
            var suspendAfter = await Invoke("DeviceSuspend", Principal(companyId, 42), device, runtime, new AuditService(runtime), CancellationToken.None);
            Assert.Equal(StatusCodes.Status409Conflict, Status(suspendAfter));
        }
        finally
        {
            foreach (var company in new[] { companyId, otherCompanyId })
            foreach (var sql in new[]
            {
                "DELETE FROM audit_logs WHERE company_id=@c", "DELETE FROM device_state_transitions WHERE company_id=@c",
                "DELETE FROM eld_devices WHERE company_id=@c", "DELETE FROM companies WHERE id=@c",
            }) await owner.ExecuteAsync(sql, c => c.Parameters.AddWithValue("@c", company));
        }
    }

    // The nine eld_devices columns revoke SETs that opstrax_app may not UPDATE.
    private static readonly string[] CredentialColumns =
    [
        "revoked_at", "credential_revoked_reason", "api_key_hash", "hmac_secret", "hmac_secret_encrypted",
        "api_key_previous_hash", "api_key_previous_valid_until",
        "hmac_previous_secret_encrypted", "hmac_previous_valid_until",
    ];

    // ── Helpers (mirrors ComplianceEvidenceTenantBoundaryPostgresTests) ──────────
    private static Database Db(string appConnection, bool rls, string? systemConnection = null)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = appConnection,
            ["ConnectionStrings:SystemConnection"] = systemConnection,
            ["Rls:EnforceTenantContext"] = rls.ToString(),
            ["Rls:TenantTicketTtlSeconds"] = "120",
        }).Build();
        return new Database(config, new TenantScopeAccessor());
    }

    private static async Task Company(Database db, long id, string suffix) => await db.ExecuteAsync(
        "INSERT INTO companies(id,company_code,name,industry) OVERRIDING SYSTEM VALUE VALUES (@c,@code,@name,'transport')",
        c => { c.Parameters.AddWithValue("@c", id); c.Parameters.AddWithValue("@code", $"REVOKE-{id}-{suffix}"); c.Parameters.AddWithValue("@name", $"Revoke tenant {suffix}"); });

    private static Task<long> Device(Database db, long company, string serial) => db.InsertAsync(
        @"INSERT INTO eld_devices(company_id,device_serial,status,device_state,api_key_hash,hmac_secret_encrypted,hmac_key_version,
                                  api_key_previous_hash,api_key_previous_valid_until,hmac_previous_secret_encrypted,hmac_previous_valid_until,created_at)
          VALUES (@c,@serial,'Active','Registered',encode(sha256(@serial::bytea),'hex'),repeat('b',32),1,
                  encode(sha256((@serial || '-prev')::bytea),'hex'),NOW() + INTERVAL '10 minutes',repeat('c',32),NOW() + INTERVAL '10 minutes',NOW())",
        c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@serial", serial); });

    private static DefaultHttpContext Principal(long companyId, long userId, string[]? permissions = null)
    {
        var http = new DefaultHttpContext { TraceIdentifier = $"revoke-{Guid.NewGuid():N}" };
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = companyId;
        http.Items[EndpointMappings.AuthUserIdItemKey] = userId;
        http.Items[EndpointMappings.AuthRoleItemKey] = "CompanyAdmin";
        http.Items[EndpointMappings.AuthPermissionsItemKey] = permissions ?? new[] { "telemetry.devices.manage", "telemetry.devices.read" };
        return http;
    }

    private static async Task<IResult> Invoke(string name, params object?[] args)
    {
        var method = typeof(EndpointMappings).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Missing endpoint {name}");
        return await ((Task<IResult>?)method.Invoke(null, args) ?? throw new InvalidOperationException($"{name} did not return a task"));
    }

    private static int? Status(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;
}
