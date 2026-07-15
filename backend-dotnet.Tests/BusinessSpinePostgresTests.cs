using Microsoft.Extensions.Configuration;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

public class BusinessSpinePostgresTests
{
    private static readonly string LocalConnectionString = TestDb.ConnectionString;

    [Fact]
    public async Task BusinessSurfaceProfile_Defaults_Are_Generic_And_Persistent()
    {
        var db = CreateDatabase();
        var svc = new BusinessSpineService(db);
        var schema = new BusinessSpineSchemaService(db);
        var companyId = NextCompanyId();

        try
        {
            await schema.EnsureAsync();

            var profile = await svc.GetOrCreateProfileAsync(companyId);

            Assert.Equal(companyId, profile.CompanyId);
            Assert.Equal("generic", profile.VerticalKey);
            Assert.Equal("Customer", profile.CustomerLabelSingular);
            Assert.Equal("Jobs", profile.JobLabelPlural);
            Assert.True(profile.UseGenericLabels);

            var stored = await db.QuerySingleAsync("SELECT * FROM business_surface_profiles WHERE company_id=@companyId",
                c => c.Parameters.AddWithValue("@companyId", companyId));
            Assert.NotNull(stored);
            Assert.Equal("generic", stored!["verticalKey"]);
        }
        finally
        {
            await CleanupAsync(db, companyId);
        }
    }

