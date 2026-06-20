using Microsoft.EntityFrameworkCore;
using Zayra.Api.Application.CountryPack;
using Zayra.Api.Data;

namespace Zayra.Api.Infrastructure.CountryPack;

public sealed class StatutoryRuleReader : IStatutoryRuleReader
{
    private readonly ZayraDbContext _db;

    public StatutoryRuleReader(ZayraDbContext db) => _db = db;

    public async Task<decimal?> GetDecimalAsync(
        string countryCode, string jurisdiction, string ruleKey,
        DateOnly effectiveDate, Guid? tenantId = null, CancellationToken ct = default)
    {
        var raw = await FetchAsync(countryCode, jurisdiction, ruleKey, effectiveDate, tenantId, ct);
        if (raw is null) return null;
        return decimal.TryParse(raw, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    public async Task<string?> GetStringAsync(
        string countryCode, string jurisdiction, string ruleKey,
        DateOnly effectiveDate, Guid? tenantId = null, CancellationToken ct = default)
        => await FetchAsync(countryCode, jurisdiction, ruleKey, effectiveDate, tenantId, ct);

    private async Task<string?> FetchAsync(
        string countryCode, string jurisdiction, string ruleKey,
        DateOnly effectiveDate, Guid? tenantId, CancellationToken ct)
    {
        var cutoff = effectiveDate.ToDateTime(TimeOnly.MinValue);

        // Prefer tenant override; fall back to platform default (TenantId = null).
        var row = await _db.StatutoryRules
            .IgnoreQueryFilters()
            .Where(r => r.CountryCode == countryCode
                     && r.Jurisdiction == jurisdiction
                     && r.RuleKey == ruleKey
                     && r.EffectiveFrom <= cutoff
                     && (r.EffectiveTo == null || r.EffectiveTo > cutoff)
                     && (tenantId == null
                         ? r.TenantId == null
                         : r.TenantId == tenantId || r.TenantId == null))
            // Tenant-specific rows rank above platform defaults; newest effective date wins.
            .OrderByDescending(r => r.TenantId != null)
            .ThenByDescending(r => r.EffectiveFrom)
            .Select(r => r.RuleValue)
            .FirstOrDefaultAsync(ct);

        return row;
    }
}
