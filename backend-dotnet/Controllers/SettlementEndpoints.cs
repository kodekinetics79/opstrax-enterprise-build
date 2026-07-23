using System.Globalization;
using Opstrax.Api.DTOs;
using Opstrax.Api.Services;

namespace Opstrax.Api.Controllers;

// Settlement / carrier-&-driver-pay (AP) — ADR-007 §C. The AP mirror of RevenueReadinessEndpoints
// (order-to-cash). Phase 1: generate/get/list/lines a driver statement, approve it (high-risk),
// record a payment, and an AP summary. RBAC via settlement.* (folds into finance:manage/billing:manage).
public static class SettlementEndpoints
{
    public static void MapSettlementEndpoints(this WebApplication app)
    {
        app.MapPost("/api/settlements/generate", GenerateStatement);
        app.MapGet("/api/settlements", ListStatements);
        app.MapGet("/api/settlements/{id:long}", GetStatement);
        app.MapGet("/api/settlements/{id:long}/lines", GetStatementLines);
        app.MapPost("/api/settlements/{id:long}/approve", ApproveStatement);
        app.MapPost("/api/settlements/{id:long}/payments", RecordPayment);
        app.MapGet("/api/finance/ap-summary", ApSummary);
        // Detention -> driver pay policy (the differentiator).
        app.MapGet("/api/settlements/detention-pay-policy", GetDetentionPayPolicy);
        app.MapPut("/api/settlements/detention-pay-policy", SetDetentionPayPolicy);
    }

    private static async Task<IResult> GetDetentionPayPolicy(HttpContext http, SettlementService svc, CancellationToken ct)
    {
        if (EndpointMappings.RequirePermission(http, "settlement.read") is { } denied) return denied;
        return Results.Ok(ApiResponse<object>.Ok(await svc.GetDetentionPayPolicyAsync(EndpointMappings.GetCompanyId(http), ct)));
    }

    private static async Task<IResult> SetDetentionPayPolicy(HttpContext http, Dictionary<string, object?> body, SettlementService svc, AuditService audit, CancellationToken ct)
    {
        if (EndpointMappings.RequirePermission(http, "settlement.manage") is { } denied) return denied;
        var trigger = Str(body, "triggerState") ?? "collected";
        var shareType = Str(body, "shareType") ?? "percent";
        if (trigger is not ("billed" or "collected")) return Results.BadRequest(ApiResponse<object>.Fail("triggerState must be 'billed' or 'collected'"));
        if (shareType is not ("percent" or "flat_per_hour")) return Results.BadRequest(ApiResponse<object>.Fail("shareType must be 'percent' or 'flat_per_hour'"));
        var enabled = body.TryGetValue("enabled", out var e) && e is not null && bool.TryParse(e.ToString(), out var eb) && eb;
        decimal shareValue = decimal.TryParse(Str(body, "shareValue"), out var sv) ? sv : 0m;
        await svc.SetDetentionPayPolicyAsync(EndpointMappings.GetCompanyId(http), enabled, trigger, shareType, shareValue, ct);
        await audit.LogAsync(http, "settlement.detention_pay_policy.saved", "DriverDetentionPayPolicy", null,
            System.Text.Json.JsonSerializer.Serialize(new { enabled, trigger, shareType, shareValue }), ct);
        return Results.Ok(ApiResponse<object>.Ok(new { enabled, triggerState = trigger, shareType, shareValue }, "Detention pay policy saved"));
    }

    private static async Task<IResult> GenerateStatement(HttpContext http, Dictionary<string, object?> body, SettlementService svc, CancellationToken ct)
    {
        if (EndpointMappings.RequirePermission(http, "settlement.create") is { } denied) return denied;
        var companyId = EndpointMappings.GetCompanyId(http);

        var payeeType = Str(body, "payeeType") ?? "driver";
        if (payeeType != "driver")
            return Results.BadRequest(ApiResponse<object>.Fail("Phase 1 supports driver statements only"));
        if (Long(body, "payeeId") is not { } driverId)
            return Results.BadRequest(ApiResponse<object>.Fail("payeeId (driver id) is required"));
        if (Date(body, "periodStart") is not { } start || Date(body, "periodEnd") is not { } end)
            return Results.BadRequest(ApiResponse<object>.Fail("periodStart and periodEnd (YYYY-MM-DD) are required"));

        var mode = string.Equals(Str(body, "mode"), "commit", StringComparison.OrdinalIgnoreCase)
            ? SettlementMode.Commit : SettlementMode.Preview;

        var outcome = await svc.GenerateDriverStatementAsync(companyId, driverId, start, end, mode, ct);
        var payload = new
        {
            outcome.Generated, outcome.StatementId, outcome.StatementNo, outcome.Status,
            outcome.Subtotal, outcome.Total, outcome.Currency, outcome.Reason,
            Lines = outcome.Lines.Select(l => new
            {
                l.JobId, l.PayCode, l.Description, l.Basis, l.BasisAmount, l.Quantity, l.UnitRate, l.Amount
            })
        };
        // Fail-closed reasons (no agreement, unsupported basis, no loads) are a valid business answer,
        // not a server error — return 200 with Generated=false so the caller can show why.
        return Results.Ok(ApiResponse<object>.Ok(payload,
            outcome.Generated ? "Settlement statement generated"
                              : mode == SettlementMode.Preview ? "Preview computed" : $"Not generated: {outcome.Reason}"));
    }

