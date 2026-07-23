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
    }
}
