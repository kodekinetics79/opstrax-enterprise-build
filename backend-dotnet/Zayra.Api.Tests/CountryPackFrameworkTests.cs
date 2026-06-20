using Zayra.Api.Application.CountryPack;
using Zayra.Api.Infrastructure.CountryPack;

namespace Zayra.Api.Tests;

// Framework-level tests: resolver lookup, fallback chain, default-pack zero-value contracts.
public class CountryPackFrameworkTests
{
    private static readonly SalaryBreakdown TestSalary = new(10_000m, 3_000m, 1_000m, 500m);
    private static readonly DateOnly Start = new(2020, 1, 1);
    private static readonly DateOnly End   = new(2023, 1, 1);

    // ── Default pack returns safe zero-value results ──────────────────────────

    [Fact]
    public async Task DefaultDeductionCalculator_ReturnsSafeZeroResult()
    {
        var calc = new DefaultStatutoryDeductionCalculator();
        var input = new StatutoryDeductionInput(Guid.NewGuid(), Guid.NewGuid(), TestSalary, "SAU", "Unlimited", 2026, 6);
        var result = await calc.CalculateAsync(input);

        Assert.Equal(0m, result.TotalEmployeeDeduction);
        Assert.Equal(0m, result.TotalEmployerContribution);
        Assert.Empty(result.Lines);
    }

    [Fact]
    public async Task DefaultEosCalculator_ReturnsSafeZeroResult()
    {
        var calc = new DefaultEndOfServiceCalculator();
        var input = new EndOfServiceInput(Guid.NewGuid(), Guid.NewGuid(), TestSalary, Start, End, "Resignation", "Unlimited", "SAU");
        var result = await calc.CalculateAsync(input);

        Assert.Equal(0m, result.TotalGratuity);
        Assert.NotEmpty(result.ApplicableRule);
    }

    [Fact]
    public async Task DefaultWpsExporter_ReturnsSafeEmptyResult()
    {
        var exporter = new DefaultWageProtectionExporter();
        var input = new WageProtectionExportInput(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2026, 6,
            "EST001", "SA0000000000000000000000", "Test Co", "شركة", Array.Empty<WpsEmployee>());
        var result = await exporter.ExportAsync(input);

        Assert.Empty(result.FileBytes);
        Assert.Equal(0, result.RecordCount);
    }

    [Fact]
    public async Task DefaultNatTracker_ReturnsNotApplicable()
    {
        var tracker = new DefaultNationalizationTracker();
        var input = new NationalizationInput(Guid.NewGuid(), Guid.NewGuid(), 50, 10);
        var result = await tracker.GetStatusAsync(input);

        Assert.Equal(NationalizationComplianceStatus.NotApplicable, result.Status);
    }

    [Fact]
    public void DefaultLocalizationProfile_ReturnsNonEmptyProfile()
    {
        var profile = new DefaultLocalizationProfile().GetProfile();
        Assert.NotEmpty(profile.CurrencyCode);
        Assert.NotEmpty(profile.LocaleCode);
        Assert.False(profile.IsRtl);
    }

    // ── SalaryBreakdown computed properties ───────────────────────────────────

    [Fact]
    public void SalaryBreakdown_ComputedProperties_AreCorrect()
    {
        var s = new SalaryBreakdown(8_000m, 3_000m, 1_000m, 500m);
        Assert.Equal(12_500m, s.Gross);
        Assert.Equal(11_000m, s.GosiCoveredWage);   // basic + housing
        Assert.Equal(11_000m, s.GpssaBase);
    }

    // ── Jurisdiction constants coverage ──────────────────────────────────────

    [Theory]
    [InlineData(CountryCodes.Saudi,  Jurisdictions.KsaMainland)]
    [InlineData(CountryCodes.Qatar,  Jurisdictions.QatarMainland)]
    [InlineData(CountryCodes.UAE,    Jurisdictions.UAEMainland)]
    [InlineData(CountryCodes.UAE,    Jurisdictions.Difc)]
    [InlineData(CountryCodes.UAE,    Jurisdictions.Adgm)]
    public void JurisdictionConstants_AreNonEmptyAndContainDash(string cc, string jurisdiction)
    {
        Assert.NotEmpty(cc);
        Assert.NotEmpty(jurisdiction);
        Assert.Contains("-", jurisdiction);
    }
}
