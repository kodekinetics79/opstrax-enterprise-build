using Opstrax.Api.Services;

namespace Opstrax.Tests;

public sealed class TenantMarketPolicyTests
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Root, .. parts]));

    [Theory]
    [InlineData("SA", MarketPackSchemaService.Packs.SaudiGcc)]
    [InlineData("CA", MarketPackSchemaService.Packs.CanadaNa)]
    [InlineData("US", MarketPackSchemaService.Packs.CanadaNa)]
    public void CountryResolvesExactlyOneCompatiblePack(string country, string expectedPack)
    {
        Assert.Equal(expectedPack, TenantMarketPolicyService.PackForCountry(country));
        Assert.True(TenantMarketPolicyService.IsPackCompatible(country, expectedPack));
        var incompatible = expectedPack == MarketPackSchemaService.Packs.SaudiGcc
            ? MarketPackSchemaService.Packs.CanadaNa
            : MarketPackSchemaService.Packs.SaudiGcc;
        Assert.False(TenantMarketPolicyService.IsPackCompatible(country, incompatible));
    }

    [Fact]
    public void SaudiPolicyUsesRiyadhAndMetricUnits()
    {
        var policy = TenantMarketPolicyService.DefaultsFor("sa");
        Assert.Equal("Asia/Riyadh", policy.Timezone);
        Assert.Equal("Kilometers", policy.DistanceUnit);
        Assert.Equal("Liters", policy.VolumeUnit);
    }

    [Theory]
    [InlineData("AE")]
    [InlineData("QA")]
    [InlineData("KW")]
    [InlineData("BH")]
    [InlineData("OM")]
    public void OtherGccCountriesFailClosedUntilTheirOwnRegulatoryProfileIsImplemented(string country)
    {
        Assert.Null(TenantMarketPolicyService.PackForCountry(country));
        Assert.False(TenantMarketPolicyService.IsPackCompatible(country, MarketPackSchemaService.Packs.SaudiGcc));
    }

    [Fact]
    public void UnassignedOrUnsupportedCountryFailsClosed()
    {
        Assert.Null(TenantMarketPolicyService.PackForCountry(null));
        Assert.Null(TenantMarketPolicyService.PackForCountry(""));
        Assert.Null(TenantMarketPolicyService.PackForCountry("ZZ"));
        Assert.False(TenantMarketPolicyService.IsPackCompatible("ZZ", MarketPackSchemaService.Packs.SaudiGcc));
    }

    [Fact]
    public void DvirAndTaxMutationsUseTheAuthoritativeTenantMarket()
    {
        var dvir = Read("backend-dotnet", "Controllers", "DvirHosEndpoints.cs");
        var tax = Read("backend-dotnet", "Controllers", "TaxEndpoints.cs");

        Assert.Contains("TenantMarketPolicyService(db).GetAsync", dvir, StringComparison.Ordinal);
        Assert.True(dvir.Split("RejectDvirCountryMismatch", StringSplitOptions.None).Length >= 6,
            "All four mapped DVIR mutations must validate the requested country through the shared guard.");
        Assert.DoesNotContain("PilotText(body, \"countryCode\", 12) ?? \"US\"", dvir, StringComparison.Ordinal);
        Assert.Contains("country_code=@country", dvir, StringComparison.Ordinal);
        Assert.Contains("c.Parameters.AddWithValue(\"@country\", market!.CountryCode)", dvir, StringComparison.Ordinal);

        Assert.Contains("RequireMarketProfileAsync", tax, StringComparison.Ordinal);
        Assert.Contains("UpsertRule(HttpContext http, long id, Dictionary<string, object?> body, TaxService svc, Database db", tax, StringComparison.Ordinal);
        Assert.Contains("PublishProfile(HttpContext http, long id, TaxService svc, Database db", tax, StringComparison.Ordinal);
        Assert.True(tax.Split("RequireMarketProfileAsync(http, id, svc, db, ct)", StringSplitOptions.None).Length == 3,
            "Both tax rule mutation and tax profile publication must reject a stale profile from another market.");
    }

    [Fact]
    public void SaudiFacingConfigurationDoesNotExposeEditableUsDefaults()
    {
        var dvirPage = Read("frontend", "src", "pages", "DvirInspectionsPage.tsx");
        var settingsPage = Read("frontend", "src", "pages", "SettingsPage.tsx");
        var hosPage = Read("frontend", "src", "pages", "HosEldPage.tsx");

        Assert.Contains("settingsApi.marketContextGet", dvirPage, StringComparison.Ordinal);
        Assert.Contains("Country · operating market locked", dvirPage, StringComparison.Ordinal);
        Assert.Contains("readOnly aria-readonly=\"true\"", dvirPage, StringComparison.Ordinal);
        Assert.DoesNotContain("countryCode: \"US\"", dvirPage, StringComparison.Ordinal);
        Assert.DoesNotContain("placeholder=\"US pre-trip inspection\"", dvirPage, StringComparison.Ordinal);

        Assert.DoesNotContain("defaultCountry:  \"US\"", settingsPage, StringComparison.Ordinal);
        Assert.DoesNotContain("timezone:        \"America/New_York\"", settingsPage, StringComparison.Ordinal);
        Assert.DoesNotContain("currency:        \"USD\"", settingsPage, StringComparison.Ordinal);
        Assert.Contains("countryCode: String(market.countryCode)", settingsPage, StringComparison.Ordinal);

        Assert.Contains("tenantCountry === \"US\"", hosPage, StringComparison.Ordinal);
        Assert.Contains("useTenantCountry", hosPage, StringComparison.Ordinal);
    }

    [Fact]
    public void PredeployOwnsCanonicalMarketReferencesWithoutFixedDatabaseIds()
    {
        var migration = Read("database", "migrations", "2026_09_15_stage145_canonical_market_reference_reconciliation.sql");
        var ownerRunner = Read("tools", "apply-neon-predeploy-migrations.sh");
        var complianceRunner = Read("tools", "apply-canada-ksa-compliance-predeploy.sh");

        Assert.Contains("2026_09_15_stage145_canonical_market_reference_reconciliation", ownerRunner, StringComparison.Ordinal);
        Assert.Contains("ON CONFLICT (code) DO UPDATE", migration, StringComparison.Ordinal);
        Assert.Contains("p.country_code=s.country_code AND p.profile_name=s.profile_name", migration, StringComparison.Ordinal);
        Assert.Contains("r.rule_code=s.rule_code", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("OVERRIDING SYSTEM VALUE", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("WHERE id=3", complianceRunner, StringComparison.Ordinal);
        Assert.DoesNotContain("WHERE id=4", complianceRunner, StringComparison.Ordinal);
        Assert.Contains("JOIN compliance_profiles p ON p.id=r.profile_id", complianceRunner, StringComparison.Ordinal);
    }
}
