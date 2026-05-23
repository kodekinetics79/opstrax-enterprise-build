using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zayra.Api.Data;
using Zayra.Api.Models;

namespace Zayra.Api.Controllers.Recruitment;

[Authorize]
[ApiController]
[Route("api/recruitment/onboarding")]
public class OnboardingController : ControllerBase
{
    private readonly ZayraDbContext _db;
    public OnboardingController(ZayraDbContext db) => _db = db;

    private Guid GetTenantId() =>
        Guid.TryParse(User.FindFirst("tenantId")?.Value, out var id) ? id : Guid.Empty;

    private Guid? GetUserId() =>
        Guid.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;

    private string GetUserName() => User.FindFirst("name")?.Value ?? User.Identity?.Name ?? "System";

    // ── Checklists ─────────────────────────────────────────────────────────────

    [HttpGet("checklists")]
    public async Task<IActionResult> ListChecklists([FromQuery] bool activeOnly = true, CancellationToken ct = default)
    {
        var tid = GetTenantId();
        var q = _db.OnboardingChecklists.Where(x => x.TenantId == tid && !x.IsDeleted);
        if (activeOnly) q = q.Where(x => x.IsActive);

        var items = await q.OrderBy(x => x.Name).ToListAsync(ct);
        return Ok(items);
    }

    [HttpPost("checklists")]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<IActionResult> CreateChecklist([FromBody] CreateChecklistRequest req, CancellationToken ct)
    {
        var tid = GetTenantId();

        var checklist = new OnboardingChecklist
        {
            TenantId = tid,
            Code = req.Code,
            Name = req.Name,
            Description = req.Description ?? string.Empty,
            ApplicableTo = req.ApplicableTo ?? "All",
            DepartmentName = req.DepartmentName ?? string.Empty,
        };

        _db.OnboardingChecklists.Add(checklist);
        await _db.SaveChangesAsync(ct);
        return Ok(checklist);
    }

    // ── Tasks ──────────────────────────────────────────────────────────────────

    // GET /api/recruitment/onboarding/tasks?employeeId=...&applicationId=...&status=...
    [HttpGet("tasks")]
    public async Task<IActionResult> ListTasks(
        [FromQuery] Guid? employeeId = null,
        [FromQuery] Guid? applicationId = null,
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30,
        CancellationToken ct = default)
    {
        var tid = GetTenantId();
        var q = _db.OnboardingTasks.Where(x => x.TenantId == tid);

        if (employeeId.HasValue) q = q.Where(x => x.EmployeeId == employeeId.Value);
        if (applicationId.HasValue) q = q.Where(x => x.ApplicationId == applicationId.Value);
        if (!string.IsNullOrEmpty(status)) q = q.Where(x => x.Status == status);

        var total = await q.CountAsync(ct);
        var items = await q.OrderBy(x => x.OrderIndex).ThenBy(x => x.DueDate)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        return Ok(new { total, page, pageSize, items });
    }

    // POST /api/recruitment/onboarding/tasks/bulk — Create tasks from checklist template
    [HttpPost("tasks/bulk")]
    [Authorize(Roles = "Admin,HR Manager,Recruiter")]
    public async Task<IActionResult> CreateBulkFromChecklist([FromBody] BulkOnboardingRequest req, CancellationToken ct)
    {
        var tid = GetTenantId();

        // Validate employee or application exists
        if (!req.EmployeeId.HasValue && !req.ApplicationId.HasValue)
            return BadRequest("Either employeeId or applicationId is required.");

        // Load template tasks from checklist if provided, else from req.Tasks
        var tasks = new List<OnboardingTask>();

        if (req.ChecklistId.HasValue)
        {
            // Checklist doesn't store template tasks — use req.Tasks with checklistId reference
        }

        if (req.Tasks != null)
        {
            for (int i = 0; i < req.Tasks.Count; i++)
            {
                var t = req.Tasks[i];
                tasks.Add(new OnboardingTask
                {
                    TenantId = tid,
                    ChecklistId = req.ChecklistId,
                    EmployeeId = req.EmployeeId,
                    ApplicationId = req.ApplicationId,
                    TaskTitle = t.TaskTitle,
                    TaskDescription = t.TaskDescription ?? string.Empty,
                    Category = t.Category ?? "General",
                    AssignedToName = t.AssignedToName ?? string.Empty,
                    AssignedToUserId = t.AssignedToUserId,
                    DueDate = t.DueDate,
                    OrderIndex = i + 1,
                    IsMandatory = t.IsMandatory,
                });
            }
        }

        _db.OnboardingTasks.AddRange(tasks);
        await _db.SaveChangesAsync(ct);
        return Ok(new { count = tasks.Count, tasks });
    }

