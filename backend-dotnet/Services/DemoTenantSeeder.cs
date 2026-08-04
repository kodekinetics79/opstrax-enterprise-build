using System.Globalization;
using System.Security.Cryptography;
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
public sealed class DemoTenantSeeder(Database db, IConfiguration? config = null)
{
    public const string DemoCompanyCode = "MERIDIAN-DEMO";
    public const string DemoCompanyName = "Meridian Logistics — Demo";
    internal const string SafetyPilotFixtureKey = "safety-pilot";
    internal const int SafetyPilotFixtureVersion = 7;
    private readonly string demoPassword = ResolveDemoPassword(config);
    // Deterministic late-failure injection for transaction regression tests. Production
    // DI never sets this; keeping the hook internal prevents it becoming an app feature.
    internal Func<string, CancellationToken, Task>? TestCheckpointAsync { get; init; }

    // companyCode/companyName are overridable so integration TESTS can seed an ISOLATED
    // throwaway tenant (e.g. "MERIDIAN-DEMO-TEST") without touching the real runtime demo
    // tenant a pilot is using. Runtime callers use the defaults.
    public async Task<DemoSeedResult> SeedAsync(CancellationToken ct = default)
        => await SeedAsync(DemoCompanyCode, DemoCompanyName, ct);

    public Task<DemoSeedResult> SeedAsync(string companyCode, string companyName, CancellationToken ct = default)
        => db.RunInSystemTransactionAsync(async () =>
    {
        // Serialize by stable company code before checking existence. This closes the
        // concurrent first-run race while preserving the existing no-op result for a
        // completed tenant. The lock is transaction-scoped and released on commit/rollback.
        await db.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended(@code, 0))",
            c => c.Parameters.AddWithValue("@code", companyCode), ct);
        var existing = await db.ScalarLongAsync(
            "SELECT COALESCE((SELECT id FROM companies WHERE company_code=@code LIMIT 1), 0)",
            c => c.Parameters.AddWithValue("@code", companyCode), ct);
        if (existing > 0)
        {
            await ReconcileSafetyPilotFixtureAsync(existing, companyCode, ct);
            return new DemoSeedResult(true, existing, companyName, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                "Demo tenant already exists — versioned pilot fixtures reconciled idempotently.");
        }

        var companyId = await db.InsertAsync(
            @"INSERT INTO companies (company_code, name, industry, timezone, status)
              VALUES (@code, @name, 'Transport & Logistics', 'America/New_York', 'Active')",
            c => { c.Parameters.AddWithValue("@code", companyCode); c.Parameters.AddWithValue("@name", companyName); }, ct);

