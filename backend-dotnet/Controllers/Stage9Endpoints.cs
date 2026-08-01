using Opstrax.Api.Data;
using Opstrax.Api.DTOs;
using Opstrax.Api.Foundation;
using Opstrax.Api.Services;

namespace Opstrax.Api.Controllers;

public static partial class EndpointMappings
{
    public static void MapStage9OperationsEndpoints(this WebApplication app)
    {
        app.MapGet("/api/operations/jobs/{jobId:long}/execution-summary", GetExecutionSummary);
        app.MapGet("/api/jobs/{jobId:long}/smart-assign/recommendations", ListSmartAssignmentRecommendations);
        app.MapPost("/api/jobs/{jobId:long}/smart-assign/recommend", RecommendSmartAssignment);
        app.MapPost("/api/smart-assign/recommendations/{id:long}/accept", AcceptSmartAssignment);
        app.MapPost("/api/smart-assign/recommendations/{id:long}/reject", RejectSmartAssignment);

        app.MapGet("/api/jobs/{jobId:long}/site-access", ListSiteAccessRequirements);
        app.MapPost("/api/jobs/{jobId:long}/site-access", CreateSiteAccessRequirement);
        app.MapPatch("/api/site-access/{id:long}", PatchSiteAccessRequirement);

        app.MapGet("/api/jobs/{jobId:long}/access-documents", ListAccessDocuments);
        app.MapPost("/api/jobs/{jobId:long}/access-documents", CreateAccessDocument);
        app.MapPatch("/api/access-documents/{id:long}/status", PatchAccessDocumentStatus);

        app.MapGet("/api/jobs/{jobId:long}/pickup-authorizations", ListPickupAuthorizations);
        app.MapPost("/api/jobs/{jobId:long}/pickup-authorizations", CreatePickupAuthorization);
        app.MapPatch("/api/pickup-authorizations/{id:long}", PatchPickupAuthorization);

        app.MapGet("/api/jobs/{jobId:long}/warehouse-handovers", ListWarehouseHandovers);
        app.MapPost("/api/jobs/{jobId:long}/warehouse-handovers", CreateWarehouseHandover);
        app.MapPatch("/api/warehouse-handovers/{id:long}", PatchWarehouseHandover);

        app.MapGet("/api/jobs/{jobId:long}/proof-packages", ListProofPackages);
        app.MapPost("/api/jobs/{jobId:long}/proof-packages", CreateProofPackage);
        app.MapGet("/api/proof-packages/{id:long}", GetProofPackage);
        app.MapPatch("/api/proof-packages/{id:long}", PatchProofPackage);
        app.MapPost("/api/proof-packages/{id:long}/submit", SubmitProofPackage);
        app.MapPost("/api/proof-packages/{id:long}/validate", ValidateProofPackage);
        app.MapGet("/api/proof-packages/{proofPackageId:long}/artifacts", ListProofArtifacts);
        app.MapPost("/api/proof-packages/{proofPackageId:long}/artifacts", CreateProofArtifact);
        app.MapGet("/api/proof-packages/{proofPackageId:long}/billing-confidence", GetBillingConfidence);
    }

    private static async Task<IResult> GetExecutionSummary(HttpContext http, long jobId, Stage9OperationalFoundationService svc, Database db, CancellationToken ct)
    {
        var denied = RequireAnyDirectPermission(http, "operations.execution_summary.read");
        if (denied is not null) return denied;
        if (!await JobInAuthorizedScope(http, jobId, db, ct)) return Results.NotFound(ApiResponse<object>.Fail("Job not found"));

        var summary = await svc.GetExecutionSummaryAsync(GetCompanyId(http), jobId, ct);
        return summary is null
            ? Results.NotFound(ApiResponse<object>.Fail("Execution summary not found"))
            : Results.Ok(ApiResponse<object>.Ok(summary));
    }

    private static async Task<IResult> ListSmartAssignmentRecommendations(HttpContext http, long jobId, Stage9OperationalFoundationService svc, Database db, CancellationToken ct)
    {
        var denied = RequireAnyDirectPermission(http, "dispatch.smart_assign.read");
        if (denied is not null) return denied;
        if (!await JobInAuthorizedScope(http, jobId, db, ct)) return Results.NotFound(ApiResponse<object>.Fail("Job not found"));
        var items = await svc.ListSmartAssignmentRecommendationsAsync(GetCompanyId(http), jobId, null, ct);
        return Results.Ok(ApiResponse<object>.Ok(new { items }));
    }

