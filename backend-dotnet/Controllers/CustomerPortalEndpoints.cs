using Opstrax.Api.DTOs;
using Opstrax.Api.Services;

namespace Opstrax.Api.Controllers;

// Customer-facing portal endpoints. Distinct security boundary from internal RBAC:
//   1. RequirePermission("customer_portal:view") — must be a portal-permissioned session.
//   2. The authenticated user must be bound to a customer_id (a portal customer-user);
//      internal staff (no customer_id) are denied with 403.
//   3. Every query is scoped by BOTH company_id AND the resolved customer_id, so a
//      customer never sees another customer's data even inside the same tenant company.
public static class CustomerPortalEndpoints
{
    public static void MapCustomerPortalEndpoints(this WebApplication app)
    {
        app.MapGet("/api/portal/invoices", PortalInvoices);
        app.MapGet("/api/portal/jobs", PortalJobs);
        app.MapGet("/api/portal/jobs/{jobId:long}", PortalJobDetail);
        app.MapGet("/api/portal/jobs/{jobId:long}/proofs", PortalJobProofs);
        app.MapPost("/api/portal/feedback", PortalSubmitFeedback);
        app.MapGet("/api/portal/feedback", PortalFeedback);
    }

    // Resolves (companyId, customerId) for the authenticated portal user, or returns a
    // denial IResult (401 no permission, 403 not a portal user).
    private static async Task<(long CompanyId, long CustomerId, IResult? Denied)> ResolvePrincipalAsync(HttpContext http, CustomerPortalService svc, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(http, "customer_portal:view");
        if (denied is not null) return (0, 0, denied);

        var companyId = EndpointMappings.GetCompanyId(http);
        var userId = http.Items.TryGetValue(EndpointMappings.AuthUserIdItemKey, out var uid) && uid is not null
            ? Convert.ToInt64(uid) : 0;

        var customerId = await svc.ResolveCustomerIdForUserAsync(companyId, userId, ct);
        if (customerId is null)
        {
            return (companyId, 0, Results.Json(
                ApiResponse<object>.Fail("Forbidden", "This account is not a customer-portal user"),
                statusCode: StatusCodes.Status403Forbidden));
        }
        return (companyId, customerId.Value, null);
    }

    private static async Task<IResult> PortalInvoices(HttpContext http, CustomerPortalService svc, CancellationToken ct)
    {
        var (companyId, customerId, denied) = await ResolvePrincipalAsync(http, svc, ct);
        if (denied is not null) return denied;
        var invoices = await svc.GetOwnInvoicesAsync(companyId, customerId, ct);
        return Results.Ok(ApiResponse<object>.Ok(new { items = invoices }));
    }

    private static async Task<IResult> PortalJobs(HttpContext http, CustomerPortalService svc, CancellationToken ct)
    {
        var (companyId, customerId, denied) = await ResolvePrincipalAsync(http, svc, ct);
        if (denied is not null) return denied;
        var jobs = await svc.GetOwnJobsAsync(companyId, customerId, ct);
        return Results.Ok(ApiResponse<object>.Ok(new { items = jobs }));
    }

    private static async Task<IResult> PortalJobDetail(HttpContext http, long jobId, CustomerPortalService svc, CancellationToken ct)
    {
        var (companyId, customerId, denied) = await ResolvePrincipalAsync(http, svc, ct);
        if (denied is not null) return denied;
        var detail = await svc.GetOwnJobDetailAsync(companyId, customerId, jobId, ct);
        return detail is null
            ? Results.NotFound(ApiResponse<object>.Fail("Job not found"))
            : Results.Ok(ApiResponse<object>.Ok(detail));
    }

    private static async Task<IResult> PortalJobProofs(HttpContext http, long jobId, CustomerPortalService svc, CancellationToken ct)
    {
        var (companyId, customerId, denied) = await ResolvePrincipalAsync(http, svc, ct);
        if (denied is not null) return denied;
        var proofs = await svc.GetOwnProofsAsync(companyId, customerId, jobId, ct);
        return Results.Ok(ApiResponse<object>.Ok(new { items = proofs }));
    }

    private static async Task<IResult> PortalSubmitFeedback(HttpContext http, Dictionary<string, object?> body, CustomerPortalService svc, CancellationToken ct)
    {
        var (companyId, customerId, denied) = await ResolvePrincipalAsync(http, svc, ct);
        if (denied is not null) return denied;

        if (!long.TryParse(Str(body, "jobId"), out var jobId) || jobId <= 0)
        {
            return Results.BadRequest(ApiResponse<object>.Fail("jobId is required"));
        }
        int? rating = int.TryParse(Str(body, "rating"), out var r) ? r : null;

        var feedback = await svc.SubmitFeedbackAsync(
            companyId, customerId, jobId, rating, Str(body, "comment"), Str(body, "feedbackType"), Str(body, "subject"), ct);

        return feedback is null
            ? Results.NotFound(ApiResponse<object>.Fail("Job not found", "You can only submit feedback for your own jobs"))
            : Results.Created($"/api/portal/feedback/{feedback["id"]}", ApiResponse<object>.Ok(feedback, "Feedback submitted"));
    }

    private static async Task<IResult> PortalFeedback(HttpContext http, CustomerPortalService svc, CancellationToken ct)
    {
        var (companyId, customerId, denied) = await ResolvePrincipalAsync(http, svc, ct);
        if (denied is not null) return denied;
        var feedback = await svc.GetOwnFeedbackAsync(companyId, customerId, ct);
        return Results.Ok(ApiResponse<object>.Ok(new { items = feedback }));
    }

    private static string? Str(Dictionary<string, object?> body, string key)
        => body.TryGetValue(key, out var value) && value is not null ? value.ToString() : null;
}
