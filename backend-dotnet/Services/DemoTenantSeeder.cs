using System.Globalization;
using Opstrax.Api.Data;
using Opstrax.Api.Foundation;

namespace Opstrax.Api.Services;

public sealed record DemoSeedResult(
    bool AlreadySeeded,
    long CompanyId,
    string CompanyName,
    int Vehicles,
    int Drivers,
    int Customers,
    int Jobs,
    int Trips,
    int DispatchAssignments,
    int ProofPackages,
    int IssuedInvoices,
    int Payments,
    int Feedback,
    int Alerts,
    int SafetyEvents,
    int WorkOrders,
    string Message);

// Builds ONE realistic demo tenant ("Meridian Logistics — Demo"). The revenue chain
// (charges → mark-ready → invoice_draft → issue → payment) and customer feedback go
// through the REAL service layer (BusinessSpineService / RevenueReadinessService /
// CustomerPortalService) — the same code paths a real user action hits, so validation
// runs and any bug surfaces here. Base entities (vehicles/drivers/customers/jobs/trips/
// dispatch/proofs) have no dedicated service layer and are created directly.
// Idempotent: if the demo company already exists, it reports "already seeded" and stops.
public sealed class DemoTenantSeeder(Database db)
{
    public const string DemoCompanyCode = "MERIDIAN-DEMO";
    public const string DemoCompanyName = "Meridian Logistics — Demo";

