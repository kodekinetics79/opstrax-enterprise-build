using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Opstrax.Api.Foundation;

public sealed class PassthroughFeatureAccessService : IFeatureAccessService
{
    public FeatureAccessResult Evaluate(string tenantId, FeatureAccessContext? context)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            return new(false, "Missing tenant context", context?.FeatureKey, context?.SubscriptionStatus, context?.BillingStatus);

        var effective = context ?? new FeatureAccessContext();
        if (!effective.Enabled)
            return new(false, "Feature disabled", effective.FeatureKey, effective.SubscriptionStatus, effective.BillingStatus, effective.UsageLimit is null || effective.UsageUsed is null || effective.UsageUsed <= effective.UsageLimit);

        if (!string.IsNullOrWhiteSpace(effective.SubscriptionStatus) &&
            !string.Equals(effective.SubscriptionStatus, "active", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(effective.SubscriptionStatus, "trial", StringComparison.OrdinalIgnoreCase))
        {
            return new(false, $"Subscription status is {effective.SubscriptionStatus}", effective.FeatureKey, effective.SubscriptionStatus, effective.BillingStatus);
        }

        if (!string.IsNullOrWhiteSpace(effective.BillingStatus) &&
            !string.Equals(effective.BillingStatus, "current", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(effective.BillingStatus, "paid", StringComparison.OrdinalIgnoreCase))
        {
            return new(false, $"Billing status is {effective.BillingStatus}", effective.FeatureKey, effective.SubscriptionStatus, effective.BillingStatus);
        }

        var withinLimit = !effective.UsageLimit.HasValue || !effective.UsageUsed.HasValue || effective.UsageUsed <= effective.UsageLimit;
        if (!withinLimit)
            return new(false, "Usage limit exceeded", effective.FeatureKey, effective.SubscriptionStatus, effective.BillingStatus, false);

        return new(true, "Feature access allowed", effective.FeatureKey, effective.SubscriptionStatus, effective.BillingStatus, withinLimit);
    }
}

public sealed class AuthorizationDecisionService(IFeatureAccessService? featureAccess = null) : IAuthorizationDecisionService
{
    private readonly IFeatureAccessService _featureAccess = featureAccess ?? new PassthroughFeatureAccessService();

    public AuthorizationDecisionResult Decide(AuthorizationDecisionRequest request)
    {
        var permission = PermissionKey.Normalize(request.Permission.Value);
        var policy = request.Policy ?? new AuthorizationPolicyContext();
        var feature = request.Feature;
        var actorPermissions = request.Actor.Permissions?.Select(PermissionKey.Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(request.Actor.ActorType) ||
            string.IsNullOrWhiteSpace(request.Actor.TenantId))
        {
            return Denied(request, permission, "Missing actor or tenant context");
        }

        if (!policy.TenantBoundaryAllowed)
            return Denied(request, permission, "Tenant boundary violation");

        if (policy.DenyOverride)
            return Denied(request, permission, "Deny override applied");

        if (!PermissionAllowed(actorPermissions, permission))
            return Denied(request, permission, $"Missing permission: {permission}");

        if (feature is not null)
        {
            var featureAccess = _featureAccess.Evaluate(request.Actor.TenantId ?? request.Resource.OwnerTenantId ?? "unknown", feature);
            if (!featureAccess.Allowed)
                return Denied(request, permission, featureAccess.Reason);
        }

        if (!policy.FeatureEnabled)
            return Denied(request, permission, "Feature disabled");

        if (!policy.UsageWithinLimit)
            return Denied(request, permission, "Usage limit exceeded");

        if (!policy.ResourceOwnershipAllowed)
            return Denied(request, permission, "Resource ownership not permitted");

        if (policy.ApprovalRequired)
            return new AuthorizationDecisionResult(
                DecisionStatus.ApprovalRequired,
                permission,
                policy.Reason ?? "Approval required",
                request.Actor,
                request.Resource,
                request.Scope,
                feature,
                policy,
                request.CorrelationId,
                request.RequestId);

        return new AuthorizationDecisionResult(
            DecisionStatus.Allowed,
            permission,
            policy.Reason ?? "Allowed",
            request.Actor,
            request.Resource,
            request.Scope,
            feature,
            policy,
            request.CorrelationId,
            request.RequestId);
    }

