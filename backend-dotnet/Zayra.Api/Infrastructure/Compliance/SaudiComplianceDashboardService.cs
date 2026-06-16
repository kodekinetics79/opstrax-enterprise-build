using Microsoft.EntityFrameworkCore;
using Zayra.Api.Data;
using Zayra.Api.Infrastructure.Payroll;
using Zayra.Api.Infrastructure.Qiwa;
using Zayra.Api.Models;

namespace Zayra.Api.Infrastructure.Compliance;

/// <summary>
/// Computes the Saudi regulatory compliance dashboard (QIWA + WPS + GOSI) for a
/// tenant.  All figures are derived from live DB records — nothing is hard-coded.
/// All queries are tenant-scoped.
/// </summary>
public sealed class SaudiComplianceDashboardService
{
    private readonly ZayraDbContext _db;

    public SaudiComplianceDashboardService(ZayraDbContext db) => _db = db;

    public async Task<SaudiComplianceDashboard> BuildAsync(Guid tenantId, CancellationToken ct)
    {
        var qiwa = await BuildQiwaAsync(tenantId, ct);
        var wps  = await BuildWpsAsync(tenantId, ct);
        var gosi = await BuildGosiAsync(tenantId, ct);

        var actionItems = new List<ComplianceActionItem>();

        if (qiwa.BlockedFromSync > 0)
            actionItems.Add(new("critical", "QIWA", $"{qiwa.BlockedFromSync} employees cannot be synced to QIWA (missing required fields)."));
        if (qiwa.ConnectionStatus is "NotConfigured")
            actionItems.Add(new("warning", "QIWA", "QIWA connection is not configured."));
        else if (qiwa.ConnectionStatus is "Error" or "ApiError" or "ConfigurationError")
            actionItems.Add(new("critical", "QIWA", "QIWA connection is in an error state."));
        if (qiwa.FailedSyncCount > 0)
            actionItems.Add(new("warning", "QIWA", $"{qiwa.FailedSyncCount} QIWA sync attempts failed or are dead-lettered."));

        foreach (var issue in wps.BlockingIssues)
            actionItems.Add(new("warning", "WPS", issue));

        foreach (var w in gosi.Warnings)
            actionItems.Add(new("warning", "GOSI", w));

        // Critical first.
        actionItems = actionItems
            .OrderBy(a => a.Severity == "critical" ? 0 : a.Severity == "warning" ? 1 : 2)
            .ToList();

        return new SaudiComplianceDashboard(qiwa, wps, gosi, actionItems);
    }

    private async Task<QiwaDashboardSection> BuildQiwaAsync(Guid tenantId, CancellationToken ct)
    {
        var connection = await _db.QiwaTenantConnections.AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId, ct);

