using System.Security.Cryptography;
using System.Text;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

/// <summary>
/// Keeps the bootstrap Platform Super Admin identity in sync with the credential the
/// operator declares in env (<c>PLATFORM_SUPERADMIN_EMAIL</c> /
/// <c>PLATFORM_SUPERADMIN_PASSWORD</c>), so the control plane can never be permanently
/// locked out.
///
/// WHY THIS EXISTS — the P0 it closes:
/// <see cref="PlatformSchemaService"/>.SeedSuperAdminAsync writes the platform super
/// admin's password to the DB EXACTLY ONCE — the first boot when <c>platform_admins</c>
/// is empty (<c>if (anyAdmin &gt; 0) return;</c>). After that the env var is never read
/// again, so rotating <c>PLATFORM_SUPERADMIN_PASSWORD</c> in Render has ZERO effect on the
/// stored hash. Worse, in production (RLS enforced, restricted <c>opstrax_app</c> role) the
/// entire schema-init block — including that seed AND the platform role seed — is SKIPPED,
/// so the bootstrap identity may never have been minted on the deployed environment at all.
/// Either way <c>/api/platform/auth/login</c> compares the entered password against a
/// stale/absent hash and returns the deliberately generic "Invalid credentials" — the exact
/// "the credentials in Render are the same but login is rejected" symptom.
///
/// HOW IT RESOLVES THAT PERMANENTLY — env is declarative desired state, reconciled on EVERY
/// boot with no flag to arm and no flag to remember to disarm:
///  • The account is CREATED when the declared email is absent from the roster (including
///    the production case where the one-time seed never ran).
///  • Login-blocking drift (not Active, still holding an invite token, missing/incorrect
///    role) is REPAIRED whenever it is seen.
///  • The password is applied from env when — and only when — the declared credential is not
///    already the one in force. That decision is driven by a stored credential fingerprint
///    (see <see cref="CredentialFingerprint"/>), which is what lets this run unconditionally:
///    rotating the env credential reaches the DB on the next deploy, while a later
///    self-service password change (env untouched) is NOT stomped on every redeploy.
///
/// DESIGN NOTES (mirrors <see cref="RolePermissionReconciler"/>):
///  • Runs as DML (no DDL) on EVERY boot, deliberately OUTSIDE the schema-init gate, so it
///    executes even when schema init is skipped (restricted role under RLS enforcement, i.e.
///    production). Executes under a SYSTEM scope because the tenant runtime role is walled off
///    from the control-plane tables entirely — <c>platform_admins</c> is reachable only via the
///    separately-authenticated <c>opstrax_system</c> identity (the same identity every
///    <c>/api/platform/*</c> request already runs under).
///  • IDEMPOTENT: a healthy, in-sync account is a no-op — no writes, no session churn, one
///    debug line per boot.
///  • <c>PLATFORM_SUPERADMIN_RESET=true</c> remains as a FORCE override for the one case the
///    fingerprint cannot cover: the env credential is unchanged but the stored password was
///    changed in-app and forgotten. It re-applies env even when the fingerprint matches.
///  • <c>PLATFORM_SUPERADMIN_SYNC=off</c> opts out entirely, for a deployment that wants the
///    DB to be the sole owner of the bootstrap password.
///  • FAIL-OPEN for availability: any error is logged loudly and swallowed. A failure here
///    leaves the account exactly as it was; it never takes the API down.
/// </summary>
public sealed class PlatformSuperAdminReconciler(Database db, ILogger<PlatformSuperAdminReconciler> logger)
{
    private const string SuperAdminRoleKey = "platform_super_admin";

    /// <summary>Audit action carrying the credential fingerprint of the last env sync.</summary>
    internal const string EnvSyncAction = "platform.superadmin.env_sync";

    // Same cost factor as the stored password hash, so the fingerprint we persist is no
    // easier to attack than the account hash itself.
    private const int FingerprintIterations = 100_000;

    public async Task ReconcileAsync(CancellationToken ct = default)
    {
        if (IsDisabled(Environment.GetEnvironmentVariable("PLATFORM_SUPERADMIN_SYNC")))
        {
            logger.LogInformation(new EventId(0, "platform_superadmin_sync_disabled"),
                "PLATFORM_SUPERADMIN_SYNC is off — the bootstrap Platform Super Admin is not reconciled from env. " +
                "The stored credential is authoritative; recovery requires re-enabling the sync.");
            return;
        }

        var email = Environment.GetEnvironmentVariable("PLATFORM_SUPERADMIN_EMAIL")?.Trim();
        var password = Environment.GetEnvironmentVariable("PLATFORM_SUPERADMIN_PASSWORD");
        var forced = IsTruthy(Environment.GetEnvironmentVariable("PLATFORM_SUPERADMIN_RESET"));

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning(new EventId(0, "platform_superadmin_env_incomplete"),
                "PLATFORM_SUPERADMIN_EMAIL/PLATFORM_SUPERADMIN_PASSWORD are not both set — the bootstrap Platform " +
                "Super Admin cannot be reconciled. If Platform Admin sign-in is rejecting valid-looking credentials, " +
                "set both and redeploy; the account is then created or repaired automatically on boot.");
            return;
        }

