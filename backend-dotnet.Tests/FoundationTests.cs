using Opstrax.Api.Controllers;
using Opstrax.Api.Foundation;
using Opstrax.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Opstrax.Tests;

public class FoundationTests
{
    [Fact]
    public void RequirePermission_Allows_WhenPermissionExists()
    {
        var http = BuildHttpContext("Tenant Admin", "42", "fleet.manage");

        var result = EndpointMappings.RequirePermission(http, "fleet:manage");

        Assert.Null(result);
        var decision = Assert.IsType<AuthorizationDecisionResult>(http.Items["opstrax.authorization.decision"]);
        Assert.True(decision.IsAllowed);
        Assert.Equal(DecisionStatus.Allowed, decision.Status);
    }

    [Fact]
    public void RequirePermission_Allows_CanonicalBusinessPermission_FromLegacyToken()
    {
        var http = BuildHttpContext("Tenant Admin", "42", "customers:view");

        var result = EndpointMappings.RequirePermission(http, "customer.account.read");

        Assert.Null(result);
        var decision = Assert.IsType<AuthorizationDecisionResult>(http.Items["opstrax.authorization.decision"]);
        Assert.True(decision.IsAllowed);
        Assert.Equal("customer.account.read", decision.Permission);
    }

    [Fact]
    public void RequirePermission_Allows_RevenueReadinessPermission_FromBillingToken()
    {
        var http = BuildHttpContext("Finance/Billing User", "42", "billing:manage");

        var result = EndpointMappings.RequirePermission(http, "finance.job.ready_to_bill");

        Assert.Null(result);
        var decision = Assert.IsType<AuthorizationDecisionResult>(http.Items["opstrax.authorization.decision"]);
        Assert.True(decision.IsAllowed);
        Assert.Equal("finance.job.ready_to_bill", decision.Permission);
    }

    [Fact]
    public void RequirePermission_Allows_ExecutionSummary_FromDispatchToken()
    {
        var http = BuildHttpContext("Dispatcher", "42", "dispatch:view");

        var result = EndpointMappings.RequirePermission(http, "operations.execution_summary.read");

        Assert.Null(result);
        var decision = Assert.IsType<AuthorizationDecisionResult>(http.Items["opstrax.authorization.decision"]);
        Assert.True(decision.IsAllowed);
        Assert.Equal("operations.execution_summary.read", decision.Permission);
    }