    private static async Task<IResult> RecommendSmartAssignment(HttpContext http, long jobId, Dictionary<string, object?> body, Stage9OperationalFoundationService svc, AuditService audit, Database db, CancellationToken ct)
    {
        var denied = RequireAnyDirectPermission(http, "dispatch.smart_assign.recommend", "dispatch:assign", "dispatch:manage");
        if (denied is not null) return denied;
        if (!await JobInAuthorizedScope(http, jobId, db, ct)) return Results.NotFound(ApiResponse<object>.Fail("Job not found"));

        var idempotencyKey = Str(body, "idempotencyKey")
            ?? (http.Request.Headers.TryGetValue("Idempotency-Key", out var idempotencyHeader) ? idempotencyHeader.FirstOrDefault() : null);
        var tripId = Long(body, "tripId");
        if (tripId.HasValue && !await TripInJobScope(GetCompanyId(http), jobId, tripId.Value, db, ct))
            return Results.BadRequest(ApiResponse<object>.Fail("Trip does not belong to this job and tenant"));
        var driverId = Long(body, "recommendedDriverId");
        var vehicleId = Long(body, "recommendedVehicleId");
        if (driverId.HasValue != vehicleId.HasValue)
            return Results.BadRequest(ApiResponse<object>.Fail("A driver and vehicle must be recommended together"));
        if (driverId.HasValue)
        {
            var assignmentBody = new Dictionary<string, object?>
            {
                ["driverId"] = driverId.Value,
                ["vehicleId"] = vehicleId!.Value,
                ["override"] = false,
            };
            var validation = await ValidateAssignment(http, assignmentBody, db, ct);
            if (validation.Count > 0)
                return Results.BadRequest(ApiResponse<object>.Fail("Recommended resources are not dispatch eligible", validation.ToArray()));
        }
        body["createdBy"] = Convert.ToInt64(http.Items[AuthUserIdItemKey] ?? 0L);
        var result = await svc.RecommendSmartAssignmentAsync(
            GetCompanyId(http),
            jobId,
            tripId,
            body,
            Str(body, "sourceChannel") ?? "api",
            Str(body, "clientGeneratedId"),
            idempotencyKey,
            ct);

        if (result is null)
        {
            return Results.Conflict(ApiResponse<object>.Fail("Recommendation could not be created"));
        }

        await audit.LogAsync(http, "stage9.smart_assignment.recommended", "smart_assignment_recommendations", Convert.ToInt64(result.GetValueOrDefault("id") ?? 0L), ct: ct);
        return Results.Created($"/api/smart-assign/recommendations/{result.GetValueOrDefault("id")}", ApiResponse<object>.Ok(result, "Smart assignment recommendation created"));
    }

    private static async Task<IResult> AcceptSmartAssignment(HttpContext http, long id, Dictionary<string, object?> body, Stage9OperationalFoundationService svc, AuditService audit, Database db, CancellationToken ct)
    {
        var denied = RequireAnyDirectPermission(http, "dispatch.smart_assign.accept", "dispatch:assign", "dispatch:manage");
        if (denied is not null) return denied;
        if (!await EntityJobInAuthorizedScope(http, "smart_assignment_recommendations", id, db, ct))
            return Results.NotFound(ApiResponse<object>.Fail("Smart assignment recommendation not found"));

        var outcome = await svc.AcceptSmartAssignmentAsync(GetCompanyId(http), id, body, ct);
        if (outcome.ApprovalRequired)
        {
            return Results.Accepted($"/api/approval-requests/{outcome.ApprovalRequestId}", ApiResponse<object>.Ok(new
            {
                approvalRequired = true,
                approvalRequestId = outcome.ApprovalRequestId,
                message = outcome.Message
            }, outcome.Message));
        }

        if (outcome.Success && outcome.Entity is not null)
        {
            await audit.LogAsync(http, "stage9.assignment.accepted", "assignment_confirmations", id, ct: ct);
            return Results.Ok(ApiResponse<object>.Ok(outcome.Entity, outcome.Message));
        }

        return Results.Conflict(ApiResponse<object>.Fail(outcome.Message));
    }

    private static async Task<IResult> RejectSmartAssignment(HttpContext http, long id, Dictionary<string, object?> body, Stage9OperationalFoundationService svc, AuditService audit, Database db, CancellationToken ct)
    {
        var denied = RequireAnyDirectPermission(http, "dispatch.smart_assign.reject", "dispatch:manage");
        if (denied is not null) return denied;
        if (!await EntityJobInAuthorizedScope(http, "smart_assignment_recommendations", id, db, ct))
            return Results.NotFound(ApiResponse<object>.Fail("Smart assignment recommendation not found"));

        var outcome = await svc.RejectSmartAssignmentAsync(GetCompanyId(http), id, body, ct);
        if (outcome.Success)
        {
            await audit.LogAsync(http, "stage9.assignment.rejected", "assignment_confirmations", id, ct: ct);
            return Results.Ok(ApiResponse<object>.Ok(outcome.Entity ?? new Dictionary<string, object?>(), outcome.Message));
        }

        return Results.Conflict(ApiResponse<object>.Fail(outcome.Message));
    }