    private static bool PermissionAllowed(IReadOnlyCollection<string> permissions, string permission)
    {
        if (permissions.Count == 0) return false;
        if (permissions.Any(p => string.Equals(p, "*", StringComparison.OrdinalIgnoreCase))) return true;
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            permission,
            permission.Replace('.', ':'),
            permission.Replace(':', '.'),
        };

        foreach (var alias in SemanticPermissionAliases(permission))
        {
            aliases.Add(alias);
            aliases.Add(alias.Replace('.', ':'));
            aliases.Add(alias.Replace(':', '.'));
        }

        return permissions.Any(aliases.Contains);
    }

    private static IEnumerable<string> SemanticPermissionAliases(string permission)
    {
        if (permission is "customer.account.read" or "customer.account.view" or "customers:view" or "crm:view")
            return ["customer.account.read", "customer.account.view", "customers:view", "crm:view", "customer.contact.read", "customer.address.read"];

        if (permission is "customer.account.create" or "customer.account.update" or "customer.account.manage" or "customers:create" or "customers:update" or "customers:manage" or "crm:manage")
            return ["customer.account.create", "customer.account.update", "customer.account.manage", "customers:create", "customers:update", "customers:manage", "crm:manage", "customer.contact.create", "customer.contact.update", "customer.contact.manage", "customer.address.create", "customer.address.update", "customer.address.manage"];

        if (permission is "customer.contact.read" or "customer.contact.create" or "customer.contact.update" or "customer.contact.manage")
            return ["customer.contact.read", "customer.contact.create", "customer.contact.update", "customer.contact.manage", "customer.account.manage", "customers:manage"];

        if (permission is "customer.address.read" or "customer.address.create" or "customer.address.update" or "customer.address.manage")
            return ["customer.address.read", "customer.address.create", "customer.address.update", "customer.address.manage", "customer.account.manage", "customers:manage"];

        if (permission is "contract.read" or "contract.view" or "contracts:view" or "finance:view")
            return ["contract.read", "contract.view", "contracts:view", "finance:view", "rate_card.read"];

        if (permission is "contract.create" or "contract.update" or "contract.manage" or "contracts:create" or "contracts:update" or "contracts:manage" or "finance:manage")
            return ["contract.create", "contract.update", "contract.manage", "contracts:create", "contracts:update", "contracts:manage", "finance:manage", "rate_card.create", "rate_card.update", "rate_card.manage", "charge.create", "charge.update", "charge.manage"];

        if (permission is "rate_card.read" or "rate_card.view" or "rate-card.read" or "contracts-rates" or "contracts:rates")
            return ["rate_card.read", "rate_card.view", "rate-card.read", "contracts-rates", "contracts:rates"];

        if (permission is "rate_card.create" or "rate_card.update" or "rate_card.manage" or "rate-card.create" or "rate-card.update" or "rate-card.manage" or "finance:manage")
            return ["rate_card.create", "rate_card.update", "rate_card.manage", "rate-card.create", "rate-card.update", "rate-card.manage", "finance:manage"];

        if (permission is "job.read" or "job.view" or "jobs:view" or "dispatch:view")
            return ["job.read", "job.view", "jobs:view", "dispatch:view", "trip.read"];

        if (permission is "job.create" or "job.update" or "job.manage" or "jobs:create" or "jobs:update" or "jobs:manage" or "dispatch:manage")
            return ["job.create", "job.update", "job.manage", "jobs:create", "jobs:update", "jobs:manage", "dispatch:manage", "trip.create", "trip.update", "trip.manage"];

        if (permission is "trip.read" or "trip.view" or "trips:view")
            return ["trip.read", "trip.view", "trips:view", "dispatch:view", "job.read"];

        if (permission is "trip.create" or "trip.update" or "trip.manage" or "trips:create" or "trips:update" or "trips:manage" or "dispatch:manage")
            return ["trip.create", "trip.update", "trip.manage", "trips:create", "trips:update", "trips:manage", "dispatch:manage", "job.create", "job.update", "job.manage"];

        if (permission is "dispatch.smart_assign.read" or "dispatch.smart_assign.recommend" or "dispatch.smart_assign.accept" or "dispatch.smart_assign.reject")
            return ["dispatch.smart_assign.read", "dispatch.smart_assign.recommend", "dispatch.smart_assign.accept", "dispatch.smart_assign.reject", "dispatch:view", "dispatch:manage", "dispatch:assign"];

        if (permission is "operations.execution_summary.read")
            return ["operations.execution_summary.read", "dispatch:view", "dispatch:manage", "shipments:view", "fleet:view", "driver:self"];

        if (permission is "telemetry.live_state.read" or "telemetry.live-state.read")
            return ["telemetry.live_state.read", "telemetry.live-state.read", "telemetry.alerts.read", "telemetry.alerts.view", "telemetry.rules.read", "telemetry.rules.view", "dashboard:view", "dashboard.view", "map:view", "map.view", "fleet:view", "fleet.view", "telematics:gps:view", "telematics.gps.view"];

        if (permission is "operations.site_access.read" or "operations.site_access.create" or "operations.site_access.update")
            return ["operations.site_access.read", "operations.site_access.create", "operations.site_access.update", "dispatch:view", "dispatch:manage", "job.update"];

        if (permission is "operations.access_document.read" or "operations.access_document.create" or "operations.access_document.update" or "operations.access_document.verify")
            return ["operations.access_document.read", "operations.access_document.create", "operations.access_document.update", "operations.access_document.verify", "dispatch:view", "dispatch:manage", "driver:self"];

        if (permission is "operations.pickup_authorization.read" or "operations.pickup_authorization.create" or "operations.pickup_authorization.update" or "operations.pickup_authorization.verify")
            return ["operations.pickup_authorization.read", "operations.pickup_authorization.create", "operations.pickup_authorization.update", "operations.pickup_authorization.verify", "dispatch:view", "dispatch:manage", "driver:self"];

        if (permission is "operations.warehouse_handover.read" or "operations.warehouse_handover.create" or "operations.warehouse_handover.update")
            return ["operations.warehouse_handover.read", "operations.warehouse_handover.create", "operations.warehouse_handover.update", "dispatch:view", "dispatch:manage", "driver:self"];

        if (permission is "operations.proof.read" or "operations.proof.create" or "operations.proof.update" or "operations.proof.submit" or "operations.proof.validate")
            return ["operations.proof.read", "operations.proof.create", "operations.proof.update", "operations.proof.submit", "operations.proof.validate", "dispatch:view", "dispatch:manage", "driver:self", "customer_portal:view"];

        if (permission is "operations.proof_artifact.read" or "operations.proof_artifact.create")
            return ["operations.proof_artifact.read", "operations.proof_artifact.create", "operations.proof.read", "operations.proof.create", "dispatch:view", "dispatch:manage", "driver:self", "customer_portal:view"];

        if (permission is "charge.read" or "charge.view" or "charges:view" or "finance:view")
            return ["charge.read", "charge.view", "charges:view", "finance:view"];

        if (permission is "charge.create" or "charge.update" or "charge.manage" or "charges:create" or "charges:update" or "charges:manage" or "finance:manage")
            return ["charge.create", "charge.update", "charge.manage", "charges:create", "charges:update", "charges:manage", "finance:manage"];

        if (permission is "finance.job.ready_to_bill" or "finance.job.ready-to-bill" or "finance.job.ready_to_bill.view")
            return ["finance.job.ready_to_bill", "finance.job.ready-to-bill", "finance.job.ready_to_bill.view", "finance:manage", "billing:manage"];

        if (permission is "finance.invoice_draft.read" or "finance.invoice_draft.view")
            return ["finance.invoice_draft.read", "finance.invoice_draft.view", "finance:view", "billing:view"];

        if (permission is "finance.invoice_draft.create" or "finance.invoice_draft.update" or "finance.invoice_draft.manage")
            return ["finance.invoice_draft.create", "finance.invoice_draft.update", "finance.invoice_draft.manage", "finance:manage", "billing:manage"];

        if (permission is "finance.invoice.read" or "finance.invoice.view")
            return ["finance.invoice.read", "finance.invoice.view", "finance:view", "billing:view"];

        if (permission is "finance.invoice.issue" or "finance.invoice.approve" or "finance.invoice.issue.approve")
            return ["finance.invoice.issue", "finance.invoice.approve", "finance.invoice.issue.approve", "finance:manage", "billing:manage"];

        if (permission is "finance.invoice.payment.record" or "finance.invoice.payment.create")
            return ["finance.invoice.payment.record", "finance.invoice.payment.create", "finance:manage", "billing:manage"];

        if (permission is "finance.ar.summary.read" or "finance.ar.summary.view")
            return ["finance.ar.summary.read", "finance.ar.summary.view", "finance:view", "billing:view"];

        if (permission is "finance.revenue.summary.read" or "finance.revenue.summary.view")
            return ["finance.revenue.summary.read", "finance.revenue.summary.view", "finance:view", "billing:view"];

        if (permission is "customer.account.summary.read" or "customer.account.summary.view")
            return ["customer.account.summary.read", "customer.account.summary.view", "customer.account.read", "customer.account.view", "customers:view", "crm:view"];

        if (permission is "dispatch:view" or "dispatch.view")
            return ["dispatch:view", "dispatch.view", "job.read", "trip.read"];

        if (permission is "dispatch:create" or "dispatch:update" or "dispatch:assign" or "dispatch:cancel" or "dispatch.manage" or "dispatch:manage")
            return ["dispatch:create", "dispatch:update", "dispatch:assign", "dispatch:cancel", "dispatch.manage", "dispatch:manage", "job.create", "job.update", "job.manage", "trip.create", "trip.update", "trip.manage"];

        if (permission is "finance:view" or "finance.view")
            return ["finance:view", "finance.view", "billing:view", "billing.view", "contract.read", "rate_card.read", "charge.read"];

        if (permission is "finance:manage" or "finance.manage")
            return ["finance:manage", "finance.manage", "billing:manage", "billing.manage", "contract.create", "contract.update", "contract.manage", "rate_card.create", "rate_card.update", "rate_card.manage", "charge.create", "charge.update", "charge.manage"];

        return [permission];
    }

