using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Zayra.Api.Data;
using Zayra.Api.Models;

namespace Zayra.Api.Infrastructure.Qiwa;

/// <summary>
/// Placeholder implementation of <see cref="IQiwaIntegrationService"/>.
///
/// This service manages local Qiwa configuration and sync-log records only.
/// It does NOT make any external HTTP calls to the Qiwa API.  When the real
/// Qiwa API integration is ready, replace the body of EnqueueEmployeeSyncAsync
/// (and add a background worker) without touching callers or the interface.
/// </summary>
public sealed class QiwaIntegrationService : IQiwaIntegrationService
{
    private readonly ZayraDbContext _db;
    private readonly ILogger<QiwaIntegrationService> _log;

    private static readonly string[] RequiredFields =
    [
        nameof(Employee.SaudiOrNonSaudi),
        nameof(Employee.IdType),
        nameof(Employee.IdNumber),
        nameof(Employee.Nationality),
        nameof(Employee.OccupationCode),
        nameof(Employee.EstablishmentId),
        nameof(Employee.WorkLocationId),
        nameof(Employee.ContractReference),
    ];

    public QiwaIntegrationService(ZayraDbContext db, ILogger<QiwaIntegrationService> log)
    {
        _db  = db;
        _log = log;
    }

    // ── Connection management ─────────────────────────────────────────────────

    public Task<QiwaTenantConnection?> GetConnectionStatusAsync(Guid tenantId, CancellationToken cancellationToken = default)
        => _db.QiwaTenantConnections
              .AsNoTracking()
              .FirstOrDefaultAsync(c => c.TenantId == tenantId, cancellationToken)!;

    public async Task<QiwaTenantConnection> UpsertConnectionAsync(
        Guid tenantId, QiwaConnectionRequest request, Guid? performedBy, string ipAddress,
        CancellationToken cancellationToken = default)
    {
        var existing = await _db.QiwaTenantConnections
            .FirstOrDefaultAsync(c => c.TenantId == tenantId, cancellationToken);

        if (existing is null)
        {
            existing = new QiwaTenantConnection { TenantId = tenantId, ConfiguredBy = performedBy };
            _db.QiwaTenantConnections.Add(existing);
        }

        var oldJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            existing.EstablishmentId,
            existing.Environment,
            existing.Status
        });

        existing.EstablishmentId             = request.EstablishmentId.Trim();
        existing.EstablishmentName           = request.EstablishmentName.Trim();
        existing.UnifiedOrganisationNumber   = request.UnifiedOrganisationNumber.Trim();
        existing.Environment                 = request.Environment.Trim();
        existing.Status                      = QiwaConnectionStatuses.Disconnected; // real check happens when API goes live
        existing.UpdatedAtUtc                = DateTime.UtcNow;

        _db.AdminAuditLogs.Add(new AdminAuditLog
        {
            TenantId        = tenantId,
            EntityType      = "QiwaTenantConnection",
            EntityId        = existing.Id.ToString(),
            Action          = "Updated",
            OldValuesJson   = oldJson,
            NewValuesJson   = System.Text.Json.JsonSerializer.Serialize(new
            {
                request.EstablishmentId,
                request.Environment,
                Status = QiwaConnectionStatuses.Disconnected
            }),
            PerformedBy     = performedBy,
            PerformedByName = string.Empty,
            IpAddress       = ipAddress
        });

        await _db.SaveChangesAsync(cancellationToken);
        _log.LogInformation("Qiwa connection upserted for tenant {TenantId}, establishment {EstId}", tenantId, request.EstablishmentId);
        return existing;
    }

    // ── Sync log ──────────────────────────────────────────────────────────────

    public async Task<QiwaSyncLog> EnqueueEmployeeSyncAsync(
        Guid tenantId, int employeeId, string direction, string triggerSource, Guid? triggeredBy,
        CancellationToken cancellationToken = default)
    {
        // Verify employee belongs to this tenant (cross-tenant guard).
        var exists = await _db.Employees
            .AnyAsync(e => e.TenantId == tenantId && e.Id == employeeId && !e.IsDeleted, cancellationToken);

        if (!exists)
            throw new InvalidOperationException($"Employee {employeeId} not found in this tenant.");

        var entry = new QiwaSyncLog
        {
            TenantId      = tenantId,
            EmployeeId    = employeeId,
            Direction     = direction,
            TriggerSource = triggerSource,
            Status        = "Pending",
            TriggeredBy   = triggeredBy,
        };
        _db.QiwaSyncLogs.Add(entry);

        // Update the employee's sync status flag.
        var employee = await _db.Employees.FirstAsync(e => e.TenantId == tenantId && e.Id == employeeId, cancellationToken);
        employee.QiwaSyncStatus = QiwaSyncStatuses.Pending;

        _db.AuditLogs.Add(new Domain.Entities.AuditLog
        {
            TenantId    = tenantId,
            UserId      = triggeredBy,
            Action      = "qiwa.sync_enqueued",
            EntityName  = "Employee",
            EntityId    = employeeId.ToString(),
            Metadata    = System.Text.Json.JsonSerializer.Serialize(new { direction, triggerSource, syncLogId = entry.Id }),
        });

        await _db.SaveChangesAsync(cancellationToken);

        _log.LogInformation(
            "Qiwa sync enqueued for employee {EmployeeId} (tenant {TenantId}), direction={Direction}, source={Source}",
            employeeId, tenantId, direction, triggerSource);

        return entry;
    }

    public Task<IReadOnlyList<QiwaSyncLog>> GetSyncLogsAsync(
        Guid tenantId, int? employeeId, int page, int pageSize,
        CancellationToken cancellationToken = default)
    {
        var q = _db.QiwaSyncLogs.AsNoTracking().Where(l => l.TenantId == tenantId);
        if (employeeId.HasValue) q = q.Where(l => l.EmployeeId == employeeId.Value);
        return q.OrderByDescending(l => l.CreatedAtUtc)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken)
                .ContinueWith(t => (IReadOnlyList<QiwaSyncLog>)t.Result, cancellationToken);
    }

    // ── Readiness check ───────────────────────────────────────────────────────

    public async Task<QiwaReadinessReport> CheckEmployeeReadinessAsync(
        Guid tenantId, int employeeId, CancellationToken cancellationToken = default)
    {
        var emp = await _db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.TenantId == tenantId && e.Id == employeeId && !e.IsDeleted, cancellationToken)
            ?? throw new InvalidOperationException($"Employee {employeeId} not found in this tenant.");

        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(emp.SaudiOrNonSaudi))    missing.Add("saudi_or_non_saudi");
        if (string.IsNullOrWhiteSpace(emp.IdType))             missing.Add("id_type");
        if (string.IsNullOrWhiteSpace(emp.IdNumber))           missing.Add("id_number");
        if (string.IsNullOrWhiteSpace(emp.Nationality))        missing.Add("nationality");
        if (string.IsNullOrWhiteSpace(emp.OccupationCode))     missing.Add("occupation_code");
        if (string.IsNullOrWhiteSpace(emp.EstablishmentId))    missing.Add("establishment_id");
        if (string.IsNullOrWhiteSpace(emp.WorkLocationId))     missing.Add("work_location_id");
        if (string.IsNullOrWhiteSpace(emp.ContractReference))  missing.Add("contract_reference");

        return new QiwaReadinessReport(
            emp.Id,
            emp.EmployeeCode,
            emp.FullName,
            missing.Count == 0,
            missing);
    }
}
