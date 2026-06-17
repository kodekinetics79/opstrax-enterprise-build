using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Zayra.Api.Application.Common;
using Zayra.Api.Data;

namespace Zayra.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly ZayraDbContext _db;
    private readonly IDistributedCache _cache;
    private readonly IDataScopeService _scopeService;

    private static readonly DistributedCacheEntryOptions CacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60)
    };

    private static readonly string[] QiwaRequiredDocs = ["Iqama", "Work Permit", "National ID", "Passport"];

    public DashboardController(ZayraDbContext db, IDistributedCache cache, IDataScopeService scopeService)
    {
        _db = db;
        _cache = cache;
        _scopeService = scopeService;
    }

    // Single consolidated endpoint — replaces three separate calls (summary + trends + overview).
    // Cached per-tenant for 60 s to absorb repeat loads without hammering MySQL.
    [HttpGet("full")]
    public async Task<IActionResult> Full([FromQuery] int months = 6, CancellationToken cancellationToken = default)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null)
            return Ok(EmptyFull());

        var cacheKey = $"dashboard:full:{tenantId}:{months}";
        var cachedBytes = await _cache.GetAsync(cacheKey, cancellationToken);
        if (cachedBytes is not null)
            return Ok(JsonSerializer.Deserialize<DashboardFullDto>(cachedBytes));

        var result = await BuildFull(tenantId.Value, months, cancellationToken);
        await _cache.SetAsync(cacheKey, JsonSerializer.SerializeToUtf8Bytes(result), CacheOptions, cancellationToken);
        return Ok(result);
    }

    // ── Kept for backwards-compat with any other consumers ───────────────────

    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken cancellationToken)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Ok(new DashboardSummaryDto(0, 0, 0, 0, 0, 0m, 0));

        var cacheKey = $"dashboard:summary:{tenantId}";
        var cachedBytes = await _cache.GetAsync(cacheKey, cancellationToken);
        if (cachedBytes is not null)
            return Ok(JsonSerializer.Deserialize<DashboardSummaryDto>(cachedBytes));

        var result = await BuildSummary(tenantId.Value, cancellationToken);
        await _cache.SetAsync(cacheKey, JsonSerializer.SerializeToUtf8Bytes(result), CacheOptions, cancellationToken);
        return Ok(result);
    }

    [HttpGet("trends")]
    public async Task<IActionResult> Trends([FromQuery] int months = 6, CancellationToken cancellationToken = default)
    {
        var tenantId = this.GetTenantId();
        months = Math.Clamp(months, 1, 12);
        if (tenantId is null) return Ok(Array.Empty<DashboardTrendDto>());

        var cacheKey = $"dashboard:trends:{tenantId}:{months}";
        var cachedBytes = await _cache.GetAsync(cacheKey, cancellationToken);
        if (cachedBytes is not null)
            return Ok(JsonSerializer.Deserialize<List<DashboardTrendDto>>(cachedBytes));

        var result = await BuildTrends(tenantId.Value, months, cancellationToken);
        await _cache.SetAsync(cacheKey, JsonSerializer.SerializeToUtf8Bytes(result), CacheOptions, cancellationToken);
        return Ok(result);
    }

    [HttpGet("overview")]
    public async Task<IActionResult> Overview(CancellationToken cancellationToken)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null)
            return Ok(new DashboardOverviewDto(0, Array.Empty<ApprovalQueueItemDto>(), null,
                Array.Empty<NamedValueDto>(), Array.Empty<NamedValueDto>(),
                Array.Empty<NamedValueDto>(), Array.Empty<DashboardAlertDto>(), 0, 0));

        var cacheKey = $"dashboard:overview:{tenantId}";
        var cachedBytes = await _cache.GetAsync(cacheKey, cancellationToken);
        if (cachedBytes is not null)
            return Ok(JsonSerializer.Deserialize<DashboardOverviewDto>(cachedBytes));

        var result = await BuildOverview(tenantId.Value, cancellationToken);
        await _cache.SetAsync(cacheKey, JsonSerializer.SerializeToUtf8Bytes(result), CacheOptions, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Role-scoped operational KPIs. Admin/HR see tenant-wide data;
    /// Manager/Supervisor see only their team; Employee sees only themselves.
    /// Not cached because it must reflect the caller's scope, not a shared tenant key.
    /// </summary>
    [HttpGet("kpis")]
    public async Task<IActionResult> Kpis(CancellationToken ct)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Ok(EmptyKpis(false));

        var tid = tenantId.Value;
        var scope = await _scopeService.ResolveAsync(User, tid, ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        // Depending on scope, all counts are filtered to the allowed employee set.
        // unrestricted = Admin/HR: full tenant; restricted = Manager/Supervisor: team; Employee: own.
        IReadOnlyCollection<int>? scopeIds = scope.IsUnrestricted ? null : scope.AllowedEmployeeIds;

        // Pending leave requests in scope
        var leaveQ = _db.LeaveRequests.Where(x => x.TenantId == tid && (x.Status == "Submitted" || x.Status == "Pending"));
        if (scopeIds is not null) leaveQ = leaveQ.Where(x => scopeIds.Contains(x.EmployeeId));
        var pendingLeave = await leaveQ.CountAsync(ct);

        // Pending attendance corrections in scope
        var corrQ = _db.AttendanceRegularizationRequests.Where(x => x.TenantId == tid && x.Status == "Submitted");
        if (scopeIds is not null) corrQ = corrQ.Where(x => scopeIds.Contains(x.EmployeeId));
        var pendingCorrections = await corrQ.CountAsync(ct);

        // Attendance exceptions today (Late or Absent) in scope
        var exceptQ = _db.AttendanceDailyRecords.Where(x => x.TenantId == tid && x.WorkDate == today
            && (x.Status == "Late" || x.Status == "Absent" || x.LateMinutes > 0));
        if (scopeIds is not null) exceptQ = exceptQ.Where(x => scopeIds.Contains(x.EmployeeId));
        var attendanceExceptions = await exceptQ.CountAsync(ct);

        // Expiring documents (next 60 days) in scope — from EmployeeDocuments
        var expiringSoon = today.AddDays(60);
        var expiringQ = _db.EmployeeDocuments.Where(x => x.TenantId == tid && !x.IsDeleted
            && x.ExpiryDate != null && x.ExpiryDate > today && x.ExpiryDate <= expiringSoon);
        if (scopeIds is not null) expiringQ = expiringQ.Where(x => x.EmployeeId != null && scopeIds.Contains(x.EmployeeId.Value));
        var expiringDocuments = await expiringQ.CountAsync(ct);

        // Expired documents in scope
        var expiredQ = _db.EmployeeDocuments.Where(x => x.TenantId == tid && !x.IsDeleted
            && x.ExpiryDate != null && x.ExpiryDate < today);
        if (scopeIds is not null) expiredQ = expiredQ.Where(x => x.EmployeeId != null && scopeIds.Contains(x.EmployeeId.Value));
        var expiredDocuments = await expiredQ.CountAsync(ct);

        // Missing required documents: count employees who are missing at least one required doc type
        // Required types per Qiwa readiness: Iqama, Work Permit, National ID, Passport
        var empQ = _db.Employees.Where(x => x.TenantId == tid && !x.IsDeleted && x.Status == "Active");
        if (scopeIds is not null) empQ = empQ.Where(x => scopeIds.Contains(x.Id));
        var activeEmployeeIds = await empQ.Select(x => x.Id).ToListAsync(ct);

        int missingDocuments = 0;
        if (activeEmployeeIds.Count > 0)
        {
            // Fetch uploaded non-deleted doc types per employee in one query
            var uploadedDocs = await _db.EmployeeDocuments
                .Where(x => x.TenantId == tid && !x.IsDeleted && x.EmployeeId != null && activeEmployeeIds.Contains(x.EmployeeId!.Value))
                .Select(x => new { x.EmployeeId, x.DocumentType })
                .ToListAsync(ct);
            var byEmployee = uploadedDocs.GroupBy(x => x.EmployeeId!.Value)
                .ToDictionary(g => g.Key, g => g.Select(d => d.DocumentType).ToHashSet(StringComparer.OrdinalIgnoreCase));
            missingDocuments = activeEmployeeIds.Count(id =>
                !byEmployee.TryGetValue(id, out var docs) ||
                QiwaRequiredDocs.Any(req => !docs.Contains(req)));
        }

        // Qiwa feature flag
        var qiwaEnabled = await _db.TenantFeatureFlags.AnyAsync(x => x.TenantId == tid && x.FeatureKey == Zayra.Api.Models.FeatureKeys.QiwaIntegration && x.IsEnabled, ct);

        return Ok(new DashboardKpisDto(
            pendingLeave,
            pendingCorrections,
            attendanceExceptions,
            expiringDocuments,
            expiredDocuments,
            missingDocuments,
            qiwaEnabled));
    }

    // ── Private builders ─────────────────────────────────────────────────────

    private async Task<DashboardFullDto> BuildFull(Guid tenantId, int months, CancellationToken ct)
    {
        months = Math.Clamp(months, 1, 12);
        var summaryTask  = BuildSummary(tenantId, ct);
        var trendsTask   = BuildTrends(tenantId, months, ct);
        var overviewTask = BuildOverview(tenantId, ct);
        await Task.WhenAll(summaryTask, trendsTask, overviewTask);
        return new DashboardFullDto(summaryTask.Result, trendsTask.Result, overviewTask.Result);
    }

    private async Task<DashboardSummaryDto> BuildSummary(Guid tenantId, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        // Single query for employee counts instead of two separate CountAsync calls
        var empCounts = await _db.Employees
            .Where(e => e.TenantId == tenantId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Active = g.Count(e => e.Status == "Active"),
            })
            .FirstOrDefaultAsync(ct);

        // Single query for today's attendance buckets instead of three CountAsync calls
        var todayBuckets = await _db.AttendanceRecords
            .Where(a => a.TenantId == tenantId && a.WorkDate == today)
            .GroupBy(a => a.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var present = todayBuckets.Where(b => b.Status == "Present").Sum(b => b.Count);
        var onLeave = todayBuckets.Where(b => b.Status == "Leave" || b.Status == "On Leave").Sum(b => b.Count);
        var absent = todayBuckets.Where(b => b.Status == "Absent").Sum(b => b.Count);

        // Two remaining aggregation queries that can't be combined with the above
        var overtimeHours = await _db.AttendanceRecords
            .Where(a => a.TenantId == tenantId && a.WorkDate >= monthStart && a.WorkDate <= today)
            .SumAsync(a => (decimal?)a.OvertimeHours, ct) ?? 0m;

        var churnRisk = await _db.AttendanceRecords
            .Where(a => a.TenantId == tenantId && a.WorkDate >= today.AddDays(-30)
                && (a.Status == "Absent" || a.OvertimeHours >= 4))
            .Select(a => a.EmployeeId)
            .Distinct()
            .CountAsync(ct);

        return new DashboardSummaryDto(
            empCounts?.Total ?? 0,
            empCounts?.Active ?? 0,
            present,
            onLeave,
            absent,
            overtimeHours,
            churnRisk);
    }

    private async Task<IReadOnlyList<DashboardTrendDto>> BuildTrends(Guid tenantId, int months, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var firstMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(-(months - 1));

        // Push GROUP BY to MySQL instead of pulling every row into app memory
        var grouped = await _db.AttendanceRecords
            .Where(a => a.TenantId == tenantId && a.WorkDate >= firstMonth && a.WorkDate <= today)
            .GroupBy(a => new { a.WorkDate.Year, a.WorkDate.Month })
            .Select(g => new
            {
                g.Key.Year,
                g.Key.Month,
                Total = g.Count(),
                PresentCount = g.Count(a => a.Status == "Present"),
                OvertimeSum = g.Sum(a => (decimal?)a.OvertimeHours) ?? 0m,
            })
            .ToListAsync(ct);

        return Enumerable.Range(0, months)
            .Select(offset =>
            {
                var month = firstMonth.AddMonths(offset);
                var row = grouped.FirstOrDefault(r => r.Year == month.Year && r.Month == month.Month);
                var attendanceRate = row is { Total: > 0 }
                    ? Math.Round(row.PresentCount * 100m / row.Total, 1)
                    : 0m;
                return new DashboardTrendDto(month.ToString("MMM"), attendanceRate, row?.OvertimeSum ?? 0m);
            })
            .ToList();
    }

    private async Task<DashboardOverviewDto> BuildOverview(Guid tenantId, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        // Run all independent overview queries concurrently
        var pendingApprovalsTask = _db.ApprovalRequests
            .CountAsync(a => a.TenantId == tenantId && a.Status == "Pending", ct);

        var approvalQueueTask = _db.ApprovalRequests
            .Where(a => a.TenantId == tenantId && a.Status == "Pending")
            .OrderByDescending(a => a.CreatedAtUtc)
            .Take(6)
            .Select(a => new ApprovalQueueItemDto(
                a.Id,
                string.IsNullOrWhiteSpace(a.Title) ? a.EntityName : a.Title,
                a.EntityName,
                a.CreatedAtUtc))
            .ToListAsync(ct);

        var latestRunTask = _db.PayrollRuns
            .Where(p => p.TenantId == tenantId)
            .OrderByDescending(p => p.Year).ThenByDescending(p => p.Month)
            .FirstOrDefaultAsync(ct);

        var workforceMixTask = _db.Employees
            .Where(e => e.TenantId == tenantId && e.Status == "Active")
            .GroupBy(e => e.EmploymentType)
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToListAsync(ct);

        var headcountTask = _db.Employees
            .Where(e => e.TenantId == tenantId && e.Status == "Active")
            .GroupBy(e => e.Department)
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(6)
            .ToListAsync(ct);

        var expiringTask = _db.EmployeeComplianceRecords
            .Where(c => c.TenantId == tenantId && !c.IsDeleted
                && c.ExpiryDate != null && c.ExpiryDate <= today.AddDays(60))
            .OrderBy(c => c.ExpiryDate)
            .Take(8)
            .Select(c => new { c.FieldLabel, c.ExpiryDate })
            .ToListAsync(ct);

        var openLeaveTask = _db.LeaveRequests
            .CountAsync(l => l.TenantId == tenantId
                && l.Status != "Approved" && l.Status != "Rejected"
                && l.Status != "Cancelled" && l.Status != "Withdrawn" && l.Status != "Draft", ct);

        var newJoinersTask = _db.Employees
            .CountAsync(e => e.TenantId == tenantId
                && e.JoiningDate >= monthStart.ToDateTime(TimeOnly.MinValue), ct);

        await Task.WhenAll(
            pendingApprovalsTask, approvalQueueTask, latestRunTask,
            workforceMixTask, headcountTask, expiringTask,
            openLeaveTask, newJoinersTask);

        // Payroll slip breakdown — depends on latestRun result
        PayrollSummaryDto? payrollSummary = null;
        IReadOnlyList<NamedValueDto> payrollByEntity = Array.Empty<NamedValueDto>();
        var latestRun = await latestRunTask;
        if (latestRun is not null)
        {
            payrollSummary = new PayrollSummaryDto(
                new DateOnly(latestRun.Year, latestRun.Month, 1).ToString("MMM yyyy"),
                latestRun.TotalGrossSalary,
                latestRun.TotalNetSalary,
                latestRun.TotalDeductions,
                latestRun.EmployeeCount,
                latestRun.Status);

            var rawPayroll = await _db.PayrollSlips
                .Where(s => s.TenantId == tenantId && s.RunId == latestRun.Id)
                .GroupBy(s => s.Department)
                .Select(g => new { Key = g.Key, Total = g.Sum(x => x.NetSalary) })
                .OrderByDescending(x => x.Total)
                .Take(8)
                .ToListAsync(ct);

            payrollByEntity = rawPayroll
                .Select(x => new NamedValueDto(string.IsNullOrWhiteSpace(x.Key) ? "Unspecified" : x.Key, x.Total))
                .ToList();
        }

        var workforceMix = (await workforceMixTask)
            .Select(x => new NamedValueDto(string.IsNullOrWhiteSpace(x.Key) ? "Unspecified" : x.Key, x.Count))
            .ToList();

        var headcount = (await headcountTask)
            .Select(x => new NamedValueDto(string.IsNullOrWhiteSpace(x.Key) ? "Unassigned" : x.Key, x.Count))
            .ToList();

        var alerts = (await expiringTask).Select(c =>
        {
            var expiry = c.ExpiryDate!.Value;
            var severity = expiry < today ? "Critical" : expiry <= today.AddDays(30) ? "Warning" : "Info";
            var label = string.IsNullOrWhiteSpace(c.FieldLabel) ? "Document" : c.FieldLabel;
            var title = expiry < today ? $"{label} expired {expiry:dd MMM}" : $"{label} expires {expiry:dd MMM}";
            return new DashboardAlertDto(title, severity);
        }).ToList();

        return new DashboardOverviewDto(
            await pendingApprovalsTask,
            await approvalQueueTask,
            payrollSummary,
            payrollByEntity,
            workforceMix,
            headcount,
            alerts,
            await openLeaveTask,
            await newJoinersTask);
    }

    private static DashboardFullDto EmptyFull() => new(
        new DashboardSummaryDto(0, 0, 0, 0, 0, 0m, 0),
        Array.Empty<DashboardTrendDto>(),
        new DashboardOverviewDto(0, Array.Empty<ApprovalQueueItemDto>(), null,
            Array.Empty<NamedValueDto>(), Array.Empty<NamedValueDto>(),
            Array.Empty<NamedValueDto>(), Array.Empty<DashboardAlertDto>(), 0, 0));

    private static DashboardKpisDto EmptyKpis(bool qiwaEnabled) =>
        new(0, 0, 0, 0, 0, 0, qiwaEnabled);
}

