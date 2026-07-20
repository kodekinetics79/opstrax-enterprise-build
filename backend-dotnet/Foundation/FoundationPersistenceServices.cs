using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using Opstrax.Api.Data;

namespace Opstrax.Api.Foundation;

public sealed class PostgresFeatureAccessService(Database db) : IFeatureAccessService
{
    public async Task<FeatureAccessResult> EvaluateAsync(string tenantId, FeatureAccessContext? context, CancellationToken ct = default)
    {
        return await Task.FromResult(Evaluate(tenantId, context));
    }

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

        if (effective.UsageLimit.HasValue && effective.UsageUsed.HasValue && effective.UsageUsed > effective.UsageLimit)
            return new(false, "Usage limit exceeded", effective.FeatureKey, effective.SubscriptionStatus, effective.BillingStatus, false);

        if (string.IsNullOrWhiteSpace(effective.FeatureKey))
            return new(true, "Feature access allowed", effective.FeatureKey, effective.SubscriptionStatus, effective.BillingStatus, true);

        if (!long.TryParse(tenantId, out var companyId) || companyId <= 0)
            return new(false, "Invalid tenant context", effective.FeatureKey, effective.SubscriptionStatus, effective.BillingStatus);

        var entitlement = db.QuerySingleAsync(
            @"SELECT enabled, limit_value
              FROM tenant_entitlements
              WHERE company_id=@cid AND module_key=@mk
              LIMIT 1",
            c =>
            {
                c.Parameters.AddWithValue("@cid", companyId);
                c.Parameters.AddWithValue("@mk", effective.FeatureKey.Trim().ToLowerInvariant());
            }).GetAwaiter().GetResult();

        if (entitlement is null)
            return new(true, "Feature access allowed", effective.FeatureKey, effective.SubscriptionStatus, effective.BillingStatus, true);

        var enabled = entitlement.TryGetValue("enabled", out var enabledValue) && enabledValue is bool enabledBool ? enabledBool : true;
        if (!enabled)
            return new(false, "Feature disabled", effective.FeatureKey, effective.SubscriptionStatus, effective.BillingStatus);

        var limitValue = entitlement.TryGetValue("limitValue", out var rawLimit) && rawLimit is not null ? Convert.ToInt32(rawLimit) : (int?)null;
        if (limitValue.HasValue && effective.UsageUsed.HasValue && effective.UsageUsed > limitValue)
            return new(false, "Usage limit exceeded", effective.FeatureKey, effective.SubscriptionStatus, effective.BillingStatus, false);

        return new(true, "Feature access allowed", effective.FeatureKey, effective.SubscriptionStatus, effective.BillingStatus, true);
    }
}

public sealed class PostgresAuditLogService(Database db) : IAuditLogService
{
    public AuthorizationDecisionLogRecord RecordAuthorizationDecision(AuthorizationDecisionResult decision, string tenantId)
    {
        if (!long.TryParse(tenantId, out var companyId) || companyId <= 0)
            throw new InvalidOperationException("Invalid tenant id for authorization decision log.");

        var createdAt = DateTimeOffset.UtcNow;
        var row = db.QuerySingleAsync(
            @"INSERT INTO authorization_decision_logs
                (tenant_id, actor_type, actor_id, permission_key, resource_type, resource_id, decision, reason, correlation_id, request_id, created_at)
              VALUES
                (@tenantId, @actorType, @actorId, @permission, @resourceType, @resourceId, @decision, @reason, @correlationId, @requestId, @createdAt)
              RETURNING id",
            c =>
            {
                c.Parameters.AddWithValue("@tenantId", companyId);
                c.Parameters.AddWithValue("@actorType", decision.Actor.ActorType);
                c.Parameters.AddWithValue("@actorId", (object?)decision.Actor.ActorId ?? DBNull.Value);
                c.Parameters.AddWithValue("@permission", decision.Permission);
                c.Parameters.AddWithValue("@resourceType", decision.Resource.ResourceType);
                c.Parameters.AddWithValue("@resourceId", (object?)decision.Resource.ResourceId ?? DBNull.Value);
                c.Parameters.AddWithValue("@decision", decision.Status.ToString().ToLowerInvariant());
                c.Parameters.AddWithValue("@reason", decision.Reason);
                c.Parameters.AddWithValue("@correlationId", (object?)decision.CorrelationId ?? DBNull.Value);
                c.Parameters.AddWithValue("@requestId", (object?)decision.RequestId ?? DBNull.Value);
                c.Parameters.AddWithValue("@createdAt", createdAt);
            }).GetAwaiter().GetResult();

        var id = row is null ? 0L : Convert.ToInt64(row["id"]);
        return new AuthorizationDecisionLogRecord(
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
            createdAt);
    }
}

