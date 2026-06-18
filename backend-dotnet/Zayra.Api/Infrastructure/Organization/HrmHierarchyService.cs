using Microsoft.EntityFrameworkCore;
using Zayra.Api.Application.Auth;
using Zayra.Api.Application.Organization;
using Zayra.Api.Data;
using Zayra.Api.Models;

namespace Zayra.Api.Infrastructure.Organization;

public class HrmHierarchyService : IHrmHierarchyService
{
    private readonly ZayraDbContext _db;
    private readonly IAuditService _audit;

    public HrmHierarchyService(ZayraDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<IReadOnlyList<OrgChartNodeDto>> GetOrgChartAsync(
        Guid tenantId, int? rootEmployeeId, int maxDepth, CancellationToken ct)
    {
        var employees = await _db.Employees
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId && !e.IsDeleted && e.Status == EmployeeStatuses.Active)
            .Select(e => new
            {
                e.Id,
                e.EmployeeCode,
                e.FullName,
                e.Designation,
                e.Department,
                e.ProfilePhotoUrl,
                e.ManagerEmployeeId
            })
            .ToListAsync(ct);

        // Build lookup
        var lookup = employees.ToLookup(e => e.ManagerEmployeeId);

        // Identify roots: employees whose manager is either null, not in this tenant, or equal to rootEmployeeId filter
        IEnumerable<int> rootIds;
        if (rootEmployeeId.HasValue)
            rootIds = new[] { rootEmployeeId.Value };
        else
            rootIds = employees.Where(e => e.ManagerEmployeeId == null || !employees.Any(m => m.Id == e.ManagerEmployeeId)).Select(e => e.Id);

        var allIds = new HashSet<int>(employees.Select(e => e.Id));

        OrgChartNodeDto Build(int id, int depth)
        {
            var e = employees.First(x => x.Id == id);
            var reports = depth < maxDepth
                ? lookup[id].Where(r => allIds.Contains(r.Id)).Select(r => Build(r.Id, depth + 1)).ToList()
                : new List<OrgChartNodeDto>();
            return new OrgChartNodeDto(e.Id, e.EmployeeCode, e.FullName, e.Designation ?? "", e.Department ?? "", e.ProfilePhotoUrl, reports);
        }

        return rootIds
            .Where(id => allIds.Contains(id))
            .Select(id => Build(id, 1))
            .ToList();
    }

    public async Task<IReadOnlyList<ReportingLineDto>> GetReportingLinesAsync(
        Guid tenantId, int employeeId, CancellationToken ct)
    {
        var lines = await _db.ReportingLines
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.EmployeeId == employeeId && r.IsActive)
            .ToListAsync(ct);

        var empIds = lines.Select(r => r.ManagerEmployeeId)
            .Append(employeeId)
            .Distinct()
            .ToList();

