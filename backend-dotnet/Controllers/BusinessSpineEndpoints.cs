using System.Globalization;
using System.Text.Json;
using Opstrax.Api.Data;
using Opstrax.Api.Foundation;
using Opstrax.Api.DTOs;
using Opstrax.Api.Services;

namespace Opstrax.Api.Controllers;

public static class BusinessSpineEndpoints
{
    public static void MapBusinessSpineEndpoints(this WebApplication app)
    {
        app.MapGet("/api/business/profile", GetProfile);
        app.MapPut("/api/business/profile", UpsertProfile);

        app.MapGet("/api/rate-cards", ListRateCards);
        app.MapPost("/api/rate-cards", CreateRateCard);
        app.MapPatch("/api/rate-cards/{id:long}", UpdateRateCard);

        app.MapGet("/api/job-charges", ListJobCharges);
        app.MapPost("/api/job-charges", CreateJobCharge);
        app.MapPatch("/api/job-charges/{id:long}", UpdateJobCharge);

        app.MapGet("/api/customers/{customerId:long}/sites", ListCustomerSites);
        app.MapPost("/api/customers/{customerId:long}/sites", UpsertCustomerSite);

        app.MapGet("/api/contracts/{contractId:long}/versions", ListContractVersions);
        app.MapPost("/api/contracts/{contractId:long}/versions", CaptureContractVersion);
    }

    private static async Task<IResult> GetProfile(HttpContext http, BusinessSpineService svc, CancellationToken ct)
    {
        var guard = EndpointMappings.RequirePermission(http, "settings:view");
        if (guard is not null) return guard;
        var companyId = EndpointMappings.GetCompanyId(http);
        return Results.Ok(ApiResponse<object>.Ok(await svc.GetOrCreateProfileAsync(companyId, ct)));
    }

    private static async Task<IResult> UpsertProfile(HttpContext http, Dictionary<string, object?> body, BusinessSpineService svc, CancellationToken ct)
    {
        var guard = EndpointMappings.RequirePermission(http, "settings:manage");
        if (guard is not null) return guard;

        var profile = await svc.UpsertProfileAsync(
            EndpointMappings.GetCompanyId(http),
            Str(body, "verticalKey") ?? "generic",
            Str(body, "customerLabelSingular") ?? "Customer",
            Str(body, "customerLabelPlural") ?? "Customers",
            Str(body, "contractLabelSingular") ?? "Contract",
            Str(body, "contractLabelPlural") ?? "Contracts",
            Str(body, "rateCardLabelSingular") ?? "Rate Card",
            Str(body, "rateCardLabelPlural") ?? "Rate Cards",
            Str(body, "jobLabelSingular") ?? "Job",
            Str(body, "jobLabelPlural") ?? "Jobs",
            Str(body, "tripLabelSingular") ?? "Trip",
            Str(body, "tripLabelPlural") ?? "Trips",
            Str(body, "chargeLabelSingular") ?? "Charge",
            Str(body, "chargeLabelPlural") ?? "Charges",
            Bool(body, "useGenericLabels", true),
            Str(body, "notes"),
            ct);

        return Results.Ok(ApiResponse<object>.Ok(profile, "Business surface profile updated"));
    }

    private static async Task<IResult> ListRateCards(HttpContext http, BusinessSpineService svc, CancellationToken ct)
    {
        var guard = EndpointMappings.RequirePermission(http, "rate_card.read");
        if (guard is not null) return guard;
        var companyId = EndpointMappings.GetCompanyId(http);
        long? contractId = null;
        if (http.Request.Query.TryGetValue("contractId", out var contractIdValue) && long.TryParse(contractIdValue.FirstOrDefault(), out var parsedContractId))
        {
            contractId = parsedContractId;
        }

        var rows = await svc.ListRateCardsAsync(companyId, contractId, ct);
        return Results.Ok(ApiResponse<object>.Ok(rows));
    }

