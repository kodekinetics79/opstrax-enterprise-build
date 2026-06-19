namespace Zayra.Api.Application.CountryPack;

// Reads effective-dated configuration values from the statutory_rules table.
// Lookup order: tenant-specific row → platform default (TenantId = null).
// Both decimal and string accessors return null when no row is found —
// callers should fall back to a hard-coded safe default.

public interface IStatutoryRuleReader
{
    Task<decimal?> GetDecimalAsync(
        string countryCode,
        string jurisdiction,
        string ruleKey,
        DateOnly effectiveDate,
        Guid? tenantId = null,
        CancellationToken ct = default);

    Task<string?> GetStringAsync(
        string countryCode,
        string jurisdiction,
        string ruleKey,
        DateOnly effectiveDate,
        Guid? tenantId = null,
        CancellationToken ct = default);
}