public sealed class PostgresApprovalWorkflowService(Database db, ICorrelationContext? correlation = null) : IApprovalWorkflowService
{
    public ApprovalRequestRecord CreateRequest(string tenantId, string requestedByActorType, string? requestedByActorId, string actionKey, string resourceType, string? resourceId, string payloadJson, string riskLevel)
    {
        if (!long.TryParse(tenantId, out var companyId) || companyId <= 0)
            throw new InvalidOperationException("Invalid tenant id for approval request.");

        var correlationId = correlation?.CorrelationId;
        var createdAt = DateTimeOffset.UtcNow;
        var row = db.QuerySingleAsync(
            @"INSERT INTO approval_requests
                (tenant_id, requested_by_actor_type, requested_by_actor_id, action_key, resource_type, resource_id, payload_json, risk_level, status, requested_at, correlation_id)
              VALUES
                (@tenantId, @actorType, @actorId, @actionKey, @resourceType, @resourceId, COALESCE(@payload::jsonb, '{}'::jsonb), @riskLevel, 'pending', @requestedAt, @correlationId)
              RETURNING id, requested_at",
            c =>
            {
                c.Parameters.AddWithValue("@tenantId", companyId);
                c.Parameters.AddWithValue("@actorType", requestedByActorType);
                c.Parameters.AddWithValue("@actorId", (object?)requestedByActorId ?? DBNull.Value);
                c.Parameters.AddWithValue("@actionKey", actionKey);
                c.Parameters.AddWithValue("@resourceType", resourceType);
                c.Parameters.AddWithValue("@resourceId", (object?)resourceId ?? DBNull.Value);
                c.Parameters.AddWithValue("@payload", payloadJson);
                c.Parameters.AddWithValue("@riskLevel", riskLevel);
                c.Parameters.AddWithValue("@requestedAt", createdAt);
                c.Parameters.AddWithValue("@correlationId", (object?)correlationId ?? DBNull.Value);
            }).GetAwaiter().GetResult();

        var id = row is null ? 0L : Convert.ToInt64(row["id"]);
        return new ApprovalRequestRecord(id, tenantId, requestedByActorType, requestedByActorId, actionKey, resourceType, resourceId, payloadJson, riskLevel, "pending", createdAt, correlationId);
    }

