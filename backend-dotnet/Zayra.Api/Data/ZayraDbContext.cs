using System.Text;
using Microsoft.EntityFrameworkCore;
using Zayra.Api.Domain.Entities;
using Zayra.Api.Models;

namespace Zayra.Api.Data;

public class ZayraDbContext : DbContext
{
    public ZayraDbContext(DbContextOptions<ZayraDbContext> options) : base(options) { }

    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();
    public DbSet<AttendanceDevice> AttendanceDevices => Set<AttendanceDevice>();
    public DbSet<AttendanceDeviceConnector> AttendanceDeviceConnectors => Set<AttendanceDeviceConnector>();
    public DbSet<AttendanceDeviceSyncLog> AttendanceDeviceSyncLogs => Set<AttendanceDeviceSyncLog>();
    public DbSet<AttendanceRawEvent> AttendanceRawEvents => Set<AttendanceRawEvent>();
    public DbSet<AttendanceDailyRecord> AttendanceDailyRecords => Set<AttendanceDailyRecord>();
    public DbSet<AttendancePolicy> AttendancePolicies => Set<AttendancePolicy>();
    public DbSet<AttendanceRule> AttendanceRules => Set<AttendanceRule>();
    public DbSet<AttendanceLocation> AttendanceLocations => Set<AttendanceLocation>();
    public DbSet<AttendanceGeofence> AttendanceGeofences => Set<AttendanceGeofence>();
    public DbSet<AttendanceRegularizationRequest> AttendanceRegularizationRequests => Set<AttendanceRegularizationRequest>();
    public DbSet<AttendanceCorrectionApproval> AttendanceCorrectionApprovals => Set<AttendanceCorrectionApproval>();
    public DbSet<AttendancePayrollImpact> AttendancePayrollImpacts => Set<AttendancePayrollImpact>();
    public DbSet<AttendanceImportBatch> AttendanceImportBatches => Set<AttendanceImportBatch>();
    public DbSet<AttendanceImportError> AttendanceImportErrors => Set<AttendanceImportError>();
    public DbSet<AttendanceException> AttendanceExceptions => Set<AttendanceException>();
    public DbSet<AttendanceLockPeriod> AttendanceLockPeriods => Set<AttendanceLockPeriod>();
    public DbSet<AttendanceAIInsight> AttendanceAIInsights => Set<AttendanceAIInsight>();
    public DbSet<AttendanceAuditLog> AttendanceAuditLogs => Set<AttendanceAuditLog>();
    // ── Leave Management ──────────────────────────────────────────────────────────
    public DbSet<LeaveType> LeaveTypes => Set<LeaveType>();
    public DbSet<LeavePolicy> LeavePolicies => Set<LeavePolicy>();
    public DbSet<EmployeeLeaveBalance> EmployeeLeaveBalances => Set<EmployeeLeaveBalance>();
    public DbSet<LeaveBalanceTransaction> LeaveBalanceTransactions => Set<LeaveBalanceTransaction>();
    public DbSet<LeaveRequest> LeaveRequests => Set<LeaveRequest>();
    public DbSet<LeaveApproval> LeaveApprovals => Set<LeaveApproval>();
    public DbSet<LeaveCancellationRequest> LeaveCancellationRequests => Set<LeaveCancellationRequest>();
    public DbSet<LeaveModificationRequest> LeaveModificationRequests => Set<LeaveModificationRequest>();
    public DbSet<PublicHolidayCalendar> PublicHolidayCalendars => Set<PublicHolidayCalendar>();
    public DbSet<PublicHoliday> PublicHolidays => Set<PublicHoliday>();
    public DbSet<LeaveBlackoutDate> LeaveBlackoutDates => Set<LeaveBlackoutDate>();
    public DbSet<LeaveEncashmentRequest> LeaveEncashmentRequests => Set<LeaveEncashmentRequest>();
    public DbSet<CompOffCredit> CompOffCredits => Set<CompOffCredit>();
    public DbSet<CompOffUsage> CompOffUsages => Set<CompOffUsage>();
    public DbSet<AbsenceRecord> AbsenceRecords => Set<AbsenceRecord>();
    public DbSet<AbsenceRegularizationRequest> AbsenceRegularizationRequests => Set<AbsenceRegularizationRequest>();
    public DbSet<LeaveDelegation> LeaveDelegations => Set<LeaveDelegation>();
    public DbSet<LeavePayrollImpact> LeavePayrollImpacts => Set<LeavePayrollImpact>();
    public DbSet<LeaveAuditLog> LeaveAuditLogs => Set<LeaveAuditLog>();
    public DbSet<LeaveAIInsight> LeaveAIInsights => Set<LeaveAIInsight>();
    public DbSet<PayrollRun> PayrollRuns => Set<PayrollRun>();
    public DbSet<PayrollSlip> PayrollSlips => Set<PayrollSlip>();
    public DbSet<ShiftDefinition> ShiftDefinitions => Set<ShiftDefinition>();
    public DbSet<ShiftAssignment> ShiftAssignments => Set<ShiftAssignment>();
    public DbSet<ManpowerRequisition> ManpowerRequisitions => Set<ManpowerRequisition>();
    public DbSet<JobOpening> JobOpenings => Set<JobOpening>();
    public DbSet<Candidate> Candidates => Set<Candidate>();
    public DbSet<JobApplication> JobApplications => Set<JobApplication>();
    public DbSet<ApplicationEvent> ApplicationEvents => Set<ApplicationEvent>();
    public DbSet<InterviewSchedule> InterviewSchedules => Set<InterviewSchedule>();
    public DbSet<OfferLetter> OfferLetters => Set<OfferLetter>();
    // ── Performance & Appraisals ─────────────────────────────────────────────
    public DbSet<PerformanceCycle> PerformanceCycles => Set<PerformanceCycle>();
    public DbSet<PerformanceScorecardTemplate> PerformanceScorecardTemplates => Set<PerformanceScorecardTemplate>();
    public DbSet<PerformanceRatingScale> PerformanceRatingScales => Set<PerformanceRatingScale>();
    public DbSet<PerformanceRatingOption> PerformanceRatingOptions => Set<PerformanceRatingOption>();
    public DbSet<PerformanceCycleEmployee> PerformanceCycleEmployees => Set<PerformanceCycleEmployee>();
    public DbSet<Competency> Competencies => Set<Competency>();
    public DbSet<RoleCompetency> RoleCompetencies => Set<RoleCompetency>();
    public DbSet<EmployeeGoal> EmployeeGoals => Set<EmployeeGoal>();
    public DbSet<GoalProgressUpdate> GoalProgressUpdates => Set<GoalProgressUpdate>();
    public DbSet<AppraisalReview> AppraisalReviews => Set<AppraisalReview>();
    public DbSet<AppraisalScoreBreakdown> AppraisalScoreBreakdowns => Set<AppraisalScoreBreakdown>();
    public DbSet<AppraisalCompetencyRating> AppraisalCompetencyRatings => Set<AppraisalCompetencyRating>();
    public DbSet<Feedback360> Feedback360 => Set<Feedback360>();
    public DbSet<AppraisalCalibration> AppraisalCalibrations => Set<AppraisalCalibration>();
    public DbSet<AppraisalAppeal> AppraisalAppeals => Set<AppraisalAppeal>();
    public DbSet<IncrementRecommendation> IncrementRecommendations => Set<IncrementRecommendation>();
    public DbSet<PromotionRecommendation> PromotionRecommendations => Set<PromotionRecommendation>();
    public DbSet<BonusRecommendation> BonusRecommendations => Set<BonusRecommendation>();
    public DbSet<PerformanceImprovementPlan> PerformanceImprovementPlans => Set<PerformanceImprovementPlan>();
    public DbSet<PIPCheckIn> PIPCheckIns => Set<PIPCheckIn>();
    public DbSet<ProbationReview> ProbationReviews => Set<ProbationReview>();
    public DbSet<ContinuousFeedback> ContinuousFeedback => Set<ContinuousFeedback>();
    public DbSet<PerformanceAuditLog> PerformanceAuditLogs => Set<PerformanceAuditLog>();
    public DbSet<EmployeeDraft> EmployeeDrafts => Set<EmployeeDraft>();
    public DbSet<EmployeeDocument> EmployeeDocuments => Set<EmployeeDocument>();
    public DbSet<EmployeeDocumentVersion> EmployeeDocumentVersions => Set<EmployeeDocumentVersion>();
    public DbSet<EmployeeHistory> EmployeeHistories => Set<EmployeeHistory>();
    public DbSet<EmployeeStatusHistory> EmployeeStatusHistories => Set<EmployeeStatusHistory>();
    public DbSet<EmployeeChangeRequest> EmployeeChangeRequests => Set<EmployeeChangeRequest>();
    public DbSet<EmployeeTransferRequest> EmployeeTransferRequests => Set<EmployeeTransferRequest>();
    public DbSet<EmployeePayrollProfile> EmployeePayrollProfiles => Set<EmployeePayrollProfile>();
    public DbSet<EmployeeComplianceRecord> EmployeeComplianceRecords => Set<EmployeeComplianceRecord>();
    public DbSet<EmployeeDependent> EmployeeDependents => Set<EmployeeDependent>();
    public DbSet<EmployeeUserAccount> EmployeeUserAccounts => Set<EmployeeUserAccount>();
    public DbSet<UserPermissionOverride> UserPermissionOverrides => Set<UserPermissionOverride>();
    public DbSet<ApprovalDelegation> ApprovalDelegations => Set<ApprovalDelegation>();
    public DbSet<ApprovalAuthority> ApprovalAuthorities => Set<ApprovalAuthority>();
    public DbSet<ESSDashboardPreference> ESSDashboardPreferences => Set<ESSDashboardPreference>();
    public DbSet<EmployeeProfileChangeRequest> EmployeeProfileChangeRequests => Set<EmployeeProfileChangeRequest>();
    public DbSet<EmployeeDocumentRequest> EmployeeDocumentRequests => Set<EmployeeDocumentRequest>();
    public DbSet<HRRequest> HRRequests => Set<HRRequest>();
    public DbSet<HRRequestCategory> HRRequestCategories => Set<HRRequestCategory>();
    public DbSet<HRRequestComment> HRRequestComments => Set<HRRequestComment>();
    public DbSet<HRRequestAttachment> HRRequestAttachments => Set<HRRequestAttachment>();
    public DbSet<HRRequestSLA> HRRequestSLAs => Set<HRRequestSLA>();
    public DbSet<EmployeePolicyAcknowledgement> EmployeePolicyAcknowledgements => Set<EmployeePolicyAcknowledgement>();
    public DbSet<EmployeeAnnouncement> EmployeeAnnouncements => Set<EmployeeAnnouncement>();
    public DbSet<EmployeeNotification> EmployeeNotifications => Set<EmployeeNotification>();
    public DbSet<EmployeeNotificationPreference> EmployeeNotificationPreferences => Set<EmployeeNotificationPreference>();
    public DbSet<EmployeePayslipAccessLog> EmployeePayslipAccessLogs => Set<EmployeePayslipAccessLog>();
    public DbSet<EmployeeSelfServiceAuditLog> EmployeeSelfServiceAuditLogs => Set<EmployeeSelfServiceAuditLog>();
    public DbSet<EmployeeAIQueryLog> EmployeeAIQueryLogs => Set<EmployeeAIQueryLog>();
    public DbSet<EmployeeActionItem> EmployeeActionItems => Set<EmployeeActionItem>();
    public DbSet<EmployeeSentimentPulse> EmployeeSentimentPulses => Set<EmployeeSentimentPulse>();
    public DbSet<EmployeeMobileDevice> EmployeeMobileDevices => Set<EmployeeMobileDevice>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Designation> Designations => Set<Designation>();
    public DbSet<Grade> Grades => Set<Grade>();
    public DbSet<CostCenter> CostCenters => Set<CostCenter>();
    public DbSet<EmployeeIdRule> EmployeeIdRules => Set<EmployeeIdRule>();
    public DbSet<ApprovalWorkflow> ApprovalWorkflows => Set<ApprovalWorkflow>();
    public DbSet<ApprovalWorkflowStep> ApprovalWorkflowSteps => Set<ApprovalWorkflowStep>();
    public DbSet<ApprovalRequest> ApprovalRequests => Set<ApprovalRequest>();
    public DbSet<ApprovalDecision> ApprovalDecisions => Set<ApprovalDecision>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ApplySnakeCaseColumns(modelBuilder);