    private static async Task<IResult> ListSiteAccessRequirements(HttpContext http, long jobId, Stage9OperationalFoundationService svc, Database db, CancellationToken ct)
    {
        var denied = RequireAnyDirectPermission(http, "operations.site_access.read");
        if (denied is not null) return denied;
        if (!await JobInAuthorizedScope(http, jobId, db, ct)) return Results.NotFound(ApiResponse<object>.Fail("Job not found"));
        var items = await svc.ListSiteAccessRequirementsAsync(GetCompanyId(http), jobId, ct);
        return Results.Ok(ApiResponse<object>.Ok(new { items }));
    }

    private static async Task<IResult> CreateSiteAccessRequirement(HttpContext http, long jobId, Dictionary<string, object?> body, Stage9OperationalFoundationService svc, AuditService audit, Database db, CancellationToken ct)
    {
        var denied = RequireAnyDirectPermission(http, "operations.site_access.create", "job:update", "dispatch:manage", "driver:self");
        if (denied is not null) return denied;
        if (!await JobInAuthorizedScope(http, jobId, db, ct)) return Results.NotFound(ApiResponse<object>.Fail("Job not found"));
        var tripId = Long(body, "tripId");
        if (tripId.HasValue && !await TripInJobScope(GetCompanyId(http), jobId, tripId.Value, db, ct))
            return Results.BadRequest(ApiResponse<object>.Fail("Trip does not belong to this job and tenant"));
        var result = await svc.CreateSiteAccessRequirementAsync(GetCompanyId(http), jobId, tripId, body, ct);
        if (result is null) return Results.Conflict(ApiResponse<object>.Fail("Site access requirement could not be created"));
        await audit.LogAsync(http, "stage9.site_access.created", "site_access_requirements", Convert.ToInt64(result["id"]), ct: ct);
        return Results.Created($"/api/site-access/{result["id"]}", ApiResponse<object>.Ok(result, "Site access requirement created"));
    }

    private static async Task<IResult> PatchSiteAccessRequirement(HttpContext http, long id, Dictionary<string, object?> body, Stage9OperationalFoundationService svc, AuditService audit, Database db, CancellationToken ct)
    {
        var denied = RequireAnyDirectPermission(http, "operations.site_access.update", "job:update", "dispatch:manage", "driver:self");
        if (denied is not null) return denied;
        if (!await EntityJobInAuthorizedScope(http, "site_access_requirements", id, db, ct))
            return Results.NotFound(ApiResponse<object>.Fail("Site access requirement not found"));
        var result = await svc.PatchSiteAccessRequirementAsync(GetCompanyId(http), id, body, ct);
        if (result is null) return Results.NotFound(ApiResponse<object>.Fail("Site access requirement not found"));
        await audit.LogAsync(http, "stage9.site_access.updated", "site_access_requirements", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(result, "Site access requirement updated"));
    }

    private static async Task<IResult> ListAccessDocuments(HttpContext http, long jobId, Stage9OperationalFoundationService svc, Database db, CancellationToken ct)
    {
        var denied = RequireAnyDirectPermission(http, "operations.access_document.read");
        if (denied is not null) return denied;
        if (!await JobInAuthorizedScope(http, jobId, db, ct)) return Results.NotFound(ApiResponse<object>.Fail("Job not found"));
        var items = await svc.ListAccessDocumentsAsync(GetCompanyId(http), jobId, ct);
        return Results.Ok(ApiResponse<object>.Ok(new { items }));
    }

