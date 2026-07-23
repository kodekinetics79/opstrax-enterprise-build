using Microsoft.Extensions.Configuration;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;

namespace Opstrax.Tests;

// P0 fix — last-mile deliveries now reach order-to-cash. A confirmed fleet_tms delivery had no job/customer/
// charge linkage, so its revenue leaked (invisible to billing). The bridge materializes a canonical customer
// + job + delivered dispatch_assignment + a manual job_charge from order_value — exactly and only what
// BillingConsolidationService needs to bill it — and is fully idempotent so a repeated confirm never
// double-bills.
[Trait("Category", "Integration")]
public class LastMileBillingBridgePostgresTests
{
    [Fact]
    public async Task Confirmed_LastMile_Delivery_Becomes_A_Billable_Charge_Once()
    {
        var db = CreateDatabase();
        var cid = await SeedCompanyAsync(db);
        var orderNo = $"LM-{Guid.NewGuid():N}".Substring(0, 12);
        const string customerName = "Acme Bridge Co";
        try
        {
            await db.ExecuteAsync(
                @"INSERT INTO fleet_tms_dispatch_orders (company_id, order_number, customer_name, order_value, status)
                  VALUES (@c, @n, @cust, 250.00, 'InTransit')",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@n", orderNo); c.Parameters.AddWithValue("@cust", customerName); });

            // Run the bridge twice — a repeated ConfirmDelivery must not double-bill.
            await FleetTmsLogisticsEndpoints.BridgeLastMileToBillingAsync(db, cid, orderNo, customerName, CancellationToken.None);
            await FleetTmsLogisticsEndpoints.BridgeLastMileToBillingAsync(db, cid, orderNo, customerName, CancellationToken.None);

            // Exactly one canonical job for the order, linked to a customer.
            var jobId = await db.ScalarLongAsync(
                "SELECT id FROM jobs WHERE company_id=@c AND job_code='FTMS-'||@n",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@n", orderNo); });
            Assert.True(jobId > 0, "a canonical job should be materialized from the last-mile order");

            var jobRow = (await db.QuerySingleAsync(
                "SELECT customer_id, status FROM jobs WHERE id=@j", c => c.Parameters.AddWithValue("@j", jobId)))!;
            Assert.NotNull(jobRow["customerId"]);            // billing requires a customer
            Assert.Equal("delivered", jobRow["status"]?.ToString());

            // Exactly one delivered dispatch_assignment (consolidation's period filter needs it).
            var deliveredAssignments = await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM dispatch_assignments WHERE company_id=@c AND job_id=@j AND assignment_status='delivered'",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@j", jobId); });
            Assert.Equal(1, deliveredAssignments);

            // Exactly ONE unbilled manual charge for the order value — no double-bill after two runs.
            var charges = await db.QueryAsync(
                "SELECT amount, billing_status, source FROM job_charges WHERE company_id=@c AND job_id=@j AND charge_code='LASTMILE'",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@j", jobId); });
            Assert.Single(charges);
            Assert.Equal(250.00m, Convert.ToDecimal(charges[0]["amount"]));
            Assert.Equal("unbilled", charges[0]["billingStatus"]?.ToString());
            Assert.Equal("manual", charges[0]["source"]?.ToString());

            // And exactly one customer for the name (idempotent resolve).
            var customers = await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM customers WHERE company_id=@c AND lower(name)=lower(@n)",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@n", customerName); });
            Assert.Equal(1, customers);
        }
        finally { await CleanupAsync(db, cid, orderNo); }
    }

    private static async Task<long> SeedCompanyAsync(Database db) =>
        await db.InsertAsync("INSERT INTO companies (company_code, name, industry) VALUES (@code, 'LM Co', 'logistics') RETURNING id",
            c => c.Parameters.AddWithValue("@code", $"LM-{Guid.NewGuid():N}".Substring(0, 15)));

    private static async Task CleanupAsync(Database db, long cid, string orderNo)
    {
        await db.ExecuteAsync("DELETE FROM job_charges WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", cid));
        await db.ExecuteAsync("DELETE FROM dispatch_assignments WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", cid));
        await db.ExecuteAsync("DELETE FROM jobs WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", cid));
        await db.ExecuteAsync("DELETE FROM customers WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", cid));
        await db.ExecuteAsync("DELETE FROM fleet_tms_dispatch_orders WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", cid));
        await db.ExecuteAsync("DELETE FROM companies WHERE id=@c", c => c.Parameters.AddWithValue("@c", cid));
    }

    private static Database CreateDatabase() =>
        new(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString }).Build());
}