    public ApprovalDecisionRecord Decide(long approvalRequestId, string approverUserId, string decision, string? notes = null)
    {
        return db.WithTransactionAsync(async (conn, tx) =>
        {
            await using var requestCmd = new NpgsqlCommand(
                @"SELECT tenant_id, requested_by_actor_type, correlation_id
                  FROM approval_requests
                  WHERE id=@id
                  FOR UPDATE", conn, tx);
            requestCmd.Parameters.AddWithValue("@id", approvalRequestId);

            await using var reader = await requestCmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                throw new InvalidOperationException("Approval request not found");

            var companyId = reader.GetInt64(0);
            var requestedByActorType = reader.IsDBNull(1) ? null : reader.GetString(1);
            var correlationId = reader.IsDBNull(2) ? null : reader.GetString(2);
            await reader.DisposeAsync();

            await using (var updateCmd = new NpgsqlCommand(
                "UPDATE approval_requests SET status=@status WHERE id=@id", conn, tx))
            {
                updateCmd.Parameters.AddWithValue("@status", decision);
                updateCmd.Parameters.AddWithValue("@id", approvalRequestId);
                await updateCmd.ExecuteNonQueryAsync();
            }

            var decidedAt = DateTimeOffset.UtcNow;
            await using var insertCmd = new NpgsqlCommand(
                @"INSERT INTO approval_decisions
                    (approval_request_id, tenant_id, approver_user_id, approver_actor_type, decision, notes, decided_at, correlation_id)
                  VALUES
                    (@approvalRequestId, @tenantId, @approverUserId, @approverActorType, @decision, @notes, @decidedAt, @correlationId)
                  RETURNING id",
                conn, tx);
            insertCmd.Parameters.AddWithValue("@approvalRequestId", approvalRequestId);
            insertCmd.Parameters.AddWithValue("@tenantId", companyId);
            insertCmd.Parameters.AddWithValue("@approverUserId", approverUserId);
            insertCmd.Parameters.AddWithValue("@approverActorType", ActorTypes.TenantUser);
            insertCmd.Parameters.AddWithValue("@decision", decision);
            insertCmd.Parameters.AddWithValue("@notes", (object?)notes ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("@decidedAt", decidedAt);
            insertCmd.Parameters.AddWithValue("@correlationId", (object?)correlationId ?? DBNull.Value);
            var id = Convert.ToInt64(await insertCmd.ExecuteScalarAsync());

            return new ApprovalDecisionRecord(id, approvalRequestId, approverUserId, decision, notes, decidedAt, companyId.ToString(), ActorTypes.TenantUser, correlationId);
        }).GetAwaiter().GetResult();
    }
}

public sealed class PostgresDomainEventPublisher(Database db, ICorrelationContext? correlation = null) : IDomainEventPublisher, IOutboxWriter, IInboxProcessor
{
    public DomainEventRecord Publish(string tenantId, string eventType, string aggregateType, string aggregateId, string payloadJson, string? correlationId = null, string? causationId = null, string? idempotencyKey = null)
    {
        return db.WithTransactionAsync(async (conn, tx) =>
        {
            var occurredAt = DateTimeOffset.UtcNow;
            await using var insertEvent = new NpgsqlCommand(
                @"INSERT INTO domain_events
                    (tenant_id, event_type, aggregate_type, aggregate_id, payload_json, correlation_id, causation_id, idempotency_key, occurred_at, status)
                  VALUES
                    (@tenantId, @eventType, @aggregateType, @aggregateId, COALESCE(@payload::jsonb, '{}'::jsonb), @correlationId, @causationId, @idempotencyKey, @occurredAt, 'pending')
                  RETURNING id",
                conn, tx);
            var effectiveCorrelationId = correlationId ?? correlation?.CorrelationId;
            var effectiveCausationId = causationId ?? correlation?.CausationId;
            insertEvent.Parameters.AddWithValue("@tenantId", FoundationPersistenceHelpers.RequireTenantId(tenantId));
            insertEvent.Parameters.AddWithValue("@eventType", eventType);
            insertEvent.Parameters.AddWithValue("@aggregateType", aggregateType);
            insertEvent.Parameters.AddWithValue("@aggregateId", aggregateId);
            insertEvent.Parameters.AddWithValue("@payload", payloadJson);
            insertEvent.Parameters.AddWithValue("@correlationId", (object?)effectiveCorrelationId ?? DBNull.Value);
            insertEvent.Parameters.AddWithValue("@causationId", (object?)effectiveCausationId ?? DBNull.Value);
            insertEvent.Parameters.AddWithValue("@idempotencyKey", (object?)idempotencyKey ?? DBNull.Value);
            insertEvent.Parameters.AddWithValue("@occurredAt", occurredAt);
            var id = Convert.ToInt64(await insertEvent.ExecuteScalarAsync());

            await WriteOutboxAsync(conn, tx, tenantId, eventType, aggregateType, aggregateId, payloadJson, correlationId, causationId, idempotencyKey, occurredAt);

            return new DomainEventRecord(id, tenantId, eventType, aggregateType, aggregateId, payloadJson, correlationId, causationId, idempotencyKey, occurredAt);
        }).GetAwaiter().GetResult();
    }

    public OutboxMessageRecord Write(string tenantId, string eventType, string aggregateType, string aggregateId, string payloadJson, string? correlationId = null, string? causationId = null, string? idempotencyKey = null)
        => db.WithTransactionAsync((conn, tx) => WriteOutboxAsync(conn, tx, tenantId, eventType, aggregateType, aggregateId, payloadJson, correlationId, causationId, idempotencyKey, DateTimeOffset.UtcNow)).GetAwaiter().GetResult();

    public InboxMessageRecord Record(string tenantId, string eventType, string source, string externalId, string payloadJson, string? correlationId = null, string? causationId = null, string? idempotencyKey = null)
    {
        return db.WithTransactionAsync(async (conn, tx) =>
        {
            var receivedAt = DateTimeOffset.UtcNow;
            var payloadHash = FoundationPersistenceHelpers.ComputeHash(payloadJson);
            var tenant = FoundationPersistenceHelpers.RequireTenantId(tenantId);
            var duplicateId = await FindDuplicateInboxIdAsync(conn, tx, tenant, source, externalId, idempotencyKey);
            if (duplicateId is not null)
            {
                await using var duplicateLog = new NpgsqlCommand(
                    @"INSERT INTO event_processing_logs
                        (tenant_id, event_type, processor, status, message, correlation_id, causation_id, processed_at, retry_count)
                      VALUES
                        (@tenantId, @eventType, @processor, 'ignored_duplicate', @message, @correlationId, @causationId, @processedAt, 0)",
                    conn, tx);
                duplicateLog.Parameters.AddWithValue("@tenantId", tenant);
                duplicateLog.Parameters.AddWithValue("@eventType", eventType);
                duplicateLog.Parameters.AddWithValue("@processor", source);
                duplicateLog.Parameters.AddWithValue("@message", $"Duplicate inbox message ignored for external_id={externalId}");
                duplicateLog.Parameters.AddWithValue("@correlationId", (object?)(correlationId ?? correlation?.CorrelationId) ?? DBNull.Value);
                duplicateLog.Parameters.AddWithValue("@causationId", (object?)(causationId ?? correlation?.CausationId) ?? DBNull.Value);
                duplicateLog.Parameters.AddWithValue("@processedAt", receivedAt);
                await duplicateLog.ExecuteNonQueryAsync();

                return new InboxMessageRecord(
                    duplicateId.Value,
                    tenantId,
                    eventType,
                    source,
                    externalId,
                    payloadJson,
                    correlationId ?? correlation?.CorrelationId,
                    causationId ?? correlation?.CausationId,
                    "ignored_duplicate",
                    receivedAt,
                    idempotencyKey,
                    payloadHash,
                    0,
                    null);
            }

            await using var insertInbox = new NpgsqlCommand(
                @"INSERT INTO inbox_messages
                    (tenant_id, event_type, source, external_id, payload_json, correlation_id, causation_id, received_at, status, payload_hash, idempotency_key)
                  VALUES
                    (@tenantId, @eventType, @source, @externalId, COALESCE(@payload::jsonb, '{}'::jsonb), @correlationId, @causationId, @receivedAt, 'received', @payloadHash, @idempotencyKey)
                  RETURNING id",
                conn, tx);
            insertInbox.Parameters.AddWithValue("@tenantId", tenant);
            insertInbox.Parameters.AddWithValue("@eventType", eventType);
            insertInbox.Parameters.AddWithValue("@source", source);
            insertInbox.Parameters.AddWithValue("@externalId", externalId);
            insertInbox.Parameters.AddWithValue("@payload", payloadJson);
            insertInbox.Parameters.AddWithValue("@correlationId", (object?)(correlationId ?? correlation?.CorrelationId) ?? DBNull.Value);
            insertInbox.Parameters.AddWithValue("@causationId", (object?)(causationId ?? correlation?.CausationId) ?? DBNull.Value);
            insertInbox.Parameters.AddWithValue("@receivedAt", receivedAt);
            insertInbox.Parameters.AddWithValue("@payloadHash", payloadHash);
            insertInbox.Parameters.AddWithValue("@idempotencyKey", (object?)idempotencyKey ?? DBNull.Value);
            long id;
            try
            {
                id = Convert.ToInt64(await insertInbox.ExecuteScalarAsync());
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                await using var fallback = new NpgsqlCommand(
                    @"SELECT id
                      FROM inbox_messages
                      WHERE tenant_id=@tenantId AND (
                          (source=@source AND external_id=@externalId) OR
                          (@idempotencyKey IS NOT NULL AND idempotency_key=@idempotencyKey)
                      )
                      ORDER BY received_at DESC
                      LIMIT 1",
                    conn, tx);
                fallback.Parameters.AddWithValue("@tenantId", tenant);
                fallback.Parameters.AddWithValue("@source", source);
                fallback.Parameters.AddWithValue("@externalId", externalId);
                fallback.Parameters.AddWithValue("@idempotencyKey", (object?)idempotencyKey ?? DBNull.Value);
                var fallbackRow = await fallback.ExecuteScalarAsync();
                var fallbackDuplicateId = fallbackRow is null ? 0L : Convert.ToInt64(fallbackRow, CultureInfo.InvariantCulture);
                await using var duplicateLog = new NpgsqlCommand(
                    @"INSERT INTO event_processing_logs
                        (tenant_id, event_type, processor, status, message, correlation_id, causation_id, processed_at, retry_count)
                      VALUES
                        (@tenantId, @eventType, @processor, 'ignored_duplicate', @message, @correlationId, @causationId, @processedAt, 0)",
                    conn, tx);
                duplicateLog.Parameters.AddWithValue("@tenantId", tenant);
                duplicateLog.Parameters.AddWithValue("@eventType", eventType);
                duplicateLog.Parameters.AddWithValue("@processor", source);
                duplicateLog.Parameters.AddWithValue("@message", $"Duplicate inbox message ignored for external_id={externalId}");
                duplicateLog.Parameters.AddWithValue("@correlationId", (object?)(correlationId ?? correlation?.CorrelationId) ?? DBNull.Value);
                duplicateLog.Parameters.AddWithValue("@causationId", (object?)(causationId ?? correlation?.CausationId) ?? DBNull.Value);
                duplicateLog.Parameters.AddWithValue("@processedAt", receivedAt);
                await duplicateLog.ExecuteNonQueryAsync();

                return new InboxMessageRecord(
                    fallbackDuplicateId,
                    tenantId,
                    eventType,
                    source,
                    externalId,
                    payloadJson,
                    correlationId ?? correlation?.CorrelationId,
                    causationId ?? correlation?.CausationId,
                    "ignored_duplicate",
                    receivedAt,
                    idempotencyKey,
                    payloadHash,
                    0,
                    null);
            }

            await using var insertLog = new NpgsqlCommand(
                @"INSERT INTO event_processing_logs
                    (tenant_id, event_type, processor, status, message, correlation_id, causation_id, processed_at, retry_count)
                  VALUES
                    (@tenantId, @eventType, @processor, 'received', NULL, @correlationId, @causationId, @processedAt, 0)",
                conn, tx);
            insertLog.Parameters.AddWithValue("@tenantId", tenant);
            insertLog.Parameters.AddWithValue("@eventType", eventType);
            insertLog.Parameters.AddWithValue("@processor", source);
            insertLog.Parameters.AddWithValue("@correlationId", (object?)(correlationId ?? correlation?.CorrelationId) ?? DBNull.Value);
            insertLog.Parameters.AddWithValue("@causationId", (object?)(causationId ?? correlation?.CausationId) ?? DBNull.Value);
            insertLog.Parameters.AddWithValue("@processedAt", receivedAt);
            await insertLog.ExecuteNonQueryAsync();

            return new InboxMessageRecord(id, tenantId, eventType, source, externalId, payloadJson, correlationId ?? correlation?.CorrelationId, causationId ?? correlation?.CausationId, "received", receivedAt, idempotencyKey, payloadHash, 0, null);
        }).GetAwaiter().GetResult();
    }

    private static async Task<long?> FindDuplicateInboxIdAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        long tenantId,
        string source,
        string externalId,
        string? idempotencyKey)
    {
        var sql = string.IsNullOrWhiteSpace(idempotencyKey)
            ? @"SELECT id
                FROM inbox_messages
                WHERE tenant_id=@tenantId AND source=@source AND external_id=@externalId
                ORDER BY received_at DESC
                LIMIT 1"
            : @"SELECT id
                FROM inbox_messages
                WHERE tenant_id=@tenantId AND (
                    (source=@source AND external_id=@externalId) OR
                    idempotency_key=@idempotencyKey
                )
                ORDER BY received_at DESC
                LIMIT 1";

        await using var lookup = new NpgsqlCommand(sql, conn, tx);
        lookup.Parameters.AddWithValue("@tenantId", tenantId);
        lookup.Parameters.AddWithValue("@source", source);
        lookup.Parameters.AddWithValue("@externalId", externalId);
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
            lookup.Parameters.AddWithValue("@idempotencyKey", idempotencyKey);

        var value = await lookup.ExecuteScalarAsync();
        return value is null or DBNull ? null : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private static async Task<OutboxMessageRecord> WriteOutboxAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string tenantId, string eventType, string aggregateType, string aggregateId, string payloadJson, string? correlationId, string? causationId, string? idempotencyKey, DateTimeOffset createdAt)
    {
        await using var cmd = new NpgsqlCommand(
            @"INSERT INTO outbox_messages
                (tenant_id, event_type, aggregate_type, aggregate_id, payload_json, correlation_id, causation_id, idempotency_key, created_at, status, retry_count)
              VALUES
                (@tenantId, @eventType, @aggregateType, @aggregateId, COALESCE(@payload::jsonb, '{}'::jsonb), @correlationId, @causationId, @idempotencyKey, @createdAt, 'pending', 0)
              RETURNING id",
            conn, tx);
        cmd.Parameters.AddWithValue("@tenantId", FoundationPersistenceHelpers.RequireTenantId(tenantId));
        cmd.Parameters.AddWithValue("@eventType", eventType);
        cmd.Parameters.AddWithValue("@aggregateType", aggregateType);
        cmd.Parameters.AddWithValue("@aggregateId", aggregateId);
        cmd.Parameters.AddWithValue("@payload", payloadJson);
        cmd.Parameters.AddWithValue("@correlationId", (object?)correlationId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@causationId", (object?)causationId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@idempotencyKey", (object?)idempotencyKey ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@createdAt", createdAt);
        var id = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        return new OutboxMessageRecord(id, tenantId, eventType, aggregateType, aggregateId, payloadJson, correlationId, causationId, idempotencyKey, createdAt, "pending", 0, null, null);
    }
}

public sealed class PostgresIdempotencyService(Database db) : IEventIdempotencyService
{
    public IdempotencyRecord Reserve(string tenantId, string operation, string idempotencyKey, string requestHash, TimeSpan ttl, string? responseReference = null)
    {
        return db.WithTransactionAsync(async (conn, tx) =>
        {
            var companyId = FoundationPersistenceHelpers.RequireTenantId(tenantId);
            var lookup = new NpgsqlCommand(
                @"SELECT id, tenant_id, operation, idempotency_key, request_hash, response_hash, response_reference, status, expires_at, created_at
                  FROM idempotency_keys
                  WHERE tenant_id=@tenantId AND operation=@operation AND idempotency_key=@idempotencyKey
                  LIMIT 1",
                conn, tx);
            lookup.Parameters.AddWithValue("@tenantId", companyId);
            lookup.Parameters.AddWithValue("@operation", operation);
            lookup.Parameters.AddWithValue("@idempotencyKey", idempotencyKey);

            await using var reader = await lookup.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var existing = MapIdempotency(reader);
                await reader.DisposeAsync();
                if (!string.Equals(existing.RequestHash, requestHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Duplicate idempotency key with a different request hash");
                return existing;
            }

            await reader.DisposeAsync();

            var createdAt = DateTimeOffset.UtcNow;
            var insert = new NpgsqlCommand(
                @"INSERT INTO idempotency_keys
                    (tenant_id, operation, idempotency_key, request_hash, response_reference, status, expires_at, created_at)
                  VALUES
                    (@tenantId, @operation, @idempotencyKey, @requestHash, @responseReference, 'reserved', @expiresAt, @createdAt)
                  RETURNING id",
                conn, tx);
            insert.Parameters.AddWithValue("@tenantId", companyId);
            insert.Parameters.AddWithValue("@operation", operation);
            insert.Parameters.AddWithValue("@idempotencyKey", idempotencyKey);
            insert.Parameters.AddWithValue("@requestHash", requestHash);
            insert.Parameters.AddWithValue("@responseReference", (object?)responseReference ?? DBNull.Value);
            insert.Parameters.AddWithValue("@expiresAt", createdAt.Add(ttl));
            insert.Parameters.AddWithValue("@createdAt", createdAt);
            var id = Convert.ToInt64(await insert.ExecuteScalarAsync());
            return new IdempotencyRecord(id, tenantId, operation, idempotencyKey, requestHash, null, responseReference, "reserved", createdAt.Add(ttl), createdAt);
        }).GetAwaiter().GetResult();
    }

    public bool TryComplete(string tenantId, string operation, string idempotencyKey, string responseHash, string? responseReference = null)
    {
        var updated = db.ExecuteAsync(
            @"UPDATE idempotency_keys
              SET response_hash=@responseHash, response_reference=@responseReference, status='completed'
              WHERE tenant_id=@tenantId AND operation=@operation AND idempotency_key=@idempotencyKey",
            c =>
            {
                c.Parameters.AddWithValue("@tenantId", FoundationPersistenceHelpers.RequireTenantId(tenantId));
                c.Parameters.AddWithValue("@operation", operation);
                c.Parameters.AddWithValue("@idempotencyKey", idempotencyKey);
                c.Parameters.AddWithValue("@responseHash", responseHash);
                c.Parameters.AddWithValue("@responseReference", (object?)responseReference ?? DBNull.Value);
            }).GetAwaiter().GetResult();
        return updated > 0;
    }

    private static IdempotencyRecord MapIdempotency(NpgsqlDataReader reader)
    {
        static DateTimeOffset ReadOffset(NpgsqlDataReader r, int ordinal)
        {
            var value = r.GetValue(ordinal);
            if (value is DateTimeOffset dto) return dto;
            if (value is DateTime dt) return new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc));
            return new DateTimeOffset(Convert.ToDateTime(value, CultureInfo.InvariantCulture), TimeSpan.Zero);
        }

        return new IdempotencyRecord(
            reader.GetInt64(0),
            reader.GetInt64(1).ToString(),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.GetString(7),
            ReadOffset(reader, 8),
            ReadOffset(reader, 9));
    }
}

public sealed class PostgresAiFoundationService(Database db, ICorrelationContext? correlation = null)
{
    public AiReasoningRunRecord StartReasoningRun(string tenantId, string triggerType, string inputJson, string promptTemplate, string expectedSchemaJson, string? correlationId = null, string? causationId = null)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var row = db.QuerySingleAsync(
            @"INSERT INTO ai_reasoning_runs
                (tenant_id, trigger_type, input_json, prompt_template, expected_schema_json, status, correlation_id, causation_id, started_at)
              VALUES
                (@tenantId, @triggerType, COALESCE(@input::jsonb, '{}'::jsonb), @promptTemplate, COALESCE(@schema::jsonb, '{}'::jsonb), 'started', @correlationId, @causationId, @startedAt)
              RETURNING id",
            c =>
            {
                c.Parameters.AddWithValue("@tenantId", FoundationPersistenceHelpers.RequireTenantId(tenantId));
                c.Parameters.AddWithValue("@triggerType", triggerType);
                c.Parameters.AddWithValue("@input", inputJson);
                c.Parameters.AddWithValue("@promptTemplate", promptTemplate);
                c.Parameters.AddWithValue("@schema", expectedSchemaJson);
                c.Parameters.AddWithValue("@correlationId", (object?)correlationId ?? DBNull.Value);
                c.Parameters.AddWithValue("@causationId", (object?)causationId ?? DBNull.Value);
                c.Parameters.AddWithValue("@startedAt", startedAt);
            }).GetAwaiter().GetResult();

