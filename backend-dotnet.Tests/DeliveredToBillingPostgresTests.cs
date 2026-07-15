using System.Reflection;
using Microsoft.Extensions.Configuration;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Foundation;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

// Delivery -> billing automation (ADR-008 §B Phase 2), full chain against real Postgres:
//   delivered transition  ->  durable job.delivered outbox row (idempotent)
//                          ->  outbox dispatcher routes it to JobDeliveredBillingHandler
//                          ->  load is rated + marked ready-to-bill.
// This is the production wiring end-to-end, not the handler in isolation. It proves the enqueue
// idempotency index and the dispatcher/registry routing that JobDeliveredBillingPostgresTests
// (handler-direct) does not exercise.
[Trait("Category", "Integration")]
public class DeliveredToBillingPostgresTests
{
    [Fact]
    public async Task Delivered_Transition_Enqueues_Once_And_Dispatcher_Bills()
    {
        var db = CreateDatabase();
        var cid = await SeedCompanyAsync(db);
        try
        {
            var cardId = await db.InsertAsync(
                @"INSERT INTO rate_cards (company_id, rate_card_code, rate_card_name, billing_basis, base_rate, fuel_surcharge_percent, currency, effective_date, status)
                  VALUES (@c, @code, 'Card', 'Per Mile', 2.00, 0, 'USD', CURRENT_DATE, 'Active') RETURNING id",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@code", $"RC-{cid}"); });
            var jobId = await db.InsertAsync(
                @"INSERT INTO jobs (company_id, job_code, job_type, rate_card_id, status)
                  VALUES (@c, @code, 'freight', @rc, 'in_transit') RETURNING id",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@code", $"J-{cid}"); c.Parameters.AddWithValue("@rc", cardId); });
            await db.ExecuteAsync("INSERT INTO trips (company_id, job_id, actual_distance_miles) VALUES (@c,@j,200)",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@j", jobId); });
            var aid = await db.InsertAsync(
                @"INSERT INTO dispatch_assignments (company_id, job_id, assignment_status, status)
                  VALUES (@c, @j, 'in_transit', 'In Transit') RETURNING id",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@j", jobId); });

            // Fire the delivered transition twice — the production state machine, invoked exactly as
            // the dispatch/driver endpoints invoke it. Two deliveries must enqueue one billing event.
            await InvokeTransitionAsync(db, cid, aid, "delivered");
            await InvokeTransitionAsync(db, cid, aid, "delivered");

            var enqueued = await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM outbox_messages WHERE tenant_id=@t AND event_type='job.delivered' AND aggregate_id=@a",
                c => { c.Parameters.AddWithValue("@t", cid); c.Parameters.AddWithValue("@a", jobId.ToString()); });
            Assert.Equal(1, enqueued);

            // Drain the outbox exactly as the background dispatcher does.
            var dispatcher = new PostgresOutboxDispatcher(
                db,
                new OutboxMessageHandlerRegistry(new IOutboxMessageHandler[]
                {
                    new JobDeliveredBillingHandler(new RatingService(db, new BusinessSpineService(db)), CreateRevenue(db))
                }),
                new PostgresEventProcessingLogService(db),
                new OutboxDispatcherOptions { WorkerName = "test-dispatcher", TenantIdFilter = cid });
            var processed = await dispatcher.DispatchOutboxOnceAsync();
            Assert.True(processed >= 1, "dispatcher should process the job.delivered event");

            // One rating charge of 200 * 2.00 = 400.
            var charges = await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM job_charges WHERE company_id=@c AND job_id=@j AND source='rating'",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@j", jobId); });
            Assert.Equal(1, charges);
            var amount = await db.ScalarDecimalAsync(
                "SELECT amount FROM job_charges WHERE company_id=@c AND job_id=@j AND source='rating' AND charge_type='base'",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@j", jobId); });
            Assert.Equal(400.00m, amount);

            // Job marked ready-to-bill, outbox row marked processed.
            var status = (await db.QuerySingleAsync("SELECT status FROM jobs WHERE id=@j", c => c.Parameters.AddWithValue("@j", jobId)))!["status"]?.ToString();
            Assert.Equal("ready_to_bill", status);
            var outboxStatus = (await db.QuerySingleAsync(
                "SELECT status FROM outbox_messages WHERE tenant_id=@t AND event_type='job.delivered' AND aggregate_id=@a",
                c => { c.Parameters.AddWithValue("@t", cid); c.Parameters.AddWithValue("@a", jobId.ToString()); }))!["status"]?.ToString();
            Assert.Equal("processed", outboxStatus);
        }
        finally { await CleanupAsync(db, cid); }
    }

    private static async Task InvokeTransitionAsync(Database db, long cid, long aid, string to)
    {
        var m = typeof(EndpointMappings).GetMethod("ApplyAssignmentTransitionAsync",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("ApplyAssignmentTransitionAsync not found");
        await (Task)m.Invoke(null, new object[] { db, cid, aid, to, CancellationToken.None })!;
    }

    private static RevenueReadinessService CreateRevenue(Database db)
    {
        var corr = new InMemoryCorrelationContext("corr-d2b", "cause-d2b", "req-d2b", null, ActorTypes.TenantUser, "42");
        return new RevenueReadinessService(db, new PostgresAiFoundationService(db, corr),
            new PostgresApprovalWorkflowService(db, corr), new PostgresIdempotencyService(db),
            new PostgresDomainEventPublisher(db, corr), corr);
    }

    private static async Task<long> SeedCompanyAsync(Database db) =>
        await db.InsertAsync("INSERT INTO companies (company_code, name, industry) VALUES (@code, 'D2B Co', 'logistics') RETURNING id",
            c => c.Parameters.AddWithValue("@code", $"D2B-{Guid.NewGuid():N}".Substring(0, 18)));

    private static async Task CleanupAsync(Database db, long cid)
    {
        await db.ExecuteAsync("DELETE FROM outbox_messages WHERE tenant_id=@t", c => c.Parameters.AddWithValue("@t", cid));
        foreach (var t in new[] { "job_charges", "dispatch_assignments", "trips", "jobs", "rate_cards" })
            await db.ExecuteAsync($"DELETE FROM {t} WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", cid));
        await db.ExecuteAsync("DELETE FROM companies WHERE id=@c", c => c.Parameters.AddWithValue("@c", cid));
    }

    private static Database CreateDatabase() =>
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString }).Build());
}