    public async Task<DemoSeedResult> SeedAsync(CancellationToken ct = default)
    {
        var existing = await db.ScalarLongAsync(
            "SELECT COALESCE((SELECT id FROM companies WHERE company_code=@code LIMIT 1), 0)",
            c => c.Parameters.AddWithValue("@code", DemoCompanyCode), ct);
        if (existing > 0)
        {
            return new DemoSeedResult(true, existing, DemoCompanyName, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                "Demo tenant already exists — skipped (idempotent).");
        }

        var companyId = await db.InsertAsync(
            @"INSERT INTO companies (company_code, name, industry, timezone, status)
              VALUES (@code, @name, 'Transport & Logistics', 'America/New_York', 'Active')",
            c => { c.Parameters.AddWithValue("@code", DemoCompanyCode); c.Parameters.AddWithValue("@name", DemoCompanyName); }, ct);

        var correlation = new InMemoryCorrelationContext($"demo-{Guid.NewGuid():N}", $"demo-cause-{Guid.NewGuid():N}", $"demo-req-{Guid.NewGuid():N}", companyId.ToString(), ActorTypes.TenantUser, "1");
        var spine = new BusinessSpineService(db);
        var approval = new PostgresApprovalWorkflowService(db, correlation);
        var revenue = new RevenueReadinessService(db, new PostgresAiFoundationService(db, correlation), approval, new PostgresIdempotencyService(db), new PostgresDomainEventPublisher(db, correlation), correlation);
        var portal = new CustomerPortalService(db);

        // ── Customers (3) ──
        var customers = new List<long>();
        foreach (var (code, name) in new[] { ("MER-ACME", "Acme Freight Co."), ("MER-NORTH", "Northwind Retail"), ("MER-COLD", "ColdChain Pharma") })
            customers.Add(await SeedCustomerAsync(companyId, code, name, ct));

        var contractId = await db.InsertAsync(
            @"INSERT INTO contracts (company_id, customer_id, contract_code, title, rate_type, status, effective_date, expiration_date)
              VALUES (@companyId, @customerId, 'MER-CONTRACT-1', 'Master lane agreement', 'Per Mile', 'Active', CURRENT_DATE, CURRENT_DATE + INTERVAL '365 days')",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@customerId", customers[0]); }, ct);

        // Real service: rate card (used by charges + below-min leakage math).
        var rateCard = await spine.CreateRateCardAsync(companyId, "MER-RC-1", "Standard lane", customers[0], contractId,
            "Per Mile", "Metro", "North", "South", "Truck", "USD", 3.75m, 300.00m, 6.5m, "Base",
            DateOnly.FromDateTime(DateTime.UtcNow.Date), null, "Active");

        // ── Drivers (5), one with an expiring compliance document ──
        var drivers = new List<long>();
        var driverStatuses = new[] { "Available", "On Duty", "On Duty", "Off Duty", "Available" };
        for (var i = 0; i < 5; i++)
            drivers.Add(await SeedDriverAsync(companyId, $"MER-DRV-{i + 1}", $"Driver {i + 1}", driverStatuses[i], ct));
        await db.ExecuteAsync(
            @"INSERT INTO documents (company_id, entity_type, entity_id, document_type, title, status, expires_at)
              VALUES (@companyId, 'driver', @driverId, 'Medical Certificate', 'DOT Medical Card', 'Expiring', CURRENT_DATE + INTERVAL '12 days')",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@driverId", drivers[0]); }, ct);

        // ── Vehicles (5), varied type/status ──
        var vehicles = new List<long>();
        var vehicleSpecs = new (string code, string type, string status)[]
        {
            ("MER-TRK-1", "Truck", "Available"),
            ("MER-VAN-1", "Van", "On Route"),
            ("MER-REEF-1", "Reefer", "On Route"),
            ("MER-BOX-1", "Box Truck", "Maintenance"),
            ("MER-TRK-2", "Truck", "Available"),
        };
        foreach (var (code, type, status) in vehicleSpecs)
            vehicles.Add(await SeedVehicleAsync(companyId, code, type, status, ct));

        // ── Jobs (12) spanning every status ──
        // Showcase statuses (indices 0-7) stay as seeded; the last 4 (indices 8-11) are
        // seeded 'completed' and then transitioned to 'ready_to_bill' by the REAL finance
        // chain below — so every status badge (incl. completed, delivered, ready_to_bill)
        // has at least one row after seeding.
        var jobStatuses = new[]
        {
            "draft", "scheduled", "assigned", "in_progress", "exception", "cancelled",
            "completed", "delivered", "completed", "completed", "completed", "completed",
        };
        var jobs = new List<long>();
        for (var i = 0; i < jobStatuses.Length; i++)
            jobs.Add(await SeedJobAsync(companyId, customers[i % customers.Count], contractId, rateCard.Id, $"MER-JOB-{i + 1}", jobStatuses[i], ct));
        var completedJobs = new[] { jobs[8], jobs[9], jobs[10], jobs[11] };

        // ── Trips + stops (for assigned/in_progress/completed), incl. one exception ──
        var trips = 0;
        foreach (var (jobId, status) in new[] { (jobs[2], "active"), (jobs[3], "active"), (jobs[4], "exception"), (completedJobs[0], "completed") })
        {
            var tripId = await db.InsertAsync(
                @"INSERT INTO trips (company_id, job_id, status, started_at) VALUES (@companyId, @jobId, @status, NOW() - INTERVAL '3 hours')",
                c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@jobId", jobId); c.Parameters.AddWithValue("@status", status); }, ct);
            trips++;
            await db.ExecuteAsync(
                @"INSERT INTO trip_stops (company_id, trip_id, stop_sequence, stop_type, status)
                  VALUES (@companyId, @tripId, 1, 'pickup', 'completed'), (@companyId, @tripId, 2, 'dropoff', @dropStatus)",
                c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@tripId", tripId); c.Parameters.AddWithValue("@dropStatus", status == "exception" ? "exception" : "completed"); }, ct);
        }

        // ── Dispatch assignments — accept / reject / complete lifecycle ──
        var dispatch = 0;
        foreach (var (jobId, aStatus) in new[] { (jobs[2], "accepted"), (jobs[3], "in_transit"), (jobs[5], "rejected"), (completedJobs[0], "delivered") })
        {
            await db.ExecuteAsync(
                @"INSERT INTO dispatch_assignments (company_id, job_id, vehicle_id, driver_id, status, assigned_at)
                  VALUES (@companyId, @jobId, @vehicleId, @driverId, @status, NOW() - INTERVAL '4 hours')",
                c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@jobId", jobId); c.Parameters.AddWithValue("@vehicleId", vehicles[0]); c.Parameters.AddWithValue("@driverId", drivers[0]); c.Parameters.AddWithValue("@status", aStatus); }, ct);
            dispatch++;
        }

        // ── Proof packages — validated / rejected / pending ──
        var proofValidation = new[] { "validated", "rejected", "pending" };
        var proofs = 0;
        for (var i = 0; i < 3; i++)
        {
            await db.ExecuteAsync(
                @"INSERT INTO proof_packages (company_id, job_id, proof_type, status, validation_status, completed_at, receiver_name, geo_latitude, geo_longitude)
                  VALUES (@companyId, @jobId, 'POD', 'completed', @vstatus, NOW() - INTERVAL '1 day', @receiver, 38.9, -77.4)",
                c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@jobId", completedJobs[i]); c.Parameters.AddWithValue("@vstatus", proofValidation[i]); c.Parameters.AddWithValue("@receiver", $"Dock {i + 1}"); }, ct);
            proofs++;
        }

        // ── REAL finance chain → AR aging spread (paid / current / 30-60 / 90+) ──
        var chargeAmounts = new[] { 1450.00m, 2100.50m, 875.25m, 3300.00m };
        var invoiceIds = new List<Guid>();
        for (var i = 0; i < completedJobs.Length; i++)
        {
            var jobId = completedJobs[i];
            await spine.CreateJobChargeAsync(companyId, jobId, null, rateCard.Id, "BASE", "Line haul", "base", $"Charge for job {jobId}", 1m, chargeAmounts[i], chargeAmounts[i], "USD", "approved", ct: ct);
            await revenue.MarkJobReadyToBillAsync(companyId, jobId, ct);
            var draft = await revenue.CreateInvoiceDraftFromJobAsync(companyId, jobId, $"demo-draft-{jobId}", ct);
            var gate = await revenue.UpdateInvoiceDraftAsync(companyId, draft.Draft!.Id, "approved", null, ct);
            approval.Decide(gate.ApprovalRequestId!.Value, "demo-approver", "approved", "demo seed auto-approval");
            var issued = await revenue.IssueInvoiceFromDraftAsync(companyId, draft.Draft.Id, $"demo-issue-{jobId}", ct);
            invoiceIds.Add(issued.Invoice!.Id);
        }

        // Invoice 0: PAID (real payment via service).
        await revenue.RecordInvoicePaymentAsync(companyId, invoiceIds[0], chargeAmounts[0], "USD", "MER-PAY-1", "manual", null, ct);
        var payments = 1;
        // Age the issued invoices into buckets (issued_at/due_at adjusted for a realistic demo spread).
        await AgeInvoiceAsync(invoiceIds[1], issuedDaysAgo: 5, dueInDays: 20, ct);   // current
        await AgeInvoiceAsync(invoiceIds[2], issuedDaysAgo: 75, dueInDays: -45, ct); // 30-60 overdue
        await AgeInvoiceAsync(invoiceIds[3], issuedDaysAgo: 150, dueInDays: -120, ct); // 90+ overdue

        // ── Customer feedback (real service — validates job ownership) ──
        // Feedback must target a job that BELONGS to the customer; the real service
        // rejects otherwise. jobs[6] is a completed job owned by customers[0] (6 % 3 == 0).
        var feedbackResult = await portal.SubmitFeedbackAsync(companyId, customers[0], jobs[6], 5, "On time, driver was great.", "praise", "Great delivery", ct);
        var feedback = feedbackResult is not null ? 1 : 0;

        // ── A few non-core rows so those modules aren't empty ──
        await db.ExecuteAsync(
            @"INSERT INTO telemetry_alerts (company_id, alert_type, message, severity, status, vehicle_id)
              VALUES (@companyId, 'harsh_braking', 'Harsh braking detected', 'High', 'open', @v1),
                     (@companyId, 'geofence_exit', 'Vehicle left geofence', 'Medium', 'open', @v2)",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@v1", vehicles[1]); c.Parameters.AddWithValue("@v2", vehicles[2]); }, ct);
        await db.ExecuteAsync(
            @"INSERT INTO safety_events (company_id, event_type, severity, status, driver_id, vehicle_id)
              VALUES (@companyId, 'Speeding', 'Medium', 'Under Review', @driverId, @vehicleId)",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@driverId", drivers[0]); c.Parameters.AddWithValue("@vehicleId", vehicles[1]); }, ct);
        await db.ExecuteAsync(
            @"INSERT INTO work_orders (company_id, work_order_code, title, status, priority, vehicle_id)
              VALUES (@companyId, 'MER-WO-1', 'Brake inspection', 'Open', 'High', @vehicleId)",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@vehicleId", vehicles[3]); }, ct);

        // ── Demo login users (so the tenant is usable in a live demo) ──
        // Note: the granular finance.* perms gate the API; the frontend Finance/AR *routes*
        // (/invoices, /cost-leakage, …) gate on the coarse legacy "finance:view". Grant both so
        // the demo admin can actually reach the AR/finance pages in the browser, not just the API.
        const string internalPerms = "[\"dashboard:view\",\"vehicles:view\",\"vehicles:create\",\"vehicles:update\",\"drivers:view\",\"shipments:view\",\"shipments:create\",\"shipments:update\",\"dispatch:view\",\"dispatch:create\",\"dispatch:assign\",\"customers:view\",\"customers:create\",\"maintenance:view\",\"maintenance:manage\",\"compliance:view\",\"alerts:view\",\"alerts:acknowledge\",\"reports:view\",\"reports:export\",\"safety:view\",\"operations.proof.read\",\"operations.proof.validate\",\"finance:view\",\"finance.invoice.read\",\"finance.ar.summary.read\",\"finance.revenue.summary.read\",\"finance.job.ready_to_bill\",\"finance.invoice_draft.read\",\"finance.invoice_draft.create\",\"finance.invoice.issue\",\"finance.invoice.payment.record\",\"notifications:view\",\"settings:view\"]";
        await SeedUserAsync(companyId, "admin@meridian.demo", "Meridian Ops Admin", "Fleet Manager", null, internalPerms, ct);
        await SeedUserAsync(companyId, "portal@acme.demo", "Acme Portal User", "Customer Portal User", customers[0], "[\"customer_portal:view\",\"shipments:view\"]", ct);

        return new DemoSeedResult(false, companyId, DemoCompanyName,
            vehicles.Count, drivers.Count, customers.Count, jobs.Count, trips, dispatch, proofs,
            invoiceIds.Count, payments, feedback, 2, 1, 1,
            "Demo tenant seeded via real service layer (finance chain + feedback) and base-entity creation. Logins: admin@meridian.demo / portal@acme.demo (password: MeridianDemo!23).");
    }

    private async Task AgeInvoiceAsync(Guid invoiceId, int issuedDaysAgo, int dueInDays, CancellationToken ct)
        => await db.ExecuteAsync(
            "UPDATE issued_invoices SET issued_at = NOW() - make_interval(days => @issued), due_at = NOW() + make_interval(days => @due) WHERE id=@id",
            c => { c.Parameters.AddWithValue("@id", invoiceId); c.Parameters.AddWithValue("@issued", issuedDaysAgo); c.Parameters.AddWithValue("@due", dueInDays); }, ct);

    private async Task SeedUserAsync(long companyId, string email, string fullName, string roleName, long? customerId, string permissionsJson, CancellationToken ct)
        => await db.ExecuteAsync(
            @"INSERT INTO users (company_id, customer_id, full_name, email, role_name, status, demo_password, permissions_json)
              VALUES (@companyId, @customerId, @fullName, @email, @roleName, 'Active', 'MeridianDemo!23', @perms::jsonb)",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@customerId", (object?)customerId ?? DBNull.Value);
                c.Parameters.AddWithValue("@fullName", fullName);
                c.Parameters.AddWithValue("@email", email);
                c.Parameters.AddWithValue("@roleName", roleName);
                c.Parameters.AddWithValue("@perms", permissionsJson);
            }, ct);

    private async Task<long> SeedCustomerAsync(long companyId, string code, string name, CancellationToken ct)
        => await db.InsertAsync(
            @"INSERT INTO customers (company_id, customer_code, name, contact_name, email, phone, status, sla_tier, sla_health_score, delivery_experience_score, risk_score)
              VALUES (@companyId, @code, @name, 'Accounts Payable', @email, '+1 571 555 0100', 'Active', 'Standard', 94, 92, 12)",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@code", code); c.Parameters.AddWithValue("@name", name); c.Parameters.AddWithValue("@email", $"ap@{code.ToLowerInvariant()}.example"); }, ct);

    private async Task<long> SeedDriverAsync(long companyId, string code, string name, string status, CancellationToken ct)
        => await db.InsertAsync(
            @"INSERT INTO drivers (company_id, driver_code, full_name, phone, email, license_number, status, safety_score, readiness_score, risk_score, compliance_score)
              VALUES (@companyId, @code, @name, '+1 571 555 0200', @email, @lic, @status, 88, 90, 14, 91)",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@code", code); c.Parameters.AddWithValue("@name", name); c.Parameters.AddWithValue("@email", $"{code.ToLowerInvariant()}@meridian.example"); c.Parameters.AddWithValue("@lic", $"VA-{code}"); c.Parameters.AddWithValue("@status", status); }, ct);

    private async Task<long> SeedVehicleAsync(long companyId, string code, string type, string status, CancellationToken ct)
        => await db.InsertAsync(
            @"INSERT INTO vehicles (company_id, vehicle_code, type, make, model, year, status, odometer_miles, readiness_score, risk_score)
              VALUES (@companyId, @code, @type, 'Freightliner', 'Cascadia', 2022, @status, 82000, 93, 11)",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@code", code); c.Parameters.AddWithValue("@type", type); c.Parameters.AddWithValue("@status", status); }, ct);

    private async Task<long> SeedJobAsync(long companyId, long customerId, long contractId, long rateCardId, string jobCode, string status, CancellationToken ct)
        => await db.InsertAsync(
            @"INSERT INTO jobs (company_id, customer_id, contract_id, rate_card_id, job_code, job_number, job_type, pickup_address, dropoff_address, scheduled_start, scheduled_end, status, priority)
              VALUES (@companyId, @customerId, @contractId, @rateCardId, @jobCode, @jobCode, 'Delivery', '100 Origin St, Manassas VA', '900 Dest Ave, Richmond VA', NOW() - INTERVAL '1 day', NOW() - INTERVAL '1 day' + INTERVAL '5 hours', @status, 'Normal')",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@customerId", customerId); c.Parameters.AddWithValue("@contractId", contractId); c.Parameters.AddWithValue("@rateCardId", rateCardId); c.Parameters.AddWithValue("@jobCode", jobCode); c.Parameters.AddWithValue("@status", status); }, ct);
}
