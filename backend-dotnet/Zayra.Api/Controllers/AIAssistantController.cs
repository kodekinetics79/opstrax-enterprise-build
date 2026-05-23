using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using Zayra.Api.Application.Common;
using Zayra.Api.Data;
using Zayra.Api.Models;

namespace Zayra.Api.Controllers;

[ApiController]
[Route("api/ai")]
[Authorize]
public class AIAssistantController : ControllerBase
{
    private readonly ZayraDbContext _db;

    public AIAssistantController(ZayraDbContext db) => _db = db;

    // ── AI HR Query (live DB context, advisory only) ─────────────────────────

    [HttpPost("query")]
    public async Task<IActionResult> Query([FromBody] AIQueryRequest req, CancellationToken ct)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var userId = this.GetUserId();
        var userRoles = User.Claims
            .Where(c => c.Type == System.Security.Claims.ClaimTypes.Role)
            .Select(c => c.Value)
            .ToList();
        var userRole = string.Join(",", userRoles);

        var sw = Stopwatch.StartNew();
        var intent = ClassifyIntent(req.Query);

        // RBAC: block sensitive queries for non-authorized roles
        var isSensitiveIntent = intent is "payroll_details" or "salary_details" or "employee_risk";
        var isHROrAdmin = userRoles.Any(r => r is "Admin" or "HR Manager" or "HR Officer" or "Payroll Manager");

        if (isSensitiveIntent && !isHROrAdmin)
        {
            var blockedLog = new AIHRQueryLog
            {
                TenantId = tenantId.Value,
                UserId = userId ?? Guid.Empty,
                EmployeeId = req.EmployeeId,
                UserRole = userRole,
                Query = req.Query,
                Response = string.Empty,
                IntentClassified = intent,
                WasBlocked = true,
                BlockedReason = "Insufficient permissions to access this information.",
                IsAdvisoryLabelShown = true
            };
            _db.AIHRQueryLogs.Add(blockedLog);
            await _db.SaveChangesAsync(ct);
            return Ok(new AIQueryResponse(
                "I'm unable to provide that information based on your current access level.",
                intent,
                true,
                "Insufficient permissions to access this information.",
                0,
                true,
                []
            ));
        }

        // Build live context from database
        var context = await BuildContextAsync(tenantId.Value, intent, req.EmployeeId, ct);
        var response = GenerateAdvisoryResponse(intent, req.Query, context);

        sw.Stop();

        var log = new AIHRQueryLog
        {
            TenantId = tenantId.Value,
            UserId = userId ?? Guid.Empty,
            EmployeeId = req.EmployeeId,
            UserRole = userRole,
            Query = req.Query,
            Response = response.Answer,
            IntentClassified = intent,
            WasBlocked = false,
            TokensUsed = (int)(req.Query.Length / 4 + response.Answer.Length / 4),
            ResponseTimeMs = (int)sw.ElapsedMilliseconds,
            IsAdvisoryLabelShown = true
        };
        _db.AIHRQueryLogs.Add(log);
        await _db.SaveChangesAsync(ct);

