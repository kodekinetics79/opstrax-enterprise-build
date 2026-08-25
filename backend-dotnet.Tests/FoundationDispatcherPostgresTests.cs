using Microsoft.Extensions.Configuration;
using Opstrax.Api.Data;
using Opstrax.Api.Foundation;

namespace Opstrax.Tests;

public class FoundationDispatcherPostgresTests
{
    private static readonly string LocalConnectionString = TestDb.ConnectionString;

    [Fact]
    public async Task OutboxDispatcher_Processes_FoundationSmokeRequested_Event()
    {
        var db = CreateDatabase();
        var tenantId = NextTenantId();
        var correlationId = $"dispatcher-{Guid.NewGuid():N}";
        var ambient = new AmbientCorrelationContext();
        var ai = new PostgresAiFoundationService(db, ambient);
        var approval = new PostgresApprovalWorkflowService(db, ambient);
        var logs = new PostgresEventProcessingLogService(db);
        var handler = new FoundationSmokeRequestedHandler(ai, approval);
        var tenantIdLong = long.Parse(tenantId);
        var dispatcher = new PostgresOutboxDispatcher(db, new OutboxMessageHandlerRegistry([handler]), logs, new OutboxDispatcherOptions { Enabled = true, BatchSize = 5, WorkerName = "dispatcher-test", TenantIdFilter = tenantIdLong });

        try
        {
            var publisher = new PostgresDomainEventPublisher(db, ambient);
            _ = publisher.Publish(tenantId, "foundation.smoke.requested", "foundation_smoke", "smoke-1", "{\"scenario\":\"dispatch-smoke\"}", correlationId, "cause-1", "dispatch-smoke-1");

            var processed = await dispatcher.DispatchOutboxOnceAsync();

            Assert.Equal(1, processed);
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM outbox_messages WHERE tenant_id=@tenantId AND event_type='foundation.smoke.requested' AND correlation_id=@corr AND status='processed' AND claimed_by='dispatcher-test' AND claimed_at IS NOT NULL AND processed_at IS NOT NULL", c =>
            {
                c.Parameters.AddWithValue("@tenantId", tenantIdLong);
                c.Parameters.AddWithValue("@corr", correlationId);
            }));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM ai_recommendations WHERE tenant_id=@tenantId AND correlation_id=@corr AND status='active'", c =>
            {
                c.Parameters.AddWithValue("@tenantId", tenantIdLong);
                c.Parameters.AddWithValue("@corr", correlationId);
            }));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM ai_action_requests WHERE tenant_id=@tenantId AND correlation_id=@corr AND status='approval_required'", c =>
            {
                c.Parameters.AddWithValue("@tenantId", tenantIdLong);
                c.Parameters.AddWithValue("@corr", correlationId);
            }));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM approval_requests WHERE tenant_id=@tenantId AND correlation_id=@corr AND action_key='ai.action.execute_external'", c =>
            {
                c.Parameters.AddWithValue("@tenantId", tenantIdLong);
                c.Parameters.AddWithValue("@corr", correlationId);
            }));
            Assert.Equal(0, await db.ScalarLongAsync("SELECT COUNT(*) FROM ai_action_outcomes WHERE tenant_id=@tenantId", c => c.Parameters.AddWithValue("@tenantId", tenantIdLong)));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM event_processing_logs WHERE tenant_id=@tenantId AND event_type='foundation.smoke.requested' AND status='success'", c =>
            {
                c.Parameters.AddWithValue("@tenantId", tenantIdLong);
            }));
        }
        finally
        {
            await CleanupTenantAsync(db, long.Parse(tenantId));
        }
    }

    [Fact]
    public async Task OutboxDispatcher_MarksFailure_And_SchedulesRetry()
    {
        var db = CreateDatabase();
        var tenantId = NextTenantId();
        var ambient = new AmbientCorrelationContext();
        var logs = new PostgresEventProcessingLogService(db);
        var tenantIdLong = long.Parse(tenantId);
        var dispatcher = new PostgresOutboxDispatcher(db, new OutboxMessageHandlerRegistry([new ThrowingOutboxHandler("foundation.smoke.fail")]), logs, new OutboxDispatcherOptions { Enabled = true, BatchSize = 5, MaxRetryCount = 3, RetryBackoffSeconds = 5, WorkerName = "dispatcher-test", TenantIdFilter = tenantIdLong });

        try
        {
            var publisher = new PostgresDomainEventPublisher(db, ambient);
            _ = publisher.Publish(tenantId, "foundation.smoke.fail", "foundation_smoke", "smoke-2", "{\"scenario\":\"retry\"}", "corr-retry", "cause-retry", "retry-1");

            var processed = await dispatcher.DispatchOutboxOnceAsync();

            Assert.Equal(0, processed);
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM outbox_messages WHERE tenant_id=@tenantId AND event_type='foundation.smoke.fail' AND correlation_id='corr-retry' AND status='retry_pending' AND retry_count=1 AND next_attempt_at IS NOT NULL AND last_error IS NOT NULL", c =>
            {
                c.Parameters.AddWithValue("@tenantId", tenantIdLong);
            }));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM event_processing_logs WHERE tenant_id=@tenantId AND event_type='foundation.smoke.fail' AND status='failure'", c =>
            {
                c.Parameters.AddWithValue("@tenantId", tenantIdLong);
            }));
        }
        finally
        {
            await CleanupTenantAsync(db, long.Parse(tenantId));
        }
    }

    [Fact]
    public async Task OutboxDispatcher_Reclaims_Expired_Processing_Lease()
    {
        var db = CreateDatabase();
        var tenantId = NextTenantId();
        var tenantIdLong = long.Parse(tenantId);
        var ambient = new AmbientCorrelationContext();
        var handler = new FoundationSmokeRequestedHandler(
            new PostgresAiFoundationService(db, ambient),
            new PostgresApprovalWorkflowService(db, ambient));
        var dispatcher = new PostgresOutboxDispatcher(
            db,
            new OutboxMessageHandlerRegistry([handler]),
            new PostgresEventProcessingLogService(db),
            new OutboxDispatcherOptions
            {
                Enabled = true,
                BatchSize = 5,
                MaxRetryCount = 3,
                WorkerName = "dispatcher-recovery-test",
                TenantIdFilter = tenantIdLong,
            });

        try
        {
            var message = new PostgresDomainEventPublisher(db, ambient).Write(
                tenantId, "foundation.smoke.requested", "foundation_smoke", "smoke-recovery",
                "{\"scenario\":\"lease-recovery\"}", "corr-recovery", "cause-recovery", "recovery-1");
            await db.ExecuteAsync(
                @"UPDATE outbox_messages
                  SET status='processing', claimed_by='crashed-worker', claimed_at=NOW() - INTERVAL '2 minutes',
                      locked_until=NOW() - INTERVAL '1 minute'
                  WHERE id=@id AND tenant_id=@tenantId",
                c =>
                {
                    c.Parameters.AddWithValue("@id", message.Id);
                    c.Parameters.AddWithValue("@tenantId", tenantIdLong);
                });

            var processed = await dispatcher.DispatchOutboxOnceAsync();

            var recoveryState = await db.QuerySingleAsync(
                "SELECT status, retry_count, last_error, claimed_by FROM outbox_messages WHERE id=@id AND tenant_id=@tenantId",
                c =>
                {
                    c.Parameters.AddWithValue("@id", message.Id);
                    c.Parameters.AddWithValue("@tenantId", tenantIdLong);
                });
            Assert.True(processed == 1,
                $"Recovered message was not processed: status={recoveryState?["status"]}, retry={recoveryState?["retryCount"]}, error={recoveryState?["lastError"]}, claimant={recoveryState?["claimedBy"]}");
            var persistedRecovery = await db.ScalarLongAsync(
                @"SELECT COUNT(*) FROM outbox_messages
                  WHERE id=@id AND tenant_id=@tenantId AND status='processed' AND retry_count=1
                    AND claimed_by='dispatcher-recovery-test' AND locked_until IS NULL",
                c =>
                {
                    c.Parameters.AddWithValue("@id", message.Id);
                    c.Parameters.AddWithValue("@tenantId", tenantIdLong);
                });
            Assert.True(persistedRecovery == 1,
                $"Recovered row did not retain the expected terminal state: status={recoveryState?["status"]}, retry={recoveryState?["retryCount"]}, error={recoveryState?["lastError"]}, claimant={recoveryState?["claimedBy"]}");
        }
        finally
        {
            await CleanupTenantAsync(db, tenantIdLong);
        }
    }

    [Fact]
    public async Task OutboxDispatcher_DeadLetters_Expired_Lease_At_Retry_Limit()
    {
        var db = CreateDatabase();
        var tenantId = NextTenantId();
        var tenantIdLong = long.Parse(tenantId);
        var ambient = new AmbientCorrelationContext();
        var dispatcher = new PostgresOutboxDispatcher(
            db,
            new OutboxMessageHandlerRegistry(Array.Empty<IOutboxMessageHandler>()),
            new PostgresEventProcessingLogService(db),
            new OutboxDispatcherOptions
            {
                Enabled = true,
                BatchSize = 5,
                MaxRetryCount = 3,
                WorkerName = "dispatcher-recovery-test",
                TenantIdFilter = tenantIdLong,
            });

        try
        {
            var message = new PostgresDomainEventPublisher(db, ambient).Write(
                tenantId, "foundation.smoke.expired", "foundation_smoke", "smoke-expired",
                "{}", "corr-expired", "cause-expired", "expired-1");
            await db.ExecuteAsync(
                @"UPDATE outbox_messages
                  SET status='processing', retry_count=2, claimed_by='crashed-worker',
                      claimed_at=NOW() - INTERVAL '2 minutes', locked_until=NOW() - INTERVAL '1 minute'
                  WHERE id=@id AND tenant_id=@tenantId",
                c =>
                {
                    c.Parameters.AddWithValue("@id", message.Id);
                    c.Parameters.AddWithValue("@tenantId", tenantIdLong);
                });

            var processed = await dispatcher.DispatchOutboxOnceAsync();

            Assert.Equal(0, processed);
            Assert.Equal(1, await db.ScalarLongAsync(
                @"SELECT COUNT(*) FROM outbox_messages
                  WHERE id=@id AND tenant_id=@tenantId AND status='dead_letter' AND retry_count=3
                    AND dead_letter_reason='Processing lease expired before completion'
                    AND locked_until IS NULL AND processed_at IS NOT NULL",
                c =>
                {
                    c.Parameters.AddWithValue("@id", message.Id);
                    c.Parameters.AddWithValue("@tenantId", tenantIdLong);
                }));
        }
        finally
        {
            await CleanupTenantAsync(db, tenantIdLong);
        }
    }

    [Fact]
    public async Task OutboxDispatcher_DeadLetters_When_MaxRetry_Reached()
    {
        var db = CreateDatabase();
        var tenantId = NextTenantId();
        var ambient = new AmbientCorrelationContext();
        var logs = new PostgresEventProcessingLogService(db);
        var tenantIdLong = long.Parse(tenantId);
        var dispatcher = new PostgresOutboxDispatcher(db, new OutboxMessageHandlerRegistry([new ThrowingOutboxHandler("foundation.smoke.dead")]), logs, new OutboxDispatcherOptions { Enabled = true, BatchSize = 5, MaxRetryCount = 1, RetryBackoffSeconds = 1, WorkerName = "dispatcher-test", TenantIdFilter = tenantIdLong });

        try
        {
            var publisher = new PostgresDomainEventPublisher(db, ambient);
            _ = publisher.Publish(tenantId, "foundation.smoke.dead", "foundation_smoke", "smoke-3", "{\"scenario\":\"dead-letter\"}", "corr-dead", "cause-dead", "dead-1");

            await dispatcher.DispatchOutboxOnceAsync();

            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM outbox_messages WHERE tenant_id=@tenantId AND event_type='foundation.smoke.dead' AND correlation_id='corr-dead' AND status='dead_letter' AND retry_count=1 AND dead_letter_reason IS NOT NULL", c =>
            {
                c.Parameters.AddWithValue("@tenantId", tenantIdLong);
            }));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM event_processing_logs WHERE tenant_id=@tenantId AND event_type='foundation.smoke.dead' AND status='dead_letter'", c =>
            {
                c.Parameters.AddWithValue("@tenantId", tenantIdLong);
            }));
        }
        finally
        {
            await CleanupTenantAsync(db, long.Parse(tenantId));
        }
    }

    [Fact]
    public async Task InboxProcessor_Ignores_Duplicate_Message()
    {
        var db = CreateDatabase();
        var tenantId = NextTenantId();
        var ambient = new AmbientCorrelationContext();
        var publisher = new PostgresDomainEventPublisher(db, ambient);

        try
        {
            var first = publisher.Record(tenantId, "foundation.inbox.received", "integration-x", "ext-99", "{\"value\":1}", "corr-inbox", "cause-inbox", "inbox-99");
            var second = publisher.Record(tenantId, "foundation.inbox.received", "integration-x", "ext-99", "{\"value\":1}", "corr-inbox", "cause-inbox", "inbox-99");

            Assert.Equal("received", first.Status);
            Assert.Equal("ignored_duplicate", second.Status);
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM inbox_messages WHERE tenant_id=@tenantId AND source='integration-x' AND external_id='ext-99'", c =>
            {
                c.Parameters.AddWithValue("@tenantId", long.Parse(tenantId));
            }));
            Assert.Equal(2, await db.ScalarLongAsync("SELECT COUNT(*) FROM event_processing_logs WHERE tenant_id=@tenantId AND event_type='foundation.inbox.received'", c =>
            {
                c.Parameters.AddWithValue("@tenantId", long.Parse(tenantId));
            }));
        }
        finally
        {
            await CleanupTenantAsync(db, long.Parse(tenantId));
        }
    }

    [Fact]
    public async Task InboxDispatcher_MarksReceived_Row_Processed()
    {
        var db = CreateDatabase();
        var tenantId = NextTenantId();
        var ambient = new AmbientCorrelationContext();
        var logs = new PostgresEventProcessingLogService(db);
        var tenantIdLong = long.Parse(tenantId);
        var dispatcher = new PostgresOutboxDispatcher(db, new OutboxMessageHandlerRegistry(Array.Empty<IOutboxMessageHandler>()), logs, new OutboxDispatcherOptions { Enabled = true, BatchSize = 5, WorkerName = "dispatcher-test", TenantIdFilter = tenantIdLong });
        var publisher = new PostgresDomainEventPublisher(db, ambient);

        try
        {
            var inbox = publisher.Record(tenantId, "foundation.inbox.received", "integration-y", "ext-100", "{\"value\":2}", "corr-inbox-2", "cause-inbox-2", "inbox-100");

            var processed = await dispatcher.DispatchInboxOnceAsync();

            Assert.Equal(1, processed);
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM inbox_messages WHERE tenant_id=@tenantId AND id=@id AND status='processed' AND claimed_by='dispatcher-test' AND claimed_at IS NOT NULL AND processed_at IS NOT NULL", c =>
            {
                c.Parameters.AddWithValue("@tenantId", tenantIdLong);
                c.Parameters.AddWithValue("@id", inbox.Id);
            }));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM event_processing_logs WHERE tenant_id=@tenantId AND event_type='foundation.inbox.received' AND status='success'", c =>
            {
                c.Parameters.AddWithValue("@tenantId", tenantIdLong);
            }));
        }
        finally
        {
            await CleanupTenantAsync(db, long.Parse(tenantId));
        }
    }

    [Fact]
    public async Task InboxDispatcher_DoesNotClaimRetryBeforeNextAttempt()
    {
        var db = CreateDatabase();
        var tenantId = NextTenantId();
        var tenantIdLong = long.Parse(tenantId);
        var dispatcher = new PostgresOutboxDispatcher(
            db,
            new OutboxMessageHandlerRegistry(Array.Empty<IOutboxMessageHandler>()),
            new PostgresEventProcessingLogService(db),
            new OutboxDispatcherOptions { Enabled = true, BatchSize = 5, WorkerName = "inbox-backoff-test", TenantIdFilter = tenantIdLong });

        try
        {
            var message = new PostgresDomainEventPublisher(db, new AmbientCorrelationContext()).Record(
                tenantId, "foundation.inbox.received", "integration-backoff", "future-1",
                "{}", "corr-inbox-backoff", "cause-inbox-backoff", "inbox-backoff-1");
            await db.ExecuteAsync(
                "UPDATE inbox_messages SET status='retry_pending', retry_count=1, next_attempt_at=NOW() + INTERVAL '5 minutes' WHERE id=@id AND tenant_id=@tenantId",
                c =>
                {
                    c.Parameters.AddWithValue("@id", message.Id);
                    c.Parameters.AddWithValue("@tenantId", tenantIdLong);
                });

            Assert.Equal(0, await dispatcher.DispatchInboxOnceAsync());
            Assert.Equal(1, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM inbox_messages WHERE id=@id AND tenant_id=@tenantId AND status='retry_pending' AND claimed_at IS NULL",
                c =>
                {
                    c.Parameters.AddWithValue("@id", message.Id);
                    c.Parameters.AddWithValue("@tenantId", tenantIdLong);
                }));
        }
        finally
        {
            await CleanupTenantAsync(db, tenantIdLong);
        }
    }

    [Fact]
    public async Task InboxDispatcher_Reclaims_Expired_Processing_Lease()
    {
        var db = CreateDatabase();
        var tenantId = NextTenantId();
        var tenantIdLong = long.Parse(tenantId);
        var ambient = new AmbientCorrelationContext();
        var dispatcher = new PostgresOutboxDispatcher(
            db,
            new OutboxMessageHandlerRegistry(Array.Empty<IOutboxMessageHandler>()),
            new PostgresEventProcessingLogService(db),
            new OutboxDispatcherOptions
            {
                Enabled = true,
                BatchSize = 5,
                MaxRetryCount = 3,
                WorkerName = "inbox-recovery-test",
                TenantIdFilter = tenantIdLong,
            });

        try
        {
            var message = new PostgresDomainEventPublisher(db, ambient).Record(
                tenantId, "foundation.inbox.received", "integration-recovery", "expired-1",
                "{}", "corr-inbox-recovery", "cause-inbox-recovery", "inbox-recovery-1");
            await db.ExecuteAsync(
                @"UPDATE inbox_messages
                  SET status='processing', claimed_by='crashed-worker', claimed_at=NOW() - INTERVAL '2 minutes',
                      locked_until=NOW() - INTERVAL '1 minute'
                  WHERE id=@id AND tenant_id=@tenantId",
                c =>
                {
                    c.Parameters.AddWithValue("@id", message.Id);
                    c.Parameters.AddWithValue("@tenantId", tenantIdLong);
                });

            var processed = await dispatcher.DispatchInboxOnceAsync();

            Assert.Equal(1, processed);
            Assert.Equal(1, await db.ScalarLongAsync(
                @"SELECT COUNT(*) FROM inbox_messages
                  WHERE id=@id AND tenant_id=@tenantId AND status='processed' AND retry_count=1
                    AND claimed_by='inbox-recovery-test' AND locked_until IS NULL",
                c =>
                {
                    c.Parameters.AddWithValue("@id", message.Id);
                    c.Parameters.AddWithValue("@tenantId", tenantIdLong);
                }));
        }
        finally
        {
            await CleanupTenantAsync(db, tenantIdLong);
        }
    }

    [Fact]
    public void MissingTenant_FailsClosed_For_FoundationServices()
    {
        var db = CreateDatabase();
        var approval = new PostgresApprovalWorkflowService(db, new AmbientCorrelationContext());
        var ai = new PostgresAiFoundationService(db, new AmbientCorrelationContext());

        Assert.Throws<InvalidOperationException>(() => approval.CreateRequest("", "tenant_user", "42", "ai.action.execute_external", "resource", "r-1", "{}", "high"));
        Assert.Throws<InvalidOperationException>(() => ai.StartReasoningRun("", "smoke", "{}", "template", "{}", "corr", "cause"));
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

    private static string NextTenantId() => Interlocked.Increment(ref _nextTenantId).ToString();

    private static long _nextTenantId = 53000;

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

    private sealed class ThrowingOutboxHandler(string eventType) : IOutboxMessageHandler
    {
        public string EventType { get; } = eventType;

        public Task HandleAsync(OutboxMessageRecord message, CancellationToken ct = default)
            => throw new InvalidOperationException("Simulated dispatcher failure");
    }
}