    private static async Task<IResult> CreateAccessDocument(HttpContext http, long jobId, Dictionary<string, object?> body, Stage9OperationalFoundationService svc, AuditService audit, Database db, CancellationToken ct)
    {
        var denied = RequireAnyDirectPermission(http, "operations.access_document.create", "dispatch:manage", "driver:self");
        if (denied is not null) return denied;
        if (!await JobInAuthorizedScope(http, jobId, db, ct)) return Results.NotFound(ApiResponse<object>.Fail("Job not found"));
        var tripId = Long(body, "tripId");
        if (tripId.HasValue && !await TripInJobScope(GetCompanyId(http), jobId, tripId.Value, db, ct))
            return Results.BadRequest(ApiResponse<object>.Fail("Trip does not belong to this job and tenant"));
        var requirementId = Long(body, "siteAccessRequirementId");
        if (requirementId.HasValue && !await ParentInJobScope("site_access_requirements", requirementId.Value, GetCompanyId(http), jobId, db, ct))
            return Results.BadRequest(ApiResponse<object>.Fail("Site access requirement does not belong to this job and tenant"));
        body["capturedByUserId"] = Convert.ToInt64(http.Items[AuthUserIdItemKey] ?? 0L);
        var idempotencyKey = Str(body, "idempotencyKey")
            ?? (http.Request.Headers.TryGetValue("Idempotency-Key", out var idempotencyHeader) ? idempotencyHeader.FirstOrDefault() : null);
        var result = await svc.CreateAccessDocumentAsync(GetCompanyId(http), jobId, tripId, body, idempotencyKey, ct);
        if (result is null) return Results.Conflict(ApiResponse<object>.Fail("Access document could not be created"));
        await audit.LogAsync(http, "stage9.access_document.created", "access_documents", Convert.ToInt64(result.GetValueOrDefault("id") ?? 0L), ct: ct);
        return Results.Created($"/api/access-documents/{result.GetValueOrDefault("id")}", ApiResponse<object>.Ok(result, "Access document created"));
    }

    private static async Task<IResult> PatchAccessDocumentStatus(HttpContext http, long id, Dictionary<string, object?> body, Stage9OperationalFoundationService svc, AuditService audit, Database db, CancellationToken ct)
    {
        var denied = RequireAnyDirectPermission(http, "operations.access_document.update", "operations.access_document.verify", "dispatch:manage", "driver:self");
        if (denied is not null) return denied;
        if (!await EntityJobInAuthorizedScope(http, "access_documents", id, db, ct))
            return Results.NotFound(ApiResponse<object>.Fail("Access document not found"));
        var outcome = await svc.UpdateAccessDocumentStatusAsync(GetCompanyId(http), id, body, ct);
        if (outcome.ApprovalRequired)
        {
            return Results.Accepted($"/api/approval-requests/{outcome.ApprovalRequestId}", ApiResponse<object>.Ok(new
            {
                approvalRequired = true,
                approvalRequestId = outcome.ApprovalRequestId,
                message = outcome.Message
            }, outcome.Message));
        }
        if (!outcome.Success) return Results.Conflict(ApiResponse<object>.Fail(outcome.Message));
        await audit.LogAsync(http, "stage9.access_document.updated", "access_documents", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(outcome.Entity ?? new Dictionary<string, object?>(), outcome.Message));
    }

    private static async Task<IResult> ListPickupAuthorizations(HttpContext http, long jobId, Stage9OperationalFoundationService svc, Database db, CancellationToken ct)
    {
        var denied = RequireAnyDirectPermission(http, "operations.pickup_authorization.read");
        if (denied is not null) return denied;
        if (!await JobInAuthorizedScope(http, jobId, db, ct)) return Results.NotFound(ApiResponse<object>.Fail("Job not found"));
        var items = await svc.ListPickupAuthorizationsAsync(GetCompanyId(http), jobId, ct);
        return Results.Ok(ApiResponse<object>.Ok(new { items }));
    }

    private static async Task<IResult> CreatePickupAuthorization(HttpContext http, long jobId, Dictionary<string, object?> body, Stage9OperationalFoundationService svc, AuditService audit, Database db, CancellationToken ct)
    {
        var denied = RequireAnyDirectPermission(http, "operations.pickup_authorization.create", "dispatch:manage", "driver:self");
        if (denied is not null) return denied;
        if (!await JobInAuthorizedScope(http, jobId, db, ct)) return Results.NotFound(ApiResponse<object>.Fail("Job not found"));
        var tripId = Long(body, "tripId");
        if (tripId.HasValue && !await TripInJobScope(GetCompanyId(http), jobId, tripId.Value, db, ct))
            return Results.BadRequest(ApiResponse<object>.Fail("Trip does not belong to this job and tenant"));
        body["capturedByUserId"] = Convert.ToInt64(http.Items[AuthUserIdItemKey] ?? 0L);
        var idempotencyKey = Str(body, "idempotencyKey")
            ?? (http.Request.Headers.TryGetValue("Idempotency-Key", out var idempotencyHeader) ? idempotencyHeader.FirstOrDefault() : null);
        var result = await svc.CreatePickupAuthorizationAsync(GetCompanyId(http), jobId, tripId, body, idempotencyKey, ct);
        if (result is null) return Results.Conflict(ApiResponse<object>.Fail("Pickup authorization could not be created"));
        await audit.LogAsync(http, "stage9.pickup_authorization.created", "pickup_authorizations", Convert.ToInt64(result.GetValueOrDefault("id") ?? 0L), ct: ct);
        return Results.Created($"/api/pickup-authorizations/{result.GetValueOrDefault("id")}", ApiResponse<object>.Ok(result, "Pickup authorization created"));
    }