        var id = row is null ? 0L : Convert.ToInt64(row["id"]);
        return new AiReasoningRunRecord(id, tenantId, triggerType, inputJson, promptTemplate, expectedSchemaJson, "started", null, null, null, correlationId, causationId, startedAt);
    }

    public AiReasoningRunRecord CompleteReasoningRun(AiReasoningRunRecord run, string outputJson, decimal confidenceScore)
    {
        var completedAt = DateTimeOffset.UtcNow;
        db.ExecuteAsync(
            @"UPDATE ai_reasoning_runs
              SET status='completed', confidence_score=@score, output_json=COALESCE(@output::jsonb, output_json), completed_at=@completedAt
              WHERE id=@id AND tenant_id=@tenantId",
            c =>
            {
                c.Parameters.AddWithValue("@id", run.Id);
                c.Parameters.AddWithValue("@tenantId", FoundationPersistenceHelpers.RequireTenantId(run.TenantId));
                c.Parameters.AddWithValue("@score", confidenceScore);
                c.Parameters.AddWithValue("@output", outputJson);
                c.Parameters.AddWithValue("@completedAt", completedAt);
            }).GetAwaiter().GetResult();
        return run with { Status = "completed", OutputJson = outputJson, ConfidenceScore = confidenceScore, CompletedAt = completedAt };
    }

