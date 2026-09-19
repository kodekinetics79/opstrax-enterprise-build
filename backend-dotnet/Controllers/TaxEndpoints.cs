using Opstrax.Api.DTOs;
using Opstrax.Api.Services;
using Opstrax.Api.Data;

namespace Opstrax.Api.Controllers;

// Tax engine (ADR-008 P3) endpoints: config (profiles/rules/customer-status/seller-registration +
// maker-checker publish) and the invoice-scoped tax breakdown (preview + read). RBAC tax.* folds into
// finance:manage/billing:manage; the invoice-scoped reads reuse the existing revenue tokens.
public static class TaxEndpoints
{
    private static string? RegimeFor(string country) => country switch
    {
        "SA" => "zatca_vat",
        "CA" => "gst",
        "US" => "us_sales_tax",
        _ => null,
    };

    private static async Task<(TenantMarketPolicyService.MarketContext? Market, IResult? Error)> MarketAsync(HttpContext http, Database db, CancellationToken ct)
    {
        var market = await new TenantMarketPolicyService(db).GetAsync(EndpointMappings.GetCompanyId(http), ct);
        return market is null
            ? (null, Results.Json(ApiResponse<object>.Fail("Operating market not assigned"), statusCode: StatusCodes.Status409Conflict))
            : (market, null);
    }

    private static async Task<IResult?> RequireMarketProfileAsync(
        HttpContext http, long profileId, TaxService svc, Database db, CancellationToken ct)
    {
        var (market, error) = await MarketAsync(http, db, ct);
        if (error is not null) return error;
        var expectedRegime = RegimeFor(market!.CountryCode);
        if (expectedRegime is null)
            return Results.Json(
                ApiResponse<object>.Fail("Tax regime is not configured", $"No approved tax regime is available for {market.CountryName}."),
                statusCode: StatusCodes.Status409Conflict);

        var profile = await svc.GetProfileAsync(EndpointMappings.GetCompanyId(http), profileId, ct);
        return profile is null ||
               !string.Equals(profile.GetValueOrDefault("regime")?.ToString(), expectedRegime, StringComparison.OrdinalIgnoreCase)
            ? Results.NotFound(ApiResponse<object>.Fail("Tax profile not found"))
            : null;
    }

    public static void MapTaxEndpoints(this WebApplication app)
    {
        app.MapGet("/api/tax/profiles", ListProfiles);
        app.MapGet("/api/tax/profiles/{id:long}", GetProfile);
        app.MapPost("/api/tax/profiles", UpsertProfile);
        app.MapPost("/api/tax/profiles/{id:long}/rules", UpsertRule);
        app.MapPost("/api/tax/profiles/{id:long}/publish", PublishProfile);
        app.MapGet("/api/tax/customer-status/{customerId:long}", GetCustomerStatus);
        app.MapPost("/api/tax/customer-status", UpsertCustomerStatus);
        app.MapGet("/api/tax/seller-registration", GetSellerRegistration);
        app.MapPost("/api/tax/seller-registration", UpsertSellerRegistration);
        app.MapPost("/api/invoice-drafts/{id:guid}/tax/preview", PreviewDraftTax);
        app.MapGet("/api/invoice-drafts/{id:guid}/tax", GetDraftTax);
        app.MapGet("/api/invoices/{id:guid}/tax", GetIssuedTax);
    }

    private static async Task<IResult> ListProfiles(HttpContext http, TaxService svc, Database db, CancellationToken ct)
    {
        if (EndpointMappings.RequirePermission(http, "tax.read") is { } d) return d;
        var (market, error) = await MarketAsync(http, db, ct); if (error is not null) return error;
        var expected = RegimeFor(market!.CountryCode);
        var rows = await svc.ListProfilesAsync(EndpointMappings.GetCompanyId(http), ct);
        return Results.Ok(ApiResponse<object>.Ok(rows.Where(r => string.Equals(r.GetValueOrDefault("regime")?.ToString(), expected, StringComparison.OrdinalIgnoreCase)).ToList()));
    }

    private static async Task<IResult> GetProfile(HttpContext http, long id, TaxService svc, Database db, CancellationToken ct)
    {
        if (EndpointMappings.RequirePermission(http, "tax.read") is { } d) return d;
        var (market, error) = await MarketAsync(http, db, ct); if (error is not null) return error;
        var p = await svc.GetProfileAsync(EndpointMappings.GetCompanyId(http), id, ct);
        if (p is not null && !string.Equals(p.GetValueOrDefault("regime")?.ToString(), RegimeFor(market!.CountryCode), StringComparison.OrdinalIgnoreCase)) p = null;
        return p is null ? Results.NotFound(ApiResponse<object>.Fail("Tax profile not found")) : Results.Ok(ApiResponse<object>.Ok(p));
    }