    [Fact]
    public async Task RateCard_And_JobCharge_Persist_WithCorrelation_Metadata()
    {
        var db = CreateDatabase();
        var svc = new BusinessSpineService(db);
        var schema = new BusinessSpineSchemaService(db);
        var companyId = NextCompanyId();
        var correlationId = $"corr-{Guid.NewGuid():N}";
        var causationId = $"cause-{Guid.NewGuid():N}";

        try
        {
            await schema.EnsureAsync();
            await svc.GetOrCreateProfileAsync(companyId);

            var rateCard = await svc.CreateRateCardAsync(
                companyId,
                "RC-GEN-001",
                "Generic service rate card",
                customerId: 101,
                contractId: 201,
                billingBasis: "Per Mile",
                serviceScope: "Generic service corridor",
                originZone: "North",
                destinationZone: "South",
                vehicleType: "Any",
                currency: "USD",
                baseRate: 2.35m,
                minimumCharge: 120m,
                fuelSurchargePercent: 7.5m,
                accessorialType: "Waiting",
                effectiveDate: new DateOnly(2026, 6, 28),
                expiryDate: null,
                status: "Active",
                correlationId: correlationId,
                causationId: causationId,
                notes: "Generic rate foundation");

            var charge = await svc.CreateJobChargeAsync(
                companyId,
                jobId: 501,
                tripId: 601,
                rateCardId: rateCard.Id,
                chargeCode: "BASE",
                chargeName: "Base service charge",
                chargeType: "base",
                description: "Initial business spine charge",
                quantity: 1m,
                unitRate: 245.50m,
                amount: 245.50m,
                currency: "USD",
                status: "pending",
                correlationId: correlationId,
                causationId: causationId,
                approvedByUserId: 77,
                approvedAt: DateTimeOffset.UtcNow);

            Assert.Equal("RC-GEN-001", rateCard.RateCardCode);
            Assert.Equal("Generic service rate card", rateCard.RateCardName);
            Assert.Equal(correlationId, rateCard.CorrelationId);
            Assert.Equal(causationId, rateCard.CausationId);
            Assert.Equal(245.50m, charge.Amount);
            Assert.Equal(rateCard.Id, charge.RateCardId);
            Assert.Equal(correlationId, charge.CorrelationId);
            Assert.Equal(causationId, charge.CausationId);
            Assert.Equal(77, charge.ApprovedByUserId);

            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM rate_cards WHERE company_id=@companyId AND rate_card_code='RC-GEN-001' AND correlation_id=@corr AND causation_id=@cause", c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@corr", correlationId);
                c.Parameters.AddWithValue("@cause", causationId);
            }));

            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM job_charges WHERE company_id=@companyId AND charge_code='BASE' AND correlation_id=@corr AND causation_id=@cause", c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@corr", correlationId);
                c.Parameters.AddWithValue("@cause", causationId);
            }));
        }
        finally
        {
            await CleanupAsync(db, companyId);
        }
    }

    [Fact]
    public async Task RateCard_Mirror_Upsert_Is_TenantScoped_And_Idempotent_ByCode()
    {
        var db = CreateDatabase();
        var svc = new BusinessSpineService(db);
        var schema = new BusinessSpineSchemaService(db);
        var companyId = NextCompanyId();

        try
        {
            await schema.EnsureAsync();

            var first = await svc.UpsertRateCardMirrorAsync(
                companyId,
                "RC-BRIDGE-001",
                "Bridge rate card",
                customerId: 301,
                contractId: 401,
                billingBasis: "Per Mile",
                serviceScope: "Bridge corridor",
                originZone: "North",
                destinationZone: "South",
                vehicleType: "Any",
                currency: "USD",
                baseRate: 1.95m,
                minimumCharge: 100m,
                fuelSurchargePercent: 6m,
                accessorialType: "Liftgate",
                effectiveDate: new DateOnly(2026, 6, 28),
                expiryDate: null,
                status: "Active",
                correlationId: "corr-bridge-1",
                causationId: "cause-bridge-1",
                notes: "bridge one");

            var second = await svc.UpsertRateCardMirrorAsync(
                companyId,
                "RC-BRIDGE-001",
                "Bridge rate card updated",
                customerId: 301,
                contractId: 401,
                billingBasis: "Per Mile",
                serviceScope: "Bridge corridor",
                originZone: "North",
                destinationZone: "South",
                vehicleType: "Any",
                currency: "USD",
                baseRate: 2.10m,
                minimumCharge: 110m,
                fuelSurchargePercent: 6.5m,
                accessorialType: "Liftgate",
                effectiveDate: new DateOnly(2026, 6, 28),
                expiryDate: null,
                status: "Active",
                correlationId: "corr-bridge-2",
                causationId: "cause-bridge-2",
                notes: "bridge two");

            Assert.Equal(first.Id, second.Id);
            Assert.Equal("Bridge rate card updated", second.RateCardName);
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM rate_cards WHERE company_id=@companyId AND rate_card_code='RC-BRIDGE-001'", c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
            }));
        }
        finally
        {
            await CleanupAsync(db, companyId);
        }
    }

    [Fact]
    public async Task RateCard_Update_Persists_And_Stays_TenantScoped()
    {
        var db = CreateDatabase();
        var svc = new BusinessSpineService(db);
        var schema = new BusinessSpineSchemaService(db);
        var companyId = NextCompanyId();

        try
        {
            await schema.EnsureAsync();

            var created = await svc.CreateRateCardAsync(
                companyId,
                "RC-UPDATE-001",
                "Update me",
                customerId: 901,
                contractId: 902,
                billingBasis: "Per Mile",
                serviceScope: "Update corridor",
                originZone: "East",
                destinationZone: "West",
                vehicleType: "Van",
                currency: "USD",
                baseRate: 1.75m,
                minimumCharge: 95m,
                fuelSurchargePercent: 5m,
                accessorialType: "Wait time",
                effectiveDate: new DateOnly(2026, 6, 28),
                expiryDate: null,
                status: "Active",
                correlationId: "corr-update-1",
                causationId: "cause-update-1",
                notes: "initial");

            var updated = await svc.UpdateRateCardAsync(
                companyId,
                created.Id,
                rateCardName: "Updated name",
                baseRate: 2.5m,
                status: "Inactive",
                correlationId: "corr-update-2",
                causationId: "cause-update-2",
                notes: "updated",
                ct: default);

            Assert.NotNull(updated);
            Assert.Equal("Updated name", updated!.RateCardName);
            Assert.Equal(2.5m, updated.BaseRate);
            Assert.Equal("Inactive", updated.Status);
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM rate_cards WHERE company_id=@companyId AND id=@id AND rate_card_name='Updated name' AND status='Inactive'", c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@id", created.Id);
            }));
        }
        finally
        {
            await CleanupAsync(db, companyId);
        }
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

    private static long NextCompanyId() => Interlocked.Increment(ref _nextCompanyId);
    private static long _nextCompanyId = 54000;

    private static async Task CleanupAsync(Database db, long companyId)
    {
        await db.ExecuteAsync("DELETE FROM job_charges WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM rate_cards WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM business_surface_profiles WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
    }
}
