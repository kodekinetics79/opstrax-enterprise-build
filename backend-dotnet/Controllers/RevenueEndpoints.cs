using Opstrax.Api.Data;
using Opstrax.Api.DTOs;
using Opstrax.Api.Services;

namespace Opstrax.Api.Controllers;

// Revenue-foundation HTTP surface. Extends (does not replace) the existing
// PlatformEndpoints control plane:
//   • Platform Admin (platform session, reuses PlatformEndpoints.RequireAsync):
//       module-package catalog, usage meters, per-tenant usage, invoice preview,
//       contract overrides.
//   • Tenant self-service (tenant session middleware): read-only subscription,
//       usage and entitlements for the signed-in company.
//
// Plans (pricing tiers) and per-tenant entitlements already have endpoints under
// /api/platform/packages and /api/platform/tenants/{id}/entitlements — those are
// reused as-is and intentionally not duplicated here.
public static class RevenueEndpoints
{
    public static void MapRevenueEndpoints(this WebApplication app)
    {
        // ── Platform Admin (Opstrax revenue control) ──────────────────────────
        app.MapGet("/api/platform/opstrax/module-packages", ModulePackages);
        app.MapGet("/api/platform/opstrax/meters", Meters);
        app.MapGet("/api/platform/opstrax/tenants/{tenantId:long}/usage", TenantUsage);
        app.MapGet("/api/platform/opstrax/tenants/{tenantId:long}/invoice-preview", InvoicePreview);
        app.MapPut("/api/platform/opstrax/tenants/{tenantId:long}/overrides", SetOverride);

        // ── Tenant self-service (read-only) ───────────────────────────────────
        app.MapGet("/api/opstrax/subscription/current", SubscriptionCurrent);
        app.MapGet("/api/opstrax/subscription/usage", SubscriptionUsage);
        app.MapGet("/api/opstrax/subscription/entitlements", SubscriptionEntitlements);
    }

    // ════════════════════════════ Platform Admin ════════════════════════════

    private static async Task<IResult> ModulePackages(HttpContext http, Database db, CancellationToken ct)
    {
        var (_, error) = await PlatformEndpoints.RequireAsync(http, db, "platform:packages:view", ct);
        if (error is not null) return error;
        var items = await db.QueryAsync("SELECT * FROM module_packages WHERE active ORDER BY sort_order, name", ct: ct);
        return Results.Json(ApiResponse<object>.Ok(new { items }));
    }

    private static async Task<IResult> Meters(HttpContext http, Database db, CancellationToken ct)
    {
        var (_, error) = await PlatformEndpoints.RequireAsync(http, db, "platform:packages:view", ct);
        if (error is not null) return error;
        var items = await db.QueryAsync("SELECT * FROM usage_meters WHERE active ORDER BY meter_key", ct: ct);
        return Results.Json(ApiResponse<object>.Ok(new { items }));
    }

    private static async Task<IResult> TenantUsage(long tenantId, HttpContext http, Database db, CancellationToken ct)
    {
        var (_, error) = await PlatformEndpoints.RequireAsync(http, db, "platform:tenants:view", ct);
        if (error is not null) return error;
        return Results.Json(ApiResponse<object>.Ok(await BuildUsageAsync(db, tenantId, ct)));
    }

    private static async Task<IResult> InvoicePreview(long tenantId, HttpContext http, Database db, CancellationToken ct)
    {
        var (_, error) = await PlatformEndpoints.RequireAsync(http, db, "platform:billing:view", ct);
        if (error is not null) return error;
        return Results.Json(ApiResponse<object>.Ok(await BuildInvoicePreviewAsync(db, tenantId, ct)));
    }

