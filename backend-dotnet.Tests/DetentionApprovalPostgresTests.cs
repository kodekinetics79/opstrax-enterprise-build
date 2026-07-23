using Microsoft.Extensions.Configuration;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

// Detention Recovery — approval money-path (Increment 3). The queue is the ONLY path to billing:
// approval creates exactly one source='detention' job_charge (double-approve conflicts), the
// AP-clerk gates hold (missing shipper references block without an audited override), the frozen
// evidence bundle exists with its sha welded onto the charge, and the share token exposes the
// no-login packet. Fail-closed everywhere.
[Trait("Category", "Integration")]
public class DetentionApprovalPostgresTests
{
    [Fact]
    public async Task Approve_Creates_One_Charge_With_Evidence_And_Blocks_Double_Approve()
    {
        var db = CreateDatabase();
        await new DetentionSchemaService(db).EnsureAsync();
        var (cid, custId, vid, fenceId) = await SeedAsync(db);
        try
        {
            var jobId = await SeedPricedDwellAsync(db, cid, custId, vid, fenceId, withReferences: true);
            var svc = new DetentionReviewService(db);
            var dwellId = await DwellIdAsync(db, cid);

            var ok = await svc.ApproveAsync(cid, dwellId, userId: 9, overrideNote: null);
            Assert.True(ok.Ok);
            Assert.NotNull(ok.JobChargeId);

            // Exactly one detention charge, welded to the dwell with the evidence sha.
            var charge = (await db.QuerySingleAsync(
                "SELECT job_id, amount, source, billing_status, detention_dwell_id, evidence_sha256 FROM job_charges WHERE company_id=@c AND id=@id",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@id", ok.JobChargeId!.Value); }))!;
            Assert.Equal(jobId, Convert.ToInt64(charge["jobId"]));
            Assert.Equal("detention", charge["source"]?.ToString());
            Assert.Equal("unbilled", charge["billingStatus"]?.ToString());
            Assert.Equal(dwellId, Convert.ToInt64(charge["detentionDwellId"]));
            Assert.False(string.IsNullOrEmpty(charge["evidenceSha256"]?.ToString()));

            // Evidence bundle exists and its sha matches the charge (the weld).
            var ev = (await db.QuerySingleAsync(
                "SELECT evidence_sha256 FROM detention_evidence WHERE company_id=@c AND dwell_id=@d",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@d", dwellId); }))!;
            Assert.Equal(ev["evidenceSha256"]?.ToString(), charge["evidenceSha256"]?.ToString());

            // Double-approve conflicts — never a second charge.
            var again = await svc.ApproveAsync(cid, dwellId, userId: 9, overrideNote: null);
            Assert.False(again.Ok);
            var charges = await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM job_charges WHERE company_id=@c AND detention_dwell_id=@d",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@d", dwellId); });
            Assert.Equal(1, charges);

            // Share token -> the public packet resolves.
            var token = await svc.MintShareTokenAsync(cid, dwellId, 90);
            Assert.NotNull(token);
            var pub = await svc.GetEvidenceByTokenAsync(token!);
            Assert.NotNull(pub);
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task Missing_Shipper_References_Block_Approval_Unless_Overridden()
    {
        var db = CreateDatabase();
        await new DetentionSchemaService(db).EnsureAsync();
        var (cid, custId, vid, fenceId) = await SeedAsync(db);
        try
        {
            await SeedPricedDwellAsync(db, cid, custId, vid, fenceId, withReferences: false);
            var svc = new DetentionReviewService(db);
            var dwellId = await DwellIdAsync(db, cid);

            // AP-clerk gate: no PO/BOL/rate-con/appointment ref -> blocked.
            var blocked = await svc.ApproveAsync(cid, dwellId, userId: 9, overrideNote: null);
            Assert.False(blocked.Ok);
            Assert.Equal("missing_references", blocked.Reason);

            // An audited override note may waive.
            var overridden = await svc.ApproveAsync(cid, dwellId, userId: 9, overrideNote: "verbal rate-con ref confirmed by broker");
            Assert.True(overridden.Ok);
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task Funnel_Reports_Detected_Notified_And_Approved_Amounts()
    {
        var db = CreateDatabase();
        await new DetentionSchemaService(db).EnsureAsync();
        var (cid, custId, vid, fenceId) = await SeedAsync(db);
        try
        {
            await SeedPricedDwellAsync(db, cid, custId, vid, fenceId, withReferences: true);
            var svc = new DetentionReviewService(db);
            var dwellId = await DwellIdAsync(db, cid);
            await svc.ApproveAsync(cid, dwellId, userId: 9, overrideNote: null);

            var funnel = await svc.FunnelAsync(cid, DateTime.UtcNow.AddDays(-30), DateTime.UtcNow);
            Assert.Equal(1L, Convert.ToInt64(funnel["detectedCount"]));
            Assert.True(Convert.ToDecimal(funnel["detectedAmount"]) > 0);
            Assert.True(Convert.ToDecimal(funnel["approvedAmount"]) > 0);
        }
        finally { await CleanupAsync(db, cid); }
    }

    // ── helpers: drive the REAL pipeline (events -> detect -> price), then approve. ──
    private static async Task<long> SeedPricedDwellAsync(Database db, long cid, long custId, long vid, long fenceId, bool withReferences)
    {
        await db.ExecuteAsync(
            @"INSERT INTO detention_rule_cards (company_id, scope_type, scope_id, free_minutes, rate_per_hour, billing_increment_minutes, effective_date)
              VALUES (@c, 'customer', @cust, 60, 60, 15, CURRENT_DATE - 2)",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@cust", custId); });

        var t0 = DateTime.UtcNow.AddHours(-6);
        var jobId = await db.InsertAsync(
            @"INSERT INTO jobs (company_id, customer_id, job_code, job_type, pickup_latitude, pickup_longitude, po_number)
              VALUES (@c, @cust, @code, 'freight', 34.05, -118.24, @po) RETURNING id",
            c =>
            {
                c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@cust", custId);
                c.Parameters.AddWithValue("@code", $"DA-{Guid.NewGuid():N}".Substring(0, 12));
                c.Parameters.AddWithValue("@po", withReferences ? "PO-12345" : (object)DBNull.Value);
            });
        await db.ExecuteAsync(
            @"INSERT INTO dispatch_assignments (company_id, job_id, vehicle_id, assigned_at, planned_pickup_at)
              VALUES (@c, @j, @v, @assigned, @appt)",
            c =>
            {
                c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@j", jobId);
                c.Parameters.AddWithValue("@v", vid); c.Parameters.AddWithValue("@assigned", t0.AddHours(-1));
                c.Parameters.AddWithValue("@appt", t0);   // appointment == arrival: whole dwell counts
            });

        await db.ExecuteAsync(
            "INSERT INTO geofence_events (company_id, geofence_id, vehicle_id, event_type, event_time) VALUES (@c, @g, @v, 'Entry', @t)",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@g", fenceId); c.Parameters.AddWithValue("@v", vid); c.Parameters.AddWithValue("@t", t0); });
        await DetentionService.DetectAsync(db);
        await db.ExecuteAsync(
            "INSERT INTO geofence_events (company_id, geofence_id, vehicle_id, event_type, event_time) VALUES (@c, @g, @v, 'Exit', @t)",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@g", fenceId); c.Parameters.AddWithValue("@v", vid); c.Parameters.AddWithValue("@t", t0.AddHours(3)); });
        await DetentionService.DetectAsync(db);
        return jobId;
    }

    private static async Task<long> DwellIdAsync(Database db, long cid) =>
        await db.ScalarLongAsync("SELECT id FROM detention_dwells WHERE company_id=@c LIMIT 1",
            c => c.Parameters.AddWithValue("@c", cid));

    private static async Task<(long cid, long custId, long vid, long fenceId)> SeedAsync(Database db)
    {
        var cid = await db.InsertAsync("INSERT INTO companies (company_code, name, industry) VALUES (@code, 'DA Co', 'logistics') RETURNING id",
            c => c.Parameters.AddWithValue("@code", $"DA-{Guid.NewGuid():N}".Substring(0, 15)));
        var custId = await db.InsertAsync(
            "INSERT INTO customers (company_id, customer_code, name, contact_name, email) VALUES (@c, @code, 'DA Cust', 'AP', 'ap@x.example') RETURNING id",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@code", $"DAC-{Guid.NewGuid():N}".Substring(0, 14)); });
        var vid = await db.InsertAsync(
            "INSERT INTO vehicles (company_id, vehicle_code, type) VALUES (@c, @code, 'truck') RETURNING id",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@code", $"DAV-{Guid.NewGuid():N}".Substring(0, 12)); });
        var fenceId = await db.InsertAsync(
            @"INSERT INTO geofences (company_id, name, geofence_type, center_lat, center_lng, radius_meters, status, customer_id, site_role)
              VALUES (@c, 'DA DC', 'circular', 34.05, -118.24, 300, 'Active', @cust, 'customer_site') RETURNING id",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@cust", custId); });
        return (cid, custId, vid, fenceId);
    }

    private static async Task CleanupAsync(Database db, long cid)
    {
        // Evidence is immutable by trigger in production; the test (superuser) disables it for teardown only.
        await db.ExecuteAsync("ALTER TABLE detention_evidence DISABLE TRIGGER trg_detention_evidence_immutable");
        try
        {
            await db.ExecuteAsync("DELETE FROM detention_evidence WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", cid));
        }
        finally
        {
            await db.ExecuteAsync("ALTER TABLE detention_evidence ENABLE TRIGGER trg_detention_evidence_immutable");
        }
        await db.ExecuteAsync("DELETE FROM outbox_messages WHERE tenant_id=@t", c => c.Parameters.AddWithValue("@t", cid));
        foreach (var t in new[] { "detention_notices", "detention_dwell_events", "detention_dwells",
                                  "detention_rule_cards", "notifications", "invoice_draft_lines", "invoice_drafts",
                                  "job_charges", "geofence_events", "geofences", "dispatch_assignments", "jobs", "vehicles", "customers" })
            await db.ExecuteAsync($"DELETE FROM {t} WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", cid));
        await db.ExecuteAsync("DELETE FROM companies WHERE id=@c", c => c.Parameters.AddWithValue("@c", cid));
    }

    private static Database CreateDatabase() =>
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString }).Build());
}
