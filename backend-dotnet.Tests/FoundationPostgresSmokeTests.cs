using Microsoft.Extensions.Configuration;
using Opstrax.Api.Data;
using Opstrax.Api.Foundation;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

public class FoundationPostgresSmokeTests
{
    private static readonly string LocalConnectionString = TestDb.ConnectionString;

    [Fact]
    public async Task Foundation_EndToEnd_Smoke_Persists_All_Core_Records()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = LocalConnectionString,
            })
            .Build();
        var db = new Database(config);
        var tenantId = "51001";
        var tenantIdLong = long.Parse(tenantId);
        var correlationId = $"stage5c-{Guid.NewGuid():N}";
        var requestId = $"req-{Guid.NewGuid():N}";
        var correlation = new InMemoryCorrelationContext(correlationId, $"cause-{Guid.NewGuid():N}", requestId, tenantId, ActorTypes.TenantUser, "42");
        var authorization = new AuthorizationDecisionService();
        var audit = new PostgresAuditLogService(db);
        var approval = new PostgresApprovalWorkflowService(db, correlation);
        var events = new PostgresDomainEventPublisher(db, correlation);
        var idempotency = new PostgresIdempotencyService(db);
        var ai = new PostgresAiFoundationService(db, correlation);

        var createdIds = new Dictionary<string, long>();

        try
        {
            await VerifyTablesExistAsync(db);

            var decision = authorization.Decide(new AuthorizationDecisionRequest(
                new ActorContext(ActorTypes.TenantUser, "42", "Tenant Admin", new[] { "fleet.manage", "finance.invoice.issue" }, tenantId),
                PermissionKey.Parse("fleet:manage"),
                new ResourceContext("api_endpoint", "fleet:manage", tenantId, "42"),
                null,
                null,
                new AuthorizationPolicyContext(),
                correlationId,
                requestId));

            Assert.True(decision.IsAllowed);
            var authLog = audit.RecordAuthorizationDecision(decision, tenantId);
            Assert.Equal(tenantId, authLog.TenantId);

            var approvalRequest = approval.CreateRequest(
                tenantId,
                ActorTypes.TenantUser,
                "42",
                "finance.invoice.issue",
                "invoice",
                "inv-smoke-1001",
                "{\"source\":\"smoke\"}",
                "high");
            createdIds["approval_request"] = approvalRequest.Id;

            var approvalDecision = approval.Decide(approvalRequest.Id, "approver-smoke-1", "approved", "local smoke approval");
            createdIds["approval_decision"] = approvalDecision.Id;

            var idempotencyRecord = idempotency.Reserve(
                tenantId,
                "dispatch.assign",
                "smoke-idem-key",
                "request-hash-a",
                TimeSpan.FromMinutes(10),
                "response-ref-1");
            Assert.Equal("reserved", idempotencyRecord.Status);
            Assert.Equal(idempotencyRecord.Id, idempotency.Reserve(tenantId, "dispatch.assign", "smoke-idem-key", "request-hash-a", TimeSpan.FromMinutes(10)).Id);
            Assert.Throws<InvalidOperationException>(() =>
                idempotency.Reserve(tenantId, "dispatch.assign", "smoke-idem-key", "request-hash-b", TimeSpan.FromMinutes(10)));

            var domainEvent = events.Publish(
                tenantId,
                "foundation.smoke.event",
                "smoke_aggregate",
                "agg-1",
                "{\"ok\":true}",
                correlationId,
                correlation.CausationId,
                "smoke-idem-key");
            createdIds["domain_event"] = domainEvent.Id;

            var outbox = events.Write(
                tenantId,
                "foundation.smoke.event",
                "smoke_aggregate",
                "agg-1",
                "{\"ok\":true}",
                correlationId,
                correlation.CausationId,
                "smoke-idem-key");
            createdIds["outbox"] = outbox.Id;

            var inbox = events.Record(
                tenantId,
                "foundation.smoke.event",
                "smoke-integration",
                "ext-1",
                "{\"ok\":true}",
                correlationId,
                correlation.CausationId);
            createdIds["inbox"] = inbox.Id;

            var reasoningRun = ai.StartReasoningRun(
                tenantId,
                "smoke",
                "{\"input\":true}",
                "template-smoke",
                "{\"schema\":true}",
                correlationId,
                correlation.CausationId);
            createdIds["ai_run"] = reasoningRun.Id;

            var completedRun = ai.CompleteReasoningRun(reasoningRun, "{\"recommendation\":true}", 0.94m);
            Assert.Equal("completed", completedRun.Status);

            var recommendation = ai.CreateRecommendation(
                tenantId,
                "smoke",
                "Investigate smoke tenant",
                "Smoke recommendation for Stage 5C",
                0.91m,
                0.84m,
                "{\"impact\":\"low\"}",
                "{\"reason\":\"smoke\"}",
                "{\"action\":\"request_approval\"}",
                "high",
                domainEvent.Id.ToString(),
                ActorTypes.AiAgent,
                "ai-smoke");
            createdIds["ai_recommendation"] = recommendation.Id;

            var actionRequest = ai.CreateActionRequest(
                tenantId,
                recommendation.Id,
                "ai.action.execute_external",
                "smoke_resource",
                "res-1",
                "{\"payload\":true}",
                "high",
                ActorTypes.AiAgent,
                "ai-smoke",
                requiresApproval: true);
            createdIds["ai_action_request"] = actionRequest.Id;
            Assert.Equal("approval_required", actionRequest.Status);

            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM authorization_decision_logs WHERE tenant_id=@tenantId AND correlation_id=@corr", c =>
            {
                c.Parameters.AddWithValue("@tenantId", tenantIdLong);
                c.Parameters.AddWithValue("@corr", correlationId);
            }));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM approval_requests WHERE tenant_id=@tenantId AND action_key='finance.invoice.issue'", c => c.Parameters.AddWithValue("@tenantId", tenantIdLong)));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM approval_decisions WHERE tenant_id=@tenantId AND approval_request_id=@id", c =>
            {
                c.Parameters.AddWithValue("@tenantId", tenantIdLong);
                c.Parameters.AddWithValue("@id", approvalRequest.Id);
            }));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM idempotency_keys WHERE tenant_id=@tenantId AND operation='dispatch.assign' AND idempotency_key='smoke-idem-key'", c => c.Parameters.AddWithValue("@tenantId", tenantIdLong)));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM domain_events WHERE tenant_id=@tenantId AND correlation_id=@corr", c =>
            {
                c.Parameters.AddWithValue("@tenantId", tenantIdLong);
                c.Parameters.AddWithValue("@corr", correlationId);
            }));
            Assert.Equal(2, await db.ScalarLongAsync("SELECT COUNT(*) FROM outbox_messages WHERE tenant_id=@tenantId AND correlation_id=@corr", c =>
            {
                c.Parameters.AddWithValue("@tenantId", tenantIdLong);
                c.Parameters.AddWithValue("@corr", correlationId);
            }));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM inbox_messages WHERE tenant_id=@tenantId AND correlation_id=@corr", c =>
            {
                c.Parameters.AddWithValue("@tenantId", tenantIdLong);
                c.Parameters.AddWithValue("@corr", correlationId);
            }));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM event_processing_logs WHERE tenant_id=@tenantId AND correlation_id=@corr", c =>
            {
                c.Parameters.AddWithValue("@tenantId", tenantIdLong);
                c.Parameters.AddWithValue("@corr", correlationId);
            }));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM ai_reasoning_runs WHERE tenant_id=@tenantId AND correlation_id=@corr", c =>
            {
                c.Parameters.AddWithValue("@tenantId", tenantIdLong);
                c.Parameters.AddWithValue("@corr", correlationId);
            }));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM ai_recommendations WHERE tenant_id=@tenantId AND correlation_id=@corr", c =>
            {
                c.Parameters.AddWithValue("@tenantId", tenantIdLong);
                c.Parameters.AddWithValue("@corr", correlationId);
            }));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM ai_action_requests WHERE tenant_id=@tenantId AND correlation_id=@corr", c =>
            {
                c.Parameters.AddWithValue("@tenantId", tenantIdLong);
                c.Parameters.AddWithValue("@corr", correlationId);
            }));
            Assert.Equal(0, await db.ScalarLongAsync("SELECT COUNT(*) FROM ai_action_outcomes WHERE tenant_id=@tenantId AND action_request_id=@id", c =>
            {
                c.Parameters.AddWithValue("@tenantId", tenantIdLong);
                c.Parameters.AddWithValue("@id", actionRequest.Id);
            }));
            Assert.True(ApprovalPolicyCatalog.RequiresApproval("ai.action.execute_external"));
        }
        finally
        {
            await CleanupTenantAsync(db, tenantIdLong);
        }
    }

    private static async Task VerifyTablesExistAsync(Database db)
    {
        var expected = new[]
        {
            "authorization_decision_logs",
            "approval_policies",
            "approval_requests",
            "approval_decisions",
            "idempotency_keys",
            "domain_events",
            "outbox_messages",
            "inbox_messages",
            "event_processing_logs",
            "ai_reasoning_runs",
            "ai_recommendations",
            "ai_recommendation_reasons",
            "ai_recommendation_impacts",
            "ai_action_requests",
            "ai_action_outcomes",
        };

        foreach (var table in expected)
        {
            var exists = await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='public' AND table_name=@table",
                c => c.Parameters.AddWithValue("@table", table));
            Assert.Equal(1, exists);
        }
    }

    private static async Task CleanupTenantAsync(Database db, long tenantId)
    {
        await db.ExecuteAsync("DELETE FROM ai_action_outcomes WHERE tenant_id=@tenantId", c => c.Parameters.AddWithValue("@tenantId", tenantId));
        await db.ExecuteAsync("DELETE FROM ai_action_requests WHERE tenant_id=@tenantId", c => c.Parameters.AddWithValue("@tenantId", tenantId));
        await db.ExecuteAsync("DELETE FROM ai_recommendation_impacts WHERE tenant_id=@tenantId", c => c.Parameters.AddWithValue("@tenantId", tenantId));
        await db.ExecuteAsync("DELETE FROM ai_recommendation_reasons WHERE tenant_id=@tenantId", c => c.Parameters.AddWithValue("@tenantId", tenantId));
        await db.ExecuteAsync("DELETE FROM ai_recommendations WHERE tenant_id=@tenantId", c => c.Parameters.AddWithValue("@tenantId", tenantId));
        await db.ExecuteAsync("DELETE FROM ai_reasoning_runs WHERE tenant_id=@tenantId", c => c.Parameters.AddWithValue("@tenantId", tenantId));
        await db.ExecuteAsync("DELETE FROM event_processing_logs WHERE tenant_id=@tenantId", c => c.Parameters.AddWithValue("@tenantId", tenantId));
        await db.ExecuteAsync("DELETE FROM inbox_messages WHERE tenant_id=@tenantId", c => c.Parameters.AddWithValue("@tenantId", tenantId));
        await db.ExecuteAsync("DELETE FROM outbox_messages WHERE tenant_id=@tenantId", c => c.Parameters.AddWithValue("@tenantId", tenantId));
        await db.ExecuteAsync("DELETE FROM domain_events WHERE tenant_id=@tenantId", c => c.Parameters.AddWithValue("@tenantId", tenantId));
        await db.ExecuteAsync("DELETE FROM approval_decisions WHERE tenant_id=@tenantId", c => c.Parameters.AddWithValue("@tenantId", tenantId));
        await db.ExecuteAsync("DELETE FROM approval_requests WHERE tenant_id=@tenantId", c => c.Parameters.AddWithValue("@tenantId", tenantId));
        await db.ExecuteAsync("DELETE FROM authorization_decision_logs WHERE tenant_id=@tenantId", c => c.Parameters.AddWithValue("@tenantId", tenantId));
        await db.ExecuteAsync("DELETE FROM idempotency_keys WHERE tenant_id=@tenantId", c => c.Parameters.AddWithValue("@tenantId", tenantId));
    }
}
