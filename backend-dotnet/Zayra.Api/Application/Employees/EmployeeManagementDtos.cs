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

/// <summary>
/// Full employee detail response. All Employee entity fields are projected explicitly
/// so newly-added entity fields never auto-serialize. Sensitive fields are populated
/// only when includeSensitive=true (i.e. CanViewSensitive() returned true for the caller).
/// </summary>
public record EmployeeDetailDto
{
    // ── Identity ──────────────────────────────────────────────────────────────────
    public int Id { get; init; }
    public Guid? UserAccountId { get; init; }
    public string EmployeeCode { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string EnglishName { get; init; } = string.Empty;
    public string ArabicName { get; init; } = string.Empty;
    public string PreferredName { get; init; } = string.Empty;
    public string ProfilePhotoUrl { get; init; } = string.Empty;
    // ── Contact ───────────────────────────────────────────────────────────────────
    public string PersonalEmail { get; init; } = string.Empty;
    public string WorkEmail { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    // ── Demographics ──────────────────────────────────────────────────────────────
    public string Gender { get; init; } = string.Empty;
    public DateOnly? DateOfBirth { get; init; }
    public string MaritalStatus { get; init; } = string.Empty;
    public string Nationality { get; init; } = string.Empty;
    public string CountryCode { get; init; } = string.Empty;
    public string EmergencyContactName { get; init; } = string.Empty;
    public string EmergencyContactPhone { get; init; } = string.Empty;
    // ── Position ──────────────────────────────────────────────────────────────────
    public string Department { get; init; } = string.Empty;
    public string Designation { get; init; } = string.Empty;
    public string Branch { get; init; } = string.Empty;
    public string WorkLocation { get; init; } = string.Empty;
    public string JobTitle { get; init; } = string.Empty;
    public string Grade { get; init; } = string.Empty;
    public string CostCenter { get; init; } = string.Empty;
    public string EmploymentType { get; init; } = string.Empty;
    public string ContractType { get; init; } = string.Empty;
    public Guid? CompanyId { get; init; }
    public Guid? BranchId { get; init; }
    public Guid? DepartmentId { get; init; }
    public Guid? DesignationId { get; init; }
    public Guid? GradeId { get; init; }
    public Guid? CostCenterId { get; init; }
    public int? ManagerEmployeeId { get; init; }
    public int? SecondLevelManagerEmployeeId { get; init; }
    public int? SupervisorEmployeeId { get; init; }
    public int? HRBusinessPartnerEmployeeId { get; init; }
    // ── Employment ────────────────────────────────────────────────────────────────
    public string Status { get; init; } = string.Empty;
    public DateTime JoiningDate { get; init; }
    public DateOnly? ConfirmationDate { get; init; }
    public DateOnly? ProbationStartDate { get; init; }
    public DateOnly? ContractStartDate { get; init; }
    public DateOnly? ContractEndDate { get; init; }
    public DateOnly? ProbationEndDate { get; init; }
    public int? NoticePeriodDays { get; init; }
    public string PayrollProfileCode { get; init; } = string.Empty;
    public string ShiftPolicyCode { get; init; } = string.Empty;
    public string LeavePolicyCode { get; init; } = string.Empty;
    public string AttendancePolicyCode { get; init; } = string.Empty;
    // ── Payroll — SENSITIVE ───────────────────────────────────────────────────────
    public decimal? Salary { get; init; }
    public string BankName { get; init; } = string.Empty;
    public string BankIban { get; init; } = string.Empty;
    public string WpsBankDetails { get; init; } = string.Empty;
    // ── Compliance — dates ────────────────────────────────────────────────────────
    public string SponsorName { get; init; } = string.Empty;
    public DateOnly? PassportIssueDate { get; init; }
    public DateOnly? PassportExpiryDate { get; init; }
    public DateOnly? VisaIssueDate { get; init; }
    public DateOnly? VisaExpiryDate { get; init; }
    public DateOnly? ResidencyIssueDate { get; init; }
    public DateOnly? WorkPermitIssueDate { get; init; }
    // ── Compliance — numbers (PassportNumber, IqamaNumber SENSITIVE) ──────────────
    public string PassportNumber { get; init; } = string.Empty;
    public string VisaNumber { get; init; } = string.Empty;
    public string VisaFileNumber { get; init; } = string.Empty;
    public string IqamaNumber { get; init; } = string.Empty;
    public string MuqeemNumber { get; init; } = string.Empty;
    public string GosiReference { get; init; } = string.Empty;
    public string QiwaContractNumber { get; init; } = string.Empty;
    public string EmiratesId { get; init; } = string.Empty;
    public string LaborCardNumber { get; init; } = string.Empty;
    public string Qid { get; init; } = string.Empty;
    public string WorkPermitNumber { get; init; } = string.Empty;
    public string CivilId { get; init; } = string.Empty;
    public string ResidencyNumber { get; init; } = string.Empty;
    // ── Qiwa integration ──────────────────────────────────────────────────────────
    public string SaudiOrNonSaudi { get; init; } = string.Empty;
    public string IdType { get; init; } = string.Empty;
    public string IdNumber { get; init; } = string.Empty;
    public string OccupationCode { get; init; } = string.Empty;
    public string EstablishmentId { get; init; } = string.Empty;
    public string WorkLocationId { get; init; } = string.Empty;
    public string ContractReference { get; init; } = string.Empty;
    public string WorkPermitReference { get; init; } = string.Empty;
    public string QiwaEmployeeReference { get; init; } = string.Empty;
    public string QiwaSyncStatus { get; init; } = string.Empty;
    // ── HR notes — SENSITIVE ──────────────────────────────────────────────────────
    public string MedicalInformation { get; init; } = string.Empty;
    public string DisciplinaryRecords { get; init; } = string.Empty;
    public string TerminationReason { get; init; } = string.Empty;
    // ── Metadata ──────────────────────────────────────────────────────────────────
    public decimal ProfileCompletenessScore { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? UpdatedAtUtc { get; init; }
    public DateTime? ActivatedAtUtc { get; init; }
    // ── Subordinate collections (EF entity types — no sensitive employee PII) ─────
    public EmployeePayrollProfile? PayrollProfile { get; init; }
    public IReadOnlyCollection<EmployeeComplianceRecord> ComplianceRecords { get; init; } = [];
    public IReadOnlyCollection<EmployeeDocument> Documents { get; init; } = [];
    public IReadOnlyCollection<EmployeeHistory> History { get; init; } = [];
    public IReadOnlyCollection<EmployeeTransferRequest> Transfers { get; init; } = [];

    /// <summary>
    /// Projects a raw Employee entity to this DTO, applying the canonical sensitive-field
    /// gate inline. Does NOT mutate the entity.
    /// Sensitive field list must stay in sync with <see cref="EmployeeSensitiveMask"/>.
    /// </summary>
    public static EmployeeDetailDto Project(
        Employee e,
        bool includeSensitive,
        EmployeePayrollProfile? payrollProfile = null,
        IReadOnlyCollection<EmployeeComplianceRecord>? complianceRecords = null,
        IReadOnlyCollection<EmployeeDocument>? documents = null,
        IReadOnlyCollection<EmployeeHistory>? history = null,
        IReadOnlyCollection<EmployeeTransferRequest>? transfers = null) =>
        new()
        {
            Id                            = e.Id,
            UserAccountId                 = e.UserAccountId,
            EmployeeCode                  = e.EmployeeCode,
            FullName                      = e.FullName,
            EnglishName                   = e.EnglishName,
            ArabicName                    = e.ArabicName,
            PreferredName                 = e.PreferredName,
            ProfilePhotoUrl               = e.ProfilePhotoUrl,
            PersonalEmail                 = e.PersonalEmail,
            WorkEmail                     = e.WorkEmail,
            Phone                         = e.Phone,
            Gender                        = e.Gender,
            DateOfBirth                   = e.DateOfBirth,
            MaritalStatus                 = e.MaritalStatus,
            Nationality                   = e.Nationality,
            CountryCode                   = e.CountryCode,
            EmergencyContactName          = e.EmergencyContactName,
            EmergencyContactPhone         = e.EmergencyContactPhone,
            Department                    = e.Department,
            Designation                   = e.Designation,
            Branch                        = e.Branch,
            WorkLocation                  = e.WorkLocation,
            JobTitle                      = e.JobTitle,
            Grade                         = e.Grade,
            CostCenter                    = e.CostCenter,
            EmploymentType                = e.EmploymentType,
            ContractType                  = e.ContractType,
            CompanyId                     = e.CompanyId,
            BranchId                      = e.BranchId,
            DepartmentId                  = e.DepartmentId,
            DesignationId                 = e.DesignationId,
            GradeId                       = e.GradeId,
            CostCenterId                  = e.CostCenterId,
            ManagerEmployeeId             = e.ManagerEmployeeId,
            SecondLevelManagerEmployeeId  = e.SecondLevelManagerEmployeeId,
            SupervisorEmployeeId          = e.SupervisorEmployeeId,
            HRBusinessPartnerEmployeeId   = e.HRBusinessPartnerEmployeeId,
            Status                        = e.Status,
            JoiningDate                   = e.JoiningDate,
            ConfirmationDate              = e.ConfirmationDate,
            ProbationStartDate            = e.ProbationStartDate,
            ContractStartDate             = e.ContractStartDate,
            ContractEndDate               = e.ContractEndDate,
            ProbationEndDate              = e.ProbationEndDate,
            NoticePeriodDays              = e.NoticePeriodDays,
            PayrollProfileCode            = e.PayrollProfileCode,
            ShiftPolicyCode               = e.ShiftPolicyCode,
            LeavePolicyCode               = e.LeavePolicyCode,
            AttendancePolicyCode          = e.AttendancePolicyCode,
            // Sensitive — gated
            Salary                        = includeSensitive ? e.Salary : null,
            BankName                      = includeSensitive ? e.BankName : string.Empty,
            BankIban                      = includeSensitive ? e.BankIban : string.Empty,
            WpsBankDetails                = includeSensitive ? e.WpsBankDetails : string.Empty,
            PassportNumber                = includeSensitive ? e.PassportNumber : string.Empty,
            IqamaNumber                   = includeSensitive ? e.IqamaNumber : string.Empty,
            MedicalInformation            = includeSensitive ? e.MedicalInformation : string.Empty,
            DisciplinaryRecords           = includeSensitive ? e.DisciplinaryRecords : string.Empty,
            TerminationReason             = includeSensitive ? e.TerminationReason : string.Empty,
            // Non-sensitive compliance
            SponsorName                   = e.SponsorName,
            PassportIssueDate             = e.PassportIssueDate,
            PassportExpiryDate            = e.PassportExpiryDate,
            VisaIssueDate                 = e.VisaIssueDate,
            VisaExpiryDate                = e.VisaExpiryDate,
            ResidencyIssueDate            = e.ResidencyIssueDate,
            WorkPermitIssueDate           = e.WorkPermitIssueDate,
            VisaNumber                    = e.VisaNumber,
            VisaFileNumber                = e.VisaFileNumber,
            MuqeemNumber                  = e.MuqeemNumber,
            GosiReference                 = e.GosiReference,
            QiwaContractNumber            = e.QiwaContractNumber,
            EmiratesId                    = e.EmiratesId,
            LaborCardNumber               = e.LaborCardNumber,
            Qid                           = e.Qid,
            WorkPermitNumber              = e.WorkPermitNumber,
            CivilId                       = e.CivilId,
            ResidencyNumber               = e.ResidencyNumber,
            SaudiOrNonSaudi               = e.SaudiOrNonSaudi,
            IdType                        = e.IdType,
            IdNumber                      = e.IdNumber,
            OccupationCode                = e.OccupationCode,
            EstablishmentId               = e.EstablishmentId,
            WorkLocationId                = e.WorkLocationId,
            ContractReference             = e.ContractReference,
            WorkPermitReference           = e.WorkPermitReference,
            QiwaEmployeeReference         = e.QiwaEmployeeReference,
            QiwaSyncStatus                = e.QiwaSyncStatus,
            ProfileCompletenessScore      = e.ProfileCompletenessScore,
            CreatedAtUtc                  = e.CreatedAtUtc,
            UpdatedAtUtc                  = e.UpdatedAtUtc,
            ActivatedAtUtc                = e.ActivatedAtUtc,
            // Collections
            PayrollProfile                = payrollProfile,
            ComplianceRecords             = complianceRecords ?? [],
            Documents                     = documents ?? [],
            History                       = history ?? [],
            Transfers                     = transfers ?? [],
        };
}

/// <summary>
/// Self-service profile for an authenticated employee viewing their own record.
/// Excludes internal HR notes (DisciplinaryRecords, MedicalInformation, TerminationReason)
/// and internal processing fields (WpsBankDetails, QiwaSyncStatus, etc.).
/// The employee sees their own financial and identity data — these are not masked.
/// </summary>
public record EssEmployeeProfileDto
{
    public int Id { get; init; }
    public string EmployeeCode { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string EnglishName { get; init; } = string.Empty;
    public string ArabicName { get; init; } = string.Empty;
    public string PreferredName { get; init; } = string.Empty;
    public string ProfilePhotoUrl { get; init; } = string.Empty;
    public string PersonalEmail { get; init; } = string.Empty;
    public string WorkEmail { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public string Gender { get; init; } = string.Empty;
    public DateOnly? DateOfBirth { get; init; }
    public string MaritalStatus { get; init; } = string.Empty;
    public string Nationality { get; init; } = string.Empty;
    public string EmergencyContactName { get; init; } = string.Empty;
    public string EmergencyContactPhone { get; init; } = string.Empty;
    public string Department { get; init; } = string.Empty;
    public string Designation { get; init; } = string.Empty;
    public string JobTitle { get; init; } = string.Empty;
    public string Branch { get; init; } = string.Empty;
    public string WorkLocation { get; init; } = string.Empty;
    public string EmploymentType { get; init; } = string.Empty;
    public string ContractType { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTime JoiningDate { get; init; }
    public DateOnly? ContractStartDate { get; init; }
    public DateOnly? ContractEndDate { get; init; }
    public DateOnly? ProbationEndDate { get; init; }
    public int? ManagerEmployeeId { get; init; }
    // Own financial data — employee can see their own salary and bank details
    public decimal? Salary { get; init; }
    public string BankName { get; init; } = string.Empty;
    public string BankIban { get; init; } = string.Empty;
    // Own identity documents
    public string PassportNumber { get; init; } = string.Empty;
    public DateOnly? PassportIssueDate { get; init; }
    public DateOnly? PassportExpiryDate { get; init; }
    public string VisaNumber { get; init; } = string.Empty;
    public DateOnly? VisaIssueDate { get; init; }
    public DateOnly? VisaExpiryDate { get; init; }
    public string IqamaNumber { get; init; } = string.Empty;
    public string EmiratesId { get; init; } = string.Empty;
    public decimal ProfileCompletenessScore { get; init; }
    public DateTime? ActivatedAtUtc { get; init; }

    public static EssEmployeeProfileDto Project(Employee e) => new()
    {
        Id                       = e.Id,
        EmployeeCode             = e.EmployeeCode,
        FullName                 = e.FullName,
        EnglishName              = e.EnglishName,
        ArabicName               = e.ArabicName,
        PreferredName            = e.PreferredName,
        ProfilePhotoUrl          = e.ProfilePhotoUrl,
        PersonalEmail            = e.PersonalEmail,
        WorkEmail                = e.WorkEmail,
        Phone                    = e.Phone,
        Gender                   = e.Gender,
        DateOfBirth              = e.DateOfBirth,
        MaritalStatus            = e.MaritalStatus,
        Nationality              = e.Nationality,
        EmergencyContactName     = e.EmergencyContactName,
        EmergencyContactPhone    = e.EmergencyContactPhone,
        Department               = e.Department,
        Designation              = e.Designation,
        JobTitle                 = e.JobTitle,
        Branch                   = e.Branch,
        WorkLocation             = e.WorkLocation,
        EmploymentType           = e.EmploymentType,
        ContractType             = e.ContractType,
        Status                   = e.Status,
        JoiningDate              = e.JoiningDate,
        ContractStartDate        = e.ContractStartDate,
        ContractEndDate          = e.ContractEndDate,
        ProbationEndDate         = e.ProbationEndDate,
        ManagerEmployeeId        = e.ManagerEmployeeId,
        Salary                   = e.Salary,
        BankName                 = e.BankName,
        BankIban                 = e.BankIban,
        PassportNumber           = e.PassportNumber,
        PassportIssueDate        = e.PassportIssueDate,
        PassportExpiryDate       = e.PassportExpiryDate,
        VisaNumber               = e.VisaNumber,
        VisaIssueDate            = e.VisaIssueDate,
        VisaExpiryDate           = e.VisaExpiryDate,
        IqamaNumber              = e.IqamaNumber,
        EmiratesId               = e.EmiratesId,
        ProfileCompletenessScore = e.ProfileCompletenessScore,
        ActivatedAtUtc           = e.ActivatedAtUtc,
    };
}

public record EmployeeGroupCountDto(string Name, int Count);
public record EmployeeHeadcountReportDto(int Total, IReadOnlyCollection<EmployeeGroupCountDto> ByCompany, IReadOnlyCollection<EmployeeGroupCountDto> ByDepartment, IReadOnlyCollection<EmployeeGroupCountDto> ByStatus);
public record EmployeeStatusSummaryDto(IReadOnlyCollection<EmployeeGroupCountDto> Statuses);
public record EmployeeMissingDocumentsReportDto(int EmployeeId, string EmployeeCode, string FullName, IReadOnlyCollection<string> MissingDocumentTypes);
public record EmployeeExpiringDocumentDto(int EmployeeId, string EmployeeCode, string FullName, string DocumentType, DateOnly? ExpiryDate);
