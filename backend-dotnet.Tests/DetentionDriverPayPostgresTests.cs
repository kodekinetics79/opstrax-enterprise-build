using System.Globalization;
using Microsoft.Extensions.Configuration;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

// Detention -> driver pay (the differentiator: OpsTrax collects detention AND pays the driver — nobody
// else owns both sides). Proves: fail-closed with no policy; a COLLECTED detention charge produces a
// driver-pay line in the period it was collected; percent vs flat-per-hour math; and — critically —
// delete-and-recompute never duplicates or orphans the detention line (it is DERIVED, not appended).
[Trait("Category", "Integration")]
public class DetentionDriverPayPostgresTests
{
    [Fact]
    public async Task Collected_Detention_Adds_A_Driver_Pay_Line_And_Fails_Closed_Without_Policy()
    {
        var db = CreateDatabase();
        var svc = new SettlementService(db);
        var cid = await SeedCompanyAsync(db);
        var driverId = 501L;
        var start = new DateOnly(2026, 6, 15); var end = new DateOnly(2026, 6, 21);
        try
        {
            await AgreementAsync(db, cid);
            await DeliveredLoadAsync(db, cid, driverId, start);
            // A detention charge attributed to the driver, on a COLLECTED (paid) invoice in the period.
            await SeedCollectedDetentionAsync(db, cid, driverId, chargeAmount: 120m, hours: 2m, paidOn: new DateTime(2026, 6, 17));

            // Fail-closed: no policy => the statement has ONLY the linehaul line, no detention pay.
            var noPolicy = await svc.GenerateDriverStatementAsync(cid, driverId, start, end, SettlementMode.Preview);
            Assert.DoesNotContain(noPolicy.Lines, l => l.PayCode == "detention");

            // Enable: 25% of the collected detention charge => $30.
            await svc.SetDetentionPayPolicyAsync(cid, enabled: true, trigger: "collected", shareType: "percent", shareValue: 25m);
            var withPolicy = await svc.GenerateDriverStatementAsync(cid, driverId, start, end, SettlementMode.Commit);
            var detLine = Assert.Single(withPolicy.Lines, l => l.PayCode == "detention");
            Assert.Equal(30m, detLine.Amount);
            Assert.Contains("Detention pay", detLine.Description);

            // Regenerate (delete-and-recompute) must NOT duplicate the detention line — it is re-derived.
            await svc.GenerateDriverStatementAsync(cid, driverId, start, end, SettlementMode.Commit);
            var detLines = await db.ScalarLongAsync(
                @"SELECT COUNT(*) FROM settlement_lines sl
                  JOIN settlement_statements ss ON ss.id=sl.statement_id AND ss.company_id=sl.company_id
                  WHERE sl.company_id=@c AND ss.payee_id=@d AND sl.pay_code='detention'",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@d", driverId); });
            Assert.Equal(1, detLines);
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task Flat_Per_Hour_Detention_Pay_Uses_Billable_Hours()
    {
        var db = CreateDatabase();
        var svc = new SettlementService(db);
        var cid = await SeedCompanyAsync(db);
        var driverId = 777L;
        var start = new DateOnly(2026, 6, 15); var end = new DateOnly(2026, 6, 21);
        try
        {
            await AgreementAsync(db, cid);
            await DeliveredLoadAsync(db, cid, driverId, start);
            await SeedCollectedDetentionAsync(db, cid, driverId, chargeAmount: 200m, hours: 3m, paidOn: new DateTime(2026, 6, 18));
            // $15/hour of billable detention × 3h = $45 (independent of the charge amount).
            await svc.SetDetentionPayPolicyAsync(cid, enabled: true, trigger: "collected", shareType: "flat_per_hour", shareValue: 15m);

            var res = await svc.GenerateDriverStatementAsync(cid, driverId, start, end, SettlementMode.Preview);
            var detLine = Assert.Single(res.Lines, l => l.PayCode == "detention");
            Assert.Equal(45m, detLine.Amount);
        }
        finally { await CleanupAsync(db, cid); }
    }

    // ── helpers ───────────────────────────────────────────────────────────────
    private static async Task<long> SeedCompanyAsync(Database db) =>
        await db.InsertAsync("INSERT INTO companies (company_code, name, industry) VALUES (@code, 'DDP Co', 'logistics') RETURNING id",
            c => c.Parameters.AddWithValue("@code", $"DDP-{Guid.NewGuid():N}".Substring(0, 15)));