// ── DTOs ─────────────────────────────────────────────────────────────────────

public record DashboardFullDto(
    DashboardSummaryDto Summary,
    IReadOnlyList<DashboardTrendDto> Trends,
    DashboardOverviewDto Overview);

public record DashboardSummaryDto(
    int TotalEmployees,
    int ActiveEmployees,
    int PresentToday,
    int OnLeave,
    int Absent,
    decimal OvertimeHours,
    int ChurnRisk);

public record DashboardTrendDto(
    string Month,
    decimal AttendanceRate,
    decimal OvertimeHours);

public record DashboardOverviewDto(
    int PendingApprovals,
    IReadOnlyList<ApprovalQueueItemDto> ApprovalQueue,
    PayrollSummaryDto? PayrollSummary,
    IReadOnlyList<NamedValueDto> PayrollByEntity,
    IReadOnlyList<NamedValueDto> WorkforceMix,
    IReadOnlyList<NamedValueDto> HeadcountByDepartment,
    IReadOnlyList<DashboardAlertDto> Alerts,
    int OpenLeaveRequests,
    int NewJoinersThisMonth);

public record ApprovalQueueItemDto(Guid Id, string Title, string Module, DateTime CreatedAtUtc);

public record PayrollSummaryDto(
    string PeriodLabel,
    decimal TotalGross,
    decimal TotalNet,
    decimal TotalDeductions,
    int EmployeeCount,
    string Status);

public record NamedValueDto(string Name, decimal Value);

public record DashboardAlertDto(string Title, string Severity);

public record DashboardKpisDto(
    int PendingLeaveRequests,
    int PendingAttendanceCorrections,
    int AttendanceExceptions,
    int ExpiringDocuments,
    int ExpiredDocuments,
    int MissingDocuments,
    bool QiwaEnabled);
