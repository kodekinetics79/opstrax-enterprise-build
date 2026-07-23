using System.Globalization;
using Opstrax.Api.DTOs;
using Opstrax.Api.Services;

namespace Opstrax.Api.Controllers;

// Revenue recognition sub-ledger (ADR-008) endpoints: recognize/preview an invoice, reverse, close a
// fiscal period (high-risk), backfill, and read entries/periods/summary. RBAC revrec.* folds into
// finance:view/manage; revrec.period.close is a high-risk action.
public static class RevenueRecognitionEndpoints
{
    public static void MapRevenueRecognitionEndpoints(this WebApplication app)
    {
        app.MapPost("/api/revrec/invoices/{id:guid}/recognize", Recognize);
        app.MapGet("/api/revrec/invoices/{id:guid}", GetInvoiceRecognition);
        app.MapPost("/api/revrec/invoices/{id:guid}/reverse", Reverse);
        app.MapGet("/api/revrec/entries", ListEntries);
        app.MapGet("/api/revrec/periods", ListPeriods);
        app.MapPost("/api/revrec/periods/{code}/close", ClosePeriod);
        app.MapGet("/api/revrec/summary", Summary);
        app.MapPost("/api/revrec/backfill", Backfill);
    }

    private static async Task<IResult> Recognize(HttpContext http, Guid id, Dictionary<string, object?>? body, RevenueRecognitionService svc, CancellationToken ct)
    {
        if (EndpointMappings.RequirePermission(http, "revrec.manage") is { } d) return d;
        var mode = string.Equals(body is not null && body.TryGetValue("mode", out var m) ? m?.ToString() : null, "commit", StringComparison.OrdinalIgnoreCase)
            ? RecognitionMode.Commit : RecognitionMode.Preview;
        var o = await svc.RecognizeInvoiceAsync(EndpointMappings.GetCompanyId(http), id, mode, ct);
        return Results.Ok(ApiResponse<object>.Ok(new { o.Recognized, o.Reason, o.EntryId, o.Amount, o.AmountFunctional, o.Currency, o.Status },
            o.Recognized ? "Revenue recognized" : $"Not recognized: {o.Reason}"));
    }

    private static async Task<IResult> GetInvoiceRecognition(HttpContext http, Guid id, RevenueRecognitionService svc, CancellationToken ct)
    {
        if (EndpointMappings.RequirePermission(http, "revrec.read") is { } d) return d;
        return Results.Ok(ApiResponse<object>.Ok(await svc.GetInvoiceRecognitionAsync(EndpointMappings.GetCompanyId(http), id, ct)));
    }

    private static async Task<IResult> Reverse(HttpContext http, Guid id, Dictionary<string, object?>? body, RevenueRecognitionService svc, CancellationToken ct)
    {
        if (EndpointMappings.RequirePermission(http, "revrec.manage") is { } d) return d;
        var userId = Convert.ToInt64(http.Items[EndpointMappings.AuthUserIdItemKey] ?? 0L);
        var memo = body is not null && body.TryGetValue("memo", out var mm) ? mm?.ToString() ?? "" : "";
        var o = await svc.ReverseInvoiceRecognitionAsync(EndpointMappings.GetCompanyId(http), id, memo, userId, ct);
        return o.Recognized ? Results.Ok(ApiResponse<object>.Ok(new { o.Status }, "Recognition reversed"))
                            : Results.BadRequest(ApiResponse<object>.Fail($"Cannot reverse: {o.Reason}"));
    }

    private static async Task<IResult> ListEntries(HttpContext http, RevenueRecognitionService svc, CancellationToken ct)
    {
        if (EndpointMappings.RequirePermission(http, "revrec.read") is { } d) return d;
        var q = http.Request.Query;
        var rows = await svc.ListEntriesAsync(EndpointMappings.GetCompanyId(http),
            q.TryGetValue("period", out var p) ? p.ToString() : null,
            q.TryGetValue("status", out var s) ? s.ToString() : null,
            q.TryGetValue("customerId", out var c) && long.TryParse(c, out var cid) ? cid : null, ct);
        return Results.Ok(ApiResponse<object>.Ok(rows));
    }

    private static async Task<IResult> ListPeriods(HttpContext http, RevenueRecognitionService svc, CancellationToken ct)
    {
        if (EndpointMappings.RequirePermission(http, "revrec.read") is { } d) return d;
        return Results.Ok(ApiResponse<object>.Ok(await svc.ListPeriodsAsync(EndpointMappings.GetCompanyId(http), ct)));
    }

    private static async Task<IResult> ClosePeriod(HttpContext http, string code, RevenueRecognitionService svc, CancellationToken ct)
    {
        if (EndpointMappings.RequirePermission(http, "revrec.period.close") is { } d) return d;
        var userId = Convert.ToInt64(http.Items[EndpointMappings.AuthUserIdItemKey] ?? 0L);
        var o = await svc.CloseFiscalPeriodAsync(EndpointMappings.GetCompanyId(http), code, userId, ct);
        return o.Ok ? Results.Ok(ApiResponse<object>.Ok(new { code, o.Status }, "Fiscal period closed"))
                    : Results.BadRequest(ApiResponse<object>.Fail($"Cannot close: {o.Reason}"));
    }

    private static async Task<IResult> Summary(HttpContext http, RevenueRecognitionService svc, CancellationToken ct)
    {
        if (EndpointMappings.RequirePermission(http, "revrec.read") is { } d) return d;
        var q = http.Request.Query;
        var from = q.TryGetValue("from", out var f) && DateOnly.TryParse(f, out var fd) ? fd : new DateOnly(DateTime.UtcNow.Year, 1, 1);
        var to = q.TryGetValue("to", out var t) && DateOnly.TryParse(t, out var td) ? td : DateOnly.FromDateTime(DateTime.UtcNow);
        return Results.Ok(ApiResponse<object>.Ok(await svc.GetRecognizedRevenueSummaryAsync(EndpointMappings.GetCompanyId(http), from, to, ct)));
    }

    private static async Task<IResult> Backfill(HttpContext http, RevenueRecognitionService svc, CancellationToken ct)
    {
        if (EndpointMappings.RequirePermission(http, "revrec.manage") is { } d) return d;
        var userId = Convert.ToInt64(http.Items[EndpointMappings.AuthUserIdItemKey] ?? 0L);
        var o = await svc.BackfillIssuedInvoicesAsync(EndpointMappings.GetCompanyId(http), userId, ct);
        return Results.Ok(ApiResponse<object>.Ok(new { o.Reason }, "Backfill complete"));
    }
}
