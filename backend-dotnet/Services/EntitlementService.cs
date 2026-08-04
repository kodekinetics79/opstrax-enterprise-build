using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

// Runtime feature-entitlement + usage-metering service. Tenant-scoped, reads the
// tenant_entitlements / usage_* tables created by RevenueSchemaService.
//
// ENFORCEMENT PHILOSOPHY (backwards-compatible):
//   • legacy_allow: a module is blocked only by an explicit disabled row. Existing
//     tenants were migrated to this mode, so rollout does not remove access.
//   • package_allowlist: a module is allowed only by an explicit enabled row. New
//     tenants default to this mode, making package omission deny-by-default.
//   • Limits are enforced only when an entitlement row carries a non-null limit_value
//     AND the override/contract does not allow overage.
public sealed class EntitlementService(Database db)
{
    public const string LegacyAllowPolicy = "legacy_allow";
    public const string PackageAllowlistPolicy = "package_allowlist";
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

    // Is the module enabled for this tenant? The tenant's explicit policy determines
    // whether a missing row inherits allow (legacy) or deny (package allowlist).
    public async Task<EntitlementDecision> CheckModuleAsync(long companyId, string moduleKey, CancellationToken ct = default)
    {
        var row = await db.QuerySingleAsync(
            """
                SELECT c.entitlement_policy_mode, e.enabled
                FROM companies c
                LEFT JOIN tenant_entitlements e
                  ON e.company_id=c.id AND e.module_key=@m
                WHERE c.id=@c
                """,
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@m", moduleKey); }, ct);
        if (row is null)
            return new EntitlementDecision(false, "Tenant entitlement policy could not be resolved.");

        var policy = row["entitlementPolicyMode"]?.ToString() ?? LegacyAllowPolicy;
        var hasExplicitRow = row["enabled"] is bool;
        var enabled = row["enabled"] is bool b && b;
        var allowed = policy switch
        {
            PackageAllowlistPolicy => hasExplicitRow && enabled,
            LegacyAllowPolicy => !hasExplicitRow || enabled,
            _ => false, // corrupted/unknown policy must never become a fail-open path
        };
        return allowed
            ? new EntitlementDecision(true, null)
            : new EntitlementDecision(false, $"Module '{moduleKey}' is not included in this tenant's plan.");
    }

    // Check a metered limit before allowing a create. meterKey drives the counter;
    // moduleKey (optional) supplies the entitlement limit_value. Overage is permitted
    // when a contract override for the meter exists (any row) — Platform Admin grants
    // overage by inserting an override.
    public async Task<EntitlementDecision> CheckLimitAsync(long companyId, string moduleKey, string meterKey, CancellationToken ct = default)
    {
        var moduleDecision = await CheckModuleAsync(companyId, moduleKey, ct);
        if (!moduleDecision.Allowed) return moduleDecision;

        var ent = await db.QuerySingleAsync(
            "SELECT enabled, limit_value FROM tenant_entitlements WHERE company_id=@c AND module_key=@m",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@m", moduleKey); }, ct);

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
