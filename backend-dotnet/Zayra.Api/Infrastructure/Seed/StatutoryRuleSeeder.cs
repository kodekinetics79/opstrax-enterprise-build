using Microsoft.EntityFrameworkCore;
using Zayra.Api.Application.CountryPack;
using Zayra.Api.Data;
using Zayra.Api.Models;

namespace Zayra.Api.Infrastructure.Seed;

// Seeds directional statutory rates for KSA, UAE, and Qatar.
// All values are DEMO-CREDIBLE approximations based on publicly available
// legal sources.  Each rate is tagged with a source note.
// ⚠️  VERIFY-AT-IMPLEMENTATION: obtain the current certified values from the
//     relevant regulatory authority before use in a production payroll run.
// Idempotent — skips rows that already exist.

public static class StatutoryRuleSeeder
{
    private static readonly DateTime Ts = DateTime.UtcNow;

    public static async Task SeedAsync(ZayraDbContext db, ILogger logger)
    {
        var rules = BuildRules();
        var added = 0;

        foreach (var rule in rules)
        {
            // IgnoreQueryFilters is intentional: seeder checks platform-default rows (TenantId == null)
            // which are excluded by the per-tenant global query filter; this read is read-only idempotency check.
            bool exists = await db.StatutoryRules
                .IgnoreQueryFilters()
                .AnyAsync(r =>
                    r.TenantId == null
                    && r.CountryCode  == rule.CountryCode
                    && r.Jurisdiction == rule.Jurisdiction
                    && r.RuleKey      == rule.RuleKey
                    && r.EffectiveFrom == rule.EffectiveFrom);

            if (!exists)
            {
                db.StatutoryRules.Add(rule);
                added++;
            }
        }

        if (added > 0)
        {
            await db.SaveChangesAsync();
            logger.LogInformation("StatutoryRuleSeeder: inserted {Count} default rules.", added);
        }
    }

    private static List<StatutoryRule> BuildRules()
    {
        var list = new List<StatutoryRule>();
        var eff16 = new DateTime(2016, 6, 1);   // GOSI regulation effective date
        var eff22 = new DateTime(2022, 1, 1);   // UAE/Qatar post-reform effective date

        // ── KSA GOSI ─────────────────────────────────────────────────────────
        // Source: GOSI Regulation 2016 (Royal Decree M/33).  Annuity + SANED.
        list.Add(Rule(CountryCodes.Saudi, Jurisdictions.KsaMainland,
            "gosi.saudi_employee_rate", "0.09", "decimal", eff16,
            "VERIFY: GOSI Annuities employee 9% — Royal Decree M/33 2016"));
        list.Add(Rule(CountryCodes.Saudi, Jurisdictions.KsaMainland,
            "gosi.saudi_employer_rate", "0.09", "decimal", eff16,
            "VERIFY: GOSI Annuities employer 9% — Royal Decree M/33 2016"));
        list.Add(Rule(CountryCodes.Saudi, Jurisdictions.KsaMainland,
            "gosi.saned_rate", "0.0075", "decimal", eff16,
            "VERIFY: SANED 0.75% each side — GOSI Regulation 2016"));
        list.Add(Rule(CountryCodes.Saudi, Jurisdictions.KsaMainland,
            "gosi.expat_occupational_hazard_rate", "0.02", "decimal", eff16,
            "VERIFY: Occupational Hazard 2% employer — GOSI Regulation 2016"));
        list.Add(Rule(CountryCodes.Saudi, Jurisdictions.KsaMainland,
            "gosi.covered_wage_ceiling_sar", "45000", "decimal", eff16,
            "VERIFY: GOSI covered wage ceiling SAR 45,000 — confirm current ceiling"));
        list.Add(Rule(CountryCodes.Saudi, Jurisdictions.KsaMainland,
            "nitaqat.default_target_ratio", "0.35", "decimal", eff16,
            "VERIFY: Nitaqat target ratio varies by sector; 35% is directional — confirm with HRSD"));

        // ── UAE GPSSA ────────────────────────────────────────────────────────
        // Source: Federal Law 7/1999 + Cabinet Resolution 50/2022.
        list.Add(Rule(CountryCodes.UAE, Jurisdictions.UAEMainland,
            "gpssa.national_employee_rate", "0.05", "decimal", eff22,
            "VERIFY: GPSSA employee 5% — Federal Law 7/1999 as amended"));
        list.Add(Rule(CountryCodes.UAE, Jurisdictions.UAEMainland,
            "gpssa.national_employer_rate", "0.125", "decimal", eff22,
            "VERIFY: GPSSA employer 12.5% — confirm current rate with GPSSA"));
        list.Add(Rule(CountryCodes.UAE, Jurisdictions.UAEMainland,
            "emiratisation.target_ratio", "0.10", "decimal", eff22,
            "VERIFY: Emiratisation 10% target varies by sector — confirm with Nafis/MOHRE"));

        // UAE DIFC DEWS
        // Source: DIFC Employment Law 2/2019, Schedule 1.
        list.Add(Rule(CountryCodes.UAE, Jurisdictions.Difc,
            "dews.tier1_monthly_rate", "0.0583", "decimal", eff22,
            "VERIFY: DEWS 5.83% monthly (yrs 1-5) — DIFC Law 2/2019 Schedule 1"));
        list.Add(Rule(CountryCodes.UAE, Jurisdictions.Difc,
            "dews.tier2_monthly_rate", "0.0833", "decimal", eff22,
            "VERIFY: DEWS 8.33% monthly (yrs 5+) — DIFC Law 2/2019 Schedule 1"));

        // ── Qatar GRSIA ───────────────────────────────────────────────────────
        // Source: Qatar Law 24/2002 (GRSIA).
        list.Add(Rule(CountryCodes.Qatar, Jurisdictions.QatarMainland,
            "grsia.national_employee_rate", "0.07", "decimal", eff22,
            "VERIFY: GRSIA employee 7% — Qatar Law 24/2002 and amendments"));
        list.Add(Rule(CountryCodes.Qatar, Jurisdictions.QatarMainland,
            "grsia.national_employer_rate", "0.14", "decimal", eff22,
            "VERIFY: GRSIA employer 14% — Qatar Law 24/2002 and amendments"));
        list.Add(Rule(CountryCodes.Qatar, Jurisdictions.QatarMainland,
            "qatarization.target_ratio", "0.20", "decimal", eff22,
            "VERIFY: Qatarization 20% directional — confirm sector targets with Ministry of Labor"));

        return list;
    }

    private static StatutoryRule Rule(
        string country, string jurisdiction, string key, string value,
        string dataType, DateTime effectiveFrom, string description) =>
        new()
        {
            Id           = Guid.NewGuid(),
            TenantId     = null,
            CountryCode  = country,
            Jurisdiction = jurisdiction,
            RuleKey      = key,
            RuleValue    = value,
            DataType     = dataType,
            Description  = description,
            EffectiveFrom = effectiveFrom,
            EffectiveTo  = null,
            CreatedAtUtc = Ts,
            CreatedBy    = null,
        };
}