        return Ok(response with { IsAdvisory = true });
    }

    // ── AI Insights ──────────────────────────────────────────────────────────

    [HttpGet("insights")]
    [Authorize(Roles = "Admin,HR Manager,HR Officer")]
    public async Task<IActionResult> ListInsights(
        [FromQuery] string? module,
        [FromQuery] string? severity,
        [FromQuery] bool? acknowledged,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var query = _db.AIInsights.Where(i => i.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(module)) query = query.Where(i => i.Module == module);
        if (!string.IsNullOrWhiteSpace(severity)) query = query.Where(i => i.Severity == severity);
        if (acknowledged.HasValue) query = query.Where(i => i.IsAcknowledged == acknowledged.Value);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(i => i.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return Ok(new { items, total, page, pageSize });
    }

    [HttpPost("insights/{id:guid}/acknowledge")]
    [Authorize(Roles = "Admin,HR Manager,HR Officer")]
    public async Task<IActionResult> AcknowledgeInsight(Guid id, CancellationToken ct)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var insight = await _db.AIInsights
            .FirstOrDefaultAsync(i => i.Id == id && i.TenantId == tenantId, ct);
        if (insight is null) return NotFound();

        insight.IsAcknowledged = true;
        insight.AcknowledgedBy = this.GetUserId();
        insight.AcknowledgedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(insight);
    }

    // ── Employee Risk Scores ─────────────────────────────────────────────────

    [HttpGet("risk-scores")]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<IActionResult> RiskScores(
        [FromQuery] string? riskLevel,
        [FromQuery] string? department,
        CancellationToken ct = default)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var query = _db.EmployeeRiskScores.Where(r => r.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(riskLevel)) query = query.Where(r => r.OverallRiskLevel == riskLevel);
        if (!string.IsNullOrWhiteSpace(department)) query = query.Where(r => r.DepartmentName == department);

        var items = await query
            .OrderByDescending(r => r.ChurnRiskScore)
            .Take(100)
            .ToListAsync(ct);

        return Ok(items);
    }

    [HttpPost("risk-scores/compute")]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<IActionResult> ComputeRiskScores(CancellationToken ct)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var ninetyDaysAgo = today.AddDays(-90);

        var employees = await _db.Employees
            .Where(e => e.TenantId == tenantId && !e.IsDeleted && e.Status == "Active")
            .ToListAsync(ct);

        var results = new List<EmployeeRiskScore>();

        foreach (var emp in employees)
        {
            var absenceCount = await _db.AbsenceRecords
                .CountAsync(a => a.TenantId == tenantId && a.EmployeeId == emp.Id
                    && a.AbsenceDate >= ninetyDaysAgo, ct);

            var overtimeMinutes = await _db.OvertimeRequests
                .Where(o => o.TenantId == tenantId && o.EmployeeId == emp.Id
                    && o.Status == "Approved"
                    && o.WorkDate >= ninetyDaysAgo)
                .SumAsync(o => (decimal?)o.ApprovedMinutes ?? 0, ct);
            var overtimeHours = overtimeMinutes / 60m;

            var yearsOfService = (DateTime.UtcNow - emp.JoiningDate).TotalDays / 365.0;
            // Heuristic scoring (advisory only, not automated decisions)
            var churnRisk = Math.Min(100m, absenceCount * 8m + (yearsOfService < 1 ? 30m : 0m));
            var burnoutRisk = Math.Min(100m, overtimeHours * 1.2m);
            var combinedRisk = (churnRisk + burnoutRisk) / 2m;
            var overallLevel = combinedRisk switch
            {
                > 70m => "Critical",
                > 50m => "High",
                > 30m => "Medium",
                _ => "Low"
            };

            var existing = await _db.EmployeeRiskScores
                .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.EmployeeId == emp.Id, ct);

            if (existing is null)
            {
                existing = new EmployeeRiskScore { TenantId = tenantId.Value, EmployeeId = emp.Id };
                _db.EmployeeRiskScores.Add(existing);
            }

            existing.EmployeeName = emp.FullName;
            existing.DepartmentName = emp.Department ?? string.Empty;
            existing.ChurnRiskScore = churnRisk;
            existing.BurnoutRiskScore = burnoutRisk;
            existing.PerformanceDeclineScore = 0;
            existing.OverallRiskLevel = overallLevel;
            existing.Recommendations = GenerateRiskRecommendation(overallLevel, absenceCount, overtimeHours);
            existing.ComputedAtUtc = DateTime.UtcNow;
            existing.IsAdvisoryOnly = true;

            results.Add(existing);
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new { computed = results.Count, message = "Risk scores computed. All results are advisory only." });
    }

    // ── Query History ────────────────────────────────────────────────────────

    [HttpGet("query-history")]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<IActionResult> QueryHistory(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var total = await _db.AIHRQueryLogs.CountAsync(l => l.TenantId == tenantId, ct);
        var items = await _db.AIHRQueryLogs
            .Where(l => l.TenantId == tenantId)
            .OrderByDescending(l => l.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return Ok(new { items, total, page, pageSize });
    }

    // ── Payroll AI Validation ────────────────────────────────────────────────

    [HttpGet("payroll-validation/{payrollRunId:guid}")]
    [Authorize(Roles = "Admin,HR Manager,Payroll Manager")]
    public async Task<IActionResult> PayrollValidation(Guid payrollRunId, CancellationToken ct)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var results = await _db.PayrollAIValidationResults
            .Where(r => r.TenantId == tenantId && r.PayrollRunId == payrollRunId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync(ct);

        return Ok(results);
    }

    [HttpPost("payroll-validation/{payrollRunId:guid}/run")]
    [Authorize(Roles = "Admin,HR Manager,Payroll Manager")]
    public async Task<IActionResult> RunPayrollValidation(Guid payrollRunId, CancellationToken ct)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var runExists = await _db.PayrollRuns
            .AnyAsync(r => r.Id == payrollRunId && r.TenantId == tenantId, ct);
        if (!runExists) return NotFound();

        var employees = await _db.PayrollRunEmployees
            .Where(e => e.TenantId == tenantId && e.PayrollRunId == payrollRunId)
            .Join(_db.Employees.Where(emp => emp.TenantId == tenantId),
                pre => pre.EmployeeId, emp => emp.Id,
                (pre, emp) => new { pre.EmployeeId, EmployeeName = emp.FullName, pre.GrossEarnings, pre.TotalDeductions, pre.NetPay })
            .ToListAsync(ct);

        // Remove old results for this run
        var old = await _db.PayrollAIValidationResults
            .Where(r => r.TenantId == tenantId && r.PayrollRunId == payrollRunId)
            .ToListAsync(ct);
        _db.PayrollAIValidationResults.RemoveRange(old);

        var findings = new List<PayrollAIValidationResult>();

        foreach (var emp in employees)
        {
            if (emp.NetPay <= 0)
            {
                findings.Add(new PayrollAIValidationResult
                {
                    TenantId = tenantId.Value,
                    PayrollRunId = payrollRunId,
                    EmployeeId = emp.EmployeeId,
                    EmployeeName = emp.EmployeeName,
                    ValidationType = "NegativeNetPay",
                    Severity = "Critical",
                    Message = $"Employee {emp.EmployeeName} has zero or negative net pay ({emp.NetPay:C}). Review deductions.",
                    IsAdvisoryOnly = true
                });
            }

            if (emp.GrossEarnings > 0 && emp.TotalDeductions > emp.GrossEarnings * 0.5m)
            {
                findings.Add(new PayrollAIValidationResult
                {
                    TenantId = tenantId.Value,
                    PayrollRunId = payrollRunId,
                    EmployeeId = emp.EmployeeId,
                    EmployeeName = emp.EmployeeName,
                    ValidationType = "ExcessiveDeductions",
                    Severity = "Warning",
                    Message = $"Deductions ({emp.TotalDeductions:C}) exceed 50% of gross earnings ({emp.GrossEarnings:C}) for {emp.EmployeeName}.",
                    IsAdvisoryOnly = true
                });
            }
        }

        _db.PayrollAIValidationResults.AddRange(findings);
        await _db.SaveChangesAsync(ct);

        return Ok(new
        {
            payrollRunId,
            totalEmployees = employees.Count,
            findings = findings.Count,
            critical = findings.Count(f => f.Severity == "Critical"),
            warnings = findings.Count(f => f.Severity == "Warning"),
            message = "Validation complete. All findings are advisory only — no payroll changes were made automatically.",
            results = findings
        });
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static string ClassifyIntent(string query)
    {
        var q = query.ToLowerInvariant();
        if (q.Contains("headcount") || q.Contains("how many employee")) return "headcount";
        if (q.Contains("on leave") || q.Contains("absent")) return "leave_status";
        if (q.Contains("leave balance")) return "leave_balance";
        if (q.Contains("pending approval")) return "pending_approvals";
        if (q.Contains("salary") || q.Contains("payroll") || q.Contains("pay slip")) return "payroll_details";
        if (q.Contains("risk") || q.Contains("churn") || q.Contains("burnout")) return "employee_risk";
        if (q.Contains("department")) return "department_info";
        if (q.Contains("overtime")) return "overtime_summary";
        if (q.Contains("holiday") || q.Contains("public holiday")) return "holiday_info";
        return "general";
    }

    private async Task<Dictionary<string, object>> BuildContextAsync(
        Guid tenantId, string intent, int? employeeId, CancellationToken ct)
    {
        var context = new Dictionary<string, object>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        switch (intent)
        {
            case "headcount":
                context["totalActive"] = await _db.Employees
                    .CountAsync(e => e.TenantId == tenantId && !e.IsDeleted && e.Status == "Active", ct);
                context["byDepartment"] = (await _db.Employees
                    .Where(e => e.TenantId == tenantId && !e.IsDeleted && e.Status == "Active")
                    .GroupBy(e => e.Department)
                    .Select(g => new { Department = g.Key, Count = g.Count() })
                    .ToListAsync(ct))
                    .Cast<object>().ToList();
                break;

            case "leave_status":
                context["onLeaveToday"] = await _db.LeaveRequests
                    .CountAsync(r => r.TenantId == tenantId && r.Status == "Approved"
                        && r.StartDate <= today && r.EndDate >= today, ct);
                break;

            case "pending_approvals":
                var statuses = new[] { "Submitted", "PendingManagerApproval", "PendingHRApproval" };
                context["pendingLeaveCount"] = await _db.LeaveRequests
                    .CountAsync(r => r.TenantId == tenantId && statuses.Contains(r.Status), ct);
                context["pendingOvertimeCount"] = await _db.OvertimeRequests
                    .CountAsync(r => r.TenantId == tenantId && r.Status == "PendingApproval", ct);
                break;

            case "leave_balance" when employeeId.HasValue:
                context["balances"] = await _db.EmployeeLeaveBalances
                    .Where(b => b.TenantId == tenantId && b.EmployeeId == employeeId.Value
                        && b.Year == DateTime.UtcNow.Year)
                    .Select(b => new { b.LeaveTypeName, b.Available, b.Used })
                    .ToListAsync(ct);
                break;

            case "department_info":
                context["departments"] = await _db.Departments
                    .Where(d => d.TenantId == tenantId && !d.IsDeleted)
                    .Select(d => new { Name = d.NameEn, d.Code })
                    .ToListAsync(ct);
                break;

            case "holiday_info":
                var nextMonth = today.AddMonths(1);
                context["upcomingHolidays"] = await _db.PublicHolidays
                    .Where(h => h.TenantId == tenantId && h.Date >= today && h.Date <= nextMonth)
                    .OrderBy(h => h.Date)
                    .Select(h => new { h.NameEn, h.Date })
                    .ToListAsync(ct);
                break;
        }

        return context;
    }

    private static AIQueryResponse GenerateAdvisoryResponse(
        string intent, string query, Dictionary<string, object> context)
    {
        var answer = intent switch
        {
            "headcount" => context.TryGetValue("totalActive", out var total)
                ? $"There are currently {total} active employees in your organisation."
                : "I couldn't retrieve headcount data at this time.",
            "leave_status" => context.TryGetValue("onLeaveToday", out var count)
                ? $"There are {count} employees on approved leave today."
                : "I couldn't retrieve leave data at this time.",
            "pending_approvals" => context.TryGetValue("pendingLeaveCount", out var lc)
                ? $"There are {lc} pending leave requests and {(context.TryGetValue("pendingOvertimeCount", out var oc) ? oc : 0)} pending overtime requests awaiting approval."
                : "I couldn't retrieve pending approval data.",
            "leave_balance" => context.TryGetValue("balances", out var b)
                ? $"Here are the leave balances for the requested employee: {System.Text.Json.JsonSerializer.Serialize(b)}"
                : "I couldn't retrieve leave balance data.",
            "department_info" => context.TryGetValue("departments", out var d)
                ? $"Your organisation has departments: {string.Join(", ", ((System.Collections.IEnumerable)d).Cast<dynamic>().Select(x => x.Name))}."
                : "I couldn't retrieve department data.",
            "holiday_info" => context.TryGetValue("upcomingHolidays", out var h)
                ? $"Upcoming public holidays: {System.Text.Json.JsonSerializer.Serialize(h)}"
                : "No upcoming holidays found in the next month.",
            "payroll_details" => "Payroll details are available to authorised payroll and HR roles only.",
            "employee_risk" => "Employee risk insights are available to HR leadership roles only.",
            _ => "I'm your HR assistant. I can help with headcount, leave status, pending approvals, balances, and department information. Please rephrase your question."
        };

        return new AIQueryResponse(answer, intent, false, string.Empty, 0, true, []);
    }

    private static string GenerateRiskRecommendation(string level, int absenceCount, decimal overtimeHours) =>
        level switch
        {
            "Critical" => $"Immediate manager check-in recommended. Employee shows {absenceCount} absences and {overtimeHours:F0}h overtime in past 90 days.",
            "High" => $"Schedule a wellbeing conversation. {absenceCount} absences and {overtimeHours:F0}h overtime detected.",
            "Medium" => "Monitor attendance and workload patterns for the next 30 days.",
            _ => "Employee engagement appears stable. Continue regular check-ins."
        };
}

public record AIQueryRequest(string Query, int? EmployeeId);
public record AIQueryResponse(
    string Answer,
    string Intent,
    bool WasBlocked,
    string BlockedReason,
    int TokensUsed,
    bool IsAdvisory,
    List<string> Suggestions);
