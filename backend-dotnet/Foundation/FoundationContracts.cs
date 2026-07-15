namespace Opstrax.Api.Foundation;

public enum DecisionStatus
{
    Allowed,
    Denied,
    ApprovalRequired,
}

public static class ActorTypes
{
    public const string TenantUser = "tenant_user";
    public const string PlatformUser = "platform_user";
    public const string CustomerPortalUser = "customer_portal_user";
    public const string DriverUser = "driver_user";
    public const string AiAgent = "ai_agent";
    public const string ApiKey = "api_key";
    public const string Integration = "integration";
    public const string System = "system";
}

public readonly record struct PermissionKey(string Value)
{
    public static PermissionKey Parse(string value) => new(Normalize(value));
    public override string ToString() => Value;

    public static string Normalize(string value)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Permission key cannot be blank.", nameof(value))
            : value.Trim().ToLowerInvariant().Replace(':', '.');
}

public sealed record ActorContext(
    string ActorType,
    string? ActorId = null,
    string? Role = null,
    IReadOnlyCollection<string>? Permissions = null,
    string? TenantId = null);

public sealed record ResourceContext(
    string ResourceType,
    string? ResourceId = null,
    string? OwnerTenantId = null,
    string? OwnerActorId = null,
    string? ScopeType = null,
    string? ScopeValue = null);

public sealed record ScopeContext(
    string ScopeType,
    string? ScopeValue = null,
    bool AllowsCrossTenant = false);

public sealed record FeatureAccessContext(
    string? FeatureKey = null,
    string? SubscriptionStatus = null,
    string? BillingStatus = null,
    bool Enabled = true,
    int? UsageLimit = null,
    int? UsageUsed = null);

public sealed record AuthorizationPolicyContext(
    bool TenantBoundaryAllowed = true,
    bool ResourceOwnershipAllowed = true,
    bool FeatureEnabled = true,
    bool UsageWithinLimit = true,
    bool DenyOverride = false,
    bool ApprovalRequired = false,
    string? TenantStatus = "active",
    string? BillingStatus = "current",
    string? Reason = null);

public sealed record AuthorizationDecisionRequest(
    ActorContext Actor,
    PermissionKey Permission,
    ResourceContext Resource,
    ScopeContext? Scope = null,
    FeatureAccessContext? Feature = null,
    AuthorizationPolicyContext? Policy = null,
    string? CorrelationId = null,
    string? RequestId = null);

public sealed record AuthorizationDecisionResult(
    DecisionStatus Status,
    string Permission,
    string Reason,
    ActorContext Actor,
    ResourceContext Resource,
    ScopeContext? Scope = null,
    FeatureAccessContext? Feature = null,
    AuthorizationPolicyContext? Policy = null,
    string? CorrelationId = null,
    string? RequestId = null)
{
    public bool IsAllowed => Status == DecisionStatus.Allowed;
}

public sealed record AuthorizationDecisionLogRecord(
    string TenantId,
    string ActorType,
    string? ActorId,
    string Permission,
    string ResourceType,
    string? ResourceId,
    DecisionStatus Decision,
    string Reason,
    string? CorrelationId,
    string? RequestId,
    DateTimeOffset CreatedAt);

public sealed record ApprovalRequestRecord(
    long Id,
    string TenantId,
    string RequestedByActorType,
    string? RequestedByActorId,
    string ActionKey,
    string ResourceType,
    string? ResourceId,
    string PayloadJson,
    string RiskLevel,
    string Status,
    DateTimeOffset RequestedAt,
    string? CorrelationId = null);

public sealed record ApprovalDecisionRecord(
    long Id,
    long ApprovalRequestId,
    string ApproverUserId,
    string Decision,
    string? Notes,
    DateTimeOffset DecidedAt,
    string? TenantId = null,
    string? ApproverActorType = null,
    string? CorrelationId = null);

public sealed record DomainEventRecord(
    long Id,
    string TenantId,
    string EventType,
    string AggregateType,
    string AggregateId,
    string PayloadJson,
    string? CorrelationId,
    string? CausationId,
    string? IdempotencyKey,
    DateTimeOffset OccurredAt,
    string Status = "pending",
    DateTimeOffset? ProcessedAt = null,
    int RetryCount = 0);