    private static async Task<IResult> UpsertProfile(HttpContext http, Dictionary<string, object?> body, TaxService svc, Database db, CancellationToken ct)
    {
        if (EndpointMappings.RequirePermission(http, "tax.manage") is { } d) return d;
        var (market, error) = await MarketAsync(http, db, ct); if (error is not null) return error;
        var expectedRegime = RegimeFor(market!.CountryCode);
        var requestedRegime = body.GetValueOrDefault("regime")?.ToString()?.Trim().ToLowerInvariant();
        if (expectedRegime is null || requestedRegime != expectedRegime)
            return Results.Json(ApiResponse<object>.Fail("Tax regime is locked", $"{market.CountryName} tenants can only configure {expectedRegime ?? "the assigned market regime"}."), statusCode: StatusCodes.Status409Conflict);
        body["currency"] = market.Currency;
        var userId = EndpointMappings.GetUserId(http);
        var o = await svc.UpsertProfileAsync(EndpointMappings.GetCompanyId(http), body, userId, ct);
        return o.Ok ? Results.Ok(ApiResponse<object>.Ok(new { o.Id, o.Status }, "Tax profile saved"))
                    : Results.BadRequest(ApiResponse<object>.Fail($"Cannot save profile: {o.Reason}"));
    }

    private static async Task<IResult> UpsertRule(HttpContext http, long id, Dictionary<string, object?> body, TaxService svc, Database db, CancellationToken ct)
    {
        if (EndpointMappings.RequirePermission(http, "tax.manage") is { } d) return d;
        if (await RequireMarketProfileAsync(http, id, svc, db, ct) is { } marketError) return marketError;
        var o = await svc.UpsertRuleAsync(EndpointMappings.GetCompanyId(http), id, body, ct);
        return o.Ok ? Results.Ok(ApiResponse<object>.Ok(new { o.Id }, "Tax rule saved"))
                    : Results.BadRequest(ApiResponse<object>.Fail($"Cannot save rule: {o.Reason}"));
    }

    private static async Task<IResult> PublishProfile(HttpContext http, long id, TaxService svc, Database db, CancellationToken ct)
    {
        if (EndpointMappings.RequirePermission(http, "tax.publish") is { } d) return d;
        if (await RequireMarketProfileAsync(http, id, svc, db, ct) is { } marketError) return marketError;
        var userId = EndpointMappings.GetUserId(http);
        var o = await svc.PublishProfileAsync(EndpointMappings.GetCompanyId(http), id, userId, ct);
        if (o.Ok) return Results.Ok(ApiResponse<object>.Ok(new { o.Id, o.Status }, "Tax profile published"));
        // approval_requested / awaiting_approval are a valid pending state, not an error.
        return o.Reason is "approval_requested" or "awaiting_approval"
            ? Results.Accepted($"/api/tax/profiles/{id}", ApiResponse<object>.Ok(new { o.Id, o.Status, o.Reason }, "Publish pending approval"))
            : Results.BadRequest(ApiResponse<object>.Fail($"Cannot publish: {o.Reason}"));
    }

    private static async Task<IResult> GetCustomerStatus(HttpContext http, long customerId, TaxService svc, CancellationToken ct)
    {
        if (EndpointMappings.RequirePermission(http, "tax.read") is { } d) return d;
        var s = await svc.GetCustomerTaxStatusAsync(EndpointMappings.GetCompanyId(http), customerId, ct);
        return s is null ? Results.NotFound(ApiResponse<object>.Fail("No tax status")) : Results.Ok(ApiResponse<object>.Ok(s));
    }

    private static async Task<IResult> UpsertCustomerStatus(HttpContext http, Dictionary<string, object?> body, TaxService svc, CancellationToken ct)
    {
        if (EndpointMappings.RequirePermission(http, "tax.manage") is { } d) return d;
        await svc.UpsertCustomerTaxStatusAsync(EndpointMappings.GetCompanyId(http), body, ct);
        return Results.Ok(ApiResponse<object>.Ok(new { ok = true }, "Customer tax status saved"));
    }

