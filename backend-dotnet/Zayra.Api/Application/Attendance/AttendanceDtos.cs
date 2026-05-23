using System.ComponentModel.DataAnnotations;
using Zayra.Api.Models;

namespace Zayra.Api.Application.Attendance;

public record AttendanceDeviceRequest(
    [Required, MaxLength(160)] string DeviceName,
    [Required, MaxLength(80)] string DeviceType,
    [Required, MaxLength(80)] string Vendor,
    [Required, MaxLength(120)] string SerialNumber,
    Guid? BranchId,
    string? LocationName,
    string? IpAddress,
    string? EndpointUrl,
    int? Port,
    string? ApiKeyReference,
    string? SyncMethod,
    string? SyncFrequency,
    bool IsActive = true);

public record AttendanceRawEventRequest(
    int? EmployeeId,
    string? EmployeeCode,
    Guid? DeviceId,
    string? Source,
    DateTime PunchTimestampUtc,
    string? PunchDirection,
    string? LocationName,
    decimal? Latitude,
    decimal? Longitude,
    string? IpAddress,
    string? PhotoReference,
    string? RawPayloadJson,
    string? SyncBatchReference,
    string? VerificationMethod,
    decimal? ConfidenceScore);

public record WebPunchRequest(int EmployeeId, string PunchDirection, string? LocationName, decimal? Latitude, decimal? Longitude);
public record ImportAttendanceRequest(string FileName, string CsvContent);
public record ProcessAttendanceRequest(DateOnly FromDate, DateOnly ToDate, int? EmployeeId);
public record RegularizationRequestDto(int EmployeeId, DateOnly WorkDate, string RequestType, DateTime? RequestedInUtc, DateTime? RequestedOutUtc, string Reason);
public record RegularizationDecisionRequest(string Comments);

public record AttendanceDailyDto(
    Guid Id,
    int EmployeeId,
    string EmployeeName,
    string Department,
    string Branch,
    DateOnly WorkDate,
    DateTime? FirstInUtc,
    DateTime? LastOutUtc,
    int TotalWorkedMinutes,
    int LateMinutes,
    int EarlyExitMinutes,
    int OvertimeMinutes,
    int UndertimeMinutes,
    bool MissingPunch,
    string Status,
    string ManualCorrectionStatus,
    bool IsPayrollLocked);

public record AttendanceDashboardDto(
    DateOnly Date,
    int ActiveEmployees,
    int Present,
    int Absent,
    int Late,
    int MissingPunch,
    int OvertimeEmployees,
    int DeviceErrors,
    int PendingRegularizations);

public record AttendanceMonthlyDto(int EmployeeId, string EmployeeName, int PresentDays, int AbsentDays, int LateDays, int MissingPunchDays, int OvertimeMinutes);
public record AttendancePayrollSummaryDto(int EmployeeId, string EmployeeName, int LateMinutes, int EarlyExitMinutes, int AbsenceDays, int OvertimeMinutes, bool HasLockedRecords);
public record AttendanceDeviceSyncDto(Guid DeviceId, string DeviceName, string Vendor, string Status, DateTime? LastSyncAtUtc, string ErrorLog);

public static class AttendanceMappings
{
    public static AttendanceDailyDto ToDto(this AttendanceDailyRecord r) => new(
        r.Id, r.EmployeeId, r.EmployeeName, r.Department, r.Branch, r.WorkDate, r.FirstInUtc, r.LastOutUtc,
        r.TotalWorkedMinutes, r.LateMinutes, r.EarlyExitMinutes, r.OvertimeMinutes, r.UndertimeMinutes,
        r.MissingPunch, r.Status, r.ManualCorrectionStatus, r.IsPayrollLocked);
}