    private static async Task<IResult> PatchPickupAuthorization(HttpContext http, long id, Dictionary<string, object?> body, Stage9OperationalFoundationService svc, AuditService audit, Database db, CancellationToken ct)
    {
        var denied = RequireAnyDirectPermission(http, "operations.pickup_authorization.update", "operations.pickup_authorization.verify", "dispatch:manage", "driver:self");
        if (denied is not null) return denied;
        if (!await EntityJobInAuthorizedScope(http, "pickup_authorizations", id, db, ct))
            return Results.NotFound(ApiResponse<object>.Fail("Pickup authorization not found"));
        var outcome = await svc.UpdatePickupAuthorizationAsync(GetCompanyId(http), id, body, ct);
        if (!outcome.Success) return Results.Conflict(ApiResponse<object>.Fail(outcome.Message));
        await audit.LogAsync(http, "stage9.pickup_authorization.updated", "pickup_authorizations", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(outcome.Entity ?? new Dictionary<string, object?>(), outcome.Message));
    }

    private static async Task<IResult> ListWarehouseHandovers(HttpContext http, long jobId, Stage9OperationalFoundationService svc, Database db, CancellationToken ct)
    {
        var denied = RequireAnyDirectPermission(http, "operations.warehouse_handover.read");
        if (denied is not null) return denied;
        if (!await JobInAuthorizedScope(http, jobId, db, ct)) return Results.NotFound(ApiResponse<object>.Fail("Job not found"));
        var items = await svc.ListWarehouseHandoversAsync(GetCompanyId(http), jobId, ct);
        return Results.Ok(ApiResponse<object>.Ok(new { items }));
    }

    private static async Task<IResult> CreateWarehouseHandover(HttpContext http, long jobId, Dictionary<string, object?> body, Stage9OperationalFoundationService svc, AuditService audit, Database db, CancellationToken ct)
    {
        var denied = RequireAnyDirectPermission(http, "operations.warehouse_handover.create", "dispatch:manage", "driver:self");
        if (denied is not null) return denied;
        if (!await JobInAuthorizedScope(http, jobId, db, ct)) return Results.NotFound(ApiResponse<object>.Fail("Job not found"));
        var tripId = Long(body, "tripId");
        if (tripId.HasValue && !await TripInJobScope(GetCompanyId(http), jobId, tripId.Value, db, ct))
            return Results.BadRequest(ApiResponse<object>.Fail("Trip does not belong to this job and tenant"));
        body["capturedByUserId"] = Convert.ToInt64(http.Items[AuthUserIdItemKey] ?? 0L);
        var idempotencyKey = Str(body, "idempotencyKey")
            ?? (http.Request.Headers.TryGetValue("Idempotency-Key", out var idempotencyHeader) ? idempotencyHeader.FirstOrDefault() : null);
        var result = await svc.CreateWarehouseHandoverAsync(GetCompanyId(http), jobId, tripId, body, idempotencyKey, ct);
        if (result is null) return Results.Conflict(ApiResponse<object>.Fail("Warehouse handover could not be created"));
        await audit.LogAsync(http, "stage9.warehouse_handover.created", "warehouse_handovers", Convert.ToInt64(result.GetValueOrDefault("id") ?? 0L), ct: ct);
        return Results.Created($"/api/warehouse-handovers/{result.GetValueOrDefault("id")}", ApiResponse<object>.Ok(result, "Warehouse handover created"));
    }

    private static async Task<IResult> PatchWarehouseHandover(HttpContext http, long id, Dictionary<string, object?> body, Stage9OperationalFoundationService svc, AuditService audit, Database db, CancellationToken ct)
    {
        var denied = RequireAnyDirectPermission(http, "operations.warehouse_handover.update", "dispatch:manage", "driver:self");
        if (denied is not null) return denied;
        if (!await EntityJobInAuthorizedScope(http, "warehouse_handovers", id, db, ct))
            return Results.NotFound(ApiResponse<object>.Fail("Warehouse handover not found"));
        var outcome = await svc.UpdateWarehouseHandoverAsync(GetCompanyId(http), id, body, ct);
        if (!outcome.Success) return Results.Conflict(ApiResponse<object>.Fail(outcome.Message));
        await audit.LogAsync(http, "stage9.warehouse_handover.updated", "warehouse_handovers", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(outcome.Entity ?? new Dictionary<string, object?>(), outcome.Message));
    }