    private static AuthorizationDecisionResult Denied(AuthorizationDecisionRequest request, string permission, string reason)
        => new(
            DecisionStatus.Denied,
            permission,
            reason,
            request.Actor,
            request.Resource,
            request.Scope,
            request.Feature,
            request.Policy,
            request.CorrelationId,
            request.RequestId);
}

public static class AuthorizationEngine
{
    public static IAuthorizationDecisionService Default { get; } = new AuthorizationDecisionService();
}

public sealed class InMemoryApprovalWorkflowService : IApprovalWorkflowService
{
    private long _nextId;
    private readonly ConcurrentDictionary<long, ApprovalRequestRecord> _requests = new();
    private readonly ConcurrentDictionary<long, ApprovalDecisionRecord> _decisions = new();

    public ApprovalRequestRecord CreateRequest(
        string tenantId,
        string requestedByActorType,
        string? requestedByActorId,
        string actionKey,
        string resourceType,
        string? resourceId,
        string payloadJson,
        string riskLevel)
    {
        var id = Interlocked.Increment(ref _nextId);
        var request = new ApprovalRequestRecord(
            id,
            tenantId,
            requestedByActorType,
            requestedByActorId,
            actionKey,
            resourceType,
            resourceId,
            payloadJson,
            riskLevel,
            "pending",
            DateTimeOffset.UtcNow);

        var stored = request with { Id = id };
        _requests[id] = stored;
        return stored;
    }