    private static async Task<IResult> CreateRateCard(HttpContext http, Dictionary<string, object?> body, BusinessSpineService svc, IDomainEventPublisher events, CancellationToken ct)
    {
        var guard = EndpointMappings.RequirePermission(http, "rate_card.create");
        if (guard is not null) return guard;

        if (!TryDate(body, "effectiveDate", out var effectiveDate))
        {
            return Results.BadRequest(ApiResponse<object>.Fail("effectiveDate is required"));
        }

        var rateCard = await svc.CreateRateCardAsync(
            EndpointMappings.GetCompanyId(http),
            Str(body, "rateCardCode") ?? $"RC-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
            Str(body, "rateCardName") ?? "Rate Card",
            Long(body, "customerId"),
            Long(body, "contractId"),
            Str(body, "billingBasis"),
            Str(body, "serviceScope"),
            Str(body, "originZone"),
            Str(body, "destinationZone"),
            Str(body, "vehicleType"),
            Str(body, "currency"),
            Dec(body, "baseRate", 0m),
            DecN(body, "minimumCharge"),
            DecN(body, "fuelSurchargePercent"),
            Str(body, "accessorialType"),
            effectiveDate,
            TryDateN(body, "expiryDate"),
            Str(body, "status"),
            Str(body, "correlationId"),
            Str(body, "causationId"),
            Str(body, "notes"),
            ct);

        _ = events.Publish(
            EndpointMappings.GetCompanyId(http).ToString(CultureInfo.InvariantCulture),
            "rate_card.created",
            "rate_card",
            rateCard.Id.ToString(CultureInfo.InvariantCulture),
            JsonSerializer.Serialize(new
            {
                rateCard.Id,
                rateCard.CompanyId,
                rateCard.RateCardCode,
                rateCard.RateCardName,
                rateCard.ContractId,
                rateCard.CustomerId,
                rateCard.Status
            }),
            Str(body, "correlationId"),
            Str(body, "causationId"),
            rateCard.RateCardCode);

        return Results.Created($"/api/rate-cards/{rateCard.Id}", ApiResponse<object>.Ok(rateCard, "Rate card created"));
    }

    private static async Task<IResult> ListJobCharges(HttpContext http, BusinessSpineService svc, CancellationToken ct)
    {
        var guard = EndpointMappings.RequirePermission(http, "charge.read");
        if (guard is not null) return guard;
        var companyId = EndpointMappings.GetCompanyId(http);
        long? jobId = null;
        if (http.Request.Query.TryGetValue("jobId", out var jobIdValue) && long.TryParse(jobIdValue.FirstOrDefault(), out var parsedJobId))
        {
            jobId = parsedJobId;
        }

        var rows = await svc.ListJobChargesAsync(companyId, jobId, ct);
        return Results.Ok(ApiResponse<object>.Ok(rows));
    }

    private static async Task<IResult> CreateJobCharge(HttpContext http, Dictionary<string, object?> body, BusinessSpineService svc, IDomainEventPublisher events, CancellationToken ct)
    {
        var guard = EndpointMappings.RequirePermission(http, "charge.create");
        if (guard is not null) return guard;

        if (!Long(body, "jobId").HasValue)
        {
            return Results.BadRequest(ApiResponse<object>.Fail("jobId is required"));
        }

        var charge = await svc.CreateJobChargeAsync(
            EndpointMappings.GetCompanyId(http),
            Long(body, "jobId")!.Value,
            Long(body, "tripId"),
            Long(body, "rateCardId"),
            Str(body, "chargeCode") ?? $"CHG-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
            Str(body, "chargeName") ?? "Charge",
            Str(body, "chargeType") ?? "base",
            Str(body, "description"),
            Dec(body, "quantity", 1m),
            Dec(body, "unitRate", 0m),
            Dec(body, "amount", 0m),
            Str(body, "currency"),
            Str(body, "status"),
            Str(body, "correlationId"),
            Str(body, "causationId"),
            Long(body, "approvedByUserId"),
            TryDto(body, "approvedAt"),
            ct);

        _ = events.Publish(
            EndpointMappings.GetCompanyId(http).ToString(CultureInfo.InvariantCulture),
            "charge.created",
            "job_charge",
            charge.Id.ToString(CultureInfo.InvariantCulture),
            JsonSerializer.Serialize(new
            {
                charge.Id,
                charge.CompanyId,
                charge.JobId,
                charge.TripId,
                charge.RateCardId,
                charge.Amount,
                charge.Status
            }),
            Str(body, "correlationId"),
            Str(body, "causationId"),
            Str(body, "chargeCode"));

        return Results.Created($"/api/job-charges/{charge.Id}", ApiResponse<object>.Ok(charge, "Job charge created"));
    }

