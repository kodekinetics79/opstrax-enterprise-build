using System.Globalization;
using Opstrax.Api.DTOs;
using Opstrax.Api.Services;

namespace Opstrax.Api.Controllers;

// Billing consolidation (ADR-008 Billing layer) endpoints: preview/commit consolidated invoice drafts
// for a customer over a period, and read the consolidation runs. RBAC billing.* folds into
// finance:manage/billing:manage; every output is an ordinary invoice_draft (issuance/ZATCA reused).
public static class BillingEndpoints
{
    public static void MapBillingEndpoints(this WebApplication app)
    {
        app.MapPost("/api/billing/consolidate", Consolidate);
        app.MapGet("/api/billing/runs", ListRuns);
        app.MapGet("/api/billing/runs/{id:long}", GetRun);
    }

    private static async Task<IResult> Consolidate(HttpContext http, Dictionary<string, object?> body, BillingConsolidationService svc, CancellationToken ct)
    {
        if (EndpointMappings.RequirePermission(http, "billing.create") is { } denied) return denied;
        var companyId = EndpointMappings.GetCompanyId(http);

        if (Long(body, "customerId") is not { } customerId)
            return Results.BadRequest(ApiResponse<object>.Fail("customerId is required"));
        if (Date(body, "periodStart") is not { } start || Date(body, "periodEnd") is not { } end)
            return Results.BadRequest(ApiResponse<object>.Fail("periodStart and periodEnd (YYYY-MM-DD) are required"));

        var mode = string.Equals(Str(body, "mode"), "commit", StringComparison.OrdinalIgnoreCase) ? BillingMode.Commit : BillingMode.Preview;
        var outcome = await svc.GenerateConsolidatedDraftsAsync(companyId, customerId, start, end, Long(body, "billingProfileId"), mode, ct);

        var payload = new
        {
            outcome.Generated, outcome.GroupCount, outcome.DraftCount, outcome.Subtotal, outcome.Reason,
            Groups = outcome.Groups.Select(g => new { g.GroupKey, g.JobId, g.Currency, g.ChargeCount, g.Subtotal, g.DraftId, g.InvoiceNo })
        };
        return Results.Ok(ApiResponse<object>.Ok(payload,
            outcome.Generated ? $"Consolidated {outcome.DraftCount} draft(s)"
                              : mode == BillingMode.Preview ? "Preview computed" : $"Nothing consolidated: {outcome.Reason}"));
    }

    private static async Task<IResult> ListRuns(HttpContext http, BillingConsolidationService svc, CancellationToken ct)
    {
        if (EndpointMappings.RequirePermission(http, "billing.read") is { } denied) return denied;
        var q = http.Request.Query;
        var rows = await svc.ListRunsAsync(EndpointMappings.GetCompanyId(http),
            q.TryGetValue("customerId", out var ci) && long.TryParse(ci, out var cid) ? cid : null,
            q.TryGetValue("status", out var st) ? st.ToString() : null, ct);
        return Results.Ok(ApiResponse<object>.Ok(rows));
    }

    private static async Task<IResult> GetRun(HttpContext http, long id, BillingConsolidationService svc, CancellationToken ct)
    {
        if (EndpointMappings.RequirePermission(http, "billing.read") is { } denied) return denied;
        var run = await svc.GetRunAsync(EndpointMappings.GetCompanyId(http), id, ct);
        return run is null ? Results.NotFound(ApiResponse<object>.Fail("Run not found")) : Results.Ok(ApiResponse<object>.Ok(run));
    }

    private static string? Str(Dictionary<string, object?> b, string k) => b.TryGetValue(k, out var v) && v is not null ? v.ToString() : null;
    private static long? Long(Dictionary<string, object?> b, string k) => Str(b, k) is { } s && long.TryParse(s, out var v) ? v : null;
    private static DateOnly? Date(Dictionary<string, object?> b, string k) => Str(b, k) is { } s && DateOnly.TryParse(s, CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var v) ? v : null;
}