    public ApprovalDecisionRecord Decide(long approvalRequestId, string approverUserId, string decision, string? notes = null)
    {
        if (!_requests.TryGetValue(approvalRequestId, out var request))
            throw new InvalidOperationException("Approval request not found");

        var id = Interlocked.Increment(ref _nextId);
        var record = new ApprovalDecisionRecord(
            id,
            approvalRequestId,
            approverUserId,
            decision,
            notes,
            DateTimeOffset.UtcNow);
        _decisions[approvalRequestId] = record;
        _requests[approvalRequestId] = request with { Status = decision };
        return record;
    }

    public ApprovalRequestRecord? GetRequest(long id) => _requests.TryGetValue(id, out var request) ? request : null;
    public ApprovalDecisionRecord? GetDecision(long id) => _decisions.TryGetValue(id, out var decision) ? decision : null;
}

public sealed class InMemoryDomainEventPublisher : IDomainEventPublisher, IOutboxWriter, IInboxProcessor
{
    private long _nextDomainEventId;
    private long _nextOutboxMessageId;
    private long _nextInboxMessageId;
    private long _nextEventProcessingLogId;

    private readonly List<DomainEventRecord> _domainEvents = [];
    private readonly List<OutboxMessageRecord> _outbox = [];
    private readonly List<InboxMessageRecord> _inbox = [];
    private readonly List<EventProcessingLogRecord> _processing = [];