public sealed record OutboxMessageRecord(
    long Id,
    string TenantId,
    string EventType,
    string AggregateType,
    string AggregateId,
    string PayloadJson,
    string? CorrelationId,
    string? CausationId,
    string? IdempotencyKey,
    DateTimeOffset CreatedAt,
    string Status = "pending",
    int RetryCount = 0,
    DateTimeOffset? NextAttemptAt = null,
    DateTimeOffset? ProcessedAt = null);

public sealed record InboxMessageRecord(
    long Id,
    string TenantId,
    string EventType,
    string Source,
    string ExternalId,
    string PayloadJson,
    string? CorrelationId,
    string? CausationId,
    string Status,
    DateTimeOffset ReceivedAt,
    string? IdempotencyKey = null,
    string? PayloadHash = null,
    int RetryCount = 0,
    DateTimeOffset? ProcessedAt = null);

public sealed record OutboxDispatcherOptions
{
    public bool Enabled { get; init; } = false;
    public bool AllowProduction { get; init; } = false;
    public int BatchSize { get; init; } = 10;
    public int PollingIntervalSeconds { get; init; } = 5;
    public int MaxRetryCount { get; init; } = 3;
    public int RetryBackoffSeconds { get; init; } = 15;
    public int ProcessingTimeoutSeconds { get; init; } = 30;
    public long? TenantIdFilter { get; init; } = null;
    public string WorkerName { get; init; } = "foundation-dispatcher";
}

public sealed record EventProcessingLogRecord(
    long Id,
    string TenantId,
    string EventType,
    string Processor,
    string Status,
    string? Message,
    string? CorrelationId,
    string? CausationId,
    DateTimeOffset ProcessedAt,
    int RetryCount = 0);

public sealed record IdempotencyRecord(
    long Id,
    string TenantId,
    string Operation,
    string IdempotencyKey,
    string RequestHash,
    string? ResponseHash,
    string? ResponseReference,
    string Status,
    DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAt);

public sealed record RecommendationReasonRecord(
    string TenantId,
    long RecommendationId,
    string ReasonJson,
    DateTimeOffset CreatedAt);

public sealed record RecommendationImpactRecord(
    string TenantId,
    long RecommendationId,
    string ImpactJson,
    DateTimeOffset CreatedAt);

public sealed record AiRecommendationRecord(
    long Id,
    string TenantId,
    string RecommendationType,
    string Title,
    string Summary,
    decimal ConfidenceScore,
    decimal UrgencyScore,
    string ImpactJson,
    string ReasonJson,
    string ProposedActionJson,
    string RiskLevel,
    string Status,
    string? SourceEventId,
    string? ActorType,
    string? ActorId,
    DateTimeOffset CreatedAt,
    string? CorrelationId = null,
    string? CausationId = null);

public sealed record AiActionRequestRecord(
    long Id,
    string TenantId,
    long RecommendationId,
    string ActionKey,
    string ResourceType,
    string? ResourceId,
    string PayloadJson,
    string RiskLevel,
    string Status,
    string? RequestedByActorType,
    string? RequestedByActorId,
    DateTimeOffset RequestedAt,
    string? CorrelationId = null,
    string? CausationId = null);

public sealed record AiActionOutcomeRecord(
    long Id,
    string TenantId,
    long ActionRequestId,
    string Status,
    string? OutcomeJson,
    DateTimeOffset RecordedAt);

public sealed record AiReasoningRunRecord(
    long Id,
    string TenantId,
    string TriggerType,
    string InputJson,
    string PromptTemplate,
    string ExpectedSchemaJson,
    string Status,
    decimal? ConfidenceScore,
    string? OutputJson,
    string? ErrorJson,
    string? CorrelationId,
    string? CausationId,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt = null);

public sealed record FeatureAccessResult(
    bool Allowed,
    string Reason,
    string? FeatureKey = null,
    string? SubscriptionStatus = null,
    string? BillingStatus = null,
    bool? UsageWithinLimit = null);

