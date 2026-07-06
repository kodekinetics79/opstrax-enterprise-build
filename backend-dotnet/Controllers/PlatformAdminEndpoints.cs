using System.Security.Cryptography;
using System.Text;
using Opstrax.Api.Data;
using Opstrax.Api.DTOs;
using Opstrax.Api.Security;
using Opstrax.Api.Services;

namespace Opstrax.Api.Controllers;

// ─────────────────────────────────────────────────────────────────────────────
// PLATFORM OPERATOR MANAGEMENT — /api/platform/admins
//
// Closes the "platform admins are seed-only" gap: the full operator lifecycle
// (invite → password setup → role changes → disable/enable → session revocation)
// is managed through the platform API, so no direct database access is needed
// after first bootstrap.
//
// Security model:
//   • Every management endpoint runs behind the platform bearer guard
//     (PlatformEndpoints.RequireAsync) — tenant sessions can never reach it.
//   • platform:admins:view   — list operators (Compliance Admin + Super Admin).
//   • platform:admins:manage — mutate operators (Super Admin only via platform:*;
//     deliberately granted to no other seeded role).
//   • Escalation fence: only a Platform Super Admin may create/modify/disable/
//     reset another Super Admin or assign the platform_super_admin role.
//   • The LAST active Super Admin can never be disabled or demoted.
//   • Invite tokens: 32 random bytes, returned ONCE at issuance; only the
//     SHA-256 hash is stored. Accepting an invite revokes existing sessions.
//   • No open signup: accept-invite only completes an operator explicitly
//     invited here, and is lockout-limited like platform login.
//   • Every mutation writes platform_audit_log via the shared audit writer.
// ─────────────────────────────────────────────────────────────────────────────
public static class PlatformAdminEndpoints
{
    private const string SuperAdminRoleKey = "platform_super_admin";
    private static readonly TimeSpan InviteLifetime = TimeSpan.FromDays(7);

    // ONE canonical route shape (the H5 spec contract). The handler methods for
    // role/status remain internal — PATCH and disable/enable delegate to them.
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/platform/admins", ListAdmins);
        app.MapPost("/api/platform/admins/invite", CreateAdmin);
        app.MapPatch("/api/platform/admins/{id:long}", PatchAdmin);
        app.MapPost("/api/platform/admins/{id:long}/disable",
            (HttpContext http, long id, Database db, CancellationToken ct) => SetStatus(http, id, new SetStatusRequest("Disabled"), db, ct));
        app.MapPost("/api/platform/admins/{id:long}/enable",
            (HttpContext http, long id, Database db, CancellationToken ct) => SetStatus(http, id, new SetStatusRequest("Active"), db, ct));
        app.MapPost("/api/platform/admins/{id:long}/revoke-sessions", RevokeSessions);
        app.MapPost("/api/platform/admins/{id:long}/reset-invite", ResetInvite);
        app.MapPost("/api/platform/admins/{id:long}/mfa/reset", ResetMfa);

        // Anonymous by necessity (the operator has no session yet) — token-gated,
        // lockout-limited, audited.
        app.MapPost("/api/platform/auth/accept-invite", AcceptInvite);