    public IReadOnlyList<DomainEventRecord> DomainEvents => _domainEvents;
    public IReadOnlyList<OutboxMessageRecord> Outbox => _outbox;
    public IReadOnlyList<InboxMessageRecord> Inbox => _inbox;
    public IReadOnlyList<EventProcessingLogRecord> Processing => _processing;

    public DomainEventRecord Publish(string tenantId, string eventType, string aggregateType, string aggregateId, string payloadJson, string? correlationId = null, string? causationId = null, string? idempotencyKey = null)
    {
        var id = Interlocked.Increment(ref _nextDomainEventId);
        var record = new DomainEventRecord(id, tenantId, eventType, aggregateType, aggregateId, payloadJson, correlationId, causationId, idempotencyKey, DateTimeOffset.UtcNow);
        _domainEvents.Add(record);
        _outbox.Add(new OutboxMessageRecord(Interlocked.Increment(ref _nextOutboxMessageId), tenantId, eventType, aggregateType, aggregateId, payloadJson, correlationId, causationId, idempotencyKey, DateTimeOffset.UtcNow));
        return record;
    }

    public OutboxMessageRecord Write(string tenantId, string eventType, string aggregateType, string aggregateId, string payloadJson, string? correlationId = null, string? causationId = null, string? idempotencyKey = null)
    {
        var record = new OutboxMessageRecord(Interlocked.Increment(ref _nextOutboxMessageId), tenantId, eventType, aggregateType, aggregateId, payloadJson, correlationId, causationId, idempotencyKey, DateTimeOffset.UtcNow);
        _outbox.Add(record);
        return record;
    }

    public InboxMessageRecord Record(string tenantId, string eventType, string source, string externalId, string payloadJson, string? correlationId = null, string? causationId = null, string? idempotencyKey = null)
    {
        var record = new InboxMessageRecord(Interlocked.Increment(ref _nextInboxMessageId), tenantId, eventType, source, externalId, payloadJson, correlationId, causationId, "received", DateTimeOffset.UtcNow);
        _inbox.Add(record);
        _processing.Add(new EventProcessingLogRecord(Interlocked.Increment(ref _nextEventProcessingLogId), tenantId, eventType, source, "received", null, correlationId, causationId, DateTimeOffset.UtcNow));
        return record;
    }
}

public sealed class InMemoryIdempotencyService : IEventIdempotencyService
{
    private long _nextIdempotencyId;
    private readonly ConcurrentDictionary<string, IdempotencyRecord> _entries = new(StringComparer.OrdinalIgnoreCase);

