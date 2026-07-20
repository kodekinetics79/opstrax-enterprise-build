using System.Globalization;
using System.Text.Json;
using Npgsql;
using Opstrax.Api.Data;
using Opstrax.Api.Foundation;

namespace Opstrax.Api.Services;

public sealed record Stage9ActionOutcome(
    bool Success,
    string Message,
    bool ApprovalRequired = false,
    long? ApprovalRequestId = null,
    Dictionary<string, object?>? Entity = null,
    string? ValidationStatus = null,
    IReadOnlyList<string>? Blockers = null);

public sealed class Stage9OperationalFoundationService(
    Database db,
    PostgresAiFoundationService ai,
    IApprovalWorkflowService approval,
    IDomainEventPublisher events,
    IEventIdempotencyService idempotency,
    ICorrelationContext correlation)
{
    public async Task<List<Dictionary<string, object?>>> ListSmartAssignmentRecommendationsAsync(long companyId, long? jobId = null, long? tripId = null, CancellationToken ct = default)
        => await QueryListAsync(
            @"SELECT *
              FROM smart_assignment_recommendations
              WHERE company_id=@companyId
                AND (@jobId::BIGINT IS NULL OR job_id=@jobId)
                AND (@tripId::BIGINT IS NULL OR trip_id=@tripId)
              ORDER BY created_at DESC, id DESC",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@jobId", (object?)jobId ?? DBNull.Value);
                c.Parameters.AddWithValue("@tripId", (object?)tripId ?? DBNull.Value);
            }, ct);

    public async Task<Dictionary<string, object?>?> RecommendSmartAssignmentAsync(
        long companyId,
        long? jobId,
        long? tripId,
        Dictionary<string, object?> body,
        string? sourceChannel,
        string? clientGeneratedId,
        string? idempotencyKey,
        CancellationToken ct = default)
    {
        var requestHash = FoundationPersistenceHelpers.ComputeHash(JsonSerializer.Serialize(new
        {
            companyId,
            jobId,
            tripId,
            driverId = Long(body, "recommendedDriverId"),
            vehicleId = Long(body, "recommendedVehicleId"),
            crewId = Long(body, "recommendedCrewId"),
            recommendationType = Str(body, "recommendationType") ?? "pod.smart_assignment",
            score = Dec(body, "score", 0m),
            confidence = Dec(body, "confidenceScore", 0m),
            riskLevel = Str(body, "riskLevel") ?? "medium",
            sourceChannel,
            clientGeneratedId,
        }));

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var existing = await LoadByIdempotencyAsync("smart_assignment_recommendations", companyId, idempotencyKey!, ct);
            if (existing is not null)
            {
                return existing;
            }
        }

        var score = Dec(body, "score", CalculateRecommendationScore(body));
        var confidence = Dec(body, "confidenceScore", Math.Min(0.99m, Math.Max(0.25m, score)));
        var reasonJson = Str(body, "reasonJson") ?? JsonSerializer.Serialize(new
        {
            driverCoverage = Long(body, "recommendedDriverId").HasValue,
            vehicleCoverage = Long(body, "recommendedVehicleId").HasValue,
            missingDataPenalty = score < 0.6m,
        });

        var recommendationId = await db.InsertAsync(
            @"INSERT INTO smart_assignment_recommendations
                (company_id, job_id, trip_id, recommended_driver_id, recommended_vehicle_id, recommended_crew_id,
                 recommendation_type, score, risk_level, confidence_score, reason_json, constraint_json,
                 proposed_action_json, status, source_channel, client_generated_id, idempotency_key,
                 created_by, correlation_id, causation_id, created_at)
              VALUES
                (@companyId, @jobId, @tripId, @driverId, @vehicleId, @crewId,
                 @type, @score, @riskLevel, @confidence, COALESCE(@reason::jsonb, '{}'::jsonb),
                 COALESCE(@constraints::jsonb, '{}'::jsonb), COALESCE(@proposal::jsonb, '{}'::jsonb),
                 @status, @sourceChannel, @clientGeneratedId, @idempotencyKey,
                 @createdBy, @correlationId, @causationId, NOW())",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@jobId", (object?)jobId ?? DBNull.Value);
                c.Parameters.AddWithValue("@tripId", (object?)tripId ?? DBNull.Value);
                c.Parameters.AddWithValue("@driverId", (object?)Long(body, "recommendedDriverId") ?? DBNull.Value);
                c.Parameters.AddWithValue("@vehicleId", (object?)Long(body, "recommendedVehicleId") ?? DBNull.Value);
                c.Parameters.AddWithValue("@crewId", (object?)Long(body, "recommendedCrewId") ?? DBNull.Value);
                c.Parameters.AddWithValue("@type", Str(body, "recommendationType") ?? "pod.smart_assignment");
                c.Parameters.AddWithValue("@score", score);
                c.Parameters.AddWithValue("@riskLevel", Str(body, "riskLevel") ?? "medium");
                c.Parameters.AddWithValue("@confidence", confidence);
                c.Parameters.AddWithValue("@reason", reasonJson);
                c.Parameters.AddWithValue("@constraints", Str(body, "constraintJson") ?? "{}");
                c.Parameters.AddWithValue("@proposal", Str(body, "proposedActionJson") ?? "{}");
                c.Parameters.AddWithValue("@status", Str(body, "status") ?? "draft");
                c.Parameters.AddWithValue("@sourceChannel", (object?)sourceChannel ?? DBNull.Value);
                c.Parameters.AddWithValue("@clientGeneratedId", (object?)clientGeneratedId ?? DBNull.Value);
                c.Parameters.AddWithValue("@idempotencyKey", (object?)idempotencyKey ?? DBNull.Value);
                c.Parameters.AddWithValue("@createdBy", (object?)Long(body, "createdBy") ?? DBNull.Value);
                c.Parameters.AddWithValue("@correlationId", (object?)correlation.CorrelationId ?? DBNull.Value);
                c.Parameters.AddWithValue("@causationId", (object?)correlation.CausationId ?? DBNull.Value);
            }, ct);

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            _ = idempotency.TryComplete(companyId.ToString(CultureInfo.InvariantCulture), "stage9.smart_assign.recommend", idempotencyKey!, requestHash, recommendationId.ToString(CultureInfo.InvariantCulture));
        }

        var recommendation = await LoadByIdAsync("smart_assignment_recommendations", companyId, recommendationId, ct);
        _ = events.Publish(
            companyId.ToString(CultureInfo.InvariantCulture),
            "smart_assignment.recommended",
            "smart_assignment_recommendation",
            recommendationId.ToString(CultureInfo.InvariantCulture),
            JsonSerializer.Serialize(new
            {
                recommendationId,
                companyId,
                jobId,
                tripId,
                recommendedDriverId = Long(body, "recommendedDriverId"),
                recommendedVehicleId = Long(body, "recommendedVehicleId"),
                score,
                confidence,
            }),
            correlation.CorrelationId,
            correlation.CausationId,
            idempotencyKey);

        return recommendation;
    }

    public async Task<Stage9ActionOutcome> AcceptSmartAssignmentAsync(
        long companyId,
        long recommendationId,
        Dictionary<string, object?> body,
        CancellationToken ct = default)
    {
        var recommendation = await LoadByIdAsync("smart_assignment_recommendations", companyId, recommendationId, ct);
        if (recommendation is null)
        {
            return new(false, "Smart assignment recommendation not found");
        }

        var riskLevel = recommendation.GetValueOrDefault("riskLevel")?.ToString() ?? "medium";
        var score = Convert.ToDecimal(recommendation.GetValueOrDefault("score") ?? 0m, CultureInfo.InvariantCulture);
        var requiresApproval = string.Equals(riskLevel, "high", StringComparison.OrdinalIgnoreCase) || score < 0.55m || Bool(body, "requiresApproval", false);

        if (requiresApproval)
        {
            var approvalRequest = approval.CreateRequest(
                companyId.ToString(CultureInfo.InvariantCulture),
                ActorTypes.TenantUser,
                correlation.ActorId,
                "dispatch.trip.reassign_high_value",
                "smart_assignment_recommendation",
                recommendationId.ToString(CultureInfo.InvariantCulture),
                JsonSerializer.Serialize(new
                {
                    recommendationId,
                    companyId,
                    body
                }),
                "high");

            return new(false, "Smart assignment requires approval", true, approvalRequest.Id, recommendation);
        }

        await db.ExecuteAsync(
            @"INSERT INTO assignment_confirmations
                (company_id, job_id, trip_id, driver_id, vehicle_id, status, accepted_at,
                 source_channel, client_generated_id, idempotency_key, device_id, mobile_app_version,
                 metadata_json, correlation_id, causation_id, created_at)
              VALUES
                (@companyId, @jobId, @tripId, @driverId, @vehicleId, 'accepted', NOW(),
                 @sourceChannel, @clientGeneratedId, @idempotencyKey, @deviceId, @mobileAppVersion,
                 COALESCE(@metadata::jsonb, '{}'::jsonb), @correlationId, @causationId, NOW())",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@jobId", recommendation.GetValueOrDefault("jobId") ?? DBNull.Value);
                c.Parameters.AddWithValue("@tripId", recommendation.GetValueOrDefault("tripId") ?? DBNull.Value);
                c.Parameters.AddWithValue("@driverId", recommendation.GetValueOrDefault("recommendedDriverId") ?? DBNull.Value);
                c.Parameters.AddWithValue("@vehicleId", recommendation.GetValueOrDefault("recommendedVehicleId") ?? DBNull.Value);
                c.Parameters.AddWithValue("@sourceChannel", (object?)Str(body, "sourceChannel") ?? recommendation.GetValueOrDefault("sourceChannel") ?? DBNull.Value);
                c.Parameters.AddWithValue("@clientGeneratedId", (object?)Str(body, "clientGeneratedId") ?? DBNull.Value);
                c.Parameters.AddWithValue("@idempotencyKey", (object?)Str(body, "idempotencyKey") ?? DBNull.Value);
                c.Parameters.AddWithValue("@deviceId", (object?)Str(body, "deviceId") ?? DBNull.Value);
                c.Parameters.AddWithValue("@mobileAppVersion", (object?)Str(body, "mobileAppVersion") ?? DBNull.Value);
                c.Parameters.AddWithValue("@metadata", Str(body, "metadataJson") ?? "{}");
                c.Parameters.AddWithValue("@correlationId", (object?)correlation.CorrelationId ?? DBNull.Value);
                c.Parameters.AddWithValue("@causationId", (object?)correlation.CausationId ?? DBNull.Value);
            }, ct);

        await db.ExecuteAsync(
            @"UPDATE smart_assignment_recommendations
              SET status='accepted', updated_at=NOW()
              WHERE company_id=@companyId AND id=@id",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@id", recommendationId);
            }, ct);

        _ = events.Publish(
            companyId.ToString(CultureInfo.InvariantCulture),
            "assignment.accepted",
            "smart_assignment_recommendation",
            recommendationId.ToString(CultureInfo.InvariantCulture),
            JsonSerializer.Serialize(new { recommendationId, companyId, accepted = true }),
            correlation.CorrelationId,
            correlation.CausationId,
            $"smart_assign.accept:{recommendationId}");

        _ = events.Publish(
            companyId.ToString(CultureInfo.InvariantCulture),
            "assignment.confirmation.created",
            "assignment_confirmation",
            recommendationId.ToString(CultureInfo.InvariantCulture),
            JsonSerializer.Serialize(new { recommendationId, companyId, status = "accepted" }),
            correlation.CorrelationId,
            correlation.CausationId,
            $"assignment_confirmation:{recommendationId}");

        return new(true, "Smart assignment accepted", false, null, await LoadAssignmentConfirmationAsync(companyId, recommendationId, ct));
    }

    public async Task<Stage9ActionOutcome> RejectSmartAssignmentAsync(
        long companyId,
        long recommendationId,
        Dictionary<string, object?> body,
        CancellationToken ct = default)
    {
        var recommendation = await LoadByIdAsync("smart_assignment_recommendations", companyId, recommendationId, ct);
        if (recommendation is null)
        {
            return new(false, "Smart assignment recommendation not found");
        }

        await db.ExecuteAsync(
            @"UPDATE smart_assignment_recommendations
              SET status='rejected', updated_at=NOW()
              WHERE company_id=@companyId AND id=@id",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@id", recommendationId);
            }, ct);

        await db.ExecuteAsync(
            @"INSERT INTO assignment_confirmations
                (company_id, job_id, trip_id, driver_id, vehicle_id, status, rejected_at, rejection_reason,
                 source_channel, client_generated_id, idempotency_key, device_id, mobile_app_version,
                 metadata_json, correlation_id, causation_id, created_at)
              VALUES
                (@companyId, @jobId, @tripId, @driverId, @vehicleId, 'rejected', NOW(), @reason,
                 @sourceChannel, @clientGeneratedId, @idempotencyKey, @deviceId, @mobileAppVersion,
                 COALESCE(@metadata::jsonb, '{}'::jsonb), @correlationId, @causationId, NOW())",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@jobId", recommendation.GetValueOrDefault("jobId") ?? DBNull.Value);
                c.Parameters.AddWithValue("@tripId", recommendation.GetValueOrDefault("tripId") ?? DBNull.Value);
                c.Parameters.AddWithValue("@driverId", recommendation.GetValueOrDefault("recommendedDriverId") ?? DBNull.Value);
                c.Parameters.AddWithValue("@vehicleId", recommendation.GetValueOrDefault("recommendedVehicleId") ?? DBNull.Value);
                c.Parameters.AddWithValue("@reason", (object?)Str(body, "rejectionReason") ?? "Rejected by user");
                c.Parameters.AddWithValue("@sourceChannel", (object?)Str(body, "sourceChannel") ?? recommendation.GetValueOrDefault("sourceChannel") ?? DBNull.Value);
                c.Parameters.AddWithValue("@clientGeneratedId", (object?)Str(body, "clientGeneratedId") ?? DBNull.Value);
                c.Parameters.AddWithValue("@idempotencyKey", (object?)Str(body, "idempotencyKey") ?? DBNull.Value);
                c.Parameters.AddWithValue("@deviceId", (object?)Str(body, "deviceId") ?? DBNull.Value);
                c.Parameters.AddWithValue("@mobileAppVersion", (object?)Str(body, "mobileAppVersion") ?? DBNull.Value);
                c.Parameters.AddWithValue("@metadata", Str(body, "metadataJson") ?? "{}");
                c.Parameters.AddWithValue("@correlationId", (object?)correlation.CorrelationId ?? DBNull.Value);
                c.Parameters.AddWithValue("@causationId", (object?)correlation.CausationId ?? DBNull.Value);
            }, ct);

        _ = events.Publish(
            companyId.ToString(CultureInfo.InvariantCulture),
            "assignment.rejected",
            "smart_assignment_recommendation",
            recommendationId.ToString(CultureInfo.InvariantCulture),
            JsonSerializer.Serialize(new { recommendationId, companyId, rejected = true, reason = Str(body, "rejectionReason") }),
            correlation.CorrelationId,
            correlation.CausationId,
            $"smart_assign.reject:{recommendationId}");

        return new(true, "Smart assignment rejected", false, null, recommendation);
    }

    public Task<List<Dictionary<string, object?>>> ListSiteAccessRequirementsAsync(long companyId, long? jobId = null, CancellationToken ct = default)
        => QueryListAsync(
            @"SELECT *
              FROM site_access_requirements
              WHERE company_id=@companyId AND (@jobId::BIGINT IS NULL OR job_id=@jobId)
              ORDER BY created_at DESC, id DESC",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@jobId", (object?)jobId ?? DBNull.Value);
            }, ct);

    public async Task<Dictionary<string, object?>?> CreateSiteAccessRequirementAsync(
        long companyId,
        long? jobId,
        long? tripId,
        Dictionary<string, object?> body,
        CancellationToken ct = default)
    {
        var requirementId = await db.InsertAsync(
            @"INSERT INTO site_access_requirements
                (company_id, customer_id, address_id, job_id, trip_id, requirement_type, status,
                 required_before, instructions, contact_name, contact_phone, source_channel, metadata_json,
                 correlation_id, causation_id, created_at)
              VALUES
                (@companyId, @customerId, @addressId, @jobId, @tripId, @type, @status,
                 @requiredBefore, @instructions, @contactName, @contactPhone, @sourceChannel, COALESCE(@metadata::jsonb, '{}'::jsonb),
                 @correlationId, @causationId, NOW())",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@customerId", (object?)Long(body, "customerId") ?? DBNull.Value);
                c.Parameters.AddWithValue("@addressId", (object?)Long(body, "addressId") ?? DBNull.Value);
                c.Parameters.AddWithValue("@jobId", (object?)jobId ?? DBNull.Value);
                c.Parameters.AddWithValue("@tripId", (object?)tripId ?? DBNull.Value);
                c.Parameters.AddWithValue("@type", Str(body, "requirementType") ?? "gate_pass");
                c.Parameters.AddWithValue("@status", Str(body, "status") ?? "required");
                c.Parameters.AddWithValue("@requiredBefore", ParseDate(body, "requiredBefore") ?? DBNull.Value);
                c.Parameters.AddWithValue("@instructions", (object?)Str(body, "instructions") ?? DBNull.Value);
                c.Parameters.AddWithValue("@contactName", (object?)Str(body, "contactName") ?? DBNull.Value);
                c.Parameters.AddWithValue("@contactPhone", (object?)Str(body, "contactPhone") ?? DBNull.Value);
                c.Parameters.AddWithValue("@sourceChannel", (object?)Str(body, "sourceChannel") ?? DBNull.Value);
                c.Parameters.AddWithValue("@metadata", Str(body, "metadataJson") ?? "{}");
                c.Parameters.AddWithValue("@correlationId", (object?)correlation.CorrelationId ?? DBNull.Value);
                c.Parameters.AddWithValue("@causationId", (object?)correlation.CausationId ?? DBNull.Value);
            }, ct);

        _ = events.Publish(
            companyId.ToString(CultureInfo.InvariantCulture),
            "site_access.required",
            "site_access_requirement",
            requirementId.ToString(CultureInfo.InvariantCulture),
            JsonSerializer.Serialize(new { requirementId, companyId, jobId, tripId, requirementType = Str(body, "requirementType") ?? "gate_pass" }),
            correlation.CorrelationId,
            correlation.CausationId,
            null);

        if (Long(body, "jobId").HasValue || jobId.HasValue)
        {
            _ = ai.CreateRecommendation(
                companyId.ToString(CultureInfo.InvariantCulture),
                "site_access.missing",
                "Site access requirement created",
                "A site access requirement was recorded for operational completion.",
                0.81m,
                0.65m,
                JsonSerializer.Serialize(new { requirementId, jobId, tripId }),
                JsonSerializer.Serialize(new { requirementType = Str(body, "requirementType") ?? "gate_pass" }),
                JsonSerializer.Serialize(new { action = "track_site_access_to_completion" }),
                "medium",
                requirementId.ToString(CultureInfo.InvariantCulture),
                ActorTypes.System,
                "stage9-service",
                status: "active");
        }

        return await LoadByIdAsync("site_access_requirements", companyId, requirementId, ct);
    }

    public async Task<Dictionary<string, object?>?> PatchSiteAccessRequirementAsync(long companyId, long id, Dictionary<string, object?> body, CancellationToken ct = default)
    {
        var current = await LoadByIdAsync("site_access_requirements", companyId, id, ct);
        if (current is null)
        {
            return null;
        }

        await db.ExecuteAsync(
            @"UPDATE site_access_requirements
              SET status=COALESCE(@status, status),
                  instructions=COALESCE(@instructions, instructions),
                  contact_name=COALESCE(@contactName, contact_name),
                  contact_phone=COALESCE(@contactPhone, contact_phone),
                  required_before=COALESCE(@requiredBefore, required_before),
                  metadata_json=COALESCE(@metadata::jsonb, metadata_json),
                  updated_at=NOW()
              WHERE company_id=@companyId AND id=@id",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@id", id);
                c.Parameters.AddWithValue("@status", (object?)Str(body, "status") ?? DBNull.Value);
                c.Parameters.AddWithValue("@instructions", (object?)Str(body, "instructions") ?? DBNull.Value);
                c.Parameters.AddWithValue("@contactName", (object?)Str(body, "contactName") ?? DBNull.Value);
                c.Parameters.AddWithValue("@contactPhone", (object?)Str(body, "contactPhone") ?? DBNull.Value);
                c.Parameters.AddWithValue("@requiredBefore", ParseDate(body, "requiredBefore") ?? (object)DBNull.Value);
                c.Parameters.AddWithValue("@metadata", Str(body, "metadataJson") ?? "{}");
            }, ct);

        var updated = await LoadByIdAsync("site_access_requirements", companyId, id, ct);
        if (updated is not null && string.Equals(updated.GetValueOrDefault("status")?.ToString(), "required", StringComparison.OrdinalIgnoreCase))
        {
            _ = ai.CreateRecommendation(
                companyId.ToString(CultureInfo.InvariantCulture),
                "site_access.risk",
                "Site access still unresolved",
                "The site access requirement remains unresolved and may block completion.",
                0.87m,
                0.72m,
                JsonSerializer.Serialize(new { requirementId = id, companyId }),
                JsonSerializer.Serialize(new { status = updated.GetValueOrDefault("status")?.ToString() }),
                JsonSerializer.Serialize(new { action = "resolve_site_access" }),
                "high",
                id.ToString(CultureInfo.InvariantCulture),
                ActorTypes.System,
                "stage9-service",
                status: "active");
        }

        return updated;
    }

    public Task<List<Dictionary<string, object?>>> ListAccessDocumentsAsync(long companyId, long? jobId = null, CancellationToken ct = default)
        => QueryListAsync(
            @"SELECT *
              FROM access_documents
              WHERE company_id=@companyId AND (@jobId::BIGINT IS NULL OR job_id=@jobId)
              ORDER BY created_at DESC, id DESC",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@jobId", (object?)jobId ?? DBNull.Value);
            }, ct);

    public async Task<Dictionary<string, object?>?> CreateAccessDocumentAsync(
        long companyId,
        long? jobId,
        long? tripId,
        Dictionary<string, object?> body,
        string? idempotencyKey,
        CancellationToken ct = default)
    {
        var requestHash = FoundationPersistenceHelpers.ComputeHash(JsonSerializer.Serialize(new
        {
            companyId,
            jobId,
            tripId,
            documentType = Str(body, "documentType") ?? "gate_pass",
            documentNo = Str(body, "documentNo"),
            status = Str(body, "status") ?? "required",
            sourceChannel = Str(body, "sourceChannel"),
            clientGeneratedId = Str(body, "clientGeneratedId"),
        }));

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var existing = await LoadByIdempotencyAsync("access_documents", companyId, idempotencyKey!, ct);
            if (existing is not null)
            {
                return existing;
            }
        }

        var documentId = await db.InsertAsync(
            @"INSERT INTO access_documents
                (company_id, job_id, trip_id, site_access_requirement_id, document_type, document_no, status,
                 issued_by, issued_to, valid_from, valid_to, file_id, notes, source_channel,
                 captured_at, uploaded_at, captured_by_user_id, device_id, mobile_app_version,
                 geo_latitude, geo_longitude, metadata_json, correlation_id, causation_id, idempotency_key, created_at)
              VALUES
                (@companyId, @jobId, @tripId, @requirementId, @type, @documentNo, @status,
                 @issuedBy, @issuedTo, @validFrom, @validTo, @fileId, @notes, @sourceChannel,
                 @capturedAt, @uploadedAt, @capturedByUserId, @deviceId, @mobileAppVersion,
                 @geoLat, @geoLong, COALESCE(@metadata::jsonb, '{}'::jsonb), @correlationId, @causationId, @idempotencyKey, NOW())",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@jobId", (object?)jobId ?? DBNull.Value);
                c.Parameters.AddWithValue("@tripId", (object?)tripId ?? DBNull.Value);
                c.Parameters.AddWithValue("@requirementId", (object?)Long(body, "siteAccessRequirementId") ?? DBNull.Value);
                c.Parameters.AddWithValue("@type", Str(body, "documentType") ?? "gate_pass");
                c.Parameters.AddWithValue("@documentNo", (object?)Str(body, "documentNo") ?? DBNull.Value);
                c.Parameters.AddWithValue("@status", Str(body, "status") ?? "required");
                c.Parameters.AddWithValue("@issuedBy", (object?)Str(body, "issuedBy") ?? DBNull.Value);
                c.Parameters.AddWithValue("@issuedTo", (object?)Str(body, "issuedTo") ?? DBNull.Value);
                c.Parameters.AddWithValue("@validFrom", ParseDate(body, "validFrom") ?? (object)DBNull.Value);
                c.Parameters.AddWithValue("@validTo", ParseDate(body, "validTo") ?? (object)DBNull.Value);
                c.Parameters.AddWithValue("@fileId", (object?)Long(body, "fileId") ?? DBNull.Value);
                c.Parameters.AddWithValue("@notes", (object?)Str(body, "notes") ?? DBNull.Value);
                c.Parameters.AddWithValue("@sourceChannel", (object?)Str(body, "sourceChannel") ?? DBNull.Value);
                c.Parameters.AddWithValue("@capturedAt", ParseDate(body, "capturedAt") ?? (object)DBNull.Value);
                c.Parameters.AddWithValue("@uploadedAt", ParseDate(body, "uploadedAt") ?? (object)DBNull.Value);
                c.Parameters.AddWithValue("@capturedByUserId", (object?)Long(body, "capturedByUserId") ?? DBNull.Value);
                c.Parameters.AddWithValue("@deviceId", (object?)Str(body, "deviceId") ?? DBNull.Value);
                c.Parameters.AddWithValue("@mobileAppVersion", (object?)Str(body, "mobileAppVersion") ?? DBNull.Value);
                c.Parameters.AddWithValue("@geoLat", (object?)DecN(body, "geoLatitude") ?? DBNull.Value);
                c.Parameters.AddWithValue("@geoLong", (object?)DecN(body, "geoLongitude") ?? DBNull.Value);
                c.Parameters.AddWithValue("@metadata", Str(body, "metadataJson") ?? "{}");
                c.Parameters.AddWithValue("@correlationId", (object?)correlation.CorrelationId ?? DBNull.Value);
                c.Parameters.AddWithValue("@causationId", (object?)correlation.CausationId ?? DBNull.Value);
                c.Parameters.AddWithValue("@idempotencyKey", (object?)idempotencyKey ?? DBNull.Value);
            }, ct);

        _ = events.Publish(
            companyId.ToString(CultureInfo.InvariantCulture),
            "access_document.created",
            "access_document",
            documentId.ToString(CultureInfo.InvariantCulture),
            JsonSerializer.Serialize(new { documentId, companyId, jobId, tripId, documentType = Str(body, "documentType") ?? "gate_pass" }),
            correlation.CorrelationId,
            correlation.CausationId,
            idempotencyKey);

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            _ = idempotency.TryComplete(companyId.ToString(CultureInfo.InvariantCulture), "stage9.access_document.create", idempotencyKey!, requestHash, documentId.ToString(CultureInfo.InvariantCulture));
        }

        return await LoadByIdAsync("access_documents", companyId, documentId, ct);
    }

    public async Task<Stage9ActionOutcome> UpdateAccessDocumentStatusAsync(long companyId, long id, Dictionary<string, object?> body, CancellationToken ct = default)
    {
        var current = await LoadByIdAsync("access_documents", companyId, id, ct);
        if (current is null)
        {
            return new(false, "Access document not found");
        }

        var status = Str(body, "status") ?? current.GetValueOrDefault("status")?.ToString() ?? "required";
        if (string.Equals(status, "waived_with_approval", StringComparison.OrdinalIgnoreCase))
        {
            var approvalRequest = approval.CreateRequest(
                companyId.ToString(CultureInfo.InvariantCulture),
                ActorTypes.TenantUser,
                correlation.ActorId,
                "operations.access_document.waive",
                "access_document",
                id.ToString(CultureInfo.InvariantCulture),
                JsonSerializer.Serialize(new { accessDocumentId = id, companyId, body }),
                "high");

            await db.ExecuteAsync(
                @"UPDATE access_documents
                  SET status=@status, notes=COALESCE(@notes, notes), updated_at=NOW()
                  WHERE company_id=@companyId AND id=@id",
                c =>
                {
                    c.Parameters.AddWithValue("@companyId", companyId);
                    c.Parameters.AddWithValue("@id", id);
                    c.Parameters.AddWithValue("@status", status);
                    c.Parameters.AddWithValue("@notes", (object?)Str(body, "notes") ?? DBNull.Value);
                }, ct);

            return new(false, "Access document waiver requires approval", true, approvalRequest.Id, await LoadByIdAsync("access_documents", companyId, id, ct));
        }

        await db.ExecuteAsync(
            @"UPDATE access_documents
              SET status=@status,
                  notes=COALESCE(@notes, notes),
                  valid_from=COALESCE(@validFrom, valid_from),
                  valid_to=COALESCE(@validTo, valid_to),
                  metadata_json=COALESCE(@metadata::jsonb, metadata_json),
                  updated_at=NOW()
              WHERE company_id=@companyId AND id=@id",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@id", id);
                c.Parameters.AddWithValue("@status", status);
                c.Parameters.AddWithValue("@notes", (object?)Str(body, "notes") ?? DBNull.Value);
                c.Parameters.AddWithValue("@validFrom", ParseDate(body, "validFrom") ?? (object)DBNull.Value);
                c.Parameters.AddWithValue("@validTo", ParseDate(body, "validTo") ?? (object)DBNull.Value);
                c.Parameters.AddWithValue("@metadata", Str(body, "metadataJson") ?? "{}");
            }, ct);

        if (string.Equals(status, "verified", StringComparison.OrdinalIgnoreCase))
        {
            _ = events.Publish(
                companyId.ToString(CultureInfo.InvariantCulture),
                "access_document.verified",
                "access_document",
                id.ToString(CultureInfo.InvariantCulture),
                JsonSerializer.Serialize(new { accessDocumentId = id, companyId, status }),
                correlation.CorrelationId,
                correlation.CausationId,
                $"access_document.verify:{id}");
        }

        return new(true, "Access document status updated", false, null, await LoadByIdAsync("access_documents", companyId, id, ct));
    }

    public Task<List<Dictionary<string, object?>>> ListPickupAuthorizationsAsync(long companyId, long? jobId = null, CancellationToken ct = default)
        => QueryListAsync(
            @"SELECT *
              FROM pickup_authorizations
              WHERE company_id=@companyId AND (@jobId::BIGINT IS NULL OR job_id=@jobId)
              ORDER BY created_at DESC, id DESC",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@jobId", (object?)jobId ?? DBNull.Value);
            }, ct);

    public async Task<Dictionary<string, object?>?> CreatePickupAuthorizationAsync(long companyId, long? jobId, long? tripId, Dictionary<string, object?> body, string? idempotencyKey, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var existing = await LoadByIdempotencyAsync("pickup_authorizations", companyId, idempotencyKey!, ct);
            if (existing is not null)
            {
                return existing;
            }
        }

        var authorizationId = await db.InsertAsync(
            @"INSERT INTO pickup_authorizations
                (company_id, job_id, trip_id, warehouse_id, third_party_name, authorization_no,
                 authorized_person_name, authorized_person_phone, status, valid_from, valid_to, notes,
                 source_channel, captured_at, uploaded_at, captured_by_user_id, device_id, mobile_app_version,
                 metadata_json, correlation_id, causation_id, idempotency_key, created_at)
              VALUES
                (@companyId, @jobId, @tripId, @warehouseId, @thirdPartyName, @authorizationNo,
                 @authorizedPersonName, @authorizedPersonPhone, @status, @validFrom, @validTo, @notes,
                 @sourceChannel, @capturedAt, @uploadedAt, @capturedByUserId, @deviceId, @mobileAppVersion,
                 COALESCE(@metadata::jsonb, '{}'::jsonb), @correlationId, @causationId, @idempotencyKey, NOW())",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@jobId", (object?)jobId ?? DBNull.Value);
                c.Parameters.AddWithValue("@tripId", (object?)tripId ?? DBNull.Value);
                c.Parameters.AddWithValue("@warehouseId", (object?)Long(body, "warehouseId") ?? DBNull.Value);
                c.Parameters.AddWithValue("@thirdPartyName", (object?)Str(body, "thirdPartyName") ?? DBNull.Value);
                c.Parameters.AddWithValue("@authorizationNo", (object?)Str(body, "authorizationNo") ?? DBNull.Value);
                c.Parameters.AddWithValue("@authorizedPersonName", (object?)Str(body, "authorizedPersonName") ?? DBNull.Value);
                c.Parameters.AddWithValue("@authorizedPersonPhone", (object?)Str(body, "authorizedPersonPhone") ?? DBNull.Value);
                c.Parameters.AddWithValue("@status", Str(body, "status") ?? "required");
                c.Parameters.AddWithValue("@validFrom", ParseDate(body, "validFrom") ?? (object)DBNull.Value);
                c.Parameters.AddWithValue("@validTo", ParseDate(body, "validTo") ?? (object)DBNull.Value);
                c.Parameters.AddWithValue("@notes", (object?)Str(body, "notes") ?? DBNull.Value);
                c.Parameters.AddWithValue("@sourceChannel", (object?)Str(body, "sourceChannel") ?? DBNull.Value);
                c.Parameters.AddWithValue("@capturedAt", ParseDate(body, "capturedAt") ?? (object)DBNull.Value);
                c.Parameters.AddWithValue("@uploadedAt", ParseDate(body, "uploadedAt") ?? (object)DBNull.Value);
                c.Parameters.AddWithValue("@capturedByUserId", (object?)Long(body, "capturedByUserId") ?? DBNull.Value);
                c.Parameters.AddWithValue("@deviceId", (object?)Str(body, "deviceId") ?? DBNull.Value);
                c.Parameters.AddWithValue("@mobileAppVersion", (object?)Str(body, "mobileAppVersion") ?? DBNull.Value);
                c.Parameters.AddWithValue("@metadata", Str(body, "metadataJson") ?? "{}");
                c.Parameters.AddWithValue("@correlationId", (object?)correlation.CorrelationId ?? DBNull.Value);
                c.Parameters.AddWithValue("@causationId", (object?)correlation.CausationId ?? DBNull.Value);
                c.Parameters.AddWithValue("@idempotencyKey", (object?)idempotencyKey ?? DBNull.Value);
            }, ct);

        _ = events.Publish(
            companyId.ToString(CultureInfo.InvariantCulture),
            "pickup_authorization.created",
            "pickup_authorization",
            authorizationId.ToString(CultureInfo.InvariantCulture),
            JsonSerializer.Serialize(new { authorizationId, companyId, jobId, tripId }),
            correlation.CorrelationId,
            correlation.CausationId,
            idempotencyKey);

        return await LoadByIdAsync("pickup_authorizations", companyId, authorizationId, ct);
    }

    public async Task<Stage9ActionOutcome> UpdatePickupAuthorizationAsync(long companyId, long id, Dictionary<string, object?> body, CancellationToken ct = default)
    {
        var current = await LoadByIdAsync("pickup_authorizations", companyId, id, ct);
        if (current is null)
        {
            return new(false, "Pickup authorization not found");
        }

        var status = Str(body, "status") ?? current.GetValueOrDefault("status")?.ToString() ?? "required";
        await db.ExecuteAsync(
            @"UPDATE pickup_authorizations
              SET status=@status,
                  notes=COALESCE(@notes, notes),
                  valid_from=COALESCE(@validFrom, valid_from),
                  valid_to=COALESCE(@validTo, valid_to),
                  metadata_json=COALESCE(@metadata::jsonb, metadata_json),
                  updated_at=NOW()
              WHERE company_id=@companyId AND id=@id",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@id", id);
                c.Parameters.AddWithValue("@status", status);
                c.Parameters.AddWithValue("@notes", (object?)Str(body, "notes") ?? DBNull.Value);
                c.Parameters.AddWithValue("@validFrom", ParseDate(body, "validFrom") ?? (object)DBNull.Value);
                c.Parameters.AddWithValue("@validTo", ParseDate(body, "validTo") ?? (object)DBNull.Value);
                c.Parameters.AddWithValue("@metadata", Str(body, "metadataJson") ?? "{}");
            }, ct);

        if (string.Equals(status, "verified", StringComparison.OrdinalIgnoreCase))
        {
            _ = events.Publish(
                companyId.ToString(CultureInfo.InvariantCulture),
                "pickup_authorization.verified",
                "pickup_authorization",
                id.ToString(CultureInfo.InvariantCulture),
                JsonSerializer.Serialize(new { pickupAuthorizationId = id, companyId, status }),
                correlation.CorrelationId,
                correlation.CausationId,
                $"pickup_authorization.verify:{id}");
        }

        return new(true, "Pickup authorization updated", false, null, await LoadByIdAsync("pickup_authorizations", companyId, id, ct));
    }

    public Task<List<Dictionary<string, object?>>> ListWarehouseHandoversAsync(long companyId, long? jobId = null, CancellationToken ct = default)
        => QueryListAsync(
            @"SELECT *
              FROM warehouse_handovers
              WHERE company_id=@companyId AND (@jobId::BIGINT IS NULL OR job_id=@jobId)
              ORDER BY created_at DESC, id DESC",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@jobId", (object?)jobId ?? DBNull.Value);
            }, ct);

    public async Task<Dictionary<string, object?>?> CreateWarehouseHandoverAsync(long companyId, long? jobId, long? tripId, Dictionary<string, object?> body, string? idempotencyKey, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var existing = await LoadByIdempotencyAsync("warehouse_handovers", companyId, idempotencyKey!, ct);
            if (existing is not null)
            {
                return existing;
            }
        }

        var handoverId = await db.InsertAsync(
            @"INSERT INTO warehouse_handovers
                (company_id, job_id, trip_id, warehouse_name, warehouse_reference_no, handover_type, status,
                 scheduled_at, completed_at, handled_by_name, notes, source_channel, captured_at, uploaded_at,
                 captured_by_user_id, device_id, mobile_app_version, geo_latitude, geo_longitude, metadata_json,
                 correlation_id, causation_id, idempotency_key, created_at)
              VALUES
                (@companyId, @jobId, @tripId, @warehouseName, @warehouseReferenceNo, @handoverType, @status,
                 @scheduledAt, @completedAt, @handledByName, @notes, @sourceChannel, @capturedAt, @uploadedAt,
                 @capturedByUserId, @deviceId, @mobileAppVersion, @geoLat, @geoLong, COALESCE(@metadata::jsonb, '{}'::jsonb),
                 @correlationId, @causationId, @idempotencyKey, NOW())",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@jobId", (object?)jobId ?? DBNull.Value);
                c.Parameters.AddWithValue("@tripId", (object?)tripId ?? DBNull.Value);
                c.Parameters.AddWithValue("@warehouseName", (object?)Str(body, "warehouseName") ?? DBNull.Value);
                c.Parameters.AddWithValue("@warehouseReferenceNo", (object?)Str(body, "warehouseReferenceNo") ?? DBNull.Value);
                c.Parameters.AddWithValue("@handoverType", Str(body, "handoverType") ?? "pickup");
                c.Parameters.AddWithValue("@status", Str(body, "status") ?? "scheduled");
                c.Parameters.AddWithValue("@scheduledAt", ParseDate(body, "scheduledAt") ?? (object)DBNull.Value);
                c.Parameters.AddWithValue("@completedAt", ParseDate(body, "completedAt") ?? (object)DBNull.Value);
                c.Parameters.AddWithValue("@handledByName", (object?)Str(body, "handledByName") ?? DBNull.Value);
                c.Parameters.AddWithValue("@notes", (object?)Str(body, "notes") ?? DBNull.Value);
                c.Parameters.AddWithValue("@sourceChannel", (object?)Str(body, "sourceChannel") ?? DBNull.Value);
                c.Parameters.AddWithValue("@capturedAt", ParseDate(body, "capturedAt") ?? (object)DBNull.Value);
                c.Parameters.AddWithValue("@uploadedAt", ParseDate(body, "uploadedAt") ?? (object)DBNull.Value);
                c.Parameters.AddWithValue("@capturedByUserId", (object?)Long(body, "capturedByUserId") ?? DBNull.Value);
                c.Parameters.AddWithValue("@deviceId", (object?)Str(body, "deviceId") ?? DBNull.Value);
                c.Parameters.AddWithValue("@mobileAppVersion", (object?)Str(body, "mobileAppVersion") ?? DBNull.Value);
                c.Parameters.AddWithValue("@geoLat", (object?)DecN(body, "geoLatitude") ?? DBNull.Value);
                c.Parameters.AddWithValue("@geoLong", (object?)DecN(body, "geoLongitude") ?? DBNull.Value);
                c.Parameters.AddWithValue("@metadata", Str(body, "metadataJson") ?? "{}");
                c.Parameters.AddWithValue("@correlationId", (object?)correlation.CorrelationId ?? DBNull.Value);
                c.Parameters.AddWithValue("@causationId", (object?)correlation.CausationId ?? DBNull.Value);
                c.Parameters.AddWithValue("@idempotencyKey", (object?)idempotencyKey ?? DBNull.Value);
            }, ct);

        _ = events.Publish(
            companyId.ToString(CultureInfo.InvariantCulture),
            "warehouse_handover.created",
            "warehouse_handover",
            handoverId.ToString(CultureInfo.InvariantCulture),
            JsonSerializer.Serialize(new { handoverId, companyId, jobId, tripId, handoverType = Str(body, "handoverType") ?? "pickup" }),
            correlation.CorrelationId,
            correlation.CausationId,
            idempotencyKey);

        return await LoadByIdAsync("warehouse_handovers", companyId, handoverId, ct);
    }

    public async Task<Stage9ActionOutcome> UpdateWarehouseHandoverAsync(long companyId, long id, Dictionary<string, object?> body, CancellationToken ct = default)
    {
        var current = await LoadByIdAsync("warehouse_handovers", companyId, id, ct);
        if (current is null)
        {
            return new(false, "Warehouse handover not found");
        }

        var status = Str(body, "status") ?? current.GetValueOrDefault("status")?.ToString() ?? "scheduled";
        await db.ExecuteAsync(
            @"UPDATE warehouse_handovers
              SET status=@status,
                  completed_at=COALESCE(@completedAt, completed_at),
                  handled_by_name=COALESCE(@handledByName, handled_by_name),
                  notes=COALESCE(@notes, notes),
                  metadata_json=COALESCE(@metadata::jsonb, metadata_json),
                  updated_at=NOW()
              WHERE company_id=@companyId AND id=@id",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@id", id);
                c.Parameters.AddWithValue("@status", status);
                c.Parameters.AddWithValue("@completedAt", ParseDate(body, "completedAt") ?? (object)DBNull.Value);
                c.Parameters.AddWithValue("@handledByName", (object?)Str(body, "handledByName") ?? DBNull.Value);
                c.Parameters.AddWithValue("@notes", (object?)Str(body, "notes") ?? DBNull.Value);
                c.Parameters.AddWithValue("@metadata", Str(body, "metadataJson") ?? "{}");
            }, ct);

        if (string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            _ = events.Publish(
                companyId.ToString(CultureInfo.InvariantCulture),
                "warehouse_handover.completed",
                "warehouse_handover",
                id.ToString(CultureInfo.InvariantCulture),
                JsonSerializer.Serialize(new { warehouseHandoverId = id, companyId, status }),
                correlation.CorrelationId,
                correlation.CausationId,
                $"warehouse_handover.completed:{id}");
        }

        return new(true, "Warehouse handover updated", false, null, await LoadByIdAsync("warehouse_handovers", companyId, id, ct));
    }

    public Task<List<Dictionary<string, object?>>> ListProofPackagesAsync(long companyId, long? jobId = null, CancellationToken ct = default)
        => QueryListAsync(
            @"SELECT *
              FROM proof_packages
              WHERE company_id=@companyId AND (@jobId::BIGINT IS NULL OR job_id=@jobId)
              ORDER BY created_at DESC, id DESC",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@jobId", (object?)jobId ?? DBNull.Value);
            }, ct);

    public async Task<Dictionary<string, object?>?> GetProofPackageAsync(long companyId, long id, CancellationToken ct = default)
        => await LoadByIdAsync("proof_packages", companyId, id, ct);

    public async Task<Dictionary<string, object?>?> CreateProofPackageAsync(long companyId, long? jobId, long? tripId, Dictionary<string, object?> body, string? idempotencyKey, CancellationToken ct = default)
    {
        var requestHash = FoundationPersistenceHelpers.ComputeHash(JsonSerializer.Serialize(new
        {
            companyId,
            jobId,
            tripId,
            proofType = Str(body, "proofType") ?? "proof_of_delivery",
            clientGeneratedId = Str(body, "clientGeneratedId"),
            sourceChannel = Str(body, "sourceChannel"),
        }));

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var reservation = idempotency.Reserve(companyId.ToString(CultureInfo.InvariantCulture), "stage9.proof_package.create", idempotencyKey!, requestHash, TimeSpan.FromHours(24));
            if (string.Equals(reservation.Status, "completed", StringComparison.OrdinalIgnoreCase) && long.TryParse(reservation.ResponseReference, out var completedId))
            {
                var existing = await LoadByIdAsync("proof_packages", companyId, completedId, ct);
                if (existing is not null)
                {
                    return existing;
                }
            }
        }

        var proofPackageId = await db.InsertAsync(
            @"INSERT INTO proof_packages
                (company_id, job_id, trip_id, proof_type, status, completed_at, completed_by_user_id,
                 receiver_name, receiver_phone, receiver_signature_file_id, geo_latitude, geo_longitude,
                 notes, validation_status, validation_summary, source_channel, client_generated_id,
                 idempotency_key, captured_at, uploaded_at, captured_by_user_id, device_id, mobile_app_version,
                 metadata_json, correlation_id, causation_id, created_at)
              VALUES
                (@companyId, @jobId, @tripId, @proofType, @status, @completedAt, @completedByUserId,
                 @receiverName, @receiverPhone, @signatureFileId, @geoLat, @geoLong,
                 @notes, @validationStatus, @validationSummary, @sourceChannel, @clientGeneratedId,
                 @idempotencyKey, @capturedAt, @uploadedAt, @capturedByUserId, @deviceId, @mobileAppVersion,
                 COALESCE(@metadata::jsonb, '{}'::jsonb), @correlationId, @causationId, NOW())",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@jobId", (object?)jobId ?? DBNull.Value);
                c.Parameters.AddWithValue("@tripId", (object?)tripId ?? DBNull.Value);
                c.Parameters.AddWithValue("@proofType", Str(body, "proofType") ?? "proof_of_delivery");
                c.Parameters.AddWithValue("@status", Str(body, "status") ?? "draft");
                c.Parameters.AddWithValue("@completedAt", ParseDate(body, "completedAt") ?? (object)DBNull.Value);
                c.Parameters.AddWithValue("@completedByUserId", (object?)Long(body, "completedByUserId") ?? DBNull.Value);
                c.Parameters.AddWithValue("@receiverName", (object?)Str(body, "receiverName") ?? DBNull.Value);
                c.Parameters.AddWithValue("@receiverPhone", (object?)Str(body, "receiverPhone") ?? DBNull.Value);
                c.Parameters.AddWithValue("@signatureFileId", (object?)Long(body, "receiverSignatureFileId") ?? DBNull.Value);
                c.Parameters.AddWithValue("@geoLat", (object?)DecN(body, "geoLatitude") ?? DBNull.Value);
                c.Parameters.AddWithValue("@geoLong", (object?)DecN(body, "geoLongitude") ?? DBNull.Value);
                c.Parameters.AddWithValue("@notes", (object?)Str(body, "notes") ?? DBNull.Value);
                c.Parameters.AddWithValue("@validationStatus", Str(body, "validationStatus") ?? "pending");
                c.Parameters.AddWithValue("@validationSummary", (object?)Str(body, "validationSummary") ?? DBNull.Value);
                c.Parameters.AddWithValue("@sourceChannel", (object?)Str(body, "sourceChannel") ?? DBNull.Value);
                c.Parameters.AddWithValue("@clientGeneratedId", (object?)Str(body, "clientGeneratedId") ?? DBNull.Value);
                c.Parameters.AddWithValue("@idempotencyKey", (object?)idempotencyKey ?? DBNull.Value);
                c.Parameters.AddWithValue("@capturedAt", ParseDate(body, "capturedAt") ?? (object)DBNull.Value);
                c.Parameters.AddWithValue("@uploadedAt", ParseDate(body, "uploadedAt") ?? (object)DBNull.Value);
                c.Parameters.AddWithValue("@capturedByUserId", (object?)Long(body, "capturedByUserId") ?? DBNull.Value);
                c.Parameters.AddWithValue("@deviceId", (object?)Str(body, "deviceId") ?? DBNull.Value);
                c.Parameters.AddWithValue("@mobileAppVersion", (object?)Str(body, "mobileAppVersion") ?? DBNull.Value);
                c.Parameters.AddWithValue("@metadata", Str(body, "metadataJson") ?? "{}");
                c.Parameters.AddWithValue("@correlationId", (object?)correlation.CorrelationId ?? DBNull.Value);
                c.Parameters.AddWithValue("@causationId", (object?)correlation.CausationId ?? DBNull.Value);
            }, ct);

        _ = events.Publish(
            companyId.ToString(CultureInfo.InvariantCulture),
            "proof_package.created",
            "proof_package",
            proofPackageId.ToString(CultureInfo.InvariantCulture),
            JsonSerializer.Serialize(new { proofPackageId, companyId, jobId, tripId, proofType = Str(body, "proofType") ?? "proof_of_delivery" }),
            correlation.CorrelationId,
            correlation.CausationId,
            idempotencyKey);

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            _ = idempotency.TryComplete(companyId.ToString(CultureInfo.InvariantCulture), "stage9.proof_package.create", idempotencyKey!, requestHash, proofPackageId.ToString(CultureInfo.InvariantCulture));
        }

        return await LoadByIdAsync("proof_packages", companyId, proofPackageId, ct);
    }

    public async Task<Stage9ActionOutcome> UpdateProofPackageAsync(long companyId, long id, Dictionary<string, object?> body, CancellationToken ct = default)
    {
        var current = await LoadByIdAsync("proof_packages", companyId, id, ct);
        if (current is null)
        {
            return new(false, "Proof package not found");
        }

        await db.ExecuteAsync(
            @"UPDATE proof_packages
              SET receiver_name=COALESCE(@receiverName, receiver_name),
                  receiver_phone=COALESCE(@receiverPhone, receiver_phone),
                  receiver_signature_file_id=COALESCE(@signatureFileId, receiver_signature_file_id),
                  geo_latitude=COALESCE(@geoLat, geo_latitude),
                  geo_longitude=COALESCE(@geoLong, geo_longitude),
                  notes=COALESCE(@notes, notes),
                  validation_summary=COALESCE(@validationSummary, validation_summary),
                  metadata_json=COALESCE(@metadata::jsonb, metadata_json),
                  updated_at=NOW()
              WHERE company_id=@companyId AND id=@id",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@id", id);
                c.Parameters.AddWithValue("@receiverName", (object?)Str(body, "receiverName") ?? DBNull.Value);
                c.Parameters.AddWithValue("@receiverPhone", (object?)Str(body, "receiverPhone") ?? DBNull.Value);
                c.Parameters.AddWithValue("@signatureFileId", (object?)Long(body, "receiverSignatureFileId") ?? DBNull.Value);
                c.Parameters.AddWithValue("@geoLat", (object?)DecN(body, "geoLatitude") ?? DBNull.Value);
                c.Parameters.AddWithValue("@geoLong", (object?)DecN(body, "geoLongitude") ?? DBNull.Value);
                c.Parameters.AddWithValue("@notes", (object?)Str(body, "notes") ?? DBNull.Value);
                c.Parameters.AddWithValue("@validationSummary", (object?)Str(body, "validationSummary") ?? DBNull.Value);
                c.Parameters.AddWithValue("@metadata", Str(body, "metadataJson") ?? "{}");
            }, ct);

        return new(true, "Proof package updated", false, null, await LoadByIdAsync("proof_packages", companyId, id, ct));
    }

    public async Task<Stage9ActionOutcome> SubmitProofPackageAsync(long companyId, long id, Dictionary<string, object?> body, CancellationToken ct = default)
    {
        var current = await LoadByIdAsync("proof_packages", companyId, id, ct);
        if (current is null)
        {
            return new(false, "Proof package not found");
        }

        var artifactCount = await db.ScalarLongAsync(
            "SELECT COUNT(*) FROM proof_artifacts WHERE company_id=@companyId AND proof_package_id=@id",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@id", id);
            }, ct);

        var exceptionNote = Str(body, "exceptionNote");
        if (artifactCount == 0 && string.IsNullOrWhiteSpace(exceptionNote))
        {
            _ = ai.CreateRecommendation(
                companyId.ToString(CultureInfo.InvariantCulture),
                "pod_missing_evidence",
                "Proof package missing evidence",
                "A proof package was submitted without an evidence artifact or exception note.",
                0.86m,
                0.82m,
                JsonSerializer.Serialize(new { proofPackageId = id, companyId }),
                JsonSerializer.Serialize(new { artifactCount, exceptionNote = false }),
                JsonSerializer.Serialize(new { action = "capture_evidence_or_exception" }),
                "high",
                id.ToString(CultureInfo.InvariantCulture),
                ActorTypes.System,
                "stage9-service",
                status: "active");

            return new(false, "Proof package requires at least one artifact or an exception note");
        }

        await db.ExecuteAsync(
            @"UPDATE proof_packages
              SET status='submitted',
                  validation_status='pending',
                  validation_summary=COALESCE(@summary, validation_summary),
                  notes=COALESCE(@notes, notes),
                  updated_at=NOW()
              WHERE company_id=@companyId AND id=@id",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@id", id);
                c.Parameters.AddWithValue("@summary", (object?)Str(body, "validationSummary") ?? DBNull.Value);
                c.Parameters.AddWithValue("@notes", (object?)exceptionNote ?? DBNull.Value);
            }, ct);

        _ = events.Publish(
            companyId.ToString(CultureInfo.InvariantCulture),
            "proof_package.submitted",
            "proof_package",
            id.ToString(CultureInfo.InvariantCulture),
            JsonSerializer.Serialize(new { proofPackageId = id, companyId, artifactCount }),
            correlation.CorrelationId,
            correlation.CausationId,
            $"proof_package.submit:{id}");

        return new(true, "Proof package submitted", false, null, await LoadByIdAsync("proof_packages", companyId, id, ct));
    }

    public async Task<Stage9ActionOutcome> ValidateProofPackageAsync(long companyId, long id, Dictionary<string, object?> body, CancellationToken ct = default)
    {
        var proof = await LoadByIdAsync("proof_packages", companyId, id, ct);
        if (proof is null)
        {
            return new(false, "Proof package not found");
        }

        var blockers = new List<string>();
        var jobId = proof.GetValueOrDefault("jobId") as long? ?? TryLong(proof.GetValueOrDefault("jobId"));
        var tripId = proof.GetValueOrDefault("tripId") as long? ?? TryLong(proof.GetValueOrDefault("tripId"));

        if (jobId.HasValue)
        {
            blockers.AddRange(await CountBlockersAsync(companyId, jobId.Value, "site_access_requirements", "job_id", new[] { "verified", "waived_with_approval" }, "site_access", ct));
            blockers.AddRange(await CountBlockersAsync(companyId, jobId.Value, "access_documents", "job_id", new[] { "verified", "waived_with_approval" }, "access_document", ct));
            blockers.AddRange(await CountBlockersAsync(companyId, jobId.Value, "pickup_authorizations", "job_id", new[] { "verified", "waived_with_approval" }, "pickup_authorization", ct));
            blockers.AddRange(await CountBlockersAsync(companyId, jobId.Value, "warehouse_handovers", "job_id", new[] { "completed" }, "warehouse_handover", ct));
        }

        if (tripId.HasValue)
        {
            blockers.AddRange(await CountBlockersAsync(companyId, tripId.Value, "site_access_requirements", "trip_id", new[] { "verified", "waived_with_approval" }, "site_access", ct));
            blockers.AddRange(await CountBlockersAsync(companyId, tripId.Value, "access_documents", "trip_id", new[] { "verified", "waived_with_approval" }, "access_document", ct));
            blockers.AddRange(await CountBlockersAsync(companyId, tripId.Value, "pickup_authorizations", "trip_id", new[] { "verified", "waived_with_approval" }, "pickup_authorization", ct));
            blockers.AddRange(await CountBlockersAsync(companyId, tripId.Value, "warehouse_handovers", "trip_id", new[] { "completed" }, "warehouse_handover", ct));
        }

        var artifactCount = await db.ScalarLongAsync(
            "SELECT COUNT(*) FROM proof_artifacts WHERE company_id=@companyId AND proof_package_id=@id",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@id", id);
            }, ct);

        if (artifactCount == 0)
        {
            blockers.Add("proof_artifacts:missing");
        }

        var hardBlocked = blockers.Count > 0;
        var validationStatus = hardBlocked ? "failed" : "passed";
        var proofStatus = hardBlocked ? "rejected" : "validated";
        var summary = hardBlocked
            ? $"Validation blocked: {string.Join(", ", blockers)}"
            : "Validation passed with operational evidence present";

        await db.ExecuteAsync(
            @"UPDATE proof_packages
              SET status=@status,
                  validation_status=@validationStatus,
                  validation_summary=@summary,
                  completed_at=COALESCE(completed_at, NOW()),
                  completed_by_user_id=COALESCE(@completedByUserId, completed_by_user_id),
                  updated_at=NOW()
              WHERE company_id=@companyId AND id=@id",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@id", id);
                c.Parameters.AddWithValue("@status", proofStatus);
                c.Parameters.AddWithValue("@validationStatus", validationStatus);
                c.Parameters.AddWithValue("@summary", summary);
                c.Parameters.AddWithValue("@completedByUserId", (object?)Long(body, "completedByUserId") ?? DBNull.Value);
            }, ct);

        if (hardBlocked)
        {
            _ = ai.CreateRecommendation(
                companyId.ToString(CultureInfo.InvariantCulture),
                "billing_blocked_missing_pod",
                "Billing blocked by missing proof",
                summary,
                0.92m,
                0.91m,
                JsonSerializer.Serialize(new { proofPackageId = id, companyId, blockers }),
                JsonSerializer.Serialize(new { proofPackageId = id, validationStatus, blockers }),
                JsonSerializer.Serialize(new { action = "resolve_proof_blockers" }),
                "high",
                id.ToString(CultureInfo.InvariantCulture),
                ActorTypes.System,
                "stage9-service",
                status: "active");
        }

        var confidenceScore = hardBlocked ? 0.35m : Math.Min(0.98m, 0.55m + (artifactCount * 0.1m) + (blockers.Count == 0 ? 0.2m : 0m));
        var confidenceId = await db.InsertAsync(
            @"INSERT INTO billing_confidence_records
                (company_id, job_id, trip_id, proof_package_id, confidence_score, status, reason_json, summary,
                 correlation_id, causation_id, created_at)
              VALUES
                (@companyId, @jobId, @tripId, @proofPackageId, @confidenceScore, @status,
                 COALESCE(@reason::jsonb, '{}'::jsonb), @summary, @correlationId, @causationId, NOW())",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@jobId", (object?)jobId ?? DBNull.Value);
                c.Parameters.AddWithValue("@tripId", (object?)tripId ?? DBNull.Value);
                c.Parameters.AddWithValue("@proofPackageId", id);
                c.Parameters.AddWithValue("@confidenceScore", confidenceScore);
                c.Parameters.AddWithValue("@status", hardBlocked ? "blocked" : "ready");
                c.Parameters.AddWithValue("@reason", JsonSerializer.Serialize(new { blockers, artifactCount }));
                c.Parameters.AddWithValue("@summary", summary);
                c.Parameters.AddWithValue("@correlationId", (object?)correlation.CorrelationId ?? DBNull.Value);
                c.Parameters.AddWithValue("@causationId", (object?)correlation.CausationId ?? DBNull.Value);
            }, ct);

        _ = events.Publish(
            companyId.ToString(CultureInfo.InvariantCulture),
            hardBlocked ? "proof_package.rejected" : "proof_package.validated",
            "proof_package",
            id.ToString(CultureInfo.InvariantCulture),
            JsonSerializer.Serialize(new { proofPackageId = id, companyId, validationStatus, blockers, confidenceScore }),
            correlation.CorrelationId,
            correlation.CausationId,
            $"proof_package.validate:{id}");

        _ = events.Publish(
            companyId.ToString(CultureInfo.InvariantCulture),
            "billing_confidence.updated",
            "billing_confidence_record",
            confidenceId.ToString(CultureInfo.InvariantCulture),
            JsonSerializer.Serialize(new { proofPackageId = id, companyId, confidenceScore, status = hardBlocked ? "blocked" : "ready" }),
            correlation.CorrelationId,
            correlation.CausationId,
            $"billing_confidence.updated:{id}");

        return new(true, summary, false, null, await LoadByIdAsync("proof_packages", companyId, id, ct), validationStatus, blockers);
    }

    public async Task<List<Dictionary<string, object?>>> ListProofArtifactsAsync(long companyId, long proofPackageId, CancellationToken ct = default)
        => await QueryListAsync(
            @"SELECT *
              FROM proof_artifacts
              WHERE company_id=@companyId AND proof_package_id=@proofPackageId
              ORDER BY created_at DESC, id DESC",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@proofPackageId", proofPackageId);
            }, ct);

    public async Task<Dictionary<string, object?>?> CreateProofArtifactAsync(long companyId, long proofPackageId, Dictionary<string, object?> body, string? idempotencyKey, CancellationToken ct = default)
    {
        var proofPackage = await LoadByIdAsync("proof_packages", companyId, proofPackageId, ct);
        if (proofPackage is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var existing = await LoadProofArtifactByIdempotencyAsync(companyId, idempotencyKey!, ct);
            if (existing is not null)
            {
                return existing;
            }
        }

        var artifactId = await db.InsertAsync(
            @"INSERT INTO proof_artifacts
                (company_id, proof_package_id, artifact_type, file_id, captured_at, uploaded_at, captured_by_user_id,
                 geo_latitude, geo_longitude, device_id, mobile_app_version, source_channel, notes, metadata_json,
                 idempotency_key, correlation_id, causation_id, created_at)
              VALUES
                (@companyId, @proofPackageId, @artifactType, @fileId, @capturedAt, @uploadedAt, @capturedByUserId,
                 @geoLat, @geoLong, @deviceId, @mobileAppVersion, @sourceChannel, @notes, COALESCE(@metadata::jsonb, '{}'::jsonb),
                 @idempotencyKey, @correlationId, @causationId, NOW())",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@proofPackageId", proofPackageId);
                c.Parameters.AddWithValue("@artifactType", Str(body, "artifactType") ?? "photo");
                c.Parameters.AddWithValue("@fileId", (object?)Long(body, "fileId") ?? DBNull.Value);
                c.Parameters.AddWithValue("@capturedAt", ParseDate(body, "capturedAt") ?? (object)DBNull.Value);
                c.Parameters.AddWithValue("@uploadedAt", ParseDate(body, "uploadedAt") ?? (object)DBNull.Value);
                c.Parameters.AddWithValue("@capturedByUserId", (object?)Long(body, "capturedByUserId") ?? DBNull.Value);
                c.Parameters.AddWithValue("@geoLat", (object?)DecN(body, "geoLatitude") ?? DBNull.Value);
                c.Parameters.AddWithValue("@geoLong", (object?)DecN(body, "geoLongitude") ?? DBNull.Value);
                c.Parameters.AddWithValue("@deviceId", (object?)Str(body, "deviceId") ?? DBNull.Value);
                c.Parameters.AddWithValue("@mobileAppVersion", (object?)Str(body, "mobileAppVersion") ?? DBNull.Value);
                c.Parameters.AddWithValue("@sourceChannel", (object?)Str(body, "sourceChannel") ?? DBNull.Value);
                c.Parameters.AddWithValue("@notes", (object?)Str(body, "notes") ?? DBNull.Value);
                c.Parameters.AddWithValue("@metadata", Str(body, "metadataJson") ?? "{}");
                c.Parameters.AddWithValue("@idempotencyKey", (object?)idempotencyKey ?? DBNull.Value);
                c.Parameters.AddWithValue("@correlationId", (object?)correlation.CorrelationId ?? DBNull.Value);
                c.Parameters.AddWithValue("@causationId", (object?)correlation.CausationId ?? DBNull.Value);
            }, ct);

        _ = events.Publish(
            companyId.ToString(CultureInfo.InvariantCulture),
            "proof_artifact.created",
            "proof_artifact",
            artifactId.ToString(CultureInfo.InvariantCulture),
            JsonSerializer.Serialize(new { artifactId, companyId, proofPackageId, artifactType = Str(body, "artifactType") ?? "photo" }),
            correlation.CorrelationId,
            correlation.CausationId,
            idempotencyKey);

        return await LoadByIdAsync("proof_artifacts", companyId, artifactId, ct);
    }

    public async Task<Dictionary<string, object?>?> GetBillingConfidenceAsync(long companyId, long proofPackageId, CancellationToken ct = default)
        => await db.QuerySingleAsync(
            @"SELECT *
              FROM billing_confidence_records
              WHERE company_id=@companyId AND proof_package_id=@proofPackageId
              ORDER BY created_at DESC, id DESC
              LIMIT 1",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@proofPackageId", proofPackageId);
            }, ct);

    public async Task<Dictionary<string, object?>?> GetExecutionSummaryAsync(long companyId, long jobId, CancellationToken ct = default)
    {
        try
        {
            var smartAssignment = await LoadLatestSmartAssignmentAsync(companyId, jobId, ct);
            var assignmentConfirmation = await LoadLatestByJobAsync("assignment_confirmations", companyId, jobId, ct);
            var siteAccessRequirements = await QueryListAsync(
                @"SELECT *
                  FROM site_access_requirements
                  WHERE company_id=@companyId AND job_id=@jobId
                  ORDER BY created_at DESC, id DESC",
                c =>
                {
                    c.Parameters.AddWithValue("@companyId", companyId);
                    c.Parameters.AddWithValue("@jobId", jobId);
                }, ct);
            var accessDocuments = await QueryListAsync(
                @"SELECT *
                  FROM access_documents
                  WHERE company_id=@companyId AND job_id=@jobId
                  ORDER BY created_at DESC, id DESC",
                c =>
                {
                    c.Parameters.AddWithValue("@companyId", companyId);
                    c.Parameters.AddWithValue("@jobId", jobId);
                }, ct);
            var pickupAuthorizations = await LoadLatestByJobAsync("pickup_authorizations", companyId, jobId, ct);
            var warehouseHandovers = await LoadLatestByJobAsync("warehouse_handovers", companyId, jobId, ct);
            var proofPackages = await LoadLatestByJobAsync("proof_packages", companyId, jobId, ct);
            var proofPackageId = TryLong(proofPackages?.GetValueOrDefault("id"));
            var proofArtifacts = proofPackageId is null
                ? new List<Dictionary<string, object?>>()
                : await QueryListAsync(
                    @"SELECT pa.*
                      FROM proof_artifacts pa
                      INNER JOIN proof_packages pp ON pp.id = pa.proof_package_id AND pp.company_id = pa.company_id
                      WHERE pa.company_id=@companyId AND pp.job_id=@jobId
                      ORDER BY pa.created_at DESC, pa.id DESC",
                    c =>
                    {
                        c.Parameters.AddWithValue("@companyId", companyId);
                        c.Parameters.AddWithValue("@jobId", jobId);
                    }, ct);
            var billingConfidence = await LoadLatestByJobAsync("billing_confidence_records", companyId, jobId, ct);
            var latestTripId = FirstLong(
                TryLong(smartAssignment?.GetValueOrDefault("tripId")),
                TryLong(assignmentConfirmation?.GetValueOrDefault("tripId")),
                TryLong(siteAccessRequirements.FirstOrDefault()?.GetValueOrDefault("tripId")),
                TryLong(accessDocuments.FirstOrDefault()?.GetValueOrDefault("tripId")),
                TryLong(pickupAuthorizations?.GetValueOrDefault("tripId")),
                TryLong(warehouseHandovers?.GetValueOrDefault("tripId")),
                TryLong(proofPackages?.GetValueOrDefault("tripId")),
                TryLong(billingConfidence?.GetValueOrDefault("tripId")));

            var siteAccessOpen = siteAccessRequirements.Count > 0 &&
                siteAccessRequirements.Any(row =>
                {
                    var status = row.GetValueOrDefault("status")?.ToString();
                    return !string.Equals(status, "verified", StringComparison.OrdinalIgnoreCase) &&
                           !string.Equals(status, "waived_with_approval", StringComparison.OrdinalIgnoreCase);
                });

            var accessDocOpen = accessDocuments.Count > 0 &&
                accessDocuments.Any(row =>
                {
                    var status = row.GetValueOrDefault("status")?.ToString();
                    return !string.Equals(status, "verified", StringComparison.OrdinalIgnoreCase) &&
                           !string.Equals(status, "waived_with_approval", StringComparison.OrdinalIgnoreCase);
                });

            var pickupOpen = pickupAuthorizations is not null &&
                !string.Equals(pickupAuthorizations.GetValueOrDefault("status")?.ToString(), "verified", StringComparison.OrdinalIgnoreCase);

            var handoverOpen = warehouseHandovers is not null &&
                !string.Equals(warehouseHandovers.GetValueOrDefault("status")?.ToString(), "completed", StringComparison.OrdinalIgnoreCase);

            var proofBlocked = proofPackages is null || !string.Equals(proofPackages.GetValueOrDefault("validationStatus")?.ToString(), "passed", StringComparison.OrdinalIgnoreCase);
            var proofHasArtifacts = proofArtifacts.Count > 0;
            var billingReady = billingConfidence is not null &&
                string.Equals(billingConfidence.GetValueOrDefault("status")?.ToString(), "ready", StringComparison.OrdinalIgnoreCase);

            return new Dictionary<string, object?>
            {
                ["job_id"] = jobId,
                ["trip_id"] = latestTripId,
                ["assignment_summary"] = BuildAssignmentSummary(smartAssignment, assignmentConfirmation),
                ["smart_assignment_summary"] = BuildSmartAssignmentSummary(smartAssignment),
                ["site_access_summary"] = BuildSiteAccessSummary(siteAccessRequirements, accessDocuments),
                ["access_document_summary"] = BuildAccessDocumentSummary(accessDocuments),
                ["pickup_authorization_summary"] = BuildPickupAuthorizationSummary(pickupAuthorizations),
                ["warehouse_handover_summary"] = BuildWarehouseHandoverSummary(warehouseHandovers),
                ["proof_package_summary"] = BuildProofPackageSummary(proofPackages),
                ["proof_artifact_summary"] = BuildProofArtifactSummary(proofArtifacts),
                ["validation_summary"] = BuildValidationSummary(proofPackages, proofHasArtifacts),
                ["billing_confidence_summary"] = BuildBillingConfidenceSummary(billingConfidence),
                ["risk_summary"] = new Dictionary<string, object?>
                {
                    ["status"] = BuildRiskStatus(siteAccessOpen, accessDocOpen, pickupOpen, handoverOpen, proofBlocked, billingReady),
                    ["open_blockers"] = BuildBlockers(siteAccessOpen, accessDocOpen, pickupOpen, handoverOpen, proofBlocked, billingReady),
                    ["billing_ready"] = billingReady,
                },
                ["next_best_actions"] = BuildNextBestActions(smartAssignment, siteAccessRequirements, accessDocuments, pickupAuthorizations, warehouseHandovers, proofPackages, proofArtifacts, billingConfidence),
                ["mobile_ready_actions"] = BuildMobileReadyActions(siteAccessRequirements, accessDocuments, pickupAuthorizations, warehouseHandovers, proofPackages, proofArtifacts, billingConfidence),
            };
        }
        catch (Exception ex)
        {
            return new Dictionary<string, object?>
            {
                ["job_id"] = jobId,
                ["trip_id"] = null,
                ["assignment_summary"] = new Dictionary<string, object?> { ["status"] = "no_data" },
                ["smart_assignment_summary"] = new Dictionary<string, object?> { ["status"] = "no_data" },
                ["site_access_summary"] = new Dictionary<string, object?> { ["count"] = 0, ["status"] = "no_data" },
                ["access_document_summary"] = new Dictionary<string, object?> { ["count"] = 0, ["status"] = "no_data" },
                ["pickup_authorization_summary"] = new Dictionary<string, object?> { ["status"] = "no_data" },
                ["warehouse_handover_summary"] = new Dictionary<string, object?> { ["status"] = "no_data" },
                ["proof_package_summary"] = new Dictionary<string, object?> { ["status"] = "no_data", ["validation_status"] = "no_data" },
                ["proof_artifact_summary"] = new Dictionary<string, object?> { ["count"] = 0, ["status"] = "no_data" },
                ["validation_summary"] = new Dictionary<string, object?> { ["status"] = "no_data", ["proof_has_artifacts"] = false },
                ["billing_confidence_summary"] = new Dictionary<string, object?> { ["status"] = "no_data" },
                ["risk_summary"] = new Dictionary<string, object?>
                {
                    ["status"] = "blocked",
                    ["open_blockers"] = new[] { "summary_unavailable" },
                    ["billing_ready"] = false,
                },
                ["next_best_actions"] = new[] { "Execution summary temporarily unavailable." },
                ["mobile_ready_actions"] = BuildMobileReadyActions(
                    new List<Dictionary<string, object?>>(),
                    new List<Dictionary<string, object?>>(),
                    null,
                    null,
                    null,
                    new List<Dictionary<string, object?>>(),
                    null),
                ["error"] = "Execution summary unavailable",
                ["errorDetail"] = ex.Message,
            };
        }
    }

    private async Task<List<string>> CountBlockersAsync(long companyId, long entityId, string table, string scopeColumn, IReadOnlyCollection<string> acceptedStatuses, string blockerPrefix, CancellationToken ct)
    {
        var accepted = acceptedStatuses.Select(status => status.ToLowerInvariant()).ToArray();
        var rows = await db.QueryAsync(
            $@"SELECT id, status
               FROM {table}
               WHERE company_id=@companyId AND {scopeColumn}=@entityId",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@entityId", entityId);
            }, ct);

        return rows
            .Where(row =>
            {
                var status = row.GetValueOrDefault("status")?.ToString() ?? string.Empty;
                return !accepted.Contains(status.ToLowerInvariant());
            })
            .Select(row => $"{blockerPrefix}:{row.GetValueOrDefault("id")}")
            .ToList();
    }

    private async Task<Dictionary<string, object?>?> LoadAssignmentConfirmationAsync(long companyId, long recommendationId, CancellationToken ct)
    {
        return await db.QuerySingleAsync(
            @"SELECT *
              FROM assignment_confirmations
              WHERE company_id=@companyId AND (job_id IS NOT NULL OR trip_id IS NOT NULL)
              ORDER BY created_at DESC, id DESC
              LIMIT 1",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
            }, ct);
    }

    private async Task<Dictionary<string, object?>?> LoadLatestByJobAsync(string table, long companyId, long jobId, CancellationToken ct)
        => await db.QuerySingleAsync(
            $"SELECT * FROM {table} WHERE company_id=@companyId AND job_id=@jobId ORDER BY created_at DESC, id DESC LIMIT 1",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@jobId", jobId);
            }, ct);

    private async Task<Dictionary<string, object?>?> LoadLatestSmartAssignmentAsync(long companyId, long jobId, CancellationToken ct)
    {
        try
        {
            return await LoadLatestByJobAsync("smart_assignment_recommendations", companyId, jobId, ct);
        }
        catch
        {
            return await db.QuerySingleAsync(
                @"SELECT id,
                         company_id,
                         module_key AS recommendationType,
                         title,
                         body AS summary,
                         score,
                         score AS confidenceScore,
                         COALESCE(priority, 'medium') AS riskLevel,
                         status,
                         action_label AS proposedActionJson,
                         action_type AS actionType,
                         correlation_id AS correlationId,
                         causation_id AS causationId,
                         NULL::bigint AS jobId,
                         NULL::bigint AS tripId,
                         NULL::text AS reasonJson,
                         NULL::text AS constraintJson,
                         NULL::text AS recommendedDriverId,
                         NULL::text AS recommendedVehicleId,
                         NULL::text AS recommendedCrewId,
                         NULL::timestamptz AS createdAt
                  FROM ai_recommendations
                  WHERE company_id=@companyId
                    AND module_key IN ('dispatch', 'control-tower', 'command-center')
                  ORDER BY id DESC
                  LIMIT 1",
                c => c.Parameters.AddWithValue("@companyId", companyId), ct);
        }
    }

    private static Dictionary<string, object?> BuildAssignmentSummary(Dictionary<string, object?>? recommendation, Dictionary<string, object?>? confirmation)
        => new()
        {
            ["status"] = confirmation is not null ? confirmation.GetValueOrDefault("status")?.ToString() : recommendation is not null ? recommendation.GetValueOrDefault("status")?.ToString() : "no_data",
            ["latest_confirmation"] = PickFields(confirmation, "id", "status", "acceptedAt", "rejectedAt", "driverId", "vehicleId", "rejectionReason", "createdAt"),
            ["latest_recommendation"] = PickFields(recommendation, "id", "status", "score", "confidenceScore", "riskLevel", "recommendationType", "recommendedDriverId", "recommendedVehicleId", "recommendedCrewId", "reasonJson", "proposedActionJson", "jobId", "tripId", "createdAt"),
        };

    private static Dictionary<string, object?> BuildSmartAssignmentSummary(Dictionary<string, object?>? recommendation)
        => new()
        {
            ["status"] = recommendation is null ? "no_data" : recommendation.GetValueOrDefault("status")?.ToString(),
            ["score"] = recommendation?.GetValueOrDefault("score"),
            ["confidence_score"] = recommendation?.GetValueOrDefault("confidenceScore"),
            ["risk_level"] = recommendation?.GetValueOrDefault("riskLevel"),
            ["latest"] = PickFields(recommendation, "id", "status", "score", "confidenceScore", "riskLevel", "recommendedDriverId", "recommendedVehicleId", "recommendedCrewId", "reasonJson", "constraintJson", "proposedActionJson", "createdAt"),
        };

    private static Dictionary<string, object?> BuildSiteAccessSummary(List<Dictionary<string, object?>> requirements, List<Dictionary<string, object?>> documents)
        => new()
        {
            ["count"] = requirements.Count,
            ["status"] = requirements.Count == 0 ? "no_data" : requirements.Any(row => !IsTerminalStatus(row.GetValueOrDefault("status")?.ToString(), "verified", "waived_with_approval")) ? "open" : "closed",
            ["latest_requirement"] = PickFields(requirements.FirstOrDefault(), "id", "status", "requirementType", "requiredBefore", "instructions", "contactName", "contactPhone", "jobId", "tripId", "createdAt"),
            ["latest_document"] = PickFields(documents.FirstOrDefault(), "id", "status", "documentType", "documentNo", "issuedBy", "issuedTo", "validFrom", "validTo", "notes", "jobId", "tripId", "createdAt"),
        };

    private static Dictionary<string, object?> BuildAccessDocumentSummary(List<Dictionary<string, object?>> documents)
        => new()
        {
            ["count"] = documents.Count,
            ["status"] = documents.Count == 0 ? "no_data" : documents.Any(row => !IsTerminalStatus(row.GetValueOrDefault("status")?.ToString(), "verified", "waived_with_approval")) ? "open" : "closed",
            ["latest"] = PickFields(documents.FirstOrDefault(), "id", "status", "documentType", "documentNo", "issuedBy", "issuedTo", "validFrom", "validTo", "notes", "capturedAt", "uploadedAt", "deviceId", "geoLatitude", "geoLongitude", "createdAt"),
        };

    private static Dictionary<string, object?> BuildPickupAuthorizationSummary(Dictionary<string, object?>? record)
        => new()
        {
            ["status"] = record is null ? "no_data" : record.GetValueOrDefault("status")?.ToString(),
            ["latest"] = PickFields(record, "id", "status", "authorizationNo", "thirdPartyName", "authorizedPersonName", "authorizedPersonPhone", "validFrom", "validTo", "notes", "jobId", "tripId", "createdAt"),
        };

    private static Dictionary<string, object?> BuildWarehouseHandoverSummary(Dictionary<string, object?>? record)
        => new()
        {
            ["status"] = record is null ? "no_data" : record.GetValueOrDefault("status")?.ToString(),
            ["latest"] = PickFields(record, "id", "status", "handoverType", "warehouseName", "warehouseReferenceNo", "scheduledAt", "completedAt", "handledByName", "notes", "jobId", "tripId", "createdAt"),
        };

    private static Dictionary<string, object?> BuildProofPackageSummary(Dictionary<string, object?>? record)
        => new()
        {
            ["status"] = record is null ? "no_data" : record.GetValueOrDefault("status")?.ToString(),
            ["validation_status"] = record?.GetValueOrDefault("validationStatus")?.ToString() ?? "no_data",
            ["latest"] = PickFields(record, "id", "status", "proofType", "validationStatus", "validationSummary", "receiverName", "receiverPhone", "completedAt", "geoLatitude", "geoLongitude", "notes", "jobId", "tripId", "createdAt"),
        };

    private static Dictionary<string, object?> BuildProofArtifactSummary(List<Dictionary<string, object?>> artifacts)
        => new()
        {
            ["count"] = artifacts.Count,
            ["status"] = artifacts.Count == 0 ? "no_data" : "attached",
            ["latest"] = PickFields(artifacts.FirstOrDefault(), "id", "artifactType", "fileId", "capturedAt", "uploadedAt", "capturedByUserId", "deviceId", "geoLatitude", "geoLongitude", "notes", "createdAt"),
        };

    private static Dictionary<string, object?> BuildValidationSummary(Dictionary<string, object?>? proofPackage, bool proofHasArtifacts)
        => new()
        {
            ["status"] = proofPackage is null ? "no_data" : proofPackage.GetValueOrDefault("validationStatus")?.ToString(),
            ["summary"] = proofPackage?.GetValueOrDefault("validationSummary")?.ToString(),
            ["proof_has_artifacts"] = proofHasArtifacts,
            ["proof_status"] = proofPackage?.GetValueOrDefault("status")?.ToString(),
        };

    private static Dictionary<string, object?> BuildBillingConfidenceSummary(Dictionary<string, object?>? record)
        => new()
        {
            ["status"] = record is null ? "no_data" : record.GetValueOrDefault("status")?.ToString(),
            ["confidence_score"] = record?.GetValueOrDefault("confidenceScore"),
            ["summary"] = record?.GetValueOrDefault("summary")?.ToString(),
            ["latest"] = PickFields(record, "id", "status", "confidenceScore", "summary", "proofPackageId", "jobId", "tripId", "createdAt"),
        };

    private static string BuildRiskStatus(bool siteAccessOpen, bool accessDocOpen, bool pickupOpen, bool handoverOpen, bool proofBlocked, bool billingReady)
        => siteAccessOpen || accessDocOpen || pickupOpen || handoverOpen || proofBlocked
            ? (billingReady ? "risky" : "blocked")
            : "confidence_ready";

    private static List<string> BuildBlockers(bool siteAccessOpen, bool accessDocOpen, bool pickupOpen, bool handoverOpen, bool proofBlocked, bool billingReady)
    {
        var blockers = new List<string>();
        if (siteAccessOpen) blockers.Add("site_access");
        if (accessDocOpen) blockers.Add("access_document");
        if (pickupOpen) blockers.Add("pickup_authorization");
        if (handoverOpen) blockers.Add("warehouse_handover");
        if (proofBlocked) blockers.Add("proof_validation");
        if (!billingReady) blockers.Add("billing_confidence_pending");
        return blockers;
    }

    private static List<string> BuildNextBestActions(
        Dictionary<string, object?>? smartAssignment,
        List<Dictionary<string, object?>> siteAccessRequirements,
        List<Dictionary<string, object?>> accessDocuments,
        Dictionary<string, object?>? pickupAuthorizations,
        Dictionary<string, object?>? warehouseHandovers,
        Dictionary<string, object?>? proofPackages,
        List<Dictionary<string, object?>> proofArtifacts,
        Dictionary<string, object?>? billingConfidence)
    {
        var actions = new List<string>();
        if (smartAssignment is null) actions.Add("Create a smart assignment recommendation.");
        if (siteAccessRequirements.Count == 0) actions.Add("Record the site access or Gate Pass requirement.");
        if (accessDocuments.Count == 0) actions.Add("Create the access document or NOC record.");
        if (pickupAuthorizations is null) actions.Add("Record the third-party pickup authorization.");
        if (warehouseHandovers is null) actions.Add("Record the warehouse handover.");
        if (proofPackages is null) actions.Add("Create the proof package.");
        if (proofPackages is not null && proofArtifacts.Count == 0) actions.Add("Attach evidence artifacts before submitting proof.");
        if (proofPackages is not null && !string.Equals(proofPackages.GetValueOrDefault("validationStatus")?.ToString(), "passed", StringComparison.OrdinalIgnoreCase))
        {
            actions.Add("Validate the proof package once evidence is complete.");
        }
        if (billingConfidence is null) actions.Add("Generate a billing confidence signal after validation.");
        return actions;
    }

    private static List<Dictionary<string, object?>> BuildMobileReadyActions(
        List<Dictionary<string, object?>> siteAccessRequirements,
        List<Dictionary<string, object?>> accessDocuments,
        Dictionary<string, object?>? pickupAuthorizations,
        Dictionary<string, object?>? warehouseHandovers,
        Dictionary<string, object?>? proofPackages,
        List<Dictionary<string, object?>> proofArtifacts,
        Dictionary<string, object?>? billingConfidence)
        => new()
        {
            new()
            {
                ["role"] = "Dispatcher / Supervisor",
                ["route_family"] = "/api/operations/jobs/{jobId}/execution-summary",
                ["permission"] = "operations.execution_summary.read",
                ["offline_ready"] = true,
                ["evidence_ready"] = true,
                ["future_events"] = new[] { "smart_assignment.recommended", "proof_package.validated", "billing_confidence.updated" },
            },
            new()
            {
                ["role"] = "Field Worker / Cleaner / Technician / Guard",
                ["route_family"] = "/api/jobs/{jobId}/site-access + /api/jobs/{jobId}/proof-packages",
                ["permission"] = "operations.site_access.create",
                ["offline_ready"] = true,
                ["evidence_ready"] = true,
                ["future_events"] = new[] { "site_access.required", "proof_package.submitted" },
            },
            new()
            {
                ["role"] = "Warehouse User",
                ["route_family"] = "/api/jobs/{jobId}/pickup-authorizations + /api/jobs/{jobId}/warehouse-handovers",
                ["permission"] = "operations.pickup_authorization.read",
                ["offline_ready"] = true,
                ["evidence_ready"] = true,
                ["future_events"] = new[] { "pickup_authorization.verified", "warehouse_handover.completed" },
            },
            new()
            {
                ["role"] = "Third-Party Pickup User",
                ["route_family"] = "/api/pickup-authorizations/{id}",
                ["permission"] = "operations.pickup_authorization.verify",
                ["offline_ready"] = true,
                ["evidence_ready"] = true,
                ["future_events"] = new[] { "pickup_authorization.verified" },
            },
            new()
            {
                ["role"] = "Customer / Client User",
                ["route_family"] = "/api/proof-packages/{id} + /api/proof-packages/{id}/validate",
                ["permission"] = "customer_portal:view",
                ["offline_ready"] = false,
                ["evidence_ready"] = billingConfidence is not null && string.Equals(billingConfidence.GetValueOrDefault("status")?.ToString(), "ready", StringComparison.OrdinalIgnoreCase),
                ["future_events"] = new[] { "proof_package.validated" },
            },
        };

    private static Dictionary<string, object?>? PickFields(Dictionary<string, object?>? row, params string[] keys)
    {
        if (row is null) return null;
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys)
        {
            if (row.TryGetValue(key, out var value))
            {
                result[key] = value;
            }
        }
        return result;
    }

    private static bool IsTerminalStatus(string? value, params string[] accepted)
        => accepted.Any(status => string.Equals(status, value, StringComparison.OrdinalIgnoreCase));

    private static long? FirstLong(params long?[] values)
        => values.FirstOrDefault(v => v.HasValue);

    private static decimal CalculateRecommendationScore(Dictionary<string, object?> body)
    {
        var score = 0.55m;
        if (Long(body, "recommendedDriverId").HasValue) score += 0.12m; else score -= 0.1m;
        if (Long(body, "recommendedVehicleId").HasValue) score += 0.12m; else score -= 0.1m;
        if (Long(body, "recommendedCrewId").HasValue) score += 0.05m;
        if (!string.IsNullOrWhiteSpace(Str(body, "riskLevel")) && string.Equals(Str(body, "riskLevel"), "high", StringComparison.OrdinalIgnoreCase))
        {
            score -= 0.1m;
        }

        return Math.Max(0.05m, Math.Min(0.99m, score));
    }

    private async Task<Dictionary<string, object?>?> LoadByIdAsync(string table, long companyId, long id, CancellationToken ct)
        => await db.QuerySingleAsync(
            $"SELECT * FROM {table} WHERE company_id=@companyId AND id=@id LIMIT 1",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@id", id);
            }, ct);

    private async Task<Dictionary<string, object?>?> LoadByIdempotencyAsync(string table, long companyId, string idempotencyKey, CancellationToken ct)
        => await db.QuerySingleAsync(
            $"SELECT * FROM {table} WHERE company_id=@companyId AND idempotency_key=@idempotencyKey ORDER BY created_at DESC, id DESC LIMIT 1",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@idempotencyKey", idempotencyKey);
            }, ct);

    private async Task<Dictionary<string, object?>?> LoadProofArtifactByIdempotencyAsync(long companyId, string idempotencyKey, CancellationToken ct)
        => await db.QuerySingleAsync(
            @"SELECT *
              FROM proof_artifacts
              WHERE company_id=@companyId AND idempotency_key=@idempotencyKey
              ORDER BY created_at DESC, id DESC
              LIMIT 1",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@idempotencyKey", idempotencyKey);
            }, ct);

    private async Task<List<Dictionary<string, object?>>> QueryListAsync(string sql, Action<NpgsqlCommand>? bind, CancellationToken ct)
        => await db.QueryAsync(sql, bind, ct);

    private static string? Str(Dictionary<string, object?> body, string key)
        => body.TryGetValue(key, out var value) && value is not null ? value.ToString() : null;

    private static long? Long(Dictionary<string, object?> body, string key)
    {
        var value = body.TryGetValue(key, out var v) ? v : null;
        if (value is null or DBNull) return null;
        if (value is long l) return l;
        if (value is int i) return i;
        if (long.TryParse(value.ToString(), out var parsed)) return parsed;
        return null;
    }

    private static object? ParseDate(Dictionary<string, object?> body, string key)
    {
        if (!body.TryGetValue(key, out var value) || value is null || value is DBNull)
        {
            return null;
        }

        if (value is DateTimeOffset dto)
        {
            return dto;
        }

        if (value is DateTime dt)
        {
            return new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc));
        }

        return DateTimeOffset.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }

    private static decimal Dec(Dictionary<string, object?> body, string key, decimal defaultValue)
        => DecN(body, key) ?? defaultValue;

    private static decimal? DecN(Dictionary<string, object?> body, string key)
    {
        var value = body.TryGetValue(key, out var v) ? v : null;
        if (value is null or DBNull) return null;
        if (value is decimal d) return d;
        if (value is double db) return Convert.ToDecimal(db, CultureInfo.InvariantCulture);
        if (value is float f) return Convert.ToDecimal(f, CultureInfo.InvariantCulture);
        if (decimal.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)) return parsed;
        return null;
    }

    private static bool Bool(Dictionary<string, object?> body, string key, bool defaultValue)
    {
        var value = body.TryGetValue(key, out var v) ? v : null;
        if (value is null or DBNull) return defaultValue;
        if (value is bool b) return b;
        if (bool.TryParse(value.ToString(), out var parsed)) return parsed;
        return defaultValue;
    }

    private static long? TryLong(object? value)
    {
        if (value is null or DBNull) return null;
        if (value is long l) return l;
        if (value is int i) return i;
        if (long.TryParse(value.ToString(), out var parsed)) return parsed;
        return null;
    }
}
