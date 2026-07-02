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

    // companyCode/companyName are overridable so integration TESTS can seed an ISOLATED
    // throwaway tenant (e.g. "MERIDIAN-DEMO-TEST") without touching the real runtime demo
    // tenant a pilot is using. Runtime callers use the defaults.
    public async Task<DemoSeedResult> SeedAsync(CancellationToken ct = default)
        => await SeedAsync(DemoCompanyCode, DemoCompanyName, ct);

    public async Task<DemoSeedResult> SeedAsync(string companyCode, string companyName, CancellationToken ct = default)
    {
        var existing = await db.ScalarLongAsync(
            "SELECT COALESCE((SELECT id FROM companies WHERE company_code=@code LIMIT 1), 0)",
            c => c.Parameters.AddWithValue("@code", companyCode), ct);
        if (existing > 0)
        {
            return new DemoSeedResult(true, existing, companyName, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                "Demo tenant already exists — skipped (idempotent).");
        }

        var companyId = await db.InsertAsync(
            @"INSERT INTO companies (company_code, name, industry, timezone, status)
              VALUES (@code, @name, 'Transport & Logistics', 'America/New_York', 'Active')",
            c => { c.Parameters.AddWithValue("@code", companyCode); c.Parameters.AddWithValue("@name", companyName); }, ct);

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
            // Set BOTH status (legacy display) and assignment_status (the canonical lowercase
            // P4 token the dispatch state machine reads) — otherwise assignment_status is empty
            // and every status transition is rejected as "from '' to ...".
            await db.ExecuteAsync(
                @"INSERT INTO dispatch_assignments (company_id, job_id, vehicle_id, driver_id, status, assignment_status, assigned_at)
                  VALUES (@companyId, @jobId, @vehicleId, @driverId, @status, @status, NOW() - INTERVAL '4 hours')",
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
        const string internalPerms = "[\"dashboard:view\",\"vehicles:view\",\"vehicles:create\",\"vehicles:update\",\"vehicles:assign\",\"fleet:manage\",\"fleet.manage\",\"fleet.read\",\"drivers:view\",\"drivers:create\",\"drivers:update\",\"drivers:assign\",\"shipments:view\",\"shipments:create\",\"shipments:update\",\"job:create\",\"job:update\",\"job:status\",\"job:assign\",\"jobs:view\",\"dispatch:view\",\"dispatch:create\",\"dispatch:assign\",\"dispatch:update\",\"dispatch:cancel\",\"dispatch:override\",\"customers:view\",\"customers:create\",\"customers:update\",\"maintenance:view\",\"maintenance:create\",\"maintenance:update\",\"maintenance:close\",\"maintenance:manage\",\"compliance:view\",\"compliance:update\",\"alerts:view\",\"alerts:acknowledge\",\"alerts:close\",\"reports:view\",\"reports:export\",\"safety:view\",\"safety:create\",\"safety:update\",\"safety:review\",\"safety:manage\",\"operations.proof.read\",\"operations.proof.validate\",\"customer_portal:view\",\"customer_portal:manage\",\"finance:view\",\"finance.invoice.read\",\"finance.ar.summary.read\",\"finance.revenue.summary.read\",\"finance.job.ready_to_bill\",\"finance.invoice_draft.read\",\"finance.invoice_draft.create\",\"finance.invoice.issue\",\"finance.invoice.payment.record\",\"rate_card.read\",\"rate_card.manage\",\"contract.read\",\"contract.manage\",\"notifications:view\",\"notifications:manage\",\"messages:send\",\"settings:view\"]";
        // users.email is globally UNIQUE. The canonical demo tenant keeps the well-known
        // logins; a non-default (e.g. test) tenant gets a code-suffixed variant so it never
        // collides with the runtime demo tenant's users.
        var isCanonical = string.Equals(companyCode, DemoCompanyCode, StringComparison.Ordinal);
        var suffix = isCanonical ? "" : "+" + companyCode.ToLowerInvariant();
        var adminEmail = $"admin{suffix}@meridian.demo";
        var portalEmail = $"portal{suffix}@acme.demo";
        await SeedUserAsync(companyId, adminEmail, "Meridian Ops Admin", "Fleet Manager", null, internalPerms, ct);
        await SeedUserAsync(companyId, portalEmail, "Acme Portal User", "Customer Portal User", customers[0], "[\"customer_portal:view\",\"shipments:view\"]", ct);

        return new DemoSeedResult(false, companyId, companyName,
            vehicles.Count, drivers.Count, customers.Count, jobs.Count, trips, dispatch, proofs,
            invoiceIds.Count, payments, feedback, 2, 1, 1,
            "Demo tenant seeded via real service layer (finance chain + feedback) and base-entity creation. Logins: admin@meridian.demo / portal@acme.demo (password: MeridianDemo!23).");
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

        return fuelInserts;
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