    public AiReasoningRunRecord FailReasoningRun(AiReasoningRunRecord run, string errorJson)
    {
        var completedAt = DateTimeOffset.UtcNow;
        db.ExecuteAsync(
            @"UPDATE ai_reasoning_runs
              SET status='failed', error_json=COALESCE(@error::jsonb, error_json), completed_at=@completedAt
              WHERE id=@id AND tenant_id=@tenantId",
            c =>
            {
                c.Parameters.AddWithValue("@id", run.Id);
                c.Parameters.AddWithValue("@tenantId", FoundationPersistenceHelpers.RequireTenantId(run.TenantId));
                c.Parameters.AddWithValue("@error", errorJson);
                c.Parameters.AddWithValue("@completedAt", completedAt);
            }).GetAwaiter().GetResult();
        return run with { Status = "failed", ErrorJson = errorJson, CompletedAt = completedAt };
    }

    public AiRecommendationRecord CreateRecommendation(string tenantId, string recommendationType, string title, string summary, decimal confidenceScore, decimal urgencyScore, string impactJson, string reasonJson, string proposedActionJson, string riskLevel, string? sourceEventId = null, string? actorType = null, string? actorId = null, string status = "draft")
    {
        var createdAt = DateTimeOffset.UtcNow;
        var row = db.QuerySingleAsync(
            @"INSERT INTO ai_recommendations
                (company_id, tenant_id, recommendation_type, title, summary, confidence_score, urgency_score, impact_json, reason_json, proposed_action_json, risk_level, status, source_event_id, actor_type, actor_id, created_at, correlation_id, causation_id)
              VALUES
                (@tenantId::bigint, @tenantId, @recommendationType, @title, @summary, @confidenceScore, @urgencyScore, COALESCE(@impact::jsonb, '{}'::jsonb), COALESCE(@reason::jsonb, '{}'::jsonb), COALESCE(@proposal::jsonb, '{}'::jsonb), @riskLevel, @status, @sourceEventId, @actorType, @actorId, @createdAt, @correlationId, @causationId)
              RETURNING id",
            c =>
            {
                c.Parameters.AddWithValue("@tenantId", FoundationPersistenceHelpers.RequireTenantId(tenantId));
                c.Parameters.AddWithValue("@recommendationType", recommendationType);
                c.Parameters.AddWithValue("@title", title);
                c.Parameters.AddWithValue("@summary", summary);
                c.Parameters.AddWithValue("@confidenceScore", confidenceScore);
                c.Parameters.AddWithValue("@urgencyScore", urgencyScore);
                c.Parameters.AddWithValue("@impact", impactJson);
                c.Parameters.AddWithValue("@reason", reasonJson);
                c.Parameters.AddWithValue("@proposal", proposedActionJson);
                c.Parameters.AddWithValue("@riskLevel", riskLevel);
                c.Parameters.AddWithValue("@status", status);
                c.Parameters.AddWithValue("@sourceEventId", (object?)sourceEventId ?? DBNull.Value);
                c.Parameters.AddWithValue("@actorType", (object?)actorType ?? DBNull.Value);
                c.Parameters.AddWithValue("@actorId", (object?)actorId ?? DBNull.Value);
                c.Parameters.AddWithValue("@createdAt", createdAt);
                c.Parameters.AddWithValue("@correlationId", (object?)correlation?.CorrelationId ?? DBNull.Value);
                c.Parameters.AddWithValue("@causationId", (object?)correlation?.CausationId ?? DBNull.Value);
            }).GetAwaiter().GetResult();
        var id = row is null ? 0L : Convert.ToInt64(row["id"]);
        return new AiRecommendationRecord(id, tenantId, recommendationType, title, summary, confidenceScore, urgencyScore, impactJson, reasonJson, proposedActionJson, riskLevel, status, sourceEventId, actorType, actorId, createdAt, correlation?.CorrelationId, correlation?.CausationId);
    }

