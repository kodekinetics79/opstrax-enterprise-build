using Microsoft.Extensions.Configuration;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

// Driver self-service earnings (driver:self). The driver sees their OWN pay — including their
// share of detention we collected (the differentiator) — and NOTHING else. These tests exercise
// the centralized read methods on SettlementService directly (the HTTP identity gate is covered
// by DriverPortalIdentityTests). What must hold:
//   * only COMMITTED money counts: source='system' AND status IN (approved,paid); drafts + manual excluded
//   * strict per-driver + per-tenant scoping on both the summary and the drill-down
//   * the drill-down is null (=> 404 at the edge) for anything not the driver's own committed statement
//   * employer economics (basis_amount = customer charge, unit_rate = internal share %, job_id) are
//     NEVER on the wire — asserted by key absence, not just null
//   * the open period is a live read-only Preview — reading earnings writes NOTHING
[Trait("Category", "Integration")]
public class DriverEarningsPostgresTests
{
    [Fact]
    public async Task Earnings_Counts_Only_Committed_System_Statements_And_Isolates_By_Driver_And_Tenant()
    {
        var db = CreateDatabase();
        var svc = new SettlementService(db);
        var cid = await SeedCompanyAsync(db);
        var other = await SeedCompanyAsync(db);   // a second tenant, must never bleed in
        long driverA = 4001, driverB = 4002, driverOther = 4003;
        var ps = new DateOnly(2026, 6, 1); var pe = new DateOnly(2026, 6, 7);
        try
        {
            // Driver A: an approved statement ($1000 + $40 detention), a paid one ($500), a DRAFT
            // ($999) and a MANUAL adjustment ($888) — the last two must be invisible as "earnings".
            var approved = await SeedStatementAsync(db, cid, driverA, "system", "approved", ps, pe, linehaul: 1000m, detention: 40m, amountPaid: 0m);
            await SeedStatementAsync(db, cid, driverA, "system", "paid", ps.AddDays(-7), pe.AddDays(-7), linehaul: 500m, detention: 0m, amountPaid: 500m);
            await SeedStatementAsync(db, cid, driverA, "system", "draft", ps.AddDays(7), pe.AddDays(7), linehaul: 999m, detention: 0m, amountPaid: 0m);
            await SeedStatementAsync(db, cid, driverA, "manual", "approved", ps.AddDays(14), pe.AddDays(14), linehaul: 888m, detention: 0m, amountPaid: 0m);
            // Another driver in the SAME tenant, and a driver in a DIFFERENT tenant.
            await SeedStatementAsync(db, cid, driverB, "system", "approved", ps, pe, linehaul: 7777m, detention: 5m, amountPaid: 0m);
            await SeedStatementAsync(db, other, driverOther, "system", "approved", ps, pe, linehaul: 6666m, detention: 9m, amountPaid: 0m);

            var earnings = await svc.GetDriverEarningsAsync(cid, driverA);

            // Only A's two committed statements — never the draft, manual, peer, or cross-tenant rows.
            var statements = (List<object>)earnings["statements"]!;
            Assert.Equal(2, statements.Count);
            var totals = statements.Select(s => (decimal)((Dictionary<string, object?>)s)["total"]!).OrderBy(x => x).ToList();
            Assert.Equal(new[] { 500m, 1040m }, totals);

            var lifetime = (Dictionary<string, object?>)earnings["lifetime"]!;
            Assert.Equal(1540m, (decimal)lifetime["earned"]!);        // 1040 + 500, no draft/manual/peer
            Assert.Equal(40m, (decimal)lifetime["detentionPay"]!);    // only A's detention lines

            // The approved-but-unpaid $1040 is the only thing owed.
            Assert.Equal(1040m, (decimal)earnings["unpaidTotal"]!);

            // The approved statement carries outstanding = total (nothing paid yet) and is NOT 'paid'.
            var approvedRow = statements.Select(s => (Dictionary<string, object?>)s).Single(s => (long)Convert.ToInt64(s["id"]) == approved);
            Assert.Equal(1040m, (decimal)approvedRow["outstanding"]!);
            Assert.Equal("approved", approvedRow["status"]);
            Assert.Equal(40m, (decimal)approvedRow["detentionTotal"]!);

            // Column allow-list: no employer economics on the wire.
            foreach (var s in statements.Select(s => (Dictionary<string, object?>)s))
                foreach (var banned in new[] { "basisAmount", "unitRate", "basis", "jobId", "source", "payAgreementId" })
                    Assert.False(s.ContainsKey(banned), $"statement leaked {banned}");
        }
        finally { await CleanupAsync(db, cid); await CleanupAsync(db, other); }
    }

