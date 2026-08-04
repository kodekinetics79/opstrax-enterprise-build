using System.Globalization;
using Npgsql;
using NpgsqlTypes;
using Opstrax.Api.Data;

namespace Opstrax.Api.Foundation;

public sealed class PostgresEventProcessingLogService(Database db) : IEventProcessingLogService
{
    public EventProcessingLogRecord Record(
        string tenantId,
        string eventType,
        string processor,
        string status,
        string? message = null,
        string? correlationId = null,
        string? causationId = null,
        int retryCount = 0)
    {
        var tenant = FoundationPersistenceHelpers.RequireTenantId(tenantId);
        var processedAt = DateTimeOffset.UtcNow;
        var row = db.QuerySingleAsync(
            @"INSERT INTO event_processing_logs
                (tenant_id, event_type, processor, status, message, correlation_id, causation_id, processed_at, retry_count)
              VALUES
                (@tenantId, @eventType, @processor, @status, @message, @correlationId, @causationId, @processedAt, @retryCount)
              RETURNING id",
            c =>
            {
                c.Parameters.AddWithValue("@tenantId", tenant);
                c.Parameters.AddWithValue("@eventType", eventType);
                c.Parameters.AddWithValue("@processor", processor);
                c.Parameters.AddWithValue("@status", status);
                c.Parameters.AddWithValue("@message", (object?)message ?? DBNull.Value);
                c.Parameters.AddWithValue("@correlationId", (object?)correlationId ?? DBNull.Value);
                c.Parameters.AddWithValue("@causationId", (object?)causationId ?? DBNull.Value);
                c.Parameters.AddWithValue("@processedAt", processedAt);
                c.Parameters.AddWithValue("@retryCount", retryCount);
            }).GetAwaiter().GetResult();

        var id = row is null ? 0L : Convert.ToInt64(row["id"], CultureInfo.InvariantCulture);
        return new EventProcessingLogRecord(id, tenant.ToString(CultureInfo.InvariantCulture), eventType, processor, status, message, correlationId, causationId, processedAt, retryCount);
    }
}

public sealed class OutboxMessageHandlerRegistry(IEnumerable<IOutboxMessageHandler> handlers) : IOutboxMessageHandlerRegistry
{
    // Group (not ToDictionary): an event type may legitimately have several derive-beside consumers
    // (invoice.issued -> rev-rec AND general ledger). Registration order is preserved within a group.
    private readonly IReadOnlyDictionary<string, IReadOnlyList<IOutboxMessageHandler>> _handlers =
        handlers.GroupBy(handler => handler.EventType, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<IOutboxMessageHandler>)g.ToList(), StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> RegisteredEventTypes => _handlers.Keys.ToArray();

    public IOutboxMessageHandler? Resolve(string eventType)
        => _handlers.TryGetValue(eventType, out var list) && list.Count > 0 ? list[0] : null;

    public IReadOnlyList<IOutboxMessageHandler> ResolveAll(string eventType)
        => _handlers.TryGetValue(eventType, out var list) ? list : Array.Empty<IOutboxMessageHandler>();
}

public sealed class FoundationSmokeRequestedHandler(
    PostgresAiFoundationService ai,
    IApprovalWorkflowService approval) : IOutboxMessageHandler
{
    public string EventType => "foundation.smoke.requested";

    public Task HandleAsync(OutboxMessageRecord message, CancellationToken ct = default)
    {
        var recommendation = ai.CreateRecommendation(
            message.TenantId,
            "foundation.smoke",
            "Foundation smoke request",
            "Runtime dispatcher processed the local foundation smoke event.",
            0.96m,
            0.81m,
            message.PayloadJson,
            "{\"reason\":\"dispatcher smoke\"}",
            "{\"action\":\"create_approval_request\"}",
            "high",
            message.Id.ToString(CultureInfo.InvariantCulture),
            ActorTypes.System,
            "foundation-dispatcher",
            status: "active");

        var actionRequest = ai.CreateActionRequest(
            message.TenantId,
            recommendation.Id,
            "ai.action.execute_external",
            "foundation_smoke",
            message.AggregateId,
            "{\"source\":\"dispatcher\",\"event\":\"foundation.smoke.requested\"}",
            "high",
            ActorTypes.System,
            "foundation-dispatcher",
            requiresApproval: true);

        if (ApprovalPolicyCatalog.RequiresApproval(actionRequest.ActionKey))
        {
            approval.CreateRequest(
                message.TenantId,
                ActorTypes.System,
                "foundation-dispatcher",
                actionRequest.ActionKey,
                actionRequest.ResourceType,
                actionRequest.ResourceId,
                actionRequest.PayloadJson,
                actionRequest.RiskLevel);
        }

        return Task.CompletedTask;
    }
}