    public AiActionRequestRecord CreateActionRequest(string tenantId, long recommendationId, string actionKey, string resourceType, string? resourceId, string payloadJson, string riskLevel, string? requestedByActorType = null, string? requestedByActorId = null, bool requiresApproval = true)
    {
        var requestedAt = DateTimeOffset.UtcNow;
        var row = db.QuerySingleAsync(
            @"INSERT INTO ai_action_requests
                (tenant_id, recommendation_id, action_key, resource_type, resource_id, payload_json, risk_level, status, requested_by_actor_type, requested_by_actor_id, requested_at, correlation_id, causation_id)
              VALUES
                (@tenantId, @recommendationId, @actionKey, @resourceType, @resourceId, COALESCE(@payload::jsonb, '{}'::jsonb), @riskLevel, @status, @requestedByActorType, @requestedByActorId, @requestedAt, @correlationId, @causationId)
              RETURNING id",
            c =>
            {
                c.Parameters.AddWithValue("@tenantId", FoundationPersistenceHelpers.RequireTenantId(tenantId));
                c.Parameters.AddWithValue("@recommendationId", recommendationId);
                c.Parameters.AddWithValue("@actionKey", actionKey);
                c.Parameters.AddWithValue("@resourceType", resourceType);
                c.Parameters.AddWithValue("@resourceId", (object?)resourceId ?? DBNull.Value);
                c.Parameters.AddWithValue("@payload", payloadJson);
                c.Parameters.AddWithValue("@riskLevel", riskLevel);
                c.Parameters.AddWithValue("@status", requiresApproval ? "approval_required" : "pending");
                c.Parameters.AddWithValue("@requestedByActorType", (object?)requestedByActorType ?? DBNull.Value);
                c.Parameters.AddWithValue("@requestedByActorId", (object?)requestedByActorId ?? DBNull.Value);
                c.Parameters.AddWithValue("@requestedAt", requestedAt);
                c.Parameters.AddWithValue("@correlationId", (object?)correlation?.CorrelationId ?? DBNull.Value);
                c.Parameters.AddWithValue("@causationId", (object?)correlation?.CausationId ?? DBNull.Value);
            }).GetAwaiter().GetResult();
        var id = row is null ? 0L : Convert.ToInt64(row["id"]);
        return new AiActionRequestRecord(id, tenantId, recommendationId, actionKey, resourceType, resourceId, payloadJson, riskLevel, requiresApproval ? "approval_required" : "pending", requestedByActorType, requestedByActorId, requestedAt, correlation?.CorrelationId, correlation?.CausationId);
    }