        var correlation = new InMemoryCorrelationContext($"demo-{Guid.NewGuid():N}", $"demo-cause-{Guid.NewGuid():N}", $"demo-req-{Guid.NewGuid():N}", companyId.ToString(), ActorTypes.TenantUser, "1");
        var spine = new BusinessSpineService(db);
        var approval = new PostgresApprovalWorkflowService(db, correlation);
        var revenue = new RevenueReadinessService(db, new PostgresAiFoundationService(db, correlation), approval, new PostgresIdempotencyService(db), new PostgresDomainEventPublisher(db, correlation), correlation, new TaxService(db));
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
        var demoAssignments = new[] { (jobs[2], "accepted"), (jobs[3], "in_transit"), (jobs[5], "rejected"), (completedJobs[0], "delivered") };
        for (var assignmentIndex = 0; assignmentIndex < demoAssignments.Length; assignmentIndex++)
        {
            var (jobId, aStatus) = demoAssignments[assignmentIndex];
            // Set BOTH status (legacy display) and assignment_status (the canonical lowercase
            // P4 token the dispatch state machine reads) — otherwise assignment_status is empty
            // and every status transition is rejected as "from '' to ...". Each demo lifecycle
            // gets its own driver/vehicle pair so active accepted/in-transit rows preserve the
            // same unique-allocation invariant enforced in production.
            await db.ExecuteAsync(
                @"INSERT INTO dispatch_assignments (company_id, job_id, vehicle_id, driver_id, status, assignment_status, assigned_at)
                  VALUES (@companyId, @jobId, @vehicleId, @driverId, @status, @status, NOW() - INTERVAL '4 hours')",
                c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@jobId", jobId); c.Parameters.AddWithValue("@vehicleId", vehicles[assignmentIndex]); c.Parameters.AddWithValue("@driverId", drivers[assignmentIndex]); c.Parameters.AddWithValue("@status", aStatus); }, ct);
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

        if (TestCheckpointAsync is not null)
            await TestCheckpointAsync("after-finance-before-feedback", ct);

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

        // ── P1 module demo data so Maintenance / Safety-coaching / Compliance-DVIR /
        //    Customer-portal-visibility pages render real rows, not empty states. ──
        // Preventive maintenance items (due + overdue) for the Maintenance module.
        await db.ExecuteAsync(
            @"INSERT INTO maintenance_items (company_id, vehicle_id, title, category, service_type, status, priority, due_date, due_odometer, estimated_cost)
              VALUES (@companyId, @v1, 'Oil change (5k)', 'Preventive', 'Oil Change', 'Open', 'Medium', CURRENT_DATE + 5, 5000, 180),
                     (@companyId, @v2, 'Tire rotation',   'Preventive', 'Tire Service', 'Open', 'High', CURRENT_DATE - 3, 8000, 120)",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@v1", vehicles[0]); c.Parameters.AddWithValue("@v2", vehicles[1]); }, ct);
        // Coaching task tied to the seeded safety event driver (Safety module).
        await db.ExecuteAsync(
            @"INSERT INTO coaching_tasks (company_id, task_number, driver_id, coaching_type, title, status, priority, due_at)
              VALUES (@companyId, 'MER-COACH-1', @driverId, 'Speed Management', 'Speed management coaching', 'Assigned', 'Medium', NOW() + INTERVAL '3 day')",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@driverId", drivers[0]); }, ct);
        // A DVIR report + one open defect (Compliance / DVIR module).
        var dvirId = await db.ScalarLongAsync(
            @"INSERT INTO dvir_reports (company_id, report_number, driver_id, vehicle_id, inspection_type, inspection_status, safe_to_operate, submitted_at)
              VALUES (@companyId, 'MER-DVIR-1', @driverId, @vehicleId, 'Pre-Trip', 'Submitted', false, NOW() - INTERVAL '2 hour') RETURNING id",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@driverId", drivers[0]); c.Parameters.AddWithValue("@vehicleId", vehicles[2]); }, ct);
        await db.ExecuteAsync(
            @"INSERT INTO dvir_defects (company_id, dvir_report_id, defect_category, defect_description, severity, status, vehicle_id)
              VALUES (@companyId, @dvirId, 'Brakes', 'Brake pad wear beyond limit', 'Major', 'Open', @vehicleId)",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@dvirId", dvirId); c.Parameters.AddWithValue("@vehicleId", vehicles[2]); }, ct);
        // A couple of notifications so the Notification Center isn't empty.
        await db.ExecuteAsync(
            @"INSERT INTO notifications (company_id, event_type, source_type, severity, title, body, message, status)
              VALUES (@companyId, 'maintenance_due', 'maintenance', 'Medium', 'PM due soon', 'Tire rotation is overdue for a vehicle.', 'Tire rotation is overdue for a vehicle.', 'unread'),
                     (@companyId, 'sla_risk', 'dispatch', 'High', 'SLA at risk', 'A shipment is approaching its SLA window.', 'A shipment is approaching its SLA window.', 'unread')",
            c => { c.Parameters.AddWithValue("@companyId", companyId); }, ct);

        // ── Demo login users (so the tenant is usable in a live demo) ──
        // Note: the granular finance.* perms gate the API; the frontend Finance/AR *routes*
        // (/invoices, /cost-leakage, …) gate on the coarse legacy "finance:view". Grant both so
        // the demo admin can actually reach the AR/finance pages in the browser, not just the API.
        // Full-operations permission set so a pilot can exercise EVERY module end-to-end
        // (create/dispatch/status/POD/maintenance/safety writes + finance), matching the
        // "Fleet Manager" role's intent. Missing dispatch:update/cancel/override and job:*
        // previously 403'd the dispatch status/POD and job-lifecycle flows in the browser.
        const string internalPerms = "[\"dashboard:view\",\"vehicles:view\",\"vehicles:create\",\"vehicles:update\",\"vehicles:assign\",\"fleet:manage\",\"fleet.manage\",\"fleet.read\",\"drivers:view\",\"drivers:create\",\"drivers:update\",\"drivers:assign\",\"shipments:view\",\"shipments:create\",\"shipments:update\",\"job:create\",\"job:update\",\"job:status\",\"job:assign\",\"jobs:view\",\"dispatch:view\",\"dispatch:create\",\"dispatch:assign\",\"dispatch:update\",\"dispatch:cancel\",\"dispatch:override\",\"customers:view\",\"customers:create\",\"customers:update\",\"maintenance:view\",\"maintenance:create\",\"maintenance:update\",\"maintenance:close\",\"maintenance:manage\",\"compliance:view\",\"compliance:update\",\"alerts:view\",\"alerts:acknowledge\",\"alerts:close\",\"reports:view\",\"reports:export\",\"safety:view\",\"safety:create\",\"safety:update\",\"safety:review\",\"safety:manage\",\"operations.proof.read\",\"operations.proof.validate\",\"customer_portal:view\",\"customer_portal:manage\",\"finance:view\",\"finance.invoice.read\",\"finance.ar.summary.read\",\"finance.revenue.summary.read\",\"finance.job.ready_to_bill\",\"finance.invoice_draft.read\",\"finance.invoice_draft.create\",\"finance.invoice.issue\",\"finance.invoice.payment.record\",\"rate_card.read\",\"rate_card.manage\",\"contract.read\",\"contract.manage\",\"notifications:view\",\"notifications:manage\",\"messages:send\",\"settings:view\",\"users:view\",\"users:create\",\"users:update\",\"roles:view\",\"roles:create\",\"roles:update\"]";
        // The canonical demo tenant keeps the well-known logins; a non-default (e.g. test)
        // tenant gets a code-suffixed variant so pilot identities stay unmistakable.
        var isCanonical = string.Equals(companyCode, DemoCompanyCode, StringComparison.Ordinal);
        var suffix = isCanonical ? "" : "+" + companyCode.ToLowerInvariant();
        var adminEmail = $"admin{suffix}@meridian.demo";
        var portalEmail = $"portal{suffix}@acme.demo";
        await SeedUserAsync(companyId, adminEmail, "Meridian Ops Admin", "Fleet Manager", null, internalPerms, ct);
        await SeedUserAsync(companyId, portalEmail, "Acme Portal User", "Customer Portal User", customers[0], "[\"customer_portal:view\",\"shipments:view\"]", ct);

        await ReconcileSafetyPilotFixtureAsync(companyId, companyCode, ct);

        return new DemoSeedResult(false, companyId, companyName,
            vehicles.Count, drivers.Count, customers.Count, jobs.Count, trips, dispatch, proofs,
            invoiceIds.Count, payments, feedback, 2, 1, 1,
            "Demo tenant seeded via real service layer (finance chain + feedback) and base-entity creation. Credentials are configured through DemoSeed:Password.");
    }, ct);

    // Versioned, transactionally-applied reconciliation for the Safety pilot. Unlike the
    // original create-only fixture, this runs for an existing Meridian tenant and repairs
    // missing pilot personas/ownership/data. The outer transaction + advisory lock make a
    // version either fully applied or not applied; the version row is written last.
    private async Task ReconcileSafetyPilotFixtureAsync(long companyId, string companyCode, CancellationToken ct)
    {
        await db.ExecuteAsync(
            @"CREATE TABLE IF NOT EXISTS demo_fixture_versions (
                company_id BIGINT NOT NULL,
                fixture_key VARCHAR(80) NOT NULL,
                fixture_version INT NOT NULL,
                applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                PRIMARY KEY (company_id, fixture_key))", ct: ct);

        var currentVersion = await db.ScalarLongAsync(
            "SELECT COALESCE((SELECT fixture_version FROM demo_fixture_versions WHERE company_id=@companyId AND fixture_key=@key),0)",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@key", SafetyPilotFixtureKey); }, ct);
        if (currentVersion >= SafetyPilotFixtureVersion) return;

        // The client-demo tenant is a reviewed commercial fixture, not an unreviewed
        // migrated customer. Reset/reconciliation must therefore fail closed for every
        // governed module that is not explicitly present. Keep all nine catalog entries
        // explicit so Platform Admin can disable/re-enable one during the control-plane
        // rehearsal without the remaining golden-path modules changing implicitly.
        await db.ExecuteAsync(
            @"UPDATE companies SET entitlement_policy_mode='package_allowlist' WHERE id=@companyId;
              INSERT INTO tenant_entitlements(company_id,module_key,enabled,tier,source,updated_by,updated_at)
              SELECT @companyId,module_key,true,'pilot','fixture','canonical-safety-pilot-v7',NOW()
              FROM unnest(ARRAY['safety','maintenance','dispatch','telematics','crm','customer_portal','reports','compliance','integrations']) module_key
              ON CONFLICT(company_id,module_key) DO UPDATE
                SET enabled=true,tier='pilot',source='fixture',updated_by='canonical-safety-pilot-v7',updated_at=NOW();",
            c => c.Parameters.AddWithValue("@companyId", companyId), ct);

        // Keep this bootstrap here as well as in the production migration: a legacy demo
        // database can be reconciled without requiring destructive reseeding.
        await db.ExecuteAsync(
            @"CREATE TABLE IF NOT EXISTS branches (
                id BIGINT GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                company_id BIGINT NOT NULL, branch_code VARCHAR(64) NOT NULL,
                name VARCHAR(200) NOT NULL, branch_type VARCHAR(32) NOT NULL DEFAULT 'branch',
                region VARCHAR(120) NULL, address VARCHAR(300) NULL, city VARCHAR(120) NULL,
                state VARCHAR(120) NULL, country_code VARCHAR(8) NULL, timezone VARCHAR(64) NULL,
                manager_user_id BIGINT NULL, status VARCHAR(32) NOT NULL DEFAULT 'Active',
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(), updated_at TIMESTAMPTZ NULL,
                deleted_at TIMESTAMPTZ NULL, UNIQUE(company_id, branch_code));
              ALTER TABLE users ADD COLUMN IF NOT EXISTS branch_id BIGINT NULL;
              ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS branch_id BIGINT NULL;
              ALTER TABLE drivers ADD COLUMN IF NOT EXISTS branch_id BIGINT NULL;", ct: ct);

        var northBranchId = await UpsertBranchAsync(companyId, "MER-NORTH", "Northern Virginia Operations", "Manassas", ct);
        var southBranchId = await UpsertBranchAsync(companyId, "MER-SOUTH", "Richmond Operations", "Richmond", ct);

        var suffix = string.Equals(companyCode, DemoCompanyCode, StringComparison.Ordinal)
            ? ""
            : "+" + companyCode.ToLowerInvariant();
        var eldSerial = string.Equals(companyCode, DemoCompanyCode, StringComparison.Ordinal)
            ? "MER-ELD-1"
            : $"MER-ELD-{companyCode}";
        var safetyManagerId = await UpsertPilotUserAsync(companyId, northBranchId, $"safety{suffix}@meridian.demo",
            "Meridian Safety Manager", "Safety Manager",
            "[\"dashboard:view\",\"drivers:view\",\"vehicles:view\",\"safety:view\",\"safety:create\",\"safety:update\",\"safety:review\",\"safety:manage\",\"compliance:view\",\"compliance:update\",\"reports:view\",\"reports:export\",\"maintenance:view\",\"alerts:view\",\"alerts:acknowledge\",\"notifications:view\"]", ct);
        var driverUserId = await UpsertPilotUserAsync(companyId, northBranchId, $"driver{suffix}@meridian.demo",
            "Meridian Driver One", "Driver",
            // Keep this exactly aligned with RolePermissionDefaults["Driver"]. A user-level
            // safety:update or back-office read bypasses the authoritative Driver role
            // reconciler because user permissions are unioned after role permissions.
            "[\"driver:self\",\"notifications:view\",\"messages:send\",\"maintenance:create\"]", ct);
        await UpsertPilotUserAsync(companyId, southBranchId, $"dispatch{suffix}@meridian.demo",
            "Meridian Dispatcher", "Dispatcher",
            "[\"dashboard:view\",\"drivers:view\",\"vehicles:view\",\"shipments:view\",\"dispatch:view\",\"dispatch:create\",\"dispatch:assign\",\"dispatch:update\",\"alerts:view\",\"safety:view\"]", ct);
        await UpsertPilotUserAsync(companyId, northBranchId, $"maintenance{suffix}@meridian.demo",
            "Meridian Maintenance Lead", "Maintenance Manager",
            "[\"dashboard:view\",\"vehicles:view\",\"maintenance:view\",\"maintenance:create\",\"maintenance:update\",\"maintenance:close\",\"compliance:view\",\"compliance:update\",\"safety:view\"]", ct);
        await UpsertPilotUserAsync(companyId, null, $"auditor{suffix}@meridian.demo",
            "Meridian Safety Auditor", "Safety Auditor",
            "[\"dashboard:view\",\"drivers:view\",\"vehicles:view\",\"safety:view\",\"compliance:view\",\"reports:view\",\"reports:export\"]", ct);

        // Deterministic branch ownership gives the pilot both scoped and tenant-wide stories.
        await db.ExecuteAsync(
            @"UPDATE drivers SET branch_id=CASE WHEN driver_code IN ('MER-DRV-1','MER-DRV-2','MER-DRV-3') THEN @north ELSE @south END
                WHERE company_id=@companyId AND driver_code LIKE 'MER-DRV-%';
              UPDATE vehicles SET branch_id=CASE WHEN vehicle_code IN ('MER-TRK-1','MER-VAN-1','MER-REEF-1') THEN @north ELSE @south END
                WHERE company_id=@companyId AND vehicle_code LIKE 'MER-%';
              UPDATE drivers SET user_id=@driverUserId WHERE company_id=@companyId AND driver_code='MER-DRV-1';",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@north", northBranchId); c.Parameters.AddWithValue("@south", southBranchId); c.Parameters.AddWithValue("@driverUserId", driverUserId); }, ct);

        var driver1 = await RequiredEntityIdAsync("drivers", "driver_code", "MER-DRV-1", companyId, ct);
        var driver2 = await RequiredEntityIdAsync("drivers", "driver_code", "MER-DRV-2", companyId, ct);
        var vehicle1 = await RequiredEntityIdAsync("vehicles", "vehicle_code", "MER-TRK-1", companyId, ct);
        var vehicle2 = await RequiredEntityIdAsync("vehicles", "vehicle_code", "MER-VAN-1", companyId, ct);

        // Normalize the original thin event and add a second connected event. Stable business
        // keys make the result deterministic while preserving any operator-created records.
        await db.ExecuteAsync(
            @"UPDATE safety_events SET event_number='MER-SAFE-1', branch_id=@branch, event_type='Speeding', severity='High',
                    status='In Review', occurred_at=NOW()-INTERVAL '2 day', event_time=NOW()-INTERVAL '2 day',
                    score_impact=12, risk_score=72, coaching_status='Created',
                    ai_summary='Repeated speeding pattern detected on the Northern Virginia corridor.',
                    recommended_action='Complete speed-management coaching and review HOS context.'
                WHERE id=(SELECT id FROM safety_events WHERE company_id=@companyId ORDER BY id LIMIT 1);
              INSERT INTO safety_events (company_id,branch_id,event_number,driver_id,vehicle_id,event_type,severity,status,event_time,occurred_at,
                    score_impact,risk_score,coaching_status,incident_status,location_description,ai_summary,recommended_action)
                SELECT @companyId,@branch,'MER-SAFE-2',@driver2,@vehicle2,'Harsh Braking','Critical','Escalated',NOW()-INTERVAL '1 day',NOW()-INTERVAL '1 day',
                    18,88,'Not Created','Open','I-95 Southbound near Richmond',
                    'Hard-braking event correlated with an hours-of-service warning.','Open an incident and preserve evidence.'
                WHERE NOT EXISTS (SELECT 1 FROM safety_events WHERE company_id=@companyId AND event_number='MER-SAFE-2');",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@branch", northBranchId); c.Parameters.AddWithValue("@driver2", driver2); c.Parameters.AddWithValue("@vehicle2", vehicle2); }, ct);
        var event1 = await RequiredEntityIdAsync("safety_events", "event_number", "MER-SAFE-1", companyId, ct);
        var event2 = await RequiredEntityIdAsync("safety_events", "event_number", "MER-SAFE-2", companyId, ct);

        await db.ExecuteAsync(
            @"UPDATE coaching_tasks SET branch_id=@branch,safety_event_id=@event1,assigned_to_user_id=@manager,status='Assigned',before_safety_score=88
                WHERE company_id=@companyId AND task_number='MER-COACH-1';
              INSERT INTO coaching_tasks (company_id,branch_id,task_number,driver_id,safety_event_id,assigned_to_user_id,coaching_type,title,status,priority,
                    driver_acknowledged,acknowledged_at,completed_at,before_safety_score,after_safety_score,effectiveness_score,due_at)
                VALUES (@companyId,@branch,'MER-COACH-2',@driver2,@event2,@manager,'Defensive Driving','Hard-braking follow-up','Completed','High',
                    true,NOW()-INTERVAL '6 hour',NOW()-INTERVAL '5 hour',74,84,82,NOW()-INTERVAL '5 hour')
                ON CONFLICT (company_id,task_number) DO NOTHING;",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@branch", northBranchId); c.Parameters.AddWithValue("@event1", event1); c.Parameters.AddWithValue("@event2", event2); c.Parameters.AddWithValue("@manager", safetyManagerId); c.Parameters.AddWithValue("@driver2", driver2); }, ct);

        await db.ExecuteAsync(
            @"INSERT INTO incidents (company_id,branch_id,incident_number,safety_event_id,driver_id,vehicle_id,incident_type,severity,status,
                    location_description,occurred_at,driver_statement,ai_summary,recommended_action,insurance_report_status,idempotency_key)
                VALUES (@companyId,@branch,'MER-INC-1',@event2,@driver2,@vehicle2,'Preventable Safety Event','Critical','Under Review',
                    'I-95 Southbound near Richmond',NOW()-INTERVAL '1 day','Traffic compressed suddenly; vehicle stopped without contact.',
                    'Event requires supervisor review; no collision or injury recorded.','Review telemetry and close with documented determination.','Not Required','demo:meridian:incident:1')
                ON CONFLICT (company_id,incident_number) WHERE deleted_at IS NULL DO UPDATE
                    SET status='Under Review', updated_at=NOW();",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@branch", northBranchId); c.Parameters.AddWithValue("@event2", event2); c.Parameters.AddWithValue("@driver2", driver2); c.Parameters.AddWithValue("@vehicle2", vehicle2); }, ct);
        var incidentId = await RequiredEntityIdAsync("incidents", "incident_number", "MER-INC-1", companyId, ct);
        await db.ExecuteAsync(
            @"UPDATE incident_evidence
                SET evidence_title='Synthetic harsh-braking telemetry metadata',evidence_url=NULL,
                    content_hash=encode(sha256(convert_to('MER-INC-1:telemetry:v1','UTF8')),'hex'),
                    evidence_json='{""fixture"":""safety-pilot-v7"",""synthetic"":true,""verificationStatus"":""not_verified"",""custodyStatus"":""not_managed"",""retrievalStatus"":""not_available""}'::jsonb
                WHERE company_id=@companyId AND incident_id=@incident AND source_entity_type='safety_event' AND source_entity_id=@event2
                  AND evidence_title IN ('Verified harsh-braking telemetry','Synthetic harsh-braking telemetry metadata');
              INSERT INTO incident_evidence (company_id,branch_id,incident_id,evidence_type,evidence_title,content_hash,evidence_json,source_entity_type,source_entity_id)
                SELECT @companyId,@branch,@incident,'Telemetry Snapshot','Synthetic harsh-braking telemetry metadata',encode(sha256(convert_to('MER-INC-1:telemetry:v1','UTF8')),'hex'),
                    '{""fixture"":""safety-pilot-v7"",""synthetic"":true,""verificationStatus"":""not_verified"",""custodyStatus"":""not_managed"",""retrievalStatus"":""not_available""}'::jsonb,'safety_event',@event2
                WHERE NOT EXISTS (SELECT 1 FROM incident_evidence WHERE company_id=@companyId AND incident_id=@incident
                    AND source_entity_type='safety_event' AND source_entity_id=@event2);",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@branch", northBranchId); c.Parameters.AddWithValue("@incident", incidentId); c.Parameters.AddWithValue("@event2", event2); }, ct);

        await db.ExecuteAsync(
            @"UPDATE dvir_reports SET branch_id=@branch,defects_found=1,driver_signature_status='Signed',notes='Major brake defect routed to maintenance.'
                WHERE company_id=@companyId AND report_number='MER-DVIR-1';
              UPDATE dvir_defects SET branch_id=@branch,out_of_service=true
                WHERE company_id=@companyId AND dvir_report_id=(SELECT id FROM dvir_reports WHERE company_id=@companyId AND report_number='MER-DVIR-1' LIMIT 1);
              UPDATE vehicles SET out_of_service=true,availability_status='out_of_service'
                WHERE company_id=@companyId AND id=(SELECT vehicle_id FROM dvir_reports WHERE company_id=@companyId AND report_number='MER-DVIR-1' LIMIT 1);
              INSERT INTO work_orders (company_id,vehicle_id,dvir_report_id,work_order_code,work_order_number,title,issue_type,status,priority,due_date,estimated_cost,notes)
                SELECT @companyId,r.vehicle_id,r.id,'MER-WO-DVIR-1','MER-WO-DVIR-1','Brake defect correction','DVIR Defect','Open','Critical',CURRENT_DATE+1,1250,'Created from the pilot DVIR defect workflow.'
                FROM dvir_reports r WHERE r.company_id=@companyId AND r.report_number='MER-DVIR-1'
                  AND NOT EXISTS (SELECT 1 FROM work_orders WHERE company_id=@companyId AND work_order_code='MER-WO-DVIR-1');
              UPDATE dvir_defects SET linked_work_order_id=(SELECT id FROM work_orders WHERE company_id=@companyId AND work_order_code='MER-WO-DVIR-1' LIMIT 1)
                WHERE company_id=@companyId AND dvir_report_id=(SELECT id FROM dvir_reports WHERE company_id=@companyId AND report_number='MER-DVIR-1' LIMIT 1);
              INSERT INTO dvir_reports (company_id,branch_id,report_number,driver_id,vehicle_id,country_code,inspection_type,inspection_status,defects_found,
                    safe_to_operate,driver_signature_status,mechanic_review_status,repair_certification_status,submitted_at,risk_score,notes)
                SELECT @companyId,@branch,'MER-DVIR-2',@driver1,@vehicle1,'US','Post-Trip','Submitted',0,true,'Signed','Not Required','Not Required',NOW()-INTERVAL '4 hour',8,'No defects found.'
                WHERE NOT EXISTS (SELECT 1 FROM dvir_reports WHERE company_id=@companyId AND report_number='MER-DVIR-2');
              INSERT INTO hos_records (company_id,branch_id,driver_id,shift_date,remaining_drive_hours,remaining_shift_hours,remaining_cycle_hours,hos_status,created_at)
                SELECT @companyId,@branch,@driver1,CURRENT_DATE,2.75,4.25,18.50,'Warning',NOW()-INTERVAL '15 minute'
                WHERE NOT EXISTS (SELECT 1 FROM hos_records WHERE company_id=@companyId AND driver_id=@driver1 AND shift_date=CURRENT_DATE);
              UPDATE hos_clocks SET branch_id=@branch,country_code='US',cycle_type='70hr/8day',drive_time_remaining_minutes=165,
                    shift_time_remaining_minutes=255,cycle_time_remaining_minutes=1110,
                    status='Warning',hos_warning='2.8h drive time remaining. Plan for a rest stop.'
                WHERE company_id=@companyId AND driver_id=@driver1;
              INSERT INTO hos_clocks (company_id,branch_id,driver_id,country_code,cycle_type,drive_time_remaining_minutes,
                    shift_time_remaining_minutes,cycle_time_remaining_minutes,status,hos_warning)
                SELECT @companyId,@branch,@driver1,'US','70hr/8day',165,255,1110,'Warning',
                    '2.8h drive time remaining. Plan for a rest stop.'
                WHERE NOT EXISTS (SELECT 1 FROM hos_clocks WHERE company_id=@companyId AND driver_id=@driver1);
              INSERT INTO hos_logs (company_id,branch_id,driver_id,vehicle_id,log_date,driving_hours,on_duty_hours,cycle_hours_left,
                    country_code,status,start_time,end_time,duration_minutes,location,is_certified,source,source_event_id)
                SELECT @companyId,@branch,@driver1,@vehicle1,CURRENT_DATE,2.50,2.50,18.50,'US','Driving',NOW()-INTERVAL '3 hour',
                    NOW()-INTERVAL '30 minute',150,'I-95 corridor',false,'demo','safety-pilot-hos-1'
                WHERE NOT EXISTS (SELECT 1 FROM hos_logs WHERE company_id=@companyId AND source='demo' AND source_event_id='safety-pilot-hos-1');
              INSERT INTO eld_devices (company_id,branch_id,device_serial,device_model,provider,vehicle_id,driver_id,status,
                    last_sync_at,firmware_version,provider_sync_status,row_version)
                SELECT @companyId,@branch,@eldSerial,'Pilot ELD','Synthetic Provider',@vehicle1,@driver1,'Diagnostic',
                    NOW()-INTERVAL '5 minute','pilot-1.0','Healthy',1
                WHERE NOT EXISTS (SELECT 1 FROM eld_devices WHERE company_id=@companyId AND device_serial=@eldSerial);",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@branch", northBranchId); c.Parameters.AddWithValue("@driver1", driver1); c.Parameters.AddWithValue("@vehicle1", vehicle1); c.Parameters.AddWithValue("@eldSerial", eldSerial); }, ct);

        await db.ExecuteAsync(
            @"INSERT INTO driver_safety_scorecards (company_id,branch_id,driver_id,period_start,period_end,safety_score,speeding_count,coaching_open_count,coaching_completed_count,incident_count,risk_score)
                VALUES (@companyId,@branch,@driver1,CURRENT_DATE-29,CURRENT_DATE,76,3,1,0,0,68),(@companyId,@branch,@driver2,CURRENT_DATE-29,CURRENT_DATE,84,0,0,1,1,46)
                ON CONFLICT (company_id,driver_id) DO UPDATE SET branch_id=EXCLUDED.branch_id,safety_score=EXCLUDED.safety_score,
                    speeding_count=EXCLUDED.speeding_count,coaching_open_count=EXCLUDED.coaching_open_count,
                    coaching_completed_count=EXCLUDED.coaching_completed_count,incident_count=EXCLUDED.incident_count,
                    risk_score=EXCLUDED.risk_score,period_start=EXCLUDED.period_start,period_end=EXCLUDED.period_end;
              INSERT INTO driver_safety_scores (company_id,driver_id,score_7d,score_30d,score_90d,events_7d,events_30d,events_90d,breakdown_json,computed_at)
                VALUES (@companyId,@driver1,76,80,86,3,5,8,'{""formulaVersion"":""safety-pilot-v2"",""speeding"":3}'::jsonb,NOW()),
                       (@companyId,@driver2,84,87,91,1,2,3,'{""formulaVersion"":""safety-pilot-v2"",""harshBraking"":1}'::jsonb,NOW())
                ON CONFLICT (company_id,driver_id) DO UPDATE SET score_7d=EXCLUDED.score_7d,score_30d=EXCLUDED.score_30d,
                    score_90d=EXCLUDED.score_90d,events_7d=EXCLUDED.events_7d,events_30d=EXCLUDED.events_30d,
                    events_90d=EXCLUDED.events_90d,breakdown_json=EXCLUDED.breakdown_json,computed_at=EXCLUDED.computed_at;",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@branch", northBranchId); c.Parameters.AddWithValue("@driver1", driver1); c.Parameters.AddWithValue("@driver2", driver2); }, ct);

        await db.ExecuteAsync(
            @"INSERT INTO demo_fixture_versions(company_id,fixture_key,fixture_version,applied_at)
                VALUES (@companyId,@key,@version,NOW())
                ON CONFLICT(company_id,fixture_key) DO UPDATE SET fixture_version=EXCLUDED.fixture_version,applied_at=EXCLUDED.applied_at",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@key", SafetyPilotFixtureKey); c.Parameters.AddWithValue("@version", SafetyPilotFixtureVersion); }, ct);
    }

    private async Task<long> UpsertBranchAsync(long companyId, string code, string name, string city, CancellationToken ct)
        => await db.ScalarLongAsync(
            @"INSERT INTO branches(company_id,branch_code,name,branch_type,region,city,state,country_code,timezone,status)
                VALUES (@companyId,@code,@name,'branch','Mid-Atlantic',@city,'VA','US','America/New_York','Active')
                ON CONFLICT(company_id,branch_code) DO UPDATE SET name=EXCLUDED.name,city=EXCLUDED.city,status='Active',deleted_at=NULL
                RETURNING id",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@code", code); c.Parameters.AddWithValue("@name", name); c.Parameters.AddWithValue("@city", city); }, ct);

    private async Task<long> UpsertPilotUserAsync(long companyId, long? branchId, string email, string fullName, string roleName, string permissionsJson, CancellationToken ct)
        => await db.ScalarLongAsync(
            @"INSERT INTO users(company_id,branch_id,full_name,email,role_name,status,password_hash,permissions_json)
                VALUES (@companyId,@branchId,@fullName,@email,@roleName,'Active',@passwordHash,@perms::jsonb)
                ON CONFLICT(company_id,email) DO UPDATE SET branch_id=EXCLUDED.branch_id,full_name=EXCLUDED.full_name,role_name=EXCLUDED.role_name,
                    status='Active',password_hash=EXCLUDED.password_hash,permissions_json=EXCLUDED.permissions_json
                RETURNING id",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@branchId", (object?)branchId ?? DBNull.Value);
                c.Parameters.AddWithValue("@fullName", fullName);
                c.Parameters.AddWithValue("@email", email);
                c.Parameters.AddWithValue("@roleName", roleName);
                c.Parameters.AddWithValue("@passwordHash", PlatformSchemaService.HashPassword(demoPassword));
                c.Parameters.AddWithValue("@perms", permissionsJson);
            }, ct);

    private async Task<long> RequiredEntityIdAsync(string table, string keyColumn, string key, long companyId, CancellationToken ct)
    {
        // table/column values are private compile-time call-site constants, never request data.
        var id = await db.ScalarLongAsync($"SELECT COALESCE((SELECT id FROM {table} WHERE company_id=@companyId AND {keyColumn}=@key LIMIT 1),0)",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@key", key); }, ct);
        return id > 0 ? id : throw new InvalidOperationException($"Safety pilot fixture requires {table}.{keyColumn}={key}.");
    }

    // ── Backdated time-series enrichment (idempotent) ──────────────────────────────
    // Generates realistic ~90-day history for the tables that drive trend charts but
    // that the create-path seeder leaves empty/thin: fuel_transactions (Carbon,
    // fuel analytics) and location_events (live map breadcrumbs, trip compliance).
    // Safe to run repeatedly on an EXISTING tenant — it no-ops once history exists,
    // so it can enrich the already-created MERIDIAN-DEMO without a re-seed.
    public async Task<int> EnrichTimeSeriesAsync(long companyId, CancellationToken ct = default)
    {
        var already = await db.ScalarLongAsync(
            "SELECT COUNT(*) FROM fuel_transactions WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", companyId), ct);
        if (already > 0) return 0; // history already present — idempotent no-op.

        // Real vehicles for this tenant, with a per-type fuel profile.
        var vehicleRows = await db.QueryAsync(
            "SELECT id, type FROM vehicles WHERE company_id=@cid AND deleted_at IS NULL ORDER BY id",
            c => c.Parameters.AddWithValue("@cid", companyId), ct);
        if (vehicleRows.Count == 0) return 0;

        var rng = new Random(unchecked((int)(companyId * 2654435761)));
        var fuelInserts = 0;

        // Diesel ~ $4.10/gal; each vehicle refuels roughly every 4 days over 90 days.
        foreach (var v in vehicleRows)
        {
            var vehicleId = Convert.ToInt64(v["id"]);
            var vtype = v.GetValueOrDefault("type")?.ToString() ?? "Truck";
            var baseGallons = vtype.Contains("Van", StringComparison.OrdinalIgnoreCase) ? 22.0
                            : vtype.Contains("Reefer", StringComparison.OrdinalIgnoreCase) ? 68.0
                            : vtype.Contains("Box", StringComparison.OrdinalIgnoreCase) ? 40.0
                            : 55.0;
            for (var daysAgo = 88; daysAgo >= 1; daysAgo -= 4)
            {
                var gallons = Math.Round(baseGallons * (0.85 + rng.NextDouble() * 0.3), 1);
                var unitPrice = Math.Round(3.95 + rng.NextDouble() * 0.35, 3);
                var idleMinutes = rng.Next(20, 140);
                await db.ExecuteAsync(
                    @"INSERT INTO fuel_transactions
                        (company_id, vehicle_id, transaction_time, gallons, quantity, unit, unit_price,
                         total_cost, currency, fuel_type, idle_minutes, payment_method, anomaly_status, fuel_station)
                      VALUES
                        (@cid, @vid, NOW() - make_interval(days => @d, hours => @h), @g, @g, 'gallon', @up,
                         @tc, 'USD', 'Diesel', @idle, 'Fuel Card', 'normal', @station)",
                    c =>
                    {
                        c.Parameters.AddWithValue("@cid", companyId);
                        c.Parameters.AddWithValue("@vid", vehicleId);
                        c.Parameters.AddWithValue("@d", daysAgo);
                        c.Parameters.AddWithValue("@h", rng.Next(6, 20));
                        c.Parameters.AddWithValue("@g", gallons);
                        c.Parameters.AddWithValue("@up", unitPrice);
                        c.Parameters.AddWithValue("@tc", Math.Round(gallons * unitPrice, 2));
                        c.Parameters.AddWithValue("@idle", idleMinutes);
                        c.Parameters.AddWithValue("@station", $"Depot Fuel {rng.Next(1, 5)}");
                    }, ct);
                fuelInserts++;
            }
        }

        // Denser location breadcrumbs for the last 24h across active vehicles so the
        // live map / trip breadcrumbs have a real trail (not a single stale point).
        foreach (var v in vehicleRows.Take(3))
        {
            var vehicleId = Convert.ToInt64(v["id"]);
            var lat = 24.7 + rng.NextDouble() * 0.4;  // Riyadh-ish region for the KSA pilot feel
            var lng = 46.6 + rng.NextDouble() * 0.4;
            for (var minsAgo = 720; minsAgo >= 15; minsAgo -= 15)
            {
                lat += (rng.NextDouble() - 0.5) * 0.01;
                lng += (rng.NextDouble() - 0.5) * 0.01;
                await db.ExecuteAsync(
                    @"INSERT INTO location_events (company_id, vehicle_id, event_time, received_at, event_type, lat, lng, speed_mph, source)
                      VALUES (@cid, @vid, NOW() - make_interval(mins => @m), NOW() - make_interval(mins => @m), 'gps_ping', @lat, @lng, @spd, 'demo-history')",
                    c =>
                    {
                        c.Parameters.AddWithValue("@cid", companyId);
                        c.Parameters.AddWithValue("@vid", vehicleId);
                        c.Parameters.AddWithValue("@m", minsAgo);
                        c.Parameters.AddWithValue("@lat", Math.Round(lat, 6));
                        c.Parameters.AddWithValue("@lng", Math.Round(lng, 6));
                        c.Parameters.AddWithValue("@spd", rng.Next(0, 70));
                    }, ct);
            }
        }

        // Real ELD/telematics devices — one per vehicle — so the IoT / Telematics
        // pages render genuine device records instead of the frontend seed overlay.
        if (await db.ScalarLongAsync("SELECT COUNT(*) FROM eld_devices WHERE company_id=@cid",
                c => c.Parameters.AddWithValue("@cid", companyId), ct) == 0)
        {
            var providers = new[] { "Geotab", "Samsara", "Motive" };
            var models = new[] { "GO9", "VG34", "LBB-3" };
            var deviceStatuses = new[] { "Active", "Active", "Active", "Diagnostic", "Active" };
            var di = 0;
            foreach (var v in vehicleRows)
            {
                var vehicleId = Convert.ToInt64(v["id"]);
                var p = di % providers.Length;
                // Active devices must carry real credentials (ck_eld_devices_active_credentials):
                // generate per-device random creds exactly like the provisioning endpoint.
                var rawApiKey  = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
                var hmacSecret = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
                await db.ExecuteAsync(
                    @"INSERT INTO eld_devices
                        (company_id, device_serial, device_model, provider, vehicle_id, firmware_version,
                         api_key_hash, hmac_secret,
                         status, last_heartbeat_at, last_sync_at, created_at)
                      VALUES
                        (@cid, @serial, @model, @provider, @vid, @fw,
                         encode(sha256(@rawKey::bytea), 'hex'), @hmac,
                         @status, NOW() - make_interval(mins => @hb), NOW() - make_interval(mins => @hb), NOW() - INTERVAL '90 days')",
                    c =>
                    {
                        c.Parameters.AddWithValue("@cid", companyId);
                        c.Parameters.AddWithValue("@serial", $"ELD-{companyId}-{di + 1:D3}");
                        c.Parameters.AddWithValue("@model", models[p]);
                        c.Parameters.AddWithValue("@provider", providers[p]);
                        c.Parameters.AddWithValue("@vid", vehicleId);
                        c.Parameters.AddWithValue("@fw", $"v{4 + p}.{rng.Next(0, 9)}.{rng.Next(0, 9)}");
                        c.Parameters.AddWithValue("@rawKey", rawApiKey);
                        c.Parameters.AddWithValue("@hmac", hmacSecret);
                        c.Parameters.AddWithValue("@status", deviceStatuses[di % deviceStatuses.Length]);
                        c.Parameters.AddWithValue("@hb", rng.Next(1, 45));
                    }, ct);
                di++;
            }
        }

        return fuelInserts;
    }

    private async Task AgeInvoiceAsync(Guid invoiceId, int issuedDaysAgo, int dueInDays, CancellationToken ct)
        => await db.ExecuteAsync(
            "UPDATE issued_invoices SET issued_at = NOW() - make_interval(days => @issued), due_at = NOW() + make_interval(days => @due) WHERE id=@id",
            c => { c.Parameters.AddWithValue("@id", invoiceId); c.Parameters.AddWithValue("@issued", issuedDaysAgo); c.Parameters.AddWithValue("@due", dueInDays); }, ct);

    private async Task SeedUserAsync(long companyId, string email, string fullName, string roleName, long? customerId, string permissionsJson, CancellationToken ct)
        => await db.ExecuteAsync(
            @"INSERT INTO users (company_id, customer_id, full_name, email, role_name, status, password_hash, permissions_json)
              VALUES (@companyId, @customerId, @fullName, @email, @roleName, 'Active', @passwordHash, @perms::jsonb)",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@customerId", (object?)customerId ?? DBNull.Value);
                c.Parameters.AddWithValue("@fullName", fullName);
                c.Parameters.AddWithValue("@email", email);
                c.Parameters.AddWithValue("@roleName", roleName);
                c.Parameters.AddWithValue("@passwordHash", PlatformSchemaService.HashPassword(demoPassword));
                c.Parameters.AddWithValue("@perms", permissionsJson);
            }, ct);

    private static string ResolveDemoPassword(IConfiguration? config)
    {
        var configured = config?["DemoSeed:Password"];
        return !string.IsNullOrWhiteSpace(configured) && configured.Length >= 12
            ? configured
            : Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
    }

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
