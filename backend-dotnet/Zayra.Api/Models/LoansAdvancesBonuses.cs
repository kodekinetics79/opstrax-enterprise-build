namespace Zayra.Api.Models;

// ── Loan Module ───────────────────────────────────────────────────────────────

public class LoanType
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Code { get; set; } = string.Empty;        // Personal, Emergency, Housing, Vehicle, Education
    public string NameEn { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public decimal MaxAmount { get; set; }
    public int MaxInstallments { get; set; } = 12;
    public string RepaymentFrequency { get; set; } = "Monthly";
    public bool IsInterestFree { get; set; } = true;
    public decimal InterestRate { get; set; }               // 0 if interest-free
    public int MinServiceMonths { get; set; }               // eligibility: min months of service
    public bool RequiresApproval { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? CreatedBy { get; set; }
}

public class LoanPolicy
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid LoanTypeId { get; set; }
    public string PolicyName { get; set; } = string.Empty;
    public int MaxConcurrentLoans { get; set; } = 1;
    public decimal MaxMultiplierOfSalary { get; set; }      // e.g. 3 = max 3x monthly salary
    public int CooldownMonthsAfterRepayment { get; set; }
    public bool AllowEarlySettlement { get; set; } = true;
    public bool AllowRescheduling { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? CreatedBy { get; set; }
}

public class EmployeeLoan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public Guid LoanTypeId { get; set; }
    public string LoanTypeName { get; set; } = string.Empty;
    public string LoanNumber { get; set; } = string.Empty;  // auto-generated e.g. LN-2026-00001
    public decimal RequestedAmount { get; set; }
    public decimal ApprovedAmount { get; set; }
    public int RequestedInstallments { get; set; }
    public int ApprovedInstallments { get; set; }
    public decimal InstallmentAmount { get; set; }
    public string RepaymentFrequency { get; set; } = "Monthly";
    public DateOnly? DisbursementDate { get; set; }
    public DateOnly? RepaymentStartDate { get; set; }
    public decimal TotalRepaid { get; set; }
    public decimal OutstandingBalance { get; set; }
    public string Status { get; set; } = "Pending";         // Pending, Approved, Rejected, Active, Settled, Cancelled, Overdue
    public string? RejectionReason { get; set; }
    public string Notes { get; set; } = string.Empty;
    public bool IsLockedByPayroll { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public Guid? UpdatedBy { get; set; }
}

public class LoanApproval
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid LoanId { get; set; }
    public int StepOrder { get; set; }
    public string ApproverRole { get; set; } = string.Empty; // Manager, HR, Finance, Admin
    public Guid? ApprovedBy { get; set; }
    public string ApprovedByName { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";         // Pending, Approved, Rejected
    public string Comments { get; set; } = string.Empty;
    public DateTime? DecidedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class LoanInstallment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid LoanId { get; set; }
    public int InstallmentNumber { get; set; }
    public DateOnly DueDate { get; set; }
    public decimal AmountDue { get; set; }
    public decimal AmountPaid { get; set; }
    public string Status { get; set; } = "Pending";         // Pending, Paid, Overdue, Waived
    public Guid? PayrollRunId { get; set; }
    public DateOnly? PaidDate { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class LoanSettlement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid LoanId { get; set; }
    public string SettlementType { get; set; } = "Early";   // Early, Normal, Waiver
    public decimal SettlementAmount { get; set; }
    public DateOnly SettlementDate { get; set; }
    public string Notes { get; set; } = string.Empty;
    public Guid? ApprovedBy { get; set; }
    public string ApprovedByName { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? CreatedBy { get; set; }
}

public class LoanAuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid LoanId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string OldValuesJson { get; set; } = string.Empty;
    public string NewValuesJson { get; set; } = string.Empty;
    public Guid? PerformedBy { get; set; }
    public string PerformedByName { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

// ── Salary Advance Module ─────────────────────────────────────────────────────

public class AdvancePolicy
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string PolicyName { get; set; } = string.Empty;
    public decimal MaxPercentageOfSalary { get; set; } = 50; // max 50% of monthly salary
    public int MaxAdvancesPerYear { get; set; } = 2;
    public int MinServiceMonths { get; set; } = 6;
    public bool AllowInstallments { get; set; } = true;
    public int MaxInstallments { get; set; } = 3;
    public int CooldownMonths { get; set; } = 3;
    public bool RequiresApproval { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? CreatedBy { get; set; }
}

public class SalaryAdvance
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public string AdvanceNumber { get; set; } = string.Empty; // e.g. ADV-2026-00001
    public decimal RequestedAmount { get; set; }
    public decimal ApprovedAmount { get; set; }
    public string RepaymentType { get; set; } = "OneTime";  // OneTime, Installments
    public int Installments { get; set; } = 1;
    public decimal InstallmentAmount { get; set; }
    public DateOnly? RepaymentStartDate { get; set; }
    public decimal TotalRepaid { get; set; }
    public decimal OutstandingBalance { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";         // Pending, Approved, Rejected, Active, Settled
    public string? RejectionReason { get; set; }
    public bool IsLockedByPayroll { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public Guid? UpdatedBy { get; set; }
}

public class AdvanceApproval
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid AdvanceId { get; set; }
    public int StepOrder { get; set; }
    public string ApproverRole { get; set; } = string.Empty;
    public Guid? ApprovedBy { get; set; }
    public string ApprovedByName { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public string Comments { get; set; } = string.Empty;
    public DateTime? DecidedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class AdvanceInstallment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid AdvanceId { get; set; }
    public int InstallmentNumber { get; set; }
    public DateOnly DueDate { get; set; }
    public decimal AmountDue { get; set; }
    public decimal AmountPaid { get; set; }
    public string Status { get; set; } = "Pending";         // Pending, Paid, Overdue
    public Guid? PayrollRunId { get; set; }
    public DateOnly? PaidDate { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class AdvanceAuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid AdvanceId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string OldValuesJson { get; set; } = string.Empty;
    public string NewValuesJson { get; set; } = string.Empty;
    public Guid? PerformedBy { get; set; }
    public string PerformedByName { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

// ── Bonus Module ──────────────────────────────────────────────────────────────

public class BonusType
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Code { get; set; } = string.Empty;        // Performance, Festival, Annual, Joining, Retention, Project
    public string NameEn { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string CalculationMethod { get; set; } = "Fixed"; // Fixed, PercentageSalary, PerformanceBased, Custom
    public bool IsTaxable { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? CreatedBy { get; set; }
}

public class BonusBatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid BonusTypeId { get; set; }
    public string BonusTypeName { get; set; } = string.Empty;
    public string BatchNumber { get; set; } = string.Empty; // e.g. BON-2026-001
    public string BatchName { get; set; } = string.Empty;
    public string PaymentPeriod { get; set; } = string.Empty; // e.g. "2026-05"
    public DateOnly PaymentDate { get; set; }
    public decimal TotalAmount { get; set; }
    public int EmployeeCount { get; set; }
    public string Status { get; set; } = "Draft";           // Draft, PendingApproval, Approved, Paid, Cancelled
    public string Notes { get; set; } = string.Empty;
    public bool IsLockedByPayroll { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public Guid? UpdatedBy { get; set; }
}

public class EmployeeBonus
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid BonusBatchId { get; set; }
    public Guid EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public Guid BonusTypeId { get; set; }
    public string BonusTypeName { get; set; } = string.Empty;
    public decimal BasicSalary { get; set; }
    public string CalculationMethod { get; set; } = "Fixed";
    public decimal CalculationValue { get; set; }           // fixed amount or percentage
    public decimal BonusAmount { get; set; }
    public string PaymentPeriod { get; set; } = string.Empty;
    public string Status { get; set; } = "Draft";           // Draft, Approved, PaidInPayroll, Cancelled
    public string Notes { get; set; } = string.Empty;
    public Guid? PayrollRunId { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public Guid? UpdatedBy { get; set; }
}

public class BonusApproval
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid BonusBatchId { get; set; }
    public int StepOrder { get; set; }
    public string ApproverRole { get; set; } = string.Empty;
    public Guid? ApprovedBy { get; set; }
    public string ApprovedByName { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public string Comments { get; set; } = string.Empty;
    public DateTime? DecidedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class BonusAuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid? BonusBatchId { get; set; }
    public Guid? EmployeeBonusId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string OldValuesJson { get; set; } = string.Empty;
    public string NewValuesJson { get; set; } = string.Empty;
    public Guid? PerformedBy { get; set; }
    public string PerformedByName { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
