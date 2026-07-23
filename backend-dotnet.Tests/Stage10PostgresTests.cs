using Microsoft.Extensions.Configuration;
using Opstrax.Api.Data;
using Opstrax.Api.Foundation;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

public class Stage10PostgresTests
{
    private static readonly string LocalConnectionString = TestDb.ConnectionString;

    [Fact]
    public async Task ExecutionSummary_Collects_All_Workflow_Sections_And_DoesNotMutate_Data()
    {
        var db = CreateDatabase();
        var schema = new Stage9SchemaService(db);
        var companyId = NextCompanyId();
        var ambient = new AmbientCorrelationContext();
        var ai = new PostgresAiFoundationService(db, ambient);
        var approval = new PostgresApprovalWorkflowService(db, ambient);
        var service = new Stage9OperationalFoundationService(db, ai, approval, new PostgresDomainEventPublisher(db, ambient), new InMemoryIdempotencyService(), ambient);
        var jobId = 7201L;
        var tripId = 7301L;

        try
        {
            await schema.EnsureAsync();
            using var scope = AmbientCorrelationContext.Begin($"stage10-{Guid.NewGuid():N}", $"cause-{Guid.NewGuid():N}", $"req-{Guid.NewGuid():N}", companyId.ToString(), ActorTypes.TenantUser, "42");

            var recommendation = await service.RecommendSmartAssignmentAsync(
                companyId,
                jobId,
                tripId,
                new Dictionary<string, object?>
                {
                    ["recommendedDriverId"] = 101,
                    ["recommendedVehicleId"] = 202,
                    ["score"] = 0.84m,
                    ["confidenceScore"] = 0.79m,
                    ["riskLevel"] = "medium",
                    ["sourceChannel"] = "web",
                    ["clientGeneratedId"] = "stage10-reco-1",
                },
                "web",
                "stage10-reco-client",
                "stage10-reco-idem");

            Assert.NotNull(recommendation);

            var siteAccess = await service.CreateSiteAccessRequirementAsync(companyId, jobId, tripId, new Dictionary<string, object?>
            {
                ["requirementType"] = "gate_pass",
                ["instructions"] = "Verify gate pass at security desk",
                ["contactName"] = "Site Desk",
                ["contactPhone"] = "555-0101",
            });
            Assert.NotNull(siteAccess);

            var accessDocument = await service.CreateAccessDocumentAsync(companyId, jobId, tripId, new Dictionary<string, object?>
            {
                ["documentType"] = "noc",
                ["documentNo"] = "NOC-2001",
                ["status"] = "required",
            }, "stage10-access-idem");
            Assert.NotNull(accessDocument);

            var pickupAuthorization = await service.CreatePickupAuthorizationAsync(companyId, jobId, tripId, new Dictionary<string, object?>
            {
                ["authorizationNo"] = "PU-1001",
                ["thirdPartyName"] = "Alpha Logistics",
                ["authorizedPersonName"] = "Jane Receiver",
                ["status"] = "required",
            }, "stage10-pickup-idem");
            Assert.NotNull(pickupAuthorization);

            var handover = await service.CreateWarehouseHandoverAsync(companyId, jobId, tripId, new Dictionary<string, object?>
            {
                ["handoverType"] = "warehouse_handover",
                ["warehouseName"] = "Main Warehouse",
                ["warehouseReferenceNo"] = "WH-2001",
                ["status"] = "scheduled",
            }, "stage10-handover-idem");
            Assert.NotNull(handover);

            var proofPackage = await service.CreateProofPackageAsync(companyId, jobId, tripId, new Dictionary<string, object?>
            {
                ["proofType"] = "proof_of_delivery",
                ["receiverName"] = "Receiver One",
                ["receiverPhone"] = "555-0102",
                ["status"] = "draft",
            }, "stage10-proof-idem");
            Assert.NotNull(proofPackage);

            var proofArtifact = await service.CreateProofArtifactAsync(companyId, Convert.ToInt64(proofPackage!["id"]), new Dictionary<string, object?>
            {
                ["artifactType"] = "photo",
                ["capturedByUserId"] = 42,
                ["notes"] = "Field photo",
                ["deviceId"] = "device-1",
            }, "stage10-artifact-idem");
            Assert.NotNull(proofArtifact);

            await service.UpdateAccessDocumentStatusAsync(companyId, Convert.ToInt64(accessDocument!["id"]), new Dictionary<string, object?>
            {
                ["status"] = "verified",
            });

            await service.PatchSiteAccessRequirementAsync(companyId, Convert.ToInt64(siteAccess["id"]), new Dictionary<string, object?>
            {
                ["status"] = "verified",
            });

            await service.UpdatePickupAuthorizationAsync(companyId, Convert.ToInt64(pickupAuthorization!["id"]), new Dictionary<string, object?>
            {
                ["status"] = "verified",
            });

            await service.UpdateWarehouseHandoverAsync(companyId, Convert.ToInt64(handover!["id"]), new Dictionary<string, object?>
            {
                ["status"] = "completed",
            });

            var submit = await service.SubmitProofPackageAsync(companyId, Convert.ToInt64(proofPackage["id"]), new Dictionary<string, object?>
            {
                ["exceptionNote"] = "All evidence captured",
            });

            Assert.True(submit.Success);

            var validate = await service.ValidateProofPackageAsync(companyId, Convert.ToInt64(proofPackage["id"]), new Dictionary<string, object?>());
            Assert.True(validate.Success);
            Assert.Equal("passed", validate.ValidationStatus);

            var beforeCounts = await CaptureCountsAsync(db, companyId);
            var summary = await service.GetExecutionSummaryAsync(companyId, jobId);
            var afterCounts = await CaptureCountsAsync(db, companyId);

            Assert.NotNull(summary);
            Assert.Equal(jobId, Convert.ToInt64(summary!["job_id"]));
            Assert.Equal(tripId, Convert.ToInt64(summary["trip_id"]));
            var mobileReadyActions = summary["mobile_ready_actions"] as IEnumerable<object>;

            var assignmentSummary = GetSection(summary, "smart_assignment_summary");
            var siteAccessSummary = GetSection(summary, "site_access_summary");
            var accessDocumentSummary = GetSection(summary, "access_document_summary");
            var pickupSummary = GetSection(summary, "pickup_authorization_summary");
            var warehouseSummary = GetSection(summary, "warehouse_handover_summary");
            var proofSummary = GetSection(summary, "proof_package_summary");
            var artifactSummary = GetSection(summary, "proof_artifact_summary");
            var billingSummary = GetSection(summary, "billing_confidence_summary");
            var riskSummary = GetSection(summary, "risk_summary");

            Assert.Equal("draft", assignmentSummary["status"]?.ToString());
            Assert.Equal("closed", siteAccessSummary["status"]?.ToString());
            Assert.Equal("closed", accessDocumentSummary["status"]?.ToString());
            Assert.Equal("verified", pickupSummary["status"]?.ToString());
            Assert.Equal("completed", warehouseSummary["status"]?.ToString());
            Assert.Equal("validated", proofSummary["status"]?.ToString());
            Assert.Equal("attached", artifactSummary["status"]?.ToString());
            Assert.Equal("ready", billingSummary["status"]?.ToString());
            Assert.Equal("confidence_ready", riskSummary["status"]?.ToString());
            Assert.NotNull(summary["next_best_actions"]);
            Assert.NotEmpty(mobileReadyActions ?? Array.Empty<object>());

            Assert.Equal(beforeCounts["site_access"], afterCounts["site_access"]);
            Assert.Equal(beforeCounts["access_documents"], afterCounts["access_documents"]);
            Assert.Equal(beforeCounts["pickup_authorizations"], afterCounts["pickup_authorizations"]);
            Assert.Equal(beforeCounts["warehouse_handovers"], afterCounts["warehouse_handovers"]);
            Assert.Equal(beforeCounts["smart_assignments"], afterCounts["smart_assignments"]);
            Assert.Equal(beforeCounts["proof_packages"], afterCounts["proof_packages"]);
            Assert.Equal(beforeCounts["proof_artifacts"], afterCounts["proof_artifacts"]);
            Assert.Equal(beforeCounts["billing_confidence"], afterCounts["billing_confidence"]);
        }
        finally
        {
            await CleanupTenantAsync(db, companyId);
        }
    }

