using Opstrax.Api.Controllers;
using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

/// <summary>
/// Break-glass recovery for the bootstrap Platform Super Admin identity, driven from
/// env (<c>PLATFORM_SUPERADMIN_EMAIL</c> / <c>PLATFORM_SUPERADMIN_PASSWORD</c>).
///
/// WHY THIS EXISTS — the P0 it closes:
/// <see cref="PlatformSchemaService"/>.SeedSuperAdminAsync writes the platform super
/// admin's password to the DB EXACTLY ONCE — the first boot when <c>platform_admins</c>
/// is empty (<c>if (anyAdmin &gt; 0) return;</c>). After that the env var is never read
/// again, so rotating <c>PLATFORM_SUPERADMIN_PASSWORD</c> in Render has ZERO effect on the
/// stored hash. Worse, in production (RLS enforced, restricted <c>opstrax_app</c> role) the
/// entire schema-init block — including that seed — is SKIPPED, so the seed may never have
/// run on the deployed environment at all. Either way <c>/api/platform/auth/login</c>
/// compares the entered password against a stale/absent hash and returns the deliberately
/// generic "Invalid credentials" — which is exactly the "the credentials in Render are the
/// same but login is rejected" symptom: Render holds the INTENDED password, the DB holds a
/// DIFFERENT (or missing) one, and nothing reconciles them.
///
/// DESIGN NOTES (mirrors <see cref="RolePermissionReconciler"/>):
///  • Runs as DML (no DDL) on EVERY boot, deliberately OUTSIDE the schema-init gate, so it
///    executes even when schema init is skipped (restricted role under RLS enforcement, i.e.
///    production). Executes under a SYSTEM scope because the tenant runtime role is walled off
///    from the control-plane tables entirely — <c>platform_admins</c> is reachable only via the
///    separately-authenticated <c>opstrax_system</c> identity (the same identity every
///    <c>/api/platform/*</c> request already runs under).
///  • OPT-IN and inert by default. It does NOTHING unless <c>PLATFORM_SUPERADMIN_RESET</c> is
///    explicitly truthy, so a normal boot never touches the roster and a self-service password
///    change is never stomped. The operator sets the flag, redeploys, signs in, then UNSETS it.
///  • IDEMPOTENT: when the env password already verifies and the account is Active / super /
///    has no pending invite, it is a no-op — a lingering flag does not churn sessions each boot.
///  • FAIL-OPEN for availability: any error is logged loudly and swallowed. A failure here
///    leaves the account exactly as it was; it never takes the API down.
/// </summary>
public sealed class PlatformSuperAdminReconciler(Database db, ILogger<PlatformSuperAdminReconciler> logger)
{
    private const string SuperAdminRoleKey = "platform_super_admin";

    public async Task ReconcileAsync(CancellationToken ct = default)
    {
        var reset = Environment.GetEnvironmentVariable("PLATFORM_SUPERADMIN_RESET");
        if (!IsTruthy(reset)) return; // Inert unless explicitly armed.

        var email = Environment.GetEnvironmentVariable("PLATFORM_SUPERADMIN_EMAIL")?.Trim();
        var password = Environment.GetEnvironmentVariable("PLATFORM_SUPERADMIN_PASSWORD");

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning(new EventId(0, "platform_superadmin_reset_incomplete"),
                "PLATFORM_SUPERADMIN_RESET is set but PLATFORM_SUPERADMIN_EMAIL/PASSWORD are missing — " +
                "skipping break-glass reset. Set both, then redeploy.");
            return;
        }

        // Same floor the accept-invite path enforces — never install a weak bootstrap password.
        if (!MeetsPasswordPolicy(password))
        {
            logger.LogWarning(new EventId(0, "platform_superadmin_reset_weak"),
                "PLATFORM_SUPERADMIN_RESET is set but PLATFORM_SUPERADMIN_PASSWORD does not meet the policy " +
                "(≥12 characters, at least one letter and one digit) — skipping break-glass reset.");
            return;
        }