        // Same floor the accept-invite path enforces — never install a weak bootstrap password.
        if (!MeetsPasswordPolicy(password))
        {
            logger.LogWarning(new EventId(0, "platform_superadmin_password_weak"),
                "PLATFORM_SUPERADMIN_PASSWORD does not meet the policy (≥12 characters, at least one letter and " +
                "one digit) — skipping the bootstrap Super Admin reconcile. Strengthen it and redeploy.");
            return;
        }

        try
        {
            await db.RunInSystemScopeAsync(() => ReconcileCoreAsync(email!, password!, forced, ct), ct);
        }
        catch (Exception ex)
        {
            logger.LogError(new EventId(0, "platform_superadmin_sync_failed"), ex,
                "Platform Super Admin reconcile FAILED for {Email}. The account is unchanged; " +
                "API is still serving. Verify the system database connection and platform_admins access.", email);
        }
    }

    private async Task ReconcileCoreAsync(string email, string password, bool forced, CancellationToken ct)
    {
        // Production skips schema init, so the platform RBAC rows may never have been seeded
        // on the deployed database. Without the super-admin role a minted admin would carry no
        // permissions and every /api/platform/* call would 403 — a different flavour of the same
        // lockout. Insert-if-absent is pure DML and safe to repeat.
        var superRoleId = await EnsureSuperAdminRoleAsync(ct);

        var existing = await db.QuerySingleAsync(
            @"SELECT a.id, a.password_hash, a.status, a.invite_token_hash, COALESCE(r.role_key,'') AS role_key
              FROM platform_admins a LEFT JOIN platform_roles r ON r.id = a.role_id
              WHERE LOWER(a.email)=LOWER(@e) LIMIT 1",
            c => c.Parameters.AddWithValue("@e", email), ct);

        var fingerprint = CredentialFingerprint(email, password);

        // ── Recovery from an empty/rotated identity: the declared email isn't in the roster ──
        if (existing is null)
        {
            if (superRoleId <= 0)
            {
                logger.LogError(new EventId(0, "platform_superadmin_role_missing"),
                    "Bootstrap Super Admin {Email} is absent and the '{Role}' role could not be created — " +
                    "cannot mint a functional super admin. Check that the system identity can write platform_roles.",
                    email, SuperAdminRoleKey);
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

            await RecordEnvSyncAsync(email, newId, "created", fingerprint, 0, ct);
            logger.LogWarning(new EventId(0, "platform_superadmin_created"),
                "Bootstrap Super Admin {Email} was ABSENT from platform_admins and has been created (Active, {Role}) " +
                "from PLATFORM_SUPERADMIN_EMAIL/PASSWORD. Platform Admin sign-in now accepts that credential.",
                email, SuperAdminRoleKey);
            return;
        }

        var adminId = Convert.ToInt64(existing["id"]);
        var status = existing["status"]?.ToString() ?? "";
        var roleKey = existing["roleKey"]?.ToString() ?? "";
        var hasPendingInvite = !string.IsNullOrWhiteSpace(existing["inviteTokenHash"]?.ToString());
        var passwordMatches = PlatformEndpoints.VerifyPassword(password, existing["passwordHash"]?.ToString());

        var recorded = await ReadRecordedFingerprintAsync(email, ct);
        var applyPassword = ShouldApplyPassword(forced, recorded, fingerprint, passwordMatches);
        var repairAccount = NeedsAccountRepair(status, roleKey, hasPendingInvite);

        if (!applyPassword && !repairAccount)
        {
            // In sync. Record the fingerprint the first time we observe an already-correct
            // credential so a later env rotation is detectable as a CHANGE rather than as an
            // unknown state.
            if (string.IsNullOrEmpty(recorded))
                await RecordEnvSyncAsync(email, adminId, "adopted", fingerprint, 0, ct);

            logger.LogInformation(new EventId(0, "platform_superadmin_in_sync"),
                "Bootstrap Super Admin {Email} matches the env credential and is a healthy, Active Super Admin — " +
                "no changes.", email);
            return;
        }

        if (applyPassword)
        {
            // role_id only changes when the role actually resolves; never null out a good assignment.
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

            var reason = forced ? "forced" : string.IsNullOrEmpty(recorded) ? "password_out_of_sync" : "env_credential_rotated";
            await RecordEnvSyncAsync(email, adminId, reason, fingerprint, revoked, ct);
            logger.LogWarning(new EventId(0, "platform_superadmin_password_applied"),
                "Bootstrap Super Admin {Email}: password reconciled from env ({Reason}), account set Active, " +
                "{Revoked} session(s) revoked. Sign in with PLATFORM_SUPERADMIN_PASSWORD.", email, reason, revoked);
            return;
        }

        // Password is owned by a self-service change; repair only what blocks sign-in.
        await db.ExecuteAsync(
            @"UPDATE platform_admins
              SET status            = 'Active',
                  invite_token_hash = NULL,
                  invite_expires_at = NULL,
                  role_id           = COALESCE(NULLIF(@r, 0), role_id),
                  updated_at        = NOW()
              WHERE id = @id",
            c =>
            {
                c.Parameters.AddWithValue("@r", superRoleId);
                c.Parameters.AddWithValue("@id", adminId);
            }, ct);

        await RecordEnvSyncAsync(email, adminId, "account_repaired", fingerprint, 0, ct);
        logger.LogWarning(new EventId(0, "platform_superadmin_account_repaired"),
            "Bootstrap Super Admin {Email}: account state repaired (was status={Status}, role='{Role}', " +
            "pendingInvite={Invite}) — set Active as {Super} with no password change, because the stored password " +
            "is a later self-service change and env is unrotated.",
            email, status, roleKey, hasPendingInvite, SuperAdminRoleKey);
    }

    /// <summary>
    /// Insert-if-absent for the super-admin role and its wildcard grant. Pure DML, so it works
    /// on a production database whose schema-init (and therefore role seed) was skipped.
    /// </summary>
    private async Task<long> EnsureSuperAdminRoleAsync(CancellationToken ct)
    {
        await db.ExecuteAsync(
            @"INSERT INTO platform_roles (role_key, name, description)
              VALUES (@k, 'Platform Super Admin', 'Full control of the SaaS business across all tenants.')
              ON CONFLICT (role_key) DO NOTHING",
            c => c.Parameters.AddWithValue("@k", SuperAdminRoleKey), ct);

        var roleId = await db.ScalarLongAsync(
            "SELECT COALESCE((SELECT id FROM platform_roles WHERE role_key=@k), 0)",
            c => c.Parameters.AddWithValue("@k", SuperAdminRoleKey), ct);

        if (roleId > 0)
        {
            await db.ExecuteAsync(
                @"INSERT INTO platform_role_permissions (role_id, permission_key)
                  VALUES (@r, 'platform:*')
                  ON CONFLICT (role_id, permission_key) DO NOTHING",
                c => c.Parameters.AddWithValue("@r", roleId), ct);
        }

        return roleId;
    }

    /// <summary>
    /// The fingerprint of the env credential at the last sync, or null when this deployment has
    /// never recorded one. Stored in the audit log rather than a new column so the reconciler
    /// stays DDL-free and every sync remains visible in Security &amp; Audit.
    /// </summary>
    private async Task<string?> ReadRecordedFingerprintAsync(string email, CancellationToken ct)
    {
        var row = await db.QuerySingleAsync(
            @"SELECT details_json->>'credentialFingerprint' AS fingerprint
              FROM platform_audit_log
              WHERE action=@a AND LOWER(actor_email)=LOWER(@e)
                AND details_json->>'credentialFingerprint' IS NOT NULL
              ORDER BY id DESC LIMIT 1",
            c =>
            {
                c.Parameters.AddWithValue("@a", EnvSyncAction);
                c.Parameters.AddWithValue("@e", email);
            }, ct);
        var value = row?["fingerprint"]?.ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private Task RecordEnvSyncAsync(string email, long adminId, string outcome, string fingerprint, long sessionsRevoked, CancellationToken ct)
        => AuditLogSequenceRepair.ExecuteWithSequenceRepairAsync(
            db, "platform_audit_log", "id",
            @"INSERT INTO platform_audit_log (actor_admin_id, actor_email, actor_role, action, entity_type, entity_id, details_json, ip_address)
              VALUES (NULL, @email, 'system', @action, 'PlatformAdmin', @id, CAST(@details AS JSONB), 'system')",
            c =>
            {
                c.Parameters.AddWithValue("@email", email);
                c.Parameters.AddWithValue("@action", EnvSyncAction);
                c.Parameters.AddWithValue("@id", adminId);
                c.Parameters.AddWithValue("@details",
                    System.Text.Json.JsonSerializer.Serialize(new
                    {
                        source = "PLATFORM_SUPERADMIN_EMAIL/PASSWORD",
                        outcome,
                        sessionsRevoked,
                        credentialFingerprint = fingerprint,
                    }));
            }, ct);

    // ── Pure decision logic (no DB / no env) — unit-tested directly ──────────────

    /// <summary>The force override is armed only when the flag is an explicit truthy token.</summary>
    internal static bool IsTruthy(string? v) =>
        v is not null && (v.Equals("true", StringComparison.OrdinalIgnoreCase)
                          || v.Equals("1", StringComparison.Ordinal)
                          || v.Equals("yes", StringComparison.OrdinalIgnoreCase));

    /// <summary>Explicit opt-out of env reconciliation entirely (DB owns the credential).</summary>
    internal static bool IsDisabled(string? v) =>
        v is not null && (v.Equals("off", StringComparison.OrdinalIgnoreCase)
                          || v.Equals("false", StringComparison.OrdinalIgnoreCase)
                          || v.Equals("0", StringComparison.Ordinal)
                          || v.Equals("no", StringComparison.OrdinalIgnoreCase)
                          || v.Equals("never", StringComparison.OrdinalIgnoreCase));

    /// <summary>Bootstrap password floor — mirrors the accept-invite policy (≥12 chars, letter + digit).</summary>
    internal static bool MeetsPasswordPolicy(string? password) =>
        !string.IsNullOrEmpty(password)
        && password.Length >= 12
        && password.Any(char.IsLetter)
        && password.Any(char.IsDigit);

    /// <summary>
    /// Whether to write the env password into the DB.
    ///
    ///  • forced                                    → always (the operator asked for it).
    ///  • no fingerprint recorded yet AND the env
    ///    password does not verify                  → yes. This is the lockout: the credential in
    ///                                                env has never reached this database.
    ///  • no fingerprint recorded yet AND it does
    ///    verify                                    → no. Already in force; just adopt it.
    ///  • fingerprint differs from env              → yes. The operator rotated env; make it real.
    ///  • fingerprint matches env                   → no. Env is unchanged since the last sync, so a
    ///                                                stored password that no longer matches is a
    ///                                                deliberate self-service change, not drift.
    /// </summary>
    internal static bool ShouldApplyPassword(bool forced, string? recordedFingerprint, string envFingerprint, bool passwordMatches)
    {
        if (forced) return true;
        if (string.IsNullOrEmpty(recordedFingerprint)) return !passwordMatches;
        return !FingerprintEquals(recordedFingerprint, envFingerprint);
    }

    /// <summary>
    /// Account state that BLOCKS sign-in regardless of the password: not Active, an invite still
    /// outstanding, or not carrying the super-admin role.
    /// </summary>
    internal static bool NeedsAccountRepair(string? status, string? roleKey, bool hasPendingInvite) =>
        !string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase)
        || !string.Equals(roleKey, SuperAdminRoleKey, StringComparison.OrdinalIgnoreCase)
        || hasPendingInvite;

    /// <summary>
    /// The full drift predicate: an existing bootstrap admin is in sync only when the env password
    /// verifies AND nothing about the account blocks sign-in.
    /// </summary>
    internal static bool NeedsReconcile(bool passwordMatches, string? status, string? roleKey, bool hasPendingInvite) =>
        !passwordMatches || NeedsAccountRepair(status, roleKey, hasPendingInvite);

    /// <summary>
    /// A deterministic, expensive-to-invert fingerprint of the declared credential, used purely to
    /// detect "did the operator change PLATFORM_SUPERADMIN_* since the last sync?". Salted with the
    /// email and run through the same PBKDF2 cost as the account password hash, so persisting it
    /// alongside the audit trail discloses no more than the stored hash already does.
    /// </summary>
    internal static string CredentialFingerprint(string email, string password)
    {
        var salt = SHA256.HashData(Encoding.UTF8.GetBytes(
            "opstrax.platform.superadmin\n" + (email ?? "").Trim().ToLowerInvariant()));
        var key = Rfc2898DeriveBytes.Pbkdf2(password ?? "", salt, FingerprintIterations, HashAlgorithmName.SHA256, 32);
        return Convert.ToBase64String(key);
    }

    /// <summary>Constant-time comparison — the fingerprint is credential-derived material.</summary>
    internal static bool FingerprintEquals(string? a, string? b)
    {
        if (a is null || b is null) return false;
        var left = Encoding.UTF8.GetBytes(a);
        var right = Encoding.UTF8.GetBytes(b);
        return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
    }
}
