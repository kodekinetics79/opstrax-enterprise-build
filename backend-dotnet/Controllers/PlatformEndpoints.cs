using System.Security.Cryptography;
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
        app.MapPost("/api/platform/tenants/{id:long}/assign-package", TenantAssignPackage);
        app.MapPost("/api/platform/tenants/{id:long}/reset-admin-invite", TenantResetInvite);
        app.MapGet("/api/platform/tenants/{id:long}/audit", TenantAudit);
        // Offboarding — schema-driven cascade delete of ALL tenant-owned rows + the company.
        app.MapDelete("/api/platform/tenants/{id:long}", TenantDelete);

        // ── Feature Entitlements ────────────────────────────────────────────────
        app.MapGet("/api/platform/tenants/{id:long}/entitlements", EntitlementsGet);
        app.MapPut("/api/platform/tenants/{id:long}/entitlements", EntitlementsSet);

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

        // ── Billing & Invoices ──────────────────────────────────────────────────
        app.MapGet("/api/platform/invoices", InvoicesList);
        app.MapPost("/api/platform/invoices", InvoiceCreate);
        app.MapPost("/api/platform/invoices/{id:long}/mark-paid", InvoiceMarkPaid);

        // ── Customer Success (health scores) ────────────────────────────────────
        app.MapGet("/api/platform/health", HealthScores);

        // ── Security & Audit ────────────────────────────────────────────────────
        app.MapGet("/api/platform/audit", AuditList);

        // ── Roles (for RBAC visibility) ─────────────────────────────────────────
        app.MapGet("/api/platform/roles", RolesList);
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

    private static Task AuditAsync(Database db, PlatformPrincipal actor, HttpContext http, string action,
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

    private static string BearerToken(HttpContext http)
    {
        var header = http.Request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..].Trim()
            : string.Empty;
    }

    // ════════════════════════════════════════════════════════════════════════════
    // AUTH HANDLERS
    // ════════════════════════════════════════════════════════════════════════════

    private sealed record PlatformLoginRequest(string Email, string Password);

    private static async Task<IResult> PlatformLogin(HttpContext http, PlatformLoginRequest request, Database db, CancellationToken ct)
    {
        var admin = await db.QuerySingleAsync(
            @"SELECT a.id, a.email, a.full_name, a.password_hash, r.role_key, r.name role_name
              FROM platform_admins a LEFT JOIN platform_roles r ON r.id = a.role_id
              WHERE a.email=@e AND a.status='Active' LIMIT 1",
            c => c.Parameters.AddWithValue("@e", request.Email ?? ""), ct);
        if (admin is null) return Results.Json(ApiResponse<object>.Fail("Invalid credentials"), statusCode: StatusCodes.Status401Unauthorized);

        if (!VerifyPassword(request.Password ?? "", admin["passwordHash"]?.ToString()))
            return Results.Json(ApiResponse<object>.Fail("Invalid credentials"), statusCode: StatusCodes.Status401Unauthorized);

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
                 ts.status, ts.seat_limit, ts.billing_currency, ts.mrr_cents, ts.trial_ends_at,
                 ts.contract_start, ts.contract_end, ts.account_owner, ts.support_owner,
                 p.name package_name, p.package_code,
                 (SELECT COUNT(*) FROM users u WHERE u.company_id = c.id) user_count
          FROM companies c
          LEFT JOIN tenant_subscriptions ts ON ts.company_id = c.id
          LEFT JOIN packages p ON p.id = ts.package_id";

    private static async Task<IResult> TenantsList(HttpContext http, Database db, CancellationToken ct)
    {
        var (_, error) = await RequireAsync(http, db, "platform:tenants:view", ct);
        if (error is not null) return error;
        var rows = await db.QueryAsync(TenantSelect + " ORDER BY c.created_at DESC", ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(rows));
    }

    private static async Task<IResult> TenantDetail(long id, HttpContext http, Database db, CancellationToken ct)
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

    private static async Task<IResult> TenantCreate(HttpContext http, Dictionary<string, object?> body, Database db, CountryProfileService countries, CancellationToken ct)
    {
        var (principal, error) = await RequireAsync(http, db, "platform:tenants:manage", ct);
        if (error is not null) return error;

        var name = Str(body, "name");
        if (string.IsNullOrWhiteSpace(name))
            return Results.Json(ApiResponse<object>.Fail("Validation failed", "name is required"), statusCode: StatusCodes.Status400BadRequest);

        var code = Str(body, "companyCode");
        if (string.IsNullOrWhiteSpace(code)) code = "T-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var industry = Str(body, "industry") ?? "Logistics";
        var packageId = Long(body, "packageId");
        var seatLimit = (int)(Long(body, "seatLimit") ?? 5);
        var status = Str(body, "status") ?? "trial";
        var trialDays = (int)(Long(body, "trialDays") ?? 14);

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

        var companyId = await db.InsertAsync(
            "INSERT INTO companies (company_code, name, industry, status) VALUES (@code, @name, @ind, 'Active')",
            c =>
            {
                c.Parameters.AddWithValue("@code", code!);
                c.Parameters.AddWithValue("@name", name!);
                c.Parameters.AddWithValue("@ind", industry);
            }, ct);

        var mrrCents = packageId.HasValue ? await ComputeMrrAsync(db, packageId.Value, seatLimit, ct) : 0;

        await db.ExecuteAsync(
            @"INSERT INTO tenant_subscriptions (company_id, package_id, status, seat_limit, billing_currency, mrr_cents, trial_ends_at, account_owner, support_owner)
              VALUES (@cid, @pid, @status, @seats, @cur, @mrr,
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

        // Optional tenant admin invite
        var adminEmail = Str(body, "adminEmail");
        if (!string.IsNullOrWhiteSpace(adminEmail))
            await CreateAdminInviteAsync(db, companyId, adminEmail!, Str(body, "adminName") ?? "Tenant Admin", ct);

        await AuditAsync(db, principal!, http, "tenant.created", "Tenant", companyId, companyId,
            new { name, code, status, packageId, seatLimit, countryCode = cascade?.CountryCode, currency = cascade?.Currency, autoEnabled = cascade?.EnabledFeatures }, ct);

        return Results.Ok(ApiResponse<object>.Ok(new
        {
            id = companyId, name, code, status,
            country = cascade?.CountryCode,
            currency = cascade?.Currency ?? currency,
            autoEnabledFeatures = cascade?.EnabledFeatures ?? [],
        }, "Tenant created"));
    }

    private static async Task<IResult> TenantUpdate(long id, HttpContext http, Dictionary<string, object?> body, Database db, CancellationToken ct)
    {
        var (principal, error) = await RequireAsync(http, db, "platform:tenants:manage", ct);
        if (error is not null) return error;

        await db.ExecuteAsync(
            @"UPDATE tenant_subscriptions SET
                seat_limit = COALESCE(@seats, seat_limit),
                billing_currency = COALESCE(@cur, billing_currency),
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
                c.Parameters.AddWithValue("@ao", (object?)Str(body, "accountOwner") ?? DBNull.Value);
                c.Parameters.AddWithValue("@so", (object?)Str(body, "supportOwner") ?? DBNull.Value);
                c.Parameters.AddWithValue("@cs", (object?)Str(body, "contractStart") ?? DBNull.Value);
                c.Parameters.AddWithValue("@ce", (object?)Str(body, "contractEnd") ?? DBNull.Value);
            }, ct);

        var newName = Str(body, "name");
        if (!string.IsNullOrWhiteSpace(newName))
            await db.ExecuteAsync("UPDATE companies SET name=@n WHERE id=@id", c =>
            {
                c.Parameters.AddWithValue("@n", newName!);
                c.Parameters.AddWithValue("@id", id);
            }, ct);

        await AuditAsync(db, principal!, http, "tenant.updated", "Tenant", id, id, body.Keys, ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id }, "Tenant updated"));
    }

    private static async Task<IResult> TenantStatus(long id, HttpContext http, Dictionary<string, object?> body, Database db, CancellationToken ct)
    {
        var (principal, error) = await RequireAsync(http, db, "platform:tenants:manage", ct);
        if (error is not null) return error;

        var action = (Str(body, "action") ?? "").ToLowerInvariant();
        string? newStatus = action switch
        {
            "activate" or "reactivate" => "active",
            "suspend" => "suspended",
            "cancel" => "cancelled",
            "extend-trial" or "extend_trial" => "trial",
            "manual-contract" or "manual_contract" => "manual_contract",
            _ => null,
        };
        if (newStatus is null)
            return Results.Json(ApiResponse<object>.Fail("Invalid action", "Use activate|suspend|cancel|extend-trial|reactivate|manual-contract"), statusCode: StatusCodes.Status400BadRequest);

        if (action is "extend-trial" or "extend_trial")
        {
            var days = (int)(Long(body, "days") ?? 14);
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

        await AuditAsync(db, principal!, http, $"tenant.{action}", "Tenant", id, id, new { newStatus, sessionsRevoked = revoked }, ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id, status = newStatus, sessionsRevoked = revoked }, $"Tenant {action} applied"));
    }

    private static async Task<IResult> TenantAssignPackage(long id, HttpContext http, Dictionary<string, object?> body, Database db, CancellationToken ct)
    {
        var (principal, error) = await RequireAsync(http, db, "platform:tenants:manage", ct);
        if (error is not null) return error;

        var packageId = Long(body, "packageId");
        if (!packageId.HasValue)
            return Results.Json(ApiResponse<object>.Fail("Validation failed", "packageId is required"), statusCode: StatusCodes.Status400BadRequest);

        var seatLimit = (int)(Long(body, "seatLimit")
            ?? await db.ScalarLongAsync("SELECT seat_limit FROM tenant_subscriptions WHERE company_id=@id", c => c.Parameters.AddWithValue("@id", id), ct));
        if (seatLimit <= 0) seatLimit = 5;

        var mrrCents = await ComputeMrrAsync(db, packageId.Value, seatLimit, ct);

        await db.ExecuteAsync(
            @"INSERT INTO tenant_subscriptions (company_id, package_id, seat_limit, mrr_cents, status)
              VALUES (@id, @pid, @seats, @mrr, 'active')
              ON CONFLICT (company_id) DO UPDATE SET package_id=@pid, seat_limit=@seats, mrr_cents=@mrr, updated_at=NOW()",
            c =>
            {
                c.Parameters.AddWithValue("@id", id);
                c.Parameters.AddWithValue("@pid", packageId.Value);
                c.Parameters.AddWithValue("@seats", seatLimit);
                c.Parameters.AddWithValue("@mrr", mrrCents);
            }, ct);

        await SeedEntitlementsFromPackageAsync(db, id, packageId.Value, principal!.Email, ct);
        await AuditAsync(db, principal!, http, "tenant.package.assigned", "Tenant", id, id, new { packageId, seatLimit, mrrCents }, ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id, packageId, mrrCents }, "Package assigned"));
    }

    private static async Task<IResult> TenantResetInvite(long id, HttpContext http, Dictionary<string, object?> body, Database db, CancellationToken ct)
    {
        var (principal, error) = await RequireAsync(http, db, "platform:tenants:manage", ct);
        if (error is not null) return error;
        var adminEmail = Str(body, "adminEmail");
        if (string.IsNullOrWhiteSpace(adminEmail))
            return Results.Json(ApiResponse<object>.Fail("Validation failed", "adminEmail is required"), statusCode: StatusCodes.Status400BadRequest);
        await CreateAdminInviteAsync(db, id, adminEmail!, Str(body, "adminName") ?? "Tenant Admin", ct);
        await AuditAsync(db, principal!, http, "tenant.admin_invite.reset", "Tenant", id, id, new { adminEmail }, ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id, adminEmail }, "Admin invite reset"));
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

    // Hard delete a tenant and ALL its data (pilot "delete on request"). Schema-driven
    // cascade — see TenantOffboardingService. Requires an explicit confirm token in the
    // body ({"confirm":"<companyCode>"}) so a tenant can never be purged by a stray DELETE.
    private static async Task<IResult> TenantDelete(long id, HttpContext http, [Microsoft.AspNetCore.Mvc.FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] Dictionary<string, object?>? body, Database db, TenantOffboardingService offboarding, CancellationToken ct)
    {
        var (principal, error) = await RequireAsync(http, db, "platform:tenants:manage", ct);
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

    private static async Task<IResult> EntitlementsGet(long id, HttpContext http, Database db, CancellationToken ct)
    {
        var (_, error) = await RequireAsync(http, db, "platform:entitlements:view", ct);
        if (error is not null) return error;
        var rows = await db.QueryAsync(
            "SELECT module_key, enabled, limit_value, tier, source, updated_by, updated_at FROM tenant_entitlements WHERE company_id=@id ORDER BY module_key",
            c => c.Parameters.AddWithValue("@id", id), ct);
        return Results.Ok(ApiResponse<object>.Ok(rows));
    }

    private static async Task<IResult> EntitlementsSet(long id, HttpContext http, Dictionary<string, object?> body, Database db, CancellationToken ct)
    {
        var (principal, error) = await RequireAsync(http, db, "platform:entitlements:manage", ct);
        if (error is not null) return error;

        var moduleKey = Str(body, "moduleKey");
        if (string.IsNullOrWhiteSpace(moduleKey))
            return Results.Json(ApiResponse<object>.Fail("Validation failed", "moduleKey is required"), statusCode: StatusCodes.Status400BadRequest);

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
        var modules = body.TryGetValue("moduleKeys", out var mk) && mk is not null ? JsonSerializer.Serialize(mk) : "[]";

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
                c.Parameters.AddWithValue("@modules", modules);
                c.Parameters.AddWithValue("@custom", Bool(body, "isCustom") ?? false);
            }, ct);

        await AuditAsync(db, principal!, http, "package.created", "Package", newId, null, new { name, code }, ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id = newId, name, code }, "Package created"));
    }

    private static async Task<IResult> PackageUpdate(long id, HttpContext http, Dictionary<string, object?> body, Database db, CancellationToken ct)
    {
        var (principal, error) = await RequireAsync(http, db, "platform:packages:manage", ct);
        if (error is not null) return error;
        var modules = body.TryGetValue("moduleKeys", out var mk) && mk is not null ? JsonSerializer.Serialize(mk) : null;
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

    // ════════════════════════════════════════════════════════════════════════════
    // BILLING & INVOICES
    // ════════════════════════════════════════════════════════════════════════════

    private static async Task<IResult> InvoicesList(HttpContext http, Database db, CancellationToken ct)
    {
        var (_, error) = await RequireAsync(http, db, "platform:billing:view", ct);
        if (error is not null) return error;
        var rows = await db.QueryAsync(
            @"SELECT i.id, i.invoice_number, i.status, i.kind, i.amount_cents, i.currency,
                     i.issued_at, i.due_at, i.paid_at, c.name tenant, i.company_id
              FROM platform_invoices i JOIN companies c ON c.id = i.company_id
              ORDER BY i.created_at DESC LIMIT 200", ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(rows));
    }

    private static async Task<IResult> InvoiceCreate(HttpContext http, Dictionary<string, object?> body, Database db, CancellationToken ct)
    {
        var (principal, error) = await RequireAsync(http, db, "platform:billing:manage", ct);
        if (error is not null) return error;

        var companyId = Long(body, "companyId");
        if (!companyId.HasValue)
            return Results.Json(ApiResponse<object>.Fail("Validation failed", "companyId is required"), statusCode: StatusCodes.Status400BadRequest);
        var amount = Long(body, "amountCents") ?? 0;
        var lineItems = body.TryGetValue("lineItems", out var li) && li is not null ? JsonSerializer.Serialize(li) : "[]";
        var number = "INV-" + DateTime.UtcNow.ToString("yyyyMM") + "-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        var newId = await db.InsertAsync(
            @"INSERT INTO platform_invoices (company_id, invoice_number, status, kind, amount_cents, currency, line_items, notes, issued_at, due_at)
              VALUES (@cid, @num, @status, @kind, @amt, @cur, CAST(@items AS JSONB), @notes, NOW(),
                      NOW() + (@dueDays || ' day')::interval)",
            c =>
            {
                c.Parameters.AddWithValue("@cid", companyId.Value);
                c.Parameters.AddWithValue("@num", number);
                c.Parameters.AddWithValue("@status", Str(body, "status") ?? "sent");
                c.Parameters.AddWithValue("@kind", Str(body, "kind") ?? "recurring");
                c.Parameters.AddWithValue("@amt", amount);
                c.Parameters.AddWithValue("@cur", Str(body, "currency") ?? "USD");
                c.Parameters.AddWithValue("@items", lineItems);
                c.Parameters.AddWithValue("@notes", (object?)Str(body, "notes") ?? DBNull.Value);
                c.Parameters.AddWithValue("@dueDays", ((int)(Long(body, "dueDays") ?? 15)).ToString());
            }, ct);

        await AuditAsync(db, principal!, http, "invoice.created", "Invoice", newId, companyId, new { number, amount }, ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id = newId, invoiceNumber = number }, "Invoice created"));
    }

    private static async Task<IResult> InvoiceMarkPaid(long id, HttpContext http, Database db, CancellationToken ct)
    {
        var (principal, error) = await RequireAsync(http, db, "platform:billing:manage", ct);
        if (error is not null) return error;
        var companyId = await db.ScalarLongAsync("SELECT company_id FROM platform_invoices WHERE id=@id", c => c.Parameters.AddWithValue("@id", id), ct);
        await db.ExecuteAsync("UPDATE platform_invoices SET status='paid', paid_at=NOW() WHERE id=@id",
            c => c.Parameters.AddWithValue("@id", id), ct);
        await AuditAsync(db, principal!, http, "invoice.paid", "Invoice", id, companyId, null, ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id, status = "paid" }, "Invoice marked paid"));
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
    // AUDIT + ROLES
    // ════════════════════════════════════════════════════════════════════════════

    private static async Task<IResult> AuditList(HttpContext http, Database db, CancellationToken ct)
    {
        var (_, error) = await RequireAsync(http, db, "platform:audit:view", ct);
        if (error is not null) return error;
        var rows = await db.QueryAsync(
            @"SELECT id, actor_email, actor_role, action, entity_type, entity_id, target_company_id, details_json, ip_address, created_at
              FROM platform_audit_log ORDER BY created_at DESC LIMIT 250", ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(rows));
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
        List<string> modules;
        try { modules = JsonSerializer.Deserialize<List<string>>(raw) ?? []; }
        catch { return; }

        foreach (var module in modules.Where(m => !string.IsNullOrWhiteSpace(m)))
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
                    c.Parameters.AddWithValue("@mk", module.Trim());
                    c.Parameters.AddWithValue("@by", actor);
                }, ct);
        }
    }

    private static async Task CreateAdminInviteAsync(Database db, long companyId, string email, string name, CancellationToken ct)
    {
        await db.ExecuteAsync(
            @"INSERT INTO users (company_id, full_name, email, role_name, status)
              VALUES (@cid, @name, @email, 'Company Admin', 'Invited')
              ON CONFLICT (email) DO UPDATE SET company_id=@cid, status='Invited'",
            c =>
            {
                c.Parameters.AddWithValue("@cid", companyId);
                c.Parameters.AddWithValue("@name", name);
                c.Parameters.AddWithValue("@email", email);
            }, ct);
    }

    private static bool VerifyPassword(string password, string? storedHash)
    {
        if (string.IsNullOrWhiteSpace(storedHash)) return false;
        var parts = storedHash.Split('$');
        if (parts.Length != 4 || !string.Equals(parts[0], "PBKDF2", StringComparison.OrdinalIgnoreCase)) return false;
        if (!int.TryParse(parts[1], out var iterations)) return false;
        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
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