    private static async Task<IResult> SetOverride(long tenantId, HttpContext http, Dictionary<string, object?> body, Database db, CancellationToken ct)
    {
        var (principal, error) = await PlatformEndpoints.RequireAsync(http, db, "platform:billing:manage", ct);
        if (error is not null) return error;

        string? Str(string k) => body.TryGetValue(k, out var v) ? v?.ToString() : null;
        long? Cents(string k) => long.TryParse(Str(k), out var n) ? n : null;
        decimal? Num(string k) => decimal.TryParse(Str(k), out var n) ? n : null;

        await db.ExecuteAsync("""
            INSERT INTO tenant_contract_overrides (company_id, meter_key, included_quantity, unit_price_cents, flat_discount_cents, note, updated_by, updated_at)
            VALUES (@c,@m,@inc,@unit,@disc,@note,@by,NOW())
            ON CONFLICT (company_id, meter_key) DO UPDATE SET
                included_quantity = EXCLUDED.included_quantity,
                unit_price_cents  = EXCLUDED.unit_price_cents,
                flat_discount_cents = EXCLUDED.flat_discount_cents,
                note = EXCLUDED.note, updated_by = EXCLUDED.updated_by, updated_at = NOW()
            """,
            c =>
            {
                c.Parameters.AddWithValue("@c", tenantId);
                c.Parameters.AddWithValue("@m", (object?)Str("meterKey") ?? DBNull.Value);
                c.Parameters.AddWithValue("@inc", (object?)Num("includedQuantity") ?? DBNull.Value);
                c.Parameters.AddWithValue("@unit", (object?)Cents("unitPriceCents") ?? DBNull.Value);
                c.Parameters.AddWithValue("@disc", (object?)Cents("flatDiscountCents") ?? DBNull.Value);
                c.Parameters.AddWithValue("@note", (object?)Str("note") ?? DBNull.Value);
                c.Parameters.AddWithValue("@by", principal!.Email);
            }, ct);
        return Results.Json(ApiResponse<object>.Ok(new { ok = true }));
    }

    // ════════════════════════════ Tenant self-service ════════════════════════

    private static async Task<IResult> SubscriptionCurrent(HttpContext http, Database db, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(http, "dashboard:view");
        if (denied is not null) return denied;
        var companyId = EndpointMappings.GetCompanyId(http);
        var sub = await db.QuerySingleAsync("""
            SELECT s.*, p.name package_name, p.package_code, p.base_price_cents, p.module_keys
            FROM tenant_subscriptions s LEFT JOIN packages p ON p.id = s.package_id
            WHERE s.company_id=@c
            """, c => c.Parameters.AddWithValue("@c", companyId), ct);
        return Results.Json(ApiResponse<object>.Ok(new { subscription = sub }));
    }

    private static async Task<IResult> SubscriptionUsage(HttpContext http, Database db, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(http, "dashboard:view");
        if (denied is not null) return denied;
        return Results.Json(ApiResponse<object>.Ok(await BuildUsageAsync(db, EndpointMappings.GetCompanyId(http), ct)));
    }

    private static async Task<IResult> SubscriptionEntitlements(HttpContext http, Database db, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(http, "dashboard:view");
        if (denied is not null) return denied;
        var companyId = EndpointMappings.GetCompanyId(http);
        var items = await db.QueryAsync("SELECT module_key, enabled, limit_value, tier, source FROM tenant_entitlements WHERE company_id=@c ORDER BY module_key",
            c => c.Parameters.AddWithValue("@c", companyId), ct);
        return Results.Json(ApiResponse<object>.Ok(new { items }));
    }

    // ════════════════════════════ Shared projections ════════════════════════

