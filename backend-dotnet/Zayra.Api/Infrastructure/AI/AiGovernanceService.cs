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
        if (q.Contains("pending approval") || q.Contains("pending action") || q.Contains("what needs approval")) return "pending_approvals";
        if (q.Contains("salary") || q.Contains("payroll") || q.Contains("pay slip") || q.Contains("payslip")) return "payroll_details";
        if (q.Contains("risk") || q.Contains("churn") || q.Contains("burnout")) return "employee_risk";
        if (q.Contains("disciplinary") || q.Contains("warning")) return "disciplinary";
        if (q.Contains("terminate") || q.Contains("termination") || q.Contains("fire")) return "termination";
        if (q.Contains("promotion") || q.Contains("demotion") || q.Contains("compensation") || q.Contains("raise")) return "compensation";
        if (q.Contains("department")) return "department_info";
        if (q.Contains("overtime")) return "overtime_summary";
        if (q.Contains("holiday") || q.Contains("public holiday")) return "holiday_info";
        // New HR use-case intents
        if (q.Contains("profile") || q.Contains("employee detail") || q.Contains("tell me about") || q.Contains("who is")) return "employee_profile_summary";
        if (q.Contains("pending hr") || q.Contains("hr action") || q.Contains("what is pending") || q.Contains("open request")) return "pending_hr_actions";
        if (q.Contains("document") || q.Contains("compliance") || q.Contains("missing doc") || q.Contains("expir")) return "document_compliance_risk";
        if (q.Contains("attendance pattern") || q.Contains("leave pattern") || q.Contains("attendance trend") || q.Contains("leave trend") || q.Contains("absence pattern")) return "attendance_leave_pattern";
        if (q.Contains("feedback") || q.Contains("performance review") || q.Contains("draft review") || q.Contains("write feedback")) return "manager_feedback_draft";
        return "general";
    }

    private static string GetModule(string intent) => intent switch
    {
        "payroll_details" or "salary_details" or "compensation" => "Payroll",
        "employee_risk" or "disciplinary" or "termination" => "HR",
        "leave_status" or "leave_balance" or "pending_approvals" or "holiday_info" => "Leave",
        "pending_hr_actions" => "HR",
        "employee_profile_summary" => "Employees",
        "document_compliance_risk" => "Compliance",
        "attendance_leave_pattern" => "Attendance",
        "manager_feedback_draft" => "Performance",
        "overtime_summary" => "Attendance",
        "headcount" or "department_info" => "Organization",
        _ => "HR"
    };

    private static bool IsSensitiveIntent(string intent) =>
        intent is "payroll_details" or "salary_details" or "employee_risk" or "disciplinary" or "termination" or "compensation"
            or "employee_profile_summary" or "manager_feedback_draft";

    private static bool IsAllowedForSensitiveIntent(string intent, IReadOnlyCollection<string> roles)
    {
        var hasAdminOrHr = roles.Any(r => r is "Admin" or "HR Manager" or "HR Officer");
        var hasPayroll = roles.Any(r => r is "Admin" or "HR Manager" or "HR Officer" or "Payroll Manager");
        var hasManagerAccess = roles.Any(r => r is "Admin" or "HR Manager" or "HR Officer" or "Manager" or "Supervisor");

        return intent switch
        {
            "payroll_details" or "salary_details" => hasPayroll,
            "employee_profile_summary" or "manager_feedback_draft" => hasManagerAccess,
            _ => hasAdminOrHr
        };
    }
}