    public AiActionOutcomeRecord RecordOutcome(string tenantId, long actionRequestId, string status, string? outcomeJson = null)
    {
        var recordedAt = DateTimeOffset.UtcNow;
        var row = db.QuerySingleAsync(
            @"INSERT INTO ai_action_outcomes
                (tenant_id, action_request_id, status, outcome_json, recorded_at, correlation_id, causation_id)
              VALUES
                (@tenantId, @actionRequestId, @status, CASE WHEN @outcome IS NULL THEN NULL ELSE @outcome::jsonb END, @recordedAt, @correlationId, @causationId)
              RETURNING id",
            c =>
            {
                c.Parameters.AddWithValue("@tenantId", FoundationPersistenceHelpers.RequireTenantId(tenantId));
                c.Parameters.AddWithValue("@actionRequestId", actionRequestId);
                c.Parameters.AddWithValue("@status", status);
                c.Parameters.AddWithValue("@outcome", (object?)outcomeJson ?? DBNull.Value);
                c.Parameters.AddWithValue("@recordedAt", recordedAt);
                c.Parameters.AddWithValue("@correlationId", (object?)correlation?.CorrelationId ?? DBNull.Value);
                c.Parameters.AddWithValue("@causationId", (object?)correlation?.CausationId ?? DBNull.Value);
            }).GetAwaiter().GetResult();
        var id = row is null ? 0L : Convert.ToInt64(row["id"]);
        return new AiActionOutcomeRecord(id, tenantId, actionRequestId, status, outcomeJson, recordedAt);
    }
}

internal static class FoundationPersistenceHelpers
{
    public static long RequireTenantId(string tenantId)
        => long.TryParse(tenantId, out var id) && id > 0
            ? id
            : throw new InvalidOperationException("Invalid tenant id.");

    public static string ComputeHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
