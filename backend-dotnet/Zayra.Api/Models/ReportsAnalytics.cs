using Zayra.Api.Domain.Entities;
namespace Zayra.Api.Models;

// ── Saved Reports ─────────────────────────────────────────────────────────────

public class SavedReport : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string ReportKey { get; set; } = string.Empty;   // e.g. "hr.headcount", "payroll.register"
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;    // HR, Payroll, Attendance, Leave, etc.
    public string FiltersJson { get; set; } = string.Empty; // serialized filter params
    public string ColumnsJson { get; set; } = string.Empty; // selected columns
    public bool IsShared { get; set; }
    public Guid CreatedBy { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
}

// ── Report Schedules ──────────────────────────────────────────────────────────

public class ReportSchedule : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string ReportKey { get; set; } = string.Empty;
    public string ReportName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string FiltersJson { get; set; } = string.Empty;
    public string Frequency { get; set; } = "Monthly";      // Daily, Weekly, Monthly, Quarterly
    public string DeliveryMethod { get; set; } = "Email";   // Email, InApp, Download
    public string Recipients { get; set; } = string.Empty;  // comma-separated emails
    public string ExportFormat { get; set; } = "Excel";     // Excel, CSV, PDF
    public bool IsActive { get; set; } = true;
    public DateTime? LastRunAtUtc { get; set; }
    public DateTime? NextRunAtUtc { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public Guid? UpdatedBy { get; set; }
}

// ── Report Execution Log ──────────────────────────────────────────────────────

public class ReportExecutionLog : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid? ScheduleId { get; set; }
    public string ReportKey { get; set; } = string.Empty;
    public string ReportName { get; set; } = string.Empty;
    public string FiltersJson { get; set; } = string.Empty;
    public string ExportFormat { get; set; } = string.Empty;
    public string Status { get; set; } = "Success";         // Success, Failed
    public int RowCount { get; set; }
    public string? ErrorMessage { get; set; }
    public string? FileUrl { get; set; }
    public Guid? RunBy { get; set; }
    public string RunByName { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public int DurationMs { get; set; }
}