    private static async Task<IResult> ListProofPackages(HttpContext http, long jobId, Stage9OperationalFoundationService svc, Database db, CancellationToken ct)
    {
        var denied = RequireAnyDirectPermission(http, "operations.proof.read");
        if (denied is not null) return denied;
        if (!await JobInAuthorizedScope(http, jobId, db, ct)) return Results.NotFound(ApiResponse<object>.Fail("Job not found"));
        var items = await svc.ListProofPackagesAsync(GetCompanyId(http), jobId, ct);
        return Results.Ok(ApiResponse<object>.Ok(new { items }));
    }

    private static async Task<IResult> CreateProofPackage(HttpContext http, long jobId, Dictionary<string, object?> body, Stage9OperationalFoundationService svc, AuditService audit, Database db, CancellationToken ct)
    {
        var denied = RequireAnyDirectPermission(http, "operations.proof.create", "dispatch:manage", "driver:self");
        if (denied is not null) return denied;
        if (!await JobInAuthorizedScope(http, jobId, db, ct)) return Results.NotFound(ApiResponse<object>.Fail("Job not found"));
        var proofType = Str(body, "proofType")?.Trim().ToLowerInvariant() ?? "proof_of_delivery";
        if (proofType is not ("proof_of_delivery" or "proof_of_pickup" or "warehouse_handover" or "exception"))
            return Results.BadRequest(ApiResponse<object>.Fail("proofType is invalid"));
        var tripId = Long(body, "tripId");
        if (tripId.HasValue && !await TripInJobScope(GetCompanyId(http), jobId, tripId.Value, db, ct))
            return Results.BadRequest(ApiResponse<object>.Fail("Trip does not belong to this job and tenant"));
        body["capturedByUserId"] = Convert.ToInt64(http.Items[AuthUserIdItemKey] ?? 0L);
        body.Remove("completedByUserId");
        body.Remove("status");
        body.Remove("validationStatus");
        var idempotencyKey = Str(body, "idempotencyKey")
            ?? (http.Request.Headers.TryGetValue("Idempotency-Key", out var idempotencyHeader) ? idempotencyHeader.FirstOrDefault() : null);
        var result = await svc.CreateProofPackageAsync(GetCompanyId(http), jobId, tripId, body, idempotencyKey, ct);
        if (result is null) return Results.Conflict(ApiResponse<object>.Fail("Proof package could not be created"));
        await audit.LogAsync(http, "stage9.proof_package.created", "proof_packages", Convert.ToInt64(result.GetValueOrDefault("id") ?? 0L), ct: ct);
        return Results.Created($"/api/proof-packages/{result.GetValueOrDefault("id")}", ApiResponse<object>.Ok(result, "Proof package created"));
    }

    private static async Task<IResult> GetProofPackage(HttpContext http, long id, Stage9OperationalFoundationService svc, Database db, CancellationToken ct)
    {
        var denied = RequireAnyDirectPermission(http, "operations.proof.read");
        if (denied is not null) return denied;
        if (!await EntityJobInAuthorizedScope(http, "proof_packages", id, db, ct))
            return Results.NotFound(ApiResponse<object>.Fail("Proof package not found"));
        var result = await svc.GetProofPackageAsync(GetCompanyId(http), id, ct);
        return result is null ? Results.NotFound(ApiResponse<object>.Fail("Proof package not found")) : Results.Ok(ApiResponse<object>.Ok(result));
    }

    private static async Task<IResult> PatchProofPackage(HttpContext http, long id, Dictionary<string, object?> body, Stage9OperationalFoundationService svc, AuditService audit, Database db, CancellationToken ct)
    {
        var denied = RequireAnyDirectPermission(http, "operations.proof.update", "operations.proof.create", "dispatch:manage", "driver:self");
        if (denied is not null) return denied;
        if (!await EntityJobInAuthorizedScope(http, "proof_packages", id, db, ct))
            return Results.NotFound(ApiResponse<object>.Fail("Proof package not found"));
        var outcome = await svc.UpdateProofPackageAsync(GetCompanyId(http), id, body, ct);
        if (!outcome.Success) return Results.Conflict(ApiResponse<object>.Fail(outcome.Message));
        await audit.LogAsync(http, "stage9.proof_package.updated", "proof_packages", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(outcome.Entity ?? new Dictionary<string, object?>(), outcome.Message));
    }