    [Fact]
    public void RequirePermission_Denies_WhenPermissionMissing()
    {
        var http = BuildHttpContext("Dispatcher", "42", "dispatch.view");

        var result = EndpointMappings.RequirePermission(http, "fleet:manage");

        Assert.NotNull(result);
        var decision = Assert.IsType<AuthorizationDecisionResult>(http.Items["opstrax.authorization.decision"]);
        Assert.Equal(DecisionStatus.Denied, decision.Status);
        Assert.Contains("missing permission", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RequirePermission_Denies_ExecutionSummary_WhenPermissionMissing()
    {
        var http = BuildHttpContext("Customer", "42", "customer_portal:view");

        var result = EndpointMappings.RequirePermission(http, "operations.execution_summary.read");

        Assert.NotNull(result);
        var decision = Assert.IsType<AuthorizationDecisionResult>(http.Items["opstrax.authorization.decision"]);
        Assert.Equal(DecisionStatus.Denied, decision.Status);
        Assert.Contains("missing permission", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RequirePermission_Denies_VehicleSummary_WhenPermissionMissing()
    {
        var http = BuildHttpContext("Customer", "42", "customer_portal:view");

        var result = EndpointMappings.RequirePermission(http, "vehicles:view");

        Assert.NotNull(result);
        var decision = Assert.IsType<AuthorizationDecisionResult>(http.Items["opstrax.authorization.decision"]);
        Assert.Equal(DecisionStatus.Denied, decision.Status);
        Assert.Contains("missing permission", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RequirePermission_Denies_JobSummary_WhenPermissionMissing()
    {
        var http = BuildHttpContext("Customer", "42", "customer_portal:view");

        var result = EndpointMappings.RequirePermission(http, "shipments:view");

        Assert.NotNull(result);
        var decision = Assert.IsType<AuthorizationDecisionResult>(http.Items["opstrax.authorization.decision"]);
        Assert.Equal(DecisionStatus.Denied, decision.Status);
        Assert.Contains("missing permission", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RequirePermission_Allows_LiveMap_FromDashboardPermission()
    {
        var http = BuildHttpContext("Tenant Admin", "42", "dashboard:view");

        var result = EndpointMappings.RequirePermission(http, "telemetry.live_state.read");

        Assert.Null(result);
        var decision = Assert.IsType<AuthorizationDecisionResult>(http.Items["opstrax.authorization.decision"]);
        Assert.True(decision.IsAllowed);
        Assert.Equal("telemetry.live_state.read", decision.Permission);
    }

    [Fact]
    public void RequirePermission_Denies_WhenTenantContextMissing()
    {
        var http = new DefaultHttpContext();
        http.Items[EndpointMappings.AuthRoleItemKey] = "Tenant Admin";
        http.Items[EndpointMappings.AuthUserIdItemKey] = "42";
        http.Items[EndpointMappings.AuthPermissionsItemKey] = new[] { "fleet.manage" };

        var result = EndpointMappings.RequirePermission(http, "fleet:manage");

        Assert.NotNull(result);
        Assert.False(http.Items.ContainsKey("opstrax.authorization.decision"));
    }

    [Fact]
    public void RequirePermission_RecordsDecisionThroughAuditService()
    {
        var services = new ServiceCollection()
            .AddSingleton<IFeatureAccessService, PassthroughFeatureAccessService>()
            .AddSingleton<IAuthorizationDecisionService, AuthorizationDecisionService>()
            .AddSingleton<IAuditLogService, InMemoryAuditLogService>()
            .AddSingleton<ICorrelationContext>(new InMemoryCorrelationContext("corr-1", "cause-1", "req-1", "1", "tenant_user", "42"))
            .BuildServiceProvider();

        var http = BuildHttpContext("Tenant Admin", "42", "fleet.manage");
        http.RequestServices = services;

        var result = EndpointMappings.RequirePermission(http, "fleet:manage");

        Assert.Null(result);
        var audit = services.GetRequiredService<IAuditLogService>() as InMemoryAuditLogService;
        Assert.NotNull(audit);
        Assert.Single(audit!.Entries);
        Assert.Equal("corr-1", audit.Entries[0].CorrelationId);
        Assert.Equal("req-1", audit.Entries[0].RequestId);
    }

    [Fact]
    public void AuthorizationDecisionService_ReturnsApprovalRequired_WhenPolicyRequestsIt()
    {
        var service = new AuthorizationDecisionService();
        var request = new AuthorizationDecisionRequest(
            new ActorContext("tenant_user", "42", "Tenant Admin", new[] { "dispatch.override" }, "1"),
            PermissionKey.Parse("dispatch:override"),
            new ResourceContext("dispatch_job", "job-7"),
            Policy: new AuthorizationPolicyContext(ApprovalRequired: true, Reason: "high risk change"));

        var decision = service.Decide(request);

        Assert.Equal(DecisionStatus.ApprovalRequired, decision.Status);
        Assert.Equal("high risk change", decision.Reason);
    }

    [Fact]
    public void AuthorizationDecisionService_DeniesCrossTenantRequest()
    {
        var service = new AuthorizationDecisionService();
        var request = new AuthorizationDecisionRequest(
            new ActorContext("tenant_user", "42", "Tenant Admin", new[] { "*" }, "1"),
            PermissionKey.Parse("fleet:manage"),
            new ResourceContext("vehicle", "veh-9", "2"),
            Policy: new AuthorizationPolicyContext(TenantBoundaryAllowed: false));

        var decision = service.Decide(request);

        Assert.Equal(DecisionStatus.Denied, decision.Status);
        Assert.Contains("tenant boundary", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AuthorizationDecisionService_DeniesWhenTenantContextMissing()
    {
        var service = new AuthorizationDecisionService();
        var request = new AuthorizationDecisionRequest(
            new ActorContext("tenant_user", "42", "Tenant Admin", new[] { "*" }, null),
            PermissionKey.Parse("fleet:manage"),
            new ResourceContext("vehicle", "veh-9", "1"));

        var decision = service.Decide(request);

        Assert.Equal(DecisionStatus.Denied, decision.Status);
        Assert.Contains("tenant context", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApprovalWorkflow_AssignsIds_AndTracksDecision()
    {
        var service = new InMemoryApprovalWorkflowService();

        var request = service.CreateRequest("1", "tenant_user", "42", "dispatch.override", "dispatch_job", "job-9", "{\"reason\":\"risk\"}", "high");
        var decision = service.Decide(request.Id, "approver-1", "approved", "looks good");

        var stored = service.GetRequest(request.Id);

        Assert.True(request.Id > 0);
        Assert.True(decision.Id > 0);
        Assert.Equal("approved", stored?.Status);
        Assert.Equal("approved", decision.Decision);
    }

    [Fact]
    public void Idempotency_RejectsConflictingRequestHashes()
    {
        var service = new InMemoryIdempotencyService();

        var first = service.Reserve("1", "dispatch.assign", "idem-123", "hash-a", TimeSpan.FromMinutes(10), "response-1");
        var second = service.Reserve("1", "dispatch.assign", "idem-123", "hash-a", TimeSpan.FromMinutes(10), "response-1");

        Assert.Equal(first.Id, second.Id);
        Assert.Throws<InvalidOperationException>(() =>
            service.Reserve("1", "dispatch.assign", "idem-123", "hash-b", TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void FeatureAccessService_DeniesDisabledOrExpiredSubscriptions()
    {
        var service = new PassthroughFeatureAccessService();

        var disabled = service.Evaluate("1", new FeatureAccessContext(FeatureKey: "ai.actions", Enabled: false));
        var expired = service.Evaluate("1", new FeatureAccessContext(FeatureKey: "ai.actions", SubscriptionStatus: "expired"));
        var missingTenant = service.Evaluate("", new FeatureAccessContext(FeatureKey: "ai.actions"));

        Assert.False(disabled.Allowed);
        Assert.False(expired.Allowed);
        Assert.False(missingTenant.Allowed);
        Assert.Contains("tenant context", missingTenant.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("subscription", expired.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DomainEventPublisher_WritesDomainEventOutboxAndInbox()
    {
        var service = new InMemoryDomainEventPublisher();

        var domainEvent = service.Publish("1", "dispatch.assigned", "dispatch_job", "job-9", "{\"jobId\":\"job-9\"}", "corr-1", "cause-1", "idem-1");
        var outbox = service.Write("1", "dispatch.assigned", "dispatch_job", "job-9", "{\"jobId\":\"job-9\"}");
        var inbox = service.Record("1", "dispatch.assigned", "integration-x", "ext-1", "{\"jobId\":\"job-9\"}");

        Assert.True(domainEvent.Id > 0);
        Assert.True(outbox.Id > 0);
        Assert.True(inbox.Id > 0);
        Assert.Single(service.DomainEvents);
        Assert.True(service.Outbox.Count >= 2);
        Assert.Single(service.Inbox);
        Assert.Single(service.Processing);
    }

    [Fact]
    public void HighRiskActionCatalog_ListsRequiredApprovalActions()
    {
        Assert.True(ApprovalPolicyCatalog.RequiresApproval("finance.invoice.issue"));
        Assert.True(ApprovalPolicyCatalog.RequiresApproval("ai.action.execute_external"));
        Assert.True(ApprovalPolicyCatalog.RequiresApproval("safety.evidence_pack.share_external"));
        Assert.False(ApprovalPolicyCatalog.RequiresApproval("dispatch.view"));
    }

    [Fact]
    public void AiFoundation_StructuresReasoningRunAndActionRequestWithoutExecutingOutcome()
    {
        var service = new InMemoryAiFoundationService();

        var run = service.StartReasoningRun("1", "alert", "{\"signal\":true}", "template-a", "{\"schema\":true}", "corr-9", "cause-9");
        var completed = service.CompleteReasoningRun(run, "{\"recommendation\":true}", 0.91m);
        var recommendation = service.CreateRecommendation("1", "maintenance", "Service truck 12", "Truck 12 needs maintenance review", 0.87m, 0.75m, "{\"downtime\":\"medium\"}", "{\"event\":\"engine_temp\"}", "{\"action\":\"create_work_order\"}", "high", "evt-22", "tenant_user", "42");
        var actionRequest = service.CreateActionRequest("1", recommendation.Id, "maintenance.create_work_order", "vehicle", "12", "{\"notes\":\"review\"}", "high", "tenant_user", "42", requiresApproval: true);

        Assert.True(run.Id > 0);
        Assert.Equal("completed", completed.Status);
        Assert.True(recommendation.Id > 0);
        Assert.Equal("approval_required", actionRequest.Status);
        Assert.Empty(service.Outcomes);
        Assert.Single(service.Runs);
        Assert.Single(service.Recommendations);
        Assert.Single(service.ActionRequests);
        var outcome = service.RecordOutcome("1", actionRequest.Id, "approved", "{\"workOrderId\":501}");
        Assert.True(outcome.Id > 0);
        Assert.Equal("approved", outcome.Status);
        Assert.Single(service.Outcomes);
    }

    [Fact]
    public void GetCompanyId_ThrowsWhenTenantContextMissing()
    {
        var http = new DefaultHttpContext();

        Assert.Throws<InvalidOperationException>(() => EndpointMappings.GetCompanyId(http));
    }

    private static DefaultHttpContext BuildHttpContext(string role, string userId, params string[] permissions)
    {
        var http = new DefaultHttpContext();
        http.Items[EndpointMappings.AuthRoleItemKey] = role;
        http.Items[EndpointMappings.AuthUserIdItemKey] = userId;
        http.Items[EndpointMappings.AuthPermissionsItemKey] = permissions;
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = 1L;
        return http;
    }
}