        // Self-service MFA enrollment (authenticated platform principal).
        app.MapPost("/api/platform/auth/mfa/enroll", MfaEnroll);
        app.MapPost("/api/platform/auth/mfa/verify", MfaVerify);
    }

    internal sealed record CreateAdminRequest(string? Email, string? FullName, string? RoleKey);
    internal sealed record AssignRoleRequest(string? RoleKey);
    internal sealed record SetStatusRequest(string? Status);
    internal sealed record PatchAdminRequest(string? RoleKey, string? FullName);
    internal sealed record AcceptInviteRequest(string? Email, string? Token, string? Password);
    internal sealed record MfaVerifyRequest(string? Code);

    // ── GET /api/platform/admins ─────────────────────────────────────────────
    internal static async Task<IResult> ListAdmins(HttpContext http, Database db, CancellationToken ct)
    {
        var (principal, error) = await PlatformEndpoints.RequireAsync(http, db, "platform:admins:view", ct);
        if (error is not null) return error;

        // Never selects password_hash / invite_token_hash.
        var rows = await db.QueryAsync(
            @"SELECT a.id, a.email, a.full_name, a.status, a.mfa_enabled, a.last_login_at, a.created_at,
                     a.invite_expires_at,
                     (a.invite_token_hash IS NOT NULL) AS invite_pending,
                     (a.password_hash IS NOT NULL)     AS password_set,
                     r.role_key, r.name AS role_name,
                     (SELECT COUNT(*) FROM platform_sessions s
                       WHERE s.admin_id = a.id AND s.expires_at > NOW()) AS active_sessions
              FROM platform_admins a
              LEFT JOIN platform_roles r ON r.id = a.role_id
              ORDER BY a.created_at", null, ct);
        return Results.Ok(ApiResponse<object>.Ok(rows, "Platform admins"));
    }

    // ── POST /api/platform/admins  (invite) ──────────────────────────────────
    internal static async Task<IResult> CreateAdmin(HttpContext http, CreateAdminRequest request, Database db, CancellationToken ct)
    {
        var (principal, error) = await PlatformEndpoints.RequireAsync(http, db, "platform:admins:manage", ct);
        if (error is not null) return error;

        var email = (request.Email ?? "").Trim().ToLowerInvariant();
        var fullName = (request.FullName ?? "").Trim();
        var roleKey = (request.RoleKey ?? "").Trim();

        var errors = new List<string>();
        if (!System.Net.Mail.MailAddress.TryCreate(email, out _)) errors.Add("A valid email is required.");
        if (fullName.Length is < 2 or > 160) errors.Add("Full name is required (2–160 characters).");
        if (string.IsNullOrWhiteSpace(roleKey)) errors.Add("roleKey is required.");
        if (errors.Count > 0) return Results.BadRequest(ApiResponse<object>.Fail("Validation failed", errors.ToArray()));

        var roleId = await db.ScalarLongAsync("SELECT COALESCE((SELECT id FROM platform_roles WHERE role_key=@k), 0)",
            c => c.Parameters.AddWithValue("@k", roleKey), ct);
        if (roleId <= 0) return Results.BadRequest(ApiResponse<object>.Fail("Validation failed", ["Unknown platform role."]));

        if (IsSuperRole(roleKey) && !IsSuperAdmin(principal!))
        {
            await PlatformEndpoints.AuditAsync(db, principal!, http, "platform.admin.escalation_denied", "PlatformAdmin", null, null,
                new { attempted = "create_super_admin", email }, ct);
            return Results.Json(ApiResponse<object>.Fail("Forbidden", "Only a Platform Super Admin can create a Super Admin"),
                statusCode: StatusCodes.Status403Forbidden);
        }

        var exists = await db.ScalarLongAsync("SELECT COUNT(*) FROM platform_admins WHERE email=@e",
            c => c.Parameters.AddWithValue("@e", email), ct);
        if (exists > 0) return Results.Conflict(ApiResponse<object>.Fail("An operator with this email already exists"));

        var (rawToken, tokenHash) = NewInviteToken();
        var adminId = await db.InsertAsync(
            @"INSERT INTO platform_admins (email, full_name, password_hash, role_id, status, invite_token_hash, invite_expires_at)
              VALUES (@e, @n, NULL, @r, 'Invited', @th, NOW() + @ttl)
              RETURNING id",
            c =>
            {
                c.Parameters.AddWithValue("@e", email);
                c.Parameters.AddWithValue("@n", fullName);
                c.Parameters.AddWithValue("@r", roleId);
                c.Parameters.AddWithValue("@th", tokenHash);
                c.Parameters.AddWithValue("@ttl", InviteLifetime);
            }, ct);

        // Best-effort email delivery (SMTP_* env). The one-time link remains the
        // canonical artifact either way; the audit row records whether the email
        // went out. The token itself is NEVER written to the audit log.
        var emailSent = await TrySendInviteEmailAsync(http, email, fullName, rawToken, "You have been invited as an OpsTrax platform operator.", ct);

        await PlatformEndpoints.AuditAsync(db, principal!, http, "platform.admin.created", "PlatformAdmin", adminId, null,
            new { email, roleKey, emailSent }, ct);

        // The raw invite token is returned exactly ONCE (same contract as device
        // provisioning): the caller relays it to the operator out-of-band. It is
        // never persisted and never appears in list/detail/audit responses.
        return Results.Created($"/api/platform/admins/{adminId}", ApiResponse<object>.Ok(new
        {
            id = adminId,
            email,
            fullName,
            roleKey,
            status = "Invited",
            inviteToken = rawToken,
            inviteExpiresAt = DateTimeOffset.UtcNow.Add(InviteLifetime),
            emailSent,
        }, "Operator invited — deliver the invite token securely; it is shown only once"));
    }

    // Builds the accept-invite link from the caller's Origin (the platform SPA)
    // or PLATFORM_PUBLIC_URL, and sends it via PlatformMailService when SMTP is
    // configured. Returns false when email is unconfigured/unavailable.
    private static async Task<bool> TrySendInviteEmailAsync(
        HttpContext http, string email, string fullName, string rawToken, string intro, CancellationToken ct)
    {
        var origin = http.Request.Headers.Origin.ToString();
        var baseUrl = !string.IsNullOrWhiteSpace(origin) && origin.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? origin.TrimEnd('/')
            : Environment.GetEnvironmentVariable("PLATFORM_PUBLIC_URL")?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl)) return false;

        var link = $"{baseUrl}/platform/accept-invite?email={Uri.EscapeDataString(email)}&token={rawToken}";
        return await PlatformMailService.TrySendAsync(
            email,
            "OpsTrax Platform — operator access setup",
            $"""
            Hello {fullName},

            {intro}

            Set your password using this single-use link (valid for 7 days):
            {link}

            If you did not expect this invitation, ignore this email and report it
            to your platform administrator.
            """,
            ct);
    }

    // ── POST /api/platform/admins/{id}/role ──────────────────────────────────
    internal static async Task<IResult> AssignRole(HttpContext http, long id, AssignRoleRequest request, Database db, CancellationToken ct)
    {
        var (principal, error) = await PlatformEndpoints.RequireAsync(http, db, "platform:admins:manage", ct);
        if (error is not null) return error;

        var roleKey = (request.RoleKey ?? "").Trim();
        if (string.IsNullOrWhiteSpace(roleKey))
            return Results.BadRequest(ApiResponse<object>.Fail("Validation failed", ["roleKey is required."]));

        var target = await LoadAdminAsync(db, id, ct);
        if (target is null) return Results.NotFound(ApiResponse<object>.Fail("Operator not found"));

        var newRoleId = await db.ScalarLongAsync("SELECT COALESCE((SELECT id FROM platform_roles WHERE role_key=@k), 0)",
            c => c.Parameters.AddWithValue("@k", roleKey), ct);
        if (newRoleId <= 0) return Results.BadRequest(ApiResponse<object>.Fail("Validation failed", ["Unknown platform role."]));

        // Escalation fence: touching a Super Admin, or granting the Super Admin
        // role, requires the actor to be a Super Admin.
        if ((IsSuperRole(target.RoleKey) || IsSuperRole(roleKey)) && !IsSuperAdmin(principal!))
        {
            await PlatformEndpoints.AuditAsync(db, principal!, http, "platform.admin.escalation_denied", "PlatformAdmin", id, null,
                new { attempted = "role_change", from = target.RoleKey, to = roleKey }, ct);
            return Results.Json(ApiResponse<object>.Fail("Forbidden", "Only a Platform Super Admin can change Super Admin assignments"),
                statusCode: StatusCodes.Status403Forbidden);
        }

        // Last-Super-Admin fence: demoting the only active Super Admin would
        // permanently lock the control plane.
        if (IsSuperRole(target.RoleKey) && !IsSuperRole(roleKey) &&
            target.Status == "Active" && await CountActiveSuperAdminsAsync(db, ct) <= 1)
        {
            return Results.Conflict(ApiResponse<object>.Fail("Cannot demote the last active Platform Super Admin"));
        }

        var roleActuallyChanged = !string.Equals(target.RoleKey, roleKey, StringComparison.OrdinalIgnoreCase);
        await db.ExecuteAsync("UPDATE platform_admins SET role_id=@r, updated_at=NOW() WHERE id=@id",
            c => { c.Parameters.AddWithValue("@r", newRoleId); c.Parameters.AddWithValue("@id", id); }, ct);

        // A role change (promotion or demotion) revokes live sessions so the
        // operator re-authenticates into the new privilege set — no session may
        // outlive the privileges it was minted under.
        var revoked = 0L;
        if (roleActuallyChanged)
        {
            revoked = await db.ScalarLongAsync(
                "WITH gone AS (DELETE FROM platform_sessions WHERE admin_id=@id RETURNING 1) SELECT COUNT(*) FROM gone",
                c => c.Parameters.AddWithValue("@id", id), ct);
        }

        await PlatformEndpoints.AuditAsync(db, principal!, http, "platform.admin.role_changed", "PlatformAdmin", id, null,
            new { from = target.RoleKey, to = roleKey, sessionsRevoked = revoked }, ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id, roleKey, sessionsRevoked = revoked }, "Role updated"));
    }

    // ── PATCH /api/platform/admins/{id}  {roleKey?, fullName?} ───────────────
    // Spec-shaped partial update. Role changes delegate to AssignRole (all
    // fences + session revocation apply); name edits carry the same Super
    // Admin fence and are audited separately.
    internal static async Task<IResult> PatchAdmin(HttpContext http, long id, PatchAdminRequest request, Database db, CancellationToken ct)
    {
        var (principal, error) = await PlatformEndpoints.RequireAsync(http, db, "platform:admins:manage", ct);
        if (error is not null) return error;

        var fullName = request.FullName?.Trim();
        var roleKey = request.RoleKey?.Trim();
        if (string.IsNullOrWhiteSpace(fullName) && string.IsNullOrWhiteSpace(roleKey))
            return Results.BadRequest(ApiResponse<object>.Fail("Validation failed", ["Provide roleKey and/or fullName."]));

        if (!string.IsNullOrWhiteSpace(fullName))
        {
            if (fullName.Length is < 2 or > 160)
                return Results.BadRequest(ApiResponse<object>.Fail("Validation failed", ["Full name must be 2–160 characters."]));

            var target = await LoadAdminAsync(db, id, ct);
            if (target is null) return Results.NotFound(ApiResponse<object>.Fail("Operator not found"));
            if (IsSuperRole(target.RoleKey) && !IsSuperAdmin(principal!))
            {
                await PlatformEndpoints.AuditAsync(db, principal!, http, "platform.admin.escalation_denied", "PlatformAdmin", id, null,
                    new { attempted = "rename" }, ct);
                return Results.Json(ApiResponse<object>.Fail("Forbidden", "Only a Platform Super Admin can modify a Super Admin"),
                    statusCode: StatusCodes.Status403Forbidden);
            }

            await db.ExecuteAsync("UPDATE platform_admins SET full_name=@n, updated_at=NOW() WHERE id=@id",
                c => { c.Parameters.AddWithValue("@n", fullName); c.Parameters.AddWithValue("@id", id); }, ct);
            await PlatformEndpoints.AuditAsync(db, principal!, http, "platform.admin.updated", "PlatformAdmin", id, null,
                new { fullName }, ct);
        }

        if (!string.IsNullOrWhiteSpace(roleKey))
            return await AssignRole(http, id, new AssignRoleRequest(roleKey), db, ct);

        return Results.Ok(ApiResponse<object>.Ok(new { id, fullName }, "Operator updated"));
    }

    // ── POST /api/platform/admins/{id}/status  {status: Active|Disabled} ─────
    internal static async Task<IResult> SetStatus(HttpContext http, long id, SetStatusRequest request, Database db, CancellationToken ct)
    {
        var (principal, error) = await PlatformEndpoints.RequireAsync(http, db, "platform:admins:manage", ct);
        if (error is not null) return error;

        var status = (request.Status ?? "").Trim();
        if (status is not ("Active" or "Disabled"))
            return Results.BadRequest(ApiResponse<object>.Fail("Validation failed", ["status must be 'Active' or 'Disabled'."]));

        var target = await LoadAdminAsync(db, id, ct);
        if (target is null) return Results.NotFound(ApiResponse<object>.Fail("Operator not found"));

        if (status == "Disabled" && id == principal!.AdminId)
            return Results.BadRequest(ApiResponse<object>.Fail("You cannot disable your own account"));

        if (IsSuperRole(target.RoleKey) && !IsSuperAdmin(principal!))
        {
            await PlatformEndpoints.AuditAsync(db, principal!, http, "platform.admin.escalation_denied", "PlatformAdmin", id, null,
                new { attempted = "status_change", to = status }, ct);
            return Results.Json(ApiResponse<object>.Fail("Forbidden", "Only a Platform Super Admin can change a Super Admin's status"),
                statusCode: StatusCodes.Status403Forbidden);
        }

        if (status == "Disabled" && IsSuperRole(target.RoleKey) &&
            target.Status == "Active" && await CountActiveSuperAdminsAsync(db, ct) <= 1)
        {
            return Results.Conflict(ApiResponse<object>.Fail("Cannot disable the last active Platform Super Admin"));
        }

        await db.ExecuteAsync("UPDATE platform_admins SET status=@s, updated_at=NOW() WHERE id=@id",
            c => { c.Parameters.AddWithValue("@s", status); c.Parameters.AddWithValue("@id", id); }, ct);

        var revoked = 0L;
        if (status == "Disabled")
        {
            // Kill live access immediately; AuthenticateAsync also rejects
            // non-Active admins, so this is defense in depth.
            revoked = await db.ScalarLongAsync(
                "WITH gone AS (DELETE FROM platform_sessions WHERE admin_id=@id RETURNING 1) SELECT COUNT(*) FROM gone",
                c => c.Parameters.AddWithValue("@id", id), ct);
        }

        await PlatformEndpoints.AuditAsync(db, principal!, http,
            status == "Disabled" ? "platform.admin.disabled" : "platform.admin.enabled",
            "PlatformAdmin", id, null, new { sessionsRevoked = revoked }, ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id, status, sessionsRevoked = revoked },
            status == "Disabled" ? "Operator disabled" : "Operator re-enabled"));
    }

    // ── POST /api/platform/admins/{id}/revoke-sessions ───────────────────────
    internal static async Task<IResult> RevokeSessions(HttpContext http, long id, Database db, CancellationToken ct)
    {
        var (principal, error) = await PlatformEndpoints.RequireAsync(http, db, "platform:admins:manage", ct);
        if (error is not null) return error;

        var target = await LoadAdminAsync(db, id, ct);
        if (target is null) return Results.NotFound(ApiResponse<object>.Fail("Operator not found"));

        if (IsSuperRole(target.RoleKey) && !IsSuperAdmin(principal!))
        {
            await PlatformEndpoints.AuditAsync(db, principal!, http, "platform.admin.escalation_denied", "PlatformAdmin", id, null,
                new { attempted = "revoke_sessions" }, ct);
            return Results.Json(ApiResponse<object>.Fail("Forbidden", "Only a Platform Super Admin can revoke a Super Admin's sessions"),
                statusCode: StatusCodes.Status403Forbidden);
        }

        var revoked = await db.ScalarLongAsync(
            "WITH gone AS (DELETE FROM platform_sessions WHERE admin_id=@id RETURNING 1) SELECT COUNT(*) FROM gone",
            c => c.Parameters.AddWithValue("@id", id), ct);

        await PlatformEndpoints.AuditAsync(db, principal!, http, "platform.admin.sessions_revoked", "PlatformAdmin", id, null,
            new { sessionsRevoked = revoked }, ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id, sessionsRevoked = revoked }, "Sessions revoked"));
    }

    // ── POST /api/platform/admins/{id}/reset-invite ──────────────────────────
    // Re-issues the password-setup token (new invite, or password reset for an
    // operator locked out). The existing password stays valid until the new
    // invite is accepted, at which point all sessions are revoked.
    internal static async Task<IResult> ResetInvite(HttpContext http, long id, Database db, CancellationToken ct)
    {
        var (principal, error) = await PlatformEndpoints.RequireAsync(http, db, "platform:admins:manage", ct);
        if (error is not null) return error;

        var target = await LoadAdminAsync(db, id, ct);
        if (target is null) return Results.NotFound(ApiResponse<object>.Fail("Operator not found"));
        if (target.Status == "Disabled")
            return Results.BadRequest(ApiResponse<object>.Fail("Re-enable the operator before resetting their invite"));

        if (IsSuperRole(target.RoleKey) && !IsSuperAdmin(principal!))
        {
            await PlatformEndpoints.AuditAsync(db, principal!, http, "platform.admin.escalation_denied", "PlatformAdmin", id, null,
                new { attempted = "reset_invite" }, ct);
            return Results.Json(ApiResponse<object>.Fail("Forbidden", "Only a Platform Super Admin can reset a Super Admin's invite"),
                statusCode: StatusCodes.Status403Forbidden);
        }

        var (rawToken, tokenHash) = NewInviteToken();
        await db.ExecuteAsync(
            "UPDATE platform_admins SET invite_token_hash=@th, invite_expires_at=NOW() + @ttl, updated_at=NOW() WHERE id=@id",
            c =>
            {
                c.Parameters.AddWithValue("@th", tokenHash);
                c.Parameters.AddWithValue("@ttl", InviteLifetime);
                c.Parameters.AddWithValue("@id", id);
            }, ct);

        var emailSent = await TrySendInviteEmailAsync(http, target.Email, target.Email, rawToken,
            "Your OpsTrax platform password setup link has been reset.", ct);

        await PlatformEndpoints.AuditAsync(db, principal!, http, "platform.admin.invite_reset", "PlatformAdmin", id, null,
            new { emailSent }, ct);
        return Results.Ok(ApiResponse<object>.Ok(new
        {
            id,
            inviteToken = rawToken,
            inviteExpiresAt = DateTimeOffset.UtcNow.Add(InviteLifetime),
            emailSent,
        }, "Invite reset — deliver the token securely; it is shown only once"));
    }

    // ── POST /api/platform/auth/mfa/enroll ───────────────────────────────────
    // Self-service: any authenticated operator starts TOTP enrollment. The
    // secret is returned ONCE (controlled enrollment flow); mfa_enabled flips
    // only after /mfa/verify proves possession of the authenticator.
    internal static async Task<IResult> MfaEnroll(HttpContext http, Database db, CancellationToken ct)
    {
        var (principal, error) = await PlatformEndpoints.RequireAsync(http, db, "platform:dashboard:view", ct);
        if (error is not null) return error;

        var secret = TotpService.GenerateSecret();
        var pii = http.RequestServices.GetRequiredService<Opstrax.Api.Security.PiiProtectionService>();
        var protectedSecret = pii.Encrypt(secret);
        await db.ExecuteAsync(
            "UPDATE platform_admins SET mfa_secret=@s, mfa_enabled=false, updated_at=NOW() WHERE id=@id",
            c => { c.Parameters.AddWithValue("@s", protectedSecret!); c.Parameters.AddWithValue("@id", principal!.AdminId); }, ct);

        await PlatformEndpoints.AuditAsync(db, principal!, http, "platform.admin.mfa_enroll_started", "PlatformAdmin", principal!.AdminId, null, null, ct);
        return Results.Ok(ApiResponse<object>.Ok(new
        {
            secret,
            otpauthUri = TotpService.BuildOtpAuthUri("OpsTrax Platform", principal!.Email, secret),
        }, "Add this secret to your authenticator app, then verify a code to activate MFA"));
    }

    // ── POST /api/platform/auth/mfa/verify {code} ────────────────────────────
    internal static async Task<IResult> MfaVerify(HttpContext http, MfaVerifyRequest request, Database db, CancellationToken ct)
    {
        var (principal, error) = await PlatformEndpoints.RequireAsync(http, db, "platform:dashboard:view", ct);
        if (error is not null) return error;

        var row = await db.QuerySingleAsync("SELECT mfa_secret FROM platform_admins WHERE id=@id",
            c => c.Parameters.AddWithValue("@id", principal!.AdminId), ct);
        var pii = http.RequestServices.GetRequiredService<Opstrax.Api.Security.PiiProtectionService>();
        var secret = pii.Decrypt(row?["mfaSecret"]?.ToString());
        if (string.IsNullOrWhiteSpace(secret))
            return Results.BadRequest(ApiResponse<object>.Fail("No MFA enrollment in progress — call enroll first"));

        if (!TotpService.VerifyCode(secret, request.Code))
        {
            await PlatformEndpoints.AuditAsync(db, principal!, http, "platform.admin.mfa_verify_failed", "PlatformAdmin", principal!.AdminId, null, null, ct);
            return Results.Json(ApiResponse<object>.Fail("Invalid code"), statusCode: StatusCodes.Status401Unauthorized);
        }

        await db.ExecuteAsync("UPDATE platform_admins SET mfa_enabled=true, updated_at=NOW() WHERE id=@id",
            c => c.Parameters.AddWithValue("@id", principal!.AdminId), ct);
        await PlatformEndpoints.AuditAsync(db, principal!, http, "platform.admin.mfa_enabled", "PlatformAdmin", principal!.AdminId, null, null, ct);
        return Results.Ok(ApiResponse<object>.Ok(new { mfaEnabled = true }, "MFA is now required on every sign-in"));
    }

    // ── POST /api/platform/admins/{id}/mfa/reset ─────────────────────────────
    // Recovery for a lost authenticator: clears the second factor and revokes
    // sessions so the account re-authenticates from scratch. Super fence applies.
    internal static async Task<IResult> ResetMfa(HttpContext http, long id, Database db, CancellationToken ct)
    {
        var (principal, error) = await PlatformEndpoints.RequireAsync(http, db, "platform:admins:manage", ct);
        if (error is not null) return error;

        var target = await LoadAdminAsync(db, id, ct);
        if (target is null) return Results.NotFound(ApiResponse<object>.Fail("Operator not found"));

        if (IsSuperRole(target.RoleKey) && !IsSuperAdmin(principal!))
        {
            await PlatformEndpoints.AuditAsync(db, principal!, http, "platform.admin.escalation_denied", "PlatformAdmin", id, null,
                new { attempted = "mfa_reset" }, ct);
            return Results.Json(ApiResponse<object>.Fail("Forbidden", "Only a Platform Super Admin can reset a Super Admin's MFA"),
                statusCode: StatusCodes.Status403Forbidden);
        }

        await db.ExecuteAsync("UPDATE platform_admins SET mfa_secret=NULL, mfa_enabled=false, updated_at=NOW() WHERE id=@id",
            c => c.Parameters.AddWithValue("@id", id), ct);
        var revoked = await db.ScalarLongAsync(
            "WITH gone AS (DELETE FROM platform_sessions WHERE admin_id=@id RETURNING 1) SELECT COUNT(*) FROM gone",
            c => c.Parameters.AddWithValue("@id", id), ct);

        await PlatformEndpoints.AuditAsync(db, principal!, http, "platform.admin.mfa_reset", "PlatformAdmin", id, null,
            new { sessionsRevoked = revoked }, ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id, mfaEnabled = false, sessionsRevoked = revoked }, "MFA reset"));
    }

    // ── POST /api/platform/auth/accept-invite ────────────────────────────────
    // Lockout mirror of platform login: 5 failures per email+IP / 15 minutes,
    // DB-backed via the audit trail so it survives restarts and spans instances.
    internal static async Task<IResult> AcceptInvite(HttpContext http, AcceptInviteRequest request, Database db, CancellationToken ct)
    {
        var email = (request.Email ?? "").Trim().ToLowerInvariant();
        var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        if (await PlatformEndpoints.CountRecentAuthFailuresAsync(
                db, email, ip, "platform.admin.invite_accept_failed", "platform.admin.invite_accepted", ct) >= PlatformEndpoints.MaxFailedLogins)
        {
            return Results.Json(ApiResponse<object>.Fail("Too many failed attempts", "Try again later"),
                statusCode: StatusCodes.Status429TooManyRequests);
        }

        async Task<IResult> FailAsync(string reason)
        {
            await AuditLogSequenceRepair.ExecuteWithSequenceRepairAsync(
                db, "platform_audit_log", "id",
                @"INSERT INTO platform_audit_log (actor_admin_id, actor_email, actor_role, action, entity_type, details_json, ip_address)
                  VALUES (NULL, @email, NULL, 'platform.admin.invite_accept_failed', 'PlatformAdmin', CAST(@details AS JSONB), @ip)",
                c =>
                {
                    c.Parameters.AddWithValue("@email", email);
                    c.Parameters.AddWithValue("@details", System.Text.Json.JsonSerializer.Serialize(new { reason }));
                    c.Parameters.AddWithValue("@ip", ip);
                }, ct);
            // Deliberately indistinguishable: unknown email, disabled account,
            // wrong or expired token all return the same result.
            return Results.Json(ApiResponse<object>.Fail("Invalid or expired invite"), statusCode: StatusCodes.Status401Unauthorized);
        }

        var password = request.Password ?? "";
        if (password.Length < 12 || !password.Any(char.IsLetter) || !password.Any(char.IsDigit))
            return Results.BadRequest(ApiResponse<object>.Fail("Validation failed",
                ["Password must be at least 12 characters and contain letters and digits."]));

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(request.Token)) return await FailAsync("missing_fields");

        var admin = await db.QuerySingleAsync(
            @"SELECT id, invite_token_hash FROM platform_admins
              WHERE email=@e AND status <> 'Disabled'
                AND invite_token_hash IS NOT NULL AND invite_expires_at > NOW()
              LIMIT 1",
            c => c.Parameters.AddWithValue("@e", email), ct);
        if (admin is null) return await FailAsync("no_pending_invite");

        var expected = Convert.FromHexString(admin["inviteTokenHash"]?.ToString() ?? "00");
        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(request.Token!.Trim()));
        if (!CryptographicOperations.FixedTimeEquals(expected, actual)) return await FailAsync("token_mismatch");

        var adminId = Convert.ToInt64(admin["id"]);

        await db.ExecuteAsync(
            @"UPDATE platform_admins
              SET password_hash=@h, status='Active', invite_token_hash=NULL, invite_expires_at=NULL, updated_at=NOW()
              WHERE id=@id",
            c =>
            {
                c.Parameters.AddWithValue("@h", PlatformSchemaService.HashPassword(password));
                c.Parameters.AddWithValue("@id", adminId);
            }, ct);
        // A completed invite/reset invalidates every pre-existing session.
        await db.ExecuteAsync("DELETE FROM platform_sessions WHERE admin_id=@id",
            c => c.Parameters.AddWithValue("@id", adminId), ct);

        await AuditLogSequenceRepair.ExecuteWithSequenceRepairAsync(
            db, "platform_audit_log", "id",
            @"INSERT INTO platform_audit_log (actor_admin_id, actor_email, actor_role, action, entity_type, entity_id, details_json, ip_address)
              VALUES (@id, @email, NULL, 'platform.admin.invite_accepted', 'PlatformAdmin', @id, NULL, @ip)",
            c =>
            {
                c.Parameters.AddWithValue("@id", adminId);
                c.Parameters.AddWithValue("@email", email);
                c.Parameters.AddWithValue("@ip", http.Connection.RemoteIpAddress?.ToString() ?? "unknown");
            }, ct);

        // No session is minted here — the operator signs in through the normal,
        // lockout-protected login so there is exactly one authentication path.
        return Results.Ok(ApiResponse<object>.Ok(new { activated = true }, "Password set — sign in to continue"));
    }

    // ── helpers ──────────────────────────────────────────────────────────────
    private sealed record AdminRow(long Id, string Email, string RoleKey, string Status);

    private static async Task<AdminRow?> LoadAdminAsync(Database db, long id, CancellationToken ct)
    {
        var row = await db.QuerySingleAsync(
            @"SELECT a.id, a.email, a.status, COALESCE(r.role_key, '') AS role_key
              FROM platform_admins a LEFT JOIN platform_roles r ON r.id = a.role_id
              WHERE a.id=@id",
            c => c.Parameters.AddWithValue("@id", id), ct);
        return row is null
            ? null
            : new AdminRow(Convert.ToInt64(row["id"]), row["email"]?.ToString() ?? "",
                row["roleKey"]?.ToString() ?? "", row["status"]?.ToString() ?? "");
    }

    private static bool IsSuperRole(string roleKey) =>
        string.Equals(roleKey, SuperAdminRoleKey, StringComparison.OrdinalIgnoreCase);

    private static bool IsSuperAdmin(PlatformEndpoints.PlatformPrincipal principal) =>
        IsSuperRole(principal.RoleKey) ||
        principal.Permissions.Any(static p => string.Equals(p, "platform:*", StringComparison.OrdinalIgnoreCase));

    private static Task<long> CountActiveSuperAdminsAsync(Database db, CancellationToken ct) =>
        db.ScalarLongAsync(
            @"SELECT COUNT(*) FROM platform_admins a
              JOIN platform_roles r ON r.id = a.role_id
              WHERE r.role_key=@k AND a.status='Active'",
            c => c.Parameters.AddWithValue("@k", SuperAdminRoleKey), ct);

    private static (string RawToken, string TokenHash) NewInviteToken()
    {
        var raw = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
        return (raw, hash);
    }
}