    // POST /api/recruitment/onboarding/tasks — Create single task
    [HttpPost("tasks")]
    [Authorize(Roles = "Admin,HR Manager,Recruiter")]
    public async Task<IActionResult> CreateTask([FromBody] CreateOnboardingTaskRequest req, CancellationToken ct)
    {
        var tid = GetTenantId();

        var task = new OnboardingTask
        {
            TenantId = tid,
            ChecklistId = req.ChecklistId,
            EmployeeId = req.EmployeeId,
            ApplicationId = req.ApplicationId,
            TaskTitle = req.TaskTitle,
            TaskDescription = req.TaskDescription ?? string.Empty,
            Category = req.Category ?? "General",
            AssignedToName = req.AssignedToName ?? string.Empty,
            AssignedToUserId = req.AssignedToUserId,
            DueDate = req.DueDate,
            IsMandatory = req.IsMandatory,
        };

        _db.OnboardingTasks.Add(task);

        _db.RecruitmentAuditLogs.Add(new RecruitmentAuditLog
        {
            TenantId = tid, EntityType = "OnboardingTask", EntityId = task.Id.ToString(),
            Action = "Created", PerformedByUserId = GetUserId(), PerformedByName = GetUserName(),
            NewValuesJson = System.Text.Json.JsonSerializer.Serialize(new { task.TaskTitle, task.Category }),
        });

        await _db.SaveChangesAsync(ct);
        return Ok(task);
    }

    // PATCH /api/recruitment/onboarding/tasks/{id}/status
    [HttpPatch("tasks/{id:guid}/status")]
    public async Task<IActionResult> UpdateTaskStatus(Guid id, [FromBody] UpdateTaskStatusRequest req, CancellationToken ct)
    {
        var tid = GetTenantId();
        var task = await _db.OnboardingTasks.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tid, ct);
        if (task == null) return NotFound();

        var old = task.Status;
        task.Status = req.Status;
        task.Notes = req.Notes ?? task.Notes;
        task.UpdatedAtUtc = DateTime.UtcNow;

        if (req.Status == "Completed")
            task.CompletedDate = DateOnly.FromDateTime(DateTime.UtcNow);

        _db.RecruitmentAuditLogs.Add(new RecruitmentAuditLog
        {
            TenantId = tid, EntityType = "OnboardingTask", EntityId = id.ToString(),
            Action = "StatusUpdated", PerformedByUserId = GetUserId(), PerformedByName = GetUserName(),
            OldValuesJson = System.Text.Json.JsonSerializer.Serialize(new { Status = old }),
            NewValuesJson = System.Text.Json.JsonSerializer.Serialize(new { Status = req.Status }),
        });

        await _db.SaveChangesAsync(ct);
        return Ok(task);
    }

    // GET /api/recruitment/onboarding/summary?employeeId=...
    [HttpGet("summary")]
    public async Task<IActionResult> Summary([FromQuery] Guid? employeeId = null, [FromQuery] Guid? applicationId = null, CancellationToken ct = default)
    {
        var tid = GetTenantId();
        var q = _db.OnboardingTasks.Where(x => x.TenantId == tid);
        if (employeeId.HasValue) q = q.Where(x => x.EmployeeId == employeeId.Value);
        if (applicationId.HasValue) q = q.Where(x => x.ApplicationId == applicationId.Value);

        var tasks = await q.ToListAsync(ct);

        return Ok(new
        {
            total = tasks.Count,
            pending = tasks.Count(t => t.Status == "Pending"),
            inProgress = tasks.Count(t => t.Status == "InProgress"),
            completed = tasks.Count(t => t.Status == "Completed"),
            blocked = tasks.Count(t => t.Status == "Blocked"),
            mandatory = tasks.Count(t => t.IsMandatory),
            mandatoryCompleted = tasks.Count(t => t.IsMandatory && t.Status == "Completed"),
            completionPct = tasks.Count > 0 ? (double)tasks.Count(t => t.Status == "Completed") / tasks.Count * 100 : 0,
        });
    }
}

public record CreateChecklistRequest(string Code, string Name, string? Description, string? ApplicableTo, string? DepartmentName);
public record BulkOnboardingRequest(Guid? ChecklistId, Guid? EmployeeId, Guid? ApplicationId, List<OnboardingTaskItem>? Tasks);
public record OnboardingTaskItem(string TaskTitle, string? TaskDescription, string? Category, string? AssignedToName, Guid? AssignedToUserId, DateOnly? DueDate, bool IsMandatory);
public record CreateOnboardingTaskRequest(string TaskTitle, string? TaskDescription, string? Category, Guid? ChecklistId, Guid? EmployeeId, Guid? ApplicationId, string? AssignedToName, Guid? AssignedToUserId, DateOnly? DueDate, bool IsMandatory);
public record UpdateTaskStatusRequest(string Status, string? Notes);