        var names = await _db.Employees
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId && empIds.Contains(e.Id))
            .Select(e => new { e.Id, e.FullName })
            .ToDictionaryAsync(e => e.Id, e => e.FullName, ct);

        names.TryGetValue(employeeId, out var empName);

        return lines.Select(r =>
        {
            names.TryGetValue(r.ManagerEmployeeId, out var mgrName);
            return new ReportingLineDto(
                r.Id, r.EmployeeId, empName ?? "", r.ManagerEmployeeId, mgrName ?? "",
                r.RelationshipType, r.EffectiveFrom, r.EffectiveTo, r.IsPrimary, r.IsActive);
        }).ToList();
    }

    public async Task SetManagerAsync(
        Guid tenantId, int employeeId, int? managerEmployeeId, RequestContext context, CancellationToken ct)
    {
        var employee = await _db.Employees
            .FirstOrDefaultAsync(e => e.TenantId == tenantId && e.Id == employeeId && !e.IsDeleted, ct)
            ?? throw new InvalidOperationException($"Employee {employeeId} not found.");

        if (managerEmployeeId.HasValue)
        {
            if (managerEmployeeId.Value == employeeId)
                throw new InvalidOperationException("An employee cannot be their own manager.");

            await ValidateNoCircularManagerAsync(tenantId, employeeId, managerEmployeeId.Value, ct);

            // Ensure manager belongs to same tenant
            var managerExists = await _db.Employees
                .AnyAsync(e => e.TenantId == tenantId && e.Id == managerEmployeeId.Value && !e.IsDeleted, ct);
            if (!managerExists)
                throw new InvalidOperationException($"Manager employee {managerEmployeeId.Value} not found in this tenant.");
        }

        var previousManagerId = employee.ManagerEmployeeId;
        employee.ManagerEmployeeId = managerEmployeeId;
        employee.UpdatedAtUtc = DateTime.UtcNow;
        employee.UpdatedBy = context.UserId;

        // Deactivate old SolidLine reporting line
        var oldLine = await _db.ReportingLines
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.EmployeeId == employeeId
                && r.RelationshipType == "SolidLine" && r.IsPrimary && r.IsActive, ct);
        if (oldLine is not null)
        {
            oldLine.IsActive = false;
            oldLine.EffectiveTo = DateTime.UtcNow;
            oldLine.UpdatedAtUtc = DateTime.UtcNow;
            oldLine.UpdatedBy = context.UserId;
        }

        // Create new SolidLine reporting line
        if (managerEmployeeId.HasValue)
        {
            _db.ReportingLines.Add(new ReportingLine
            {
                TenantId = tenantId,
                EmployeeId = employeeId,
                ManagerEmployeeId = managerEmployeeId.Value,
                RelationshipType = "SolidLine",
                EffectiveFrom = DateTime.UtcNow,
                IsPrimary = true,
                IsActive = true,
                CreatedBy = context.UserId
            });
        }

        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("employee.manager_changed", nameof(Employee), employeeId.ToString(), context,
            System.Text.Json.JsonSerializer.Serialize(new { previousManagerId, newManagerId = managerEmployeeId }), ct);
    }

    public async Task<ReportingLineDto> AddReportingLineAsync(
        Guid tenantId, int employeeId, AddReportingLineRequest req, RequestContext context, CancellationToken ct)
    {
        if (req.ManagerEmployeeId == employeeId)
            throw new InvalidOperationException("An employee cannot report to themselves.");

        var allowedTypes = new HashSet<string> { "SolidLine", "DottedLine", "Temporary", "Functional" };
        if (!allowedTypes.Contains(req.RelationshipType))
            throw new InvalidOperationException($"Invalid RelationshipType '{req.RelationshipType}'. Use: SolidLine, DottedLine, Temporary, Functional.");

        if (req.RelationshipType == "SolidLine")
            await ValidateNoCircularManagerAsync(tenantId, employeeId, req.ManagerEmployeeId, ct);

        var line = new ReportingLine
        {
            TenantId = tenantId,
            EmployeeId = employeeId,
            ManagerEmployeeId = req.ManagerEmployeeId,
            RelationshipType = req.RelationshipType,
            EffectiveFrom = req.EffectiveFrom ?? DateTime.UtcNow,
            EffectiveTo = req.EffectiveTo,
            IsPrimary = req.IsPrimary,
            IsActive = true,
            CreatedBy = context.UserId
        };
        _db.ReportingLines.Add(line);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("employee.reporting_line_added", nameof(ReportingLine), line.Id.ToString(), context, null, ct);

        var names = await _db.Employees
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId && (e.Id == employeeId || e.Id == req.ManagerEmployeeId))
            .Select(e => new { e.Id, e.FullName })
            .ToDictionaryAsync(e => e.Id, e => e.FullName, ct);
        names.TryGetValue(employeeId, out var empName);
        names.TryGetValue(req.ManagerEmployeeId, out var mgrName);

        return new ReportingLineDto(line.Id, line.EmployeeId, empName ?? "", line.ManagerEmployeeId, mgrName ?? "",
            line.RelationshipType, line.EffectiveFrom, line.EffectiveTo, line.IsPrimary, line.IsActive);
    }

    public async Task<bool> RemoveReportingLineAsync(
        Guid tenantId, Guid reportingLineId, RequestContext context, CancellationToken ct)
    {
        var line = await _db.ReportingLines
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Id == reportingLineId, ct);
        if (line is null) return false;
        line.IsActive = false;
        line.EffectiveTo = DateTime.UtcNow;
        line.UpdatedAtUtc = DateTime.UtcNow;
        line.UpdatedBy = context.UserId;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("employee.reporting_line_removed", nameof(ReportingLine), reportingLineId.ToString(), context, null, ct);
        return true;
    }

    public async Task<int> ValidateNoCircularManagerAsync(
        Guid tenantId, int employeeId, int newManagerId, CancellationToken ct)
    {
        // Walk the manager chain from newManagerId upward; if we ever reach employeeId, it's circular.
        var allManagers = await _db.Employees
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId && !e.IsDeleted)
            .Select(e => new { e.Id, e.ManagerEmployeeId })
            .ToDictionaryAsync(e => e.Id, e => e.ManagerEmployeeId, ct);

        var visited = new HashSet<int>();
        var current = (int?)newManagerId;
        var depth = 0;
        while (current.HasValue)
        {
            if (current.Value == employeeId)
                throw new InvalidOperationException(
                    $"Setting employee {newManagerId} as manager of {employeeId} would create a circular reporting chain.");
            if (visited.Contains(current.Value))
                break; // existing circular loop in data — stop but don't throw (that's a data integrity issue)
            visited.Add(current.Value);
            allManagers.TryGetValue(current.Value, out var next);
            current = next;
            depth++;
            if (depth > 50) break; // safety cap
        }
        return depth;
    }
}
