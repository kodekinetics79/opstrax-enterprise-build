using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

// Runtime feature-entitlement + usage-metering service. Tenant-scoped, reads the
// tenant_entitlements / usage_* tables created by RevenueSchemaService.
//
// ENFORCEMENT PHILOSOPHY (backwards-compatible):
//   • A module is BLOCKED only when an explicit tenant_entitlements row exists with
//     enabled = false. Absence of a row = allowed. This means existing/ungoverned
//     tenants keep working, while Platform Admin can switch a module off per tenant.
//   • Limits are enforced only when an entitlement row carries a non-null limit_value
//     AND the override/contract does not allow overage.
public sealed class EntitlementService(Database db)
{
    public sealed record EntitlementDecision(bool Allowed, string? Reason);

    public static string CurrentPeriodKey() => DateTime.UtcNow.ToString("yyyy-MM");

    // ── Market packs (paid add-ons; DENY-BY-DEFAULT) ────────────────────────
    // Unlike core fleet modules (allow-unless-disabled), a market pack is only
    // accessible when the tenant has an ACTIVE tenant_market_packs assignment.
    public async Task<bool> HasMarketPackAsync(long companyId, string packCode, CancellationToken ct = default)
    {
        var n = await db.ScalarLongAsync(
            "SELECT COUNT(*) FROM tenant_market_packs WHERE company_id=@c AND pack_code=@p AND status='active'",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@p", packCode); }, ct);
        return n > 0;
    }

    public async Task<EntitlementDecision> CheckMarketPackAsync(long companyId, string packCode, CancellationToken ct = default)
        => await HasMarketPackAsync(companyId, packCode, ct)
            ? new EntitlementDecision(true, null)
            : new EntitlementDecision(false, $"Market pack '{packCode}' is not enabled for this tenant.");

    // Is the module enabled for this tenant? Blocked only on explicit disable.
    public async Task<EntitlementDecision> CheckModuleAsync(long companyId, string moduleKey, CancellationToken ct = default)
    {
        var row = await db.QuerySingleAsync(
            "SELECT enabled FROM tenant_entitlements WHERE company_id=@c AND module_key=@m",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@m", moduleKey); }, ct);
        if (row is null) return new EntitlementDecision(true, null); // ungoverned → allow
        var enabled = row["enabled"] is bool b && b;
        return enabled
            ? new EntitlementDecision(true, null)
            : new EntitlementDecision(false, $"Module '{moduleKey}' is not included in this tenant's plan.");
    }

    // Check a metered limit before allowing a create. meterKey drives the counter;
    // moduleKey (optional) supplies the entitlement limit_value. Overage is permitted
    // when a contract override for the meter exists (any row) — Platform Admin grants
    // overage by inserting an override.
    public async Task<EntitlementDecision> CheckLimitAsync(long companyId, string moduleKey, string meterKey, CancellationToken ct = default)
    {
        var ent = await db.QuerySingleAsync(
            "SELECT enabled, limit_value FROM tenant_entitlements WHERE company_id=@c AND module_key=@m",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@m", moduleKey); }, ct);
        if (ent is not null && ent["enabled"] is bool e && !e)
            return new EntitlementDecision(false, $"Module '{moduleKey}' is not included in this tenant's plan.");

        var limit = ent?["limitValue"];
        if (limit is null or DBNull) return new EntitlementDecision(true, null); // no cap

        var limitValue = Convert.ToInt32(limit);
        var overageAllowed = await db.ScalarLongAsync(
            "SELECT COUNT(*) FROM tenant_contract_overrides WHERE company_id=@c AND (meter_key=@k OR meter_key IS NULL)",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@k", meterKey); }, ct) > 0;
        if (overageAllowed) return new EntitlementDecision(true, null);

        var used = await GetUsageValueAsync(companyId, meterKey, ct);
        return used >= limitValue
            ? new EntitlementDecision(false, $"Plan limit reached for '{meterKey}' ({limitValue}). Enable overage or upgrade the plan.")
            : new EntitlementDecision(true, null);
    }

    public async Task<decimal> GetUsageValueAsync(long companyId, string meterKey, CancellationToken ct = default)
    {
        var period = await MeterPeriodKeyAsync(meterKey, ct);
        return await db.ScalarDecimalAsync(
            "SELECT COALESCE(value,0) FROM usage_counters WHERE company_id=@c AND meter_key=@m AND period_key=@p",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@m", meterKey); c.Parameters.AddWithValue("@p", period); }, ct) ?? 0m;
    }

    // Records a usage event and bumps the rolled-up counter for the period. Never
    // throws into the caller's request path — metering must not break a tenant action.
    public async Task RecordAsync(long companyId, string meterKey, decimal quantity = 1, string? reference = null, string? actor = null, CancellationToken ct = default)
    {
        try
        {
            var period = await MeterPeriodKeyAsync(meterKey, ct);
            await db.ExecuteAsync(
                "INSERT INTO usage_events (company_id, meter_key, quantity, reference, actor, period_key) VALUES (@c,@m,@q,@r,@a,@p)",
                c =>
                {
                    c.Parameters.AddWithValue("@c", companyId);
                    c.Parameters.AddWithValue("@m", meterKey);
                    c.Parameters.AddWithValue("@q", quantity);
                    c.Parameters.AddWithValue("@r", (object?)reference ?? DBNull.Value);
                    c.Parameters.AddWithValue("@a", (object?)actor ?? DBNull.Value);
                    c.Parameters.AddWithValue("@p", period);
                }, ct);
            await db.ExecuteAsync("""
                INSERT INTO usage_counters (company_id, meter_key, period_key, value, updated_at)
                VALUES (@c,@m,@p,@q,NOW())
                ON CONFLICT (company_id, meter_key, period_key)
                DO UPDATE SET value = usage_counters.value + EXCLUDED.value, updated_at = NOW()
                """,
                c =>
                {
                    c.Parameters.AddWithValue("@c", companyId);
                    c.Parameters.AddWithValue("@m", meterKey);
                    c.Parameters.AddWithValue("@p", period);
                    c.Parameters.AddWithValue("@q", quantity);
                }, ct);
        }
        catch { /* metering is best-effort; never fail the tenant's action */ }
    }

    private async Task<string> MeterPeriodKeyAsync(string meterKey, CancellationToken ct)
    {
        var period = await db.QuerySingleAsync(
            "SELECT period FROM usage_meters WHERE meter_key=@m",
            c => c.Parameters.AddWithValue("@m", meterKey), ct);
        var p = period?["period"]?.ToString();
        return string.Equals(p, "lifetime", StringComparison.OrdinalIgnoreCase) ? "lifetime" : CurrentPeriodKey();
    }
}