    private static async Task<IResult> UpdateRateCard(HttpContext http, long id, Dictionary<string, object?> body, BusinessSpineService svc, IApprovalWorkflowService approval, IDomainEventPublisher events, CancellationToken ct)
    {
        var guard = EndpointMappings.RequirePermission(http, "rate_card.update");
        if (guard is not null) return guard;

        var companyId = EndpointMappings.GetCompanyId(http);
        var current = await svc.GetRateCardByIdAsync(companyId, id, ct);
        if (current is null)
        {
            return Results.NotFound(ApiResponse<object>.Fail("Rate card not found"));
        }

        var approvalRequired = string.Equals(current.Status, "Active", StringComparison.OrdinalIgnoreCase) &&
            (body.ContainsKey("baseRate") ||
             body.ContainsKey("contractId") ||
             body.ContainsKey("customerId") ||
             body.ContainsKey("billingBasis") ||
             body.ContainsKey("accessorialType"));

        if (approvalRequired)
        {
            var approvalRequest = approval.CreateRequest(
                companyId.ToString(CultureInfo.InvariantCulture),
                ActorTypes.TenantUser,
                http.Items[EndpointMappings.AuthUserIdItemKey]?.ToString(),
                "customer.contract.rate_change",
                "rate_card",
                id.ToString(CultureInfo.InvariantCulture),
                JsonSerializer.Serialize(new
                {
                    rateCardId = id,
                    currentRate = current.BaseRate,
                    requestedRate = body.TryGetValue("baseRate", out var baseRate) ? baseRate : null,
                    contractId = body.TryGetValue("contractId", out var contractId) ? contractId : null,
                    customerId = body.TryGetValue("customerId", out var customerId) ? customerId : null,
                    billingBasis = body.TryGetValue("billingBasis", out var billingBasis) ? billingBasis : null,
                    accessorialType = body.TryGetValue("accessorialType", out var accessorialType) ? accessorialType : null,
                    status = current.Status,
                }),
                "high");

            return Results.Accepted(
                $"/api/approval-requests/{approvalRequest.Id}",
                ApiResponse<object>.Ok(new
                {
                    approvalRequired = true,
                    approvalRequestId = approvalRequest.Id,
                    message = "Active rate card change requires approval"
                }, "Active rate card change requires approval"));
        }

        var rateCard = await svc.UpdateRateCardAsync(
            companyId,
            id,
            Str(body, "rateCardCode"),
            Str(body, "rateCardName"),
            Long(body, "customerId"),
            Long(body, "contractId"),
            Str(body, "billingBasis"),
            Str(body, "serviceScope"),
            Str(body, "originZone"),
            Str(body, "destinationZone"),
            Str(body, "vehicleType"),
            Str(body, "currency"),
            body.ContainsKey("baseRate") ? DecN(body, "baseRate") : null,
            DecN(body, "minimumCharge"),
            DecN(body, "fuelSurchargePercent"),
            Str(body, "accessorialType"),
            TryDateN(body, "effectiveDate"),
            TryDateN(body, "expiryDate"),
            Str(body, "status"),
            Str(body, "correlationId"),
            Str(body, "causationId"),
            Str(body, "notes"),
            ct);

        if (rateCard is null)
        {
            return Results.NotFound(ApiResponse<object>.Fail("Rate card not found"));
        }

        _ = events.Publish(
            companyId.ToString(CultureInfo.InvariantCulture),
            "rate_card.updated",
            "rate_card",
            rateCard.Id.ToString(CultureInfo.InvariantCulture),
            JsonSerializer.Serialize(new { rateCard.Id, rateCard.CompanyId, rateCard.RateCardCode, rateCard.Status }),
            Str(body, "correlationId"),
            Str(body, "causationId"),
            rateCard.RateCardCode);

        return Results.Ok(ApiResponse<object>.Ok(rateCard, "Rate card updated"));
    }