        modelBuilder.Entity<Employee>(entity =>
        {
            entity.ToTable("employees");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.EmployeeCode).HasMaxLength(50).IsRequired();
            entity.Property(x => x.FullName).HasMaxLength(150).IsRequired();
            entity.Property(x => x.Salary).HasPrecision(12, 2);
            entity.Property(x => x.ProfileCompletenessScore).HasPrecision(5, 2);
            entity.HasIndex(x => new { x.TenantId, x.EmployeeCode }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.Status });
            entity.HasIndex(x => new { x.TenantId, x.Department });
            entity.HasIndex(x => new { x.TenantId, x.IsDeleted });
        });


        modelBuilder.Entity<EmployeeDraft>(entity =>
        {
            entity.ToTable("employee_drafts");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Salary).HasPrecision(12, 2);
            entity.Property(x => x.ProfileCompletenessScore).HasPrecision(5, 2);
            entity.HasIndex(x => new { x.TenantId, x.Status });
        });

        modelBuilder.Entity<EmployeeDocument>(entity =>
        {
            entity.ToTable("employee_documents");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.EmployeeId });
            entity.HasIndex(x => new { x.TenantId, x.DraftId });
            entity.HasIndex(x => new { x.TenantId, x.EmployeeId, x.DocumentType, x.IsDeleted });
        });

        modelBuilder.Entity<EmployeeDocumentVersion>(entity =>
        {
            entity.ToTable("employee_document_versions");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.EmployeeDocumentId, x.VersionNumber }).IsUnique();
        });

        modelBuilder.Entity<EmployeeHistory>(entity =>
        {
            entity.ToTable("employee_histories");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.SnapshotJson).HasColumnType("json");
            entity.HasIndex(x => new { x.TenantId, x.EmployeeId, x.CreatedAtUtc });
        });

        modelBuilder.Entity<EmployeeStatusHistory>(entity =>
        {
            entity.ToTable("employee_status_histories");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.EmployeeId, x.CreatedAtUtc });
        });

        modelBuilder.Entity<EmployeeChangeRequest>(entity =>
        {
            entity.ToTable("employee_change_requests");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ProposedChangesJson).HasColumnType("json");
            entity.HasIndex(x => new { x.TenantId, x.EmployeeId, x.Status });
        });

        modelBuilder.Entity<EmployeeTransferRequest>(entity =>
        {
            entity.ToTable("employee_transfer_requests");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.EmployeeId, x.Status });
        });

        modelBuilder.Entity<EmployeePayrollProfile>(entity =>
        {
            entity.ToTable("employee_payroll_profiles");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.EmployeeId }).IsUnique();
        });

        modelBuilder.Entity<EmployeeComplianceRecord>(entity =>
        {
            entity.ToTable("employee_compliance_records");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.EmployeeId, x.CountryCode, x.FieldKey }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.ExpiryDate });
        });

        modelBuilder.Entity<EmployeeDependent>(entity =>
        {
            entity.ToTable("employee_dependents");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.EmployeeId });
        });

        modelBuilder.Entity<EmployeeUserAccount>(entity =>
        {
            entity.ToTable("employee_user_accounts");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.AccessMode).HasMaxLength(40).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(40).IsRequired();
            entity.Property(x => x.InvitationTokenHash).HasMaxLength(128);
            entity.HasIndex(x => new { x.TenantId, x.EmployeeId, x.IsPrimary });
            entity.HasIndex(x => new { x.TenantId, x.UserId }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.InvitationTokenHash });
            entity.HasOne(x => x.User).WithMany(x => x.EmployeeUserAccounts).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<UserPermissionOverride>(entity =>
        {
            entity.ToTable("user_permission_overrides");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.PermissionKey).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Effect).HasMaxLength(20).IsRequired();
            entity.HasIndex(x => new { x.TenantId, x.UserId, x.PermissionKey }).IsUnique();
            entity.HasOne(x => x.User).WithMany(x => x.PermissionOverrides).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApprovalDelegation>(entity =>
        {
            entity.ToTable("approval_delegations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Scope).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(40).IsRequired();
            entity.HasIndex(x => new { x.TenantId, x.FromEmployeeId, x.ToEmployeeId, x.Status });
            entity.HasIndex(x => new { x.TenantId, x.StartDate, x.EndDate });
        });

        modelBuilder.Entity<ApprovalAuthority>(entity =>
        {
            entity.ToTable("approval_authorities");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.AmountLimit).HasPrecision(14, 2);
            entity.HasIndex(x => new { x.TenantId, x.EmployeeId, x.AuthorityScope, x.IsActive });
        });

        modelBuilder.Entity<ESSDashboardPreference>(entity =>
        {
            entity.ToTable("ess_dashboard_preferences");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.WidgetLayoutJson).HasColumnType("json");
            entity.HasIndex(x => new { x.TenantId, x.EmployeeId }).IsUnique();
        });

        modelBuilder.Entity<EmployeeProfileChangeRequest>(entity =>
        {
            entity.ToTable("employee_profile_change_requests");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.RequestedChangesJson).HasColumnType("json");
            entity.HasIndex(x => new { x.TenantId, x.EmployeeId, x.Status });
        });

        modelBuilder.Entity<EmployeeDocumentRequest>(entity =>
        {
            entity.ToTable("employee_document_requests");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.EmployeeId, x.Status });
        });

        modelBuilder.Entity<HRRequest>(entity =>
        {
            entity.ToTable("hr_requests");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.EmployeeId, x.Status });
            entity.HasIndex(x => new { x.TenantId, x.DueAtUtc });
        });

        modelBuilder.Entity<HRRequestCategory>(entity =>
        {
            entity.ToTable("hr_request_categories");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        });

        modelBuilder.Entity<HRRequestComment>(entity =>
        {
            entity.ToTable("hr_request_comments");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.HRRequestId, x.CreatedAtUtc });
        });

        modelBuilder.Entity<HRRequestAttachment>(entity =>
        {
            entity.ToTable("hr_request_attachments");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.HRRequestId });
        });

        modelBuilder.Entity<HRRequestSLA>(entity =>
        {
            entity.ToTable("hr_request_slas");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.CategoryId, x.Priority });
        });

        modelBuilder.Entity<EmployeePolicyAcknowledgement>(entity =>
        {
            entity.ToTable("employee_policy_acknowledgements");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.EmployeeId, x.PolicyId }).IsUnique();
        });

        modelBuilder.Entity<EmployeeAnnouncement>(entity =>
        {
            entity.ToTable("employee_announcements");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.IsActive, x.PublishedAtUtc });
        });

        modelBuilder.Entity<EmployeeNotification>(entity =>
        {
            entity.ToTable("employee_notifications");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.EmployeeId, x.IsRead });
        });

        modelBuilder.Entity<EmployeeNotificationPreference>(entity =>
        {
            entity.ToTable("employee_notification_preferences");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.QuietHoursJson).HasColumnType("json");
            entity.HasIndex(x => new { x.TenantId, x.EmployeeId }).IsUnique();
        });

        modelBuilder.Entity<EmployeePayslipAccessLog>(entity =>
        {
            entity.ToTable("employee_payslip_access_logs");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.EmployeeId, x.PayslipId });
        });

        modelBuilder.Entity<EmployeeSelfServiceAuditLog>(entity =>
        {
            entity.ToTable("employee_self_service_audit_logs");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.EmployeeId, x.CreatedAtUtc });
        });

        modelBuilder.Entity<EmployeeAIQueryLog>(entity =>
        {
            entity.ToTable("employee_ai_query_logs");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.EmployeeId, x.CreatedAtUtc });
        });

        modelBuilder.Entity<EmployeeActionItem>(entity =>
        {
            entity.ToTable("employee_action_items");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.EmployeeId, x.Status });
        });

        modelBuilder.Entity<EmployeeSentimentPulse>(entity =>
        {
            entity.ToTable("employee_sentiment_pulses");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.EmployeeId, x.CreatedAtUtc });
        });

        modelBuilder.Entity<EmployeeMobileDevice>(entity =>
        {
            entity.ToTable("employee_mobile_devices");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.EmployeeId, x.DeviceIdentifier }).IsUnique();
        });


        modelBuilder.Entity<Notification>(entity =>
        {
            entity.ToTable("notifications");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.UserId, x.Status, x.CreatedAtUtc });
        });

        modelBuilder.Entity<Company>(entity =>
        {
            entity.ToTable("companies");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.LegalNameEn });
            entity.HasIndex(x => new { x.TenantId, x.RegistrationNumber });
            entity.HasIndex(x => new { x.TenantId, x.IsDeleted });
        });

        modelBuilder.Entity<Branch>(entity =>
        {
            entity.ToTable("branches");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.CompanyId });
            entity.HasIndex(x => new { x.TenantId, x.IsDeleted });
        });

        modelBuilder.Entity<Department>(entity =>
        {
            entity.ToTable("departments");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.BranchId });
            entity.HasIndex(x => new { x.TenantId, x.ParentDepartmentId });
            entity.HasIndex(x => new { x.TenantId, x.IsDeleted });
        });

        modelBuilder.Entity<Designation>(entity =>
        {
            entity.ToTable("designations");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.DepartmentId });
            entity.HasIndex(x => new { x.TenantId, x.IsDeleted });
        });

        modelBuilder.Entity<Grade>(entity =>
        {
            entity.ToTable("grades");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.IsDeleted });
        });

        modelBuilder.Entity<CostCenter>(entity =>
        {
            entity.ToTable("cost_centers");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.CompanyId });
            entity.HasIndex(x => new { x.TenantId, x.IsDeleted });
        });

        modelBuilder.Entity<EmployeeIdRule>(entity =>
        {
            entity.ToTable("employee_id_rules");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.CompanyId, x.IsActive });
        });

        modelBuilder.Entity<ApprovalWorkflow>(entity =>
        {
            entity.ToTable("approval_workflows");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            entity.HasMany(x => x.Steps).WithOne().HasForeignKey(x => x.WorkflowId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApprovalWorkflowStep>(entity =>
        {
            entity.ToTable("approval_workflow_steps");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.WorkflowId, x.StepOrder }).IsUnique();
        });

        modelBuilder.Entity<ApprovalRequest>(entity =>
        {
            entity.ToTable("approval_requests");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.EntityName).HasMaxLength(120);
            entity.Property(x => x.EntityId).HasMaxLength(80);
            entity.Property(x => x.Status).HasMaxLength(40);
            entity.Property(x => x.Title).HasMaxLength(240);
            entity.HasIndex(x => new { x.TenantId, x.EntityName, x.EntityId, x.Status });
            entity.HasMany(x => x.Decisions).WithOne().HasForeignKey(x => x.ApprovalRequestId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApprovalDecision>(entity =>
        {
            entity.ToTable("approval_decisions");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.ApprovalRequestId, x.StepOrder });
        });

        modelBuilder.Entity<AttendanceRecord>(entity =>
        {
            entity.ToTable("attendance_records");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Status).HasMaxLength(30).IsRequired();
            entity.Property(x => x.OvertimeHours).HasPrecision(5, 2);
            entity.HasIndex(x => new { x.TenantId, x.EmployeeId, x.WorkDate }).IsUnique();
        });

        modelBuilder.Entity<AttendanceDevice>(entity =>
        {
            entity.ToTable("attendance_devices");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.SerialNumber }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.Vendor, x.DeviceType, x.IsActive });
            entity.HasIndex(x => new { x.TenantId, x.IsDeleted });
        });
        modelBuilder.Entity<AttendanceDeviceConnector>(entity =>
        {
            entity.ToTable("attendance_device_connectors");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.SettingsJson).HasColumnType("json");
            entity.HasIndex(x => new { x.TenantId, x.ConnectorCode }).IsUnique();
        });
        modelBuilder.Entity<AttendanceDeviceSyncLog>(entity =>
        {
            entity.ToTable("attendance_device_sync_logs");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.DeviceId, x.StartedAtUtc });
        });
        modelBuilder.Entity<AttendanceRawEvent>(entity =>
        {
            entity.ToTable("attendance_raw_events");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Latitude).HasPrecision(10, 7);
            entity.Property(x => x.Longitude).HasPrecision(10, 7);
            entity.Property(x => x.ConfidenceScore).HasPrecision(5, 2);
            entity.Property(x => x.RawPayloadJson).HasColumnType("json");
            entity.HasIndex(x => new { x.TenantId, x.EmployeeId, x.PunchTimestampUtc, x.PunchDirection, x.DeviceId }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.IsProcessed, x.PunchTimestampUtc });
            entity.HasIndex(x => new { x.TenantId, x.SyncBatchReference });
        });
        modelBuilder.Entity<AttendanceDailyRecord>(entity =>
        {
            entity.ToTable("attendance_daily_records");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.EmployeeId, x.WorkDate }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.WorkDate, x.Status });
            entity.HasIndex(x => new { x.TenantId, x.MissingPunch });
        });
        modelBuilder.Entity<AttendancePolicy>(entity =>
        {
            entity.ToTable("attendance_policies");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.IsActive });
        });
        modelBuilder.Entity<AttendanceRule>(entity =>
        {
            entity.ToTable("attendance_rules");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.RuleValueJson).HasColumnType("json");
            entity.HasIndex(x => new { x.TenantId, x.AttendancePolicyId, x.RuleType });
        });
        modelBuilder.Entity<AttendanceLocation>(entity =>
        {
            entity.ToTable("attendance_locations");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.BranchId });
        });
        modelBuilder.Entity<AttendanceGeofence>(entity =>
        {
            entity.ToTable("attendance_geofences");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Latitude).HasPrecision(10, 7);
            entity.Property(x => x.Longitude).HasPrecision(10, 7);
            entity.HasIndex(x => new { x.TenantId, x.AttendanceLocationId });
        });
        modelBuilder.Entity<AttendanceRegularizationRequest>(entity =>
        {
            entity.ToTable("attendance_regularization_requests");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.EmployeeId, x.WorkDate });
            entity.HasIndex(x => new { x.TenantId, x.Status });
        });
        modelBuilder.Entity<AttendanceCorrectionApproval>(entity =>
        {
            entity.ToTable("attendance_correction_approvals");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.RegularizationRequestId, x.ApprovalLevel });
        });
        modelBuilder.Entity<AttendancePayrollImpact>(entity =>
        {
            entity.ToTable("attendance_payroll_impacts");
            entity.HasKey(x => x.Id);
            entity.Ignore(x => x.Hours);
            entity.HasIndex(x => new { x.TenantId, x.EmployeeId, x.WorkDate });
            entity.HasIndex(x => new { x.TenantId, x.Status });
        });
        modelBuilder.Entity<AttendanceImportBatch>(entity =>
        {
            entity.ToTable("attendance_import_batches");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.CreatedAtUtc });
        });
        modelBuilder.Entity<AttendanceImportError>(entity =>
        {
            entity.ToTable("attendance_import_errors");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.ImportBatchId });
        });
        modelBuilder.Entity<AttendanceException>(entity =>
        {
            entity.ToTable("attendance_exceptions");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.WorkDate, x.ExceptionType, x.IsResolved });
        });
        modelBuilder.Entity<AttendanceLockPeriod>(entity =>
        {
            entity.ToTable("attendance_lock_periods");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.PeriodStart, x.PeriodEnd, x.LockType });
        });
        modelBuilder.Entity<AttendanceAIInsight>(entity =>
        {
            entity.ToTable("attendance_ai_insights");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.DataJson).HasColumnType("json");
            entity.HasIndex(x => new { x.TenantId, x.InsightType, x.IsAcknowledged });
        });
        modelBuilder.Entity<AttendanceAuditLog>(entity =>
        {
            entity.ToTable("attendance_audit_logs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.MetadataJson).HasColumnType("json");
            entity.HasIndex(x => new { x.TenantId, x.EntityName, x.EntityId, x.CreatedAtUtc });
        });

        // ── Leave Management ──────────────────────────────────────────────────────
        modelBuilder.Entity<LeaveType>(entity => {
            entity.ToTable("leave_types");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.IsActive });
        });
        modelBuilder.Entity<LeavePolicy>(entity => {
            entity.ToTable("leave_policies");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.AnnualEntitlementDays).HasPrecision(6,2);
            entity.Property(x => x.CarryForwardMax).HasPrecision(6,2);
            entity.Property(x => x.EncashmentMaxDays).HasPrecision(6,2);
            entity.Property(x => x.MinimumDaysPerRequest).HasPrecision(5,2);
            entity.Property(x => x.MaximumDaysPerRequest).HasPrecision(5,2);
            entity.HasIndex(x => new { x.TenantId, x.LeaveTypeId, x.Status });
        });
        modelBuilder.Entity<EmployeeLeaveBalance>(entity => {
            entity.ToTable("employee_leave_balances");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Entitled).HasPrecision(7,2);
            entity.Property(x => x.Accrued).HasPrecision(7,2);
            entity.Property(x => x.Used).HasPrecision(7,2);
            entity.Property(x => x.Pending).HasPrecision(7,2);
            entity.Property(x => x.CarriedForward).HasPrecision(7,2);
            entity.Property(x => x.Encashed).HasPrecision(7,2);
            entity.Property(x => x.Expired).HasPrecision(7,2);
            entity.Property(x => x.ManualAdjustment).HasPrecision(7,2);
            entity.Ignore(x => x.Available);
            entity.HasIndex(x => new { x.TenantId, x.EmployeeId, x.LeaveTypeId, x.Year }).IsUnique();
        });
        modelBuilder.Entity<LeaveBalanceTransaction>(entity => {
            entity.ToTable("leave_balance_transactions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Amount).HasPrecision(7,2);
            entity.Property(x => x.BalanceBefore).HasPrecision(7,2);
            entity.Property(x => x.BalanceAfter).HasPrecision(7,2);
            entity.HasIndex(x => new { x.TenantId, x.EmployeeId, x.LeaveTypeId });
        });
        modelBuilder.Entity<LeaveRequest>(entity => {
            entity.ToTable("leave_requests");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TotalDays).HasPrecision(6,2);
            entity.Property(x => x.HoursRequested).HasPrecision(5,2);
            entity.HasIndex(x => new { x.TenantId, x.Status });
            entity.HasIndex(x => new { x.TenantId, x.EmployeeId, x.StartDate });
        });
        modelBuilder.Entity<LeaveApproval>(entity => { entity.ToTable("leave_approvals"); entity.HasKey(x => x.Id); entity.HasIndex(x => new { x.TenantId, x.LeaveRequestId }); });
        modelBuilder.Entity<LeaveCancellationRequest>(entity => { entity.ToTable("leave_cancellation_requests"); entity.HasKey(x => x.Id); });
        modelBuilder.Entity<LeaveModificationRequest>(entity => {
            entity.ToTable("leave_modification_requests"); entity.HasKey(x => x.Id);
            entity.Property(x => x.NewTotalDays).HasPrecision(6,2);
        });
        modelBuilder.Entity<PublicHolidayCalendar>(entity => { entity.ToTable("public_holiday_calendars"); entity.HasKey(x => x.Id); entity.HasIndex(x => new { x.TenantId, x.CountryCode, x.CalendarYear }); });
        modelBuilder.Entity<PublicHoliday>(entity => { entity.ToTable("public_holidays"); entity.HasKey(x => x.Id); entity.HasIndex(x => new { x.TenantId, x.CalendarId, x.Date }); });
        modelBuilder.Entity<LeaveBlackoutDate>(entity => { entity.ToTable("leave_blackout_dates"); entity.HasKey(x => x.Id); entity.HasIndex(x => new { x.TenantId, x.StartDate }); });
        modelBuilder.Entity<LeaveEncashmentRequest>(entity => {
            entity.ToTable("leave_encashment_requests"); entity.HasKey(x => x.Id);
            entity.Property(x => x.DaysToEncash).HasPrecision(6,2);
            entity.Property(x => x.AmountPerDay).HasPrecision(10,2);
            entity.Property(x => x.TotalAmount).HasPrecision(12,2);
            entity.HasIndex(x => new { x.TenantId, x.Status });
        });
        modelBuilder.Entity<CompOffCredit>(entity => {
            entity.ToTable("comp_off_credits"); entity.HasKey(x => x.Id);
            entity.Property(x => x.HoursWorked).HasPrecision(5,2);
            entity.Property(x => x.DaysEarned).HasPrecision(5,2);
            entity.HasIndex(x => new { x.TenantId, x.EmployeeId, x.Status });
        });
        modelBuilder.Entity<CompOffUsage>(entity => { entity.ToTable("comp_off_usages"); entity.HasKey(x => x.Id); entity.Property(x => x.DaysUsed).HasPrecision(5,2); });
        modelBuilder.Entity<AbsenceRecord>(entity => { entity.ToTable("absence_records"); entity.HasKey(x => x.Id); entity.HasIndex(x => new { x.TenantId, x.EmployeeId, x.AbsenceDate }); });
        modelBuilder.Entity<AbsenceRegularizationRequest>(entity => { entity.ToTable("absence_regularization_requests"); entity.HasKey(x => x.Id); entity.HasIndex(x => new { x.TenantId, x.Status }); });
        modelBuilder.Entity<LeaveDelegation>(entity => { entity.ToTable("leave_delegations"); entity.HasKey(x => x.Id); entity.HasIndex(x => new { x.TenantId, x.EmployeeId, x.Status }); });
        modelBuilder.Entity<LeavePayrollImpact>(entity => {
            entity.ToTable("leave_payroll_impacts"); entity.HasKey(x => x.Id);
            entity.Property(x => x.Days).HasPrecision(6,2);
            entity.Property(x => x.Amount).HasPrecision(12,2);
            entity.HasIndex(x => new { x.TenantId, x.Status });
        });
        modelBuilder.Entity<LeaveAuditLog>(entity => { entity.ToTable("leave_audit_logs"); entity.HasKey(x => x.Id); entity.HasIndex(x => new { x.TenantId, x.EntityType, x.EntityId }); });
        modelBuilder.Entity<LeaveAIInsight>(entity => { entity.ToTable("leave_ai_insights"); entity.HasKey(x => x.Id); entity.HasIndex(x => new { x.TenantId, x.InsightType, x.IsAcknowledged }); });

        modelBuilder.Entity<PayrollRun>(entity =>
        {
            entity.ToTable("payroll_runs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TotalGrossSalary).HasPrecision(14, 2);
            entity.Property(x => x.TotalDeductions).HasPrecision(14, 2);
            entity.Property(x => x.TotalNetSalary).HasPrecision(14, 2);
            entity.HasIndex(x => new { x.TenantId, x.Year, x.Month }).IsUnique();
        });

        modelBuilder.Entity<PayrollSlip>(entity =>
        {
            entity.ToTable("payroll_slips");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.BasicSalary).HasPrecision(12, 2);
            entity.Property(x => x.HousingAllowance).HasPrecision(12, 2);
            entity.Property(x => x.TransportAllowance).HasPrecision(12, 2);
            entity.Property(x => x.OtherAllowances).HasPrecision(12, 2);
            entity.Property(x => x.GrossSalary).HasPrecision(12, 2);
            entity.Property(x => x.Deductions).HasPrecision(12, 2);
            entity.Property(x => x.NetSalary).HasPrecision(12, 2);
            entity.HasIndex(x => new { x.TenantId, x.RunId, x.EmployeeId }).IsUnique();
        });

        modelBuilder.Entity<ManpowerRequisition>(entity =>
        {
            entity.ToTable("manpower_requisitions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.BudgetFrom).HasPrecision(12, 2);
            entity.Property(x => x.BudgetTo).HasPrecision(12, 2);
            entity.HasIndex(x => new { x.TenantId, x.RequisitionNumber }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.Status });
        });

        modelBuilder.Entity<JobOpening>(entity =>
        {
            entity.ToTable("job_openings");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.SalaryFrom).HasPrecision(12, 2);
            entity.Property(x => x.SalaryTo).HasPrecision(12, 2);
            entity.HasIndex(x => new { x.TenantId, x.JobCode }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.Status });
        });

        modelBuilder.Entity<Candidate>(entity =>
        {
            entity.ToTable("candidates");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TotalExperienceYears).HasPrecision(5, 1);
            entity.HasIndex(x => new { x.TenantId, x.Email }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.Status });
        });

        modelBuilder.Entity<JobApplication>(entity =>
        {
            entity.ToTable("job_applications");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.OfferedSalary).HasPrecision(12, 2);
            entity.HasIndex(x => new { x.TenantId, x.JobOpeningId, x.CandidateId }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.JobOpeningId, x.Stage });
        });

        modelBuilder.Entity<ApplicationEvent>(entity =>
        {
            entity.ToTable("application_events");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.ApplicationId, x.CreatedAtUtc });
        });

        modelBuilder.Entity<InterviewSchedule>(entity =>
        {
            entity.ToTable("interview_schedules");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.ApplicationId });
        });

        modelBuilder.Entity<OfferLetter>(entity =>
        {
            entity.ToTable("offer_letters");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.BasicSalary).HasPrecision(12, 2);
            entity.Property(x => x.HousingAllowance).HasPrecision(12, 2);
            entity.Property(x => x.TransportAllowance).HasPrecision(12, 2);
            entity.Property(x => x.OtherAllowances).HasPrecision(12, 2);
            entity.Property(x => x.GrossSalary).HasPrecision(12, 2);
            entity.Property(x => x.ContentHtml).HasColumnType("longtext");
            entity.HasIndex(x => new { x.TenantId, x.ApplicationId });
        });

        modelBuilder.Entity<ShiftDefinition>(entity =>
        {
            entity.ToTable("shift_definitions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Code).HasMaxLength(20).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Color).HasMaxLength(20);
            entity.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        });

        modelBuilder.Entity<ShiftAssignment>(entity =>
        {
            entity.ToTable("shift_assignments");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.EmployeeId, x.AssignedDate }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.AssignedDate });
        });

        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.ToTable("tenants");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Slug).HasMaxLength(80).IsRequired();
            entity.HasIndex(x => x.Slug).IsUnique();
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Email).HasMaxLength(256).IsRequired();
            entity.Property(x => x.NormalizedEmail).HasMaxLength(256).IsRequired();
            entity.Property(x => x.FullName).HasMaxLength(180).IsRequired();
            entity.Property(x => x.PasswordHash).HasMaxLength(512).IsRequired();
            entity.HasIndex(x => new { x.TenantId, x.NormalizedEmail }).IsUnique();
            entity.HasOne(x => x.Tenant).WithMany(x => x.Users).HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.ToTable("roles");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(80).IsRequired();
            entity.Property(x => x.NormalizedName).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(240).IsRequired();
            entity.HasIndex(x => new { x.TenantId, x.NormalizedName }).IsUnique();
        });

        modelBuilder.Entity<Permission>(entity =>
        {
            entity.ToTable("permissions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Key).HasColumnName("permission_key").HasMaxLength(120).IsRequired();
            entity.Property(x => x.Module).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(240).IsRequired();
            entity.HasIndex(x => x.Key).IsUnique();
        });

        modelBuilder.Entity<UserRole>(entity =>
        {
            entity.ToTable("user_roles");
            entity.HasKey(x => new { x.UserId, x.RoleId });
            entity.HasOne(x => x.User).WithMany(x => x.UserRoles).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Role).WithMany(x => x.UserRoles).HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RolePermission>(entity =>
        {
            entity.ToTable("role_permissions");
            entity.HasKey(x => new { x.RoleId, x.PermissionId });
            entity.HasOne(x => x.Role).WithMany(x => x.RolePermissions).HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Permission).WithMany(x => x.RolePermissions).HasForeignKey(x => x.PermissionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("refresh_tokens");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
            entity.Property(x => x.ReplacedByTokenHash).HasMaxLength(128);
            entity.Property(x => x.CreatedByIp).HasMaxLength(64);
            entity.Property(x => x.RevokedByIp).HasMaxLength(64);
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasOne(x => x.User).WithMany(x => x.RefreshTokens).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PasswordResetToken>(entity =>
        {
            entity.ToTable("password_reset_tokens");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
            entity.Property(x => x.CreatedByIp).HasMaxLength(64);
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasOne(x => x.User).WithMany(x => x.PasswordResetTokens).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("audit_logs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Action).HasMaxLength(120).IsRequired();
            entity.Property(x => x.EntityName).HasMaxLength(120).IsRequired();
            entity.Property(x => x.EntityId).HasMaxLength(80);
            entity.Property(x => x.IpAddress).HasMaxLength(64);
            entity.Property(x => x.UserAgent).HasMaxLength(512);
            entity.Property(x => x.Metadata).HasColumnType("json");
            entity.HasIndex(x => new { x.TenantId, x.CreatedAtUtc });
            entity.HasIndex(x => new { x.UserId, x.CreatedAtUtc });
        });

        // ── Performance & Appraisals ───────────────────────────────────────────

        modelBuilder.Entity<PerformanceCycle>(entity =>
        {
            entity.ToTable("performance_cycles");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.Status });
        });

        modelBuilder.Entity<PerformanceScorecardTemplate>(entity =>
        {
            entity.ToTable("performance_scorecard_templates");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.KpiWeight).HasPrecision(5, 2);
            entity.Property(x => x.CompetencyWeight).HasPrecision(5, 2);
            entity.Property(x => x.AttendanceWeight).HasPrecision(5, 2);
            entity.Property(x => x.ProductivityWeight).HasPrecision(5, 2);
            entity.Property(x => x.FeedbackWeight).HasPrecision(5, 2);
            entity.Property(x => x.DisciplineWeight).HasPrecision(5, 2);
            entity.Property(x => x.MinPassingScore).HasPrecision(5, 2);
            entity.HasIndex(x => new { x.TenantId, x.IsActive });
        });

        modelBuilder.Entity<PerformanceRatingScale>(entity =>
        {
            entity.ToTable("performance_rating_scales");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.IsDefault });
        });

        modelBuilder.Entity<PerformanceRatingOption>(entity =>
        {
            entity.ToTable("performance_rating_options");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.MinScore).HasPrecision(5, 2);
            entity.Property(x => x.MaxScore).HasPrecision(5, 2);
            entity.HasIndex(x => new { x.TenantId, x.ScaleId });
        });

        modelBuilder.Entity<PerformanceCycleEmployee>(entity =>
        {
            entity.ToTable("performance_cycle_employees");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.CycleId, x.EmployeeId }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.CycleId, x.Status });
        });

        modelBuilder.Entity<Competency>(entity =>
        {
            entity.ToTable("competencies");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.Category, x.IsActive });
        });

        modelBuilder.Entity<RoleCompetency>(entity =>
        {
            entity.ToTable("role_competencies");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Weight).HasPrecision(5, 2);
            entity.HasIndex(x => new { x.TenantId, x.DepartmentName });
        });

        modelBuilder.Entity<EmployeeGoal>(entity =>
        {
            entity.ToTable("employee_goals");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TargetValue).HasPrecision(14, 4);
            entity.Property(x => x.ActualValue).HasPrecision(14, 4);
            entity.Property(x => x.Weight).HasPrecision(5, 2);
            entity.Property(x => x.AchievementPct).HasPrecision(5, 2);
            entity.HasIndex(x => new { x.TenantId, x.EmployeeId, x.Status });
            entity.HasIndex(x => new { x.TenantId, x.CycleId });
        });

        modelBuilder.Entity<GoalProgressUpdate>(entity =>
        {
            entity.ToTable("goal_progress_updates");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.UpdatedValue).HasPrecision(14, 4);
            entity.HasIndex(x => new { x.TenantId, x.GoalId });
        });

        modelBuilder.Entity<AppraisalReview>(entity =>
        {
            entity.ToTable("appraisal_reviews");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.KpiScore).HasPrecision(5, 2);
            entity.Property(x => x.CompetencyScore).HasPrecision(5, 2);
            entity.Property(x => x.AttendanceScore).HasPrecision(5, 2);
            entity.Property(x => x.ProductivityScore).HasPrecision(5, 2);
            entity.Property(x => x.FeedbackScore).HasPrecision(5, 2);
            entity.Property(x => x.DisciplineScore).HasPrecision(5, 2);
            entity.Property(x => x.FinalScore).HasPrecision(5, 2);
            entity.Property(x => x.CalibrationAdjustment).HasPrecision(5, 2);
            entity.HasIndex(x => new { x.TenantId, x.CycleId, x.EmployeeId }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.Status });
            entity.HasIndex(x => new { x.TenantId, x.DepartmentName });
        });

        modelBuilder.Entity<AppraisalScoreBreakdown>(entity =>
        {
            entity.ToTable("appraisal_score_breakdowns");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.RawScore).HasPrecision(5, 2);
            entity.Property(x => x.Weight).HasPrecision(5, 2);
            entity.Property(x => x.WeightedScore).HasPrecision(5, 2);
            entity.HasIndex(x => new { x.TenantId, x.ReviewId });
        });

        modelBuilder.Entity<AppraisalCompetencyRating>(entity =>
        {
            entity.ToTable("appraisal_competency_ratings");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.SelfRating).HasPrecision(4, 2);
            entity.Property(x => x.ManagerRating).HasPrecision(4, 2);
            entity.Property(x => x.Weight).HasPrecision(5, 2);
            entity.HasIndex(x => new { x.TenantId, x.ReviewId, x.CompetencyId }).IsUnique();
        });

        modelBuilder.Entity<Feedback360>(entity =>
        {
            entity.ToTable("feedback_360");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Score).HasPrecision(4, 2);
            entity.HasIndex(x => new { x.TenantId, x.ReviewId });
        });

        modelBuilder.Entity<AppraisalCalibration>(entity =>
        {
            entity.ToTable("appraisal_calibrations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.OriginalScore).HasPrecision(5, 2);
            entity.Property(x => x.AdjustedScore).HasPrecision(5, 2);
            entity.HasIndex(x => new { x.TenantId, x.CycleId });
            entity.HasIndex(x => new { x.TenantId, x.ReviewId });
        });

        modelBuilder.Entity<AppraisalAppeal>(entity =>
        {
            entity.ToTable("appraisal_appeals");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.ReviewId });
            entity.HasIndex(x => new { x.TenantId, x.Status });
        });

        modelBuilder.Entity<IncrementRecommendation>(entity =>
        {
            entity.ToTable("increment_recommendations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.CurrentSalary).HasPrecision(14, 2);
            entity.Property(x => x.RecommendedIncrementPct).HasPrecision(5, 2);
            entity.Property(x => x.RecommendedIncrementAmount).HasPrecision(14, 2);
            entity.Property(x => x.NewSalary).HasPrecision(14, 2);
            entity.HasIndex(x => new { x.TenantId, x.Status });
        });

        modelBuilder.Entity<PromotionRecommendation>(entity =>
        {
            entity.ToTable("promotion_recommendations");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.Status });
        });

        modelBuilder.Entity<BonusRecommendation>(entity =>
        {
            entity.ToTable("bonus_recommendations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.BonusAmount).HasPrecision(14, 2);
            entity.HasIndex(x => new { x.TenantId, x.Status });
        });

        modelBuilder.Entity<PerformanceImprovementPlan>(entity =>
        {
            entity.ToTable("performance_improvement_plans");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.EmployeeId, x.Status });
        });

        modelBuilder.Entity<PIPCheckIn>(entity =>
        {
            entity.ToTable("pip_check_ins");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.PipId });
        });

        modelBuilder.Entity<ProbationReview>(entity =>
        {
            entity.ToTable("probation_reviews");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.OverallRating).HasPrecision(4, 2);
            entity.HasIndex(x => new { x.TenantId, x.EmployeeId, x.Status });
        });

        modelBuilder.Entity<ContinuousFeedback>(entity =>
        {
            entity.ToTable("continuous_feedback");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.EmployeeId });
            entity.HasIndex(x => new { x.TenantId, x.FeedbackType });
        });

        modelBuilder.Entity<PerformanceAuditLog>(entity =>
        {
            entity.ToTable("performance_audit_logs");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.EntityType, x.EntityId });
            entity.HasIndex(x => new { x.TenantId, x.CreatedAtUtc });
        });
    }

    private static void ApplySnakeCaseColumns(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }
        }
    }

    private static string ToSnakeCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        var builder = new StringBuilder(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (char.IsUpper(character) && index > 0) builder.Append('_');
            builder.Append(char.ToLowerInvariant(character));
        }
        return builder.ToString();
    }
}
