using Npgsql;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Opstrax.Api.Data;
using Opstrax.Api.DTOs;
using Opstrax.Api.Services;

namespace Opstrax.Api.Controllers;

// ─────────────────────────────────────────────────────────────────────────────
// PLATFORM ADMIN API — global SaaS business control plane.
//
// All routes live under /api/platform/* and are EXCLUDED from the tenant auth
// middleware (see Program.cs). Each handler authenticates the platform bearer
// token itself via RequireAsync(...), so a tenant user token can never reach a
// platform endpoint, and a platform token never grants tenant data access except
// through these explicitly-permissioned handlers.
//
// Every mutating action writes a platform_audit_log row.
// ─────────────────────────────────────────────────────────────────────────────

public static class PlatformEndpoints
{
    public sealed record PlatformPrincipal(long AdminId, string Email, string RoleKey, string RoleName, string[] Permissions);

    // Canonical commercial catalog. Keep this aligned with the Platform tenant and
    // package UIs plus Program.ModuleKeyForPath. Commercial writes must never create
    // a well-formed but inert entitlement row because of an operator typo.
    internal static readonly HashSet<string> GovernedEntitlementModuleKeys = new(StringComparer.Ordinal)
    {
        "safety", "maintenance", "dispatch", "telematics", "crm",
        "customer_portal", "reports", "compliance", "integrations",
    };

    public static void MapPlatformEndpoints(this WebApplication app)
    {
        // ── Auth ──────────────────────────────────────────────────────────────
        app.MapPost("/api/platform/auth/login", PlatformLogin);
        app.MapGet("/api/platform/auth/me", PlatformMe);
        app.MapPost("/api/platform/auth/logout", PlatformLogout);

        // ── Command Center ──────────────────────────────────────────────────────
        app.MapGet("/api/platform/command-center/summary", CommandCenter);
        app.MapGet("/api/platform/commercial-ops/summary", CommercialOpsSummary);

        // ── Tenant Management ───────────────────────────────────────────────────
        app.MapGet("/api/platform/tenants", TenantsList);
        app.MapGet("/api/platform/tenants/{id:long}", TenantDetail);
        app.MapPost("/api/platform/tenants", TenantCreate);
        app.MapPut("/api/platform/tenants/{id:long}", TenantUpdate);
        app.MapPost("/api/platform/tenants/{id:long}/status", TenantStatus);
        // Safe, time-limited, fully-audited tenant impersonation (Platform Admin P0).
        app.MapPost("/api/platform/tenants/{id:long}/impersonate", TenantImpersonate);
        app.MapPost("/api/platform/impersonation/{id:long}/end", ImpersonationEnd);
        // Without a list, an operator can only end a grant whose id they still hold
        // from the start response — so a grant left open by a closed browser tab was
        // unrevocable until it expired, and nobody could review who had access.
        app.MapGet("/api/platform/support-access", SupportAccessList);
        app.MapPost("/api/platform/tenants/{id:long}/assign-package", TenantAssignPackage);
        app.MapPost("/api/platform/tenants/{id:long}/reset-admin-invite", TenantResetInvite);
        // Emergency/support control: kill every active session for a tenant without
        // changing its subscription status (suspend/cancel also do this implicitly).
        app.MapPost("/api/platform/tenants/{id:long}/revoke-sessions", TenantRevokeSessions);
        app.MapGet("/api/platform/tenants/{id:long}/audit", TenantAudit);
        app.MapPost("/api/platform/tenants/{id:long}/control-snapshot", TenantControlSnapshot);
        // Tenant user directory + platform-initiated password reset (works without SMTP:
        // returns a one-time temporary password for the operator to hand over).
        app.MapGet("/api/platform/tenants/{id:long}/users", TenantUsers);
        app.MapPost("/api/platform/tenants/{id:long}/users/{userId:long}/reset-password", TenantUserResetPassword);
        // Full 360 user administration for a tenant: create an operator, correct a
        // sign-in email, change a role, disable/re-enable, and re-arm an invite —
        // all without needing anyone inside the tenant to already be able to log in.
        app.MapPost("/api/platform/tenants/{id:long}/users", TenantUserCreate);
        app.MapPut("/api/platform/tenants/{id:long}/users/{userId:long}", TenantUserUpdate);
        app.MapPost("/api/platform/tenants/{id:long}/users/{userId:long}/resend-invite", TenantUserResendInvite);
        // Offboarding — schema-driven cascade delete of ALL tenant-owned rows + the company.
        app.MapDelete("/api/platform/tenants/{id:long}", TenantDelete);
        // Bulk operations for the Tenants table multi-select action bar. Routes each
        // id through the SAME audited persistence path as its single-row counterpart.
        app.MapPost("/api/platform/tenants/bulk", TenantBulk);

        // ── Feature Entitlements ────────────────────────────────────────────────
        app.MapGet("/api/platform/tenants/{id:long}/entitlements", EntitlementsGet);
        app.MapPut("/api/platform/tenants/{id:long}/entitlements", EntitlementsSet);
        app.MapPut("/api/platform/tenants/{id:long}/entitlement-policy", EntitlementPolicySet);

        // ── Country Profiles (market/localization defaults + tenant cascade) ─────
        app.MapGet("/api/platform/country-profiles", CountryProfilesList);
        app.MapGet("/api/platform/country-profiles/{code}", CountryProfileGet);
        app.MapPost("/api/platform/country-profiles", CountryProfileUpsert);
        app.MapPut("/api/platform/country-profiles/{code}", CountryProfileUpsertByCode);
        app.MapDelete("/api/platform/country-profiles/{code}", CountryProfileDelete);

        // ── Packages & Pricing ──────────────────────────────────────────────────
        app.MapGet("/api/platform/packages", PackagesList);
        app.MapPost("/api/platform/packages", PackageCreate);
        app.MapPut("/api/platform/packages/{id:long}", PackageUpdate);
        app.MapDelete("/api/platform/packages/{id:long}", PackageDelete);

        // ── Billing & Invoices ──────────────────────────────────────────────────
        app.MapGet("/api/platform/invoices", InvoicesList);
        app.MapPost("/api/platform/invoices", InvoiceCreate);
        app.MapPost("/api/platform/invoices/{id:long}/mark-paid", InvoiceMarkPaid);
        app.MapPost("/api/platform/invoices/bulk", InvoiceBulk);

        // ── Customer Success (health scores) ────────────────────────────────────
        app.MapGet("/api/platform/health", HealthScores);

        // ── Reliability Center (platform-scoped mirror of /api/ops/reliability) ──
        // Same aggregated system-health snapshot, reachable with the platform
        // bearer token so the Platform Admin console renders real health, SLOs,
        // error-budget burn, top failing endpoints, incidents, and per-tenant
        // reliability — no mock/demo data.
        app.MapGet("/api/platform/reliability", ReliabilityCenter);
        app.MapGet("/api/platform/reliability/slo", ReliabilitySlo);
        app.MapPost("/api/platform/reliability/incidents/{id:long}/ack", ReliabilityAckIncident);
        app.MapPost("/api/platform/reliability/incidents/{id:long}/resolve", ReliabilityResolveIncident);

        // ── Security & Audit ────────────────────────────────────────────────────
        app.MapGet("/api/platform/audit", AuditList);
        app.MapGet("/api/platform/audit/export.csv", AuditExport);

        // ── Roles (for RBAC visibility) ─────────────────────────────────────────
        app.MapGet("/api/platform/roles", RolesList);

        // ── Platform operator management (list/invite/role/status/sessions) ──────
        PlatformAdminEndpoints.Map(app);

        // ── Platform settings (outbound email / SMTP) ───────────────────────────
    }

    // ════════════════════════════════════════════════════════════════════════════
    // AUTH + RBAC PRIMITIVES
    // ════════════════════════════════════════════════════════════════════════════

    private static async Task<PlatformPrincipal?> AuthenticateAsync(HttpContext http, Database db, CancellationToken ct)
    {
        var token = BearerToken(http);
        if (string.IsNullOrWhiteSpace(token)) return null;

        var row = await db.QuerySingleAsync(
            @"SELECT a.id, a.email, a.full_name, r.role_key, r.name role_name
              FROM platform_sessions s
              JOIN platform_admins a ON a.id = s.admin_id
              LEFT JOIN platform_roles r ON r.id = a.role_id
              WHERE s.session_token=@t AND s.expires_at > NOW() AND a.status='Active'
              LIMIT 1",
            c => c.Parameters.AddWithValue("@t", token), ct);
        if (row is null) return null;

        var adminId = Convert.ToInt64(row["id"]);
        var roleKey = row["roleKey"]?.ToString() ?? "";
        var roleName = row["roleName"]?.ToString() ?? "Platform Admin";

        var perms = (await db.QueryAsync(
                @"SELECT rp.permission_key FROM platform_admins a
                  JOIN platform_role_permissions rp ON rp.role_id = a.role_id
                  WHERE a.id=@id",
                c => c.Parameters.AddWithValue("@id", adminId), ct))
            .Select(x => x["permissionKey"]?.ToString() ?? "")
            .Where(x => x.Length > 0)
            .ToArray();

        return new PlatformPrincipal(adminId, row["email"]?.ToString() ?? "", roleKey, roleName, perms);
    }

    // Returns the principal when authorized, or an IResult error (401/403) to short-circuit.
    // internal so sibling endpoint modules (e.g. RevenueEndpoints) reuse one platform guard.
    internal static async Task<(PlatformPrincipal? Principal, IResult? Error)> RequireAsync(
        HttpContext http, Database db, string permission, CancellationToken ct)
    {
        var principal = await AuthenticateAsync(http, db, ct);
        if (principal is null)
            return (null, Results.Json(ApiResponse<object>.Fail("Unauthorized", "Platform session required"), statusCode: StatusCodes.Status401Unauthorized));

        if (!HasPlatformPermission(principal.Permissions, permission))
            return (null, Results.Json(ApiResponse<object>.Fail("Forbidden", $"Missing permission: {permission}"), statusCode: StatusCodes.Status403Forbidden));

        return (principal, null);
    }

