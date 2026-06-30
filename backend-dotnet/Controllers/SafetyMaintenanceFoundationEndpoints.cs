using Opstrax.Api.Data;
using Opstrax.Api.DTOs;
using Opstrax.Api.Foundation;
using Opstrax.Api.Services;

namespace Opstrax.Api.Controllers;

public static class SafetyMaintenanceFoundationEndpoints
{
    public static void MapSafetyMaintenanceFoundationEndpoints(this WebApplication app)
    {
        app.MapGet("/api/foundation/safety-maintenance/summary", GetSummary);
        app.MapGet("/api/foundation/safety-maintenance/fleet-health-snapshots", ListFleetHealthSnapshots);
        app.MapGet("/api/foundation/safety-maintenance/recommendations", ListRecommendations);
    }

    private static async Task<IResult> GetSummary(HttpContext http, SafetyMaintenanceFoundationService svc, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(http, "dashboard:view");
        if (denied is not null) return denied;

        var summary = await svc.GetSummaryAsync(EndpointMappings.GetCompanyId(http), ct);
        return Results.Ok(ApiResponse<object>.Ok(summary));
    }

    private static async Task<IResult> ListFleetHealthSnapshots(HttpContext http, SafetyMaintenanceFoundationService svc, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(http, "dashboard:view");
        if (denied is not null) return denied;

        var items = await svc.ListSnapshotsAsync(EndpointMappings.GetCompanyId(http), 14, ct);
        return Results.Ok(ApiResponse<object>.Ok(new { items }));
    }

    private static async Task<IResult> ListRecommendations(HttpContext http, Database db, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(http, "dashboard:view");
        if (denied is not null) return denied;

        var companyId = EndpointMappings.GetCompanyId(http);
        var items = await db.QueryAsync(
            @"SELECT id, recommendation_type, title, summary, confidence_score, urgency_score, risk_level, status, source_event_id, created_at
              FROM ai_recommendations
              WHERE tenant_id=@companyId
                AND (
                    recommendation_type LIKE 'safety.%'
                    OR recommendation_type LIKE 'maintenance.%'
                    OR recommendation_type LIKE 'fleet.health.%'
                )
              ORDER BY created_at DESC, id DESC
              LIMIT 20",
            c => c.Parameters.AddWithValue("@companyId", companyId), ct);

        return Results.Ok(ApiResponse<object>.Ok(new { items }));
    }
}
