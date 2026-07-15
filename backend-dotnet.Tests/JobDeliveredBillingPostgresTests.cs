using Microsoft.Extensions.Configuration;
using Opstrax.Api.Data;
using Opstrax.Api.Foundation;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

// Delivery -> billing automation (ADR-008 §B Phase 2): the job.delivered handler rates the load and
// marks it ready-to-bill, idempotently. Verified against real Postgres.
[Trait("Category", "Integration")]
public class JobDeliveredBillingPostgresTests
{
    [Fact]
    public async Task Handler_Rates_And_Marks_ReadyToBill_Idempotently()
    {
        var db = CreateDatabase();
        var handler = new JobDeliveredBillingHandler(new RatingService(db, new BusinessSpineService(db)), CreateRevenue(db));
        var cid = await SeedCompanyAsync(db);
        try
        {
            var cardId = await db.InsertAsync(
                @"INSERT INTO rate_cards (company_id, rate_card_code, rate_card_name, billing_basis, base_rate, fuel_surcharge_percent, currency, effective_date, status)
                  VALUES (@c, @code, 'Card', 'Per Mile', 2.00, 0, 'USD', CURRENT_DATE, 'Active') RETURNING id",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@code", $"RC-{cid}"); });
            var jobId = await db.InsertAsync(
                @"INSERT INTO jobs (company_id, job_code, job_type, rate_card_id, status)
                  VALUES (@c, @code, 'freight', @rc, 'delivered') RETURNING id",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@code", $"J-{cid}"); c.Parameters.AddWithValue("@rc", cardId); });
            await db.ExecuteAsync("INSERT INTO trips (company_id, job_id, actual_distance_miles) VALUES (@c,@j,200)",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@j", jobId); });

            var msg = new OutboxMessageRecord(1, cid.ToString(), "job.delivered", "job", jobId.ToString(), "{}", null, null, null, DateTimeOffset.UtcNow);
            await handler.HandleAsync(msg);
            await handler.HandleAsync(msg);   // idempotent replay

            // Rated (200 * 2.00 = 400), exactly one rating charge (not two).
            var charges = await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM job_charges WHERE company_id=@c AND job_id=@j AND source='rating'",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@j", jobId); });
            Assert.Equal(1, charges);
            var amount = await db.ScalarDecimalAsync(
                "SELECT amount FROM job_charges WHERE company_id=@c AND job_id=@j AND source='rating' AND charge_type='base'",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@j", jobId); });
            Assert.Equal(400.00m, amount);

            // Marked ready-to-bill.
            var status = (await db.QuerySingleAsync("SELECT status FROM jobs WHERE id=@j", c => c.Parameters.AddWithValue("@j", jobId)))!["status"]?.ToString();
            Assert.Equal("ready_to_bill", status);
        }
        finally { await CleanupAsync(db, cid); }
    }

    private static RevenueReadinessService CreateRevenue(Database db)
    {
        var corr = new InMemoryCorrelationContext("corr-jdb", "cause-jdb", "req-jdb", null, ActorTypes.TenantUser, "42");
        return new RevenueReadinessService(db, new PostgresAiFoundationService(db, corr),
            new PostgresApprovalWorkflowService(db, corr), new PostgresIdempotencyService(db),
            new PostgresDomainEventPublisher(db, corr), corr);
    }

    private static async Task<long> SeedCompanyAsync(Database db) =>
        await db.InsertAsync("INSERT INTO companies (company_code, name, industry) VALUES (@code, 'JDB Co', 'logistics') RETURNING id",
            c => c.Parameters.AddWithValue("@code", $"JDB-{Guid.NewGuid():N}".Substring(0, 18)));

    private static async Task CleanupAsync(Database db, long cid)
    {
        foreach (var t in new[] { "job_charges", "trips", "jobs", "rate_cards" })
            await db.ExecuteAsync($"DELETE FROM {t} WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", cid));
        await db.ExecuteAsync("DELETE FROM companies WHERE id=@c", c => c.Parameters.AddWithValue("@c", cid));
    }

    private static Database CreateDatabase() =>
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString }).Build());
}