    private static async Task<object> BuildUsageAsync(Database db, long companyId, CancellationToken ct)
    {
        var period = EntitlementService.CurrentPeriodKey();
        var rows = await db.QueryAsync("""
            SELECT m.meter_key, m.name, m.unit, m.period,
                   COALESCE(c.value, 0) AS value,
                   e.limit_value
            FROM usage_meters m
            LEFT JOIN usage_counters c
              ON c.meter_key = m.meter_key AND c.company_id = @c
             AND c.period_key = CASE WHEN m.period = 'lifetime' THEN 'lifetime' ELSE @p END
            LEFT JOIN tenant_entitlements e
              ON e.module_key = m.module_key AND e.company_id = @c
            WHERE m.active
            ORDER BY m.meter_key
            """,
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@p", period); }, ct);
        return new { period, meters = rows };
    }

    private static async Task<object> BuildInvoicePreviewAsync(Database db, long companyId, CancellationToken ct)
    {
        var period = EntitlementService.CurrentPeriodKey();
        var sub = await db.QuerySingleAsync("""
            SELECT s.*, p.id package_id, p.name package_name, p.base_price_cents, p.seat_price_cents,
                   p.included_seats, p.currency
            FROM tenant_subscriptions s LEFT JOIN packages p ON p.id = s.package_id
            WHERE s.company_id=@c
            """, c => c.Parameters.AddWithValue("@c", companyId), ct);

        var currency = sub?["currency"]?.ToString() ?? sub?["billingCurrency"]?.ToString() ?? "USD";
        var lineItems = new List<object>();
        long total = 0;

        long basePrice = sub?["basePriceCents"] is { } bp and not DBNull ? Convert.ToInt64(bp) : 0;
        if (sub is not null)
        {
            lineItems.Add(new { description = $"Subscription — {sub["packageName"] ?? "Plan"}", quantity = 1, unitPriceCents = basePrice, amountCents = basePrice });
            total += basePrice;
        }

        // Metered overage lines from pricing rules for the package (if any).
        long? packageId = sub?["packageId"] is { } pid and not DBNull ? Convert.ToInt64(pid) : null;
        if (packageId is not null)
        {
            var rules = await db.QueryAsync("""
                SELECT pr.meter_key, pr.included_quantity, pr.unit_price_cents, pr.overage_allowed,
                       COALESCE(uc.value,0) AS used,
                       o.included_quantity AS o_included, o.unit_price_cents AS o_unit, o.flat_discount_cents AS o_disc
                FROM pricing_rules pr
                LEFT JOIN usage_meters m ON m.meter_key = pr.meter_key
                LEFT JOIN usage_counters uc
                  ON uc.meter_key = pr.meter_key AND uc.company_id = @c
                 AND uc.period_key = CASE WHEN m.period='lifetime' THEN 'lifetime' ELSE @p END
                LEFT JOIN tenant_contract_overrides o ON o.company_id=@c AND o.meter_key=pr.meter_key
                WHERE pr.package_id=@pkg AND pr.active
                """,
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@p", period); c.Parameters.AddWithValue("@pkg", packageId.Value); }, ct);

            foreach (var r in rules)
            {
                decimal used = r["used"] is { } u and not DBNull ? Convert.ToDecimal(u) : 0;
                decimal included = r["oIncluded"] is { } oi and not DBNull ? Convert.ToDecimal(oi)
                                 : (r["includedQuantity"] is { } inc and not DBNull ? Convert.ToDecimal(inc) : 0);
                long unit = r["oUnit"] is { } ou and not DBNull ? Convert.ToInt64(ou)
                          : (r["unitPriceCents"] is { } up and not DBNull ? Convert.ToInt64(up) : 0);
                long disc = r["oDisc"] is { } od and not DBNull ? Convert.ToInt64(od) : 0;
                bool overageAllowed = r["overageAllowed"] is bool oa && oa;

                var overage = used > included ? used - included : 0;
                if (overage <= 0 || !overageAllowed) continue;
                long amount = (long)(overage * unit) - disc;
                if (amount < 0) amount = 0;
                lineItems.Add(new { description = $"Overage — {r["meterKey"]}", quantity = overage, unitPriceCents = unit, discountCents = disc, amountCents = amount });
                total += amount;
            }
        }

        return new
        {
            period,
            currency,
            companyId,
            status = sub?["status"] ?? "none",
            lineItems,
            totalCents = total,
            note = sub is null ? "No active subscription for this tenant." : null,
        };
    }
}
