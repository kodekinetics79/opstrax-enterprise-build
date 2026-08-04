using Microsoft.Extensions.Configuration;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

// Settlement / carrier-&-driver-pay (AP) — ADR-007 §C, Phase 1 (driver flat/per-mile).
// Verified against real Postgres: pay computed from load + agreement, idempotent regenerate,
// fail-closed on missing/unsupported inputs, and the approve → pay lifecycle.
[Trait("Category", "Integration")]
public class SettlementServicePostgresTests
{
    private const long Driver = 9001;
    private static readonly DateOnly PStart = new(2026, 1, 1);
    private static readonly DateOnly PEnd = new(2026, 12, 31);

    [Fact]
    public async Task PerMile_Two_Loads_Commits_One_Statement_With_Lines()
    {
        var (db, svc, cid) = Setup();
        try
        {
            await AgreementAsync(db, cid, "per_mile", 0.55m);
            await DeliveredLoadAsync(db, cid, Driver, 100m);
            await DeliveredLoadAsync(db, cid, Driver, 200m);

            var o = await svc.GenerateDriverStatementAsync(cid, Driver, PStart, PEnd, SettlementMode.Commit);

            Assert.True(o.Generated);
            Assert.Equal(2, o.Lines.Count);
            Assert.Equal(165.00m, o.Total);   // 100*0.55 + 200*0.55
            var lineCount = await db.ScalarLongAsync("SELECT COUNT(*) FROM settlement_lines WHERE company_id=@c AND statement_id=@s",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@s", o.StatementId!); });
            Assert.Equal(2, lineCount);
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task Regenerate_Is_Idempotent()
    {
        var (db, svc, cid) = Setup();
        try
        {
            await AgreementAsync(db, cid, "per_mile", 0.55m);
            await DeliveredLoadAsync(db, cid, Driver, 100m);
            await DeliveredLoadAsync(db, cid, Driver, 200m);

            await svc.GenerateDriverStatementAsync(cid, Driver, PStart, PEnd, SettlementMode.Commit);
            var o = await svc.GenerateDriverStatementAsync(cid, Driver, PStart, PEnd, SettlementMode.Commit);

            var statements = await db.ScalarLongAsync("SELECT COUNT(*) FROM settlement_statements WHERE company_id=@c AND payee_id=@d",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@d", Driver); });
            var lines = await db.ScalarLongAsync("SELECT COUNT(*) FROM settlement_lines WHERE company_id=@c AND statement_id=@s",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@s", o.StatementId!); });
            Assert.Equal(1, statements);
            Assert.Equal(2, lines);
            Assert.Equal(165.00m, o.Total);
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task MinPay_Floors_The_Line()
    {
        var (db, svc, cid) = Setup();
        try
        {
            await AgreementAsync(db, cid, "per_mile", 0.55m, minPay: 100m);
            await DeliveredLoadAsync(db, cid, Driver, 100m);   // 55 < 100 floor

            var o = await svc.GenerateDriverStatementAsync(cid, Driver, PStart, PEnd, SettlementMode.Commit);

            Assert.True(o.Generated);
            Assert.Equal(100.00m, o.Total);
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task FailClosed_No_Agreement_Writes_Nothing()
    {
        var (db, svc, cid) = Setup();
        try
        {
            await DeliveredLoadAsync(db, cid, Driver, 100m);   // load exists, but no pay agreement

            var o = await svc.GenerateDriverStatementAsync(cid, Driver, PStart, PEnd, SettlementMode.Commit);

            Assert.False(o.Generated);
            Assert.Equal("no_pay_agreement", o.Reason);
            var statements = await db.ScalarLongAsync("SELECT COUNT(*) FROM settlement_statements WHERE company_id=@c",
                c => c.Parameters.AddWithValue("@c", cid));
            Assert.Equal(0, statements);
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task Percent_Basis_Is_FailClosed_In_Phase1()
    {
        var (db, svc, cid) = Setup();
        try
        {
            await AgreementAsync(db, cid, "percent", 25m);
            await DeliveredLoadAsync(db, cid, Driver, 100m);

            var o = await svc.GenerateDriverStatementAsync(cid, Driver, PStart, PEnd, SettlementMode.Commit);

            Assert.False(o.Generated);
            Assert.StartsWith("basis_unsupported_phase1", o.Reason);
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task Approve_Then_Full_Payment_Marks_Paid()
    {
        var (db, svc, cid) = Setup();
        try
        {
            await AgreementAsync(db, cid, "flat", 300m);
            await DeliveredLoadAsync(db, cid, Driver, 100m);

            var o = await svc.GenerateDriverStatementAsync(cid, Driver, PStart, PEnd, SettlementMode.Commit);
            var sid = o.StatementId!.Value;

            // Cannot pay before approval.
            var early = await svc.RecordPaymentAsync(cid, sid, 300m, "ach", "R1", "idem-early", 42);
            Assert.False(early.Ok);
            Assert.Equal("not_approved", early.Reason);

            var approve = await svc.ApproveStatementAsync(cid, sid, 42);
            Assert.True(approve.Ok);

            var pay = await svc.RecordPaymentAsync(cid, sid, 300m, "ach", "R1", "idem-1", 42);
            Assert.True(pay.Ok);
            Assert.Equal("paid", pay.Status);
            Assert.Equal(300.00m, pay.AmountPaid);

            // Idempotent payment: same key does not double-pay.
            var dup = await svc.RecordPaymentAsync(cid, sid, 300m, "ach", "R1", "idem-1", 42);
            Assert.True(dup.Ok);
            var payments = await db.ScalarLongAsync("SELECT COUNT(*) FROM settlement_payments WHERE company_id=@c AND statement_id=@s",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@s", sid); });
            Assert.Equal(1, payments);
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task Approved_Statement_Cannot_Be_Regenerated()
    {
        var (db, svc, cid) = Setup();
        try
        {
            await AgreementAsync(db, cid, "flat", 300m);
            await DeliveredLoadAsync(db, cid, Driver, 100m);

            var o = await svc.GenerateDriverStatementAsync(cid, Driver, PStart, PEnd, SettlementMode.Commit);
            await svc.ApproveStatementAsync(cid, o.StatementId!.Value, 42);

            var regen = await svc.GenerateDriverStatementAsync(cid, Driver, PStart, PEnd, SettlementMode.Commit);
            Assert.False(regen.Generated);
            Assert.Equal("statement_locked", regen.Reason);

            var status = (await db.QuerySingleAsync("SELECT status FROM settlement_statements WHERE id=@s",
                c => c.Parameters.AddWithValue("@s", o.StatementId!.Value)))!["status"]?.ToString();
            Assert.Equal("approved", status);
        }
        finally { await CleanupAsync(db, cid); }
    }

    // ── helpers ──

    private static (Database, SettlementService, long) Setup()
    {
        var db = new Database(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString }).Build());
        var cid = SeedCompanyAsync(db).GetAwaiter().GetResult();
        return (db, new SettlementService(db), cid);
    }

    private static async Task<long> SeedCompanyAsync(Database db) =>
        await db.InsertAsync("INSERT INTO companies (company_code, name, industry) VALUES (@code, 'STL Co', 'logistics') RETURNING id",
            c => c.Parameters.AddWithValue("@code", $"STL-{Guid.NewGuid():N}".Substring(0, 18)));

    private static async Task AgreementAsync(Database db, long cid, string basis, decimal rate, decimal? minPay = null) =>
        await db.ExecuteAsync(
            @"INSERT INTO pay_agreements (company_id, agreement_code, agreement_name, payee_type, payee_id, basis, rate, min_pay, effective_date, status)
              VALUES (@c, @code, 'Driver default', 'driver', NULL, @basis, @rate, @min, DATE '2025-01-01', 'active')",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@code", $"PA-{cid}-{basis}");
                   c.Parameters.AddWithValue("@basis", basis); c.Parameters.AddWithValue("@rate", rate);
                   c.Parameters.AddWithValue("@min", (object?)minPay ?? DBNull.Value); });

    private static async Task DeliveredLoadAsync(Database db, long cid, long driverId, decimal miles)
    {
        var jobId = await db.InsertAsync(
            "INSERT INTO jobs (company_id, job_code, job_type, status) VALUES (@c, @code, 'freight', 'delivered') RETURNING id",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@code", $"J-{cid}-{Guid.NewGuid():N}".Substring(0, 24)); });
        await db.ExecuteAsync("INSERT INTO trips (company_id, job_id, actual_distance_miles) VALUES (@c, @j, @m)",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@j", jobId); c.Parameters.AddWithValue("@m", miles); });
        await db.ExecuteAsync(
            @"INSERT INTO dispatch_assignments (company_id, job_id, driver_id, assignment_status, status, actual_delivery_at)
              VALUES (@c, @j, @d, 'delivered', 'Delivered', TIMESTAMPTZ '2026-06-15T12:00:00Z')",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@j", jobId); c.Parameters.AddWithValue("@d", driverId); });
    }

    private static async Task CleanupAsync(Database db, long cid)
    {
        foreach (var t in new[] { "settlement_payments", "settlement_lines", "settlement_statements", "pay_agreements", "dispatch_assignments", "trips", "jobs" })
            await db.ExecuteAsync($"DELETE FROM {t} WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", cid));
        await db.ExecuteAsync("DELETE FROM companies WHERE id=@c", c => c.Parameters.AddWithValue("@c", cid));
    }
}
