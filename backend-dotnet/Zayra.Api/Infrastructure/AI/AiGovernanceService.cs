namespace Zayra.Api.Infrastructure.AI;

using Zayra.Api.Application.AI;

public sealed class AiGovernanceService : IAiGovernanceService
{
    public AiGovernanceDecision Evaluate(string query, IReadOnlyCollection<string> roles)
    {
        var intent = ClassifyIntent(query);
        var module = GetModule(intent);
        var sensitive = IsSensitiveIntent(intent);
        var humanReviewRequired = sensitive;

        if (!sensitive)
        {
            return new AiGovernanceDecision(intent, module, false, true, humanReviewRequired, null);
        }

        var allowed = IsAllowedForSensitiveIntent(intent, roles);
        return new AiGovernanceDecision(
            intent,
            module,
            true,
            allowed,
            humanReviewRequired,
            allowed ? null : "Insufficient permissions to access this information.");
    }

    private static string ClassifyIntent(string query)
    {
        var q = (query ?? string.Empty).ToLowerInvariant();
        if (q.Contains("headcount") || q.Contains("how many employee")) return "headcount";
        if (q.Contains("how many employees") || q.Contains("active employees")) return "headcount";
        if (q.Contains("on leave") || q.Contains("absent")) return "leave_status";
        if (q.Contains("leave balance")) return "leave_balance";
        if (q.Contains("pending approval")) return "pending_approvals";
        if (q.Contains("salary") || q.Contains("payroll") || q.Contains("pay slip") || q.Contains("payslip")) return "payroll_details";
        if (q.Contains("risk") || q.Contains("churn") || q.Contains("burnout")) return "employee_risk";
        if (q.Contains("disciplinary") || q.Contains("warning")) return "disciplinary";
        if (q.Contains("terminate") || q.Contains("termination") || q.Contains("fire")) return "termination";
        if (q.Contains("promotion") || q.Contains("demotion") || q.Contains("compensation") || q.Contains("raise")) return "compensation";
        if (q.Contains("department")) return "department_info";
        if (q.Contains("overtime")) return "overtime_summary";
        if (q.Contains("holiday") || q.Contains("public holiday")) return "holiday_info";
        return "general";
    }

    private static string GetModule(string intent) => intent switch
    {
        "payroll_details" or "salary_details" or "compensation" => "Payroll",
        "employee_risk" or "disciplinary" or "termination" => "HR",
        "leave_status" or "leave_balance" or "pending_approvals" or "holiday_info" => "Leave",
        "overtime_summary" => "Attendance",
        "headcount" or "department_info" => "Organization",
        _ => "HR"
    };

    private static bool IsSensitiveIntent(string intent) =>
        intent is "payroll_details" or "salary_details" or "employee_risk" or "disciplinary" or "termination" or "compensation";

    private static bool IsAllowedForSensitiveIntent(string intent, IReadOnlyCollection<string> roles)
    {
        var hasAdminOrHr = roles.Any(r => r is "Admin" or "HR Manager" or "HR Officer");
        var hasPayroll = roles.Any(r => r is "Admin" or "HR Manager" or "HR Officer" or "Payroll Manager");

        return intent switch
        {
            "payroll_details" or "salary_details" => hasPayroll,
            _ => hasAdminOrHr
        };
    }
}