    private static async Task<IResult> SubmitProofPackage(HttpContext http, long id, Dictionary<string, object?> body, Stage9OperationalFoundationService svc, AuditService audit, Database db, CancellationToken ct)
    {
        var denied = RequireAnyDirectPermission(http, "operations.proof.submit", "operations.proof.create", "dispatch:manage", "driver:self");
        if (denied is not null) return denied;
        if (!await EntityJobInAuthorizedScope(http, "proof_packages", id, db, ct))
            return Results.NotFound(ApiResponse<object>.Fail("Proof package not found"));
        var outcome = await svc.SubmitProofPackageAsync(GetCompanyId(http), id, body, ct);
        if (!outcome.Success) return Results.Conflict(ApiResponse<object>.Fail(outcome.Message));
        await audit.LogAsync(http, "stage9.proof_package.submitted", "proof_packages", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(outcome.Entity ?? new Dictionary<string, object?>(), outcome.Message));
    }

    private static async Task<IResult> ValidateProofPackage(HttpContext http, long id, Dictionary<string, object?> body, Stage9OperationalFoundationService svc, AuditService audit, Database db, CancellationToken ct)
    {
        var denied = RequireAnyDirectPermission(http, "operations.proof.validate", "dispatch:manage");
        if (denied is not null) return denied;
        if (!await EntityJobInAuthorizedScope(http, "proof_packages", id, db, ct))
            return Results.NotFound(ApiResponse<object>.Fail("Proof package not found"));
        body["completedByUserId"] = Convert.ToInt64(http.Items[AuthUserIdItemKey] ?? 0L);
        var outcome = await svc.ValidateProofPackageAsync(GetCompanyId(http), id, body, ct);
        if (!outcome.Success) return Results.Conflict(ApiResponse<object>.Fail(outcome.Message));
        await audit.LogAsync(http, "stage9.proof_package.validated", "proof_packages", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new
        {
            proofPackage = outcome.Entity,
            validationStatus = outcome.ValidationStatus,
            blockers = outcome.Blockers
        }, outcome.Message));
    }

    private static async Task<IResult> ListProofArtifacts(HttpContext http, long proofPackageId, Stage9OperationalFoundationService svc, Database db, CancellationToken ct)
    {
        var denied = RequireAnyDirectPermission(http, "operations.proof_artifact.read");
        if (denied is not null) return denied;
        if (!await EntityJobInAuthorizedScope(http, "proof_packages", proofPackageId, db, ct))
            return Results.NotFound(ApiResponse<object>.Fail("Proof package not found"));
        var items = await svc.ListProofArtifactsAsync(GetCompanyId(http), proofPackageId, ct);
        return Results.Ok(ApiResponse<object>.Ok(new { items }));
    }

    private static async Task<IResult> CreateProofArtifact(HttpContext http, long proofPackageId, Dictionary<string, object?> body, Stage9OperationalFoundationService svc, AuditService audit, Database db, CancellationToken ct)
    {
        var denied = RequireAnyDirectPermission(http, "operations.proof_artifact.create", "operations.proof.create", "dispatch:manage", "driver:self");
        if (denied is not null) return denied;
        if (!await EntityJobInAuthorizedScope(http, "proof_packages", proofPackageId, db, ct))
            return Results.NotFound(ApiResponse<object>.Fail("Proof package not found"));
        var package = await svc.GetProofPackageAsync(GetCompanyId(http), proofPackageId, ct);
        if (package is null) return Results.NotFound(ApiResponse<object>.Fail("Proof package not found"));
        var packageStatus = package.GetValueOrDefault("status")?.ToString()?.ToLowerInvariant();
        if (packageStatus is not ("draft" or "rejected"))
            return Results.Conflict(ApiResponse<object>.Fail("Artifacts can only be added to a draft or rejected proof package"));
        var artifactType = Str(body, "artifactType")?.Trim().ToLowerInvariant();
        if (artifactType is not ("photo" or "signature" or "document" or "scan"))
            return Results.BadRequest(ApiResponse<object>.Fail("artifactType must be photo, signature, document, or scan"));
        if (Long(body, "fileId") is not > 0)
            return Results.BadRequest(ApiResponse<object>.Fail("fileId is required for evidence integrity"));
        if (!await ManagedDocumentInTenantScope(GetCompanyId(http), Long(body, "fileId")!.Value, db, ct))
            return Results.BadRequest(ApiResponse<object>.Fail("fileId must reference an active uploaded document owned by this tenant"));
        body["capturedByUserId"] = Convert.ToInt64(http.Items[AuthUserIdItemKey] ?? 0L);
        var idempotencyKey = Str(body, "idempotencyKey")
            ?? (http.Request.Headers.TryGetValue("Idempotency-Key", out var idempotencyHeader) ? idempotencyHeader.FirstOrDefault() : null);
        var result = await svc.CreateProofArtifactAsync(GetCompanyId(http), proofPackageId, body, idempotencyKey, ct);
        if (result is null) return Results.NotFound(ApiResponse<object>.Fail("Proof package not found"));
        await audit.LogAsync(http, "stage9.proof_artifact.created", "proof_artifacts", Convert.ToInt64(result.GetValueOrDefault("id") ?? 0L), ct: ct);
        return Results.Created($"/api/proof-packages/{proofPackageId}/artifacts/{result.GetValueOrDefault("id")}", ApiResponse<object>.Ok(result, "Proof artifact created"));
    }

    private static async Task<IResult> GetBillingConfidence(HttpContext http, long proofPackageId, Stage9OperationalFoundationService svc, Database db, CancellationToken ct)
    {
        var denied = RequireAnyDirectPermission(http, "operations.proof.read");
        if (denied is not null) return denied;
        if (!await EntityJobInAuthorizedScope(http, "proof_packages", proofPackageId, db, ct))
            return Results.NotFound(ApiResponse<object>.Fail("Proof package not found"));
        var result = await svc.GetBillingConfidenceAsync(GetCompanyId(http), proofPackageId, ct);
        return result is null ? Results.NotFound(ApiResponse<object>.Fail("Billing confidence not found")) : Results.Ok(ApiResponse<object>.Ok(result));
    }

    private static string? Str(Dictionary<string, object?> body, string key)
        => body.TryGetValue(key, out var value) && value is not null ? value.ToString() : null;

    private static long? Long(Dictionary<string, object?> body, string key)
    {
        if (!body.TryGetValue(key, out var value) || value is null || value is DBNull) return null;
        if (value is long l) return l;
        if (value is int i) return i;
        return long.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }

    private static async Task<bool> TripInJobScope(long companyId, long jobId, long tripId, Database db, CancellationToken ct)
        => await db.ScalarLongAsync(
            @"SELECT COUNT(*) FROM trips t
              WHERE t.id=@tripId AND t.company_id=@companyId
                AND EXISTS (SELECT 1 FROM dispatch_assignments da
                            WHERE da.company_id=t.company_id AND da.trip_id=t.id AND da.job_id=@jobId)",
            c => { c.Parameters.AddWithValue("@tripId", tripId); c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@jobId", jobId); }, ct) == 1;

    private static async Task<bool> ParentInJobScope(string table, long id, long companyId, long jobId, Database db, CancellationToken ct)
        => await db.ScalarLongAsync(
            $"SELECT COUNT(*) FROM {table} WHERE id=@id AND company_id=@companyId AND job_id=@jobId",
            c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@jobId", jobId); }, ct) == 1;

    private static async Task<bool> EntityJobInAuthorizedScope(HttpContext http, string table, long id, Database db, CancellationToken ct)
    {
        var sql = table switch
        {
            "smart_assignment_recommendations" => "SELECT job_id FROM smart_assignment_recommendations WHERE id=@id AND company_id=@companyId",
            "site_access_requirements" => "SELECT job_id FROM site_access_requirements WHERE id=@id AND company_id=@companyId",
            "access_documents" => "SELECT job_id FROM access_documents WHERE id=@id AND company_id=@companyId",
            "pickup_authorizations" => "SELECT job_id FROM pickup_authorizations WHERE id=@id AND company_id=@companyId",
            "warehouse_handovers" => "SELECT job_id FROM warehouse_handovers WHERE id=@id AND company_id=@companyId",
            "proof_packages" => "SELECT job_id FROM proof_packages WHERE id=@id AND company_id=@companyId",
            _ => throw new ArgumentOutOfRangeException(nameof(table)),
        };
        var row = await db.QuerySingleAsync(sql, c =>
        {
            c.Parameters.AddWithValue("@id", id);
            c.Parameters.AddWithValue("@companyId", GetCompanyId(http));
        }, ct);
        var jobId = row is null ? null : Long(row, "jobId");
        return jobId.HasValue && await JobInAuthorizedScope(http, jobId.Value, db, ct);
    }

    private static async Task<bool> ManagedDocumentInTenantScope(long companyId, long fileId, Database db, CancellationToken ct)
        => await db.ScalarLongAsync(
            @"SELECT COUNT(*) FROM documents
              WHERE id=@fileId AND company_id=@companyId AND deleted_at IS NULL
                AND status='Active' AND file_url LIKE @tenantPrefix",
            c =>
            {
                c.Parameters.AddWithValue("@fileId", fileId);
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@tenantPrefix", $"objkey:tenant/{companyId}/%");
            }, ct) == 1;
}
