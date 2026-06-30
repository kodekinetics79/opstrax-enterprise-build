using System.Globalization;
using Opstrax.Api.Data;
using Opstrax.Api.DTOs;
using Opstrax.Api.Foundation;
using Opstrax.Api.Services;

namespace Opstrax.Api.Controllers;

public static class RevenueReadinessEndpoints
{
    public static void MapRevenueReadinessEndpoints(this WebApplication app)
    {
        app.MapPost("/api/jobs/{jobId:long}/mark-ready-to-bill", MarkReadyToBill);
        app.MapGet("/api/invoice-drafts", ListInvoiceDrafts);
        app.MapGet("/api/invoice-drafts/{id:guid}", GetInvoiceDraft);
        app.MapPost("/api/jobs/{jobId:long}/invoice-draft", CreateInvoiceDraft);
        app.MapPatch("/api/invoice-drafts/{id:guid}", UpdateInvoiceDraft);
        app.MapPost("/api/invoice-drafts/{id:guid}/issue", IssueInvoiceDraft);
        app.MapGet("/api/issued-invoices", ListIssuedInvoices);
        app.MapGet("/api/issued-invoices/{id:guid}", GetIssuedInvoice);
        app.MapPost("/api/issued-invoices/{id:guid}/payments", RecordIssuedInvoicePayment);
        app.MapGet("/api/finance/ar-summary", AccountsReceivableSummary);
        app.MapPost("/api/approval-requests/{id:long}/decide", DecideApprovalRequest);
        app.MapGet("/api/revenue/summary", RevenueSummary);
        app.MapGet("/api/customers/{customerId:long}/summary", CustomerSummary);
    }

    private static async Task<IResult> MarkReadyToBill(HttpContext http, long jobId, RevenueReadinessService svc, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(http, "finance.job.ready_to_bill");
        if (denied is not null) return denied;

        var outcome = await svc.MarkJobReadyToBillAsync(EndpointMappings.GetCompanyId(http), jobId, ct);
        return outcome.Success
            ? Results.Ok(ApiResponse<object>.Ok(outcome, outcome.Message))
            : Results.Conflict(ApiResponse<object>.Fail(outcome.Message, outcome.RecommendationCreated ? "Revenue leakage recommendation created" : null));
    }

    private static async Task<IResult> ListInvoiceDrafts(HttpContext http, RevenueReadinessService svc, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(http, "finance.invoice_draft.read");
        if (denied is not null) return denied;

        var drafts = await svc.ListInvoiceDraftsAsync(EndpointMappings.GetCompanyId(http), ct);
        return Results.Ok(ApiResponse<object>.Ok(new { items = drafts }));
    }

    private static async Task<IResult> GetInvoiceDraft(HttpContext http, Guid id, RevenueReadinessService svc, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(http, "finance.invoice_draft.read");
        if (denied is not null) return denied;

        var draft = await svc.GetInvoiceDraftAsync(EndpointMappings.GetCompanyId(http), id, ct);
        return draft is null
            ? Results.NotFound(ApiResponse<object>.Fail("Invoice draft not found"))
            : Results.Ok(ApiResponse<object>.Ok(draft));
    }

    private static async Task<IResult> CreateInvoiceDraft(HttpContext http, long jobId, RevenueReadinessService svc, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(http, "finance.invoice_draft.create");
        if (denied is not null) return denied;

        var idempotencyKey = http.Request.Headers.TryGetValue("Idempotency-Key", out var headerValue) ? headerValue.FirstOrDefault() : null;
        var outcome = await svc.CreateInvoiceDraftFromJobAsync(EndpointMappings.GetCompanyId(http), jobId, idempotencyKey, ct);

        if (!outcome.Success)
        {
            return Results.Conflict(ApiResponse<object>.Fail(outcome.Message));
        }

        if (outcome.Draft is null)
        {
            return Results.Conflict(ApiResponse<object>.Fail("Invoice draft could not be created"));
        }

        return outcome.Replay
            ? Results.Ok(ApiResponse<object>.Ok(outcome.Draft, outcome.Message))
            : Results.Created($"/api/invoice-drafts/{outcome.Draft.Id}", ApiResponse<object>.Ok(outcome.Draft, outcome.Message));
    }

    private static async Task<IResult> UpdateInvoiceDraft(HttpContext http, Guid id, Dictionary<string, object?> body, RevenueReadinessService svc, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(http, "finance.invoice_draft.update");
        if (denied is not null) return denied;

        var outcome = await svc.UpdateInvoiceDraftAsync(
            EndpointMappings.GetCompanyId(http),
            id,
            Str(body, "status"),
            Str(body, "metadataJson"),
            ct);

        if (outcome.ApprovalRequired)
        {
            return Results.Accepted($"/api/approval-requests/{outcome.ApprovalRequestId}", ApiResponse<object>.Ok(new
            {
                approvalRequired = true,
                approvalRequestId = outcome.ApprovalRequestId,
                message = outcome.Message
            }, outcome.Message));
        }

        return outcome.Success && outcome.Draft is not null
            ? Results.Ok(ApiResponse<object>.Ok(outcome.Draft, outcome.Message))
            : Results.Conflict(ApiResponse<object>.Fail(outcome.Message));
    }

