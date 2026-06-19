using Zayra.Api.Application.CountryPack;

namespace Zayra.Api.Tests;

// In-memory IStatutoryRuleReader for unit tests — no database required.
// Keys are looked up by ruleKey only (ignores country/jurisdiction/date for simplicity).
// Configure per-test by chaining .Set(key, value) calls.

internal sealed class StubRuleReader : IStatutoryRuleReader
{
    private readonly Dictionary<string, decimal> _decimals = new();
    private readonly Dictionary<string, string>  _strings  = new();

    public StubRuleReader Set(string ruleKey, decimal value)
    {
        _decimals[ruleKey] = value;
        return this;
    }

    public StubRuleReader Set(string ruleKey, string value)
    {
        _strings[ruleKey] = value;
        return this;
    }

    public Task<decimal?> GetDecimalAsync(
        string countryCode, string jurisdiction, string ruleKey,
        DateOnly effectiveDate, Guid? tenantId = null, CancellationToken ct = default)
        => Task.FromResult(_decimals.TryGetValue(ruleKey, out var v) ? (decimal?)v : null);

    public Task<string?> GetStringAsync(
        string countryCode, string jurisdiction, string ruleKey,
        DateOnly effectiveDate, Guid? tenantId = null, CancellationToken ct = default)
        => Task.FromResult(_strings.TryGetValue(ruleKey, out var v) ? v : null);
}