    private static async Task<IResult> UpdateJobCharge(HttpContext http, long id, Dictionary<string, object?> body, BusinessSpineService svc, IDomainEventPublisher events, CancellationToken ct)
    {
        var guard = EndpointMappings.RequirePermission(http, "charge.update");
        if (guard is not null) return guard;

        var companyId = EndpointMappings.GetCompanyId(http);
        var charge = await svc.UpdateJobChargeAsync(
            companyId,
            id,
            Long(body, "jobId"),
            Long(body, "tripId"),
            Long(body, "rateCardId"),
            Str(body, "chargeCode"),
            Str(body, "chargeName"),
            Str(body, "chargeType"),
            Str(body, "description"),
            body.ContainsKey("quantity") ? DecN(body, "quantity") : null,
            body.ContainsKey("unitRate") ? DecN(body, "unitRate") : null,
            body.ContainsKey("amount") ? DecN(body, "amount") : null,
            Str(body, "currency"),
            Str(body, "status"),
            Str(body, "correlationId"),
            Str(body, "causationId"),
            Long(body, "approvedByUserId"),
            TryDto(body, "approvedAt"),
            ct);

        if (charge is null)
        {
            return Results.NotFound(ApiResponse<object>.Fail("Job charge not found"));
        }

        _ = events.Publish(
            companyId.ToString(CultureInfo.InvariantCulture),
            "charge.updated",
            "job_charge",
            charge.Id.ToString(CultureInfo.InvariantCulture),
            JsonSerializer.Serialize(new { charge.Id, charge.CompanyId, charge.JobId, charge.Status, charge.Amount }),
            Str(body, "correlationId"),
            Str(body, "causationId"),
            charge.ChargeCode);

        return Results.Ok(ApiResponse<object>.Ok(charge, "Job charge updated"));
    }

    private static async Task<IResult> ListCustomerSites(HttpContext http, long customerId, CommercialFoundationService commercial, CancellationToken ct)
    {
        var guard = EndpointMappings.RequirePermission(http, "customers:view");
        if (guard is not null) return guard;

        var companyId = EndpointMappings.GetCompanyId(http);
        var sites = await commercial.ListCustomerSitesAsync(companyId, customerId, ct);
        return Results.Ok(ApiResponse<object>.Ok(new { items = sites }));
    }

    private static async Task<IResult> UpsertCustomerSite(HttpContext http, long customerId, Dictionary<string, object?> body, CommercialFoundationService commercial, CancellationToken ct)
    {
        var guard = EndpointMappings.RequirePermission(http, "customer.account.update");
        if (guard is not null) return guard;

        var site = await commercial.UpsertCustomerSiteAsync(
            EndpointMappings.GetCompanyId(http),
            customerId,
            Str(body, "siteCode") ?? Str(body, "site_code") ?? $"SITE-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}",
            Str(body, "siteName") ?? Str(body, "site_name") ?? "Customer Site",
            Str(body, "siteType") ?? Str(body, "site_type"),
            Str(body, "addressLine1") ?? Str(body, "address_line1"),
            Str(body, "addressLine2") ?? Str(body, "address_line2"),
            Str(body, "city"),
            Str(body, "state"),
            Str(body, "postalCode") ?? Str(body, "postal_code"),
            Str(body, "countryCode") ?? Str(body, "country_code"),
            DecN(body, "geoLatitude") ?? DecN(body, "geo_latitude"),
            DecN(body, "geoLongitude") ?? DecN(body, "geo_longitude"),
            Str(body, "accessInstructions") ?? Str(body, "access_instructions"),
            Str(body, "externalReference") ?? Str(body, "external_reference"),
            Str(body, "status"),
            Str(body, "sourceChannel") ?? Str(body, "source_channel"),
            Str(body, "clientGeneratedId") ?? Str(body, "client_generated_id"),
            Str(body, "idempotencyKey") ?? Str(body, "idempotency_key") ?? (http.Request.Headers.TryGetValue("Idempotency-Key", out var idemHeader) ? idemHeader.FirstOrDefault() : null),
            Str(body, "correlationId") ?? Str(body, "correlation_id"),
            Str(body, "causationId") ?? Str(body, "causation_id"),
            Str(body, "metadataJson") ?? Str(body, "metadata_json") ?? "{}",
            ct);

        return Results.Ok(ApiResponse<object>.Ok(site, "Customer site saved"));
    }