    [Fact]
    public async Task Statement_Detail_Reconciles_Redacts_And_Refuses_Non_Owned()
    {
        var db = CreateDatabase();
        var svc = new SettlementService(db);
        var cid = await SeedCompanyAsync(db);
        var other = await SeedCompanyAsync(db);
        long driverA = 5001, driverB = 5002;
        var ps = new DateOnly(2026, 5, 1); var pe = new DateOnly(2026, 5, 7);
        try
        {
            var sid    = await SeedStatementAsync(db, cid, driverA, "system", "approved", ps, pe, linehaul: 800m, detention: 60m, amountPaid: 0m);
            var draft  = await SeedStatementAsync(db, cid, driverA, "system", "draft", ps.AddDays(7), pe.AddDays(7), linehaul: 111m, detention: 0m, amountPaid: 0m);
            var peer   = await SeedStatementAsync(db, cid, driverB, "system", "approved", ps, pe, linehaul: 900m, detention: 0m, amountPaid: 0m);

            var detail = await svc.GetDriverStatementDetailAsync(cid, driverA, sid);
            Assert.NotNull(detail);

            // Per-payCode totals reconcile exactly to the statement total.
            var t = (Dictionary<string, object?>)detail!["totals"]!;
            var stmt = (Dictionary<string, object?>)detail["statement"]!;
            Assert.Equal(60m, (decimal)t["detention"]!);
            Assert.Equal(800m, (decimal)t["linehaul"]!);
            Assert.Equal((decimal)stmt["total"]!, (decimal)t["gross"]!);
            Assert.Equal(860m, (decimal)stmt["total"]!);

            // Lines are redacted to the allow-list: no basis_amount / unit_rate / basis / job_id.
            var lines = (List<object>)detail["lines"]!;
            Assert.Equal(2, lines.Count);
            foreach (var l in lines.Select(l => (Dictionary<string, object?>)l))
            {
                foreach (var banned in new[] { "basisAmount", "unitRate", "basis", "jobId" })
                    Assert.False(l.ContainsKey(banned), $"line leaked {banned}");
                Assert.True(l.ContainsKey("payCode") && l.ContainsKey("amount") && l.ContainsKey("label"));
            }

            // Ownership: a peer's statement, a cross-tenant read, and a not-yet-committed draft are all
            // indistinguishable from a nonexistent id (null -> 404 at the edge).
            Assert.Null(await svc.GetDriverStatementDetailAsync(cid, driverA, peer));    // cross-driver
            Assert.Null(await svc.GetDriverStatementDetailAsync(other, driverA, sid));   // cross-tenant scope
            Assert.Null(await svc.GetDriverStatementDetailAsync(cid, driverA, draft));   // not committed
            Assert.Null(await svc.GetDriverStatementDetailAsync(cid, driverA, 999999));  // nonexistent
        }
        finally { await CleanupAsync(db, cid); await CleanupAsync(db, other); }
    }