    private static Task AgreementAsync(Database db, long cid) =>
        db.ExecuteAsync(
            @"INSERT INTO pay_agreements (company_id, agreement_code, agreement_name, payee_type, payee_id, basis, rate, effective_date, status)
              VALUES (@c, @code, 'Driver default', 'driver', NULL, 'per_mile', 0.55, DATE '2025-01-01', 'active')",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@code", $"PA-{cid}"); });

    private static async Task DeliveredLoadAsync(Database db, long cid, long driverId, DateOnly period)
    {
        var jobId = await db.InsertAsync(
            "INSERT INTO jobs (company_id, job_code, job_type, status) VALUES (@c, @code, 'freight', 'delivered') RETURNING id",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@code", $"J-{cid}-{Guid.NewGuid():N}".Substring(0, 24)); });
        await db.ExecuteAsync("INSERT INTO trips (company_id, job_id, actual_distance_miles) VALUES (@c, @j, 100)",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@j", jobId); });
        await db.ExecuteAsync(
            @"INSERT INTO dispatch_assignments (company_id, job_id, driver_id, assignment_status, status, actual_delivery_at)
              VALUES (@c, @j, @d, 'delivered', 'Delivered', @dt)",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@j", jobId); c.Parameters.AddWithValue("@d", driverId);
                   c.Parameters.AddWithValue("@dt", period.ToDateTime(new TimeOnly(12, 0))); });
    }

    // A detention charge on a paid invoice, attributed to the driver via detention_dwells.driver_id.
    private static async Task SeedCollectedDetentionAsync(Database db, long cid, long driverId, decimal chargeAmount, decimal hours, DateTime paidOn)
    {
        var custId = await db.InsertAsync(
            "INSERT INTO customers (company_id, customer_code, name) VALUES (@c, @code, 'DDP Cust') RETURNING id",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@code", $"DDPC-{Guid.NewGuid():N}".Substring(0, 14)); });
        var vid = await db.InsertAsync("INSERT INTO vehicles (company_id, vehicle_code, type) VALUES (@c, @code, 'truck') RETURNING id",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@code", $"DV-{Guid.NewGuid():N}".Substring(0, 12)); });
        var gid = await db.InsertAsync(
            "INSERT INTO geofences (company_id, name, geofence_type, center_lat, center_lng, radius_meters, status, customer_id, site_role) VALUES (@c, 'Depot', 'circular', 34, -118, 300, 'Active', @cust, 'customer_site') RETURNING id",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@cust", custId); });
        var jobId = await db.InsertAsync(
            "INSERT INTO jobs (company_id, customer_id, job_code, job_type, status) VALUES (@c, @cust, @code, 'freight', 'delivered') RETURNING id",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@cust", custId); c.Parameters.AddWithValue("@code", $"DJ-{Guid.NewGuid():N}".Substring(0, 12)); });
        var chargeId = await db.InsertAsync(
            @"INSERT INTO job_charges (company_id, job_id, charge_code, charge_name, charge_type, quantity, unit_rate, amount, source, billing_status)
              VALUES (@c, @j, 'DETENTION', 'Detention', 'accessorial', @h, 60, @amt, 'detention', 'billed') RETURNING id",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@j", jobId); c.Parameters.AddWithValue("@h", hours); c.Parameters.AddWithValue("@amt", chargeAmount); });
        // The dwell attributes the charge to the driver.
        await db.ExecuteAsync(
            @"INSERT INTO detention_dwells (company_id, geofence_id, vehicle_id, driver_id, customer_id, job_id, entry_event_id, entered_at, quantity_hours, amount, status, job_charge_id)
              VALUES (@c, @g, @v, @d, @cust, @j, 1, NOW(), @h, @amt, 'charged', @ch)",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@g", gid); c.Parameters.AddWithValue("@v", vid);
                   c.Parameters.AddWithValue("@d", driverId); c.Parameters.AddWithValue("@cust", custId); c.Parameters.AddWithValue("@j", jobId);
                   c.Parameters.AddWithValue("@h", hours); c.Parameters.AddWithValue("@amt", chargeAmount); c.Parameters.AddWithValue("@ch", chargeId); });
        // A PAID invoice carrying the detention charge line, paid in the period.
        var draftNo = $"D-{Guid.NewGuid():N}".Substring(0, 12);
        var draft = (await db.QuerySingleAsync(
            "INSERT INTO invoice_drafts (company_id, customer_id, invoice_draft_no) VALUES (@c, @cust, @dno) RETURNING id",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@cust", custId); c.Parameters.AddWithValue("@dno", draftNo); }))!;
        var inv = (await db.QuerySingleAsync(
            @"INSERT INTO issued_invoices (company_id, customer_id, source_invoice_draft_id, source_invoice_draft_no, invoice_number, subtotal, tax_total, total, amount_paid, balance_due, payment_status, status, currency, issued_at, paid_at)
              VALUES (@c, @cust, @draft::uuid, @dno, @ino, @amt, 0, @amt, @amt, 0, 'paid', 'paid', 'USD', @paid - INTERVAL '2 day', @paid) RETURNING id",
            c =>
            {
                c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@cust", custId);
                c.Parameters.AddWithValue("@draft", draft["id"]!.ToString()!); c.Parameters.AddWithValue("@dno", draftNo);
                c.Parameters.AddWithValue("@ino", $"INV-{Guid.NewGuid():N}".Substring(0, 12));
                c.Parameters.AddWithValue("@amt", chargeAmount); c.Parameters.AddWithValue("@paid", paidOn);
            }))!;
        await db.ExecuteAsync(
            @"INSERT INTO issued_invoice_lines (company_id, issued_invoice_id, job_charge_id, line_no, description, quantity, unit_rate, amount)
              VALUES (@c, @inv, @ch, 1, 'Detention', @h, 60, @amt)",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@inv", (Guid)inv["id"]!); c.Parameters.AddWithValue("@ch", chargeId);
                   c.Parameters.AddWithValue("@h", hours); c.Parameters.AddWithValue("@amt", chargeAmount); });
    }

    private static async Task CleanupAsync(Database db, long cid)
    {
        foreach (var t in new[] { "settlement_payments", "settlement_lines", "settlement_statements", "pay_agreements",
                                  "driver_detention_pay_policy", "detention_dwells", "issued_invoice_lines", "issued_invoices",
                                  "invoice_drafts", "job_charges", "geofences", "dispatch_assignments", "trips", "jobs", "vehicles", "customers" })
            await db.ExecuteAsync($"DELETE FROM {t} WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", cid));
        await db.ExecuteAsync("DELETE FROM companies WHERE id=@c", c => c.Parameters.AddWithValue("@c", cid));
    }

    private static Database CreateDatabase() =>
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString }).Build());
}
