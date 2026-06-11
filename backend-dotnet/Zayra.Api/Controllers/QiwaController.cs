using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zayra.Api.Infrastructure.Qiwa;

namespace Zayra.Api.Controllers;

/// <summary>
/// Qiwa integration management endpoints.
///
/// All routes are protected by the "qiwa_integration" feature flag via
/// FeatureFlagGuardFilter — a tenant must have that flag enabled before
/// these endpoints are accessible.
///
/// No real Qiwa API calls are made by any endpoint in this controller.
/// </summary>
[ApiController]
[Route("api/qiwa")]
[Authorize]
public class QiwaController : ControllerBase
{
    private readonly IQiwaIntegrationService _qiwa;

    public QiwaController(IQiwaIntegrationService qiwa) => _qiwa = qiwa;

    // ── Connection ────────────────────────────────────────────────────────────

    /// <summary>Returns the Qiwa connection configuration for the current tenant.</summary>
    [HttpGet("connection")]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<IActionResult> GetConnection(CancellationToken cancellationToken)
    {
        var connection = await _qiwa.GetConnectionStatusAsync(RequireTenant(), cancellationToken);
        if (connection is null)
            return Ok(new { status = "Disconnected", configured = false });

        return Ok(new
        {
            connection.Id,
            connection.TenantId,
            connection.EstablishmentId,
            connection.EstablishmentName,
            connection.UnifiedOrganisationNumber,
            connection.Environment,
            connection.Status,
            connection.LastConnectedAtUtc,
            connection.LastCheckedAtUtc,
            configured = true,
            hasError    = connection.Status is "ConfigurationError" or "ApiError",
            connection.LastErrorMessage
        });
    }

    /// <summary>Saves or updates the Qiwa establishment configuration for the tenant.</summary>
    [HttpPut("connection")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpsertConnection([FromBody] QiwaConnectionRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.EstablishmentId))
            return BadRequest(new { error = "establishment_id_required", message = "EstablishmentId is required." });

        if (request.Environment is not ("sandbox" or "production"))
            return BadRequest(new { error = "invalid_environment", message = "Environment must be 'sandbox' or 'production'." });

        try
        {
            var conn = await _qiwa.UpsertConnectionAsync(
                RequireTenant(), request, GetUserId(),
                HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
                cancellationToken);

            return Ok(new { conn.Id, conn.EstablishmentId, conn.Environment, conn.Status });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // ── Employee readiness ────────────────────────────────────────────────────

    /// <summary>
    /// Returns a report of Qiwa-required fields that are missing for an employee.
    /// </summary>
    [HttpGet("employees/{employeeId:int}/readiness")]
    [Authorize(Roles = "Admin,HR Manager,HR Officer")]
    public async Task<IActionResult> GetEmployeeReadiness(int employeeId, CancellationToken cancellationToken)
    {
        try
        {
            var report = await _qiwa.CheckEmployeeReadinessAsync(RequireTenant(), employeeId, cancellationToken);
            return Ok(report);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    // ── Sync ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Enqueues a Qiwa sync attempt for a single employee.
    /// This creates a QiwaSyncLog entry with Status=Pending.
    /// No real Qiwa API call is made in this release.
    /// </summary>
    [HttpPost("employees/{employeeId:int}/sync")]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<IActionResult> EnqueueSync(int employeeId, [FromQuery] string direction = "Push", CancellationToken cancellationToken = default)
    {
        if (direction is not ("Push" or "Pull"))
            return BadRequest(new { error = "invalid_direction", message = "Direction must be 'Push' or 'Pull'." });

        try
        {
            var log = await _qiwa.EnqueueEmployeeSyncAsync(
                RequireTenant(), employeeId, direction, "Manual", GetUserId(), cancellationToken);

            return Accepted(new
            {
                log.Id,
                log.EmployeeId,
                log.Direction,
                log.Status,
                log.TriggerSource,
                log.CreatedAtUtc,
                message = "Sync enqueued. The record will be processed when Qiwa integration is activated."
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // ── Sync logs ─────────────────────────────────────────────────────────────

    /// <summary>Returns paginated Qiwa sync logs for the tenant, optionally filtered to one employee.</summary>
    [HttpGet("sync-logs")]
    [Authorize(Roles = "Admin,HR Manager,HR Officer,Auditor")]
    public async Task<IActionResult> GetSyncLogs(
        [FromQuery] int? employeeId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        var logs = await _qiwa.GetSyncLogsAsync(RequireTenant(), employeeId, page, pageSize, cancellationToken);
        return Ok(new { page, pageSize, data = logs });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Guid RequireTenant()
        => Guid.Parse(User.FindFirstValue("tenant_id") ?? throw new UnauthorizedAccessException("Tenant claim missing."));

    private Guid? GetUserId()
        => Guid.TryParse(
            User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"),
            out var id) ? id : null;
}
