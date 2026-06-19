using Microsoft.EntityFrameworkCore;
using Zayra.Api.Data;
using Zayra.Api.Models;

namespace Zayra.Api.Infrastructure.Seed;

/// <summary>
/// Seeds the default GOSI contribution rules (system-wide, TenantId = Guid.Empty)
/// if they do not already exist in the database.
///
/// Baseline rates (Saudi Arabia, effective 2016-06-01, per GOSI Regulation):
///   Saudi Annuities:           Employee 9%,    Employer 9%
///   Saudi SANED:               Employee 0.75%, Employer 0.75%
///   Occupational Hazards:      Employer 2%     (applies to all classifications)
///
/// GCC rates mirror the Saudi baseline but are marked as pending legal confirmation.
/// NonSaudi nationals: Occupational Hazards employer 2% only.
///
/// NOTE: Rates and wage caps must be reviewed annually against current GOSI circulars.
/// </summary>
public static class GosiRuleSeeder
{
    private static readonly DateOnly EffectiveFrom = new(2016, 6, 1);

    public static async Task SeedDefaultsAsync(ZayraDbContext db, ILogger logger)
    {
        // IgnoreQueryFilters is intentional: seeder runs at startup outside any request context
        // (no IHttpContextAccessor, so the tenant filter is inactive). Must see Guid.Empty rows
        // to check whether platform defaults have already been seeded.
        var hasDefaults = await db.GosiContributionRules
            .IgnoreQueryFilters()
            .AnyAsync(r => r.TenantId == Guid.Empty);

        if (hasDefaults) return;

        var rules = BuildDefaultRules();
        db.GosiContributionRules.AddRange(rules);
        await db.SaveChangesAsync();

        logger.LogInformation("GOSI: seeded {Count} default contribution rules.", rules.Count);
    }

    private static List<GosiContributionRule> BuildDefaultRules()
    {
        var rules = new List<GosiContributionRule>();

        // ── Saudi nationals ────────────────────────────────────────────────────
        rules.Add(Rule(GosiClassifications.Saudi, GosiBranches.Annuities, GosiPayers.Employee, 9.00m,
            "GOSI Regulation 2016 — Saudi Annuities employee contribution"));
        rules.Add(Rule(GosiClassifications.Saudi, GosiBranches.Annuities, GosiPayers.Employer, 9.00m,
            "GOSI Regulation 2016 — Saudi Annuities employer contribution"));

        rules.Add(Rule(GosiClassifications.Saudi, GosiBranches.SANED, GosiPayers.Employee, 0.75m,
            "GOSI Regulation 2016 — SANED employee contribution"));
        rules.Add(Rule(GosiClassifications.Saudi, GosiBranches.SANED, GosiPayers.Employer, 0.75m,
            "GOSI Regulation 2016 — SANED employer contribution"));

        rules.Add(Rule(GosiClassifications.Saudi, GosiBranches.OccupationalHazards, GosiPayers.Employer, 2.00m,
            "GOSI Regulation 2016 — Occupational Hazards employer contribution"));

        // ── GCC nationals (pending bilateral treaty confirmation) ──────────────
        var gccNote = "GCC bilateral treaty baseline — PENDING LEGAL CONFIRMATION per applicable bilateral agreement. " +
                      "Rates mirror Saudi baseline until legally confirmed.";

        rules.Add(Rule(GosiClassifications.GCC, GosiBranches.Annuities, GosiPayers.Employee, 9.00m,
            gccNote, notes: "Pending legal confirmation"));
        rules.Add(Rule(GosiClassifications.GCC, GosiBranches.Annuities, GosiPayers.Employer, 9.00m,
            gccNote, notes: "Pending legal confirmation"));

        rules.Add(Rule(GosiClassifications.GCC, GosiBranches.SANED, GosiPayers.Employee, 0.75m,
            gccNote, notes: "Pending legal confirmation"));
        rules.Add(Rule(GosiClassifications.GCC, GosiBranches.SANED, GosiPayers.Employer, 0.75m,
            gccNote, notes: "Pending legal confirmation"));

        rules.Add(Rule(GosiClassifications.GCC, GosiBranches.OccupationalHazards, GosiPayers.Employer, 2.00m,
            gccNote, notes: "Pending legal confirmation"));

        // ── Non-Saudi nationals (Occupational Hazards employer only) ──────────
        rules.Add(Rule(GosiClassifications.NonSaudi, GosiBranches.OccupationalHazards, GosiPayers.Employer, 2.00m,
            "GOSI Regulation 2016 — Occupational Hazards employer contribution (all employees including expats)"));

        return rules;
    }

    private static GosiContributionRule Rule(
        string  classification,
        string  branch,
        string  payer,
        decimal rate,
        string  sourceReference,
        string? notes = null) =>
        new()
        {
            Id              = Guid.NewGuid(),
            TenantId        = Guid.Empty,
            CountryCode     = "SA",
            Classification  = classification,
            Branch          = branch,
            Payer           = payer,
            Rate            = rate,
            EffectiveFrom   = EffectiveFrom,
            EffectiveTo     = null,
            IsActive        = true,
            SourceReference = sourceReference,
            Notes           = notes,
            CreatedAtUtc    = DateTime.UtcNow,
        };
}