    private static async Task<IResult> ListStatements(HttpContext http, SettlementService svc, CancellationToken ct)
    {
        if (EndpointMappings.RequirePermission(http, "settlement.read") is { } denied) return denied;
        var q = http.Request.Query;
        var rows = await svc.ListStatementsAsync(
            EndpointMappings.GetCompanyId(http),
            q.TryGetValue("payeeType", out var pt) ? pt.ToString() : null,
            q.TryGetValue("payeeId", out var pi) && long.TryParse(pi, out var pid) ? pid : null,
            q.TryGetValue("status", out var st) ? st.ToString() : null, ct);
        return Results.Ok(ApiResponse<object>.Ok(rows));
    }

    private static async Task<IResult> GetStatement(HttpContext http, long id, SettlementService svc, CancellationToken ct)
    {
        if (EndpointMappings.RequirePermission(http, "settlement.read") is { } denied) return denied;
        var row = await svc.GetStatementAsync(EndpointMappings.GetCompanyId(http), id, ct);
        return row is null
            ? Results.NotFound(ApiResponse<object>.Fail("Settlement statement not found"))
            : Results.Ok(ApiResponse<object>.Ok(row));
    }

    private static async Task<IResult> GetStatementLines(HttpContext http, long id, SettlementService svc, CancellationToken ct)
    {
        if (EndpointMappings.RequirePermission(http, "settlement.read") is { } denied) return denied;
        var rows = await svc.GetLinesAsync(EndpointMappings.GetCompanyId(http), id, ct);
        return Results.Ok(ApiResponse<object>.Ok(rows));
    }

    private static async Task<IResult> ApproveStatement(HttpContext http, long id, SettlementService svc, CancellationToken ct)
    {
        if (EndpointMappings.RequirePermission(http, "settlement.approve") is { } denied) return denied;
        var userId = Convert.ToInt64(http.Items[EndpointMappings.AuthUserIdItemKey] ?? 0L);
        var outcome = await svc.ApproveStatementAsync(EndpointMappings.GetCompanyId(http), id, userId, ct);
        if (!outcome.Ok)
            return outcome.Reason == "not_found"
                ? Results.NotFound(ApiResponse<object>.Fail("Settlement statement not found"))
                : Results.BadRequest(ApiResponse<object>.Fail($"Cannot approve: {outcome.Reason}"));
        return Results.Ok(ApiResponse<object>.Ok(new { id, outcome.Status }, "Settlement statement approved"));
    }

    private static async Task<IResult> RecordPayment(HttpContext http, long id, Dictionary<string, object?> body, SettlementService svc, CancellationToken ct)
    {
        if (EndpointMappings.RequirePermission(http, "settlement.pay") is { } denied) return denied;
        var userId = Convert.ToInt64(http.Items[EndpointMappings.AuthUserIdItemKey] ?? 0L);
        if (Dec(body, "amount") is not { } amount)
            return Results.BadRequest(ApiResponse<object>.Fail("amount is required"));

        var outcome = await svc.RecordPaymentAsync(
            EndpointMappings.GetCompanyId(http), id, amount,
            Str(body, "method"), Str(body, "reference"), Str(body, "idempotencyKey"), userId, ct);
        if (!outcome.Ok)
            return outcome.Reason == "not_found"
                ? Results.NotFound(ApiResponse<object>.Fail("Settlement statement not found"))
                : Results.BadRequest(ApiResponse<object>.Fail($"Cannot record payment: {outcome.Reason}"));
        return Results.Ok(ApiResponse<object>.Ok(
            new { id, outcome.PaymentId, outcome.Status, outcome.AmountPaid }, "Payment recorded"));
    }

    private static async Task<IResult> ApSummary(HttpContext http, SettlementService svc, CancellationToken ct)
    {
        if (EndpointMappings.RequirePermission(http, "settlement.read") is { } denied) return denied;
        var summary = await svc.GetApSummaryAsync(EndpointMappings.GetCompanyId(http), ct);
        return Results.Ok(ApiResponse<object>.Ok(summary));
    }

    private static string? Str(Dictionary<string, object?> b, string k)
        => b.TryGetValue(k, out var v) && v is not null ? v.ToString() : null;

    private static long? Long(Dictionary<string, object?> b, string k)
        => Str(b, k) is { } s && long.TryParse(s, out var v) ? v : null;

    private static decimal? Dec(Dictionary<string, object?> b, string k)
        => Str(b, k) is { } s && decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static DateOnly? Date(Dictionary<string, object?> b, string k)
        => Str(b, k) is { } s && DateOnly.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var v) ? v : null;
}