    private static async Task<IResult> GetSellerRegistration(HttpContext http, TaxService svc, Database db, CancellationToken ct)
    {
        if (EndpointMappings.RequirePermission(http, "tax.read") is { } d) return d;
        var (market, error) = await MarketAsync(http, db, ct); if (error is not null) return error;
        var q = http.Request.Query;
        var requestedJurisdiction = q.TryGetValue("jurisdiction", out var requestedJ) ? requestedJ.ToString().ToUpperInvariant() : market!.CountryCode;
        var requestedRegime = q.TryGetValue("regime", out var requestedR) ? requestedR.ToString().ToLowerInvariant() : RegimeFor(market!.CountryCode);
        if (requestedJurisdiction != market!.CountryCode || requestedRegime != RegimeFor(market.CountryCode))
            return Results.Json(ApiResponse<object>.Fail("Tax jurisdiction is locked", "Seller registration must use the tenant operating market."), statusCode: StatusCodes.Status409Conflict);
        var s = await svc.GetSellerRegistrationAsync(EndpointMappings.GetCompanyId(http),
            market.CountryCode, RegimeFor(market.CountryCode)!, ct);
        return s is null ? Results.NotFound(ApiResponse<object>.Fail("No seller registration")) : Results.Ok(ApiResponse<object>.Ok(s));
    }

    private static async Task<IResult> UpsertSellerRegistration(HttpContext http, Dictionary<string, object?> body, TaxService svc, Database db, CancellationToken ct)
    {
        if (EndpointMappings.RequirePermission(http, "tax.manage") is { } d) return d;
        var (market, error) = await MarketAsync(http, db, ct); if (error is not null) return error;
        var requestedJurisdiction = body.GetValueOrDefault("jurisdiction")?.ToString()?.Trim().ToUpperInvariant();
        var requestedRegime = body.GetValueOrDefault("regime")?.ToString()?.Trim().ToLowerInvariant();
        if ((!string.IsNullOrWhiteSpace(requestedJurisdiction) && requestedJurisdiction != market!.CountryCode) ||
            (!string.IsNullOrWhiteSpace(requestedRegime) && requestedRegime != RegimeFor(market!.CountryCode)))
            return Results.Json(ApiResponse<object>.Fail("Tax jurisdiction is locked", "Seller registration must use the tenant operating market."), statusCode: StatusCodes.Status409Conflict);
        body["jurisdiction"] = market!.CountryCode;
        body["regime"] = RegimeFor(market.CountryCode);
        await svc.UpsertSellerRegistrationAsync(EndpointMappings.GetCompanyId(http), body, ct);
        return Results.Ok(ApiResponse<object>.Ok(new { ok = true }, "Seller registration saved"));
    }

    private static async Task<IResult> PreviewDraftTax(HttpContext http, Guid id, TaxService svc, CancellationToken ct)
    {
        if (EndpointMappings.RequirePermission(http, "finance.invoice_draft.create") is { } d) return d;
        var o = await svc.ComputeForDraftAsync(EndpointMappings.GetCompanyId(http), id, TaxMode.Preview, ct);
        return Results.Ok(ApiResponse<object>.Ok(new
        {
            o.Applied, o.Reason, o.TaxTotal, o.Subtotal, o.Total, o.TaxProfileId,
            Lines = o.Lines.Select(l => new { l.TaxCode, l.TaxCategory, l.Jurisdiction, l.TaxableAmount, l.Rate, l.TaxAmount, l.PriceInclusive })
        }, o.Applied ? "Tax preview" : $"No tax applied: {o.Reason}"));
    }

    private static async Task<IResult> GetDraftTax(HttpContext http, Guid id, TaxService svc, CancellationToken ct)
    {
        if (EndpointMappings.RequirePermission(http, "finance.invoice.read") is { } d) return d;
        return Results.Ok(ApiResponse<object>.Ok(await svc.GetDraftTaxLinesAsync(EndpointMappings.GetCompanyId(http), id, ct)));
    }

    private static async Task<IResult> GetIssuedTax(HttpContext http, Guid id, TaxService svc, CancellationToken ct)
    {
        if (EndpointMappings.RequirePermission(http, "finance.invoice.read") is { } d) return d;
        return Results.Ok(ApiResponse<object>.Ok(await svc.GetIssuedTaxLinesAsync(EndpointMappings.GetCompanyId(http), id, ct)));
    }
}