public sealed class PostgresOutboxDispatcher(
    Database db,
    IOutboxMessageHandlerRegistry outboxHandlers,
    IEventProcessingLogService eventLogs,
    OutboxDispatcherOptions options) : IOutboxDispatcher
{
    public async Task<int> DispatchOutboxOnceAsync(CancellationToken ct = default)
    {
        var claimed = await ClaimOutboxAsync(ct);
        var processed = 0;

        foreach (var message in claimed)
        {
            ct.ThrowIfCancellationRequested();
            using var correlation = AmbientCorrelationContext.Begin(
                message.CorrelationId,
                message.CausationId,
                $"outbox:{message.Id}",
                message.TenantId,
                ActorTypes.System,
                options.WorkerName);

            var handlers = outboxHandlers.ResolveAll(message.EventType);
            if (handlers.Count == 0)
            {
                await HandleOutboxFailureAsync(message, new InvalidOperationException($"No handler registered for {message.EventType}"), ct);
                continue;
            }

            try
            {
                // Fan-out: run every registered handler for this event type; the message is marked
                // processed only after ALL succeed. On any failure the whole message goes to retry,
                // re-running already-succeeded handlers — safe because every handler is idempotent.
                // The failing handler's type name is preserved in last_error for audit.
                foreach (var handler in handlers)
                {
                    try
                    {
                        await handler.HandleAsync(message, ct);
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException($"{handler.GetType().Name}: {ex.Message}", ex);
                    }
                }
                await MarkOutboxProcessedAsync(message, ct);
                eventLogs.Record(message.TenantId, message.EventType, options.WorkerName, "success", null, message.CorrelationId, message.CausationId, message.RetryCount);
                processed++;
            }
            catch (Exception ex)
            {
                await HandleOutboxFailureAsync(message, ex, ct);
            }
        }

        return processed;
    }

    public async Task<int> DispatchInboxOnceAsync(CancellationToken ct = default)
    {
        var claimed = await ClaimInboxAsync(ct);
        var processed = 0;

        foreach (var message in claimed)
        {
            ct.ThrowIfCancellationRequested();
            using var correlation = AmbientCorrelationContext.Begin(
                message.CorrelationId,
                message.CausationId,
                $"inbox:{message.Id}",
                message.TenantId,
                ActorTypes.System,
                options.WorkerName);

            try
            {
                await MarkInboxProcessedAsync(message, ct);
                eventLogs.Record(message.TenantId, message.EventType, options.WorkerName, "success", "Inbox message processed", message.CorrelationId, message.CausationId, message.RetryCount);
                processed++;
            }
            catch (Exception ex)
            {
                await HandleInboxFailureAsync(message, ex, ct);
            }
        }

        return processed;
    }

    private async Task<List<OutboxMessageRecord>> ClaimOutboxAsync(CancellationToken ct)
        => await db.WithTransactionAsync(async (conn, tx) =>
        {
            var ids = new List<long>();
            await using (var select = new NpgsqlCommand(
                @"SELECT id
                  FROM outbox_messages
                  WHERE status IN ('pending', 'retry_pending')
                    AND (next_attempt_at IS NULL OR next_attempt_at <= NOW())
                    AND (locked_until IS NULL OR locked_until < NOW())
                    AND (@tenantIdFilter IS NULL OR tenant_id = @tenantIdFilter)
                  ORDER BY created_at, id
                  LIMIT @limit
                  FOR UPDATE SKIP LOCKED",
                conn, tx))
            {
                select.Parameters.AddWithValue("@limit", options.BatchSize);
                select.Parameters.Add(new NpgsqlParameter("@tenantIdFilter", NpgsqlDbType.Bigint) { Value = (object?)options.TenantIdFilter ?? DBNull.Value });
                await using var reader = await select.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                    ids.Add(reader.GetInt64(0));
            }

            if (ids.Count == 0)
                return [];

            await using var cmd = new NpgsqlCommand(
                @"UPDATE outbox_messages
                  SET status='processing',
                      claimed_at=NOW(),
                      claimed_by=@worker,
                      locked_until=NOW() + (@timeoutSeconds || ' seconds')::interval,
                      last_error=NULL,
                      dead_letter_reason=NULL
                  WHERE id = ANY(@ids)
                  RETURNING id, tenant_id, event_type, aggregate_type, aggregate_id, payload_json,
                            correlation_id, causation_id, idempotency_key, created_at, status, retry_count,
                            next_attempt_at, processed_at",
                conn, tx);
            cmd.Parameters.AddWithValue("@worker", options.WorkerName);
            cmd.Parameters.AddWithValue("@timeoutSeconds", options.ProcessingTimeoutSeconds);
            cmd.Parameters.AddWithValue("@ids", ids.ToArray());

            var rows = new List<OutboxMessageRecord>();
            await using var reader2 = await cmd.ExecuteReaderAsync(ct);
            while (await reader2.ReadAsync(ct))
                rows.Add(ReadOutbox(reader2));
            return rows;
        }, ct);

    private async Task<List<InboxMessageRecord>> ClaimInboxAsync(CancellationToken ct)
        => await db.WithTransactionAsync(async (conn, tx) =>
        {
            var ids = new List<long>();
            await using (var select = new NpgsqlCommand(
                @"SELECT id
                  FROM inbox_messages
                  WHERE status IN ('received', 'retry_pending')
                    AND (locked_until IS NULL OR locked_until < NOW())
                    AND (@tenantIdFilter IS NULL OR tenant_id = @tenantIdFilter)
                  ORDER BY received_at, id
                  LIMIT @limit
                  FOR UPDATE SKIP LOCKED",
                conn, tx))
            {
                select.Parameters.AddWithValue("@limit", options.BatchSize);
                select.Parameters.Add(new NpgsqlParameter("@tenantIdFilter", NpgsqlDbType.Bigint) { Value = (object?)options.TenantIdFilter ?? DBNull.Value });
                await using var reader = await select.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                    ids.Add(reader.GetInt64(0));
            }

            if (ids.Count == 0)
                return [];

            await using var cmd = new NpgsqlCommand(
                @"UPDATE inbox_messages
                  SET status='processing',
                      claimed_at=NOW(),
                      claimed_by=@worker,
                      locked_until=NOW() + (@timeoutSeconds || ' seconds')::interval,
                      last_error=NULL,
                      dead_letter_reason=NULL
                  WHERE id = ANY(@ids)
                  RETURNING id, tenant_id, event_type, source, external_id, payload_json,
                            correlation_id, causation_id, status, received_at, idempotency_key,
                            payload_hash, retry_count, processed_at",
                conn, tx);
            cmd.Parameters.AddWithValue("@worker", options.WorkerName);
            cmd.Parameters.AddWithValue("@timeoutSeconds", options.ProcessingTimeoutSeconds);
            cmd.Parameters.AddWithValue("@ids", ids.ToArray());

            var rows = new List<InboxMessageRecord>();
            await using var reader2 = await cmd.ExecuteReaderAsync(ct);
            while (await reader2.ReadAsync(ct))
                rows.Add(ReadInbox(reader2));
            return rows;
        }, ct);

    private async Task MarkOutboxProcessedAsync(OutboxMessageRecord message, CancellationToken ct)
    {
        await db.ExecuteAsync(
            @"UPDATE outbox_messages
              SET status='processed',
                  processed_at=NOW(),
                  locked_until=NULL,
                  last_error=NULL,
                  dead_letter_reason=NULL
              WHERE id=@id AND tenant_id=@tenantId",
            c =>
            {
                c.Parameters.AddWithValue("@id", message.Id);
                c.Parameters.AddWithValue("@tenantId", FoundationPersistenceHelpers.RequireTenantId(message.TenantId));
            }, ct);
    }

    private async Task HandleOutboxFailureAsync(OutboxMessageRecord message, Exception ex, CancellationToken ct)
    {
        var nextRetryCount = message.RetryCount + 1;
        var terminal = nextRetryCount >= options.MaxRetryCount;
        var nextAttemptAt = terminal
            ? (DateTimeOffset?)null
            : DateTimeOffset.UtcNow.AddSeconds(options.RetryBackoffSeconds * Math.Pow(2, Math.Max(0, nextRetryCount - 1)));

        await db.ExecuteAsync(
            @"UPDATE outbox_messages
              SET status=@status,
                  retry_count=@retryCount,
                  next_attempt_at=@nextAttemptAt,
                  last_error=@lastError,
                  dead_letter_reason=@deadLetterReason,
                  processed_at=@processedAt,
                  locked_until=NULL
              WHERE id=@id AND tenant_id=@tenantId",
            c =>
            {
                c.Parameters.AddWithValue("@status", terminal ? "dead_letter" : "retry_pending");
                c.Parameters.AddWithValue("@retryCount", nextRetryCount);
                c.Parameters.AddWithValue("@nextAttemptAt", (object?)nextAttemptAt ?? DBNull.Value);
                c.Parameters.AddWithValue("@lastError", ex.Message);
                c.Parameters.AddWithValue("@deadLetterReason", terminal ? ex.Message : DBNull.Value);
                c.Parameters.AddWithValue("@processedAt", terminal ? DateTimeOffset.UtcNow : DBNull.Value);
                c.Parameters.AddWithValue("@id", message.Id);
                c.Parameters.AddWithValue("@tenantId", FoundationPersistenceHelpers.RequireTenantId(message.TenantId));
            }, ct);

        eventLogs.Record(
            message.TenantId,
            message.EventType,
            options.WorkerName,
            terminal ? "dead_letter" : "failure",
            ex.Message,
            message.CorrelationId,
            message.CausationId,
            nextRetryCount);
    }

    private async Task MarkInboxProcessedAsync(InboxMessageRecord message, CancellationToken ct)
    {
        await db.ExecuteAsync(
            @"UPDATE inbox_messages
              SET status='processed',
                  processed_at=NOW(),
                  locked_until=NULL,
                  last_error=NULL,
                  dead_letter_reason=NULL
              WHERE id=@id AND tenant_id=@tenantId",
            c =>
            {
                c.Parameters.AddWithValue("@id", message.Id);
                c.Parameters.AddWithValue("@tenantId", FoundationPersistenceHelpers.RequireTenantId(message.TenantId));
            }, ct);
    }

    private async Task HandleInboxFailureAsync(InboxMessageRecord message, Exception ex, CancellationToken ct)
    {
        var nextRetryCount = message.RetryCount + 1;
        var terminal = nextRetryCount >= options.MaxRetryCount;
        var nextAttemptAt = terminal
            ? (DateTimeOffset?)null
            : DateTimeOffset.UtcNow.AddSeconds(options.RetryBackoffSeconds * Math.Pow(2, Math.Max(0, nextRetryCount - 1)));

        await db.ExecuteAsync(
            @"UPDATE inbox_messages
              SET status=@status,
                  retry_count=@retryCount,
                  next_attempt_at=@nextAttemptAt,
                  last_error=@lastError,
                  dead_letter_reason=@deadLetterReason,
                  processed_at=@processedAt,
                  locked_until=NULL
              WHERE id=@id AND tenant_id=@tenantId",
            c =>
            {
                c.Parameters.AddWithValue("@status", terminal ? "dead_letter" : "retry_pending");
                c.Parameters.AddWithValue("@retryCount", nextRetryCount);
                c.Parameters.AddWithValue("@nextAttemptAt", (object?)nextAttemptAt ?? DBNull.Value);
                c.Parameters.AddWithValue("@lastError", ex.Message);
                c.Parameters.AddWithValue("@deadLetterReason", terminal ? ex.Message : DBNull.Value);
                c.Parameters.AddWithValue("@processedAt", terminal ? DateTimeOffset.UtcNow : DBNull.Value);
                c.Parameters.AddWithValue("@id", message.Id);
                c.Parameters.AddWithValue("@tenantId", FoundationPersistenceHelpers.RequireTenantId(message.TenantId));
            }, ct);

        eventLogs.Record(
            message.TenantId,
            message.EventType,
            options.WorkerName,
            terminal ? "dead_letter" : "failure",
            ex.Message,
            message.CorrelationId,
            message.CausationId,
            nextRetryCount);
    }

    private static OutboxMessageRecord ReadOutbox(NpgsqlDataReader reader)
    {
        return new OutboxMessageRecord(
            reader.GetInt64(0),
            reader.GetInt64(1).ToString(CultureInfo.InvariantCulture),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            ReadString(reader, 5),
            ReadString(reader, 6),
            ReadString(reader, 7),
            ReadString(reader, 8),
            ReadOffset(reader, 9),
            reader.GetString(10),
            reader.GetInt32(11),
            reader.IsDBNull(12) ? null : ReadOffset(reader, 12),
            reader.IsDBNull(13) ? null : ReadOffset(reader, 13));
    }

    private static InboxMessageRecord ReadInbox(NpgsqlDataReader reader)
    {
        return new InboxMessageRecord(
            reader.GetInt64(0),
            reader.GetInt64(1).ToString(CultureInfo.InvariantCulture),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            ReadString(reader, 5),
            ReadString(reader, 6),
            ReadString(reader, 7),
            reader.GetString(8),
            ReadOffset(reader, 9),
            ReadString(reader, 10),
            ReadString(reader, 11),
            reader.GetInt32(12),
            reader.IsDBNull(13) ? null : ReadOffset(reader, 13));
    }

    private static string? ReadString(NpgsqlDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture);

    private static DateTimeOffset ReadOffset(NpgsqlDataReader reader, int ordinal)
    {
        var value = reader.GetValue(ordinal);
        if (value is DateTimeOffset dto) return dto;
        if (value is DateTime dt) return new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc));
        return new DateTimeOffset(Convert.ToDateTime(value, CultureInfo.InvariantCulture), TimeSpan.Zero);
    }
}
