using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zayra.Api.Application.Auth;
using Zayra.Api.Application.Common;
using Zayra.Api.Application.Employees;
using Zayra.Api.Data;
using Zayra.Api.Domain.Entities;
using Zayra.Api.Infrastructure.Auth;
using Zayra.Api.Infrastructure.Notifications;
using Zayra.Api.Infrastructure.Localization;
using Zayra.Api.Infrastructure.Documents;
using Zayra.Api.Infrastructure.Documents.Letters;
using Zayra.Api.Models;

namespace Zayra.Api.Controllers;

[ApiController]
[Route("api/employees")]
[Authorize]
public class EmployeesController : ControllerBase
{
    private static readonly HashSet<string> SensitiveFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "salary", "bankName", "bankIban", "wpsBankDetails", "passportNumber", "passportExpiryDate", "visaNumber",
        "dateOfBirth", "salary", "bankName", "bankIban", "wpsBankDetails", "passportNumber", "passportIssueDate",
        "passportExpiryDate", "visaNumber", "visaIssueDate", "visaExpiryDate", "iqamaNumber", "muqeemNumber",
        "gosiReference", "emiratesId", "laborCardNumber", "visaFileNumber", "qid", "civilId", "residencyNumber",
        "residencyIssueDate", "workPermitNumber", "workPermitIssueDate", "medicalInformation", "disciplinaryRecords",
        "terminationReason"
    };

    private readonly ZayraDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IAuditService _audit;
    private readonly IDocumentStorage _documents;
    private readonly INotificationService _notifications;
    private readonly IHijriDateService _hijri;
    private readonly IDataScopeService _scopeService;
    private readonly ILetterService _letters;

    public EmployeesController(ZayraDbContext db, IPasswordHasher passwordHasher, IAuditService audit, IDocumentStorage documents, INotificationService notifications, IHijriDateService hijri, IDataScopeService scopeService, ILetterService letters)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _audit = audit;
        _documents = documents;
        _notifications = notifications;
        _hijri = hijri;
        _scopeService = scopeService;
        _letters = letters;
    }

    [HttpGet]
    [Authorize(Roles = "Admin,HR Manager,HR Officer,Payroll Officer,Manager,Auditor")]
    public async Task<ActionResult<PagedResult<EmployeeListItemDto>>> Search([FromServices] IEmployeeManagementService employeeManagement, [FromQuery] string? search, [FromQuery] string? status, [FromQuery] string? department, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        var scope = await _scopeService.ResolveAsync(User, tenantId, cancellationToken);
        if (scope.IsUnrestricted)
            return Ok(await employeeManagement.SearchAsync(tenantId, search, status, department, page, pageSize, cancellationToken));

        // Restricted scope: query directly and apply AllowedEmployeeIds filter
        var query = _db.Employees.Where(e => (e.TenantId == tenantId || e.TenantId == null) && !e.IsDeleted
            && scope.AllowedEmployeeIds!.Contains(e.Id));
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(e => e.FullName.Contains(search) || e.EmployeeCode.Contains(search) || (e.WorkEmail != null && e.WorkEmail.Contains(search)));
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(e => e.Status == status);
        if (!string.IsNullOrWhiteSpace(department)) query = query.Where(e => e.Department == department);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(e => e.FullName).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(e => new EmployeeListItemDto(e.Id, e.EmployeeCode, e.FullName, e.ArabicName ?? string.Empty, e.Department ?? string.Empty, e.Designation ?? string.Empty, e.Branch ?? string.Empty, e.ManagerEmployeeId, e.Status, e.ProfileCompletenessScore, e.VisaExpiryDate, e.PassportExpiryDate, e.IqamaNumber ?? string.Empty))
            .ToListAsync(cancellationToken);
        return Ok(new PagedResult<EmployeeListItemDto>(items, total, page, pageSize));
    }

    // ── Configurable export / import / shareable template ────────────────────────
    private static readonly string[] EmployeeCsvHeaders =
        { "EmployeeCode", "FullName", "ArabicName", "WorkEmail", "Phone", "Gender", "Nationality", "Department", "Designation", "JobTitle", "EmploymentType", "ContractType", "Status", "JoiningDate" };

    [HttpGet("export")]
    [Authorize(Roles = "Admin,HR Manager,HR Officer,Payroll Officer,Auditor")]
    public async Task<IActionResult> Export(CancellationToken ct)
    {
        var tenantId = RequireTenant();
        var emps = await _db.Employees.Where(e => (e.TenantId == tenantId || e.TenantId == null) && !e.IsDeleted)
            .OrderBy(e => e.EmployeeCode).ToListAsync(ct);
        var rows = emps.Select(e => (IReadOnlyList<object?>)new object?[]
        {
            e.EmployeeCode, e.FullName, e.ArabicName, e.WorkEmail, e.Phone, e.Gender, e.Nationality,
            e.Department, e.Designation, e.JobTitle, e.EmploymentType, e.ContractType, e.Status,
            e.JoiningDate.ToString("yyyy-MM-dd")
        });
        var csv = Csv.Build(EmployeeCsvHeaders, rows);
        return File(Encoding.UTF8.GetBytes(csv), "text/csv", $"employees_{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    /// <summary>Downloadable blank template — the shareable "data format" to fill and import.</summary>
    [HttpGet("import-template")]
    [Authorize(Roles = "Admin,HR Manager,HR Officer")]
    public IActionResult ImportTemplate() =>
        File(Encoding.UTF8.GetBytes(Csv.Template(EmployeeCsvHeaders)), "text/csv", "employees_import_template.csv");

    [HttpPost("import")]
    [Authorize(Roles = "Admin,HR Manager,HR Officer")]
    public async Task<IActionResult> Import([FromBody] ImportEmployeesRequest req, CancellationToken ct)
    {
        var tenantId = RequireTenant();
        var rows = Csv.Parse(req.CsvContent ?? string.Empty);
        var company = await _db.Companies.FirstOrDefaultAsync(c => c.TenantId == tenantId, ct);
        var branch = await _db.Branches.FirstOrDefaultAsync(b => b.TenantId == tenantId, ct);
        int created = 0, skipped = 0;
        var errors = new List<string>();
        var rowNum = 1;
        foreach (var row in rows)
        {
            rowNum++;
            var name = row.GetValueOrDefault("FullName", string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name)) { skipped++; continue; }
            var code = row.GetValueOrDefault("EmployeeCode", string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(code) && await _db.Employees.AnyAsync(e => e.TenantId == tenantId && e.EmployeeCode == code, ct))
            { skipped++; errors.Add($"Row {rowNum}: EmployeeCode '{code}' already exists."); continue; }
            DateTime.TryParse(row.GetValueOrDefault("JoiningDate", string.Empty), out var jd);
            var statusVal = row.GetValueOrDefault("Status", string.Empty).Trim();
            _db.Employees.Add(new Employee
            {
                TenantId = tenantId,
                CompanyId = company?.Id,
                BranchId = branch?.Id,
                EmployeeCode = string.IsNullOrWhiteSpace(code) ? $"IMP-{Guid.NewGuid().ToString()[..6].ToUpperInvariant()}" : code,
                FullName = name,
                EnglishName = name,
                ArabicName = row.GetValueOrDefault("ArabicName", string.Empty),
                WorkEmail = row.GetValueOrDefault("WorkEmail", string.Empty),
                Phone = row.GetValueOrDefault("Phone", string.Empty),
                Gender = row.GetValueOrDefault("Gender", string.Empty),
                Nationality = row.GetValueOrDefault("Nationality", string.Empty),
                Department = row.GetValueOrDefault("Department", string.Empty),
                Designation = row.GetValueOrDefault("Designation", string.Empty),
                JobTitle = row.GetValueOrDefault("JobTitle", row.GetValueOrDefault("Designation", string.Empty)),
                EmploymentType = row.GetValueOrDefault("EmploymentType", "Full-time"),
                ContractType = row.GetValueOrDefault("ContractType", string.Empty),
                Status = string.IsNullOrWhiteSpace(statusVal) ? "Active" : statusVal,
                JoiningDate = jd == default ? DateTime.UtcNow : jd,
            });
            created++;
        }
        await _db.SaveChangesAsync(ct);
        return Ok(new { received = rows.Count, created, skipped, errors = errors.Take(20) });
    }

    public record ImportEmployeesRequest(string CsvContent);

    [HttpGet("{id:int}")]
    public async Task<ActionResult<EmployeeDetailDto>> Get(int id, [FromServices] IEmployeeManagementService employeeManagement, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();
        var scope = await _scopeService.ResolveAsync(User, tenantId, cancellationToken);
        if (!scope.IsUnrestricted && !scope.AllowedEmployeeIds!.Contains(id))
            return Forbid();
        var employee = await employeeManagement.GetAsync(tenantId, id, CanViewSensitive(), Context(), cancellationToken);
        return employee is null ? NotFound() : Ok(employee);
    }

    [HttpPost]
    [Authorize(Roles = "Admin,HR Manager,HR Officer")]
    public async Task<ActionResult<EmployeeDetailDto>> CreateEmployee(EmployeeCreateRequest request, [FromServices] IEmployeeManagementService employeeManagement, CancellationToken cancellationToken)
    {
        try
        {
            var employee = await employeeManagement.CreateAsync(RequireTenant(), request, Context(), cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = employee.Employee.Id }, employee);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("drafts")]
    [Authorize(Roles = "Admin,HR Manager,HR Officer")]
    public async Task<ActionResult<EmployeeDraft>> CreateDraft(EmployeeDraftRequest request, CancellationToken cancellationToken)
    {
        var draft = ApplyDraft(new EmployeeDraft { TenantId = RequireTenant(), CreatedByUserId = GetUserId() }, request);
        draft.ProfileCompletenessScore = CalculateCompleteness(draft, 0);
        _db.EmployeeDrafts.Add(draft);
        await _db.SaveChangesAsync(cancellationToken);
        await Audit("employee.draft_created", "EmployeeDraft", draft.Id.ToString(), cancellationToken);
        return Created($"/api/employees/drafts/{draft.Id}", draft);
    }

    [HttpPut("drafts/{draftId:guid}")]
    [Authorize(Roles = "Admin,HR Manager,HR Officer")]
    public async Task<ActionResult<EmployeeDraft>> UpdateDraft(Guid draftId, EmployeeDraftRequest request, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();
        var draft = await _db.EmployeeDrafts.FirstOrDefaultAsync(x => x.Id == draftId && x.TenantId == tenantId, cancellationToken);
        if (draft is null) return NotFound();
        ApplyDraft(draft, request);
        var docs = await _db.EmployeeDocuments.CountAsync(x => x.TenantId == tenantId && x.DraftId == draftId, cancellationToken);
        draft.ProfileCompletenessScore = CalculateCompleteness(draft, docs);
        await _db.SaveChangesAsync(cancellationToken);
        await Audit("employee.draft_updated", "EmployeeDraft", draft.Id.ToString(), cancellationToken);
        return Ok(draft);
    }

    [HttpPost("drafts/{draftId:guid}/documents")]
    [Authorize(Roles = "Admin,HR Manager,HR Officer")]
    public async Task<ActionResult<EmployeeDocument>> AddDraftDocument(Guid draftId, EmployeeDocumentRequest request, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();
        if (!await _db.EmployeeDrafts.AnyAsync(x => x.Id == draftId && x.TenantId == tenantId, cancellationToken)) return NotFound();
        var document = new EmployeeDocument
        {
            TenantId = tenantId,
            DraftId = draftId,
            DocumentType = request.DocumentType.Trim(),
            FileName = request.FileName.Trim(),
            ContentType = request.ContentType.Trim(),
            StorageUrl = request.StorageUrl.Trim(),
            IsRequired = request.IsRequired,
            ExpiryDate = request.ExpiryDate
        };
        _db.EmployeeDocuments.Add(document);
        await _db.SaveChangesAsync(cancellationToken);
        await Audit("employee.document_uploaded", "EmployeeDraft", draftId.ToString(), cancellationToken);
        return Created($"/api/employees/documents/{document.Id}", document);
    }

    [HttpPost("drafts/{draftId:guid}/documents/upload")]
    [Authorize(Roles = "Admin,HR Manager,HR Officer")]
    [RequestSizeLimit(10_485_760)]
    public async Task<ActionResult<EmployeeDocument>> UploadDraftDocument(Guid draftId, [FromForm] EmployeeDocumentUploadRequest request, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();
        if (request.File is null) return BadRequest(new { message = "Document file is required." });
        if (!await _db.EmployeeDrafts.AnyAsync(x => x.Id == draftId && x.TenantId == tenantId, cancellationToken)) return NotFound();
        var stored = await _documents.SaveAsync(tenantId, request.File, cancellationToken);
        var document = new EmployeeDocument
        {
            TenantId = tenantId,
            DraftId = draftId,
            DocumentType = request.DocumentType.Trim(),
            FileName = stored.FileName,
            ContentType = stored.ContentType,
            StorageUrl = stored.StorageUrl,
            IsRequired = request.IsRequired,
            ExpiryDate = request.ExpiryDate
        };
        _db.EmployeeDocuments.Add(document);
        await _db.SaveChangesAsync(cancellationToken);
        await Notify("Document uploaded", $"{request.DocumentType} was uploaded for draft {draftId}.", "EmployeeDraft", draftId.ToString(), cancellationToken);
        await Audit("employee.document_file_uploaded", "EmployeeDraft", draftId.ToString(), cancellationToken);
        return Created($"/api/employees/documents/{document.Id}", document);
    }

    [HttpGet("documents/{documentId:guid}/download")]
    public async Task<IActionResult> DownloadDocument(Guid documentId, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();
        var document = await _db.EmployeeDocuments.FirstOrDefaultAsync(x => x.Id == documentId && x.TenantId == tenantId, cancellationToken);
        if (document is null) return NotFound();
        var path = _documents.ResolvePath(document.StorageUrl);
        if (!System.IO.File.Exists(path)) return NotFound(new { message = "Stored document file was not found." });
        return PhysicalFile(path, document.ContentType, document.FileName);
    }

    [HttpPost("drafts/{draftId:guid}/submit")]
    [Authorize(Roles = "Admin,HR Manager,HR Officer")]
    public async Task<IActionResult> SubmitDraft(Guid draftId, CancellationToken cancellationToken)
    {
        var draft = await FindDraft(draftId, cancellationToken);
        if (draft is null) return NotFound();
        draft.Status = "PendingHrApproval";
        draft.CurrentStep = "HrApproval";
        draft.SubmittedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await Notify("Employee draft submitted", "A draft is waiting for HR approval.", "EmployeeDraft", draftId.ToString(), cancellationToken);
        await Audit("employee.draft_submitted", "EmployeeDraft", draftId.ToString(), cancellationToken);
        return NoContent();
    }

    [HttpPost("drafts/{draftId:guid}/approve")]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<ActionResult<EmployeeProfileDto>> ApproveDraft(Guid draftId, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();
        var draft = await FindDraft(draftId, cancellationToken);
        if (draft is null) return NotFound();
        if (draft.Status != "PendingHrApproval" && draft.Status != "Draft") return BadRequest(new { message = "Draft is not ready for HR approval." });

        var employee = new Employee
        {
            TenantId = tenantId,
            EmployeeCode = await GenerateEmployeeCode(tenantId, cancellationToken),
            FullName = FirstNonEmpty(draft.EnglishName, draft.ArabicName),
            EnglishName = draft.EnglishName,
            ArabicName = draft.ArabicName,
            PersonalEmail = draft.PersonalEmail,
            WorkEmail = draft.WorkEmail,
            Phone = draft.Phone,
            Gender = draft.Gender,
            DateOfBirth = draft.DateOfBirth,
            MaritalStatus = draft.MaritalStatus,
            EmergencyContactName = draft.EmergencyContactName,
            EmergencyContactPhone = draft.EmergencyContactPhone,
            Nationality = draft.Nationality,
            CountryCode = draft.CountryCode,
            Department = draft.Department,
            Designation = draft.Designation,
            WorkLocation = draft.WorkLocation,
            Branch = draft.Branch,
            ManagerEmployeeId = draft.ManagerEmployeeId,
            Status = "Active",
            JoiningDate = draft.JoiningDate ?? DateTime.UtcNow.Date,
            ContractType = draft.ContractType,
            Grade = draft.Grade,
            CostCenter = draft.CostCenter,
            ContractStartDate = draft.ContractStartDate,
            ContractEndDate = draft.ContractEndDate,
            ProbationEndDate = draft.ProbationEndDate,
            PayrollProfileCode = draft.PayrollProfileCode,
            Salary = draft.Salary,
            BankName = draft.BankName,
            BankIban = draft.BankIban,
            WpsBankDetails = draft.WpsBankDetails,
            ShiftPolicyCode = draft.ShiftPolicyCode,
            LeavePolicyCode = draft.LeavePolicyCode,
            SponsorName = draft.SponsorName,
            PassportIssueDate = draft.PassportIssueDate,
            PassportNumber = draft.PassportNumber,
            PassportExpiryDate = draft.PassportExpiryDate,
            VisaIssueDate = draft.VisaIssueDate,
            VisaNumber = draft.VisaNumber,
            VisaExpiryDate = draft.VisaExpiryDate,
            ResidencyIssueDate = draft.ResidencyIssueDate,
            WorkPermitIssueDate = draft.WorkPermitIssueDate,
            IqamaNumber = draft.IqamaNumber,
            MuqeemNumber = draft.MuqeemNumber,
            GosiReference = draft.GosiReference,
            QiwaContractNumber = draft.QiwaContractNumber,
            EmiratesId = draft.EmiratesId,
            LaborCardNumber = draft.LaborCardNumber,
            VisaFileNumber = draft.VisaFileNumber,
            Qid = draft.Qid,
            WorkPermitNumber = draft.WorkPermitNumber,
            CivilId = draft.CivilId,
            ResidencyNumber = draft.ResidencyNumber,
            ProfileCompletenessScore = draft.ProfileCompletenessScore,
            ActivatedAtUtc = DateTime.UtcNow
        };
        _db.Employees.Add(employee);
        await _db.SaveChangesAsync(cancellationToken);

        var draftDocuments = await _db.EmployeeDocuments.Where(x => x.TenantId == tenantId && x.DraftId == draftId).ToListAsync(cancellationToken);
        foreach (var document in draftDocuments) document.EmployeeId = employee.Id;
        employee.UserAccountId = await CreateEmployeeUserAccount(employee, cancellationToken);
        draft.Status = "Activated";
        draft.CurrentStep = "Activated";
        draft.ApprovedAtUtc = DateTime.UtcNow;
        draft.ActivatedAtUtc = DateTime.UtcNow;
        await AddHistory(employee, "Activated", DateOnly.FromDateTime(employee.JoiningDate), cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        await Notify("Employee activated", $"{employee.FullName} was activated with ID {employee.EmployeeCode}.", "Employee", employee.Id.ToString(), cancellationToken);
        await Audit("employee.activated", "Employee", employee.Id.ToString(), cancellationToken);
        return Ok(new EmployeeProfileDto(employee, await _db.EmployeeDocuments.Where(x => x.EmployeeId == employee.Id).ToListAsync(cancellationToken), await _db.EmployeeHistories.Where(x => x.EmployeeId == employee.Id).ToListAsync(cancellationToken)));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin,HR Manager,HR Officer,Payroll Officer")]
    public async Task<IActionResult> UpdateEmployee(int id, EmployeeUpdateRequest request, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();
        var employee = await _db.Employees.FirstOrDefaultAsync(x => x.Id == id && (x.TenantId == tenantId || x.TenantId == null), cancellationToken);
        if (employee is null) return NotFound();
        var sensitive = request.Changes.Keys.Where(SensitiveFields.Contains).ToList();
        if (sensitive.Count > 0)
        {
            if (!CanEditSensitive()) return Forbid();
            var change = new EmployeeChangeRequest
            {
                TenantId = tenantId,
                EmployeeId = employee.Id,
                RequestedByUserId = GetUserId(),
                EffectiveDate = request.EffectiveDate,
                SensitiveFields = string.Join(',', sensitive),
                ProposedChangesJson = JsonSerializer.Serialize(request.Changes)
            };
            _db.EmployeeChangeRequests.Add(change);
            await _db.SaveChangesAsync(cancellationToken);
            await Notify("Sensitive employee change requires approval", $"Fields requiring approval: {change.SensitiveFields}.", "EmployeeChangeRequest", change.Id.ToString(), cancellationToken);
            await Audit("employee.change_requested", "EmployeeChangeRequest", change.Id.ToString(), cancellationToken);
            return Accepted(new { changeRequestId = change.Id, requiresApproval = true, sensitiveFields = sensitive });
        }

        ApplyChanges(employee, request.Changes);
        employee.UpdatedAtUtc = DateTime.UtcNow;
        await AddHistory(employee, "Updated", request.EffectiveDate, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        await Audit("employee.updated", "Employee", employee.Id.ToString(), cancellationToken);
        return Ok(employee);
    }

    [HttpPatch("{id:int}/status")]
    [Authorize(Roles = "Admin,HR Manager,HR Officer")]
    public async Task<ActionResult<EmployeeDetailDto>> ChangeStatus(int id, EmployeeStatusChangeRequest request, [FromServices] IEmployeeManagementService employeeManagement, CancellationToken cancellationToken)
    {
        try
        {
            var employee = await employeeManagement.ChangeStatusAsync(RequireTenant(), id, request, Context(), cancellationToken);
            return employee is null ? NotFound() : Ok(employee);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("{id:int}/documents")]
    [Authorize(Roles = "Admin,HR Manager,HR Officer")]
    [RequestSizeLimit(10_485_760)]
    public async Task<ActionResult<EmployeeDocument>> UploadEmployeeDocument(int id, [FromForm] EmployeeDocumentUploadMetadata request, [FromForm] IFormFile file, [FromServices] IEmployeeManagementService employeeManagement, CancellationToken cancellationToken)
    {
        try
        {
            var document = await employeeManagement.UploadDocumentAsync(RequireTenant(), id, request, file, Context(), cancellationToken);
            return Created($"/api/employees/{id}/documents/{document.Id}", document);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpGet("{id:int}/documents")]
    public async Task<ActionResult<IReadOnlyCollection<EmployeeDocument>>> EmployeeDocuments(int id, [FromServices] IEmployeeManagementService employeeManagement, CancellationToken cancellationToken)
    {
        return Ok(await employeeManagement.GetDocumentsAsync(RequireTenant(), id, cancellationToken));
    }

    [HttpGet("{id:int}/history")]
    public async Task<ActionResult<IReadOnlyCollection<EmployeeHistory>>> EmployeeHistory(int id, [FromServices] IEmployeeManagementService employeeManagement, CancellationToken cancellationToken)
    {
        return Ok(await employeeManagement.GetHistoryAsync(RequireTenant(), id, cancellationToken));
    }

    [HttpPost("{id:int}/activate")]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<ActionResult<EmployeeDetailDto>> Activate(int id, EmployeeStatusChangeRequest request, [FromServices] IEmployeeManagementService employeeManagement, CancellationToken cancellationToken)
    {
        var employee = await employeeManagement.ActivateAsync(RequireTenant(), id, request, Context(), cancellationToken);
        return employee is null ? NotFound() : Ok(employee);
    }

    [HttpPost("{id:int}/terminate")]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<ActionResult<EmployeeDetailDto>> Terminate(int id, EmployeeStatusChangeRequest request, [FromServices] IEmployeeManagementService employeeManagement, CancellationToken cancellationToken)
    {
        var employee = await employeeManagement.TerminateAsync(RequireTenant(), id, request, Context(), cancellationToken);
        return employee is null ? NotFound() : Ok(employee);
    }

    [HttpPost("changes/{changeId:guid}/approve")]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<IActionResult> ApproveChange(Guid changeId, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();
        var change = await _db.EmployeeChangeRequests.FirstOrDefaultAsync(x => x.Id == changeId && x.TenantId == tenantId, cancellationToken);
        if (change is null) return NotFound();
        var employee = await _db.Employees.FirstOrDefaultAsync(x => x.Id == change.EmployeeId && (x.TenantId == tenantId || x.TenantId == null), cancellationToken);
        if (employee is null) return NotFound();
        var changes = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(change.ProposedChangesJson) ?? new();
        ApplyChanges(employee, changes);
        employee.UpdatedAtUtc = DateTime.UtcNow;
        change.Status = "ApprovedApplied";
        change.ApprovedByUserId = GetUserId();
        change.ApprovedAtUtc = DateTime.UtcNow;
        change.AppliedAtUtc = DateTime.UtcNow;
        await AddHistory(employee, "SensitiveChangeApproved", change.EffectiveDate, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        await Audit("employee.change_approved", "EmployeeChangeRequest", change.Id.ToString(), cancellationToken);
        return Ok(employee);
    }

    [HttpPost("{id:int}/transfer")]
    [Authorize(Roles = "Admin,HR Manager,Manager")]
    public async Task<ActionResult<EmployeeTransferRequest>> RequestTransfer(int id, EmployeeTransferCreateRequest request, [FromServices] IEmployeeManagementService employeeManagement, CancellationToken cancellationToken)
    {
        var transfer = await employeeManagement.RequestTransferAsync(RequireTenant(), id, request, Context(), cancellationToken);
        if (transfer is null) return NotFound();
        return Created($"/api/employees/transfers/{transfer.Id}", transfer);
    }

    [HttpPost("transfers/{transferId:guid}/approve-current-manager")]
    [Authorize(Roles = "Admin,HR Manager,Manager")]
    public Task<IActionResult> ApproveCurrentManager(Guid transferId, CancellationToken cancellationToken) => AdvanceTransfer(transferId, "PendingNewManager", x => x.CurrentManagerApprovedAtUtc = DateTime.UtcNow, cancellationToken);

    [HttpPost("transfers/{transferId:guid}/approve-new-manager")]
    [Authorize(Roles = "Admin,HR Manager,Manager")]
    public Task<IActionResult> ApproveNewManager(Guid transferId, CancellationToken cancellationToken) => AdvanceTransfer(transferId, "PendingHrApproval", x => x.NewManagerApprovedAtUtc = DateTime.UtcNow, cancellationToken);

    [HttpPost("transfers/{transferId:guid}/approve-hr")]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<IActionResult> ApproveHrTransfer(Guid transferId, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();
        var transfer = await _db.EmployeeTransferRequests.FirstOrDefaultAsync(x => x.Id == transferId && x.TenantId == tenantId, cancellationToken);
        if (transfer is null) return NotFound();
        var employee = await _db.Employees.FirstOrDefaultAsync(x => x.Id == transfer.EmployeeId && x.TenantId == tenantId, cancellationToken);
        if (employee is null) return NotFound();
        employee.Department = transfer.NewDepartment;
        employee.Branch = transfer.NewBranch;
        employee.ManagerEmployeeId = transfer.NewManagerEmployeeId;
        employee.UpdatedAtUtc = DateTime.UtcNow;
        transfer.Status = "ApprovedApplied";
        transfer.HrApprovedAtUtc = DateTime.UtcNow;
        await AddHistory(employee, "TransferApproved", transfer.EffectiveDate, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        await Notify("Employee transfer approved", $"Transfer for employee {employee.EmployeeCode} was applied.", "EmployeeTransferRequest", transfer.Id.ToString(), cancellationToken);
        await Audit("employee.transfer_approved", "EmployeeTransferRequest", transfer.Id.ToString(), cancellationToken);
        return Ok(employee);
    }

    [HttpGet("reports/summary")]
    [Authorize(Roles = "Admin,HR Manager,HR Officer,Payroll Officer,Auditor")]
    public async Task<ActionResult<EmployeeReportsDto>> Reports(CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();
        var employees = _db.Employees.Where(x => x.TenantId == tenantId || x.TenantId == null);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var next60 = today.AddDays(60);
        return Ok(new EmployeeReportsDto(
            await employees.CountAsync(cancellationToken),
            await employees.CountAsync(x => x.Status == "Active", cancellationToken),
            await employees.CountAsync(x => x.JoiningDate >= DateTime.UtcNow.AddDays(-30), cancellationToken),
            await employees.CountAsync(x => x.Status == "Exited" || x.Status == "Terminated", cancellationToken),
            await employees.CountAsync(x => x.ProbationEndDate != null && x.ProbationEndDate >= today, cancellationToken),
            await employees.GroupBy(x => x.Department).Select(x => new GroupCountDto(x.Key, x.Count())).ToListAsync(cancellationToken),
            await employees.GroupBy(x => x.Branch).Select(x => new GroupCountDto(x.Key, x.Count())).ToListAsync(cancellationToken),
            await employees.GroupBy(x => x.Nationality).Select(x => new GroupCountDto(x.Key, x.Count())).ToListAsync(cancellationToken),
            await employees.GroupBy(x => x.Gender).Select(x => new GroupCountDto(x.Key, x.Count())).ToListAsync(cancellationToken),
            await employees.CountAsync(x => x.ContractEndDate != null && x.ContractEndDate <= next60, cancellationToken),
            await employees.CountAsync(x => (x.VisaExpiryDate != null && x.VisaExpiryDate <= next60) || (x.PassportExpiryDate != null && x.PassportExpiryDate <= next60), cancellationToken),
            await employees.CountAsync(x => x.ProfileCompletenessScore < 80, cancellationToken)));
    }

    [HttpGet("reports/headcount")]
    [Authorize(Roles = "Admin,HR Manager,HR Officer,Payroll Officer,Auditor")]
    public async Task<ActionResult<EmployeeHeadcountReportDto>> Headcount([FromServices] IEmployeeManagementService employeeManagement, CancellationToken cancellationToken)
    {
        return Ok(await employeeManagement.HeadcountAsync(RequireTenant(), cancellationToken));
    }

    [HttpGet("reports/expiring-documents")]
    [Authorize(Roles = "Admin,HR Manager,HR Officer,Payroll Officer,Auditor")]
    public async Task<ActionResult<IReadOnlyCollection<EmployeeExpiringDocumentDto>>> ExpiringDocuments([FromServices] IEmployeeManagementService employeeManagement, [FromQuery] int days = 60, CancellationToken cancellationToken = default)
    {
        return Ok(await employeeManagement.ExpiringDocumentsAsync(RequireTenant(), days, cancellationToken));
    }

    [HttpGet("reports/missing-documents")]
    [Authorize(Roles = "Admin,HR Manager,HR Officer,Payroll Officer,Auditor")]
    public async Task<ActionResult<IReadOnlyCollection<EmployeeMissingDocumentsReportDto>>> MissingDocuments([FromServices] IEmployeeManagementService employeeManagement, CancellationToken cancellationToken)
    {
        return Ok(await employeeManagement.MissingDocumentsAsync(RequireTenant(), cancellationToken));
    }

    [HttpGet("reports/status-summary")]
    [Authorize(Roles = "Admin,HR Manager,HR Officer,Payroll Officer,Auditor")]
    public async Task<ActionResult<EmployeeStatusSummaryDto>> StatusSummary([FromServices] IEmployeeManagementService employeeManagement, CancellationToken cancellationToken)
    {
        return Ok(await employeeManagement.StatusSummaryAsync(RequireTenant(), cancellationToken));
    }

    [HttpGet("ai/insights")]
    public async Task<ActionResult<EmployeeAiResponseDto>> AiInsights([FromQuery] string query, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();
        var normalized = (query ?? string.Empty).ToLowerInvariant();
        var employees = _db.Employees.Where(x => x.TenantId == tenantId || x.TenantId == null);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        if (normalized.Contains("iqama") || normalized.Contains("visa") || normalized.Contains("expiry"))
        {
            var days = normalized.Contains("60") ? 60 : 30;
            var until = today.AddDays(days);
            var matches = await employees.Where(x => (x.VisaExpiryDate != null && x.VisaExpiryDate <= until) || (x.PassportExpiryDate != null && x.PassportExpiryDate <= until)).Take(50).ToListAsync(cancellationToken);
            return Ok(new EmployeeAiResponseDto($"Found {matches.Count} employees with visa/passport expiry risk in the next {days} days.", matches.Select(ToListItem).ToList()));
        }
        if (normalized.Contains("bank"))
        {
            var matches = await employees.Where(x => x.BankIban == "" || x.BankName == "").Take(50).ToListAsync(cancellationToken);
            return Ok(new EmployeeAiResponseDto($"Found {matches.Count} employees missing bank details.", matches.Select(ToListItem).ToList()));
        }
        if (normalized.Contains("probation"))
        {
            var monthEnd = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
            var matches = await employees.Where(x => x.ProbationEndDate >= today && x.ProbationEndDate <= monthEnd).Take(50).ToListAsync(cancellationToken);
            return Ok(new EmployeeAiResponseDto($"Found {matches.Count} employees with probation ending this month.", matches.Select(ToListItem).ToList()));
        }
        var incomplete = await employees.Where(x => x.ProfileCompletenessScore < 80).Take(50).ToListAsync(cancellationToken);
        return Ok(new EmployeeAiResponseDto($"Found {incomplete.Count} employees with incomplete onboarding profiles.", incomplete.Select(ToListItem).ToList()));
    }

    [HttpGet("{id:int}/letters/appointment")]
    [Authorize(Roles = "Admin,HR Manager,HR Officer")]
    public async Task<IActionResult> AppointmentLetter(int id, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();
        var employee = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId && !x.IsDeleted, cancellationToken);
        if (employee is null) return NotFound();
        var tenant = await _db.Tenants.AsNoTracking().Select(t => new { t.Id, t.Name }).FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        var salary = await _db.EmployeeSalaryStructures.AsNoTracking().Where(x => x.TenantId == tenantId && x.EmployeeId == id && x.IsActive).OrderByDescending(x => x.EffectiveDate).FirstOrDefaultAsync(cancellationToken);
        var data = new LetterData(
            EmployeeName: employee.FullName,
            EmployeeCode: employee.EmployeeCode,
            Department: employee.Department,
            Designation: employee.Designation,
            JoiningDate: employee.JoiningDate,
            LeavingDate: null,
            BasicSalary: salary?.BasicSalary ?? employee.Salary ?? 0m,
            Currency: "AED",
            CompanyName: tenant?.Name ?? "KynexOne Technologies",
            IssuedBy: "HR Department",
            IssuedDate: DateTime.UtcNow
        );
        var pdf = await _letters.GenerateAppointmentLetterAsync(data, cancellationToken);
        await Audit("employee.letter.appointment", "Employee", id.ToString(), cancellationToken);
        return File(pdf, "application/pdf", $"appointment-letter-{employee.EmployeeCode}.pdf");
    }

    [HttpGet("{id:int}/letters/experience")]
    [Authorize(Roles = "Admin,HR Manager,HR Officer")]
    public async Task<IActionResult> ExperienceLetter(int id, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();
        var employee = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId && !x.IsDeleted, cancellationToken);
        if (employee is null) return NotFound();
        var tenant = await _db.Tenants.AsNoTracking().Select(t => new { t.Id, t.Name }).FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        var salary = await _db.EmployeeSalaryStructures.AsNoTracking().Where(x => x.TenantId == tenantId && x.EmployeeId == id && x.IsActive).OrderByDescending(x => x.EffectiveDate).FirstOrDefaultAsync(cancellationToken);
        var data = new LetterData(
            EmployeeName: employee.FullName,
            EmployeeCode: employee.EmployeeCode,
            Department: employee.Department,
            Designation: employee.Designation,
            JoiningDate: employee.JoiningDate,
            LeavingDate: employee.ContractEndDate.HasValue ? employee.ContractEndDate.Value.ToDateTime(TimeOnly.MinValue) : null,
            BasicSalary: salary?.BasicSalary ?? employee.Salary ?? 0m,
            Currency: "AED",
            CompanyName: tenant?.Name ?? "KynexOne Technologies",
            IssuedBy: "HR Department",
            IssuedDate: DateTime.UtcNow
        );
        var pdf = await _letters.GenerateExperienceLetterAsync(data, cancellationToken);
        await Audit("employee.letter.experience", "Employee", id.ToString(), cancellationToken);
        return File(pdf, "application/pdf", $"experience-letter-{employee.EmployeeCode}.pdf");
    }

    [HttpGet("{id:int}/templates/{templateType}")]
    public async Task<IActionResult> RenderTemplate(int id, string templateType, [FromQuery] string language = "en", CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        var employee = await _db.Employees.FirstOrDefaultAsync(x => x.Id == id && (x.TenantId == tenantId || x.TenantId == null), cancellationToken);
        if (employee is null) return NotFound();
        var hijriJoining = _hijri.FromGregorian(DateOnly.FromDateTime(employee.JoiningDate));
        var isArabic = language.Equals("ar", StringComparison.OrdinalIgnoreCase);
        var title = templateType.ToLowerInvariant() switch
        {
            "contract" => isArabic ? "عقد عمل" : "Employment Contract",
            "sponsorship" => isArabic ? "خطاب كفالة" : "Sponsorship Letter",
            "offer" => isArabic ? "عرض عمل" : "Offer Letter",
            _ => isArabic ? "خطاب موظف" : "Employee Letter"
        };
        var body = isArabic
            ? $"{title}\n\nالاسم: {FirstNonEmpty(employee.ArabicName, employee.EnglishName, employee.FullName)}\nالرقم الوظيفي: {employee.EmployeeCode}\nالقسم: {employee.Department}\nتاريخ الانضمام: {employee.JoiningDate:yyyy-MM-dd} / {hijriJoining.HijriDate}\nالكفيل: {employee.SponsorName}\n"
            : $"{title}\n\nName: {FirstNonEmpty(employee.EnglishName, employee.FullName)}\nEmployee ID: {employee.EmployeeCode}\nDepartment: {employee.Department}\nJoining date: {employee.JoiningDate:yyyy-MM-dd} / Hijri {hijriJoining.HijriDate}\nSponsor: {employee.SponsorName}\n";
        return Ok(new EmployeeTemplateDto(templateType, language, title, body));
    }

    [HttpGet("{id:int}/localized-dates")]
    public async Task<IActionResult> LocalizedDates(int id, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();
        var employee = await _db.Employees.FirstOrDefaultAsync(x => x.Id == id && (x.TenantId == tenantId || x.TenantId == null), cancellationToken);
        if (employee is null) return NotFound();
        return Ok(new
        {
            joiningDate = _hijri.FromGregorian(DateOnly.FromDateTime(employee.JoiningDate)),
            passportExpiryDate = employee.PassportExpiryDate is null ? null : _hijri.FromGregorian(employee.PassportExpiryDate.Value),
            visaExpiryDate = employee.VisaExpiryDate is null ? null : _hijri.FromGregorian(employee.VisaExpiryDate.Value),
            contractEndDate = employee.ContractEndDate is null ? null : _hijri.FromGregorian(employee.ContractEndDate.Value)
        });
    }

    private async Task<IActionResult> AdvanceTransfer(Guid transferId, string nextStatus, Action<EmployeeTransferRequest> stamp, CancellationToken cancellationToken)
    {
        var transfer = await _db.EmployeeTransferRequests.FirstOrDefaultAsync(x => x.Id == transferId && x.TenantId == RequireTenant(), cancellationToken);
        if (transfer is null) return NotFound();
        stamp(transfer);
        transfer.Status = nextStatus;
        await _db.SaveChangesAsync(cancellationToken);
        await Audit("employee.transfer_advanced", "EmployeeTransferRequest", transfer.Id.ToString(), cancellationToken);
        return Ok(transfer);
    }

    private async Task<EmployeeDraft?> FindDraft(Guid draftId, CancellationToken cancellationToken) => await _db.EmployeeDrafts.FirstOrDefaultAsync(x => x.Id == draftId && x.TenantId == RequireTenant(), cancellationToken);

    private EmployeeDraft ApplyDraft(EmployeeDraft draft, EmployeeDraftRequest request)
    {
        draft.CurrentStep = request.CurrentStep ?? draft.CurrentStep;
        draft.EnglishName = request.EnglishName ?? draft.EnglishName;
        draft.ArabicName = request.ArabicName ?? draft.ArabicName;
        draft.PersonalEmail = request.PersonalEmail ?? draft.PersonalEmail;
        draft.WorkEmail = request.WorkEmail ?? draft.WorkEmail;
        draft.Phone = request.Phone ?? draft.Phone;
        draft.Gender = request.Gender ?? draft.Gender;
        draft.DateOfBirth = request.DateOfBirth ?? draft.DateOfBirth;
        draft.MaritalStatus = request.MaritalStatus ?? draft.MaritalStatus;
        draft.EmergencyContactName = request.EmergencyContactName ?? draft.EmergencyContactName;
        draft.EmergencyContactPhone = request.EmergencyContactPhone ?? draft.EmergencyContactPhone;
        draft.Nationality = request.Nationality ?? draft.Nationality;
        draft.CountryCode = request.CountryCode ?? draft.CountryCode;
        draft.Department = request.Department ?? draft.Department;
        draft.Designation = request.Designation ?? draft.Designation;
        draft.Branch = request.Branch ?? draft.Branch;
        draft.WorkLocation = request.WorkLocation ?? draft.WorkLocation;
        draft.ManagerEmployeeId = request.ManagerEmployeeId ?? draft.ManagerEmployeeId;
        draft.JoiningDate = request.JoiningDate ?? draft.JoiningDate;
        draft.ContractType = request.ContractType ?? draft.ContractType;
        draft.Grade = request.Grade ?? draft.Grade;
        draft.CostCenter = request.CostCenter ?? draft.CostCenter;
        draft.ContractStartDate = request.ContractStartDate ?? draft.ContractStartDate;
        draft.ContractEndDate = request.ContractEndDate ?? draft.ContractEndDate;
        draft.ProbationEndDate = request.ProbationEndDate ?? draft.ProbationEndDate;
        draft.PayrollProfileCode = request.PayrollProfileCode ?? draft.PayrollProfileCode;
        draft.Salary = request.Salary ?? draft.Salary;
        draft.BankName = request.BankName ?? draft.BankName;
        draft.BankIban = request.BankIban ?? draft.BankIban;
        draft.WpsBankDetails = request.WpsBankDetails ?? draft.WpsBankDetails;
        draft.ShiftPolicyCode = request.ShiftPolicyCode ?? draft.ShiftPolicyCode;
        draft.LeavePolicyCode = request.LeavePolicyCode ?? draft.LeavePolicyCode;
        draft.SponsorName = request.SponsorName ?? draft.SponsorName;
        draft.PassportIssueDate = request.PassportIssueDate ?? draft.PassportIssueDate;
        draft.PassportNumber = request.PassportNumber ?? draft.PassportNumber;
        draft.PassportExpiryDate = request.PassportExpiryDate ?? draft.PassportExpiryDate;
        draft.VisaIssueDate = request.VisaIssueDate ?? draft.VisaIssueDate;
        draft.VisaNumber = request.VisaNumber ?? draft.VisaNumber;
        draft.VisaExpiryDate = request.VisaExpiryDate ?? draft.VisaExpiryDate;
        draft.ResidencyIssueDate = request.ResidencyIssueDate ?? draft.ResidencyIssueDate;
        draft.WorkPermitIssueDate = request.WorkPermitIssueDate ?? draft.WorkPermitIssueDate;
        draft.IqamaNumber = request.IqamaNumber ?? draft.IqamaNumber;
        draft.MuqeemNumber = request.MuqeemNumber ?? draft.MuqeemNumber;
        draft.GosiReference = request.GosiReference ?? draft.GosiReference;
        draft.QiwaContractNumber = request.QiwaContractNumber ?? draft.QiwaContractNumber;
        draft.EmiratesId = request.EmiratesId ?? draft.EmiratesId;
        draft.LaborCardNumber = request.LaborCardNumber ?? draft.LaborCardNumber;
        draft.VisaFileNumber = request.VisaFileNumber ?? draft.VisaFileNumber;
        draft.Qid = request.Qid ?? draft.Qid;
        draft.WorkPermitNumber = request.WorkPermitNumber ?? draft.WorkPermitNumber;
        draft.CivilId = request.CivilId ?? draft.CivilId;
        draft.ResidencyNumber = request.ResidencyNumber ?? draft.ResidencyNumber;
        return draft;
    }

    private static decimal CalculateCompleteness(EmployeeDraft draft, int documentCount)
    {
        var values = new[] { draft.EnglishName, draft.Department, draft.Designation, draft.WorkEmail, draft.CountryCode, draft.Nationality, draft.MaritalStatus, draft.ContractType, draft.PayrollProfileCode, draft.ShiftPolicyCode, draft.LeavePolicyCode, draft.PassportNumber, draft.SponsorName, draft.EmergencyContactName };
        var completed = values.Count(x => !string.IsNullOrWhiteSpace(x)) + (draft.DateOfBirth.HasValue ? 1 : 0) + (draft.JoiningDate.HasValue ? 1 : 0) + (documentCount > 0 ? 2 : 0);
        return Math.Round(Math.Min(100m, completed * 100m / 18m), 1);
    }

    private async Task<string> GenerateEmployeeCode(Guid tenantId, CancellationToken cancellationToken)
    {
        var count = await _db.Employees.CountAsync(x => x.TenantId == tenantId || x.TenantId == null, cancellationToken) + 1;
        return $"EMP-{count:00000}";
    }

    private async Task<Guid?> CreateEmployeeUserAccount(Employee employee, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(employee.WorkEmail) || employee.TenantId is null) return null;
        var normalized = AuthService.Normalize(employee.WorkEmail);
        var exists = await _db.Users.FirstOrDefaultAsync(x => x.TenantId == employee.TenantId && x.NormalizedEmail == normalized, cancellationToken);
        if (exists is not null) return exists.Id;
        var role = await _db.Roles.FirstOrDefaultAsync(x => x.TenantId == employee.TenantId && x.NormalizedName == "EMPLOYEE", cancellationToken);
        var user = new User { TenantId = employee.TenantId.Value, Email = employee.WorkEmail.Trim().ToLowerInvariant(), NormalizedEmail = normalized, FullName = employee.FullName, PasswordHash = _passwordHasher.Hash("ChangeMe123!"), IsActive = true, IsEmailConfirmed = false };
        _db.Users.Add(user);
        if (role is not null) user.UserRoles.Add(new UserRole { User = user, Role = role });
        await _db.SaveChangesAsync(cancellationToken);
        return user.Id;
    }

    private void ApplyChanges(Employee employee, Dictionary<string, JsonElement> changes)
    {
        foreach (var (field, value) in changes)
        {
            switch (field)
            {
                case "department": employee.Department = value.GetString() ?? employee.Department; break;
                case "designation": employee.Designation = value.GetString() ?? employee.Designation; break;
                case "branch": employee.Branch = value.GetString() ?? employee.Branch; break;
                case "workLocation": employee.WorkLocation = value.GetString() ?? employee.WorkLocation; break;
                case "managerEmployeeId": employee.ManagerEmployeeId = value.ValueKind == JsonValueKind.Null ? null : value.GetInt32(); break;
                case "dateOfBirth": employee.DateOfBirth = ReadDateOnly(value); break;
                case "maritalStatus": employee.MaritalStatus = value.GetString() ?? employee.MaritalStatus; break;
                case "emergencyContactName": employee.EmergencyContactName = value.GetString() ?? employee.EmergencyContactName; break;
                case "emergencyContactPhone": employee.EmergencyContactPhone = value.GetString() ?? employee.EmergencyContactPhone; break;
                case "contractType": employee.ContractType = value.GetString() ?? employee.ContractType; break;
                case "grade": employee.Grade = value.GetString() ?? employee.Grade; break;
                case "costCenter": employee.CostCenter = value.GetString() ?? employee.CostCenter; break;
                case "salary": employee.Salary = value.GetDecimal(); break;
                case "bankName": employee.BankName = value.GetString() ?? employee.BankName; break;
                case "bankIban": employee.BankIban = value.GetString() ?? employee.BankIban; break;
                case "wpsBankDetails": employee.WpsBankDetails = value.GetString() ?? employee.WpsBankDetails; break;
                case "passportNumber": employee.PassportNumber = value.GetString() ?? employee.PassportNumber; break;
                case "passportIssueDate": employee.PassportIssueDate = ReadDateOnly(value); break;
                case "passportExpiryDate": employee.PassportExpiryDate = ReadDateOnly(value); break;
                case "visaNumber": employee.VisaNumber = value.GetString() ?? employee.VisaNumber; break;
                case "visaIssueDate": employee.VisaIssueDate = ReadDateOnly(value); break;
                case "visaExpiryDate": employee.VisaExpiryDate = ReadDateOnly(value); break;
                case "iqamaNumber": employee.IqamaNumber = value.GetString() ?? employee.IqamaNumber; break;
                case "muqeemNumber": employee.MuqeemNumber = value.GetString() ?? employee.MuqeemNumber; break;
                case "gosiReference": employee.GosiReference = value.GetString() ?? employee.GosiReference; break;
                case "emiratesId": employee.EmiratesId = value.GetString() ?? employee.EmiratesId; break;
                case "laborCardNumber": employee.LaborCardNumber = value.GetString() ?? employee.LaborCardNumber; break;
                case "visaFileNumber": employee.VisaFileNumber = value.GetString() ?? employee.VisaFileNumber; break;
                case "qid": employee.Qid = value.GetString() ?? employee.Qid; break;
                case "workPermitNumber": employee.WorkPermitNumber = value.GetString() ?? employee.WorkPermitNumber; break;
                case "workPermitIssueDate": employee.WorkPermitIssueDate = ReadDateOnly(value); break;
                case "civilId": employee.CivilId = value.GetString() ?? employee.CivilId; break;
                case "residencyNumber": employee.ResidencyNumber = value.GetString() ?? employee.ResidencyNumber; break;
                case "residencyIssueDate": employee.ResidencyIssueDate = ReadDateOnly(value); break;
                case "terminationReason": employee.TerminationReason = value.GetString() ?? employee.TerminationReason; break;
            }
        }
    }

    private static DateOnly? ReadDateOnly(JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
        return DateOnly.TryParse(value.GetString(), out var parsed) ? parsed : null;
    }

    private async Task AddHistory(Employee employee, string eventType, DateOnly effectiveDate, CancellationToken cancellationToken)
    {
        _db.EmployeeHistories.Add(new EmployeeHistory { TenantId = employee.TenantId ?? RequireTenant(), EmployeeId = employee.Id, EventType = eventType, EffectiveDate = effectiveDate, SnapshotJson = JsonSerializer.Serialize(employee), CreatedByUserId = GetUserId() });
        await Task.CompletedTask;
    }

    private bool CanEditSensitive() => User.IsInRole("Admin") || User.IsInRole("HR Manager") || User.HasClaim("permission", "employees.sensitive");
    private bool CanViewSensitive() => CanEditSensitive() || User.IsInRole("Payroll Officer") || User.HasClaim("permission", "employees.sensitive");
    private static void MaskSensitive(Employee employee)
    {
        employee.Salary = null;
        employee.BankName = string.Empty;
        employee.BankIban = string.Empty;
        employee.WpsBankDetails = string.Empty;
        employee.MedicalInformation = string.Empty;
        employee.DisciplinaryRecords = string.Empty;
        employee.TerminationReason = string.Empty;
    }
    private Task Notify(string title, string message, string entity, string? entityId, CancellationToken cancellationToken) => _notifications.NotifyAsync(RequireTenant(), null, title, message, entity, entityId, cancellationToken);

    private EmployeeListItemDto ToListItem(Employee employee) => new(employee.Id, employee.EmployeeCode, employee.FullName, employee.ArabicName, employee.Department, employee.Designation, employee.Branch, employee.ManagerEmployeeId, employee.Status, employee.ProfileCompletenessScore, employee.VisaExpiryDate, employee.PassportExpiryDate, employee.IqamaNumber);
    private static string FirstNonEmpty(params string[] values) => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "Unnamed Employee";
    private Guid RequireTenant() => Guid.Parse(User.FindFirstValue("tenant_id") ?? throw new UnauthorizedAccessException("Tenant claim missing."));
    private Guid? GetUserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id) ? id : null;
    private RequestContext Context() => new(HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), GetUserId(), RequireTenant());
    private Task Audit(string action, string entity, string? entityId, CancellationToken cancellationToken) => _audit.WriteAsync(action, entity, entityId, new RequestContext(HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), GetUserId(), RequireTenant()), null, cancellationToken);
}

public record EmployeeListItemDto(int Id, string EmployeeCode, string FullName, string ArabicName, string Department, string Designation, string Branch, int? ManagerEmployeeId, string Status, decimal ProfileCompletenessScore, DateOnly? VisaExpiryDate, DateOnly? PassportExpiryDate, string IqamaNumber);
public record EmployeeProfileDto(Employee Employee, IReadOnlyCollection<EmployeeDocument> Documents, IReadOnlyCollection<EmployeeHistory> History);
public record EmployeeDocumentRequest(string DocumentType, string FileName, string ContentType, string StorageUrl, bool IsRequired, DateOnly? ExpiryDate);
public record EmployeeTransferRequestDto(string NewDepartment, string NewBranch, int? NewManagerEmployeeId, DateOnly EffectiveDate);
public record EmployeeUpdateRequest(DateOnly EffectiveDate, Dictionary<string, JsonElement> Changes);
public record GroupCountDto(string Name, int Count);
public record EmployeeReportsDto(int TotalHeadcount, int ActiveEmployees, int NewJoiners, int Exits, int ProbationEmployees, IReadOnlyCollection<GroupCountDto> DepartmentHeadcount, IReadOnlyCollection<GroupCountDto> BranchHeadcount, IReadOnlyCollection<GroupCountDto> NationalityMix, IReadOnlyCollection<GroupCountDto> GenderMix, int ContractExpiringSoon, int VisaOrPassportExpiringSoon, int MissingDocumentsOrIncompleteProfiles);
public record EmployeeAiResponseDto(string Answer, IReadOnlyCollection<EmployeeListItemDto> Employees);
public record EmployeeDraftRequest(string? CurrentStep, string? EnglishName, string? ArabicName, string? PersonalEmail, string? WorkEmail, string? Phone, string? Gender, DateOnly? DateOfBirth, string? MaritalStatus, string? EmergencyContactName, string? EmergencyContactPhone, string? Nationality, string? CountryCode, string? Department, string? Designation, string? Branch, string? WorkLocation, int? ManagerEmployeeId, DateTime? JoiningDate, string? ContractType, string? Grade, string? CostCenter, DateOnly? ContractStartDate, DateOnly? ContractEndDate, DateOnly? ProbationEndDate, string? PayrollProfileCode, decimal? Salary, string? BankName, string? BankIban, string? WpsBankDetails, string? ShiftPolicyCode, string? LeavePolicyCode, string? SponsorName, DateOnly? PassportIssueDate, string? PassportNumber, DateOnly? PassportExpiryDate, DateOnly? VisaIssueDate, string? VisaNumber, DateOnly? VisaExpiryDate, string? IqamaNumber, string? MuqeemNumber, string? GosiReference, string? QiwaContractNumber, string? EmiratesId, string? LaborCardNumber, string? VisaFileNumber, string? Qid, string? WorkPermitNumber, DateOnly? WorkPermitIssueDate, string? CivilId, string? ResidencyNumber, DateOnly? ResidencyIssueDate);
public record EmployeeDocumentUploadRequest(string DocumentType, bool IsRequired, DateOnly? ExpiryDate, IFormFile File);
public record EmployeeTemplateDto(string TemplateType, string Language, string Title, string Body);
