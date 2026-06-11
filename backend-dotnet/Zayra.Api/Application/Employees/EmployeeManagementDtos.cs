using System.ComponentModel.DataAnnotations;
using Zayra.Api.Models;

namespace Zayra.Api.Application.Employees;

public record EmployeeCreateRequest(
    string? EmployeeCode,
    bool ManualEmployeeCode,
    [Required, MaxLength(180)] string EnglishName,
    [MaxLength(180)] string? ArabicName,
    [MaxLength(120)] string? PreferredName,
    [Required, MaxLength(40)] string Gender,
    DateOnly? DateOfBirth,
    [MaxLength(80)] string? Nationality,
    [MaxLength(60)] string? MaritalStatus,
    [EmailAddress] string? PersonalEmail,
    [EmailAddress] string? WorkEmail,
    [MaxLength(60)] string? MobileNumber,
    [MaxLength(500)] string? ProfilePhotoUrl,
    Guid? CompanyId,
    Guid? BranchId,
    Guid? DepartmentId,
    Guid? DesignationId,
    Guid? GradeId,
    Guid? CostCenterId,
    [MaxLength(180)] string? JobTitle,
    int? ReportingManagerEmployeeId,
    int? SecondLevelManagerEmployeeId,
    [MaxLength(80)] string? EmploymentType,
    [MaxLength(80)] string? ContractType,
    DateTime? JoiningDate,
    DateOnly? ConfirmationDate,
    DateOnly? ProbationStartDate,
    DateOnly? ProbationEndDate,
    int? NoticePeriodDays,
    [MaxLength(120)] string? WorkLocation,
    [MaxLength(80)] string? PayrollGroup,
    [MaxLength(80)] string? ShiftPolicyCode,
    [MaxLength(80)] string? LeavePolicyCode,
    [MaxLength(80)] string? AttendancePolicyCode,
    EmployeePayrollProfileRequest? PayrollProfile,
    IReadOnlyCollection<EmployeeComplianceRecordRequest>? ComplianceRecords);

public record EmployeePayrollProfileRequest(
    string? BankName,
    string? Iban,
    string? AccountNumber,
    string? PaymentMethod,
    string? SalaryCurrency,
    string? PayrollGroup,
    string? SalaryStructureReference,
    bool WpsEligible,
    bool EosbEligible,
    string? SocialInsuranceReference);

public record EmployeeComplianceRecordRequest(
    [Required, MaxLength(10)] string CountryCode,
    [Required, MaxLength(120)] string FieldKey,
    [Required, MaxLength(180)] string FieldLabel,
    string? FieldValue,
    DateOnly? IssueDate,
    DateOnly? ExpiryDate,
    bool IsSensitive,
    bool IsRequired);

public record EmployeeStatusChangeRequest(
    [Required, MaxLength(80)] string Status,
    [Required] DateOnly EffectiveDate,
    [Required, MaxLength(500)] string Reason);

public record EmployeeDocumentUploadMetadata(
    [Required, MaxLength(80)] string DocumentType,
    [MaxLength(80)] string? DocumentCategory,
    DateOnly? IssueDate,
    DateOnly? ExpiryDate,
    DateOnly? RenewalReminderDate,
    bool IsRequired,
    [MaxLength(40)] string? ApprovalStatus,
    [MaxLength(1000)] string? Notes);

public record UpdateDocumentMetadataRequest(
    [MaxLength(80)] string? DocumentType,
    [MaxLength(80)] string? DocumentCategory,
    DateOnly? IssueDate,
    DateOnly? ExpiryDate,
    DateOnly? RenewalReminderDate,
    bool? IsRequired,
    [MaxLength(1000)] string? Notes);

public record DocumentVerifyRequest([MaxLength(1000)] string? Notes);
public record DocumentRejectRequest([Required, MaxLength(1000)] string Reason);
public record DocumentExpiryCheckResult(int MarkedExpired, int RemindersSent);

public record EmployeeTransferCreateRequest(
    string? NewBranch,
    string? NewDepartment,
    string? NewDesignation,
    int? NewManagerEmployeeId,
    [Required] DateOnly EffectiveDate,
    [Required, MaxLength(1000)] string Reason);

public record EmployeeDetailDto(
    Employee Employee,
    EmployeePayrollProfile? PayrollProfile,
    IReadOnlyCollection<EmployeeComplianceRecord> ComplianceRecords,
    IReadOnlyCollection<EmployeeDocument> Documents,
    IReadOnlyCollection<EmployeeHistory> History,
    IReadOnlyCollection<EmployeeTransferRequest> Transfers);

public record EmployeeGroupCountDto(string Name, int Count);
public record EmployeeHeadcountReportDto(int Total, IReadOnlyCollection<EmployeeGroupCountDto> ByCompany, IReadOnlyCollection<EmployeeGroupCountDto> ByDepartment, IReadOnlyCollection<EmployeeGroupCountDto> ByStatus);
public record EmployeeStatusSummaryDto(IReadOnlyCollection<EmployeeGroupCountDto> Statuses);
public record EmployeeMissingDocumentsReportDto(int EmployeeId, string EmployeeCode, string FullName, IReadOnlyCollection<string> MissingDocumentTypes);
public record EmployeeExpiringDocumentDto(int EmployeeId, string EmployeeCode, string FullName, string DocumentType, DateOnly? ExpiryDate);
