using Microsoft.Extensions.Configuration;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

// Rating engine Phase 1 (ADR-008 §A) — money-math verified against a real Postgres.
[Trait("Category", "Integration")]
public class RatingServicePostgresTests
{
    [Fact]
    public async Task PerMile_Computes_Linehaul_Fuel_And_Persists_As_Rating()
    {
        var db = CreateDatabase();
        var rating = new RatingService(db, new BusinessSpineService(db));
        var cid = await SeedCompanyAsync(db);
        try
        {
            var cardId = await SeedRateCardAsync(db, cid, "Per Mile", baseRate: 2.75m, min: 350m, fuelPct: 14.5m);
            var jobId = await SeedJobAsync(db, cid, cardId);
            await SeedTripAsync(db, cid, jobId, miles: 300m);

            var outcome = await rating.RateJobAsync(cid, jobId, RateMode.Commit);

            Assert.True(outcome.Priced);
            Assert.Equal(2, outcome.Lines.Count);
            var linehaul = outcome.Lines.Single(l => l.ChargeType == "base");
            var fuel = outcome.Lines.Single(l => l.ChargeType == "fuel_surcharge");
            Assert.Equal(825.00m, linehaul.Amount);        // 300 * 2.75
            Assert.Equal(119.63m, fuel.Amount);            // 825 * 0.145 = 119.625 -> 119.63
            Assert.Equal(944.63m, outcome.Total);

            // Persisted as source='rating'.
            var count = await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM job_charges WHERE company_id=@c AND job_id=@j AND source='rating'",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@j", jobId); });
            Assert.Equal(2, count);
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task Minimum_Charge_Floors_The_Linehaul()
    {
        var db = CreateDatabase();
        var rating = new RatingService(db, new BusinessSpineService(db));
        var cid = await SeedCompanyAsync(db);
        try
        {
            var cardId = await SeedRateCardAsync(db, cid, "Per Mile", baseRate: 1.00m, min: 500m, fuelPct: 0m);
            var jobId = await SeedJobAsync(db, cid, cardId);
            await SeedTripAsync(db, cid, jobId, miles: 100m);   // 100*1 = 100 < 500 min

            var outcome = await rating.RateJobAsync(cid, jobId, RateMode.Commit);

            Assert.Equal(500.00m, outcome.Lines.Single(l => l.ChargeType == "base").Amount);
            Assert.Equal(500.00m, outcome.Total);
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task PerStop_Computes_From_Trip_Stop_Count()
    {
        var db = CreateDatabase();
        var rating = new RatingService(db, new BusinessSpineService(db));
        var cid = await SeedCompanyAsync(db);
        try
        {
            var cardId = await SeedRateCardAsync(db, cid, "Per Stop", baseRate: 25m, min: null, fuelPct: 0m);
            var jobId = await SeedJobAsync(db, cid, cardId);
            await db.ExecuteAsync("INSERT INTO trips (company_id, job_id, total_planned_stops) VALUES (@c,@j,3)",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@j", jobId); });

            var outcome = await rating.RateJobAsync(cid, jobId, RateMode.Commit);

            Assert.Equal(75.00m, outcome.Lines.Single(l => l.ChargeType == "base").Amount); // 3 * 25
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task PerHour_Computes_From_Trip_Duration()
    {
        var db = CreateDatabase();
        var rating = new RatingService(db, new BusinessSpineService(db));
        var cid = await SeedCompanyAsync(db);
        try
        {
            var cardId = await SeedRateCardAsync(db, cid, "Hourly", baseRate: 20m, min: null, fuelPct: 0m);
            var jobId = await SeedJobAsync(db, cid, cardId);
            await db.ExecuteAsync("INSERT INTO trips (company_id, job_id, actual_duration_minutes) VALUES (@c,@j,180)",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@j", jobId); });

            var outcome = await rating.RateJobAsync(cid, jobId, RateMode.Commit);

            Assert.Equal(60.00m, outcome.Lines.Single(l => l.ChargeType == "base").Amount); // 180min/60 * 20
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task ReRating_Is_Idempotent_No_Duplicate_Charges()
    {
        var db = CreateDatabase();
        var rating = new RatingService(db, new BusinessSpineService(db));
        var cid = await SeedCompanyAsync(db);
        try
        {
            var cardId = await SeedRateCardAsync(db, cid, "Flat Rate", baseRate: 450m, min: null, fuelPct: 0m);
            var jobId = await SeedJobAsync(db, cid, cardId);

            await rating.RateJobAsync(cid, jobId, RateMode.Commit);
            await rating.RateJobAsync(cid, jobId, RateMode.Commit);   // re-rate

            var count = await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM job_charges WHERE company_id=@c AND job_id=@j AND source='rating'",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@j", jobId); });
            Assert.Equal(1, count);   // one flat base charge, not two
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task No_Rate_Card_Fails_Closed_And_Writes_Nothing()
    {
        var db = CreateDatabase();
        var rating = new RatingService(db, new BusinessSpineService(db));
        var cid = await SeedCompanyAsync(db);
        try
        {
            var jobId = await SeedJobAsync(db, cid, rateCardId: null);   // no rate card, no contract

            var outcome = await rating.RateJobAsync(cid, jobId, RateMode.Commit);

            Assert.False(outcome.Priced);
            Assert.Equal("no_rate_card", outcome.UnpricedReason);
            var count = await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM job_charges WHERE company_id=@c AND job_id=@j", c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@j", jobId); });
            Assert.Equal(0, count);   // fail-closed: never invented a price
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task Manual_Charges_Are_Preserved_Across_ReRating()
    {
        var db = CreateDatabase();
        var rating = new RatingService(db, new BusinessSpineService(db));
        var cid = await SeedCompanyAsync(db);
        try
        {
            var cardId = await SeedRateCardAsync(db, cid, "Flat Rate", baseRate: 450m, min: null, fuelPct: 0m);
            var jobId = await SeedJobAsync(db, cid, cardId);
            // A hand-keyed charge that rating must never delete.
            await db.ExecuteAsync(
                @"INSERT INTO job_charges (company_id, job_id, charge_code, charge_name, charge_type, quantity, unit_rate, amount, currency, status, source)
                  VALUES (@c, @j, 'DETENTION', 'Manual detention', 'accessorial', 1, 75, 75, 'USD', 'pending', 'manual')",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@j", jobId); });

            await rating.RateJobAsync(cid, jobId, RateMode.Commit);
            await rating.RateJobAsync(cid, jobId, RateMode.Commit);

            var manual = await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM job_charges WHERE company_id=@c AND job_id=@j AND source='manual'",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@j", jobId); });
            Assert.Equal(1, manual);   // manual survived both re-ratings
        }
        finally { await CleanupAsync(db, cid); }
    }

    // ── seed helpers ──
    private static async Task<long> SeedCompanyAsync(Database db) =>
        await db.InsertAsync(
            "INSERT INTO companies (company_code, name, industry) VALUES (@code, 'Rating Test Co', 'logistics') RETURNING id",
            c => c.Parameters.AddWithValue("@code", $"RTC-{Guid.NewGuid():N}".Substring(0, 18)));

    private static async Task<long> SeedRateCardAsync(Database db, long cid, string basis, decimal baseRate, decimal? min, decimal fuelPct) =>
        await db.InsertAsync(
            @"INSERT INTO rate_cards (company_id, rate_card_code, rate_card_name, billing_basis, base_rate, minimum_charge, fuel_surcharge_percent, currency, effective_date, status)
              VALUES (@c, @code, 'Test card', @basis, @rate, @min, @fuel, 'USD', CURRENT_DATE, 'Active') RETURNING id",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@code", $"RC-{cid}");
                   c.Parameters.AddWithValue("@basis", basis); c.Parameters.AddWithValue("@rate", baseRate);
                   c.Parameters.AddWithValue("@min", (object?)min ?? DBNull.Value); c.Parameters.AddWithValue("@fuel", fuelPct); });

    private static async Task<long> SeedJobAsync(Database db, long cid, long? rateCardId) =>
        await db.InsertAsync(
            @"INSERT INTO jobs (company_id, job_code, job_type, rate_card_id, status)
              VALUES (@c, @code, 'freight', @rc, 'delivered') RETURNING id",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@code", $"J-{cid}-{Guid.NewGuid():N}".Substring(0, 20));
                   c.Parameters.AddWithValue("@rc", (object?)rateCardId ?? DBNull.Value); });

    private static async Task SeedTripAsync(Database db, long cid, long jobId, decimal miles) =>
        await db.ExecuteAsync(
            "INSERT INTO trips (company_id, job_id, actual_distance_miles) VALUES (@c, @j, @m)",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@j", jobId); c.Parameters.AddWithValue("@m", miles); });

    private static async Task CleanupAsync(Database db, long cid)
    {
        await db.ExecuteAsync("DELETE FROM job_charges WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", cid));
        await db.ExecuteAsync("DELETE FROM trips WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", cid));
        await db.ExecuteAsync("DELETE FROM jobs WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", cid));
        await db.ExecuteAsync("DELETE FROM rate_cards WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", cid));
        await db.ExecuteAsync("DELETE FROM companies WHERE id=@c", c => c.Parameters.AddWithValue("@c", cid));
    }

    private static Database CreateDatabase()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString })
            .Build();
        return new Database(config);
    }

    private static long NextCompanyId() => System.Threading.Interlocked.Increment(ref _nextCompanyId);
    private static long _nextCompanyId = 62000;
}