    internal static bool HasPlatformPermission(IReadOnlyCollection<string> permissions, string required)
    {
        if (permissions.Count == 0) return false;
        foreach (var p in permissions)
        {
            if (string.Equals(p, "platform:*", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(p, required, StringComparison.OrdinalIgnoreCase)) return true;
            // prefix wildcard, e.g. "platform:tenants:*" matches "platform:tenants:manage"
            if (p.EndsWith(":*", StringComparison.Ordinal))
            {
                var prefix = p[..^1]; // keep trailing ':' -> "platform:tenants:"
                if (required.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        return false;
    }

    // internal so PlatformAdminEndpoints shares the single platform audit writer.
    internal static Task AuditAsync(Database db, PlatformPrincipal actor, HttpContext http, string action,
        string entityType, long? entityId, long? targetCompanyId, object? details, CancellationToken ct)
    {
        var detailsJson = details is null ? null : JsonSerializer.Serialize(details);
        return AuditLogSequenceRepair.ExecuteWithSequenceRepairAsync(
            db,
            "platform_audit_log",
            "id",
            @"INSERT INTO platform_audit_log (actor_admin_id, actor_email, actor_role, action, entity_type, entity_id, target_company_id, details_json, ip_address)
              VALUES (@aid, @email, @role, @action, @etype, @eid, @cid, CAST(@details AS JSONB), @ip)",
            c =>
            {
                c.Parameters.AddWithValue("@aid", actor.AdminId);
                c.Parameters.AddWithValue("@email", actor.Email);
                c.Parameters.AddWithValue("@role", (object?)actor.RoleKey ?? DBNull.Value);
                c.Parameters.AddWithValue("@action", action);
                c.Parameters.AddWithValue("@etype", entityType);
                c.Parameters.AddWithValue("@eid", (object?)entityId ?? DBNull.Value);
                c.Parameters.AddWithValue("@cid", (object?)targetCompanyId ?? DBNull.Value);
                c.Parameters.AddWithValue("@details", (object?)detailsJson ?? DBNull.Value);
                c.Parameters.AddWithValue("@ip", http.Connection.RemoteIpAddress?.ToString() ?? "unknown");
            }, ct);
    }

    internal static string BearerToken(HttpContext http)
    {
        var header = http.Request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..].Trim()
            : string.Empty;
    }

    // ════════════════════════════════════════════════════════════════════════════
    // AUTH HANDLERS
    // ════════════════════════════════════════════════════════════════════════════

    internal sealed record PlatformLoginRequest(string Email, string Password, string? MfaCode = null);

    // Failed-login lockout: 5 failures per email+IP within 15 minutes → 429.
    // DB-backed: the platform_audit_log rows ARE the counter, so the lockout
    // survives process restarts and applies across instances.
    internal const int MaxFailedLogins = 5;

    // Failures within the window, scoped to email+IP, counted only since the
    // account's most recent successful login (a success resets the ledger,
    // matching the previous in-memory semantics).
    internal static Task<long> CountRecentAuthFailuresAsync(
        Database db, string email, string ip, string failedAction, string? successAction, CancellationToken ct)
        => db.ScalarLongAsync(
            @"SELECT COUNT(*) FROM platform_audit_log
              WHERE LOWER(actor_email)=@e AND ip_address=@ip AND action=@fail
                AND created_at > NOW() - INTERVAL '15 minutes'
                AND (@success IS NULL OR created_at > COALESCE(
                    (SELECT MAX(created_at) FROM platform_audit_log
                      WHERE LOWER(actor_email)=@e AND action=@success), '-infinity'::timestamptz))",
            c =>
            {
                c.Parameters.AddWithValue("@e", email.ToLowerInvariant());
                c.Parameters.AddWithValue("@ip", ip);
                c.Parameters.AddWithValue("@fail", failedAction);
                c.Parameters.AddWithValue("@success", (object?)successAction ?? DBNull.Value);
            }, ct);

    // Audit a failed/locked login attempt. No principal exists yet, so this writes the
    // attempted email directly. NEVER include the submitted password in details.
    private static Task AuditLoginFailureAsync(Database db, HttpContext http, string email, string action, string reason, CancellationToken ct)
        => AuditLogSequenceRepair.ExecuteWithSequenceRepairAsync(
            db, "platform_audit_log", "id",
            @"INSERT INTO platform_audit_log (actor_admin_id, actor_email, actor_role, action, entity_type, entity_id, target_company_id, details_json, ip_address)
              VALUES (NULL, @email, NULL, @action, 'PlatformAdmin', NULL, NULL, CAST(@details AS JSONB), @ip)",
            c =>
            {
                c.Parameters.AddWithValue("@email", email);
                c.Parameters.AddWithValue("@action", action);
                c.Parameters.AddWithValue("@details", JsonSerializer.Serialize(new { reason }));
                c.Parameters.AddWithValue("@ip", http.Connection.RemoteIpAddress?.ToString() ?? "unknown");
            }, ct);

    internal static async Task<IResult> PlatformLogin(HttpContext http, PlatformLoginRequest request, Database db, CancellationToken ct)
    {
        var email = (request.Email ?? "").Trim();
        var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        if (await CountRecentAuthFailuresAsync(db, email, ip, "platform.login_failed", "platform.login", ct) >= MaxFailedLogins)
        {
            await AuditLoginFailureAsync(db, http, email, "platform.login_locked", "too_many_failed_attempts", ct);
            return Results.Json(ApiResponse<object>.Fail("Too many failed attempts", "Try again later"), statusCode: StatusCodes.Status429TooManyRequests);
        }

        async Task<IResult> FailAsync(string reason)
        {
            await AuditLoginFailureAsync(db, http, email, "platform.login_failed", reason, ct);
            return Results.Json(ApiResponse<object>.Fail("Invalid credentials"), statusCode: StatusCodes.Status401Unauthorized);
        }

        var admin = await db.QuerySingleAsync(
            @"SELECT a.id, a.email, a.full_name, a.password_hash, a.mfa_enabled, a.mfa_secret, r.role_key, r.name role_name
              FROM platform_admins a LEFT JOIN platform_roles r ON r.id = a.role_id
              WHERE LOWER(a.email)=LOWER(@e) AND a.status='Active' LIMIT 1",
            c => c.Parameters.AddWithValue("@e", email), ct);
        if (admin is null) return await FailAsync("unknown_or_inactive_account");

        if (!VerifyPassword(request.Password ?? "", admin["passwordHash"]?.ToString()))
            return await FailAsync("invalid_password");

        // Second factor: once enrolled+verified (mfa_enabled), a valid TOTP code is
        // required on every login. A missing code is a distinct, non-counted prompt
        // (the password was right); a WRONG code counts toward the lockout.
        var storedMfaSecret = admin["mfaSecret"]?.ToString();
        if (admin["mfaEnabled"] is true && !string.IsNullOrWhiteSpace(storedMfaSecret))
        {
            var pii = http.RequestServices.GetRequiredService<Opstrax.Api.Security.PiiProtectionService>();
            var mfaSecret = pii.Decrypt(storedMfaSecret);
            if (string.IsNullOrWhiteSpace(mfaSecret))
                return await FailAsync("mfa_secret_unavailable");
            if (string.IsNullOrWhiteSpace(request.MfaCode))
            {
                return Results.Json(
                    ApiResponse<object>.Fail("MFA code required", "mfa_required"),
                    statusCode: StatusCodes.Status401Unauthorized);
            }
            if (!Opstrax.Api.Security.TotpService.VerifyCode(mfaSecret, request.MfaCode))
                return await FailAsync("invalid_mfa_code");
        }

        var adminId = Convert.ToInt64(admin["id"]);
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        await db.ExecuteAsync(
            @"INSERT INTO platform_sessions (admin_id, session_token, expires_at)
              VALUES (@aid, @t, NOW() + INTERVAL '8 hour')",
            c =>
            {
                c.Parameters.AddWithValue("@aid", adminId);
                c.Parameters.AddWithValue("@t", token);
            }, ct);
        await db.ExecuteAsync("UPDATE platform_admins SET last_login_at = NOW() WHERE id=@id",
            c => c.Parameters.AddWithValue("@id", adminId), ct);

        var perms = (await db.QueryAsync(
                "SELECT permission_key FROM platform_role_permissions rp JOIN platform_admins a ON a.role_id=rp.role_id WHERE a.id=@id",
                c => c.Parameters.AddWithValue("@id", adminId), ct))
            .Select(x => x["permissionKey"]?.ToString()).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();

        var principal = new PlatformPrincipal(adminId, admin["email"]?.ToString() ?? "", admin["roleKey"]?.ToString() ?? "", admin["roleName"]?.ToString() ?? "", perms!);
        await AuditAsync(db, principal, http, "platform.login", "PlatformAdmin", adminId, null, null, ct);

        return Results.Ok(ApiResponse<object>.Ok(new
        {
            token,
            admin = new { id = adminId, email = admin["email"], name = admin["fullName"] },
            role = new { key = admin["roleKey"], name = admin["roleName"] },
            permissions = perms,
        }, "Platform login successful"));
    }

    private static async Task<IResult> PlatformMe(HttpContext http, Database db, CancellationToken ct)
    {
        var principal = await AuthenticateAsync(http, db, ct);
        if (principal is null) return Results.Json(ApiResponse<object>.Fail("Unauthorized"), statusCode: StatusCodes.Status401Unauthorized);
        var name = await db.QuerySingleAsync("SELECT full_name FROM platform_admins WHERE id=@id",
            c => c.Parameters.AddWithValue("@id", principal.AdminId), ct);
        return Results.Ok(ApiResponse<object>.Ok(new
        {
            admin = new { id = principal.AdminId, email = principal.Email, name = name?["fullName"] },
            role = new { key = principal.RoleKey, name = principal.RoleName },
            permissions = principal.Permissions,
        }, "Session active"));
    }

    private static async Task<IResult> PlatformLogout(HttpContext http, Database db, CancellationToken ct)
    {
        var token = BearerToken(http);
        if (!string.IsNullOrWhiteSpace(token))
            await db.ExecuteAsync("DELETE FROM platform_sessions WHERE session_token=@t",
                c => c.Parameters.AddWithValue("@t", token), ct);
        return Results.Ok(ApiResponse<object>.Ok(new { loggedOut = true }, "Logged out"));
    }

    // ════════════════════════════════════════════════════════════════════════════
    // COMMAND CENTER
    // ════════════════════════════════════════════════════════════════════════════

    private static async Task<IResult> CommandCenter(HttpContext http, Database db, CancellationToken ct)
    {
        var (_, error) = await RequireAsync(http, db, "platform:dashboard:view", ct);
        if (error is not null) return error;

        var counts = await db.QueryAsync(
            @"SELECT status, COUNT(*) n, COALESCE(SUM(mrr_cents),0) mrr FROM tenant_subscriptions GROUP BY status", ct: ct);
        long active = 0, trial = 0, pastDue = 0, suspended = 0, cancelled = 0, manual = 0, mrrCents = 0;
        foreach (var r in counts)
        {
            var status = r["status"]?.ToString() ?? "";
            var n = Convert.ToInt64(r["n"]);
            var mrr = Convert.ToInt64(r["mrr"]);
            switch (status)
            {
                case "active": active = n; mrrCents += mrr; break;
                case "trial": trial = n; break;
                case "past_due": pastDue = n; mrrCents += mrr; break;
                case "suspended": suspended = n; break;
                case "cancelled": cancelled = n; break;
                case "manual_contract": manual = n; mrrCents += mrr; break;
            }
        }

        var pastDueRevenue = await db.ScalarLongAsync(
            "SELECT COALESCE(SUM(amount_cents),0) FROM platform_invoices WHERE status IN ('overdue','sent')", ct: ct);

        var trialEndingSoon = await db.ScalarLongAsync(
            "SELECT COUNT(*) FROM tenant_subscriptions WHERE status='trial' AND trial_ends_at IS NOT NULL AND trial_ends_at < NOW() + INTERVAL '7 day'", ct: ct);

        var renewalsDue = await db.ScalarLongAsync(
            "SELECT COUNT(*) FROM tenant_subscriptions WHERE contract_end IS NOT NULL AND contract_end < (NOW() + INTERVAL '30 day')::date AND status IN ('active','past_due')", ct: ct);

        // Top risks — real rows
        var risks = await db.QueryAsync(
            @"SELECT c.name tenant, ts.status, ts.mrr_cents, ts.trial_ends_at, ts.contract_end
              FROM tenant_subscriptions ts JOIN companies c ON c.id = ts.company_id
              WHERE ts.status IN ('past_due','suspended')
                 OR (ts.status='trial' AND ts.trial_ends_at IS NOT NULL AND ts.trial_ends_at < NOW() + INTERVAL '7 day')
              ORDER BY CASE ts.status WHEN 'past_due' THEN 0 WHEN 'suspended' THEN 1 ELSE 2 END, ts.mrr_cents DESC
              LIMIT 8", ct: ct);

        var recommended = new List<object>();
        if (pastDue > 0) recommended.Add(new { priority = "Critical", title = $"Chase {pastDue} past-due tenant(s)", action = "payment_follow_up" });
        if (trialEndingSoon > 0) recommended.Add(new { priority = "High", title = $"{trialEndingSoon} trial(s) ending within 7 days", action = "trial_conversion" });
        if (renewalsDue > 0) recommended.Add(new { priority = "Medium", title = $"{renewalsDue} renewal(s) due in 30 days", action = "renewal_follow_up" });
        if (suspended > 0) recommended.Add(new { priority = "High", title = $"{suspended} suspended tenant(s) to recover", action = "reactivation" });

        return Results.Ok(ApiResponse<object>.Ok(new
        {
            mrrCents,
            arrCents = mrrCents * 12,
            currency = "USD",
            tenants = new
            {
                active, trial, pastDue, suspended, cancelled, manual,
                total = active + trial + pastDue + suspended + cancelled + manual,
            },
            pastDueRevenueCents = pastDueRevenue,
            trialEndingSoon,
            renewalsDue,
            topRisks = risks,
            recommendedActions = recommended,
        }));
    }

    private static async Task<IResult> CommercialOpsSummary(HttpContext http, Database db, CancellationToken ct)
    {
        var (_, error) = await RequireAsync(http, db, "platform:dashboard:view", ct);
        if (error is not null) return error;

        var summary = await BuildCommercialOpsSummaryAsync(db, ct);
        return Results.Ok(ApiResponse<object>.Ok(summary, "Platform commercial operations summary"));
    }

    internal static async Task<Dictionary<string, object?>> BuildCommercialOpsSummaryAsync(Database db, CancellationToken ct)
    {
        var subscriptionCounts = await db.QueryAsync(
            @"SELECT status, COUNT(*) n, COALESCE(SUM(mrr_cents),0) mrr
              FROM tenant_subscriptions
              GROUP BY status", ct: ct);

        long active = 0, trial = 0, pastDue = 0, suspended = 0, cancelled = 0, manual = 0, mrrCents = 0;
        foreach (var r in subscriptionCounts)
        {
            var status = r["status"]?.ToString() ?? "";
            var n = Convert.ToInt64(r["n"]);
            var mrr = Convert.ToInt64(r["mrr"]);
            switch (status)
            {
                case "active": active = n; mrrCents += mrr; break;
                case "trial": trial = n; break;
                case "past_due": pastDue = n; mrrCents += mrr; break;
                case "suspended": suspended = n; break;
                case "cancelled": cancelled = n; break;
                case "manual_contract": manual = n; mrrCents += mrr; break;
            }
        }

        var tenantTotal = await db.ScalarLongAsync("SELECT COUNT(*) FROM companies", ct: ct);
        var trialEndingSoon = await db.ScalarLongAsync(
            "SELECT COUNT(*) FROM tenant_subscriptions WHERE status='trial' AND trial_ends_at IS NOT NULL AND trial_ends_at < NOW() + INTERVAL '7 day'",
            ct: ct);
        var renewalsDue = await db.ScalarLongAsync(
            "SELECT COUNT(*) FROM tenant_subscriptions WHERE contract_end IS NOT NULL AND contract_end < (NOW() + INTERVAL '30 day')::date AND status IN ('active','past_due')",
            ct: ct);
        var openInvoiceCount = await db.ScalarLongAsync("SELECT COUNT(*) FROM platform_invoices WHERE status IN ('sent','overdue')", ct: ct);
        var outstandingRevenue = await db.ScalarLongAsync("SELECT COALESCE(SUM(amount_cents),0) FROM platform_invoices WHERE status IN ('sent','overdue')", ct: ct);
        var collectedRevenue = await db.ScalarLongAsync("SELECT COALESCE(SUM(amount_cents),0) FROM platform_invoices WHERE status='paid'", ct: ct);

        var packageRows = await db.QueryAsync(
            @"SELECT p.package_code, p.name, p.active, p.is_custom, COALESCE(COUNT(ts.id),0) tenant_count, COALESCE(SUM(ts.mrr_cents),0) mrr_cents
              FROM packages p
              LEFT JOIN tenant_subscriptions ts ON ts.package_id = p.id
              GROUP BY p.id, p.package_code, p.name, p.active, p.is_custom
              ORDER BY tenant_count DESC, p.name
              LIMIT 5", ct: ct);

        var riskRows = await db.QueryAsync(
            @"SELECT c.id, c.name tenant, ts.status, ts.contract_end,
                     COALESCE(COUNT(u.id),0) user_count,
                     (SELECT COUNT(*) FROM platform_invoices i WHERE i.company_id=c.id AND i.status IN ('overdue','sent')) open_invoices
              FROM companies c
              JOIN tenant_subscriptions ts ON ts.company_id = c.id
              LEFT JOIN users u ON u.company_id = c.id
              GROUP BY c.id, c.name, ts.status, ts.contract_end
              ORDER BY open_invoices DESC, ts.contract_end NULLS LAST, c.name
              LIMIT 8", ct: ct);

        var auditRows = await db.QueryAsync(
            @"SELECT id, actor_email, actor_role, action, entity_type, entity_id, target_company_id, created_at
              FROM platform_audit_log
              ORDER BY created_at DESC
              LIMIT 8", ct: ct);

        var roleRows = await db.QueryAsync(
            @"SELECT r.role_key, r.name,
                     (SELECT COUNT(*) FROM platform_role_permissions rp WHERE rp.role_id=r.id) permission_count,
                     (SELECT COUNT(*) FROM platform_admins a WHERE a.role_id=r.id) admin_count
              FROM platform_roles r
              ORDER BY r.id", ct: ct);

        var recommendedActions = new List<object>();
        if (pastDue > 0) recommendedActions.Add(new { priority = "Critical", title = $"{pastDue} tenant(s) past due", action = "payment_follow_up" });
        if (trialEndingSoon > 0) recommendedActions.Add(new { priority = "High", title = $"{trialEndingSoon} trial tenant(s) ending soon", action = "trial_conversion" });
        if (renewalsDue > 0) recommendedActions.Add(new { priority = "High", title = $"{renewalsDue} renewal(s) due in 30 days", action = "renewal_follow_up" });
        if (suspended > 0) recommendedActions.Add(new { priority = "High", title = $"{suspended} suspended tenant(s) to review", action = "reactivation" });

        return new Dictionary<string, object?>
        {
            ["generatedAtUtc"] = DateTime.UtcNow,
            ["currency"] = "USD",
            ["mrrCents"] = mrrCents,
            ["arrCents"] = mrrCents * 12,
            ["tenantLifecycle"] = new
            {
                total = tenantTotal,
                active,
                trial,
                pastDue,
                suspended,
                cancelled,
                manual,
                trialEndingSoon,
                renewalsDue,
            },
            ["billing"] = new
            {
                openInvoiceCount,
                outstandingRevenueCents = outstandingRevenue,
                collectedRevenueCents = collectedRevenue,
            },
            ["packages"] = new
            {
                total = packageRows.Count,
                active = packageRows.Count(r => string.Equals(r["active"]?.ToString(), "true", StringComparison.OrdinalIgnoreCase)),
                custom = packageRows.Count(r => string.Equals(r["isCustom"]?.ToString(), "true", StringComparison.OrdinalIgnoreCase)),
                items = packageRows.Select(r => new
                {
                    packageCode = r["packageCode"],
                    name = r["name"],
                    tenantCount = r["tenantCount"],
                    mrrCents = r["mrrCents"],
                    active = r["active"],
                    isCustom = r["isCustom"],
                }).ToArray(),
            },
            ["health"] = new
            {
                total = riskRows.Count,
                critical = riskRows.Count(r => string.Equals(r["status"]?.ToString(), "suspended", StringComparison.OrdinalIgnoreCase) || string.Equals(r["status"]?.ToString(), "past_due", StringComparison.OrdinalIgnoreCase)),
                risky = riskRows.Count(r => string.Equals(r["status"]?.ToString(), "trial", StringComparison.OrdinalIgnoreCase)),
                items = riskRows.Select(r => new
                {
                    id = r["id"],
                    tenant = r["tenant"],
                    status = r["status"],
                    contractEnd = r["contractEnd"],
                    userCount = r["userCount"],
                    openInvoices = r["openInvoices"],
                }).ToArray(),
            },
            ["audit"] = new
            {
                recent = auditRows.Select(r => new
                {
                    id = r["id"],
                    actorEmail = r["actorEmail"],
                    actorRole = r["actorRole"],
                    action = r["action"],
                    entityType = r["entityType"],
                    entityId = r["entityId"],
                    targetCompanyId = r["targetCompanyId"],
                    createdAt = r["createdAt"],
                }).ToArray(),
            },
            ["roles"] = new
            {
                total = roleRows.Count,
                items = roleRows.Select(r => new
                {
                    roleKey = r["roleKey"],
                    name = r["name"],
                    permissionCount = r["permissionCount"],
                    adminCount = r["adminCount"],
                }).ToArray(),
            },
            ["recommendedActions"] = recommendedActions,
        };
    }

    // ════════════════════════════════════════════════════════════════════════════
    // TENANT MANAGEMENT
    // ════════════════════════════════════════════════════════════════════════════

    private const string TenantSelect =
        @"SELECT c.id, c.name, c.company_code, c.industry, c.status company_status, c.created_at,
                 c.country, c.currency, c.legal_name, c.website, c.fleet_size, c.tax_id,
                 c.primary_contact_name, c.primary_contact_email, c.primary_contact_phone, c.billing_email,
                 c.entitlement_policy_mode,
                 ts.status, ts.seat_limit, ts.billing_currency, ts.mrr_cents, ts.trial_ends_at, ts.billing_cycle,
                 ts.contract_start, ts.contract_end, ts.account_owner, ts.support_owner,
                 p.name package_name, p.package_code,
                 (SELECT COUNT(*) FROM users u WHERE u.company_id = c.id) user_count
          FROM companies c
          LEFT JOIN tenant_subscriptions ts ON ts.company_id = c.id
          LEFT JOIN packages p ON p.id = ts.package_id";

    // POST /api/platform/tenants/{id}/impersonate {targetUserId, reason, minutes?} — issue a
    // uniquely-bound, short-lived READ-ONLY support grant. Disabled by default. The tenant auth
    // edge validates the grant on every request and denies state-changing methods before a handler.
    internal static async Task<IResult> TenantImpersonate(long id, HttpContext http, System.Text.Json.JsonElement body,
        Database db, IConfiguration configuration, CancellationToken ct)
    {
        var (principal, error) = await RequireAsync(http, db, "platform:impersonation:start", ct);
        if (error is not null) return error;

        if (!PlatformImpersonationPolicy.IsEnabled(configuration))
            return Results.Json(ApiResponse<object>.Fail("Support access disabled",
                "Platform impersonation is disabled by deployment policy."), statusCode: StatusCodes.Status503ServiceUnavailable);

        var reason = body.TryGetProperty("reason", out var r) ? r.GetString()?.Trim() : null;
        if (string.IsNullOrWhiteSpace(reason))
            return Results.BadRequest(ApiResponse<object>.Fail("A reason is required to impersonate a tenant."));
        if (reason.Length > 400)
            return Results.BadRequest(ApiResponse<object>.Fail("Reason must be 400 characters or fewer."));
        var minutes = body.TryGetProperty("minutes", out var m) && m.TryGetInt32(out var mv) ? mv : 30;
        if (minutes is < 5 or > 60)
            return Results.BadRequest(ApiResponse<object>.Fail("Support access duration must be between 5 and 60 minutes."));
        if (!body.TryGetProperty("targetUserId", out var tu) || !tu.TryGetInt64(out var targetUserId))
            return Results.BadRequest(ApiResponse<object>.Fail("targetUserId is required (the tenant user to act as)."));

        // The target and tenant must both be active. A support grant must never
        // bypass the same lifecycle controls as an ordinary login.
        var target = await db.QuerySingleAsync(
            @"SELECT u.id, u.email, u.full_name
              FROM users u JOIN companies c ON c.id=u.company_id
              WHERE u.id=@u AND u.company_id=@c
                AND u.status='Active' AND c.status='Active'",
            c => { c.Parameters.AddWithValue("@u", targetUserId); c.Parameters.AddWithValue("@c", id); }, ct);
        if (target is null)
            return Results.NotFound(ApiResponse<object>.Fail("Active target user not found in an active tenant."));

        var grantRef = Guid.NewGuid();
        var token = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var grantId = await db.RunInSystemTransactionAsync(async () =>
        {
            var createdGrantId = await db.InsertAsync(
                @"INSERT INTO platform_impersonation_sessions
                    (admin_id, company_id, target_user_id, grant_ref, reason, expires_at)
                  VALUES (@a, @c, @u, @g, @r, NOW() + make_interval(mins => @min)) RETURNING id",
                c =>
                {
                    c.Parameters.AddWithValue("@a", principal!.AdminId);
                    c.Parameters.AddWithValue("@c", id);
                    c.Parameters.AddWithValue("@u", targetUserId);
                    c.Parameters.AddWithValue("@g", grantRef);
                    c.Parameters.AddWithValue("@r", reason);
                    c.Parameters.AddWithValue("@min", minutes);
                }, ct);

            await db.ExecuteAsync(
                @"INSERT INTO user_sessions (user_id, company_id, session_token, expires_at, impersonation_grant_id)
                  VALUES (@u, @c, @t, NOW() + make_interval(mins => @min), @grantId)",
                c =>
                {
                    c.Parameters.AddWithValue("@u", targetUserId); c.Parameters.AddWithValue("@c", id);
                    c.Parameters.AddWithValue("@t", token); c.Parameters.AddWithValue("@min", minutes);
                    c.Parameters.AddWithValue("@grantId", createdGrantId);
                }, ct);

            await AuditAsync(db, principal!, http, "platform.impersonation.started", "SupportAccessGrant",
                createdGrantId, id, new { grantRef, targetUserId, reason, minutes, mode = "read_only" }, ct);
            await TenantSupportAuditAsync(db, id, "platform.support_access.started", grantRef,
                new { mode = "read_only", expiresInMinutes = minutes }, ct);
            return createdGrantId;
        }, ct);

        return Results.Ok(ApiResponse<object>.Ok(new
        {
            impersonationSessionId = grantId,
            grantRef,
            token,
            actingAs = new { id = target["id"], email = target["email"], name = target["fullName"] },
            mode = "read_only",
            expiresInMinutes = minutes,
        }, $"Read-only support access active for {minutes} minutes. Every request is attributed and audited."));
    }

    // POST /api/platform/impersonation/{id}/end — end early: stamps ended_at and revokes the tenant
    // sessions minted inside the impersonation window for that (user, company).
    internal static async Task<IResult> ImpersonationEnd(long id, HttpContext http, Database db, CancellationToken ct)
    {
        var (principal, error) = await RequireAsync(http, db, "platform:impersonation:start", ct);
        if (error is not null) return error;

        var row = await db.QuerySingleAsync(
            "SELECT company_id, grant_ref, ended_at FROM platform_impersonation_sessions WHERE id=@id",
            c => c.Parameters.AddWithValue("@id", id), ct);
        if (row is null) return Results.NotFound(ApiResponse<object>.Fail("Impersonation session not found."));
        if (row["endedAt"] is not null and not DBNull)
            return Results.Ok(ApiResponse<object>.Ok(new { id }, "Already ended."));

        var companyId = Convert.ToInt64(row["companyId"]);
        var grantRef = Guid.Parse(row["grantRef"]!.ToString()!);
        var revoked = await db.RunInSystemTransactionAsync(async () =>
        {
            var deleted = await db.ExecuteAsync(
                "DELETE FROM user_sessions WHERE impersonation_grant_id=@id",
                c => c.Parameters.AddWithValue("@id", id), ct);
            await db.ExecuteAsync(
                "UPDATE platform_impersonation_sessions SET ended_at=NOW() WHERE id=@id AND ended_at IS NULL",
                c => c.Parameters.AddWithValue("@id", id), ct);
            await AuditAsync(db, principal!, http, "platform.impersonation.ended", "SupportAccessGrant", id,
                companyId, new { grantRef, sessionsRevoked = deleted }, ct);
            await TenantSupportAuditAsync(db, companyId, "platform.support_access.ended", grantRef,
                new { sessionsRevoked = deleted }, ct);
            return deleted;
        }, ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id, grantRef, sessionsRevoked = revoked },
            "Support access ended and its exact session was revoked."));
    }

    // The support-access ledger: who holds live access to which tenant right now,
    // under what stated reason, expiring when — plus the recent history, which is
    // what a customer asks for when they ask "who at your company saw our data".
    internal static async Task<IResult> SupportAccessList(
        HttpContext http, Database db, IConfiguration configuration, CancellationToken ct)
    {
        var (_, error) = await RequireAsync(http, db, "platform:tenants:view", ct);
        if (error is not null) return error;

        var rows = await db.QueryAsync("""
            SELECT s.id, s.grant_ref, s.reason, s.created_at, s.expires_at, s.ended_at,
                   s.company_id, c.name AS tenant, a.email AS operator_email,
                   u.email AS target_email, u.full_name AS target_name,
                   (s.ended_at IS NULL AND s.expires_at > NOW()) AS is_active,
                   GREATEST(0, EXTRACT(EPOCH FROM (s.expires_at - NOW()))::int) AS seconds_remaining
            FROM platform_impersonation_sessions s
            JOIN companies c ON c.id = s.company_id
            LEFT JOIN platform_admins a ON a.id = s.admin_id
            LEFT JOIN users u ON u.id = s.target_user_id
            ORDER BY (s.ended_at IS NULL AND s.expires_at > NOW()) DESC, s.created_at DESC
            LIMIT 100
            """, ct: ct);

        return Results.Ok(ApiResponse<object>.Ok(new
        {
            enabled = PlatformImpersonationPolicy.IsEnabled(configuration),
            readOnlyScope = PlatformImpersonationPolicy.ReadOnlyScope,
            grants = rows,
            activeCount = rows.Count(r => r["isActive"] is bool b && b),
        }));
    }

    private static Task TenantSupportAuditAsync(Database db, long companyId, string action, Guid grantRef,
        object details, CancellationToken ct) => AuditLogSequenceRepair.ExecuteWithSequenceRepairAsync(
        db, "audit_logs", "id",
        @"INSERT INTO audit_logs
            (company_id, actor_user_id, actor_name, action_name, entity_name, details_json)
          VALUES (@companyId, NULL, @actor, @action, 'SupportAccessGrant', @details::jsonb)",
        c =>
        {
            c.Parameters.AddWithValue("@companyId", companyId);
            c.Parameters.AddWithValue("@actor", $"platform-support:{grantRef:N}");
            c.Parameters.AddWithValue("@action", action);
            c.Parameters.AddWithValue("@details", JsonSerializer.Serialize(details));
        }, ct);

    internal static async Task<IResult> TenantsList(HttpContext http, Database db, CancellationToken ct)
    {
        var (_, error) = await RequireAsync(http, db, "platform:tenants:view", ct);
        if (error is not null) return error;
        var rows = await db.QueryAsync(TenantSelect + " ORDER BY c.created_at DESC", ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(rows));
    }

    internal static async Task<IResult> TenantDetail(long id, HttpContext http, Database db, CancellationToken ct)
    {
        var (_, error) = await RequireAsync(http, db, "platform:tenants:view", ct);
        if (error is not null) return error;
        var tenant = await db.QuerySingleAsync(TenantSelect + " WHERE c.id=@id",
            c => c.Parameters.AddWithValue("@id", id), ct);
        if (tenant is null) return Results.Json(ApiResponse<object>.Fail("Not found"), statusCode: StatusCodes.Status404NotFound);
        var entitlements = await db.QueryAsync(
            "SELECT module_key, enabled, limit_value, tier, source, updated_at FROM tenant_entitlements WHERE company_id=@id ORDER BY module_key",
            c => c.Parameters.AddWithValue("@id", id), ct);
        var invoices = await db.QueryAsync(
            "SELECT id, invoice_number, status, amount_cents, currency, issued_at, due_at, paid_at FROM platform_invoices WHERE company_id=@id ORDER BY created_at DESC LIMIT 25",
            c => c.Parameters.AddWithValue("@id", id), ct);
        return Results.Ok(ApiResponse<object>.Ok(new { tenant, entitlements, invoices }));
    }

    internal static async Task<IResult> TenantCreate(HttpContext http, Dictionary<string, object?> body, Database db, CountryProfileService countries, FeatureFlagService flags, CancellationToken ct)
    {
        var (principal, error) = await RequireAsync(http, db, "platform:tenants:manage", ct);
        if (error is not null) return error;

        var name = Str(body, "name");
        if (string.IsNullOrWhiteSpace(name))
            return Results.Json(ApiResponse<object>.Fail("Validation failed", "name is required"), statusCode: StatusCodes.Status400BadRequest);

        var code = Str(body, "companyCode");
        if (string.IsNullOrWhiteSpace(code)) code = "T-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        // Duplicate tenant code must be a clean 409, not an unhandled unique-violation 500.
        var codeTaken = await db.ScalarLongAsync("SELECT COUNT(*) FROM companies WHERE company_code=@code",
            c => c.Parameters.AddWithValue("@code", code!), ct);
        if (codeTaken > 0)
            return Results.Json(ApiResponse<object>.Fail("Conflict", $"Tenant code '{code}' already exists"), statusCode: StatusCodes.Status409Conflict);

        var industry = Str(body, "industry") ?? "Logistics";
        var packageId = Long(body, "packageId");
        var seatLimit = (int)(Long(body, "seatLimit") ?? 5);
        var status = Str(body, "status") ?? "trial";
        var trialDays = (int)(Long(body, "trialDays") ?? 14);
        // Existing rows retain legacy_allow through the additive schema default;
        // newly provisioned customers are commercially isolated by default.
        var policyMode = Str(body, "entitlementPolicyMode") ?? EntitlementService.PackageAllowlistPolicy;
        if (policyMode is not (EntitlementService.LegacyAllowPolicy or EntitlementService.PackageAllowlistPolicy))
            return Results.Json(ApiResponse<object>.Fail("Validation failed",
                "entitlementPolicyMode must be legacy_allow or package_allowlist"),
                statusCode: StatusCodes.Status400BadRequest);
        if (policyMode == EntitlementService.LegacyAllowPolicy &&
            !HasPlatformPermission(principal!.Permissions, "platform:entitlements:manage"))
            return Results.Json(ApiResponse<object>.Fail("Forbidden",
                "Creating a legacy-allow tenant requires platform entitlement-management permission."),
                statusCode: StatusCodes.Status403Forbidden);

        // Country profile (optional): resolve BEFORE insert so its default currency
        // seeds the subscription. Reject an unknown code rather than silently ignoring.
        var countryCode = Str(body, "countryCode") ?? Str(body, "country_code");
        CountryProfileService.CountryProfile? countryProfile = null;
        if (!string.IsNullOrWhiteSpace(countryCode))
        {
            countryProfile = await countries.GetAsync(countryCode!, ct);
            if (countryProfile is null)
                return Results.Json(ApiResponse<object>.Fail("Validation failed", $"Unknown country_code: {countryCode}"), statusCode: StatusCodes.Status400BadRequest);
        }

        // Explicit billingCurrency in the body wins; otherwise inherit the country
        // profile default; otherwise USD.
        var currency = Str(body, "billingCurrency") ?? countryProfile?.DefaultCurrency ?? "USD";

        long companyId;
        try
        {
            companyId = await db.InsertAsync(
                "INSERT INTO companies (company_code, name, industry, status, entitlement_policy_mode) VALUES (@code, @name, @ind, 'Active', @policy)",
                c =>
                {
                    c.Parameters.AddWithValue("@code", code!);
                    c.Parameters.AddWithValue("@name", name!);
                    c.Parameters.AddWithValue("@ind", industry);
                    c.Parameters.AddWithValue("@policy", policyMode);
                }, ct);
        }
        catch (Npgsql.PostgresException pex) when (pex.SqlState == "23505") // race on unique company_code
        {
            return Results.Json(ApiResponse<object>.Fail("Conflict", $"Tenant code '{code}' already exists"), statusCode: StatusCodes.Status409Conflict);
        }

        var mrrCents = packageId.HasValue ? await ComputeMrrAsync(db, packageId.Value, seatLimit, ct) : 0;

        // Extended firmographic / contact attributes captured on the New Tenant form.
        // Nullable — only overwrites the fresh company row's columns when provided.
        await db.ExecuteAsync(
            @"UPDATE companies SET
                legal_name = @legal, website = @web, fleet_size = @fleet, tax_id = @tax,
                primary_contact_name = @pcn, primary_contact_email = @pce,
                primary_contact_phone = @pcp, billing_email = @bill
              WHERE id=@id",
            c =>
            {
                c.Parameters.AddWithValue("@id", companyId);
                c.Parameters.AddWithValue("@legal", (object?)Str(body, "legalName") ?? DBNull.Value);
                c.Parameters.AddWithValue("@web", (object?)Str(body, "website") ?? DBNull.Value);
                c.Parameters.AddWithValue("@fleet", (object?)(int?)Long(body, "fleetSize") ?? DBNull.Value);
                c.Parameters.AddWithValue("@tax", (object?)Str(body, "taxId") ?? DBNull.Value);
                c.Parameters.AddWithValue("@pcn", (object?)Str(body, "primaryContactName") ?? DBNull.Value);
                c.Parameters.AddWithValue("@pce", (object?)Str(body, "primaryContactEmail") ?? DBNull.Value);
                c.Parameters.AddWithValue("@pcp", (object?)Str(body, "primaryContactPhone") ?? DBNull.Value);
                c.Parameters.AddWithValue("@bill", (object?)Str(body, "billingEmail") ?? DBNull.Value);
            }, ct);

        await db.ExecuteAsync(
            @"INSERT INTO tenant_subscriptions (company_id, package_id, status, seat_limit, billing_currency, mrr_cents, billing_cycle, contract_start, contract_end, trial_ends_at, account_owner, support_owner)
              VALUES (@cid, @pid, @status, @seats, @cur, @mrr, @cycle, @cs::date, @ce::date,
                      CASE WHEN @status='trial' THEN NOW() + (@trialDays || ' day')::interval ELSE NULL END,
                      @ao, @so)",
            c =>
            {
                c.Parameters.AddWithValue("@cid", companyId);
                c.Parameters.AddWithValue("@pid", (object?)packageId ?? DBNull.Value);
                c.Parameters.AddWithValue("@status", status);
                c.Parameters.AddWithValue("@seats", seatLimit);
                c.Parameters.AddWithValue("@cur", currency);
                c.Parameters.AddWithValue("@mrr", mrrCents);
                c.Parameters.AddWithValue("@cycle", Str(body, "billingCycle") ?? "monthly");
                c.Parameters.AddWithValue("@cs", (object?)Str(body, "contractStart") ?? DBNull.Value);
                c.Parameters.AddWithValue("@ce", (object?)Str(body, "contractEnd") ?? DBNull.Value);
                c.Parameters.AddWithValue("@trialDays", trialDays.ToString());
                c.Parameters.AddWithValue("@ao", (object?)Str(body, "accountOwner") ?? DBNull.Value);
                c.Parameters.AddWithValue("@so", (object?)Str(body, "supportOwner") ?? DBNull.Value);
            }, ct);

        if (packageId.HasValue)
            await SeedEntitlementsFromPackageAsync(db, companyId, packageId.Value, principal!.Email, ct);

        // Country cascade: populate company country/currency/timezone and auto-enable
        // the profile's feature keys as country defaults (never locks — the entitlement
        // override path can still toggle any of them afterwards).
        CountryProfileService.CascadeResult? cascade = null;
        if (countryProfile is not null)
            cascade = await countries.ApplyToTenantAsync(companyId, countryCode!, principal!.Email, ct);

        // Optional tenant admin invite. A cross-tenant email collision is REFUSED (never
        // relocated) — the tenant is still created, but without an admin invite, and the
        // response says so instead of silently stealing another tenant's account.
        var adminEmail = Str(body, "adminEmail");
        object? adminInvite = null;
        if (!string.IsNullOrWhiteSpace(adminEmail))
        {
            var invite = await CreateAdminInviteAsync(http, db, companyId, adminEmail!, Str(body, "adminName") ?? "Tenant Admin", ct);
            adminInvite = invite.Status == AdminInviteStatus.CrossTenantConflict
                ? new { email = adminEmail, sent = false, invited = false, error = "That email already belongs to another tenant; the tenant was created without an admin invite. Re-issue the invite with a different admin email." }
                : new { email = adminEmail, sent = invite.EmailSent, invited = true, error = (string?)null };
        }

        // Give the new tenant the standard flag set (seeded enabled — these are kill
        // switches / ramp controls over features that already ship, not hidden features).
        await flags.SeedDefaultsAsync(companyId, ct);

        await AuditAsync(db, principal!, http, "tenant.created", "Tenant", companyId, companyId,
            new { name, code, status, packageId, seatLimit, entitlementPolicyMode = policyMode, countryCode = cascade?.CountryCode, currency = cascade?.Currency, autoEnabled = cascade?.EnabledFeatures }, ct);

        return Results.Ok(ApiResponse<object>.Ok(new
        {
            id = companyId, name, code, status, entitlementPolicyMode = policyMode,
            country = cascade?.CountryCode,
            currency = cascade?.Currency ?? currency,
            autoEnabledFeatures = cascade?.EnabledFeatures ?? [],
            adminInvite,
        }, "Tenant created"));
    }

    internal static async Task<IResult> TenantUpdate(long id, HttpContext http, Dictionary<string, object?> body, Database db, CountryProfileService countries, CancellationToken ct)
    {
        var (principal, error) = await RequireAsync(http, db, "platform:tenants:manage", ct);
        if (error is not null) return error;

        var exists = await db.ScalarLongAsync("SELECT COUNT(*) FROM companies WHERE id=@id",
            c => c.Parameters.AddWithValue("@id", id), ct);
        if (exists == 0) return Results.Json(ApiResponse<object>.Fail("Not found"), statusCode: StatusCodes.Status404NotFound);

        // Operating region (optional): validate against country_profiles BEFORE any
        // writes, then run the same cascade as tenant creation so region-gated
        // modules and country-default entitlements follow the reassignment.
        var countryCode = Str(body, "countryCode") ?? Str(body, "country_code");
        if (!string.IsNullOrWhiteSpace(countryCode))
        {
            var countryProfile = await countries.GetAsync(countryCode!, ct);
            if (countryProfile is null)
                return Results.Json(ApiResponse<object>.Fail("Validation failed", $"Unknown country_code: {countryCode}"), statusCode: StatusCodes.Status400BadRequest);
        }

        await db.ExecuteAsync(
            @"UPDATE tenant_subscriptions SET
                seat_limit = COALESCE(@seats, seat_limit),
                billing_currency = COALESCE(@cur, billing_currency),
                billing_cycle = COALESCE(@cycle, billing_cycle),
                account_owner = COALESCE(@ao, account_owner),
                support_owner = COALESCE(@so, support_owner),
                contract_start = COALESCE(@cs, contract_start),
                contract_end = COALESCE(@ce, contract_end),
                updated_at = NOW()
              WHERE company_id=@id",
            c =>
            {
                c.Parameters.AddWithValue("@id", id);
                c.Parameters.AddWithValue("@seats", (object?)Long(body, "seatLimit") ?? DBNull.Value);
                c.Parameters.AddWithValue("@cur", (object?)Str(body, "billingCurrency") ?? DBNull.Value);
                c.Parameters.AddWithValue("@cycle", (object?)Str(body, "billingCycle") ?? DBNull.Value);
                c.Parameters.AddWithValue("@ao", (object?)Str(body, "accountOwner") ?? DBNull.Value);
                c.Parameters.AddWithValue("@so", (object?)Str(body, "supportOwner") ?? DBNull.Value);
                c.Parameters.AddWithValue("@cs", (object?)Str(body, "contractStart") ?? DBNull.Value);
                c.Parameters.AddWithValue("@ce", (object?)Str(body, "contractEnd") ?? DBNull.Value);
            }, ct);

        // Full company-profile edit — every field the New Tenant form captures is now
        // editable (previously only `name` could be changed). COALESCE keeps any field
        // the caller omitted.
        await db.ExecuteAsync(
            @"UPDATE companies SET
                name                  = COALESCE(@n, name),
                industry              = COALESCE(@ind, industry),
                legal_name            = COALESCE(@legal, legal_name),
                website               = COALESCE(@web, website),
                fleet_size            = COALESCE(@fleet, fleet_size),
                tax_id                = COALESCE(@tax, tax_id),
                primary_contact_name  = COALESCE(@pcn, primary_contact_name),
                primary_contact_email = COALESCE(@pce, primary_contact_email),
                primary_contact_phone = COALESCE(@pcp, primary_contact_phone),
                billing_email         = COALESCE(@bill, billing_email)
              WHERE id=@id",
            c =>
            {
                c.Parameters.AddWithValue("@id", id);
                c.Parameters.AddWithValue("@n", (object?)Str(body, "name") ?? DBNull.Value);
                c.Parameters.AddWithValue("@ind", (object?)Str(body, "industry") ?? DBNull.Value);
                c.Parameters.AddWithValue("@legal", (object?)Str(body, "legalName") ?? DBNull.Value);
                c.Parameters.AddWithValue("@web", (object?)Str(body, "website") ?? DBNull.Value);
                c.Parameters.AddWithValue("@fleet", (object?)(int?)Long(body, "fleetSize") ?? DBNull.Value);
                c.Parameters.AddWithValue("@tax", (object?)Str(body, "taxId") ?? DBNull.Value);
                c.Parameters.AddWithValue("@pcn", (object?)Str(body, "primaryContactName") ?? DBNull.Value);
                c.Parameters.AddWithValue("@pce", (object?)Str(body, "primaryContactEmail") ?? DBNull.Value);
                c.Parameters.AddWithValue("@pcp", (object?)Str(body, "primaryContactPhone") ?? DBNull.Value);
                c.Parameters.AddWithValue("@bill", (object?)Str(body, "billingEmail") ?? DBNull.Value);
            }, ct);

        CountryProfileService.CascadeResult? cascade = null;
        if (!string.IsNullOrWhiteSpace(countryCode))
            cascade = await countries.ApplyToTenantAsync(id, countryCode!, principal!.Email, ct);

        await AuditAsync(db, principal!, http, "tenant.updated", "Tenant", id, id,
            new { fields = body.Keys, countryCode = cascade?.CountryCode, autoEnabled = cascade?.EnabledFeatures }, ct);
        return Results.Ok(ApiResponse<object>.Ok(new
        {
            id,
            country = cascade?.CountryCode,
            currency = cascade?.Currency,
            autoEnabledFeatures = cascade?.EnabledFeatures,
        }, "Tenant updated"));
    }

    internal static async Task<IResult> TenantStatus(long id, HttpContext http, Dictionary<string, object?> body, Database db, CancellationToken ct)
    {
        var (principal, error) = await RequireAsync(http, db, "platform:tenants:manage", ct);
        if (error is not null) return error;

        var tenantExists = await db.ScalarLongAsync("SELECT COUNT(*) FROM companies WHERE id=@id",
            c => c.Parameters.AddWithValue("@id", id), ct);
        if (tenantExists == 0) return Results.Json(ApiResponse<object>.Fail("Not found"), statusCode: StatusCodes.Status404NotFound);

        var action = (Str(body, "action") ?? "").ToLowerInvariant();
        var days = (int)(Long(body, "days") ?? 14);

        var applied = await ApplyTenantStatusAsync(db, id, action, days, ct);
        if (applied is null)
            return Results.Json(ApiResponse<object>.Fail("Invalid action", "Use activate|suspend|cancel|extend-trial|reactivate|manual-contract"), statusCode: StatusCodes.Status400BadRequest);

        await AuditAsync(db, principal!, http, $"tenant.{action}", "Tenant", id, id, new { newStatus = applied.Value.NewStatus, sessionsRevoked = applied.Value.Revoked }, ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id, status = applied.Value.NewStatus, sessionsRevoked = applied.Value.Revoked }, $"Tenant {action} applied"));
    }

    // Core subscription-status transition — shared by the single-tenant TenantStatus
    // handler and the bulk TenantBulk handler so the tenant_subscriptions write, the
    // companies status mirror, and the mandatory session revocation on suspend/cancel
    // can never diverge between the two entry points. Returns null for an unrecognized
    // action; the caller is responsible for the audit row.
    private static async Task<(string NewStatus, int Revoked)?> ApplyTenantStatusAsync(
        Database db, long id, string action, int days, CancellationToken ct)
    {
        string? newStatus = action switch
        {
            "activate" or "reactivate" => "active",
            "suspend" => "suspended",
            "cancel" => "cancelled",
            "extend-trial" or "extend_trial" => "trial",
            "manual-contract" or "manual_contract" => "manual_contract",
            _ => null,
        };
        if (newStatus is null) return null;

        if (action is "extend-trial" or "extend_trial")
        {
            await db.ExecuteAsync(
                "UPDATE tenant_subscriptions SET status='trial', trial_ends_at = GREATEST(COALESCE(trial_ends_at, NOW()), NOW()) + (@d || ' day')::interval, updated_at=NOW() WHERE company_id=@id",
                c => { c.Parameters.AddWithValue("@d", days.ToString()); c.Parameters.AddWithValue("@id", id); }, ct);
        }
        else
        {
            await db.ExecuteAsync(
                "UPDATE tenant_subscriptions SET status=@s, updated_at=NOW() WHERE company_id=@id",
                c => { c.Parameters.AddWithValue("@s", newStatus); c.Parameters.AddWithValue("@id", id); }, ct);
        }

        // Mirror suspension/cancellation onto the company so tenant login can be gated.
        var companyStatus = newStatus switch { "suspended" => "Suspended", "cancelled" => "Cancelled", _ => "Active" };
        await db.ExecuteAsync("UPDATE companies SET status=@s WHERE id=@id",
            c => { c.Parameters.AddWithValue("@s", companyStatus); c.Parameters.AddWithValue("@id", id); }, ct);

        // Revoke active sessions immediately on suspend/cancel — otherwise a user who
        // is already logged in keeps operating until their token expires (up to 8h).
        // Blocking new logins is not enough; existing sessions must be killed too.
        var revoked = 0;
        if (newStatus is "suspended" or "cancelled")
            revoked = await db.ExecuteAsync("DELETE FROM user_sessions WHERE company_id=@id",
                c => c.Parameters.AddWithValue("@id", id), ct);

        return (newStatus, revoked);
    }

    // Bulk tenant operations — the platform Tenants table's multi-select action bar.
    // Every action routes through the SAME persistence + audit path as its single-row
    // counterpart; there is no bulk-only shortcut that could bypass session revocation
    // or the offboarding cascade. One bad row does not fail the batch — outcomes are
    // reported per-id so the operator sees exactly what happened.
    private static async Task<IResult> TenantBulk(
        HttpContext http, Dictionary<string, object?> body, Database db, TenantOffboardingService offboarding, CancellationToken ct)
    {
        var action = (Str(body, "action") ?? "").ToLowerInvariant();
        var allowed = new[] { "activate", "reactivate", "suspend", "cancel", "extend-trial", "manual-contract", "revoke-sessions", "assign-package", "delete" };
        if (!allowed.Contains(action))
            return Results.Json(ApiResponse<object>.Fail("Invalid action",
                "Use activate|suspend|cancel|extend-trial|manual-contract|revoke-sessions|assign-package|delete"),
                statusCode: StatusCodes.Status400BadRequest);

        var ids = ReadLongArray(body, "ids").Distinct().ToList();
        if (ids.Count == 0)
            return Results.Json(ApiResponse<object>.Fail("Validation failed", "ids must be a non-empty array"), statusCode: StatusCodes.Status400BadRequest);
        if (ids.Count > 200)
            return Results.Json(ApiResponse<object>.Fail("Validation failed", "A bulk action is limited to 200 tenants at once"), statusCode: StatusCodes.Status400BadRequest);

        // Hard delete demands the dedicated offboard permission plus an explicit typed
        // confirmation, mirroring the single-tenant guard. Everything else is manage.
        var isDelete = action == "delete";
        var (principal, error) = await RequireAsync(http, db, isDelete ? "platform:tenants:offboard" : "platform:tenants:manage", ct);
        if (error is not null) return error;

        // The literal "DELETE" is too easy to fire by accident (copy/paste from docs,
        // a saved request). Require the caller to echo back the exact tenant count
        // being deleted, same spirit as the per-tenant company_code guard below.
        if (isDelete && !string.Equals(Str(body, "confirm"), ids.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal))
            return Results.Json(ApiResponse<object>.Fail("Confirmation required",
                $"To permanently delete these {ids.Count} tenants and ALL their data, send {{\"confirm\":\"{ids.Count}\"}}."),
                statusCode: StatusCodes.Status400BadRequest);

        // assign-package needs a target package for the whole batch; seatLimit is an
        // optional shared override (null → each tenant keeps its current seat_limit).
        var assignPackageId = Long(body, "packageId");
        var assignSeatOverride = Long(body, "seatLimit") is { } sl ? (int)sl : (int?)null;
        if (action == "assign-package" && !assignPackageId.HasValue)
            return Results.Json(ApiResponse<object>.Fail("Validation failed", "packageId is required for assign-package"), statusCode: StatusCodes.Status400BadRequest);

        var days = (int)(Long(body, "days") ?? 14);
        var results = new List<object>();
        var succeeded = 0;

        foreach (var id in ids)
        {
            try
            {
                var exists = await db.ScalarLongAsync("SELECT COUNT(*) FROM companies WHERE id=@id",
                    c => c.Parameters.AddWithValue("@id", id), ct);
                if (exists == 0) { results.Add(new { id, ok = false, error = "Not found" }); continue; }

                if (action == "delete")
                {
                    var del = await offboarding.DeleteTenantAsync(id, ct);
                    results.Add(new { id, ok = true, rowsDeleted = del.TotalRowsDeleted });
                }
                else if (action == "revoke-sessions")
                {
                    var revoked = await db.ExecuteAsync("DELETE FROM user_sessions WHERE company_id=@id",
                        c => c.Parameters.AddWithValue("@id", id), ct);
                    results.Add(new { id, ok = true, sessionsRevoked = revoked });
                }
                else if (action == "assign-package")
                {
                    var (seatLimit, mrrCents) = await ApplyAssignPackageAsync(db, id, assignPackageId!.Value, assignSeatOverride, principal!.Email, ct);
                    results.Add(new { id, ok = true, packageId = assignPackageId, seatLimit, mrrCents });
                }
                else
                {
                    var applied = await ApplyTenantStatusAsync(db, id, action, days, ct);
                    results.Add(new { id, ok = true, status = applied!.Value.NewStatus, sessionsRevoked = applied.Value.Revoked });
                }
                succeeded++;
            }
            catch (Exception ex)
            {
                results.Add(new { id, ok = false, error = ex.Message });
            }
        }

        await AuditAsync(db, principal!, http, $"tenant.bulk.{action}", "Tenant", null, null,
            new { action, requested = ids.Count, succeeded, failed = ids.Count - succeeded, ids }, ct);

        return Results.Ok(ApiResponse<object>.Ok(
            new { action, requested = ids.Count, succeeded, failed = ids.Count - succeeded, results },
            $"Bulk {action}: {succeeded}/{ids.Count} succeeded"));
    }

    internal static async Task<IResult> TenantAssignPackage(long id, HttpContext http, Dictionary<string, object?> body, Database db, CancellationToken ct)
    {
        var (principal, error) = await RequireAsync(http, db, "platform:tenants:manage", ct);
        if (error is not null) return error;

        var packageId = Long(body, "packageId");
        if (!packageId.HasValue)
            return Results.Json(ApiResponse<object>.Fail("Validation failed", "packageId is required"), statusCode: StatusCodes.Status400BadRequest);

        var targetExists = await db.ScalarLongAsync("SELECT COUNT(*) FROM companies WHERE id=@id",
            c => c.Parameters.AddWithValue("@id", id), ct);
        if (targetExists == 0)
            return Results.Json(ApiResponse<object>.Fail("Not found", "Tenant not found"), statusCode: StatusCodes.Status404NotFound);
        var packageExists = await db.ScalarLongAsync("SELECT COUNT(*) FROM packages WHERE id=@id AND active=true",
            c => c.Parameters.AddWithValue("@id", packageId.Value), ct);
        if (packageExists == 0)
            return Results.Json(ApiResponse<object>.Fail("Not found", "Active package not found"), statusCode: StatusCodes.Status404NotFound);

        var seatOverride = Long(body, "seatLimit") is { } s ? (int)s : (int?)null;
        var (seatLimit, mrrCents) = await ApplyAssignPackageAsync(db, id, packageId.Value, seatOverride, principal!.Email, ct);

        await AuditAsync(db, principal!, http, "tenant.package.assigned", "Tenant", id, id, new { packageId, seatLimit, mrrCents }, ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id, packageId, mrrCents }, "Package assigned"));
    }

    // Core assign-package transition — shared by the single-tenant TenantAssignPackage
    // handler and the bulk TenantBulk handler so the tenant_subscriptions upsert and the
    // package entitlement seeding can never diverge between the two entry points. When
    // seatOverride is null the tenant's current seat_limit is reused (falling back to 5).
    internal static async Task<(int SeatLimit, long MrrCents)> ApplyAssignPackageAsync(
        Database db, long id, long packageId, int? seatOverride, string actor, CancellationToken ct)
    {
        return await db.RunInSystemTransactionAsync(async () =>
        {
            var packageExists = await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM packages WHERE id=@id AND active=true",
                c => c.Parameters.AddWithValue("@id", packageId), ct);
            if (packageExists == 0) throw new InvalidOperationException("Active package not found.");

            var seatLimit = seatOverride ?? (int)await db.ScalarLongAsync(
                "SELECT seat_limit FROM tenant_subscriptions WHERE company_id=@id", c => c.Parameters.AddWithValue("@id", id), ct);
            if (seatLimit <= 0) seatLimit = 5;

            var mrrCents = await ComputeMrrAsync(db, packageId, seatLimit, ct);

            await db.ExecuteAsync(
                @"INSERT INTO tenant_subscriptions (company_id, package_id, seat_limit, mrr_cents, status)
                  VALUES (@id, @pid, @seats, @mrr, 'active')
                  ON CONFLICT (company_id) DO UPDATE SET package_id=@pid, seat_limit=@seats, mrr_cents=@mrr, updated_at=NOW()",
                c =>
                {
                    c.Parameters.AddWithValue("@id", id);
                    c.Parameters.AddWithValue("@pid", packageId);
                    c.Parameters.AddWithValue("@seats", seatLimit);
                    c.Parameters.AddWithValue("@mrr", mrrCents);
                }, ct);

            // Reassignment replaces package-derived rights only. Explicit Platform
            // overrides and country/market-pack grants survive. Missing rows remain
            // allowed in legacy mode and are denied in package_allowlist mode.
            await db.ExecuteAsync(
                "DELETE FROM tenant_entitlements WHERE company_id=@id AND source='package'",
                c => c.Parameters.AddWithValue("@id", id), ct);
            await SeedEntitlementsFromPackageAsync(db, id, packageId, actor, ct);
            return (seatLimit, mrrCents);
        }, ct);
    }

    internal static async Task<IResult> TenantResetInvite(long id, HttpContext http, Dictionary<string, object?> body, Database db, CancellationToken ct)
    {
        var (principal, error) = await RequireAsync(http, db, "platform:tenants:manage", ct);
        if (error is not null) return error;
        var adminEmail = Str(body, "adminEmail");
        if (string.IsNullOrWhiteSpace(adminEmail))
            return Results.Json(ApiResponse<object>.Fail("Validation failed", "adminEmail is required"), statusCode: StatusCodes.Status400BadRequest);

        var invite = await CreateAdminInviteAsync(http, db, id, adminEmail!, Str(body, "adminName") ?? "Tenant Admin", ct);
        if (invite.Status == AdminInviteStatus.CrossTenantConflict)
        {
            await AuditAsync(db, principal!, http, "tenant.admin_invite.cross_tenant_denied", "Tenant", id, id, new { adminEmail }, ct);
            return Results.Json(ApiResponse<object>.Fail("Conflict",
                "That email already belongs to another tenant. Use a different admin email."),
                statusCode: StatusCodes.Status409Conflict);
        }

        await AuditAsync(db, principal!, http, "tenant.admin_invite.reset", "Tenant", id, id, new { adminEmail, emailSent = invite.EmailSent }, ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id, adminEmail, emailSent = invite.EmailSent }, "Admin invite reset"));
    }