    private static async Task<IResult> ListContractVersions(HttpContext http, long contractId, CommercialFoundationService commercial, CancellationToken ct)
    {
        var guard = EndpointMappings.RequirePermission(http, "contract.view");
        if (guard is not null) return guard;

        var versions = await commercial.ListContractVersionsAsync(EndpointMappings.GetCompanyId(http), contractId, ct);
        return Results.Ok(ApiResponse<object>.Ok(new { items = versions }));
    }

    private static async Task<IResult> CaptureContractVersion(HttpContext http, long contractId, Dictionary<string, object?> body, Database db, CommercialFoundationService commercial, CancellationToken ct)
    {
        var guard = EndpointMappings.RequirePermission(http, "contract.update");
        if (guard is not null) return guard;

        var companyId = EndpointMappings.GetCompanyId(http);
        var contractRow = await db.QuerySingleAsync(
            "SELECT * FROM contracts WHERE id=@id AND company_id=@companyId AND deleted_at IS NULL LIMIT 1",
            c =>
            {
                c.Parameters.AddWithValue("@id", contractId);
                c.Parameters.AddWithValue("@companyId", companyId);
            }, ct);

        if (contractRow is null)
        {
            return Results.NotFound(ApiResponse<object>.Fail("Contract not found"));
        }

        var version = await commercial.CaptureContractVersionAsync(
            companyId,
            contractId,
            contractRow,
            Str(body, "sourceChannel") ?? Str(body, "source_channel"),
            Str(body, "clientGeneratedId") ?? Str(body, "client_generated_id"),
            Str(body, "idempotencyKey") ?? Str(body, "idempotency_key"),
            Str(body, "correlationId") ?? Str(body, "correlation_id"),
            Str(body, "causationId") ?? Str(body, "causation_id"),
            Str(body, "versionLabel") ?? Str(body, "version_label"),
            ct);

        return version is null
            ? Results.Conflict(ApiResponse<object>.Fail("Contract version could not be captured"))
            : Results.Created($"/api/contracts/{contractId}/versions/{version.Id}", ApiResponse<object>.Ok(version, "Contract version captured"));
    }

    private static string? Str(Dictionary<string, object?> body, string key)
        => body.TryGetValue(key, out var value) && value is not null && value is not DBNull ? Convert.ToString(value, CultureInfo.InvariantCulture) : null;

    private static long? Long(Dictionary<string, object?> body, string key)
        => body.TryGetValue(key, out var value) && value is not null && value is not DBNull ? Convert.ToInt64(value, CultureInfo.InvariantCulture) : null;

    private static decimal Dec(Dictionary<string, object?> body, string key, decimal fallback = 0m)
        => body.TryGetValue(key, out var value) && value is not null && value is not DBNull ? Convert.ToDecimal(value, CultureInfo.InvariantCulture) : fallback;

    private static decimal? DecN(Dictionary<string, object?> body, string key)
        => body.TryGetValue(key, out var value) && value is not null && value is not DBNull ? Convert.ToDecimal(value, CultureInfo.InvariantCulture) : null;

    private static bool Bool(Dictionary<string, object?> body, string key, bool fallback = false)
        => body.TryGetValue(key, out var value) && value is not null && value is not DBNull ? Convert.ToBoolean(value, CultureInfo.InvariantCulture) : fallback;

    private static bool TryDate(Dictionary<string, object?> body, string key, out DateOnly value)
    {
        value = default;
        if (!body.TryGetValue(key, out var raw) || raw is null || raw is DBNull)
        {
            return false;
        }

        if (raw is DateOnly dateOnly)
        {
            value = dateOnly;
            return true;
        }

        if (raw is DateTime dateTime)
        {
            value = DateOnly.FromDateTime(dateTime);
            return true;
        }

        return DateOnly.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }

    private static DateOnly? TryDateN(Dictionary<string, object?> body, string key)
        => TryDate(body, key, out var value) ? value : null;

    private static DateTimeOffset? TryDto(Dictionary<string, object?> body, string key)
    {
        if (!body.TryGetValue(key, out var raw) || raw is null || raw is DBNull)
        {
            return null;
        }

        return raw switch
        {
            DateTimeOffset dto => dto,
            DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
            _ when DateTimeOffset.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) => parsed,
            _ => null
        };
    }
}