public static class ApprovalPolicyCatalog
{
    public static readonly IReadOnlyCollection<string> HighRiskActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Normalize("finance.invoice.issue"),
        Normalize("finance.credit_note.approve"),
        Normalize("settlement.approve"),
        Normalize("finance.tax_profile.publish"),
        Normalize("revrec.period.close"),
        Normalize("customer.contract.rate_change"),
        Normalize("dispatch.trip.reassign_high_value"),
        Normalize("iot.vehicle.immobilize"),
        Normalize("ai.action.execute_external"),
        Normalize("platform.tenant.suspend"),
        Normalize("safety.evidence_pack.share_external"),
    };

    public static bool RequiresApproval(string actionKey)
        => HighRiskActions.Contains(Normalize(actionKey));

    public static string Normalize(string value)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Action key cannot be blank.", nameof(value))
            : value.Trim().ToLowerInvariant().Replace(':', '.');
}

public interface IFeatureAccessService
{
    FeatureAccessResult Evaluate(string tenantId, FeatureAccessContext? context);
}

public interface IAuthorizationDecisionService
{
    AuthorizationDecisionResult Decide(AuthorizationDecisionRequest request);
}

public interface IApprovalWorkflowService
{
    ApprovalRequestRecord CreateRequest(
        string tenantId,
        string requestedByActorType,
        string? requestedByActorId,
        string actionKey,
        string resourceType,
        string? resourceId,
        string payloadJson,
        string riskLevel);

    ApprovalDecisionRecord Decide(long approvalRequestId, string approverUserId, string decision, string? notes = null);
}

public interface IDomainEventPublisher
{
    DomainEventRecord Publish(
        string tenantId,
        string eventType,
        string aggregateType,
        string aggregateId,
        string payloadJson,
        string? correlationId = null,
        string? causationId = null,
        string? idempotencyKey = null);
}

public interface IOutboxWriter
{
    OutboxMessageRecord Write(
        string tenantId,
        string eventType,
        string aggregateType,
        string aggregateId,
        string payloadJson,
        string? correlationId = null,
        string? causationId = null,
        string? idempotencyKey = null);
}

public interface IOutboxMessageHandler
{
    string EventType { get; }
    Task HandleAsync(OutboxMessageRecord message, CancellationToken ct = default);
}

public interface IOutboxMessageHandlerRegistry
{
    IOutboxMessageHandler? Resolve(string eventType);
    IReadOnlyCollection<string> RegisteredEventTypes { get; }
}

public interface IEventProcessingLogService
{
    EventProcessingLogRecord Record(
        string tenantId,
        string eventType,
        string processor,
        string status,
        string? message = null,
        string? correlationId = null,
        string? causationId = null,
        int retryCount = 0);
}

public interface IOutboxDispatcher
{
    Task<int> DispatchOutboxOnceAsync(CancellationToken ct = default);
    Task<int> DispatchInboxOnceAsync(CancellationToken ct = default);
}

public interface IInboxProcessor
{
    InboxMessageRecord Record(
        string tenantId,
        string eventType,
        string source,
        string externalId,
        string payloadJson,
        string? correlationId = null,
        string? causationId = null,
        string? idempotencyKey = null);
}

public interface IEventIdempotencyService
{
    IdempotencyRecord Reserve(
        string tenantId,
        string operation,
        string idempotencyKey,
        string requestHash,
        TimeSpan ttl,
        string? responseReference = null);

    bool TryComplete(
        string tenantId,
        string operation,
        string idempotencyKey,
        string responseHash,
        string? responseReference = null);
}

public interface ICorrelationContext
{
    string? CorrelationId { get; }
    string? CausationId { get; }
    string? RequestId { get; }
    string? TenantId { get; }
    string? ActorType { get; }
    string? ActorId { get; }
}

public interface IAuditLogService
{
    AuthorizationDecisionLogRecord RecordAuthorizationDecision(AuthorizationDecisionResult decision, string tenantId);
}