    private static async Task<IResult> IssueInvoiceDraft(HttpContext http, Guid id, Dictionary<string, object?> body, RevenueReadinessService svc, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(http, "finance.invoice.issue");
        if (denied is not null) return denied;

        var outcome = await svc.IssueInvoiceFromDraftAsync(
            EndpointMappings.GetCompanyId(http),
            id,
            Str(body, "idempotencyKey") ?? (http.Request.Headers.TryGetValue("Idempotency-Key", out var headerValue) ? headerValue.FirstOrDefault() : null),
            ct);

        if (outcome.ApprovalRequired)
        {
            return Results.Accepted($"/api/approval-requests/{outcome.ApprovalRequestId}", ApiResponse<object>.Ok(new
            {
                approvalRequired = true,
                approvalRequestId = outcome.ApprovalRequestId,
                message = outcome.Message
            }, outcome.Message));
        }

        return outcome.Success && outcome.Invoice is not null
            ? (outcome.Replay
                ? Results.Ok(ApiResponse<object>.Ok(outcome.Invoice, outcome.Message))
                : Results.Created($"/api/issued-invoices/{outcome.Invoice.Id}", ApiResponse<object>.Ok(outcome.Invoice, outcome.Message)))
            : Results.Conflict(ApiResponse<object>.Fail(outcome.Message));
    }

    private static async Task<IResult> ListIssuedInvoices(HttpContext http, RevenueReadinessService svc, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(http, "finance.invoice.read");
        if (denied is not null) return denied;

        var invoices = await svc.ListIssuedInvoicesAsync(EndpointMappings.GetCompanyId(http), ct);
        return Results.Ok(ApiResponse<object>.Ok(new { items = invoices }));
    }

    private static async Task<IResult> GetIssuedInvoice(HttpContext http, Guid id, RevenueReadinessService svc, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(http, "finance.invoice.read");
        if (denied is not null) return denied;

        var invoice = await svc.GetIssuedInvoiceAsync(EndpointMappings.GetCompanyId(http), id, ct);
        return invoice is null
            ? Results.NotFound(ApiResponse<object>.Fail("Issued invoice not found"))
            : Results.Ok(ApiResponse<object>.Ok(invoice));
    }

    private static async Task<IResult> RecordIssuedInvoicePayment(HttpContext http, Guid id, Dictionary<string, object?> body, RevenueReadinessService svc, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(http, "finance.invoice.payment.record");
        if (denied is not null) return denied;

        if (!decimal.TryParse(Str(body, "amount"), out var amount) || amount <= 0)
        {
            return Results.BadRequest(ApiResponse<object>.Fail("amount is required"));
        }

        var payment = await svc.RecordInvoicePaymentAsync(
            EndpointMappings.GetCompanyId(http),
            id,
            amount,
            Str(body, "currency") ?? "USD",
            Str(body, "paymentReference") ?? $"PAY-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}",
            Str(body, "paymentMethod") ?? "manual",
            Str(body, "metadataJson"),
            ct);

        return payment is null
            ? Results.NotFound(ApiResponse<object>.Fail("Issued invoice not found"))
            : Results.Created($"/api/issued-invoices/{id}/payments/{payment.Id}", ApiResponse<object>.Ok(payment, "Payment recorded"));
    }

    private static async Task<IResult> AccountsReceivableSummary(HttpContext http, RevenueReadinessService svc, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(http, "finance.ar.summary.read");
        if (denied is not null) return denied;

        var summary = await svc.GetAccountsReceivableSummaryAsync(EndpointMappings.GetCompanyId(http), ct);
        return Results.Ok(ApiResponse<object>.Ok(summary));
    }

    private static async Task<IResult> DecideApprovalRequest(HttpContext http, long id, Dictionary<string, object?> body, IApprovalWorkflowService approval, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(http, "finance.invoice.issue");
        if (denied is not null) return denied;

        var decision = Str(body, "decision") ?? "approved";
        var notes = Str(body, "notes");
        var actorId = http.Items[EndpointMappings.AuthUserIdItemKey]?.ToString() ?? "unknown";
        var result = approval.Decide(id, actorId, decision, notes);
        return Results.Ok(ApiResponse<object>.Ok(result, "Approval decision recorded"));
    }

    private static async Task<IResult> RevenueSummary(HttpContext http, RevenueReadinessService svc, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(http, "finance.revenue.summary.read");
        if (denied is not null) return denied;

        var summary = await svc.GetRevenueSummaryAsync(EndpointMappings.GetCompanyId(http), ct);
        return Results.Ok(ApiResponse<object>.Ok(summary));
    }

    private static async Task<IResult> CustomerSummary(HttpContext http, long customerId, RevenueReadinessService svc, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(http, "customer.account.summary.read");
        if (denied is not null) return denied;

        var summary = await svc.GetCustomerSummaryAsync(EndpointMappings.GetCompanyId(http), customerId, ct);
        return summary is null
            ? Results.NotFound(ApiResponse<object>.Fail("Customer not found"))
            : Results.Ok(ApiResponse<object>.Ok(summary));
    }

    private static string? Str(Dictionary<string, object?> body, string key)
        => body.TryGetValue(key, out var value) && value is not null ? value.ToString() : null;
}
