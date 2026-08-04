using Microsoft.Extensions.Configuration;
using Opstrax.Api.Data;
using Opstrax.Api.Foundation;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

public class Stage9PostgresTests
{
    private static readonly string LocalConnectionString = TestDb.ConnectionString;

    [Fact]
    public async Task Stage9_SiteAccessRequirement_CreatesOperationalRecommendation()
    {
        var db = CreateDatabase();
        var schema = new Stage9SchemaService(db);
        var companyId = NextCompanyId();
        var ambient = new AmbientCorrelationContext();
        var ai = new PostgresAiFoundationService(db, ambient);
        var approval = new PostgresApprovalWorkflowService(db, ambient);
        var service = new Stage9OperationalFoundationService(db, ai, approval, new PostgresDomainEventPublisher(db, ambient), new InMemoryIdempotencyService(), ambient);

        try
        {
            await schema.EnsureAsync();
            using var scope = AmbientCorrelationContext.Begin($"stage9-site-{Guid.NewGuid():N}", $"cause-{Guid.NewGuid():N}", $"req-{Guid.NewGuid():N}", companyId.ToString(), ActorTypes.TenantUser, "42");

            var record = await service.CreateSiteAccessRequirementAsync(
                companyId,
                jobId: 501,
                tripId: 601,
                new Dictionary<string, object?>
                {
                    ["requirementType"] = "gate_pass",
                    ["instructions"] = "Call site security on arrival",
                });

            Assert.NotNull(record);
            Assert.Equal(companyId, Convert.ToInt64(record!["companyId"]));
            Assert.Equal("required", record["status"]?.ToString());
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM site_access_requirements WHERE company_id=@companyId AND job_id=501 AND trip_id=601", c => c.Parameters.AddWithValue("@companyId", companyId)));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM ai_recommendations WHERE tenant_id=@tenantId AND recommendation_type='site_access.missing' AND status='active'", c => c.Parameters.AddWithValue("@tenantId", companyId)));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM domain_events WHERE tenant_id=@tenantId AND event_type='site_access.required'", c => c.Parameters.AddWithValue("@tenantId", companyId)));
        }
        finally
        {
            await CleanupTenantAsync(db, companyId);
        }
    }

    [Fact]
    public async Task Stage9_AccessDocument_Waiver_CreatesApprovalRequest_And_DoesNotAutoApprove()
    {
        var db = CreateDatabase();
        var schema = new Stage9SchemaService(db);
        var companyId = NextCompanyId();
        var ambient = new AmbientCorrelationContext();
        var ai = new PostgresAiFoundationService(db, ambient);
        var approval = new PostgresApprovalWorkflowService(db, ambient);
        var service = new Stage9OperationalFoundationService(db, ai, approval, new PostgresDomainEventPublisher(db, ambient), new InMemoryIdempotencyService(), ambient);

        try
        {
            await schema.EnsureAsync();
            using var scope = AmbientCorrelationContext.Begin($"stage9-doc-{Guid.NewGuid():N}", $"cause-{Guid.NewGuid():N}", $"req-{Guid.NewGuid():N}", companyId.ToString(), ActorTypes.TenantUser, "42");

            var created = await service.CreateAccessDocumentAsync(companyId, 901, 902, new Dictionary<string, object?>
            {
                ["documentType"] = "gate_pass",
                ["documentNo"] = "GP-1001",
            }, "stage9-doc-idem-1");

            Assert.NotNull(created);

            var outcome = await service.UpdateAccessDocumentStatusAsync(companyId, Convert.ToInt64(created!["id"]), new Dictionary<string, object?>
            {
                ["status"] = "waived_with_approval",
                ["notes"] = "Supervisor waiver requested",
            });

            Assert.True(outcome.ApprovalRequired);
            Assert.False(outcome.Success);
            Assert.NotNull(outcome.ApprovalRequestId);
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM access_documents WHERE company_id=@companyId AND status='waived_with_approval'", c => c.Parameters.AddWithValue("@companyId", companyId)));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM approval_requests WHERE tenant_id=@tenantId AND action_key='operations.access_document.waive' AND status='pending'", c => c.Parameters.AddWithValue("@tenantId", companyId)));
        }
        finally
        {
            await CleanupTenantAsync(db, companyId);
        }
    }

    [Fact]
    public async Task Stage9_ProofPackage_SubmitWithoutArtifacts_CreatesAIRecommendationAndBlocksSubmit()
    {
        var db = CreateDatabase();
        var schema = new Stage9SchemaService(db);
        var companyId = NextCompanyId();
        var ambient = new AmbientCorrelationContext();
        var ai = new PostgresAiFoundationService(db, ambient);
        var approval = new PostgresApprovalWorkflowService(db, ambient);
        var service = new Stage9OperationalFoundationService(db, ai, approval, new PostgresDomainEventPublisher(db, ambient), new InMemoryIdempotencyService(), ambient);

        try
        {
            await schema.EnsureAsync();
            using var scope = AmbientCorrelationContext.Begin($"stage9-proof-{Guid.NewGuid():N}", $"cause-{Guid.NewGuid():N}", $"req-{Guid.NewGuid():N}", companyId.ToString(), ActorTypes.TenantUser, "42");

            var proof = await service.CreateProofPackageAsync(companyId, 1201, 1301, new Dictionary<string, object?>
            {
                ["proofType"] = "proof_of_delivery",
                ["status"] = "draft",
            }, "stage9-proof-idem-1");

            var submit = await service.SubmitProofPackageAsync(companyId, Convert.ToInt64(proof!["id"]), new Dictionary<string, object?>());

            Assert.False(submit.Success);
            Assert.Contains("requires at least one artifact", submit.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM ai_recommendations WHERE tenant_id=@tenantId AND recommendation_type='pod_missing_evidence' AND status='active'", c => c.Parameters.AddWithValue("@tenantId", companyId)));
            Assert.Equal(0, await db.ScalarLongAsync("SELECT COUNT(*) FROM proof_packages WHERE company_id=@companyId AND status='submitted'", c => c.Parameters.AddWithValue("@companyId", companyId)));
        }
        finally
        {
            await CleanupTenantAsync(db, companyId);
        }
    }

    [Fact]
    public async Task Stage9_SmartAssignment_HighRisk_Accept_ReturnsApprovalRequired()
    {
        var db = CreateDatabase();
        var schema = new Stage9SchemaService(db);
        var companyId = NextCompanyId();
        var ambient = new AmbientCorrelationContext();
        var ai = new PostgresAiFoundationService(db, ambient);
        var approval = new PostgresApprovalWorkflowService(db, ambient);
        var service = new Stage9OperationalFoundationService(db, ai, approval, new PostgresDomainEventPublisher(db, ambient), new InMemoryIdempotencyService(), ambient);

        try
        {
            await schema.EnsureAsync();
            using var scope = AmbientCorrelationContext.Begin($"stage9-assign-{Guid.NewGuid():N}", $"cause-{Guid.NewGuid():N}", $"req-{Guid.NewGuid():N}", companyId.ToString(), ActorTypes.TenantUser, "42");

            var recommendation = await service.RecommendSmartAssignmentAsync(
                companyId,
                jobId: 2201,
                tripId: 2301,
                new Dictionary<string, object?>
                {
                    ["recommendedDriverId"] = 99,
                    ["score"] = 0.40m,
                    ["riskLevel"] = "high",
                    ["sourceChannel"] = "mobile",
                },
                "mobile",
                "client-assign-1",
                "stage9-assign-idem-1");

            Assert.NotNull(recommendation);

            var outcome = await service.AcceptSmartAssignmentAsync(companyId, Convert.ToInt64(recommendation!["id"]), new Dictionary<string, object?>
            {
                ["requiresApproval"] = true,
            });

            Assert.True(outcome.ApprovalRequired);
            Assert.False(outcome.Success);
            Assert.NotNull(outcome.ApprovalRequestId);
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM approval_requests WHERE tenant_id=@tenantId AND action_key='dispatch.trip.reassign_high_value' AND status='pending'", c => c.Parameters.AddWithValue("@tenantId", companyId)));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM smart_assignment_recommendations WHERE company_id=@companyId AND status='draft'", c => c.Parameters.AddWithValue("@companyId", companyId)));
        }
        finally
        {
            await CleanupTenantAsync(db, companyId);
        }
    }

    [Fact]
    public async Task Stage9_ProofLifecycle_IsImmutableActorBoundTenantSafeAndSingleWinner()
    {
        var db = CreateDatabase();
        var companyId = NextCompanyId();
        var ambient = new AmbientCorrelationContext();
        var service = new Stage9OperationalFoundationService(db,
            new PostgresAiFoundationService(db, ambient), new PostgresApprovalWorkflowService(db, ambient),
            new PostgresDomainEventPublisher(db, ambient), new InMemoryIdempotencyService(), ambient);
        try
        {
            await new Stage9SchemaService(db).EnsureAsync();
            using var scope = AmbientCorrelationContext.Begin($"proof-race-{Guid.NewGuid():N}", null, null,
                companyId.ToString(), ActorTypes.TenantUser, "77");
            var proof = await service.CreateProofPackageAsync(companyId, 88001, null,
                new Dictionary<string, object?> { ["proofType"] = "proof_of_delivery", ["status"] = "validated", ["capturedByUserId"] = 999L },
                $"proof-{Guid.NewGuid():N}");
            Assert.NotNull(proof);
            Assert.Equal("draft", proof!["status"]);
            Assert.Equal("pending", proof["validationStatus"]);
            Assert.Equal(77, Convert.ToInt64(proof["capturedByUserId"]));
            var id = Convert.ToInt64(proof["id"]);
            Assert.Null(await service.GetProofPackageAsync(companyId + 1, id));
            Assert.False((await service.ValidateProofPackageAsync(companyId, id, new())).Success);
            Assert.Null(await service.CreateProofArtifactAsync(companyId, id,
                new Dictionary<string, object?> { ["artifactType"] = "photo", ["capturedByUserId"] = 999L }, null));
            Assert.Null(await service.CreateProofArtifactAsync(companyId, id,
                new Dictionary<string, object?> { ["artifactType"] = "photo", ["fileId"] = 9001L }, null));
            Assert.False((await service.UpdateProofPackageAsync(companyId, id,
                new() { ["receiverSignatureFileId"] = 9001L })).Success);

            var documentId = await db.InsertAsync(
                @"INSERT INTO documents (company_id,title,document_type,status,file_url)
                  VALUES (@companyId,'Pilot POD','Proof','Active',@fileUrl)",
                c =>
                {
                    c.Parameters.AddWithValue("@companyId", companyId);
                    c.Parameters.AddWithValue("@fileUrl", $"objkey:tenant/{companyId}/proof/2026/08/pilot.jpg");
                });
            var artifactIdempotencyKey = $"artifact-{Guid.NewGuid():N}";
            var artifacts = await Task.WhenAll(
                service.CreateProofArtifactAsync(companyId, id,
                    new Dictionary<string, object?> { ["artifactType"] = "photo", ["fileId"] = documentId, ["capturedByUserId"] = 999L },
                    artifactIdempotencyKey),
                service.CreateProofArtifactAsync(companyId, id,
                    new Dictionary<string, object?> { ["artifactType"] = "photo", ["fileId"] = documentId, ["capturedByUserId"] = 999L },
                    artifactIdempotencyKey));
            Assert.All(artifacts, Assert.NotNull);
            Assert.Equal(artifacts[0]!["id"], artifacts[1]!["id"]);
            Assert.Equal(77, Convert.ToInt64(artifacts[0]!["capturedByUserId"]));
            Assert.Equal(1, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM proof_artifacts WHERE company_id=@c AND proof_package_id=@p",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@p", id); }));
            Assert.True((await service.SubmitProofPackageAsync(companyId, id, new())).Success);
            Assert.False((await service.SubmitProofPackageAsync(companyId, id, new())).Success);
            Assert.False((await service.UpdateProofPackageAsync(companyId, id, new() { ["notes"] = "tamper" })).Success);
            Assert.Null(await service.CreateProofArtifactAsync(companyId, id,
                new Dictionary<string, object?> { ["artifactType"] = "photo", ["fileId"] = 9002L }, null));

            var results = await Task.WhenAll(
                service.ValidateProofPackageAsync(companyId, id, new()),
                service.ValidateProofPackageAsync(companyId, id, new()));
            Assert.Single(results, result => result.Success);
            Assert.Single(results, result => !result.Success);
            Assert.Equal(1, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM billing_confidence_records WHERE company_id=@c AND proof_package_id=@p",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@p", id); }));
        }
        finally { await CleanupTenantAsync(db, companyId); }
    }

    [Fact]
    public async Task Stage9_SmartAssignmentAndOperationalStates_RejectSpoofingReplayAndRegression()
    {
        var db = CreateDatabase();
        var companyId = NextCompanyId();
        var ambient = new AmbientCorrelationContext();
        var service = new Stage9OperationalFoundationService(db,
            new PostgresAiFoundationService(db, ambient), new PostgresApprovalWorkflowService(db, ambient),
            new PostgresDomainEventPublisher(db, ambient), new InMemoryIdempotencyService(), ambient);
        try
        {
            await new Stage9SchemaService(db).EnsureAsync();
            using var scope = AmbientCorrelationContext.Begin($"stage9-state-{Guid.NewGuid():N}", null, null,
                companyId.ToString(), ActorTypes.TenantUser, "78");

            var idempotencyKey = $"smart-{Guid.NewGuid():N}";
            var body = new Dictionary<string, object?>
            {
                ["recommendedDriverId"] = 7001L,
                ["recommendedVehicleId"] = 8001L,
                ["status"] = "accepted",
                ["createdBy"] = 999L,
                ["score"] = 4.2m,
                ["confidenceScore"] = -2m,
                ["riskLevel"] = "low",
            };
            var recommendations = await Task.WhenAll(
                service.RecommendSmartAssignmentAsync(companyId, 9001, null, body, "api", "client-1", idempotencyKey),
                service.RecommendSmartAssignmentAsync(companyId, 9001, null, body, "api", "client-1", idempotencyKey));
            Assert.All(recommendations, Assert.NotNull);
            Assert.Equal(recommendations[0]!["id"], recommendations[1]!["id"]);
            Assert.Equal("draft", recommendations[0]!["status"]);
            Assert.Equal(78, Convert.ToInt64(recommendations[0]!["createdBy"]));
            Assert.Equal(1m, Convert.ToDecimal(recommendations[0]!["score"]));
            Assert.Equal(0m, Convert.ToDecimal(recommendations[0]!["confidenceScore"]));

            var recommendationId = Convert.ToInt64(recommendations[0]!["id"]);
            var decisions = await Task.WhenAll(
                service.AcceptSmartAssignmentAsync(companyId, recommendationId, new()),
                service.RejectSmartAssignmentAsync(companyId, recommendationId, new() { ["rejectionReason"] = "simulation" }));
            Assert.Single(decisions, result => result.Success);
            Assert.Single(decisions, result => !result.Success);
            Assert.Equal(1, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM assignment_confirmations WHERE company_id=@c AND recommendation_id=@r",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@r", recommendationId); }));

            var site = await service.CreateSiteAccessRequirementAsync(companyId, 9001, null,
                new() { ["status"] = "verified", ["requirementType"] = "gate_pass" });
            Assert.Equal("required", site!["status"]);
            Assert.Null(await service.PatchSiteAccessRequirementAsync(companyId, Convert.ToInt64(site["id"]),
                new() { ["status"] = "invented" }));
            Assert.Equal("verified", (await service.PatchSiteAccessRequirementAsync(companyId, Convert.ToInt64(site["id"]),
                new() { ["status"] = "verified" }))!["status"]);
            Assert.Null(await service.PatchSiteAccessRequirementAsync(companyId, Convert.ToInt64(site["id"]),
                new() { ["status"] = "required" }));

            var pickup = await service.CreatePickupAuthorizationAsync(companyId, 9001, null,
                new() { ["status"] = "verified", ["capturedByUserId"] = 999L }, $"pickup-{Guid.NewGuid():N}");
            Assert.Equal("required", pickup!["status"]);
            Assert.Equal(78, Convert.ToInt64(pickup["capturedByUserId"]));
            Assert.False((await service.UpdatePickupAuthorizationAsync(companyId, Convert.ToInt64(pickup["id"]),
                new() { ["status"] = "invented" })).Success);
            Assert.True((await service.UpdatePickupAuthorizationAsync(companyId, Convert.ToInt64(pickup["id"]),
                new() { ["status"] = "verified" })).Success);
            Assert.False((await service.UpdatePickupAuthorizationAsync(companyId, Convert.ToInt64(pickup["id"]),
                new() { ["status"] = "required" })).Success);

            var handover = await service.CreateWarehouseHandoverAsync(companyId, 9001, null,
                new() { ["status"] = "completed", ["completedAt"] = DateTimeOffset.UtcNow }, $"handover-{Guid.NewGuid():N}");
            Assert.Equal("scheduled", handover!["status"]);
            Assert.Null(handover["completedAt"]);
            Assert.True((await service.UpdateWarehouseHandoverAsync(companyId, Convert.ToInt64(handover["id"]),
                new() { ["status"] = "completed" })).Success);
            Assert.False((await service.UpdateWarehouseHandoverAsync(companyId, Convert.ToInt64(handover["id"]),
                new() { ["status"] = "scheduled" })).Success);
        }
        finally { await CleanupTenantAsync(db, companyId); }
    }

    private static Database CreateDatabase()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = LocalConnectionString,
            })
            .Build();
        return new Database(config);
    }

    private static long NextCompanyId() => Interlocked.Increment(ref _nextCompanyId);

    private static long _nextCompanyId = 64000;

    private static async Task CleanupTenantAsync(Database db, long companyId)
    {
        await db.ExecuteAsync("DELETE FROM ai_action_outcomes WHERE tenant_id=@tenantId", c => c.Parameters.AddWithValue("@tenantId", companyId));
        await db.ExecuteAsync("DELETE FROM ai_action_requests WHERE tenant_id=@tenantId", c => c.Parameters.AddWithValue("@tenantId", companyId));
        await db.ExecuteAsync("DELETE FROM ai_recommendation_impacts WHERE tenant_id=@tenantId", c => c.Parameters.AddWithValue("@tenantId", companyId));
        await db.ExecuteAsync("DELETE FROM ai_recommendation_reasons WHERE tenant_id=@tenantId", c => c.Parameters.AddWithValue("@tenantId", companyId));
        await db.ExecuteAsync("DELETE FROM ai_recommendations WHERE tenant_id=@tenantId", c => c.Parameters.AddWithValue("@tenantId", companyId));
        await db.ExecuteAsync("DELETE FROM ai_reasoning_runs WHERE tenant_id=@tenantId", c => c.Parameters.AddWithValue("@tenantId", companyId));
        await db.ExecuteAsync("DELETE FROM event_processing_logs WHERE tenant_id=@tenantId", c => c.Parameters.AddWithValue("@tenantId", companyId));
        await db.ExecuteAsync("DELETE FROM inbox_messages WHERE tenant_id=@tenantId", c => c.Parameters.AddWithValue("@tenantId", companyId));
        await db.ExecuteAsync("DELETE FROM outbox_messages WHERE tenant_id=@tenantId", c => c.Parameters.AddWithValue("@tenantId", companyId));
        await db.ExecuteAsync("DELETE FROM domain_events WHERE tenant_id=@tenantId", c => c.Parameters.AddWithValue("@tenantId", companyId));
        await db.ExecuteAsync("DELETE FROM approval_decisions WHERE tenant_id=@tenantId", c => c.Parameters.AddWithValue("@tenantId", companyId));
        await db.ExecuteAsync("DELETE FROM approval_requests WHERE tenant_id=@tenantId", c => c.Parameters.AddWithValue("@tenantId", companyId));
        await db.ExecuteAsync("DELETE FROM authorization_decision_logs WHERE tenant_id=@tenantId", c => c.Parameters.AddWithValue("@tenantId", companyId));
        await db.ExecuteAsync("DELETE FROM idempotency_keys WHERE tenant_id=@tenantId", c => c.Parameters.AddWithValue("@tenantId", companyId));

        await db.ExecuteAsync("DELETE FROM billing_confidence_records WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM proof_artifacts WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM proof_packages WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM warehouse_handovers WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM pickup_authorizations WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM access_documents WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM site_access_requirements WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM assignment_confirmations WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM smart_assignment_recommendations WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM documents WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
    }
}