        try
        {
            await db.RunInSystemScopeAsync(() => ReconcileCoreAsync(email!, password!, ct), ct);
        }
        catch (Exception ex)
        {
            logger.LogError(new EventId(0, "platform_superadmin_reset_failed"), ex,
                "Platform Super Admin break-glass reset FAILED for {Email}. The account is unchanged; " +
                "API is still serving. Verify the system database connection and platform_admins access.", email);
        }
    }

    private async Task ReconcileCoreAsync(string email, string password, CancellationToken ct)
    {
        var superRoleId = await db.ScalarLongAsync(
            "SELECT COALESCE((SELECT id FROM platform_roles WHERE role_key=@k), 0)",
            c => c.Parameters.AddWithValue("@k", SuperAdminRoleKey), ct);

        var existing = await db.QuerySingleAsync(
            @"SELECT a.id, a.password_hash, a.status, a.invite_token_hash, COALESCE(r.role_key,'') AS role_key
              FROM platform_admins a LEFT JOIN platform_roles r ON r.id = a.role_id
              WHERE LOWER(a.email)=LOWER(@e) LIMIT 1",
            c => c.Parameters.AddWithValue("@e", email), ct);

        // ── Recovery from an empty/rotated identity: the bootstrap email isn't in the roster ──
        if (existing is null)
        {
            if (superRoleId <= 0)
            {
                logger.LogWarning(new EventId(0, "platform_superadmin_role_missing"),
                    "Break-glass reset for {Email} requested but the '{Role}' role does not exist yet — " +
                    "cannot mint a functional super admin. Ensure platform roles are seeded first.", email, SuperAdminRoleKey);
                return;
            }

            var newId = await db.InsertAsync(
                @"INSERT INTO platform_admins (email, full_name, password_hash, role_id, status)
                  VALUES (@e, @n, @h, @r, 'Active')
                  ON CONFLICT (email) DO NOTHING
                  RETURNING id",
                c =>
                {
                    c.Parameters.AddWithValue("@e", email);
                    c.Parameters.AddWithValue("@n", "Platform Owner");
                    c.Parameters.AddWithValue("@h", PlatformSchemaService.HashPassword(password));
                    c.Parameters.AddWithValue("@r", superRoleId);
                }, ct);

            await WriteAuditAsync(email, newId, "created", 0, ct);
            logger.LogWarning(new EventId(0, "platform_superadmin_reset_created"),
                "PLATFORM_SUPERADMIN_RESET applied: bootstrap Super Admin {Email} was ABSENT and has been created (Active). " +
                "Sign in, then UNSET PLATFORM_SUPERADMIN_RESET so a later self-service password change is never overwritten.", email);
            return;
        }

        var adminId = Convert.ToInt64(existing["id"]);
        var status = existing["status"]?.ToString() ?? "";
        var roleKey = existing["roleKey"]?.ToString() ?? "";
        var hasPendingInvite = !string.IsNullOrWhiteSpace(existing["inviteTokenHash"]?.ToString());
        var passwordMatches = PlatformEndpoints.VerifyPassword(password, existing["passwordHash"]?.ToString());

        // Idempotent: a healthy, in-sync account is a no-op, so leaving the flag armed does not
        // revoke sessions or spam the log on every deploy.
        if (!NeedsReconcile(passwordMatches, status, roleKey, hasPendingInvite))
        {
            logger.LogInformation(new EventId(0, "platform_superadmin_reset_noop"),
                "PLATFORM_SUPERADMIN_RESET is armed but {Email} already matches the env password and is a healthy, " +
                "Active Super Admin — nothing to do. You can safely UNSET PLATFORM_SUPERADMIN_RESET now.", email);
            return;
        }

        // role_id only changes when the env role actually resolves; never null out a good assignment.
        await db.ExecuteAsync(
            @"UPDATE platform_admins
              SET password_hash     = @h,
                  status            = 'Active',
                  invite_token_hash = NULL,
                  invite_expires_at = NULL,
                  role_id           = COALESCE(NULLIF(@r, 0), role_id),
                  updated_at        = NOW()
              WHERE id = @id",
            c =>
            {
                c.Parameters.AddWithValue("@h", PlatformSchemaService.HashPassword(password));
                c.Parameters.AddWithValue("@r", superRoleId);
                c.Parameters.AddWithValue("@id", adminId);
            }, ct);

        // The new password is now the only way in — kill any pre-existing sessions.
        var revoked = await db.ExecuteAsync("DELETE FROM platform_sessions WHERE admin_id=@id",
            c => c.Parameters.AddWithValue("@id", adminId), ct);

        await WriteAuditAsync(email, adminId, "reconciled", revoked, ct);
        logger.LogWarning(new EventId(0, "platform_superadmin_reset_applied"),
            "PLATFORM_SUPERADMIN_RESET applied: Super Admin {Email} password reconciled from env, account set Active, " +
            "{Revoked} session(s) revoked. Sign in, then UNSET PLATFORM_SUPERADMIN_RESET so a later self-service " +
            "password change is never overwritten on the next deploy.", email, revoked);
    }

    private Task WriteAuditAsync(string email, long adminId, string outcome, long sessionsRevoked, CancellationToken ct)
        => AuditLogSequenceRepair.ExecuteWithSequenceRepairAsync(
            db, "platform_audit_log", "id",
            @"INSERT INTO platform_audit_log (actor_admin_id, actor_email, actor_role, action, entity_type, entity_id, details_json, ip_address)
              VALUES (NULL, @email, 'system', 'platform.superadmin.break_glass_reset', 'PlatformAdmin', @id, CAST(@details AS JSONB), 'system')",
            c =>
            {
                c.Parameters.AddWithValue("@email", email);
                c.Parameters.AddWithValue("@id", adminId);
                c.Parameters.AddWithValue("@details",
                    System.Text.Json.JsonSerializer.Serialize(new { reason = "PLATFORM_SUPERADMIN_RESET", outcome, sessionsRevoked }));
            }, ct);

    // ── Pure decision logic (no DB / no env) — unit-tested directly ──────────────

    /// <summary>The reset is armed only when the flag is an explicit truthy token.</summary>
    internal static bool IsTruthy(string? v) =>
        v is not null && (v.Equals("true", StringComparison.OrdinalIgnoreCase)
                          || v.Equals("1", StringComparison.Ordinal)
                          || v.Equals("yes", StringComparison.OrdinalIgnoreCase));

    /// <summary>Bootstrap password floor — mirrors the accept-invite policy (≥12 chars, letter + digit).</summary>
    internal static bool MeetsPasswordPolicy(string? password) =>
        !string.IsNullOrEmpty(password)
        && password.Length >= 12
        && password.Any(char.IsLetter)
        && password.Any(char.IsDigit);

    /// <summary>
    /// The idempotent core: an existing bootstrap admin needs reconciling ONLY when it has
    /// drifted from the intended state — the env password no longer verifies, the account is
    /// not Active, it is not the super-admin role, or an invite is still pending. A healthy,
    /// in-sync account returns false so an armed flag is a harmless no-op on every boot.
    /// </summary>
    internal static bool NeedsReconcile(bool passwordMatches, string? status, string? roleKey, bool hasPendingInvite) =>
        !(passwordMatches
          && string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase)
          && string.Equals(roleKey, SuperAdminRoleKey, StringComparison.OrdinalIgnoreCase)
          && !hasPendingInvite);
}