    [Fact]
    public async Task Open_Period_Preview_Surfaces_Live_Detention_And_Writes_Nothing()
    {
        var db = CreateDatabase();
        var svc = new SettlementService(db);
        var cid = await SeedCompanyAsync(db);
        long driver = 6001;
        try
        {
            await AgreementAsync(db, cid);
            // A detention charge collected THIS week (inside the open window: since there is no prior
            // committed statement, openStart = start-of-week, openEnd = today).
            await SeedCollectedDetentionAsync(db, cid, driver, chargeAmount: 100m, hours: 2m, paidOn: DateTime.Today);
            await svc.SetDetentionPayPolicyAsync(cid, enabled: true, trigger: "collected", shareType: "percent", shareValue: 30m);

            var before = await CountStatementsAsync(db, cid);
            var earnings = await svc.GetDriverEarningsAsync(cid, driver);
            var after = await CountStatementsAsync(db, cid);
            Assert.Equal(before, after);   // reading earnings must write NOTHING

            var open = (Dictionary<string, object?>)earnings["openPeriod"]!;
            Assert.True((bool)open["available"]!);
            Assert.Equal(30m, (decimal)open["detentionPay"]!);        // 30% of the $100 collected
            Assert.Equal(1, Convert.ToInt32(open["detentionEventCount"]));
            Assert.True((bool)earnings["detentionPolicyEnabled"]!);
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task Unapproved_Draft_Does_Not_Advance_The_Open_Window_Or_Hide_Live_Earnings()
    {
        var db = CreateDatabase();
        var svc = new SettlementService(db);
        var cid = await SeedCompanyAsync(db);
        long driver = 8001;
        try
        {
            await AgreementAsync(db, cid);
            // Detention collected this week -> belongs in the LIVE open period.
            await SeedCollectedDetentionAsync(db, cid, driver, chargeAmount: 100m, hours: 2m, paidOn: DateTime.Today);
            await svc.SetDetentionPayPolicyAsync(cid, enabled: true, trigger: "collected", shareType: "percent", shareValue: 30m);
            // An admin generated (but has NOT approved) a draft whose period runs through today. A draft
            // is source='system', status='draft' — it must NOT anchor the open window past today.
            await SeedStatementAsync(db, cid, driver, "system", "draft",
                DateOnly.FromDateTime(DateTime.Today.AddDays(-3)), DateOnly.FromDateTime(DateTime.Today),
                linehaul: 200m, detention: 0m, amountPaid: 0m);

            var earnings = await svc.GetDriverEarningsAsync(cid, driver);
            var open = (Dictionary<string, object?>)earnings["openPeriod"]!;
            Assert.True((bool)open["available"]!);                     // draft did not collapse the window
            Assert.Equal(30m, (decimal)open["detentionPay"]!);         // live detention still surfaces
            Assert.Empty((List<object>)earnings["statements"]!);        // the draft is not "earned" money
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task No_Pay_Agreement_Is_An_Honest_Empty_Not_A_Zero_Or_Crash()
    {
        var db = CreateDatabase();
        var svc = new SettlementService(db);
        var cid = await SeedCompanyAsync(db);
        long driver = 7001;
        try
        {
            // No pay agreement, no policy, no statements at all.
            var earnings = await svc.GetDriverEarningsAsync(cid, driver);
            var open = (Dictionary<string, object?>)earnings["openPeriod"]!;
            Assert.False((bool)open["available"]!);
            Assert.False(string.IsNullOrWhiteSpace((string?)open["reason"]));   // a friendly human reason
            Assert.Equal(0m, (decimal)open["grossPay"]!);                       // never a misleading number
            Assert.False((bool)earnings["detentionPolicyEnabled"]!);
            Assert.Empty((List<object>)earnings["statements"]!);
        }
        finally { await CleanupAsync(db, cid); }
    }

    // ── helpers ───────────────────────────────────────────────────────────────
    private static async Task<long> SeedCompanyAsync(Database db) =>
        await db.InsertAsync("INSERT INTO companies (company_code, name, industry) VALUES (@code, 'Earn Co', 'logistics') RETURNING id",
            c => c.Parameters.AddWithValue("@code", $"ERN-{Guid.NewGuid():N}".Substring(0, 15)));

    private static Task AgreementAsync(Database db, long cid) =>
        db.ExecuteAsync(
            @"INSERT INTO pay_agreements (company_id, agreement_code, agreement_name, payee_type, payee_id, basis, rate, effective_date, status)
              VALUES (@c, @code, 'Driver default', 'driver', NULL, 'per_mile', 0.55, DATE '2025-01-01', 'active')",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@code", $"PA-{cid}"); });

    // A committed/draft/manual statement with a linehaul line and (optionally) a detention line whose
    // basis_amount + unit_rate carry the (withheld) customer charge + internal share % — so a redaction
    // leak would be caught.
    private static async Task<long> SeedStatementAsync(
        Database db, long cid, long driverId, string source, string status,
        DateOnly ps, DateOnly pe, decimal linehaul, decimal detention, decimal amountPaid)
    {
        var total = linehaul + detention;
        var sid = await db.InsertAsync(
            @"INSERT INTO settlement_statements
                (company_id, statement_no, payee_type, payee_id, period_start, period_end, status, currency, subtotal, total, amount_paid, source)
              VALUES (@c, @no, 'driver', @d, @ps, @pe, @st, 'USD', @tot, @tot, @paid, @src) RETURNING id",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@no", $"ST-{Guid.NewGuid():N}".Substring(0, 20));
                   c.Parameters.AddWithValue("@d", driverId); c.Parameters.AddWithValue("@ps", ps); c.Parameters.AddWithValue("@pe", pe);
                   c.Parameters.AddWithValue("@st", status); c.Parameters.AddWithValue("@tot", total);
                   c.Parameters.AddWithValue("@paid", amountPaid); c.Parameters.AddWithValue("@src", source); });
        await db.ExecuteAsync(
            @"INSERT INTO settlement_lines (company_id, statement_id, job_id, line_no, pay_code, description, basis, basis_amount, quantity, unit_rate, amount, source)
              VALUES (@c, @sid, @jid, 1, 'linehaul', 'Line-haul', 'per_mile', 500, 500, 0.55, @amt, 'settlement')",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@sid", sid); c.Parameters.AddWithValue("@jid", sid * 10 + 1); c.Parameters.AddWithValue("@amt", linehaul); });
        if (detention > 0)
            await db.ExecuteAsync(
                @"INSERT INTO settlement_lines (company_id, statement_id, job_id, line_no, pay_code, description, basis, basis_amount, quantity, unit_rate, amount, source)
                  VALUES (@c, @sid, @jid, 2, 'detention', 'Detention pay — Depot (collected)', 'percent', 999, 2, 25, @amt, 'settlement')",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@sid", sid); c.Parameters.AddWithValue("@jid", sid * 10 + 2); c.Parameters.AddWithValue("@amt", detention); });
        if (amountPaid > 0)
            await db.ExecuteAsync(
                "INSERT INTO settlement_payments (company_id, statement_id, amount, currency, method, reference, paid_at) VALUES (@c, @sid, @amt, 'USD', 'ach', 'REF-SECRET', NOW())",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@sid", sid); c.Parameters.AddWithValue("@amt", amountPaid); });
        return sid;
    }

    private static Task<long> CountStatementsAsync(Database db, long cid) =>
        db.ScalarLongAsync("SELECT COUNT(*) FROM settlement_statements WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", cid));

    // A detention charge on a paid invoice, attributed to the driver (mirrors DetentionDriverPayPostgresTests).
    private static async Task SeedCollectedDetentionAsync(Database db, long cid, long driverId, decimal chargeAmount, decimal hours, DateTime paidOn)
    {
        var custId = await db.InsertAsync("INSERT INTO customers (company_id, customer_code, name) VALUES (@c, @code, 'Earn Cust') RETURNING id",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@code", $"ERC-{Guid.NewGuid():N}".Substring(0, 14)); });
        var vid = await db.InsertAsync("INSERT INTO vehicles (company_id, vehicle_code, type) VALUES (@c, @code, 'truck') RETURNING id",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@code", $"EV-{Guid.NewGuid():N}".Substring(0, 12)); });
        var gid = await db.InsertAsync(
            "INSERT INTO geofences (company_id, name, geofence_type, center_lat, center_lng, radius_meters, status, customer_id, site_role) VALUES (@c, 'Depot', 'circular', 34, -118, 300, 'Active', @cust, 'customer_site') RETURNING id",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@cust", custId); });
        var jobId = await db.InsertAsync(
            "INSERT INTO jobs (company_id, customer_id, job_code, job_type, status) VALUES (@c, @cust, @code, 'freight', 'delivered') RETURNING id",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@cust", custId); c.Parameters.AddWithValue("@code", $"EJ-{Guid.NewGuid():N}".Substring(0, 12)); });
        var chargeId = await db.InsertAsync(
            @"INSERT INTO job_charges (company_id, job_id, charge_code, charge_name, charge_type, quantity, unit_rate, amount, source, billing_status)
              VALUES (@c, @j, 'DETENTION', 'Detention', 'accessorial', @h, 60, @amt, 'detention', 'billed') RETURNING id",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@j", jobId); c.Parameters.AddWithValue("@h", hours); c.Parameters.AddWithValue("@amt", chargeAmount); });
        await db.ExecuteAsync(
            @"INSERT INTO detention_dwells (company_id, geofence_id, vehicle_id, driver_id, customer_id, job_id, entry_event_id, entered_at, quantity_hours, amount, status, job_charge_id)
              VALUES (@c, @g, @v, @d, @cust, @j, 1, NOW(), @h, @amt, 'charged', @ch)",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@g", gid); c.Parameters.AddWithValue("@v", vid);
                   c.Parameters.AddWithValue("@d", driverId); c.Parameters.AddWithValue("@cust", custId); c.Parameters.AddWithValue("@j", jobId);
                   c.Parameters.AddWithValue("@h", hours); c.Parameters.AddWithValue("@amt", chargeAmount); c.Parameters.AddWithValue("@ch", chargeId); });
        var draftNo = $"D-{Guid.NewGuid():N}".Substring(0, 12);
        var draft = (await db.QuerySingleAsync(
            "INSERT INTO invoice_drafts (company_id, customer_id, invoice_draft_no) VALUES (@c, @cust, @dno) RETURNING id",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@cust", custId); c.Parameters.AddWithValue("@dno", draftNo); }))!;
        var inv = (await db.QuerySingleAsync(
            @"INSERT INTO issued_invoices (company_id, customer_id, source_invoice_draft_id, source_invoice_draft_no, invoice_number, subtotal, tax_total, total, amount_paid, balance_due, payment_status, status, currency, issued_at, paid_at)
              VALUES (@c, @cust, @draft::uuid, @dno, @ino, @amt, 0, @amt, @amt, 0, 'paid', 'paid', 'USD', @paid - INTERVAL '1 day', @paid) RETURNING id",
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
                                  "invoice_drafts", "job_charges", "geofences", "jobs", "vehicles", "customers" })
            await db.ExecuteAsync($"DELETE FROM {t} WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", cid));
        await db.ExecuteAsync("DELETE FROM companies WHERE id=@c", c => c.Parameters.AddWithValue("@c", cid));
    }

    private static Database CreateDatabase() =>
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString }).Build());
}
