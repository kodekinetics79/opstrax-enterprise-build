using Zayra.Api.Models;

namespace Zayra.Api.Infrastructure.Payroll;

/// <summary>
/// Validates whether an employee has all required data for GOSI contribution calculation.
///
/// Blocking issues prevent the payroll run from computing a GOSI deduction for the employee.
/// Warnings are informational (calculation proceeds but result should be reviewed).
/// </summary>
public static class GosiReadinessValidator
{
    /// <summary>
    /// Validates a single employee against their salary and payroll profile.
    /// </summary>
    public static GosiReadinessReport Validate(
        Employee               employee,
        decimal?               basicSalary,
        IReadOnlyList<GosiContributionRule> applicableRules)
    {
        var blocking = new List<GosiReadinessIssue>();
        var warnings = new List<GosiReadinessIssue>();

        // ── Blocking: GOSI reference number ─────────────────────────────────

        if (string.IsNullOrWhiteSpace(employee.GosiReference))
            blocking.Add(new GosiReadinessIssue(
                Code:        "MISSING_GOSI_REFERENCE",
                Message:     "Employee does not have a GOSI reference number. Register the employee with GOSI before processing deductions.",
                IsBlocking:  true));

        // ── Blocking: salary ─────────────────────────────────────────────────

        if (basicSalary is null || basicSalary <= 0)
            blocking.Add(new GosiReadinessIssue(
                Code:        "MISSING_BASIC_SALARY",
                Message:     "Employee has no basic salary. GOSI contribution cannot be calculated without a contributory wage.",
                IsBlocking:  true));

        // ── Warning: nationality / classification ───────────────────────────

        var classification = GosiCalculationService.DeriveClassification(employee.Nationality);

        if (string.IsNullOrWhiteSpace(employee.Nationality))
            warnings.Add(new GosiReadinessIssue(
                Code:        "MISSING_NATIONALITY",
                Message:     "Employee nationality is not set. Defaulting to NonSaudi classification — no Annuities or SANED deductions will be applied.",
                IsBlocking:  false));

        // ── Warning: GCC classification pending legal confirmation ──────────

        if (classification == GosiClassifications.GCC)
            warnings.Add(new GosiReadinessIssue(
                Code:        "GCC_RULES_PENDING_CONFIRMATION",
                Message:     "Employee is classified as GCC national. GCC GOSI rules mirror Saudi baseline but require legal confirmation per bilateral treaty — verify before submitting.",
                IsBlocking:  false));

        // ── Warning: no applicable rules found ──────────────────────────────

        if (blocking.Count == 0 && applicableRules.Count == 0)
            warnings.Add(new GosiReadinessIssue(
                Code:        "NO_APPLICABLE_RULES",
                Message:     $"No active GOSI contribution rules found for classification '{classification}'. Deduction will be zero.",
                IsBlocking:  false));

        return new GosiReadinessReport(
            EmployeeId:     employee.Id,
            EmployeeCode:   employee.EmployeeCode,
            Classification: classification,
            IsReady:        blocking.Count == 0,
            BlockingIssues: blocking,
            Warnings:       warnings);
    }
}

// ── Result types ─────────────────────────────────────────────────────────────

public record GosiReadinessReport(
    int                          EmployeeId,
    string                       EmployeeCode,
    string                       Classification,
    bool                         IsReady,
    IReadOnlyList<GosiReadinessIssue> BlockingIssues,
    IReadOnlyList<GosiReadinessIssue> Warnings
)
{
    public int BlockingCount => BlockingIssues.Count;
    public int WarningCount  => Warnings.Count;
}

public record GosiReadinessIssue(string Code, string Message, bool IsBlocking);