    internal static async Task<IResult> TenantRevokeSessions(long id, HttpContext http, Database db, CancellationToken ct)
    {
        var (principal, error) = await RequireAsync(http, db, "platform:tenants:manage", ct);
        if (error is not null) return error;

        var exists = await db.ScalarLongAsync("SELECT COUNT(*) FROM companies WHERE id=@id",
            c => c.Parameters.AddWithValue("@id", id), ct);
        if (exists == 0) return Results.Json(ApiResponse<object>.Fail("Not found"), statusCode: StatusCodes.Status404NotFound);

        var revoked = await db.ExecuteAsync("DELETE FROM user_sessions WHERE company_id=@id",
            c => c.Parameters.AddWithValue("@id", id), ct);

        await AuditAsync(db, principal!, http, "tenant.sessions_revoked", "Tenant", id, id, new { sessionsRevoked = revoked }, ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id, sessionsRevoked = revoked }, "Tenant sessions revoked"));
    }

    // Tenant user directory — who can actually sign in to this tenant.
    private static async Task<IResult> TenantUsers(long id, HttpContext http, Database db, CancellationToken ct)
    {
        var (_, error) = await RequireAsync(http, db, "platform:tenants:view", ct);
        if (error is not null) return error;
        // NB: `users` has no last_login_at column — only the columns below exist.
        var rows = await db.QueryAsync(
            @"SELECT id, full_name, email, role_name, status, customer_id, password_changed_at, created_at,
                     (password_hash IS NOT NULL AND password_hash <> '') AS has_password,
                     (SELECT COUNT(*) FROM user_sessions s WHERE s.user_id = users.id) AS active_sessions
              FROM users
              WHERE company_id=@id AND COALESCE(status,'') <> 'Deleted'
              ORDER BY (role_name ILIKE '%admin%') DESC, full_name",
            c => c.Parameters.AddWithValue("@id", id), ct);
        // The assignable roles are the tenant's own plus the system catalog, so the
        // editor offers real role names instead of a free-text field that can quietly
        // strip a user of every permission.
        var roles = await db.QueryAsync(
            @"SELECT name, is_system FROM roles WHERE company_id IS NULL OR company_id=@id ORDER BY name",
            c => c.Parameters.AddWithValue("@id", id), ct);
        return Results.Ok(ApiResponse<object>.Ok(new { users = rows, roles }));
    }

    // Platform-initiated password reset for a tenant user. Generates a strong one-time
    // password, sets it, kills that user's sessions, and returns the password ONCE so the
    // operator can hand it over. Deliberately does NOT depend on SMTP, and never changes
    // the user's status (a disabled user stays disabled).
    private static async Task<IResult> TenantUserResetPassword(long id, long userId, HttpContext http, Database db, CancellationToken ct)
    {
        var (principal, error) = await RequireAsync(http, db, "platform:tenants:manage", ct);
        if (error is not null) return error;

        var user = await db.QuerySingleAsync(
            "SELECT id, email, full_name FROM users WHERE id=@uid AND company_id=@cid",
            c => { c.Parameters.AddWithValue("@uid", userId); c.Parameters.AddWithValue("@cid", id); }, ct);
        if (user is null)
            return Results.Json(ApiResponse<object>.Fail("Not found", "That user does not belong to this tenant"),
                statusCode: StatusCodes.Status404NotFound);

        var temp = GenerateTempPassword();
        // Mirrors the canonical self-service reset path: set the hash, stamp the change,
        // and CLEAR the lockout counters — a locked-out user is usually the reason an
        // operator is resetting in the first place.
        await db.ExecuteAsync(
            @"UPDATE users SET
                password_hash=@h, demo_password='', password_changed_at=NOW(),
                failed_login_attempts=0, locked_until=NULL
              WHERE id=@uid AND company_id=@cid",
            c =>
            {
                c.Parameters.AddWithValue("@h", PlatformSchemaService.HashPassword(temp));
                c.Parameters.AddWithValue("@uid", userId);
                c.Parameters.AddWithValue("@cid", id);
            }, ct);
        var revoked = await db.ExecuteAsync("DELETE FROM user_sessions WHERE user_id=@uid",
            c => c.Parameters.AddWithValue("@uid", userId), ct);

        await AuditAsync(db, principal!, http, "tenant.user.password_reset", "User", userId, id,
            new { email = user["email"], sessionsRevoked = revoked }, ct);

        return Results.Ok(ApiResponse<object>.Ok(new
        {
            userId,
            email = user["email"],
            fullName = user["fullName"],
            temporaryPassword = temp,
            sessionsRevoked = revoked,
        }, "Temporary password generated — copy it now, it is shown only once"));
    }

    // Roles carrying tenant-wide authority. Used both to seed a new administrator
    // and to guard against removing the last one.
    private static readonly string[] TenantAdminRoles = ["Company Admin", "Super Admin", "Reseller / Partner Admin"];

    private static bool IsTenantAdminRole(string? roleName) =>
        roleName is not null && TenantAdminRoles.Contains(roleName, StringComparer.OrdinalIgnoreCase);

    // Counts the tenant's remaining people who can actually sign in AND administer.
    // Every mutation that could reduce this to zero is refused — a tenant with no
    // reachable administrator is a support ticket that platform admin then has to
    // dig them out of.
    private static Task<long> ActiveAdminCountAsync(Database db, long companyId, long? excludingUserId, CancellationToken ct) =>
        db.ScalarLongAsync(
            @"SELECT COUNT(*) FROM users
              WHERE company_id=@cid AND status='Active' AND (@ex IS NULL OR id <> @ex)
                AND role_name = ANY(@roles)",
            c =>
            {
                c.Parameters.AddWithValue("@cid", companyId);
                c.Parameters.AddWithValue("@ex", (object?)excludingUserId ?? DBNull.Value);
                c.Parameters.AddWithValue("@roles", TenantAdminRoles);
            }, ct);

    private static bool LooksLikeEmail(string s) =>
        System.Text.RegularExpressions.Regex.IsMatch(s, @"^[^\s@]+@[^\s@]+\.[^\s@]+$");

    // Creates a tenant user from the platform console and arms a set-password invite.
    // This is the escape hatch for "the customer's only admin left the company".
    internal static async Task<IResult> TenantUserCreate(long id, HttpContext http, Dictionary<string, object?> body, Database db, CancellationToken ct)
    {
        var (principal, error) = await RequireAsync(http, db, "platform:tenants:manage", ct);
        if (error is not null) return error;

        var tenantExists = await db.ScalarLongAsync("SELECT COUNT(*) FROM companies WHERE id=@id",
            c => c.Parameters.AddWithValue("@id", id), ct);
        if (tenantExists == 0) return Results.Json(ApiResponse<object>.Fail("Not found"), statusCode: StatusCodes.Status404NotFound);

        var email = Str(body, "email")?.Trim();
        var fullName = Str(body, "fullName")?.Trim();
        var roleName = Str(body, "roleName")?.Trim() ?? "Company Admin";
        if (string.IsNullOrWhiteSpace(email) || !LooksLikeEmail(email!))
            return Results.Json(ApiResponse<object>.Fail("Validation failed", "A valid email address is required"), statusCode: StatusCodes.Status400BadRequest);
        if (string.IsNullOrWhiteSpace(fullName))
            return Results.Json(ApiResponse<object>.Fail("Validation failed", "fullName is required"), statusCode: StatusCodes.Status400BadRequest);

        var roleKnown = await db.ScalarLongAsync(
            "SELECT COUNT(*) FROM roles WHERE LOWER(name)=LOWER(@r) AND (company_id IS NULL OR company_id=@cid)",
            c => { c.Parameters.AddWithValue("@r", roleName); c.Parameters.AddWithValue("@cid", id); }, ct);
        if (roleKnown == 0)
            return Results.Json(ApiResponse<object>.Fail("Validation failed", $"Unknown role: {roleName}"), statusCode: StatusCodes.Status400BadRequest);

        // Same cross-tenant refusal as the admin invite: an email owned by another
        // company is never relocated, because that is account takeover by typo.
        var existing = await db.QuerySingleAsync(
            "SELECT id, company_id FROM users WHERE LOWER(email)=LOWER(@e) LIMIT 1",
            c => c.Parameters.AddWithValue("@e", email!), ct);
        if (existing is not null)
        {
            var owner = Convert.ToInt64(existing["companyId"]);
            return Results.Json(ApiResponse<object>.Fail("Conflict",
                owner == id
                    ? "A user with that email already exists in this tenant — edit that user instead."
                    : "That email address already belongs to a user in a different tenant."),
                statusCode: StatusCodes.Status409Conflict);
        }

        var userId = await db.InsertAsync(
            @"INSERT INTO users (company_id, full_name, email, role_name, status)
              VALUES (@cid, @name, @email, @role, 'Pending') RETURNING id",
            c =>
            {
                c.Parameters.AddWithValue("@cid", id);
                c.Parameters.AddWithValue("@name", fullName!);
                c.Parameters.AddWithValue("@email", email!);
                c.Parameters.AddWithValue("@role", roleName);
            }, ct);

        // Hand back a temporary password as well as the invite, because SMTP is not
        // guaranteed and an operator on a call needs something to read out now.
        var temp = GenerateTempPassword();
        await db.ExecuteAsync(
            "UPDATE users SET password_hash=@h, password_changed_at=NOW(), status='Active' WHERE id=@uid",
            c =>
            {
                c.Parameters.AddWithValue("@h", PlatformSchemaService.HashPassword(temp));
                c.Parameters.AddWithValue("@uid", userId);
            }, ct);

        await AuditAsync(db, principal!, http, "tenant.user.created", "User", userId, id,
            new { email, roleName }, ct);

        return Results.Ok(ApiResponse<object>.Ok(new
        {
            userId, email, fullName, roleName, status = "Active",
            temporaryPassword = temp,
        }, "User created — copy the temporary password now, it is shown only once"));
    }

    // Edits identity and access for one tenant user: sign-in email, display name,
    // role, and enabled/disabled state.
    internal static async Task<IResult> TenantUserUpdate(long id, long userId, HttpContext http, Dictionary<string, object?> body, Database db, CancellationToken ct)
    {
        var (principal, error) = await RequireAsync(http, db, "platform:tenants:manage", ct);
        if (error is not null) return error;

        var user = await db.QuerySingleAsync(
            "SELECT id, email, full_name, role_name, status FROM users WHERE id=@uid AND company_id=@cid",
            c => { c.Parameters.AddWithValue("@uid", userId); c.Parameters.AddWithValue("@cid", id); }, ct);
        if (user is null)
            return Results.Json(ApiResponse<object>.Fail("Not found", "That user does not belong to this tenant"),
                statusCode: StatusCodes.Status404NotFound);

        var currentEmail = user["email"]?.ToString() ?? "";
        var currentRole = user["roleName"]?.ToString();
        var currentStatus = user["status"]?.ToString() ?? "";

        var newEmail = Str(body, "email")?.Trim();
        var newName = Str(body, "fullName")?.Trim();
        var newRole = Str(body, "roleName")?.Trim();
        var newStatus = Str(body, "status")?.Trim();

        var emailChanged = !string.IsNullOrWhiteSpace(newEmail)
            && !string.Equals(newEmail, currentEmail, StringComparison.OrdinalIgnoreCase);

        if (emailChanged)
        {
            if (!LooksLikeEmail(newEmail!))
                return Results.Json(ApiResponse<object>.Fail("Validation failed", "That is not a valid email address"), statusCode: StatusCodes.Status400BadRequest);
            var clash = await db.QuerySingleAsync(
                "SELECT id, company_id FROM users WHERE LOWER(email)=LOWER(@e) AND id <> @uid LIMIT 1",
                c => { c.Parameters.AddWithValue("@e", newEmail!); c.Parameters.AddWithValue("@uid", userId); }, ct);
            if (clash is not null)
                return Results.Json(ApiResponse<object>.Fail("Conflict",
                    Convert.ToInt64(clash["companyId"]) == id
                        ? "Another user in this tenant already uses that email."
                        : "That email address already belongs to a user in a different tenant."),
                    statusCode: StatusCodes.Status409Conflict);
        }

        if (!string.IsNullOrWhiteSpace(newRole))
        {
            var roleKnown = await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM roles WHERE LOWER(name)=LOWER(@r) AND (company_id IS NULL OR company_id=@cid)",
                c => { c.Parameters.AddWithValue("@r", newRole!); c.Parameters.AddWithValue("@cid", id); }, ct);
            if (roleKnown == 0)
                return Results.Json(ApiResponse<object>.Fail("Validation failed", $"Unknown role: {newRole}"), statusCode: StatusCodes.Status400BadRequest);
        }

        if (!string.IsNullOrWhiteSpace(newStatus) && newStatus is not ("Active" or "Disabled" or "Pending"))
            return Results.Json(ApiResponse<object>.Fail("Validation failed", "status must be Active, Disabled or Pending"), statusCode: StatusCodes.Status400BadRequest);

        // Lockout guard: demoting or disabling the tenant's last reachable admin
        // would leave nobody inside able to administer it.
        var losesAdmin =
            (IsTenantAdminRole(currentRole) && !string.IsNullOrWhiteSpace(newRole) && !IsTenantAdminRole(newRole))
            || (IsTenantAdminRole(currentRole) && string.Equals(currentStatus, "Active", StringComparison.OrdinalIgnoreCase)
                && newStatus is not null && !string.Equals(newStatus, "Active", StringComparison.OrdinalIgnoreCase));
        if (losesAdmin && await ActiveAdminCountAsync(db, id, userId, ct) == 0)
            return Results.Json(ApiResponse<object>.Fail("Refused",
                "This is the tenant's last active administrator. Promote or create another admin first."),
                statusCode: StatusCodes.Status409Conflict);

        await db.ExecuteAsync(
            @"UPDATE users SET
                email     = COALESCE(@email, email),
                full_name = COALESCE(@name, full_name),
                role_name = COALESCE(@role, role_name),
                status    = COALESCE(@status, status)
              WHERE id=@uid AND company_id=@cid",
            c =>
            {
                c.Parameters.AddWithValue("@email", (object?)newEmail ?? DBNull.Value);
                c.Parameters.AddWithValue("@name", (object?)newName ?? DBNull.Value);
                c.Parameters.AddWithValue("@role", (object?)newRole ?? DBNull.Value);
                c.Parameters.AddWithValue("@status", (object?)newStatus ?? DBNull.Value);
                c.Parameters.AddWithValue("@uid", userId);
                c.Parameters.AddWithValue("@cid", id);
            }, ct);

        // The email IS the sign-in identity and the role IS the permission set, so a
        // live session issued under the old values must not survive the change.
        var revoked = 0;
        var mustRevoke = emailChanged
            || (!string.IsNullOrWhiteSpace(newRole) && !string.Equals(newRole, currentRole, StringComparison.OrdinalIgnoreCase))
            || (newStatus is not null && !string.Equals(newStatus, "Active", StringComparison.OrdinalIgnoreCase));
        if (mustRevoke)
            revoked = await db.ExecuteAsync("DELETE FROM user_sessions WHERE user_id=@uid",
                c => c.Parameters.AddWithValue("@uid", userId), ct);

        await AuditAsync(db, principal!, http, "tenant.user.updated", "User", userId, id,
            new
            {
                emailFrom = emailChanged ? currentEmail : null,
                emailTo = emailChanged ? newEmail : null,
                roleFrom = newRole is null ? null : currentRole,
                roleTo = newRole,
                statusFrom = newStatus is null ? null : currentStatus,
                statusTo = newStatus,
                sessionsRevoked = revoked,
            }, ct);

        return Results.Ok(ApiResponse<object>.Ok(new
        {
            userId,
            email = newEmail ?? currentEmail,
            roleName = newRole ?? currentRole,
            status = newStatus ?? currentStatus,
            sessionsRevoked = revoked,
        }, mustRevoke && revoked > 0
            ? $"User updated — {revoked} active session{(revoked == 1 ? "" : "s")} revoked"
            : "User updated"));
    }

    // Re-arms the set-password invite for any tenant user, not only the original
    // admin. Returns whether SMTP actually carried it, so the operator knows
    // whether they still have to hand the link over themselves.
    internal static async Task<IResult> TenantUserResendInvite(long id, long userId, HttpContext http, Database db, CancellationToken ct)
    {
        var (principal, error) = await RequireAsync(http, db, "platform:tenants:manage", ct);
        if (error is not null) return error;

        var user = await db.QuerySingleAsync(
            "SELECT email, full_name FROM users WHERE id=@uid AND company_id=@cid",
            c => { c.Parameters.AddWithValue("@uid", userId); c.Parameters.AddWithValue("@cid", id); }, ct);
        if (user is null)
            return Results.Json(ApiResponse<object>.Fail("Not found", "That user does not belong to this tenant"),
                statusCode: StatusCodes.Status404NotFound);

        var email = user["email"]?.ToString() ?? "";
        var fullName = user["fullName"]?.ToString() ?? "Team member";

        // Deliberately NOT CreateAdminInviteAsync: that path forces role_name to
        // 'Company Admin', which would quietly promote a dispatcher whose invite an
        // operator merely re-sent. Mint the same single-use token, touch no role.
        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var tokenHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawToken)));
        await db.ExecuteAsync(
            @"INSERT INTO password_reset_tokens (user_id, company_id, token_hash, expires_at, request_ip_hash)
              VALUES (@uid, @cid, @hash, NOW() + INTERVAL '7 days', @ip)
              ON CONFLICT (user_id) DO UPDATE SET token_hash=EXCLUDED.token_hash, expires_at=EXCLUDED.expires_at,
                consumed_at=NULL, request_ip_hash=EXCLUDED.request_ip_hash, created_at=NOW()",
            c =>
            {
                c.Parameters.AddWithValue("@uid", userId);
                c.Parameters.AddWithValue("@cid", id);
                c.Parameters.AddWithValue("@hash", tokenHash);
                c.Parameters.AddWithValue("@ip", InviteRequestIpHash(http));
            }, ct);

        var emailSent = await TrySendTenantInviteEmailAsync(http, email, fullName, rawToken, ct);

        await AuditAsync(db, principal!, http, "tenant.user.invite_resent", "User", userId, id,
            new { email, emailSent }, ct);
        return Results.Ok(ApiResponse<object>.Ok(new { userId, email, emailSent },
            emailSent
                ? "Invite emailed"
                : "Invite re-armed, but no email was sent (SMTP or the tenant public URL is not configured)"));
    }

    // Unambiguous alphabet (no O/0, I/l/1) so a handed-over password is easy to type.
    private static string GenerateTempPassword()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
        var bytes = RandomNumberGenerator.GetBytes(16);
        var sb = new System.Text.StringBuilder(16);
        foreach (var b in bytes) sb.Append(chars[b % chars.Length]);
        return sb.ToString();
    }

    private static async Task<IResult> TenantAudit(long id, HttpContext http, Database db, CancellationToken ct)
    {
        var (_, error) = await RequireAsync(http, db, "platform:tenants:view", ct);
        if (error is not null) return error;
        var rows = await db.QueryAsync(
            "SELECT action, entity_type, actor_email, actor_role, details_json, created_at FROM platform_audit_log WHERE target_company_id=@id ORDER BY created_at DESC LIMIT 100",
            c => c.Parameters.AddWithValue("@id", id), ct);
        return Results.Ok(ApiResponse<object>.Ok(rows));
    }

    // Release evidence capture for the exact tenant control boundary. This is a
    // deliberately redacted snapshot: it includes connector readiness but never
    // connector config/secrets, and configuration posture only as booleans.
    private static async Task<IResult> TenantControlSnapshot(
        long id, HttpContext http, Database db, IConfiguration configuration, CancellationToken ct)
    {
        var (principal, error) = await RequireAsync(http, db, "platform:tenants:view", ct);
        if (error is not null) return error;

        var tenant = await db.QuerySingleAsync(TenantSelect + " WHERE c.id=@id",
            c => c.Parameters.AddWithValue("@id", id), ct);
        if (tenant is null)
            return Results.Json(ApiResponse<object>.Fail("Not found"), statusCode: StatusCodes.Status404NotFound);

        var entitlements = await db.QueryAsync(
            "SELECT module_key,enabled,limit_value,tier,source,updated_by,updated_at FROM tenant_entitlements WHERE company_id=@id ORDER BY module_key",
            c => c.Parameters.AddWithValue("@id", id), ct);
        var policyMode = tenant.GetValueOrDefault("entitlementPolicyMode")?.ToString()
            ?? EntitlementService.LegacyAllowPolicy;
        var entitlementRows = entitlements.ToDictionary(
            row => row.GetValueOrDefault("moduleKey")?.ToString() ?? "",
            row => row,
            StringComparer.Ordinal);
        var effectiveEntitlements = GovernedEntitlementModuleKeys.Order(StringComparer.Ordinal).Select(moduleKey =>
        {
            entitlementRows.TryGetValue(moduleKey, out var row);
            var enabled = row is not null
                ? Convert.ToBoolean(row.GetValueOrDefault("enabled") ?? false)
                : policyMode == EntitlementService.LegacyAllowPolicy;
            return new
            {
                moduleKey,
                enabled,
                decision = row is null
                    ? (enabled ? "legacy inherited allow" : "package allowlist default deny")
                    : $"{row.GetValueOrDefault("source") ?? "override"} {(enabled ? "allow" : "deny")}",
                tier = row?.GetValueOrDefault("tier"),
                limitValue = row?.GetValueOrDefault("limitValue"),
                updatedAt = row?.GetValueOrDefault("updatedAt"),
            };
        }).ToArray();

        var marketPacks = await db.QueryAsync(
            "SELECT pack_code,status,price_override_cents,enabled_by,enabled_at,updated_at FROM tenant_market_packs WHERE company_id=@id ORDER BY pack_code",
            c => c.Parameters.AddWithValue("@id", id), ct);
        var featureFlags = await db.QueryAsync(
            "SELECT flag_key,enabled,rollout_pct,environment,updated_at FROM feature_flags WHERE company_id=@id ORDER BY flag_key",
            c => c.Parameters.AddWithValue("@id", id), ct);
        var branches = await db.QueryAsync(
            "SELECT id,name,branch_code,status FROM branches WHERE company_id=@id ORDER BY name,id",
            c => c.Parameters.AddWithValue("@id", id), ct);
        var personas = await db.QueryAsync(
            @"SELECT COALESCE(r.name,u.role_name,'Unassigned') role_name,COUNT(*) user_count,
                     COUNT(*) FILTER (WHERE LOWER(u.status)='active') active_user_count
              FROM users u LEFT JOIN roles r ON r.id=u.role_id
              WHERE u.company_id=@id GROUP BY COALESCE(r.name,u.role_name,'Unassigned') ORDER BY role_name",
            c => c.Parameters.AddWithValue("@id", id), ct);
        var roleRows = await db.QueryAsync(
            @"SELECT id,name,company_id,permissions_json
              FROM roles WHERE company_id IS NULL OR company_id=@id
              ORDER BY name,company_id NULLS FIRST,id",
            c => c.Parameters.AddWithValue("@id", id), ct);
        var effectiveRoleGrants = new List<object>(roleRows.Count);
        var roleRefs = new Dictionary<long, string>();
        foreach (var role in roleRows)
        {
            var roleId = Convert.ToInt64(role.GetValueOrDefault("id") ?? 0L);
            var roleName = role.GetValueOrDefault("name")?.ToString() ?? "Unassigned";
            var permissions = await EndpointMappings.ResolveEffectivePermissionsAsync(
                roleId, roleName, role.GetValueOrDefault("permissionsJson"), null, db, ct);
            var roleRef = OpaqueControlRef(id, "role", roleId.ToString());
            roleRefs[roleId] = roleRef;
            effectiveRoleGrants.Add(new
            {
                roleRef,
                roleName,
                scope = role.GetValueOrDefault("companyId") is null or DBNull ? "global" : "tenant",
                permissions = permissions.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            });
        }
        var userRows = await db.QueryAsync(
            @"SELECT u.id,u.status,u.role_id,u.role_name,u.permissions_json,u.branch_id,
                     b.branch_code,b.status branch_status
              FROM users u LEFT JOIN branches b ON b.id=u.branch_id AND b.company_id=u.company_id
              WHERE u.company_id=@id ORDER BY u.id",
            c => c.Parameters.AddWithValue("@id", id), ct);
        var userBranchBindings = new List<object>(userRows.Count);
        foreach (var user in userRows)
        {
            var userId = Convert.ToInt64(user.GetValueOrDefault("id") ?? 0L);
            var roleId = Convert.ToInt64(user.GetValueOrDefault("roleId") ?? 0L);
            var roleName = user.GetValueOrDefault("roleName")?.ToString() ?? "Unassigned";
            var permissions = await EndpointMappings.ResolveEffectivePermissionsAsync(
                roleId, roleName, null, user.GetValueOrDefault("permissionsJson"), db, ct);
            userBranchBindings.Add(new
            {
                subjectRef = OpaqueControlRef(id, "user", userId.ToString()),
                status = user.GetValueOrDefault("status"),
                roleRef = roleId > 0 && roleRefs.TryGetValue(roleId, out var roleRef) ? roleRef : null,
                roleName,
                grantSource = roleId > 0 ? "role" : "legacy_user",
                branchBinding = user.GetValueOrDefault("branchId") is null or DBNull ? "tenant_wide" : "branch",
                branchId = user.GetValueOrDefault("branchId"),
                branchCode = user.GetValueOrDefault("branchCode"),
                branchStatus = user.GetValueOrDefault("branchStatus"),
                effectivePermissionCount = permissions.Length,
                effectiveGrantSha256 = Sha256Hex(JsonSerializer.Serialize(
                    permissions.Order(StringComparer.OrdinalIgnoreCase).ToArray())),
            });
        }
        var integrations = await db.QueryAsync(
            @"SELECT integration_key,provider_name,category,status,scope,last_sync_at,
                     last_tested_at,last_test_ok
              FROM integrations WHERE company_id=@id ORDER BY category,provider_name",
            c => c.Parameters.AddWithValue("@id", id), ct);
        var recentAudit = await db.QueryAsync(
            @"SELECT id,action,entity_type,actor_role,created_at
              FROM platform_audit_log WHERE target_company_id=@id ORDER BY id DESC LIMIT 25",
            c => c.Parameters.AddWithValue("@id", id), ct);

        // Deliberately exclude billing/contact/tax/profile fields from the release
        // evidence artifact. They do not affect access decisions and may contain PII.
        var tenantControl = new
        {
            id = tenant.GetValueOrDefault("id"),
            name = tenant.GetValueOrDefault("name"),
            companyCode = tenant.GetValueOrDefault("companyCode"),
            companyStatus = tenant.GetValueOrDefault("companyStatus"),
            subscriptionStatus = tenant.GetValueOrDefault("status"),
            entitlementPolicyMode = tenant.GetValueOrDefault("entitlementPolicyMode"),
            packageName = tenant.GetValueOrDefault("packageName"),
            packageCode = tenant.GetValueOrDefault("packageCode"),
            seatLimit = tenant.GetValueOrDefault("seatLimit"),
            country = tenant.GetValueOrDefault("country"),
            currency = tenant.GetValueOrDefault("currency"),
            billingCurrency = tenant.GetValueOrDefault("billingCurrency"),
            trialEndsAt = tenant.GetValueOrDefault("trialEndsAt"),
            contractStart = tenant.GetValueOrDefault("contractStart"),
            contractEnd = tenant.GetValueOrDefault("contractEnd"),
            createdAt = tenant.GetValueOrDefault("createdAt"),
        };

        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
        var generatedAt = DateTimeOffset.UtcNow;
        var moduleCatalog = PlatformTenantModuleCatalog.Modules
            .Select(module => new { moduleKey = module.Key, requiredEntitlement = module.RequiredEntitlement })
            .ToArray();
        var governedModulesByEntitlement = PlatformTenantModuleCatalog.Modules
            .Where(module => module.RequiredEntitlement is not null)
            .GroupBy(module => module.RequiredEntitlement!, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var governedUiModuleCount = governedModulesByEntitlement.Values.Sum();
        var totalUiModuleCount = PlatformTenantModuleCatalog.Modules.Count;
        var includedCoreUiModuleCount = totalUiModuleCount - governedUiModuleCount;
        var commercialModel = new
        {
            governedUiModules = governedUiModuleCount,
            includedCoreUiModules = includedCoreUiModuleCount,
            totalUiModules = totalUiModuleCount,
            governedModulesByEntitlement,
            governedEntitlementKeys = GovernedEntitlementModuleKeys.Order(StringComparer.Ordinal).ToArray(),
            policyMode,
        };
        var semanticSnapshot = new
        {
            tenant = tenantControl,
            commercialModel,
            moduleCatalog,
            effectiveEntitlements = effectiveEntitlements.Select(row => new
            {
                row.moduleKey, row.enabled, row.decision, row.tier, row.limitValue,
            }).ToArray(),
            marketPacks = marketPacks.Select(WithoutVolatileControlFields).ToArray(),
            featureFlags = featureFlags.Select(WithoutVolatileControlFields).ToArray(),
            branches,
            personas,
            effectiveRoleGrants,
            userBranchBindings,
            integrations = integrations.Select(WithoutVolatileControlFields).ToArray(),
            environmentControls = new
            {
                environment,
                production = string.Equals(environment, "Production", StringComparison.OrdinalIgnoreCase),
                demoSeedEnabled = configuration.GetValue<bool>("DemoSeed:Enabled"),
                demoResetEnabled = configuration.GetValue<bool>("DemoSeed:ResetEnabled"),
                telemetrySimulatorEnabled = configuration.GetValue<bool>("Telemetry:Simulator:Enabled"),
                tenantRlsEnforced = configuration.GetValue<bool>("Rls:EnforceTenantContext"),
                systemConnectionConfigured = !string.IsNullOrWhiteSpace(configuration.GetConnectionString("SystemConnection")),
            },
        };
        var semanticSha256 = Sha256Hex(JsonSerializer.Serialize(semanticSnapshot));
        var snapshot = new
        {
            schemaVersion = 2,
            generatedAt,
            tenant = tenantControl,
            commercialModel,
            moduleCatalog,
            effectiveEntitlements,
            marketPacks,
            featureFlags,
            branches,
            personas,
            effectiveRoleGrants,
            userBranchBindings,
            integrations,
            environmentControls = new
            {
                environment,
                production = string.Equals(environment, "Production", StringComparison.OrdinalIgnoreCase),
                demoSeedEnabled = configuration.GetValue<bool>("DemoSeed:Enabled"),
                demoResetEnabled = configuration.GetValue<bool>("DemoSeed:ResetEnabled"),
                telemetrySimulatorEnabled = configuration.GetValue<bool>("Telemetry:Simulator:Enabled"),
                tenantRlsEnforced = configuration.GetValue<bool>("Rls:EnforceTenantContext"),
                systemConnectionConfigured = !string.IsNullOrWhiteSpace(configuration.GetConnectionString("SystemConnection")),
            },
            recentPlatformAudit = recentAudit,
            semanticComparison = new
            {
                profileVersion = 1,
                semanticSha256,
                excludes = new[] { "generatedAt", "recentPlatformAudit", "*.updatedAt", "*.enabledAt", "integrations.*.lastSyncAt", "integrations.*.lastTestedAt" },
            },
        };
        var canonicalJson = JsonSerializer.Serialize(snapshot);
        var snapshotSha256 = Sha256Hex(canonicalJson);

        await AuditAsync(db, principal!, http, "tenant.control_snapshot.captured", "Company", id, id,
            new { snapshotSha256, semanticSha256, generatedAt, schemaVersion = 2 }, ct);
        return Results.Ok(ApiResponse<object>.Ok(new { snapshotSha256, semanticSha256, snapshot }, "Control snapshot captured and audited"));
    }

    private static string OpaqueControlRef(long companyId, string kind, string value) =>
        Sha256Hex($"opstrax-control-snapshot:v1:{companyId}:{kind}:{value}")[..20];

    private static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static Dictionary<string, object?> WithoutVolatileControlFields(Dictionary<string, object?> row)
    {
        var stable = new Dictionary<string, object?>(row, StringComparer.Ordinal);
        foreach (var key in new[] { "updatedAt", "enabledAt", "lastSyncAt", "lastTestedAt" })
            stable.Remove(key);
        return stable;
    }

    // Hard delete a tenant and ALL its data (pilot "delete on request"). Schema-driven
    // cascade — see TenantOffboardingService. Requires an explicit confirm token in the
    // body ({"confirm":"<companyCode>"}) so a tenant can never be purged by a stray DELETE.
    private static async Task<IResult> TenantDelete(long id, HttpContext http, [Microsoft.AspNetCore.Mvc.FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] Dictionary<string, object?>? body, Database db, TenantOffboardingService offboarding, CancellationToken ct)
    {
        // Hard delete requires the dedicated offboard permission — deliberately NOT
        // granted by "platform:tenants:manage" so routine tenant admins (sales, CS)
        // can never purge a tenant. Super admin qualifies via the platform:* wildcard.
        var (principal, error) = await RequireAsync(http, db, "platform:tenants:offboard", ct);
        if (error is not null) return error;

        var tenant = await db.QuerySingleAsync(
            "SELECT id, name, company_code FROM companies WHERE id=@id",
            c => c.Parameters.AddWithValue("@id", id), ct);
        if (tenant is null)
            return Results.Json(ApiResponse<object>.Fail("Not found"), statusCode: StatusCodes.Status404NotFound);

        var companyCode = tenant["companyCode"]?.ToString() ?? "";
        var confirm = body is not null ? Str(body, "confirm") : null;
        if (!string.Equals(confirm, companyCode, StringComparison.Ordinal))
            return Results.Json(ApiResponse<object>.Fail("Confirmation required",
                $"To permanently delete this tenant and ALL its data, send {{\"confirm\":\"{companyCode}\"}}."),
                statusCode: StatusCodes.Status400BadRequest);

        var result = await offboarding.DeleteTenantAsync(id, ct);

        // Audit AFTER deletion; platform_audit_log is a platform table (not deleted with the
        // tenant), so the record of the offboarding survives.
        await AuditAsync(db, principal!, http, "tenant.deleted", "Tenant", id, id,
            new { companyCode, name = tenant["name"], result.TotalRowsDeleted, tableCount = result.DeletedByTable.Count }, ct);

        return Results.Ok(ApiResponse<object>.Ok(new
        {
            id,
            companyCode,
            companyDeleted = result.CompanyDeleted,
            totalRowsDeleted = result.TotalRowsDeleted,
            tablesAffected = result.DeletedByTable.Count,
        }, "Tenant permanently deleted"));
    }

    // ════════════════════════════════════════════════════════════════════════════
    // ENTITLEMENTS
    // ════════════════════════════════════════════════════════════════════════════

    internal static async Task<IResult> EntitlementsGet(long id, HttpContext http, Database db, CancellationToken ct)
    {
        var (_, error) = await RequireAsync(http, db, "platform:entitlements:view", ct);
        if (error is not null) return error;
        var rows = await db.QueryAsync(
            "SELECT module_key, enabled, limit_value, tier, source, updated_by, updated_at FROM tenant_entitlements WHERE company_id=@id ORDER BY module_key",
            c => c.Parameters.AddWithValue("@id", id), ct);
        return Results.Ok(ApiResponse<object>.Ok(rows));
    }

    internal static async Task<IResult> EntitlementsSet(long id, HttpContext http, Dictionary<string, object?> body, Database db, CancellationToken ct)
    {
        var (principal, error) = await RequireAsync(http, db, "platform:entitlements:manage", ct);
        if (error is not null) return error;

        var moduleKey = Str(body, "moduleKey");
        if (string.IsNullOrWhiteSpace(moduleKey))
            return Results.Json(ApiResponse<object>.Fail("Validation failed", "moduleKey is required"), statusCode: StatusCodes.Status400BadRequest);

        if (!GovernedEntitlementModuleKeys.Contains(moduleKey))
            return Results.Json(ApiResponse<object>.Fail("Validation failed",
                $"moduleKey must be one of: {string.Join(", ", GovernedEntitlementModuleKeys.Order())}"),
                statusCode: StatusCodes.Status400BadRequest);

        var tenantExists = await db.ScalarLongAsync("SELECT COUNT(*) FROM companies WHERE id=@id",
            c => c.Parameters.AddWithValue("@id", id), ct);
        if (tenantExists == 0) return Results.Json(ApiResponse<object>.Fail("Not found"), statusCode: StatusCodes.Status404NotFound);

        var enabled = Bool(body, "enabled") ?? true;
        var limit = Long(body, "limitValue");
        var tier = Str(body, "tier") ?? "standard";

        await db.ExecuteAsync(
            @"INSERT INTO tenant_entitlements (company_id, module_key, enabled, limit_value, tier, source, updated_by, updated_at)
              VALUES (@cid, @mk, @en, @lim, @tier, 'override', @by, NOW())
              ON CONFLICT (company_id, module_key) DO UPDATE
                SET enabled=@en, limit_value=@lim, tier=@tier, source='override', updated_by=@by, updated_at=NOW()",
            c =>
            {
                c.Parameters.AddWithValue("@cid", id);
                c.Parameters.AddWithValue("@mk", moduleKey!);
                c.Parameters.AddWithValue("@en", enabled);
                c.Parameters.AddWithValue("@lim", (object?)limit ?? DBNull.Value);
                c.Parameters.AddWithValue("@tier", tier);
                c.Parameters.AddWithValue("@by", principal!.Email);
            }, ct);

        await AuditAsync(db, principal!, http, enabled ? "entitlement.enabled" : "entitlement.disabled",
            "Entitlement", id, id, new { moduleKey, enabled, limit, tier }, ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id, moduleKey, enabled }, "Entitlement updated"));
    }

    // Changing the semantics of a missing entitlement row is a commercial control,
    // not a general tenant-profile edit. Restrict it to entitlement operators and
    // record the before/after state for the release control snapshot.
    internal static async Task<IResult> EntitlementPolicySet(long id, HttpContext http, Dictionary<string, object?> body, Database db, CancellationToken ct)
    {
        var (principal, error) = await RequireAsync(http, db, "platform:entitlements:manage", ct);
        if (error is not null) return error;

        var policyMode = Str(body, "policyMode") ?? Str(body, "entitlementPolicyMode");
        if (policyMode is not (EntitlementService.LegacyAllowPolicy or EntitlementService.PackageAllowlistPolicy))
            return Results.Json(ApiResponse<object>.Fail("Validation failed",
                "policyMode must be legacy_allow or package_allowlist"),
                statusCode: StatusCodes.Status400BadRequest);

        var current = await db.QuerySingleAsync(
            "SELECT entitlement_policy_mode FROM companies WHERE id=@id",
            c => c.Parameters.AddWithValue("@id", id), ct);
        if (current is null)
            return Results.Json(ApiResponse<object>.Fail("Not found"), statusCode: StatusCodes.Status404NotFound);

        var before = current["entitlementPolicyMode"]?.ToString() ?? EntitlementService.LegacyAllowPolicy;
        var reconciledPackageId = await db.RunInSystemTransactionAsync(async () =>
        {
            await db.ExecuteAsync(
                "UPDATE companies SET entitlement_policy_mode=@mode WHERE id=@id",
                c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@mode", policyMode); }, ct);

            // Historical package assignments only ever added rows. Reconcile package
            // sources while changing policy so stale rights from an older package can
            // never become part of an allowlist. Overrides/country/add-ons survive.
            await db.ExecuteAsync(
                "DELETE FROM tenant_entitlements WHERE company_id=@id AND source='package'",
                c => c.Parameters.AddWithValue("@id", id), ct);
            var packageId = await db.ScalarLongAsync(
                "SELECT COALESCE(package_id,0) FROM tenant_subscriptions WHERE company_id=@id",
                c => c.Parameters.AddWithValue("@id", id), ct);
            if (packageId > 0)
                await SeedEntitlementsFromPackageAsync(db, id, packageId, principal!.Email, ct);
            return packageId > 0 ? packageId : (long?)null;
        }, ct);

        await AuditAsync(db, principal!, http, "entitlement.policy.changed", "Company", id, id,
            new { before, after = policyMode, reconciledPackageId }, ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id, policyMode, reconciledPackageId }, "Entitlement policy updated"));
    }

    // ════════════════════════════════════════════════════════════════════════════
    // COUNTRY PROFILES
    // ════════════════════════════════════════════════════════════════════════════

    private static async Task<IResult> CountryProfilesList(HttpContext http, Database db, CountryProfileService countries, CancellationToken ct)
    {
        var (_, error) = await RequireAsync(http, db, "platform:countries:view", ct);
        if (error is not null) return error;
        var profiles = await countries.ListAsync(ct);
        return Results.Ok(ApiResponse<object>.Ok(profiles.Select(ToDto)));
    }

    private static async Task<IResult> CountryProfileGet(string code, HttpContext http, Database db, CountryProfileService countries, CancellationToken ct)
    {
        var (_, error) = await RequireAsync(http, db, "platform:countries:view", ct);
        if (error is not null) return error;
        var profile = await countries.GetAsync(code, ct);
        if (profile is null) return Results.Json(ApiResponse<object>.Fail("Not found"), statusCode: StatusCodes.Status404NotFound);
        return Results.Ok(ApiResponse<object>.Ok(ToDto(profile)));
    }

    private static Task<IResult> CountryProfileUpsert(HttpContext http, Dictionary<string, object?> body, Database db, CountryProfileService countries, CancellationToken ct)
        => CountryProfileUpsertCore(http, body, db, countries, null, ct);

    private static Task<IResult> CountryProfileUpsertByCode(string code, HttpContext http, Dictionary<string, object?> body, Database db, CountryProfileService countries, CancellationToken ct)
        => CountryProfileUpsertCore(http, body, db, countries, code, ct);

    private static async Task<IResult> CountryProfileUpsertCore(HttpContext http, Dictionary<string, object?> body, Database db, CountryProfileService countries, string? routeCode, CancellationToken ct)
    {
        var (principal, error) = await RequireAsync(http, db, "platform:countries:manage", ct);
        if (error is not null) return error;

        var countryCode = routeCode ?? Str(body, "countryCode") ?? Str(body, "country_code");
        if (string.IsNullOrWhiteSpace(countryCode) || countryCode.Trim().Length != 2)
            return Results.Json(ApiResponse<object>.Fail("Validation failed", "countryCode must be an ISO 3166-1 alpha-2 code"), statusCode: StatusCodes.Status400BadRequest);

        var name = Str(body, "countryName");
        var currency = Str(body, "defaultCurrency");
        var locale = Str(body, "defaultLocale");
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(currency) || string.IsNullOrWhiteSpace(locale))
            return Results.Json(ApiResponse<object>.Fail("Validation failed", "countryName, defaultCurrency and defaultLocale are required"), statusCode: StatusCodes.Status400BadRequest);

        var direction = (Str(body, "textDirection") ?? "ltr").ToLowerInvariant();
        if (direction is not ("ltr" or "rtl"))
            return Results.Json(ApiResponse<object>.Fail("Validation failed", "textDirection must be 'ltr' or 'rtl'"), statusCode: StatusCodes.Status400BadRequest);

        var features = ReadStringArray(body, "autoEnabledFeatures");
        var taxRate = Decimal(body, "defaultTaxRate");

        var profile = new CountryProfileService.CountryProfile(
            countryCode.Trim().ToUpperInvariant(),
            name!,
            currency!,
            locale!,
            direction,
            Str(body, "calendarSystem") ?? "gregorian",
            Str(body, "invoicingScheme") ?? "standard",
            Str(body, "taxIdLabel") ?? "Tax ID",
            taxRate,
            Str(body, "dataResidencyNote"),
            features);

        var saved = await countries.UpsertAsync(profile, ct);
        await AuditAsync(db, principal!, http, "country_profile.upserted", "CountryProfile", null, null,
            new { saved.CountryCode, saved.DefaultCurrency, saved.AutoEnabledFeatures }, ct);
        return Results.Ok(ApiResponse<object>.Ok(ToDto(saved), "Country profile saved"));
    }

    private static async Task<IResult> CountryProfileDelete(string code, HttpContext http, Database db, CountryProfileService countries, CancellationToken ct)
    {
        var (principal, error) = await RequireAsync(http, db, "platform:countries:manage", ct);
        if (error is not null) return error;
        var removed = await countries.DeleteAsync(code, ct);
        if (!removed) return Results.Json(ApiResponse<object>.Fail("Not found"), statusCode: StatusCodes.Status404NotFound);
        await AuditAsync(db, principal!, http, "country_profile.deleted", "CountryProfile", null, null, new { countryCode = code }, ct);
        return Results.Ok(ApiResponse<object>.Ok(new { countryCode = code.Trim().ToUpperInvariant() }, "Country profile deleted"));
    }

    private static object ToDto(CountryProfileService.CountryProfile p) => new
    {
        countryCode = p.CountryCode,
        countryName = p.CountryName,
        defaultCurrency = p.DefaultCurrency,
        defaultLocale = p.DefaultLocale,
        textDirection = p.TextDirection,
        calendarSystem = p.CalendarSystem,
        invoicingScheme = p.InvoicingScheme,
        taxIdLabel = p.TaxIdLabel,
        defaultTaxRate = p.DefaultTaxRate,
        dataResidencyNote = p.DataResidencyNote,
        autoEnabledFeatures = p.AutoEnabledFeatures,
    };

    // ════════════════════════════════════════════════════════════════════════════
    // PACKAGES
    // ════════════════════════════════════════════════════════════════════════════

    private static async Task<IResult> PackagesList(HttpContext http, Database db, CancellationToken ct)
    {
        var (_, error) = await RequireAsync(http, db, "platform:packages:view", ct);
        if (error is not null) return error;
        var rows = await db.QueryAsync(
            @"SELECT id, package_code, name, description, billing_interval, currency, base_price_cents, seat_price_cents,
                     included_seats, setup_fee_cents, annual_price_cents, module_keys, is_custom, active, created_at
              FROM packages ORDER BY is_custom, base_price_cents", ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(rows));
    }

    private static async Task<IResult> PackageCreate(HttpContext http, Dictionary<string, object?> body, Database db, CancellationToken ct)
    {
        var (principal, error) = await RequireAsync(http, db, "platform:packages:manage", ct);
        if (error is not null) return error;

        var name = Str(body, "name");
        if (string.IsNullOrWhiteSpace(name))
            return Results.Json(ApiResponse<object>.Fail("Validation failed", "name is required"), statusCode: StatusCodes.Status400BadRequest);
        var code = Str(body, "packageCode") ?? "PKG-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var (modules, moduleError) = ParseGovernedModuleKeys(body, defaultEmpty: true);
        if (moduleError is not null) return moduleError;

        var newId = await db.InsertAsync(
            @"INSERT INTO packages (package_code, name, description, billing_interval, currency, base_price_cents, seat_price_cents, included_seats, setup_fee_cents, annual_price_cents, module_keys, is_custom, active)
              VALUES (@code, @name, @desc, @interval, @cur, @base, @seat, @incl, @setup, @annual, CAST(@modules AS JSONB), @custom, true)",
            c =>
            {
                c.Parameters.AddWithValue("@code", code);
                c.Parameters.AddWithValue("@name", name!);
                c.Parameters.AddWithValue("@desc", (object?)Str(body, "description") ?? DBNull.Value);
                c.Parameters.AddWithValue("@interval", Str(body, "billingInterval") ?? "monthly");
                c.Parameters.AddWithValue("@cur", Str(body, "currency") ?? "USD");
                c.Parameters.AddWithValue("@base", Long(body, "basePriceCents") ?? 0);
                c.Parameters.AddWithValue("@seat", Long(body, "seatPriceCents") ?? 0);
                c.Parameters.AddWithValue("@incl", Long(body, "includedSeats") ?? 0);
                c.Parameters.AddWithValue("@setup", Long(body, "setupFeeCents") ?? 0);
                c.Parameters.AddWithValue("@annual", Long(body, "annualPriceCents") ?? 0);
                c.Parameters.AddWithValue("@modules", modules!);
                c.Parameters.AddWithValue("@custom", Bool(body, "isCustom") ?? false);
            }, ct);

        await AuditAsync(db, principal!, http, "package.created", "Package", newId, null, new { name, code }, ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id = newId, name, code }, "Package created"));
    }

    private static async Task<IResult> PackageUpdate(long id, HttpContext http, Dictionary<string, object?> body, Database db, CancellationToken ct)
    {
        var (principal, error) = await RequireAsync(http, db, "platform:packages:manage", ct);
        if (error is not null) return error;
        var (modules, moduleError) = ParseGovernedModuleKeys(body, defaultEmpty: false);
        if (moduleError is not null) return moduleError;
        await db.ExecuteAsync(
            @"UPDATE packages SET
                name = COALESCE(@name, name),
                description = COALESCE(@desc, description),
                base_price_cents = COALESCE(@base, base_price_cents),
                seat_price_cents = COALESCE(@seat, seat_price_cents),
                included_seats = COALESCE(@incl, included_seats),
                setup_fee_cents = COALESCE(@setup, setup_fee_cents),
                annual_price_cents = COALESCE(@annual, annual_price_cents),
                module_keys = COALESCE(CAST(@modules AS JSONB), module_keys),
                active = COALESCE(@active, active)
              WHERE id=@id",
            c =>
            {
                c.Parameters.AddWithValue("@id", id);
                c.Parameters.AddWithValue("@name", (object?)Str(body, "name") ?? DBNull.Value);
                c.Parameters.AddWithValue("@desc", (object?)Str(body, "description") ?? DBNull.Value);
                c.Parameters.AddWithValue("@base", (object?)Long(body, "basePriceCents") ?? DBNull.Value);
                c.Parameters.AddWithValue("@seat", (object?)Long(body, "seatPriceCents") ?? DBNull.Value);
                c.Parameters.AddWithValue("@incl", (object?)Long(body, "includedSeats") ?? DBNull.Value);
                c.Parameters.AddWithValue("@setup", (object?)Long(body, "setupFeeCents") ?? DBNull.Value);
                c.Parameters.AddWithValue("@annual", (object?)Long(body, "annualPriceCents") ?? DBNull.Value);
                c.Parameters.AddWithValue("@modules", (object?)modules ?? DBNull.Value);
                c.Parameters.AddWithValue("@active", (object?)Bool(body, "active") ?? DBNull.Value);
            }, ct);
        await AuditAsync(db, principal!, http, "package.updated", "Package", id, null, body.Keys, ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id }, "Package updated"));
    }

    private static (string? Json, IResult? Error) ParseGovernedModuleKeys(
        Dictionary<string, object?> body, bool defaultEmpty)
    {
        if (!body.TryGetValue("moduleKeys", out var raw) || raw is null)
            return (defaultEmpty ? "[]" : null, null);

        string[]? keys;
        try
        {
            keys = JsonSerializer.Deserialize<string[]>(JsonSerializer.Serialize(raw));
        }
        catch (JsonException)
        {
            keys = null;
        }

        if (keys is null || keys.Any(key => !GovernedEntitlementModuleKeys.Contains(key)))
            return (null, Results.Json(ApiResponse<object>.Fail("Validation failed",
                $"moduleKeys must be an array containing only: {string.Join(", ", GovernedEntitlementModuleKeys.Order())}"),
                statusCode: StatusCodes.Status400BadRequest));

        return (JsonSerializer.Serialize(keys.Distinct(StringComparer.Ordinal)), null);
    }

    // Delete a package. Refuses while any tenant is still subscribed to it — reassign
    // those tenants (or just deactivate the package) first, so a live subscription can
    // never be orphaned by a delete.
    private static async Task<IResult> PackageDelete(long id, HttpContext http, Database db, CancellationToken ct)
    {
        var (principal, error) = await RequireAsync(http, db, "platform:packages:manage", ct);
        if (error is not null) return error;

        var exists = await db.ScalarLongAsync("SELECT COUNT(*) FROM packages WHERE id=@id",
            c => c.Parameters.AddWithValue("@id", id), ct);
        if (exists == 0) return Results.Json(ApiResponse<object>.Fail("Not found"), statusCode: StatusCodes.Status404NotFound);

        var inUse = await db.ScalarLongAsync("SELECT COUNT(*) FROM tenant_subscriptions WHERE package_id=@id",
            c => c.Parameters.AddWithValue("@id", id), ct);
        if (inUse > 0)
            return Results.Json(ApiResponse<object>.Fail("Package in use",
                $"{inUse} tenant(s) are on this package. Reassign them, or deactivate the package instead of deleting it."),
                statusCode: StatusCodes.Status409Conflict);

        await db.ExecuteAsync("DELETE FROM packages WHERE id=@id", c => c.Parameters.AddWithValue("@id", id), ct);
        await AuditAsync(db, principal!, http, "package.deleted", "Package", id, null, null, ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id, deleted = true }, "Package deleted"));
    }

    // ════════════════════════════════════════════════════════════════════════════
    // BILLING & INVOICES
    // ════════════════════════════════════════════════════════════════════════════

    private static async Task<IResult> InvoicesList(HttpContext http, Database db, CancellationToken ct)
    {
        var (_, error) = await RequireAsync(http, db, "platform:billing:view", ct);
        if (error is not null) return error;
        var rows = await db.QueryAsync(
            @"SELECT i.id, i.invoice_number, i.status, i.kind, i.document_type, i.amount_cents, i.currency,
                     i.subtotal_cents, i.discount_cents, i.tax_total_cents, i.total_cents,
                     i.tax_country, i.tax_treatment, i.tax_label, i.period_start, i.period_end,
                     i.credit_note_of, i.issued_at, i.due_at, i.paid_at, c.name tenant, i.company_id,
                     (SELECT COUNT(*) FROM platform_invoice_lines l WHERE l.invoice_id = i.id) line_count
              FROM platform_invoices i JOIN companies c ON c.id = i.company_id
              ORDER BY i.created_at DESC LIMIT 200", ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(rows));
    }

    // Manual invoice creation. Every invoice now goes through the same itemized,
    // taxed path as a generated one: the caller supplies net lines (or a single
    // amount, which becomes one line), tax is determined from the tenant's country
    // of activation, and the document is saved as a draft. Unless the caller asks
    // for a draft it is issued immediately, which is what the old flat-amount API
    // did — so an existing caller keeps its behaviour and gains the line detail.
    internal static async Task<IResult> InvoiceCreate(HttpContext http, Dictionary<string, object?> body, Database db, PlatformBillingService billing, CancellationToken ct)
    {
        var (principal, error) = await RequireAsync(http, db, "platform:billing:manage", ct);
        if (error is not null) return error;

        var companyId = Long(body, "companyId");
        if (!companyId.HasValue)
            return Results.Json(ApiResponse<object>.Fail("Validation failed", "companyId is required"), statusCode: StatusCodes.Status400BadRequest);

        var exists = await db.ScalarLongAsync("SELECT COUNT(*) FROM companies WHERE id=@id",
            c => c.Parameters.AddWithValue("@id", companyId.Value), ct);
        if (exists == 0)
            return Results.Json(ApiResponse<object>.Fail("Not found", "No such tenant"), statusCode: StatusCodes.Status404NotFound);

        var lines = PlatformBillingEndpoints.ReadLines(body);
        if (lines.Count == 0)
        {
            // Flat-amount compatibility: one line rather than a bare number, so even
            // the simplest invoice still reconciles line-by-line.
            var amount = Long(body, "amountCents") ?? 0;
            if (amount == 0)
                return Results.Json(ApiResponse<object>.Fail("Validation failed", "Provide either lines[] or a non-zero amountCents"), statusCode: StatusCodes.Status400BadRequest);
            lines.Add(new PlatformBillingService.DraftLine
            {
                Source = "manual",
                Description = Str(body, "notes") ?? "Platform services",
                ChargeModel = "flat",
                Quantity = 1,
                UnitPriceCents = amount,
                GrossAmountCents = amount,
            });
        }

        var tax = await billing.ResolveTaxAsync(companyId.Value, ct);
        PlatformBillingService.Price(lines, tax);

        var dueDays = (int)(Long(body, "dueDays") ?? 15);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var periodStart = new DateOnly(today.Year, today.Month, 1);
        var draft = new PlatformBillingService.Draft(
            companyId.Value, "", periodStart, periodStart.AddMonths(1).AddDays(-1), tax, lines,
            lines.Sum(l => l.NetAmountCents), lines.Sum(l => l.DiscountCents),
            lines.Sum(l => l.TaxAmountCents),
            lines.Sum(l => l.NetAmountCents) + lines.Sum(l => l.TaxAmountCents), []);

        var newId = await billing.SaveDraftAsync(companyId.Value, draft,
            Str(body, "kind") ?? "recurring", "invoice", dueDays, Str(body, "notes"), principal!.Email, ct);

        var requested = (Str(body, "status") ?? "sent").ToLowerInvariant();
        string? number = null;
        if (requested != "draft") number = await billing.IssueAsync(newId, principal!.Email, ct);

        await AuditAsync(db, principal!, http, "invoice.created", "Invoice", newId, companyId,
            new { number, lines = lines.Count, subtotal = draft.SubtotalCents, tax = draft.TaxTotalCents, total = draft.TotalCents }, ct);
        return Results.Ok(ApiResponse<object>.Ok(
            new { id = newId, invoiceNumber = number, status = requested == "draft" ? "draft" : "sent", totalCents = draft.TotalCents },
            requested == "draft" ? "Draft invoice created" : "Invoice created and issued"));
    }

    internal static async Task<IResult> InvoiceMarkPaid(long id, HttpContext http, Database db, CancellationToken ct)
    {
        var (principal, error) = await RequireAsync(http, db, "platform:billing:manage", ct);
        if (error is not null) return error;
        var companyId = await db.ScalarLongAsync("SELECT company_id FROM platform_invoices WHERE id=@id", c => c.Parameters.AddWithValue("@id", id), ct);
        await db.ExecuteAsync("UPDATE platform_invoices SET status='paid', paid_at=NOW() WHERE id=@id",
            c => c.Parameters.AddWithValue("@id", id), ct);
        await AuditAsync(db, principal!, http, "invoice.paid", "Invoice", id, companyId, null, ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id, status = "paid" }, "Invoice marked paid"));
    }

    // Bulk invoice operations — the Collections table multi-select action bar.
    // mark-paid / void are idempotent status writes. `delete` is restricted to
    // UNISSUED drafts: once a document has been issued it holds an allocated
    // sequence number, and removing it puts a hole in a gap-free tax sequence that
    // neither the ledger nor a tax authority can account for. Issued documents are
    // corrected by void or credit note, never by deletion.
    internal static async Task<IResult> InvoiceBulk(HttpContext http, Dictionary<string, object?> body, Database db, CancellationToken ct)
    {
        var (principal, error) = await RequireAsync(http, db, "platform:billing:manage", ct);
        if (error is not null) return error;

        var action = (Str(body, "action") ?? "").ToLowerInvariant();
        if (action is not ("mark-paid" or "void" or "delete"))
            return Results.Json(ApiResponse<object>.Fail("Invalid action", "Use mark-paid|void|delete"), statusCode: StatusCodes.Status400BadRequest);

        var ids = ReadLongArray(body, "ids").Distinct().ToList();
        if (ids.Count == 0)
            return Results.Json(ApiResponse<object>.Fail("Validation failed", "ids must be a non-empty array"), statusCode: StatusCodes.Status400BadRequest);
        if (ids.Count > 200)
            return Results.Json(ApiResponse<object>.Fail("Validation failed", "A bulk action is limited to 200 invoices at once"), statusCode: StatusCodes.Status400BadRequest);

        var results = new List<object>();
        var succeeded = 0;

        foreach (var id in ids)
        {
            try
            {
                var companyId = await db.ScalarLongAsync("SELECT company_id FROM platform_invoices WHERE id=@id", c => c.Parameters.AddWithValue("@id", id), ct);
                if (companyId == 0) { results.Add(new { id, ok = false, error = "Not found" }); continue; }

                var affected = action switch
                {
                    "mark-paid" => await db.ExecuteAsync("UPDATE platform_invoices SET status='paid', paid_at=NOW() WHERE id=@id AND status<>'paid'", c => c.Parameters.AddWithValue("@id", id), ct),
                    // Void never touches an already-paid invoice — collected revenue is immutable.
                    "void" => await db.ExecuteAsync("UPDATE platform_invoices SET status='void' WHERE id=@id AND status<>'paid'", c => c.Parameters.AddWithValue("@id", id), ct),
                    // Guarded by BOTH status and the provisional number pattern, so a
                    // draft that somehow reached a real number is still protected.
                    _ => await db.ExecuteAsync(
                        "DELETE FROM platform_invoices WHERE id=@id AND status='draft' AND invoice_number LIKE 'DRAFT-%'",
                        c => c.Parameters.AddWithValue("@id", id), ct),
                };

                if (affected == 0 && action == "delete")
                {
                    results.Add(new { id, ok = false, error = "Only an unissued draft can be deleted — void it or raise a credit note" });
                    continue;
                }
                if (affected == 0 && action == "void")
                { results.Add(new { id, ok = false, error = "Cannot void a paid invoice" }); continue; }
                if (affected == 0 && action == "mark-paid")
                { results.Add(new { id, ok = true, note = "already paid" }); succeeded++; continue; }

                await AuditAsync(db, principal!, http, $"invoice.{action}", "Invoice", id, companyId, null, ct);
                results.Add(new { id, ok = true });
                succeeded++;
            }
            catch (Exception ex)
            {
                results.Add(new { id, ok = false, error = ex.Message });
            }
        }

        await AuditAsync(db, principal!, http, $"invoice.bulk.{action}", "Invoice", null, null,
            new { action, requested = ids.Count, succeeded, failed = ids.Count - succeeded, ids }, ct);

        return Results.Ok(ApiResponse<object>.Ok(
            new { action, requested = ids.Count, succeeded, failed = ids.Count - succeeded, results },
            $"Bulk {action}: {succeeded}/{ids.Count} succeeded"));
    }

    // ════════════════════════════════════════════════════════════════════════════
    // CUSTOMER SUCCESS — health scores
    // ════════════════════════════════════════════════════════════════════════════

    private static async Task<IResult> HealthScores(HttpContext http, Database db, CancellationToken ct)
    {
        var (_, error) = await RequireAsync(http, db, "platform:health:view", ct);
        if (error is not null) return error;

        // Health derived from real signals: subscription status, user count, overdue invoices, renewal proximity.
        var rows = await db.QueryAsync(
            @"SELECT c.id, c.name tenant, ts.status, ts.contract_end,
                     (SELECT COUNT(*) FROM users u WHERE u.company_id=c.id) user_count,
                     (SELECT COUNT(*) FROM platform_invoices i WHERE i.company_id=c.id AND i.status IN ('overdue','sent')) open_invoices
              FROM companies c JOIN tenant_subscriptions ts ON ts.company_id=c.id
              ORDER BY c.name", ct: ct);

        var result = rows.Select(r =>
        {
            var status = r["status"]?.ToString() ?? "";
            var users = Convert.ToInt64(r["userCount"]);
            var openInv = Convert.ToInt64(r["openInvoices"]);
            var score = 100;
            if (status == "past_due") score -= 40;
            if (status == "suspended") score -= 60;
            if (status == "trial") score -= 10;
            if (users == 0) score -= 25;
            score -= (int)Math.Min(openInv * 10, 30);
            score = Math.Clamp(score, 0, 100);
            var health = score >= 75 ? "green" : score >= 50 ? "yellow" : "red";
            var actions = new List<string>();
            if (status == "past_due" || openInv > 0) actions.Add("payment_follow_up");
            if (users == 0) actions.Add("schedule_training");
            if (status == "trial") actions.Add("trial_conversion");
            if (score >= 75 && status == "active") actions.Add("upsell");
            return new
            {
                id = r["id"], tenant = r["tenant"], status, userCount = users, openInvoices = openInv,
                healthScore = score, health, recommendedActions = actions,
            };
        });
        return Results.Ok(ApiResponse<object>.Ok(result));
    }

    // ════════════════════════════════════════════════════════════════════════════
    // RELIABILITY CENTER (platform-scoped)
    // ════════════════════════════════════════════════════════════════════════════

    private static async Task<IResult> ReliabilityCenter(
        HttpContext http, Database db,
        Opstrax.Api.Observability.ReliabilityService reliability, CancellationToken ct)
    {
        var (_, error) = await RequireAsync(http, db, "platform:health:view", ct);
        if (error is not null) return error;

        var snapshot = await reliability.GetSnapshotAsync(ct);
        return Results.Ok(ApiResponse<object>.Ok(snapshot, $"Reliability: {snapshot.Status}"));
    }

    private static async Task<IResult> ReliabilitySlo(
        HttpContext http, Database db,
        Opstrax.Api.Observability.SloService slo, CancellationToken ct)
    {
        var (_, error) = await RequireAsync(http, db, "platform:health:view", ct);
        if (error is not null) return error;

        var report = slo.Evaluate();
        return Results.Ok(ApiResponse<object>.Ok(new
        {
            report,
            definitions = Opstrax.Api.Observability.SloService.Definitions,
            alertRules  = Opstrax.Api.Observability.SloService.AlertRules,
        }, $"SLO status: {report.OverallStatus}"));
    }

    private static async Task<IResult> ReliabilityAckIncident(
        long id, HttpContext http, Database db, IncidentService incidents, CancellationToken ct)
    {
        // Mutating an incident requires manage, not the read grant (a read-only role must not change state).
        var (principal, error) = await RequireAsync(http, db, "platform:health:manage", ct);
        if (error is not null) return error;

        await incidents.AcknowledgeAsync(id, principal!.Email, ct);
        await AuditAsync(db, principal!, http, "incident.acknowledged", "platform_incident", id, null, null, ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id, acknowledgedBy = principal!.Email }, "Incident acknowledged"));
    }

    private static async Task<IResult> ReliabilityResolveIncident(
        long id, HttpContext http, Database db, IncidentService incidents, CancellationToken ct)
    {
        var (principal, error) = await RequireAsync(http, db, "platform:health:manage", ct);
        if (error is not null) return error;

        var body = await http.Request.ReadFromJsonAsync<PlatformIncidentResolve>(ct);
        await incidents.ResolveAsync(id, body?.RootCause, body?.ActionsTaken, principal!.Email, ct);
        await AuditAsync(db, principal!, http, "incident.resolved", "platform_incident", id, null,
            new { body?.RootCause, body?.ActionsTaken }, ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id, status = "resolved" }, "Incident resolved"));
    }

    private sealed record PlatformIncidentResolve(string? RootCause = null, string? ActionsTaken = null);

    // ════════════════════════════════════════════════════════════════════════════
    // AUDIT + ROLES
    // ════════════════════════════════════════════════════════════════════════════

    // Filtered, keyset-paged audit read. "Every privileged action against tenant X
    // between March and June" is the question this log exists to answer, and an
    // unfiltered 250-row dump cannot answer it. Tenant names are joined in so an
    // investigator is not reading raw company ids.
    internal static async Task<IResult> AuditList(HttpContext http, Database db, CancellationToken ct)
    {
        var (_, error) = await RequireAsync(http, db, "platform:audit:view", ct);
        if (error is not null) return error;

        var q = http.Request.Query;
        var limit = Math.Clamp(int.TryParse(q["limit"], out var l) ? l : 100, 1, 500);
        var (sql, bind) = BuildAuditQuery(q, limit);
        var rows = await db.QueryAsync(sql, bind, ct);

        // Keyset rather than OFFSET: the log grows while an investigator pages, and
        // OFFSET would silently skip or repeat rows as it does.
        string? nextCursor = rows.Count == limit ? rows[^1]["id"]?.ToString() : null;
        var actions = await db.QueryAsync(
            "SELECT DISTINCT action FROM platform_audit_log ORDER BY action", ct: ct);

        return Results.Ok(ApiResponse<object>.Ok(new
        {
            rows,
            nextCursor,
            actions = actions.Select(a => a["action"]?.ToString()).Where(a => a is not null),
        }));
    }

    // CSV export of exactly the filtered set on screen, so an evidence request is a
    // download rather than a DBA ticket. The export is itself audited — who pulled
    // the log, and with what filter, is part of the record.
    private static async Task<IResult> AuditExport(HttpContext http, Database db, CancellationToken ct)
    {
        var (principal, error) = await RequireAsync(http, db, "platform:audit:view", ct);
        if (error is not null) return error;

        var q = http.Request.Query;
        var (sql, bind) = BuildAuditQuery(q, 50_000);
        var rows = await db.QueryAsync(sql, bind, ct);

        var csv = new System.Text.StringBuilder();
        csv.AppendLine("timestamp,actor_email,actor_role,action,entity_type,entity_id,tenant,ip_address,details");
        foreach (var r in rows)
            csv.AppendLine(string.Join(",", new[]
            {
                r["createdAt"]?.ToString(), r["actorEmail"]?.ToString(), r["actorRole"]?.ToString(),
                r["action"]?.ToString(), r["entityType"]?.ToString(), r["entityId"]?.ToString(),
                r["tenantName"]?.ToString(), r["ipAddress"]?.ToString(), r["detailsJson"]?.ToString(),
            }.Select(CsvCell)));

        await AuditAsync(db, principal!, http, "audit.exported", "AuditLog", null, null,
            new { rows = rows.Count, filter = q.ToDictionary(k => k.Key, v => v.Value.ToString()) }, ct);

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        return Results.File(System.Text.Encoding.UTF8.GetBytes(csv.ToString()),
            "text/csv", $"opstrax-platform-audit-{stamp}.csv");
    }

    // RFC 4180: quote every field, double any embedded quote. details_json is raw
    // JSON full of commas and quotes — unquoted it would shred the column layout.
    private static string CsvCell(string? value) =>
        "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";

    private static (string Sql, Action<NpgsqlCommand> Bind) BuildAuditQuery(IQueryCollection q, int limit)
    {
        var where = new List<string>();
        if (!string.IsNullOrWhiteSpace(q["actor"])) where.Add("a.actor_email ILIKE '%' || @actor || '%'");
        if (!string.IsNullOrWhiteSpace(q["action"])) where.Add("a.action = @action");
        if (!string.IsNullOrWhiteSpace(q["entityType"])) where.Add("a.entity_type = @entityType");
        if (!string.IsNullOrWhiteSpace(q["companyId"])) where.Add("a.target_company_id = @companyId");
        if (!string.IsNullOrWhiteSpace(q["from"])) where.Add("a.created_at >= @from::timestamptz");
        if (!string.IsNullOrWhiteSpace(q["to"])) where.Add("a.created_at < (@to::timestamptz + INTERVAL '1 day')");
        if (!string.IsNullOrWhiteSpace(q["cursor"])) where.Add("a.id < @cursor");

        var sql = $@"
            SELECT a.id, a.actor_email, a.actor_role, a.action, a.entity_type, a.entity_id,
                   a.target_company_id, c.name AS tenant_name, a.details_json, a.ip_address, a.created_at
            FROM platform_audit_log a
            LEFT JOIN companies c ON c.id = a.target_company_id
            {(where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "")}
            ORDER BY a.id DESC
            LIMIT {limit}";

        return (sql, cmd =>
        {
            if (!string.IsNullOrWhiteSpace(q["actor"])) cmd.Parameters.AddWithValue("@actor", q["actor"].ToString());
            if (!string.IsNullOrWhiteSpace(q["action"])) cmd.Parameters.AddWithValue("@action", q["action"].ToString());
            if (!string.IsNullOrWhiteSpace(q["entityType"])) cmd.Parameters.AddWithValue("@entityType", q["entityType"].ToString());
            if (!string.IsNullOrWhiteSpace(q["companyId"]) && long.TryParse(q["companyId"], out var cid))
                cmd.Parameters.AddWithValue("@companyId", cid);
            if (!string.IsNullOrWhiteSpace(q["from"])) cmd.Parameters.AddWithValue("@from", q["from"].ToString());
            if (!string.IsNullOrWhiteSpace(q["to"])) cmd.Parameters.AddWithValue("@to", q["to"].ToString());
            if (!string.IsNullOrWhiteSpace(q["cursor"]) && long.TryParse(q["cursor"], out var cur))
                cmd.Parameters.AddWithValue("@cursor", cur);
        });
    }

    private static async Task<IResult> RolesList(HttpContext http, Database db, CancellationToken ct)
    {
        var (_, error) = await RequireAsync(http, db, "platform:dashboard:view", ct);
        if (error is not null) return error;
        var rows = await db.QueryAsync(
            @"SELECT r.role_key, r.name, r.description,
                     (SELECT COUNT(*) FROM platform_role_permissions rp WHERE rp.role_id=r.id) permission_count,
                     (SELECT COUNT(*) FROM platform_admins a WHERE a.role_id=r.id) admin_count
              FROM platform_roles r ORDER BY r.id", ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(rows));
    }

    // ════════════════════════════════════════════════════════════════════════════
    // SHARED HELPERS
    // ════════════════════════════════════════════════════════════════════════════

    private static async Task<long> ComputeMrrAsync(Database db, long packageId, int seatLimit, CancellationToken ct)
    {
        var pkg = await db.QuerySingleAsync(
            "SELECT base_price_cents, seat_price_cents, included_seats FROM packages WHERE id=@id",
            c => c.Parameters.AddWithValue("@id", packageId), ct);
        if (pkg is null) return 0;
        var basePrice = Convert.ToInt64(pkg["basePriceCents"]);
        var seatPrice = Convert.ToInt64(pkg["seatPriceCents"]);
        var included = Convert.ToInt32(pkg["includedSeats"]);
        var billable = Math.Max(0, seatLimit - included);
        return basePrice + seatPrice * billable;
    }

    private static async Task SeedEntitlementsFromPackageAsync(Database db, long companyId, long packageId, string actor, CancellationToken ct)
    {
        var pkg = await db.QuerySingleAsync("SELECT module_keys FROM packages WHERE id=@id",
            c => c.Parameters.AddWithValue("@id", packageId), ct);
        if (pkg?["moduleKeys"] is null) return;

        var raw = pkg["moduleKeys"]!.ToString() ?? "[]";
        List<string> parsedModules;
        try { parsedModules = JsonSerializer.Deserialize<List<string>>(raw) ?? []; }
        catch (JsonException ex)
        {
            // A malformed persisted package is a control-plane integrity failure.
            // Do not silently assign a commercially empty package and report success.
            throw new InvalidOperationException("Package module catalog is invalid JSON.", ex);
        }

        var modules = parsedModules
            .Select(module => module?.Trim() ?? string.Empty)
            .ToArray();
        if (modules.Any(string.IsNullOrWhiteSpace) ||
            modules.Any(module => !GovernedEntitlementModuleKeys.Contains(module)))
            throw new InvalidOperationException("Package contains an unknown governed module key.");

        foreach (var module in modules.Distinct(StringComparer.Ordinal))
        {
            // Package default — never clobbers an explicit override (source='override').
            await db.ExecuteAsync(
                @"INSERT INTO tenant_entitlements (company_id, module_key, enabled, source, updated_by)
                  VALUES (@cid, @mk, true, 'package', @by)
                  ON CONFLICT (company_id, module_key) DO UPDATE
                    SET enabled = CASE WHEN tenant_entitlements.source='override' THEN tenant_entitlements.enabled ELSE true END,
                        updated_at = NOW()",
                c =>
                {
                    c.Parameters.AddWithValue("@cid", companyId);
                    c.Parameters.AddWithValue("@mk", module);
                    c.Parameters.AddWithValue("@by", actor);
                }, ct);
        }
    }

    internal enum AdminInviteStatus { Sent, CrossTenantConflict }

    // Result of a tenant-admin invite attempt. CrossTenantConflict means the email is
    // bound to a DIFFERENT company and was REFUSED (never relocated); the caller decides
    // how to surface that. EmailSent reports whether the accept link actually went out.
    internal sealed record AdminInviteResult(AdminInviteStatus Status, bool EmailSent, string? ConflictCompanyId);

    // Tenant-admin onboarding invite. Mirrors the platform-operator invite in
    // PlatformAdminEndpoints: a single-use, hashed-at-rest token with a 7-day expiry is
    // minted and the accept link is emailed; NO usable password is set until the invitee
    // completes the flow.
    //
    // The tenant side reuses the canonical tenant onboarding path — the
    // password_reset_tokens table + POST /api/auth/reset-password (ResetPassword flips a
    // 'Pending' user to 'Active', which is exactly what the login gate at Login() requires),
    // so no separate tenant accept-invite page/endpoint is needed. The emailed link targets
    // the TENANT app's existing /reset-password?...&welcome=1 route. (The platform
    // accept-invite page is a distinct flow for operators against platform_admins and is
    // deliberately NOT reused here.)
    //
    // SECURITY — the reason this helper exists: users.email is now unique PER TENANT
    // (2026_07_13_users_email_per_tenant.sql), not globally. An email already bound to a
    // DIFFERENT company is REFUSED, never absorbed. The previous body did
    // `ON CONFLICT (email) DO UPDATE SET company_id=@cid`, which relocated the victim's
    // existing users row — carrying their password_hash / role / permissions — into the
    // new tenant: a provisioning typo became a cross-tenant account takeover. Re-inviting
    // WITHIN the same tenant is fine.
    private static async Task<AdminInviteResult> CreateAdminInviteAsync(
        HttpContext http, Database db, long companyId, string email, string name, CancellationToken ct)
    {
        var normEmail = email.Trim();

        // Look up any existing owner of this email (case-insensitively, matching the
        // login lookup). A hit under another company_id is refused outright.
        var existing = await db.QuerySingleAsync(
            "SELECT id, company_id, status FROM users WHERE LOWER(email)=LOWER(@e) LIMIT 1",
            c => c.Parameters.AddWithValue("@e", normEmail), ct);

        long userId;
        if (existing is not null)
        {
            var owner = Convert.ToInt64(existing["companyId"]);
            if (owner != companyId)
                return new AdminInviteResult(AdminInviteStatus.CrossTenantConflict, false, owner.ToString());

            userId = Convert.ToInt64(existing["id"]);

            // Same-tenant re-invite. Never downgrade an already-active admin (that would
            // lock them out of a working account); only (re)arm the Pending onboarding
            // state for a user who has not yet finished setting a password.
            var status = existing["status"]?.ToString() ?? "";
            if (!string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase))
            {
                await db.ExecuteAsync(
                    "UPDATE users SET full_name=@n, role_name='Company Admin', status='Pending' WHERE id=@id AND company_id=@cid",
                    c =>
                    {
                        c.Parameters.AddWithValue("@n", name);
                        c.Parameters.AddWithValue("@id", userId);
                        c.Parameters.AddWithValue("@cid", companyId);
                    }, ct);
            }
        }
        else
        {
            // Fresh admin: status 'Pending' (NOT the old 'Invited', which the login gate
            // and ResetPassword both reject) and NO password_hash. ResetPassword flips
            // 'Pending' -> 'Active' when the invite is accepted.
            userId = await db.InsertAsync(
                @"INSERT INTO users (company_id, full_name, email, role_name, status)
                  VALUES (@cid, @name, @email, 'Company Admin', 'Pending')
                  RETURNING id",
                c =>
                {
                    c.Parameters.AddWithValue("@cid", companyId);
                    c.Parameters.AddWithValue("@name", name);
                    c.Parameters.AddWithValue("@email", normEmail);
                }, ct);
        }

        // Mint a single-use set-password token, hashed at rest, 7-day expiry — same shape
        // and lifetime as the operator invite and the tenant activation-link path.
        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var tokenHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawToken)));
        await db.ExecuteAsync(
            @"INSERT INTO password_reset_tokens (user_id, company_id, token_hash, expires_at, request_ip_hash)
              VALUES (@uid, @cid, @hash, NOW() + INTERVAL '7 days', @ip)
              ON CONFLICT (user_id) DO UPDATE SET token_hash=EXCLUDED.token_hash, expires_at=EXCLUDED.expires_at,
                consumed_at=NULL, request_ip_hash=EXCLUDED.request_ip_hash, created_at=NOW()",
            c =>
            {
                c.Parameters.AddWithValue("@uid", userId);
                c.Parameters.AddWithValue("@cid", companyId);
                c.Parameters.AddWithValue("@hash", tokenHash);
                c.Parameters.AddWithValue("@ip", InviteRequestIpHash(http));
            }, ct);

        var emailSent = await TrySendTenantInviteEmailAsync(http, normEmail, name, rawToken, ct);
        return new AdminInviteResult(AdminInviteStatus.Sent, emailSent, null);
    }

    private static string InviteRequestIpHash(HttpContext http) =>
        Convert.ToHexString(SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(http.Connection.RemoteIpAddress?.ToString() ?? string.Empty)))[..16];

    // Emails the tenant admin their set-password link. Unlike the operator invite (which
    // derives its base URL from the request Origin / PLATFORM_PUBLIC_URL and targets the
    // platform SPA), this must target the TENANT app: the request Origin here is the
    // platform console, so the tenant app's public URL (FRONTEND_PUBLIC_URL /
    // PUBLIC_APP_URL) is used, exactly like ForgotPassword. Returns false when no tenant
    // base URL or SMTP is configured — the caller reports that truthfully.
    private static async Task<bool> TrySendTenantInviteEmailAsync(
        HttpContext http, string email, string fullName, string rawToken, CancellationToken ct)
    {
        var baseUrl = (Environment.GetEnvironmentVariable("FRONTEND_PUBLIC_URL")
            ?? Environment.GetEnvironmentVariable("PUBLIC_APP_URL") ?? "").TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl)) return false;

        var link = $"{baseUrl}/reset-password?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(rawToken)}&welcome=1";
        return await PlatformMailService.TrySendAsync(
            email,
            "OpsTrax — set up your administrator account",
            $"""
            Hello {fullName},

            An OpsTrax administrator account has been created for you.

            Set your password using this single-use link (valid for 7 days):
            {link}

            If you did not expect this, ignore this email and report it to your
            administrator.
            """,
            ct);
    }

    internal static bool VerifyPassword(string password, string? storedHash)
    {
        const int requiredIterations = 100_000;
        const int requiredSaltLength = 16;
        const int requiredSubkeyLength = 32;
        if (string.IsNullOrWhiteSpace(storedHash)) return false;
        var parts = storedHash.Split('$');
        if (parts.Length != 4 || !string.Equals(parts[0], "PBKDF2", StringComparison.OrdinalIgnoreCase)) return false;
        if (!int.TryParse(parts[1], out var iterations) ||
            iterations < requiredIterations || iterations > 2_000_000) return false;
        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            if (salt.Length != requiredSaltLength || expected.Length != requiredSubkeyLength)
                return false;
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch { return false; }
    }

    // ── Dictionary body accessors (JSON numbers arrive as JsonElement) ──────────
    private static string? Str(Dictionary<string, object?> body, string key)
    {
        if (!body.TryGetValue(key, out var v) || v is null) return null;
        if (v is JsonElement je)
            return je.ValueKind switch
            {
                JsonValueKind.String => je.GetString(),
                JsonValueKind.Number => je.ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => je.ToString(),
            };
        var s = v.ToString();
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    private static long? Long(Dictionary<string, object?> body, string key)
    {
        if (!body.TryGetValue(key, out var v) || v is null) return null;
        if (v is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Number && je.TryGetInt64(out var n)) return n;
            if (je.ValueKind == JsonValueKind.String && long.TryParse(je.GetString(), out var sn)) return sn;
            return null;
        }
        return long.TryParse(v.ToString(), out var fallback) ? fallback : null;
    }

    private static decimal? Decimal(Dictionary<string, object?> body, string key)
    {
        if (!body.TryGetValue(key, out var v) || v is null) return null;
        if (v is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Number && je.TryGetDecimal(out var n)) return n;
            if (je.ValueKind == JsonValueKind.String && decimal.TryParse(je.GetString(), out var sn)) return sn;
            return null;
        }
        return decimal.TryParse(v.ToString(), out var fallback) ? fallback : null;
    }

    private static List<string> ReadStringArray(Dictionary<string, object?> body, string key)
    {
        if (!body.TryGetValue(key, out var v) || v is null) return [];
        if (v is JsonElement je && je.ValueKind == JsonValueKind.Array)
            return je.EnumerateArray()
                .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!.Trim())
                .ToList();
        return [];
    }

    private static List<long> ReadLongArray(Dictionary<string, object?> body, string key)
    {
        if (!body.TryGetValue(key, out var v) || v is null) return [];
        if (v is JsonElement je && je.ValueKind == JsonValueKind.Array)
        {
            var list = new List<long>();
            foreach (var e in je.EnumerateArray())
            {
                if (e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out var n)) list.Add(n);
                else if (e.ValueKind == JsonValueKind.String && long.TryParse(e.GetString(), out var sn)) list.Add(sn);
            }
            return list;
        }
        return [];
    }

    private static bool? Bool(Dictionary<string, object?> body, string key)
    {
        if (!body.TryGetValue(key, out var v) || v is null) return null;
        if (v is JsonElement je)
            return je.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => bool.TryParse(je.GetString(), out var b) ? b : null,
                _ => null,
            };
        return bool.TryParse(v.ToString(), out var fb) ? fb : null;
    }
}