    [Fact]
    public async Task ExecutionSummary_Is_TenantScoped_And_Does_Not_Return_CrossTenant_Data()
    {
        var db = CreateDatabase();
        var schema = new Stage9SchemaService(db);
        var tenantA = NextCompanyId();
        var tenantB = NextCompanyId();
        var ambient = new AmbientCorrelationContext();
        var ai = new PostgresAiFoundationService(db, ambient);
        var approval = new PostgresApprovalWorkflowService(db, ambient);
        var service = new Stage9OperationalFoundationService(db, ai, approval, new PostgresDomainEventPublisher(db, ambient), new InMemoryIdempotencyService(), ambient);

        try
        {
            await schema.EnsureAsync();
            using var scope = AmbientCorrelationContext.Begin($"stage10-cross-{Guid.NewGuid():N}", $"cause-{Guid.NewGuid():N}", $"req-{Guid.NewGuid():N}", tenantA.ToString(), ActorTypes.TenantUser, "42");

            var created = await service.CreateSiteAccessRequirementAsync(tenantA, 9101, 9201, new Dictionary<string, object?>
            {
                ["requirementType"] = "gate_pass",
                ["instructions"] = "Tenant A data only",
            });
            Assert.NotNull(created);

            var summary = await service.GetExecutionSummaryAsync(tenantB, 9101);
            Assert.NotNull(summary);
            Assert.Equal("no_data", ((Dictionary<string, object?>)summary!["site_access_summary"])["status"]?.ToString());
            Assert.Equal(0L, Convert.ToInt64(((Dictionary<string, object?>)summary["site_access_summary"])["count"]));
        }
        finally
        {
            await CleanupTenantAsync(db, tenantA);
            await CleanupTenantAsync(db, tenantB);
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

    private static long _nextCompanyId = 66000;

    private static Dictionary<string, object?> GetSection(Dictionary<string, object?> summary, string key)
        => (Dictionary<string, object?>)summary[key]!;

    private static async Task<Dictionary<string, long>> CaptureCountsAsync(Database db, long companyId)
    {
        var counts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
        {
            ["site_access"] = await db.ScalarLongAsync("SELECT COUNT(*) FROM site_access_requirements WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId)),
            ["access_documents"] = await db.ScalarLongAsync("SELECT COUNT(*) FROM access_documents WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId)),
            ["pickup_authorizations"] = await db.ScalarLongAsync("SELECT COUNT(*) FROM pickup_authorizations WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId)),
            ["warehouse_handovers"] = await db.ScalarLongAsync("SELECT COUNT(*) FROM warehouse_handovers WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId)),
            ["smart_assignments"] = await db.ScalarLongAsync("SELECT COUNT(*) FROM smart_assignment_recommendations WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId)),
            ["proof_packages"] = await db.ScalarLongAsync("SELECT COUNT(*) FROM proof_packages WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId)),
            ["proof_artifacts"] = await db.ScalarLongAsync("SELECT COUNT(*) FROM proof_artifacts WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId)),
            ["billing_confidence"] = await db.ScalarLongAsync("SELECT COUNT(*) FROM billing_confidence_records WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId)),
        };

        return counts;
    }

    private static async Task CleanupTenantAsync(Database db, long companyId)
    {
        await db.ExecuteAsync("DELETE FROM billing_confidence_records WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM proof_artifacts WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM proof_packages WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM warehouse_handovers WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM pickup_authorizations WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM access_documents WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM site_access_requirements WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM assignment_confirmations WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM smart_assignment_recommendations WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
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
    }
}
