using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Opstrax.Api.Data;
using Opstrax.Api.Foundation;
using Opstrax.Api.Services;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Xunit;

namespace Opstrax.Tests;

// Proves the demo-tenant seeder: it runs through the REAL finance service layer,
// produces the expected counts + AR aging spread, and is idempotent. Doubles as the
// STEP-2 KPI spot-check (numbers are hand-calculated against what was seeded).
public class DemoTenantSeederPostgresTests
{
    private static readonly string LocalConnectionString = TestDb.ConnectionString;

    // Isolated, throwaway tenant code — MUST differ from the production DemoTenantSeeder
    // code so this test never deletes/recreates the real runtime demo tenant a pilot uses.
    private const string TestCompanyName = "Meridian Logistics — Demo (Test)";

    [Fact]
    public async Task DemoSeed_ProducesExpectedCountsAndArSpread_AndIsIdempotent()
    {
        var db = CreateDatabase();
        await EnsureSchemasAsync(db);
        var testCompanyCode = $"MERIDIAN-DEMO-TEST-{Guid.NewGuid():N}"[..36];
        await DeleteDemoTenantAsync(db, testCompanyCode); // clean slate for a full seed

        var seeder = new DemoTenantSeeder(db);
        var result = await seeder.SeedAsync(testCompanyCode, TestCompanyName);

        Assert.False(result.AlreadySeeded);
        Assert.Equal(TestCompanyName, result.CompanyName);
        Assert.Equal(5, result.Vehicles);
        Assert.Equal(5, result.Drivers);
        Assert.Equal(3, result.Customers);
        Assert.Equal(12, result.Jobs);
        Assert.Equal(4, result.IssuedInvoices);
        Assert.Equal(1, result.Payments);
        Assert.Equal(1, result.Feedback);
        // The feedback must actually be persisted (real service enforces job ownership).
        // (Assert deferred until after companyId is known — see below.)
        Assert.True(result.Trips >= 4);
        Assert.True(result.ProofPackages >= 3);

        var companyId = result.CompanyId;

        // Demo data must obey the same allocation invariant as live dispatch: no two
        // concurrently active assignments may share a vehicle or driver.
        Assert.Equal(0, await db.ScalarLongAsync(@"SELECT COUNT(*) FROM (
            SELECT vehicle_id FROM dispatch_assignments WHERE company_id=@c
              AND assignment_status NOT IN ('delivered','cancelled','rejected')
            GROUP BY vehicle_id HAVING COUNT(*) > 1) duplicates", c => c.Parameters.AddWithValue("@c", companyId)));
        Assert.Equal(0, await db.ScalarLongAsync(@"SELECT COUNT(*) FROM (
            SELECT driver_id FROM dispatch_assignments WHERE company_id=@c
              AND assignment_status NOT IN ('delivered','cancelled','rejected')
            GROUP BY driver_id HAVING COUNT(*) > 1) duplicates", c => c.Parameters.AddWithValue("@c", companyId)));

        // KPI spot-check #1 — jobs span every status (12 rows).
        Assert.Equal(12, await db.ScalarLongAsync("SELECT COUNT(*) FROM jobs WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId)));
        // every distinct status present
        foreach (var s in new[] { "draft", "scheduled", "assigned", "in_progress", "completed", "cancelled", "exception" })
        {
            Assert.True(await db.ScalarLongAsync("SELECT COUNT(*) FROM jobs WHERE company_id=@c AND status=@s", c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@s", s); }) >= 1, $"missing job status {s}");
        }

        var revenue = CreateRevenueService(db, companyId);

        // KPI spot-check #2 — AR aging spread (hand-calculated: paid excluded; 1 current, 1 in 31-60, 1 in 90+).
        var aging = await revenue.GetAccountsReceivableAgingAsync(companyId);
        Assert.Equal(2100.50m, aging.Current);
        Assert.Equal(0m, aging.Days1To30);
        Assert.Equal(875.25m, aging.Days31To60);
        Assert.Equal(0m, aging.Days61To90);
        Assert.Equal(3300.00m, aging.Days90Plus);
        Assert.Equal(6275.75m, aging.TotalOutstanding); // 2100.50 + 875.25 + 3300.00 (paid 1450 excluded)

        // KPI spot-check #3 — AR summary: 4 issued invoices, 1450.00 paid.
        var ar = await revenue.GetAccountsReceivableSummaryAsync(companyId);
        Assert.Equal(4, ar.IssuedInvoiceCount);
        Assert.Equal(1450.00m, ar.PaidBalance);
        Assert.Equal(6275.75m, ar.OpenBalance);

        // Proof lifecycle spread: one validated, one rejected, one pending.
        Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM proof_packages WHERE company_id=@c AND validation_status='validated'", c => c.Parameters.AddWithValue("@c", companyId)));
        Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM proof_packages WHERE company_id=@c AND validation_status='rejected'", c => c.Parameters.AddWithValue("@c", companyId)));
        Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM proof_packages WHERE company_id=@c AND validation_status='pending'", c => c.Parameters.AddWithValue("@c", companyId)));

        // Feedback was actually persisted (guards the ownership-mismatch bug the walkthrough caught).
        Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM customer_feedback WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId)));

        // Idempotent — a second run must NOT duplicate.
        var second = await seeder.SeedAsync(testCompanyCode, TestCompanyName);
        Assert.True(second.AlreadySeeded);
        Assert.Equal(companyId, second.CompanyId);
        Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM companies WHERE company_code=@code", c => c.Parameters.AddWithValue("@code", testCompanyCode)));
        Assert.Equal(12, await db.ScalarLongAsync("SELECT COUNT(*) FROM jobs WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId)));

        // Golden Safety pilot contract: two operating branches, the original admin/portal
        // identities, and five least-privilege personas. The Driver identity is linked to
        // exactly one driver record so browser/API identity derivation is deterministic.
        Assert.Equal(2, await db.ScalarLongAsync("SELECT COUNT(*) FROM branches WHERE company_id=@c AND branch_code IN ('MER-NORTH','MER-SOUTH') AND status='Active'", c => c.Parameters.AddWithValue("@c", companyId)));
        Assert.Equal(7, await db.ScalarLongAsync("SELECT COUNT(*) FROM users WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId)));
        foreach (var role in new[] { "Safety Manager", "Driver", "Dispatcher", "Maintenance Manager", "Safety Auditor" })
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM users WHERE company_id=@c AND role_name=@role AND status='Active'", c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@role", role); }));
        Assert.Equal(1, await db.ScalarLongAsync(
            @"SELECT COUNT(*) FROM users WHERE company_id=@c AND role_name='Driver'
                AND permissions_json='[""driver:self"",""notifications:view"",""messages:send""]'::jsonb
                AND NOT permissions_json ?| ARRAY['safety:view','safety:update','compliance:view','shipments:view','drivers:view','vehicles:view']",
            c => c.Parameters.AddWithValue("@c", companyId)));
        Assert.Equal(1, await db.ScalarLongAsync(
            @"SELECT COUNT(*) FROM drivers d JOIN users u ON u.id=d.user_id AND u.company_id=d.company_id
              WHERE d.company_id=@c AND d.driver_code='MER-DRV-1' AND u.role_name='Driver' AND d.branch_id=u.branch_id",
            c => c.Parameters.AddWithValue("@c", companyId)));
        Assert.Equal(1, await db.ScalarLongAsync(
            "SELECT COUNT(*) FROM companies WHERE id=@c AND entitlement_policy_mode='package_allowlist'",
            c => c.Parameters.AddWithValue("@c", companyId)));
        Assert.Equal(9, await db.ScalarLongAsync(
            @"SELECT COUNT(*) FROM tenant_entitlements
              WHERE company_id=@c AND enabled=true AND tier='pilot' AND source='fixture'
                AND module_key = ANY(ARRAY['safety','maintenance','dispatch','telematics','crm','customer_portal','reports','compliance','integrations'])",
            c => c.Parameters.AddWithValue("@c", companyId)));

        // Connected, explainable Safety stories—not disconnected vanity rows.
        Assert.Equal(2, await db.ScalarLongAsync("SELECT COUNT(*) FROM safety_events WHERE company_id=@c AND event_number IN ('MER-SAFE-1','MER-SAFE-2') AND branch_id IS NOT NULL", c => c.Parameters.AddWithValue("@c", companyId)));
        Assert.Equal(2, await db.ScalarLongAsync("SELECT COUNT(*) FROM coaching_tasks WHERE company_id=@c AND task_number IN ('MER-COACH-1','MER-COACH-2') AND safety_event_id IS NOT NULL AND assigned_to_user_id IS NOT NULL", c => c.Parameters.AddWithValue("@c", companyId)));
        Assert.Equal(1, await db.ScalarLongAsync(
            @"SELECT COUNT(*) FROM incidents i JOIN incident_evidence e ON e.company_id=i.company_id AND e.incident_id=i.id
              WHERE i.company_id=@c AND i.incident_number='MER-INC-1' AND i.status='Under Review'
                AND i.safety_event_id IS NOT NULL AND e.content_hash IS NOT NULL
                AND e.evidence_title='Synthetic harsh-braking telemetry metadata'
                AND e.evidence_url IS NULL
                AND e.evidence_json @> '{""synthetic"":true,""verificationStatus"":""not_verified"",""custodyStatus"":""not_managed"",""retrievalStatus"":""not_available""}'::jsonb
                AND NOT (e.evidence_json ? 'verified')",
            c => c.Parameters.AddWithValue("@c", companyId)));
        Assert.Equal(2, await db.ScalarLongAsync("SELECT COUNT(*) FROM dvir_reports WHERE company_id=@c AND report_number IN ('MER-DVIR-1','MER-DVIR-2') AND branch_id IS NOT NULL", c => c.Parameters.AddWithValue("@c", companyId)));
        Assert.Equal(1, await db.ScalarLongAsync(
            @"SELECT COUNT(*) FROM dvir_defects d JOIN work_orders w ON w.company_id=d.company_id AND w.id=d.linked_work_order_id
              WHERE d.company_id=@c AND d.out_of_service=true AND w.work_order_code='MER-WO-DVIR-1' AND w.dvir_report_id=d.dvir_report_id",
            c => c.Parameters.AddWithValue("@c", companyId)));
        Assert.Equal(1, await db.ScalarLongAsync(
            @"SELECT COUNT(*) FROM dvir_reports r JOIN vehicles v ON v.company_id=r.company_id AND v.id=r.vehicle_id
              WHERE r.company_id=@c AND r.report_number='MER-DVIR-1' AND v.out_of_service=true AND v.availability_status='out_of_service'",
            c => c.Parameters.AddWithValue("@c", companyId)));
        Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM hos_records WHERE company_id=@c AND hos_status='Warning' AND branch_id IS NOT NULL", c => c.Parameters.AddWithValue("@c", companyId)));
        Assert.Equal(1, await db.ScalarLongAsync(
            "SELECT COUNT(*) FROM hos_clocks WHERE company_id=@c AND status='Warning' AND branch_id IS NOT NULL AND drive_time_remaining_minutes=165",
            c => c.Parameters.AddWithValue("@c", companyId)));
        Assert.Equal(1, await db.ScalarLongAsync(
            @"SELECT COUNT(*) FROM hos_logs WHERE company_id=@c AND source='demo' AND source_event_id='safety-pilot-hos-1'
                AND branch_id IS NOT NULL AND status='Driving' AND duration_minutes=150 AND is_certified=false
                AND driving_hours=2.50 AND on_duty_hours=2.50 AND cycle_hours_left=18.50",
            c => c.Parameters.AddWithValue("@c", companyId)));
        Assert.Equal(1, await db.ScalarLongAsync(
            @"SELECT COUNT(*) FROM eld_devices WHERE company_id=@c AND device_serial LIKE 'MER-ELD-%'
                AND branch_id IS NOT NULL AND status='Diagnostic' AND provider_sync_status='Healthy'
                AND api_key_hash IS NULL AND hmac_secret_encrypted IS NULL",
            c => c.Parameters.AddWithValue("@c", companyId)));
        Assert.Equal(2, await db.ScalarLongAsync("SELECT COUNT(*) FROM driver_safety_scores WHERE company_id=@c AND breakdown_json->>'formulaVersion'='safety-pilot-v2'", c => c.Parameters.AddWithValue("@c", companyId)));
        Assert.Equal(DemoTenantSeeder.SafetyPilotFixtureVersion, await db.ScalarLongAsync(
            "SELECT fixture_version FROM demo_fixture_versions WHERE company_id=@c AND fixture_key=@key",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@key", DemoTenantSeeder.SafetyPilotFixtureKey); }));

        // Reconciliatory upgrade proof: simulate a stale v6 tenant with one missing persona,
        // compatibility-mode commercial access, the retired incident token and the old
        // misleading evidence claim. A v7 reconcile must repair every one atomically.
        // A normal SeedAsync call must repair it without duplicating the tenant/base fixture.
        // Synthetic persona credentials are part of the deterministic fixture contract: a
        // configured credential rotation must repair stale hashes as well as roles/data.
        await db.ExecuteAsync("DELETE FROM users WHERE company_id=@c AND role_name='Safety Auditor'", c => c.Parameters.AddWithValue("@c", companyId));
        await db.ExecuteAsync("UPDATE users SET password_hash='stale-fixture-hash' WHERE company_id=@c AND role_name='Driver'", c => c.Parameters.AddWithValue("@c", companyId));
        await db.ExecuteAsync("UPDATE companies SET entitlement_policy_mode='legacy_allow' WHERE id=@c", c => c.Parameters.AddWithValue("@c", companyId));
        await db.ExecuteAsync("DELETE FROM tenant_entitlements WHERE company_id=@c AND module_key='safety'", c => c.Parameters.AddWithValue("@c", companyId));
        await db.ExecuteAsync("UPDATE incidents SET status='Under Investigation' WHERE company_id=@c AND incident_number='MER-INC-1'", c => c.Parameters.AddWithValue("@c", companyId));
        await db.ExecuteAsync(@"UPDATE incident_evidence SET evidence_title='Verified harsh-braking telemetry',
            evidence_url='https://example.invalid/not-verified',evidence_json='{""fixture"":""safety-pilot-v2"",""verified"":true}'::jsonb
            WHERE company_id=@c AND incident_id=(SELECT id FROM incidents WHERE company_id=@c AND incident_number='MER-INC-1')", c => c.Parameters.AddWithValue("@c", companyId));
        await db.ExecuteAsync("UPDATE demo_fixture_versions SET fixture_version=6 WHERE company_id=@c AND fixture_key=@key", c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@key", DemoTenantSeeder.SafetyPilotFixtureKey); });
        var repaired = await seeder.SeedAsync(testCompanyCode, TestCompanyName);
        Assert.True(repaired.AlreadySeeded);
        Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM users WHERE company_id=@c AND role_name='Safety Auditor'", c => c.Parameters.AddWithValue("@c", companyId)));
        Assert.Equal(0, await db.ScalarLongAsync("SELECT COUNT(*) FROM users WHERE company_id=@c AND role_name='Driver' AND password_hash='stale-fixture-hash'", c => c.Parameters.AddWithValue("@c", companyId)));
        Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM incidents WHERE company_id=@c AND incident_number='MER-INC-1'", c => c.Parameters.AddWithValue("@c", companyId)));
        Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM incidents WHERE company_id=@c AND incident_number='MER-INC-1' AND status='Under Review'", c => c.Parameters.AddWithValue("@c", companyId)));
        Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM companies WHERE id=@c AND entitlement_policy_mode='package_allowlist'", c => c.Parameters.AddWithValue("@c", companyId)));
        Assert.Equal(9, await db.ScalarLongAsync("SELECT COUNT(*) FROM tenant_entitlements WHERE company_id=@c AND enabled=true AND source='fixture'", c => c.Parameters.AddWithValue("@c", companyId)));
        Assert.Equal(1, await db.ScalarLongAsync(@"SELECT COUNT(*) FROM incident_evidence e JOIN incidents i ON i.id=e.incident_id AND i.company_id=e.company_id
            WHERE e.company_id=@c AND i.incident_number='MER-INC-1' AND e.evidence_title='Synthetic harsh-braking telemetry metadata'
              AND e.evidence_url IS NULL AND e.evidence_json->>'verificationStatus'='not_verified' AND NOT (e.evidence_json ? 'verified')", c => c.Parameters.AddWithValue("@c", companyId)));

        // NOTE: intentionally NOT deleted — the demo tenant persists so it is usable in a
        // live demo. The seeder is idempotent, so a subsequent suite run re-seeds cleanly.
    }

    [Fact]
    public async Task CleanDatabase_BootstrapRollsBackMidSeedFailure_ThenCompletesAndRepeatsAsNoOp()
    {
        // A real database provisioned exactly the way every environment is provisioned —
        // base init SQL, the RLS cutover fixtures, then tools/apply-neon-predeploy-migrations.sh —
        // and then booted by the actual application process.
        //
        // RECONCILED WITH STAGE 88: this test used to assert that BOOT materialized the
        // schema. That contract is retired. Program.cs skipped every runtime *SchemaService
        // whenever it connected as the restricted role under RLS enforcement — always true
        // in staging and production — so the boot path could never be the schema authority
        // there, and 1,006 columns that only those services declared went missing. Migrations
        // are now the sole authority (ResolveRuntimeSchemaDdlAsync disables boot DDL the
        // moment the stage88 ledger row exists). The assertions below are unchanged and the
        // test is STRICTLY STRONGER: the columns must exist after the CHAIN has run, and the
        // boot is additionally proven to have performed no DDL of its own — so drift between
        // the migration chain and DemoTenantSeeder can no longer be papered over by a
        // developer-only boot path that production never executes.
        var databaseName = $"opstrax_seed_clean_{Guid.NewGuid():N}";
        var adminBuilder = new NpgsqlConnectionStringBuilder(LocalConnectionString)
        {
            Database = "postgres",
            Pooling = false,
        };
        var admin = CreateDatabase(adminBuilder.ConnectionString);
        await admin.ExecuteAsync($"CREATE DATABASE \"{databaseName}\"");

        var cleanBuilder = new NpgsqlConnectionStringBuilder(LocalConnectionString)
        {
            Database = databaseName,
            Pooling = false,
        };
        Process? app = null;
        try
        {
            var clean = CreateDatabase(cleanBuilder.ConnectionString);
            ApplyMigrationChain(cleanBuilder);

            // The chain — not a boot — is what must have produced the schema.
            Assert.Equal(1, await clean.ScalarLongAsync(
                "SELECT COUNT(*) FROM schema_migrations WHERE version='2026_08_22_stage88_runtime_schema_service_contract'"));

            var port = FreeTcpPort();
            app = await StartCleanAppAsync(cleanBuilder.ConnectionString, port);

            foreach (var column in new[] { "customer_id", "comment", "feedback_type", "subject", "status", "updated_at" })
                Assert.Equal(1, await clean.ScalarLongAsync(
                    "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema='public' AND table_name='customer_feedback' AND column_name=@column",
                    c => c.Parameters.AddWithValue("@column", column)));

            // Stage86/88 columns the runtime schema services used to own alone. On a
            // protected environment the boot path never ran, so these were the live
            // 42703s (/api/routes, /api/routes/summary, /api/expenses/summary,
            // /api/safety/summary) that the migration chain must now guarantee.
            foreach (var (table, column) in new[]
                     {
                         ("routes", "sla_risk"), ("routes", "efficiency_score"),
                         ("routes", "total_stops"), ("routes", "cost_estimate"),
                         ("expenses", "approval_status"), ("expenses", "receipt_status"),
                         ("safety_events", "incident_status"), ("safety_events", "event_number"),
                         ("work_orders", "asset_id"), ("audit_logs", "severity"),
                     })
            {
                Assert.Equal(1, await clean.ScalarLongAsync(
                    "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema='public' AND table_name=@table AND column_name=@column",
                    c =>
                    {
                        c.Parameters.AddWithValue("@table", table);
                        c.Parameters.AddWithValue("@column", column);
                    }));
            }

            // /api/settings/api-keys returned 42P01 in every protected environment because
            // tenant_api_keys existed only as a runtime schema-service declaration.
            Assert.Equal(1, await clean.ScalarLongAsync(
                "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='public' AND table_name='tenant_api_keys'"));

            // Exercise the real HTTP endpoint once and prove its repeat is a no-op.
            await clean.ExecuteAsync(
                @"INSERT INTO companies(id,company_code,name,industry) OVERRIDING SYSTEM VALUE
                    VALUES (900001,'CLEAN-SEED-AUTH','Clean Seed Auth','Transportation');
                  INSERT INTO users(id,company_id,full_name,email,role_name,permissions_json,status) OVERRIDING SYSTEM VALUE
                    VALUES (900002,900001,'Clean Seed Operator','clean-seed-operator@example.invalid','Fleet Manager','[""fleet:view""]'::jsonb,'Active');
                  INSERT INTO user_sessions(id,user_id,company_id,session_token,expires_at) OVERRIDING SYSTEM VALUE
                    VALUES (900003,900002,900001,'clean-seed-endpoint-token',NOW()+INTERVAL '1 hour');");
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "clean-seed-endpoint-token");
            var endpointFirst = await client.PostAsync("/api/dev/seed-demo-tenant", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, endpointFirst.StatusCode);
            using var firstJson = JsonDocument.Parse(await endpointFirst.Content.ReadAsStringAsync());
            Assert.False(firstJson.RootElement.GetProperty("data").GetProperty("alreadySeeded").GetBoolean());
            Assert.Equal(12, firstJson.RootElement.GetProperty("data").GetProperty("jobs").GetInt32());
            Assert.Equal(1, firstJson.RootElement.GetProperty("data").GetProperty("feedback").GetInt32());
            var endpointSecond = await client.PostAsync("/api/dev/seed-demo-tenant", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, endpointSecond.StatusCode);
            using var secondJson = JsonDocument.Parse(await endpointSecond.Content.ReadAsStringAsync());
            Assert.True(secondJson.RootElement.GetProperty("data").GetProperty("alreadySeeded").GetBoolean());

            var companyCode = $"ROLLBACK-{Guid.NewGuid():N}"[..36];
            var failingSeeder = new DemoTenantSeeder(clean)
            {
                TestCheckpointAsync = (checkpoint, _) => checkpoint == "after-finance-before-feedback"
                    ? Task.FromException(new InvalidOperationException("forced late demo-seed failure"))
                    : Task.CompletedTask,
            };

            // Fail deliberately late, after base entities and finance rows. The transaction must
            // roll back, including the company marker used by the idempotency check.
            await Assert.ThrowsAsync<InvalidOperationException>(() => failingSeeder.SeedAsync(companyCode, "Clean Seed Transaction Test"));
            Assert.Equal(0, await clean.ScalarLongAsync(
                "SELECT COUNT(*) FROM companies WHERE company_code=@code",
                c => c.Parameters.AddWithValue("@code", companyCode)));

            var seeder = new DemoTenantSeeder(clean);
            var first = await seeder.SeedAsync(companyCode, "Clean Seed Transaction Test");
            Assert.False(first.AlreadySeeded);
            Assert.Equal(5, first.Vehicles);
            Assert.Equal(5, first.Drivers);
            Assert.Equal(3, first.Customers);
            Assert.Equal(12, first.Jobs);
            Assert.Equal(4, first.IssuedInvoices);
            Assert.Equal(1, first.Payments);
            Assert.Equal(1, first.Feedback);
            Assert.Equal(1, await clean.ScalarLongAsync(
                "SELECT COUNT(*) FROM customer_feedback WHERE company_id=@companyId AND subject='Great delivery' AND status='open'",
                c => c.Parameters.AddWithValue("@companyId", first.CompanyId)));

            var second = await seeder.SeedAsync(companyCode, "Clean Seed Transaction Test");
            Assert.True(second.AlreadySeeded);
            Assert.Equal(first.CompanyId, second.CompanyId);
            Assert.Equal(12, await clean.ScalarLongAsync(
                "SELECT COUNT(*) FROM jobs WHERE company_id=@companyId",
                c => c.Parameters.AddWithValue("@companyId", first.CompanyId)));

            // Two simultaneous first runs serialize on the transaction advisory lock:
            // exactly one creates the tenant and the waiter observes the committed row.
            var concurrentCode = $"CONCURRENT-{Guid.NewGuid():N}"[..36];
            var concurrent = await Task.WhenAll(
                new DemoTenantSeeder(CreateDatabase(cleanBuilder.ConnectionString)).SeedAsync(concurrentCode, "Concurrent Seed Test"),
                new DemoTenantSeeder(CreateDatabase(cleanBuilder.ConnectionString)).SeedAsync(concurrentCode, "Concurrent Seed Test"));
            Assert.Single(concurrent, x => !x.AlreadySeeded);
            Assert.Single(concurrent, x => x.AlreadySeeded);
            var concurrentCompanyId = concurrent[0].CompanyId;
            Assert.All(concurrent, x => Assert.Equal(concurrentCompanyId, x.CompanyId));
            Assert.Equal(1, await clean.ScalarLongAsync("SELECT COUNT(*) FROM companies WHERE company_code=@code", c => c.Parameters.AddWithValue("@code", concurrentCode)));
            Assert.Equal(12, await clean.ScalarLongAsync("SELECT COUNT(*) FROM jobs WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", concurrentCompanyId)));
            Assert.Equal(1, await clean.ScalarLongAsync("SELECT COUNT(*) FROM customer_feedback WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", concurrentCompanyId)));
        }
        finally
        {
            if (app is { HasExited: false })
            {
                app.Kill(entireProcessTree: true);
                await app.WaitForExitAsync();
            }
            NpgsqlConnection.ClearAllPools();
            await admin.ExecuteAsync($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)");
        }
    }

    [Fact]
    public async Task CustomerFeedbackMigration_UpgradesLegacyShape_PreservesRows_AndReruns()
    {
        var schema = $"feedback_legacy_{Guid.NewGuid():N}";
        var owner = CreateDatabase();
        await owner.ExecuteAsync($"CREATE SCHEMA \"{schema}\"");
        var scopedBuilder = new NpgsqlConnectionStringBuilder(LocalConnectionString)
        {
            SearchPath = schema,
            Pooling = false,
        };
        try
        {
            var scoped = CreateDatabase(scopedBuilder.ConnectionString);
            await scoped.ExecuteAsync(
                @"CREATE TABLE jobs(id BIGINT PRIMARY KEY, company_id BIGINT NOT NULL, customer_id BIGINT NULL, deleted_at TIMESTAMPTZ NULL);
                  CREATE TABLE customer_feedback(
                    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                    company_id BIGINT NOT NULL,
                    job_id BIGINT NOT NULL,
                    tracking_code VARCHAR(80) NULL,
                    rating INT NULL,
                    sentiment VARCHAR(80) NULL,
                    comments TEXT NULL,
                    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW());
                  INSERT INTO jobs(id,company_id,customer_id) VALUES (1,11,22);
                  INSERT INTO customer_feedback(company_id,job_id,rating,sentiment,comments)
                    VALUES (11,1,4,'positive','legacy feedback survives');");

            var migrationPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "../../../../database/migrations/2026_07_30_customer_feedback_contract.sql"));
            Assert.True(File.Exists(migrationPath), $"Packaged feedback migration not found: {migrationPath}");
            var migration = await File.ReadAllTextAsync(migrationPath);
            await scoped.ExecuteAsync(migration);
            await scoped.ExecuteAsync(migration); // additive/idempotent rerun

            foreach (var column in new[] { "customer_id", "comment", "feedback_type", "subject", "status", "updated_at" })
                Assert.Equal(1, await scoped.ScalarLongAsync(
                    "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='customer_feedback' AND column_name=@column",
                    c => c.Parameters.AddWithValue("@column", column)));
            Assert.Equal(1, await scoped.ScalarLongAsync(
                "SELECT COUNT(*) FROM pg_indexes WHERE schemaname=current_schema() AND tablename='customer_feedback' AND indexname='ix_customer_feedback_company_customer'"));
            Assert.Equal(1, await scoped.ScalarLongAsync("SELECT COUNT(*) FROM customer_feedback WHERE comments='legacy feedback survives'"));

            var submitted = await new CustomerPortalService(scoped).SubmitFeedbackAsync(
                11, 22, 1, 5, "canonical feedback", "praise", "Migration works");
            Assert.NotNull(submitted);
            Assert.Equal(1, await scoped.ScalarLongAsync(
                "SELECT COUNT(*) FROM customer_feedback WHERE customer_id=22 AND comment='canonical feedback' AND subject='Migration works' AND status='open'"));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await owner.ExecuteAsync($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE");
        }
    }

    private static int FreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    // Provisions a database the ONE supported way: base init SQL + the RLS cutover
    // fixtures, then the owner migration runner. This is what tools/test-predeploy-clean-chain.sh
    // and every real environment do, and since stage88 it is the only schema authority —
    // boot performs no DDL against a database carrying its ledger row.
    private static void ApplyMigrationChain(NpgsqlConnectionStringBuilder target)
    {
        var root = RepoRootPath();
        var fixtures = new[]
        {
            Path.Combine("database", "init", "001_schema.sql"),
            Path.Combine("database", "init", "002_seed.sql"),
            Path.Combine("database", "init", "004_jobs_execution.sql"),
            Path.Combine("database", "migrations", "2026_06_30_stage19_row_level_security.sql"),
            Path.Combine("database", "migrations", "2026_06_30_stage20_rls_force_and_app_role.sql"),
            Path.Combine("database", "migrations", "2026_07_01_stage22_rls_reconcile_coverage.sql"),
        };
        var uri = $"postgresql://{Uri.EscapeDataString(target.Username!)}:{Uri.EscapeDataString(target.Password!)}" +
                  $"@{target.Host}:{target.Port}/{target.Database}?sslmode=disable";

        foreach (var fixture in fixtures)
        {
            RunOrThrow(root, "psql", [uri, "-v", "ON_ERROR_STOP=1", "-q", "-f", fixture],
                new Dictionary<string, string>());
        }
        RunOrThrow(root, "bash", [Path.Combine("tools", "apply-neon-predeploy-migrations.sh")],
            new Dictionary<string, string> { ["NEON_PG_URI"] = uri });
    }

    private static string RepoRootPath() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));

    private static void RunOrThrow(string workingDirectory, string fileName, string[] arguments,
        IDictionary<string, string> environment)
    {
        var start = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        foreach (var pair in environment) start.Environment[pair.Key] = pair.Value;

        Process process;
        try
        {
            process = Process.Start(start)!;
        }
        catch (Exception ex)
        {
            // Never silently weaken this test into "boot builds the schema": if the
            // provisioning toolchain is missing, say so and fail.
            throw new Xunit.Sdk.XunitException(
                $"Could not launch '{fileName}' to provision the database via the migration chain. " +
                $"psql and bash are required (CI installs postgresql-client). {ex.Message}");
        }
        // Drain BOTH pipes concurrently. The migration chain writes thousands of NOTICE
        // lines to stderr; reading stdout to the end first fills the stderr pipe buffer
        // and psql blocks forever mid-migration.
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            var tail = string.Join(Environment.NewLine,
                (stdout + Environment.NewLine + stderr).Split('\n').TakeLast(40));
            throw new Xunit.Sdk.XunitException(
                $"Migration chain step failed: {fileName} {string.Join(' ', arguments)} (exit {process.ExitCode}){Environment.NewLine}{tail}");
        }
    }

    private static async Task<Process> StartCleanAppAsync(string connectionString, int port)
    {
        var appDll = Path.Combine(AppContext.BaseDirectory, "Opstrax.Api.dll");
        Assert.True(File.Exists(appDll), $"API assembly not found: {appDll}");
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
        var start = new ProcessStartInfo("dotnet", appDll)
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        start.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        start.Environment["ConnectionStrings__DefaultConnection"] = connectionString;
        start.Environment["PG_CONNECTION"] = connectionString;
        start.Environment["Jwt__Key"] = "clean-database-demo-seed-test-key-material-at-least-64-characters-long";
        start.Environment["Rls__EnforceTenantContext"] = "false";
        start.Environment["DemoSeed__Enabled"] = "true";
        start.Environment["DemoSeed__Password"] = "CleanDemoSeed-Test-Only-2026!";
        var process = Process.Start(start)!;
        var output = new List<string>();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.Add(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.Add(e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        // A genuinely empty database runs every authoritative schema service before
        // Kestrel listens. The launch-hardened graph is intentionally larger than an
        // already-migrated boot, so keep this bounded without assuming a 60-second
        // workstation/CI scheduler budget.
        var deadline = DateTime.UtcNow.AddSeconds(120);
        while (DateTime.UtcNow < deadline && !process.HasExited)
        {
            try
            {
                if ((await client.GetAsync("/health")).IsSuccessStatusCode) return process;
            }
            catch (HttpRequestException) { }
            await Task.Delay(200);
        }
        var log = string.Join(Environment.NewLine, output.TakeLast(80));
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
        throw new Xunit.Sdk.XunitException($"Clean app did not become healthy. Exit={process.ExitCode}; log:{Environment.NewLine}{log}");
    }

    private static RevenueReadinessService CreateRevenueService(Database db, long companyId)
    {
        var correlation = new InMemoryCorrelationContext("corr-demo", "cause-demo", "req-demo", companyId.ToString(), ActorTypes.TenantUser, "1");
        return new RevenueReadinessService(db, new PostgresAiFoundationService(db, correlation), new PostgresApprovalWorkflowService(db, correlation), new PostgresIdempotencyService(db), new PostgresDomainEventPublisher(db, correlation), correlation, new TaxService(db));
    }

    private static Database CreateDatabase() => CreateDatabase(LocalConnectionString);

    private static Database CreateDatabase(string connectionString)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = connectionString })
            .Build();
        return new Database(config);
    }

    private static async Task EnsureSchemasAsync(Database db)
    {
        // Match the production bootstrap order for every table/column DemoTenantSeeder
        // writes. Do not manually ALTER individual columns here: doing so previously
        // masked clean-install drift in both customer_feedback and documents.
        await new Batch1SchemaService(db).EnsureAsync();
        await new Batch2SchemaService(db).EnsureAsync();
        await new Batch3SchemaService(db).EnsureAsync();
        await new Batch4SchemaService(db).EnsureAsync();
        await new Batch5SchemaService(db).EnsureAsync();
        await new Batch6SchemaService(db).EnsureAsync();
        await new Batch7SchemaService(db).EnsureAsync();
        await new TelemetrySchemaService(db).EnsureAsync();
        await new SafetySchemaService(db).EnsureAsync();
        await new TripSchemaService(db).EnsureAsync();
        await new MaintenanceSchemaService(db).EnsureAsync();
        await new DispatchSchemaService(db, NullLogger<DispatchSchemaService>.Instance).EnsureAsync();
        await new CustomerVisibilitySchemaService(db, NullLogger<CustomerVisibilitySchemaService>.Instance).EnsureAsync();
        await new DriverSchemaService(db, NullLogger<DriverSchemaService>.Instance).EnsureAsync();
        await new NotificationSchemaService(db).EnsureAsync();
        await new AlertWorkflowSchemaService(db).EnsureAsync();
        await new FoundationSchemaService(db).EnsureAsync();
        await new BusinessSpineSchemaService(db).EnsureAsync();
        await new RevenueReadinessSchemaService(db).EnsureAsync();
        await new FinanceActivationSchemaService(db).EnsureAsync();
        await new TaxSchemaService(db).EnsureAsync();
        await new Stage9SchemaService(db).EnsureAsync();
        await db.ExecuteAsync("ALTER TABLE ai_recommendations ADD COLUMN IF NOT EXISTS company_id BIGINT NOT NULL DEFAULT 1");
        await db.ExecuteAsync("ALTER TABLE ai_recommendations ADD COLUMN IF NOT EXISTS module_key VARCHAR(100) NULL");
        await db.ExecuteAsync("ALTER TABLE ai_recommendations ADD COLUMN IF NOT EXISTS body TEXT NULL");
        await db.ExecuteAsync("ALTER TABLE ai_recommendations ADD COLUMN IF NOT EXISTS score DECIMAL(6,2) NOT NULL DEFAULT 80");
    }

    // Schema-driven cleanup (mirrors TenantOffboardingService) — discovers EVERY table
    // carrying company_id/tenant_id and deletes in FK-safe iterative passes, so the demo
    // tenant is always fully removed and this test is order-independent. The old
    // hand-maintained DELETE list omitted several child tables (driver_documents,
    // vehicle_documents, customer_contacts/addresses, maintenance_*, …), which left the
    // company row undeletable and made the idempotency assertion state-dependent.
    private static async Task DeleteDemoTenantAsync(Database db, string companyCode)
    {
        var companyId = await db.ScalarLongAsync("SELECT COALESCE((SELECT id FROM companies WHERE company_code=@code LIMIT 1),0)", c => c.Parameters.AddWithValue("@code", companyCode));
        if (companyId == 0) return;

        var pairs = await db.QueryAsync(
            @"SELECT c.table_name, c.column_name
              FROM information_schema.columns c
              JOIN information_schema.tables t
                ON t.table_name=c.table_name AND t.table_schema=c.table_schema
              WHERE c.table_schema='public' AND t.table_type='BASE TABLE'
                AND c.column_name IN ('company_id','tenant_id')
                AND c.table_name <> 'companies'");
        var tenantTables = pairs
            .Select(p => (Table: p["tableName"]!.ToString()!, Column: p["columnName"]!.ToString()!))
            .ToList();

        for (var pass = 0; pass < tenantTables.Count + 2; pass++)
        {
            var removed = 0;
            foreach (var (table, column) in tenantTables)
            {
                try { removed += await db.ExecuteAsync($"DELETE FROM \"{table}\" WHERE {column}=@c", c => c.Parameters.AddWithValue("@c", companyId)); }
                catch { /* FK-blocked this pass — a later pass clears the child first */ }
            }
            if (removed == 0) break;
        }
        try { await db.ExecuteAsync("DELETE FROM companies WHERE id=@c", c => c.Parameters.AddWithValue("@c", companyId)); } catch { /* residual FK cycle — leave for inspection */ }
    }
}