        var employees = await _db.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId && !e.IsDeleted && e.Status == "Active")
            .ToListAsync(ct);

        var blocked = new List<BlockedEmployee>();
        foreach (var e in employees)
        {
            var missing = QiwaIntegrationService.MissingQiwaFields(e);
            if (missing.Count > 0)
                blocked.Add(new BlockedEmployee(e.Id, e.EmployeeCode, e.FullName, missing));
        }

        var total = employees.Count;
        var ready = total - blocked.Count;
        var percent = total == 0 ? 0 : Math.Round(ready * 100.0 / total, 1);

        var failedCount = await _db.QiwaSyncLogs
            .CountAsync(l => l.TenantId == tenantId &&
                             (l.Status == QiwaSyncLogStatuses.Failed || l.Status == QiwaSyncLogStatuses.DeadLetter), ct);

        var lastSuccess = await _db.QiwaSyncLogs
            .Where(l => l.TenantId == tenantId && l.Status == QiwaSyncLogStatuses.Success)
            .OrderByDescending(l => l.CompletedAtUtc)
            .Select(l => l.CompletedAtUtc)
            .FirstOrDefaultAsync(ct);

        return new QiwaDashboardSection(
            connection?.Status ?? "NotConfigured",
            connection?.LastConnectedAtUtc,
            total, ready, blocked.Count, percent,
            failedCount, lastSuccess, blocked);
    }

    private async Task<WpsDashboardSection> BuildWpsAsync(Guid tenantId, CancellationToken ct)
    {
        var lastRun = await _db.PayrollRuns.AsNoTracking()
            .Where(r => r.TenantId == tenantId)
            .OrderByDescending(r => r.Year).ThenByDescending(r => r.Month)
            .FirstOrDefaultAsync(ct);

        var issues = new List<string>();
        string? lastRunStatus = lastRun?.Status;
        string? lastRunPeriod = lastRun is null ? null : $"{lastRun.Year}-{lastRun.Month:D2}";

        // Pending approvals = runs not yet Locked/Paid.
        var pendingApprovals = await _db.PayrollRuns
            .CountAsync(r => r.TenantId == tenantId && r.Status != "Locked" && r.Status != "Paid" && r.Status != "Draft", ct);

        if (lastRun is not null)
        {
            if (lastRun.Status != "Locked" && lastRun.Status != "Paid")
                issues.Add($"Latest payroll run ({lastRunPeriod}) is not yet approved/locked.");

            // Count active employees missing a valid IBAN in their payroll profile.
            var profiles = await _db.EmployeePayrollProfiles.AsNoTracking()
                .Where(p => p.TenantId == tenantId && !p.IsDeleted)
                .ToListAsync(ct);
            var missingIban = profiles.Count(p => !IbanValidator.IsValid(p.Iban));
            if (missingIban > 0)
                issues.Add($"{missingIban} employees missing or with invalid IBAN for WPS payment.");
        }

        return new WpsDashboardSection(lastRunStatus, lastRunPeriod, pendingApprovals, issues);
    }

    private async Task<GosiDashboardSection> BuildGosiAsync(Guid tenantId, CancellationToken ct)
    {
        var employees = await _db.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId && !e.IsDeleted && e.Status == "Active")
            .Select(e => new { e.GosiReference, e.Nationality })
            .ToListAsync(ct);

        var company = await _db.Companies.AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId, ct);

        var missingRef = employees.Count(e => string.IsNullOrWhiteSpace(e.GosiReference));
        // GosiEmployerId lives on the Company; absence affects all employees.
        var missingEmployerId = string.IsNullOrWhiteSpace(company?.GosiEmployerId) ? employees.Count : 0;

        var warnings = new List<string>();
        if (missingRef > 0) warnings.Add($"{missingRef} employees missing GOSI reference number.");
        if (string.IsNullOrWhiteSpace(company?.GosiEmployerId))
            warnings.Add("Company GOSI employer ID is not set.");

        return new GosiDashboardSection(missingRef, missingEmployerId, warnings);
    }
}

// ── DTOs ──────────────────────────────────────────────────────────────────────

public record SaudiComplianceDashboard(
    QiwaDashboardSection Qiwa,
    WpsDashboardSection Wps,
    GosiDashboardSection Gosi,
    IReadOnlyList<ComplianceActionItem> ActionItems);

public record QiwaDashboardSection(
    string ConnectionStatus,
    DateTime? LastConnectedAt,
    int TotalEmployees,
    int ReadyForSync,
    int BlockedFromSync,
    double ReadinessPercent,
    int FailedSyncCount,
    DateTime? LastSuccessfulSync,
    IReadOnlyList<BlockedEmployee> BlockedEmployees);

public record BlockedEmployee(int EmployeeId, string EmployeeCode, string FullName, IReadOnlyList<string> MissingFields);

public record WpsDashboardSection(
    string? LastRunStatus,
    string? LastRunPeriod,
    int PendingApprovals,
    IReadOnlyList<string> BlockingIssues);

public record GosiDashboardSection(
    int EmployeesMissingGosiRef,
    int EmployeesMissingGosiEmployerId,
    IReadOnlyList<string> Warnings);

public record ComplianceActionItem(string Severity, string Area, string Message);
