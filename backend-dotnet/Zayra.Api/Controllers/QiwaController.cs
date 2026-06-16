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
/// these endpoints are accessible.  Fine-grained access is enforced per-endpoint
/// via the qiwa.read / qiwa.sync / qiwa.configure permissions (not role strings).
///
/// Real Qiwa API calls are performed by the background QiwaSyncWorker once
/// credentials are configured and the live adapter is enabled.
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
    public async Task<IActionResult> GetConnection(CancellationToken cancellationToken)
    {
        if (!HasPermission("qiwa.read")) return Forbid();

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
    public async Task<IActionResult> UpsertConnection([FromBody] QiwaConnectionRequest request, CancellationToken cancellationToken)
    {
        if (!HasPermission("qiwa.configure")) return Forbid();

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

    /// <summary>
    /// Saves the tenant's Qiwa OAuth2 client credentials.  The secret is encrypted
    /// at rest and never returned.  Requires qiwa.configure.
    /// </summary>
    [HttpPut("credentials")]
    public async Task<IActionResult> SaveCredentials([FromBody] QiwaCredentialRequest request, CancellationToken cancellationToken)
    {
        if (!HasPermission("qiwa.configure")) return Forbid();

        if (string.IsNullOrWhiteSpace(request.ClientId) || string.IsNullOrWhiteSpace(request.ClientSecret))
            return BadRequest(new { error = "credentials_required", message = "ClientId and ClientSecret are required." });

        if (request.Environment is not ("sandbox" or "production"))
            return BadRequest(new { error = "invalid_environment", message = "Environment must be 'sandbox' or 'production'." });

        await _qiwa.SaveApiCredentialAsync(
            RequireTenant(), request.ClientId.Trim(), request.ClientSecret, request.Environment.Trim(),
            GetUserId() ?? Guid.Empty,
            HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
            cancellationToken);

        // Never echo the secret back.
        return Ok(new { configured = true, environment = request.Environment, message = "Qiwa credentials saved and encrypted." });
    }

    // ── Employee readiness ────────────────────────────────────────────────────

    /// <summary>Returns a report of Qiwa-required fields that are missing for an employee.</summary>
    [HttpGet("employees/{employeeId:int}/readiness")]
    public async Task<IActionResult> GetEmployeeReadiness(int employeeId, CancellationToken cancellationToken)
    {
        if (!HasPermission("qiwa.read")) return Forbid();

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

    /// <summary>Tenant-wide readiness summary: ready vs blocked employee counts.</summary>
    [HttpGet("readiness-summary")]
    public async Task<IActionResult> GetReadinessSummary(CancellationToken cancellationToken)
    {
        if (!HasPermission("qiwa.read")) return Forbid();
        return Ok(await _qiwa.GetReadinessSummaryAsync(RequireTenant(), cancellationToken));
    }

    /// <summary>Aggregate QIWA compliance summary for dashboards.</summary>
    [HttpGet("compliance-summary")]
    public async Task<IActionResult> GetComplianceSummary(CancellationToken cancellationToken)
    {
        if (!HasPermission("qiwa.read")) return Forbid();
        return Ok(await _qiwa.GetComplianceSummaryAsync(RequireTenant(), cancellationToken));
    }

    // ── Sync ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Enqueues a Qiwa sync attempt for a single employee (Status=Pending).
    /// The background worker performs the actual push.
    /// </summary>
    [HttpPost("employees/{employeeId:int}/sync")]
    public async Task<IActionResult> EnqueueSync(int employeeId, [FromQuery] string direction = "Push", CancellationToken cancellationToken = default)
    {
        if (!HasPermission("qiwa.sync")) return Forbid();

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
                message = "Sync enqueued. The background worker will process it shortly."
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Resets a dead-lettered sync log back to Pending for reprocessing.</summary>
    [HttpPost("sync-logs/{syncLogId:guid}/retry")]
    public async Task<IActionResult> RetryDeadLetter(Guid syncLogId, CancellationToken cancellationToken)
    {
        if (!HasPermission("qiwa.sync")) return Forbid();

        try
        {
            await _qiwa.RetryDeadLetterAsync(RequireTenant(), syncLogId, GetUserId() ?? Guid.Empty, cancellationToken);
            return Ok(new { syncLogId, status = "Pending", message = "Sync log reset for retry." });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    // ── Sync logs ─────────────────────────────────────────────────────────────

    /// <summary>Returns paginated Qiwa sync logs for the tenant, optionally filtered to one employee.</summary>
    [HttpGet("sync-logs")]
    public async Task<IActionResult> GetSyncLogs(
        [FromQuery] int? employeeId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        if (!HasPermission("qiwa.read")) return Forbid();

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

    private bool HasPermission(string permission) =>
        User.Claims.Any(c => c.Type == "permission" && string.Equals(c.Value, permission, StringComparison.OrdinalIgnoreCase));
}

public record QiwaCredentialRequest(string ClientId, string ClientSecret, string Environment);
