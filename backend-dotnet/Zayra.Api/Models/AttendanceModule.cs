namespace Zayra.Api.Models;

public class AttendanceDevice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public string DeviceType { get; set; } = string.Empty;
    public string Vendor { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public Guid? BranchId { get; set; }
    public string LocationName { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string EndpointUrl { get; set; } = string.Empty;
    public int? Port { get; set; }
    public string ApiKeyReference { get; set; } = string.Empty;
    public string SyncMethod { get; set; } = "Manual upload";
    public string SyncFrequency { get; set; } = "Manual";
    public string LastSyncStatus { get; set; } = "Never";
    public DateTime? LastSyncAtUtc { get; set; }
    public string ErrorLog { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public Guid? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public Guid? DeletedBy { get; set; }
}

public class AttendanceDeviceConnector
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid? DeviceId { get; set; }
    public string ConnectorCode { get; set; } = string.Empty;
    public string Vendor { get; set; } = string.Empty;
    public string ConnectorType { get; set; } = string.Empty;
    public string SettingsJson { get; set; } = "{}";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class AttendanceDeviceSyncLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid? DeviceId { get; set; }
    public string SyncMethod { get; set; } = string.Empty;
    public string Status { get; set; } = "Started";
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    public int RawEventsReceived { get; set; }
    public int RawEventsProcessed { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}

public class AttendanceRawEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public int? EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public Guid? DeviceId { get; set; }
    public string Source { get; set; } = "Web punch";
    public DateTime PunchTimestampUtc { get; set; }
    public string PunchDirection { get; set; } = "Unknown";
    public string LocationName { get; set; } = string.Empty;
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public string IpAddress { get; set; } = string.Empty;
    public string PhotoReference { get; set; } = string.Empty;
    public string RawPayloadJson { get; set; } = "{}";
    public string SyncBatchReference { get; set; } = string.Empty;
    public string VerificationMethod { get; set; } = "Manual";
    public decimal? ConfidenceScore { get; set; }
    public bool IsProcessed { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? CreatedBy { get; set; }
}

public class AttendanceDailyRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Branch { get; set; } = string.Empty;
    public DateOnly WorkDate { get; set; }
    public DateTime? FirstInUtc { get; set; }
    public DateTime? LastOutUtc { get; set; }
    public int TotalWorkedMinutes { get; set; }
    public int BreakMinutes { get; set; }
    public int LateMinutes { get; set; }
    public int EarlyExitMinutes { get; set; }
    public int OvertimeMinutes { get; set; }
    public int UndertimeMinutes { get; set; }
    public bool MissingPunch { get; set; }
    public string Status { get; set; } = "Absent";
    public string WorkMode { get; set; } = "Work from site";
    public string ManualCorrectionStatus { get; set; } = "None";
    public bool IsPayrollLocked { get; set; }
    public DateTime ProcessedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
    public bool IsDeleted { get; set; }
}

public class AttendancePolicy
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Guid? BranchId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? GradeId { get; set; }
    public int GraceMinutes { get; set; } = 10;
    public int LateThresholdMinutes { get; set; } = 15;
    public int EarlyExitThresholdMinutes { get; set; } = 15;
    public int HalfDayThresholdMinutes { get; set; } = 240;
    public int AbsentThresholdMinutes { get; set; } = 120;
    public int StandardWorkMinutes { get; set; } = 480;
    public int BreakMinutes { get; set; } = 60;
    public string RoundingRule { get; set; } = "NearestMinute";
    public bool RequiresOvertimeApproval { get; set; } = true;
    public bool AllowAbsenceToLeaveConversion { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class AttendanceRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid AttendancePolicyId { get; set; }
    public string RuleType { get; set; } = string.Empty;
    public string RuleValueJson { get; set; } = "{}";
    public bool IsActive { get; set; } = true;
}

public class AttendanceLocation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? BranchId { get; set; }
    public string LocationType { get; set; } = "Branch";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class AttendanceGeofence
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid AttendanceLocationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public int RadiusMeters { get; set; } = 100;
    public bool ClockInRequiredInside { get; set; } = true;
    public bool ClockOutRequiredInside { get; set; }
    public bool SpoofingRiskCheckEnabled { get; set; }
    public bool IsActive { get; set; } = true;
}

public class AttendanceRegularizationRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public int EmployeeId { get; set; }
    public DateOnly WorkDate { get; set; }
    public string RequestType { get; set; } = "Missed punch";
    public DateTime? RequestedInUtc { get; set; }
    public DateTime? RequestedOutUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = "PendingManager";
    public Guid? RequestedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? DecidedAtUtc { get; set; }
    public bool PayrollLockChecked { get; set; }
}

public class AttendanceCorrectionApproval
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid RegularizationRequestId { get; set; }
    public string ApprovalLevel { get; set; } = "Manager";
    public string Decision { get; set; } = "Pending";
    public string Comments { get; set; } = string.Empty;
    public Guid? DecidedByUserId { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
}

public class AttendancePayrollImpact
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public int EmployeeId { get; set; }
    public DateOnly WorkDate { get; set; }
    public string ImpactType { get; set; } = string.Empty;
    public int Minutes { get; set; }
    public decimal Hours => Math.Round(Minutes / 60m, 2);
    public string Status { get; set; } = "PendingPayroll";
    public Guid? DailyRecordId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class AttendanceImportBatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string Source { get; set; } = "CSV import";
    public string Status { get; set; } = "Pending";
    public int TotalRows { get; set; }
    public int ImportedRows { get; set; }
    public int FailedRows { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class AttendanceImportError
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ImportBatchId { get; set; }
    public int RowNumber { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public string RawRow { get; set; } = string.Empty;
}

public class AttendanceException
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public int EmployeeId { get; set; }
    public Guid? DailyRecordId { get; set; }
    public DateOnly WorkDate { get; set; }
    public string ExceptionType { get; set; } = string.Empty;
    public string Severity { get; set; } = "Info";
    public string Details { get; set; } = string.Empty;
    public bool IsResolved { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class AttendanceLockPeriod
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public string LockType { get; set; } = "Payroll";
    public string Status { get; set; } = "Locked";
    public Guid? LockedByUserId { get; set; }
    public DateTime LockedAtUtc { get; set; } = DateTime.UtcNow;
}

public class AttendanceAIInsight
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string InsightType { get; set; } = string.Empty;
    public string Severity { get; set; } = "Info";
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public int? EmployeeId { get; set; }
    public string DataJson { get; set; } = "{}";
    public bool IsAcknowledged { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class AttendanceAuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid? UserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string MetadataJson { get; set; } = "{}";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