    public IdempotencyRecord Reserve(string tenantId, string operation, string idempotencyKey, string requestHash, TimeSpan ttl, string? responseReference = null)
    {
        var compositeKey = BuildKey(tenantId, operation, idempotencyKey);
        var created = DateTimeOffset.UtcNow;
        var record = new IdempotencyRecord(Interlocked.Increment(ref _nextIdempotencyId), tenantId, operation, idempotencyKey, requestHash, null, responseReference, "reserved", created.Add(ttl), created);
        if (!_entries.TryAdd(compositeKey, record))
        {
            var existing = _entries[compositeKey];
            if (!string.Equals(existing.RequestHash, requestHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Duplicate idempotency key with a different request hash");
            return existing;
        }
        return record;
    }

    public bool TryComplete(string tenantId, string operation, string idempotencyKey, string responseHash, string? responseReference = null)
    {
        var compositeKey = BuildKey(tenantId, operation, idempotencyKey);
        if (!_entries.TryGetValue(compositeKey, out var existing)) return false;
        _entries[compositeKey] = existing with { ResponseHash = responseHash, ResponseReference = responseReference, Status = "completed" };
        return true;
    }

    private static string BuildKey(string tenantId, string operation, string idempotencyKey)
        => $"{tenantId}:{operation}:{idempotencyKey}";
}

public sealed class InMemoryAiFoundationService
{
    private long _nextRecommendationId;
    private long _nextActionRequestId;
    private long _nextReasoningRunId;
    private long _nextOutcomeId;

    private readonly List<AiRecommendationRecord> _recommendations = [];
    private readonly List<RecommendationReasonRecord> _reasons = [];
    private readonly List<RecommendationImpactRecord> _impacts = [];
    private readonly List<AiActionRequestRecord> _actionRequests = [];
    private readonly List<AiActionOutcomeRecord> _outcomes = [];
    private readonly List<AiReasoningRunRecord> _runs = [];

    public IReadOnlyList<AiRecommendationRecord> Recommendations => _recommendations;
    public IReadOnlyList<RecommendationReasonRecord> Reasons => _reasons;
    public IReadOnlyList<RecommendationImpactRecord> Impacts => _impacts;
    public IReadOnlyList<AiActionRequestRecord> ActionRequests => _actionRequests;
    public IReadOnlyList<AiActionOutcomeRecord> Outcomes => _outcomes;
    public IReadOnlyList<AiReasoningRunRecord> Runs => _runs;

    public AiReasoningRunRecord StartReasoningRun(string tenantId, string triggerType, string inputJson, string promptTemplate, string expectedSchemaJson, string? correlationId = null, string? causationId = null)
    {
        var id = Interlocked.Increment(ref _nextReasoningRunId);
        var run = new AiReasoningRunRecord(id, tenantId, triggerType, inputJson, promptTemplate, expectedSchemaJson, "started", null, null, null, correlationId, causationId, DateTimeOffset.UtcNow);
        _runs.Add(run);
        return run;
    }

    public AiReasoningRunRecord CompleteReasoningRun(AiReasoningRunRecord run, string outputJson, decimal confidenceScore)
    {
        var completed = run with { Status = "completed", OutputJson = outputJson, ConfidenceScore = confidenceScore, CompletedAt = DateTimeOffset.UtcNow };
        ReplaceRun(run, completed);
        return completed;
    }

    public AiReasoningRunRecord FailReasoningRun(AiReasoningRunRecord run, string errorJson)
    {
        var failed = run with { Status = "failed", ErrorJson = errorJson, CompletedAt = DateTimeOffset.UtcNow };
        ReplaceRun(run, failed);
        return failed;
    }

    public AiRecommendationRecord CreateRecommendation(
        string tenantId,
        string recommendationType,
        string title,
        string summary,
        decimal confidenceScore,
        decimal urgencyScore,
        string impactJson,
        string reasonJson,
        string proposedActionJson,
        string riskLevel,
        string? sourceEventId = null,
        string? actorType = null,
        string? actorId = null)
    {
        var recommendationId = Interlocked.Increment(ref _nextRecommendationId);
        var recommendation = new AiRecommendationRecord(
            recommendationId,
            tenantId,
            recommendationType,
            title,
            summary,
            confidenceScore,
            urgencyScore,
            impactJson,
            reasonJson,
            proposedActionJson,
            riskLevel,
            "draft",
            sourceEventId,
            actorType,
            actorId,
            DateTimeOffset.UtcNow);

        _recommendations.Add(recommendation);
        _reasons.Add(new RecommendationReasonRecord(tenantId, recommendationId, reasonJson, DateTimeOffset.UtcNow));
        _impacts.Add(new RecommendationImpactRecord(tenantId, recommendationId, impactJson, DateTimeOffset.UtcNow));
        return recommendation;
    }

    public AiActionRequestRecord CreateActionRequest(
        string tenantId,
        long recommendationId,
        string actionKey,
        string resourceType,
        string? resourceId,
        string payloadJson,
        string riskLevel,
        string? requestedByActorType = null,
        string? requestedByActorId = null,
        bool requiresApproval = true)
    {
        var id = Interlocked.Increment(ref _nextActionRequestId);
        var record = new AiActionRequestRecord(
            id,
            tenantId,
            recommendationId,
            actionKey,
            resourceType,
            resourceId,
            payloadJson,
            riskLevel,
            requiresApproval ? "approval_required" : "pending",
            requestedByActorType,
            requestedByActorId,
            DateTimeOffset.UtcNow);
        _actionRequests.Add(record);
        return record;
    }

    public AiActionOutcomeRecord RecordOutcome(string tenantId, long actionRequestId, string status, string? outcomeJson = null)
    {
        var record = new AiActionOutcomeRecord(Interlocked.Increment(ref _nextOutcomeId), tenantId, actionRequestId, status, outcomeJson, DateTimeOffset.UtcNow);
        _outcomes.Add(record);
        return record;
    }

    private void ReplaceRun(AiReasoningRunRecord original, AiReasoningRunRecord replacement)
    {
        var index = _runs.IndexOf(original);
        if (index >= 0) _runs[index] = replacement;
    }
}

public sealed class AmbientCorrelationContext : ICorrelationContext
{
    private static readonly AsyncLocal<CorrelationValues?> Current = new();

    public string? CorrelationId => Current.Value?.CorrelationId;
    public string? CausationId => Current.Value?.CausationId;
    public string? RequestId => Current.Value?.RequestId;
    public string? TenantId => Current.Value?.TenantId;
    public string? ActorType => Current.Value?.ActorType;
    public string? ActorId => Current.Value?.ActorId;

    public static IDisposable Begin(string? correlationId = null, string? causationId = null, string? requestId = null, string? tenantId = null, string? actorType = null, string? actorId = null)
    {
        var previous = Current.Value;
        Current.Value = new CorrelationValues(correlationId, causationId, requestId, tenantId, actorType, actorId);
        return new Scope(() => Current.Value = previous);
    }

    private sealed record CorrelationValues(string? CorrelationId, string? CausationId, string? RequestId, string? TenantId, string? ActorType, string? ActorId);

    private sealed class Scope(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}

public sealed class InMemoryAuditLogService : IAuditLogService
{
    private readonly List<AuthorizationDecisionLogRecord> _entries = [];
    public IReadOnlyList<AuthorizationDecisionLogRecord> Entries => _entries;

    public AuthorizationDecisionLogRecord RecordAuthorizationDecision(AuthorizationDecisionResult decision, string tenantId)
    {
        var record = new AuthorizationDecisionLogRecord(
            tenantId,
            decision.Actor.ActorType,
            decision.Actor.ActorId,
            decision.Permission,
            decision.Resource.ResourceType,
            decision.Resource.ResourceId,
            decision.Status,
            decision.Reason,
            decision.CorrelationId,
            decision.RequestId,
            DateTimeOffset.UtcNow);
        _entries.Add(record);
        return record;
    }
}
