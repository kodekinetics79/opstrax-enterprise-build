using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

/// <summary>
/// Resolves the single operating-market policy for a tenant.  The companies row
/// is the authority; tenant-editable profile and locale rows must never select a
/// different regulatory market.
/// </summary>
public sealed class TenantMarketPolicyService(Database db)
{
    public sealed record MarketContext(
        string CountryCode,
        string CountryName,
        string Currency,
        string Locale,
        string Timezone,
        string TextDirection,
        string CalendarSystem,
        string InvoicingScheme,
        string TaxIdLabel,
        decimal? DefaultTaxRate,
        string DistanceUnit,
        string VolumeUnit,
        string? MarketPackCode,
        string? MarketPackName,
        bool MarketLocked,
        string ManagedBy);

    private sealed record Defaults(string Timezone, string DistanceUnit, string VolumeUnit);

    private static readonly IReadOnlyDictionary<string, Defaults> CountryDefaults =
        new Dictionary<string, Defaults>(StringComparer.OrdinalIgnoreCase)
        {
            ["SA"] = new("Asia/Riyadh", "Kilometers", "Liters"),
            ["AE"] = new("Asia/Dubai", "Kilometers", "Liters"),
            ["QA"] = new("Asia/Qatar", "Kilometers", "Liters"),
            ["KW"] = new("Asia/Kuwait", "Kilometers", "Liters"),
            ["BH"] = new("Asia/Bahrain", "Kilometers", "Liters"),
            ["OM"] = new("Asia/Muscat", "Kilometers", "Liters"),
            ["CA"] = new("America/Toronto", "Kilometers", "Liters"),
            ["US"] = new("America/New_York", "Miles", "Gallons"),
        };

    public static string NormalizeCountry(string? value) =>
        (value ?? string.Empty).Trim().ToUpperInvariant();

    public static string? PackForCountry(string? countryCode) => NormalizeCountry(countryCode) switch
    {
        "US" or "CA" => MarketPackSchemaService.Packs.CanadaNa,
        // The current regional implementation contains Saudi TGA/ZATCA rules.
        // Other GCC countries need their own approved profile before activation.
        "SA" => MarketPackSchemaService.Packs.SaudiGcc,
        _ => null,
    };

    public static bool IsPackCompatible(string? countryCode, string? packCode) =>
        string.Equals(PackForCountry(countryCode), packCode?.Trim(), StringComparison.OrdinalIgnoreCase);

    public static (string Timezone, string DistanceUnit, string VolumeUnit) DefaultsFor(string? countryCode)
    {
        var code = NormalizeCountry(countryCode);
        var values = CountryDefaults.TryGetValue(code, out var defaults)
            ? defaults
            : new Defaults("UTC", "Kilometers", "Liters");
        return (values.Timezone, values.DistanceUnit, values.VolumeUnit);
    }

    public async Task<MarketContext?> GetAsync(long companyId, CancellationToken ct = default)
    {
        var row = await db.QuerySingleAsync(
            @"SELECT c.country, c.currency, c.timezone,
                     p.country_name, p.default_currency, p.default_locale,
                     p.text_direction, p.calendar_system, p.invoicing_scheme,
                     p.tax_id_label, p.default_tax_rate
                FROM companies c
                LEFT JOIN country_profiles p ON p.country_code = UPPER(c.country)
               WHERE c.id = @cid",
            c => c.Parameters.AddWithValue("@cid", companyId), ct);

        if (row is null) return null;
        var country = NormalizeCountry(row.GetValueOrDefault("country")?.ToString());
        if (country.Length != 2) return null; // fail closed until Platform Admin assigns a market

        var profile = new CountryProfileService(db);
        var countryProfile = await profile.GetAsync(country, ct);
        if (countryProfile is null) return null;

        var defaults = DefaultsFor(country);
        var packCode = PackForCountry(country);
        Dictionary<string, object?>? pack = null;
        if (packCode is not null)
        {
            pack = await db.QuerySingleAsync(
                "SELECT name FROM market_packs WHERE code=@code AND status='active'",
                c => c.Parameters.AddWithValue("@code", packCode), ct);
        }

        return new MarketContext(
            country,
            countryProfile.CountryName,
            countryProfile.DefaultCurrency,
            countryProfile.DefaultLocale,
            defaults.Timezone,
            countryProfile.TextDirection,
            countryProfile.CalendarSystem,
            countryProfile.InvoicingScheme,
            countryProfile.TaxIdLabel,
            countryProfile.DefaultTaxRate,
            defaults.DistanceUnit,
            defaults.VolumeUnit,
            packCode,
            pack?.GetValueOrDefault("name")?.ToString(),
            true,
            "Platform Admin");
    }
}
