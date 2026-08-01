using Microsoft.Extensions.Logging;
using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

// P4 Dispatch Execution Workflow — schema bootstrap.
// Adds missing columns to dispatch_assignments and creates new tables:
//   dispatch_exceptions, dispatch_proofs, dispatch_eligibility_config
public sealed class DispatchSchemaService(Database db, ILogger<DispatchSchemaService> log)
{
    public async Task EnsureAsync()
    {
        await AddColumns();
        await CreateTables();
        await CreateIndexes();
    }

    private async Task AddColumns()
    {
        // The public assignment contract allows operational assignments before a
        // job exists. Reconcile legacy bootstrap schemas that required job_id.
        await db.ExecuteAsync("ALTER TABLE dispatch_assignments ALTER COLUMN job_id DROP NOT NULL");
        var cols = new[]
        {
            ("dispatch_assignments", "route_id",            "BIGINT NULL"),
            ("dispatch_assignments", "trailer_id",          "BIGINT NULL"),
            ("dispatch_assignments", "planned_pickup_at",   "TIMESTAMPTZ NULL"),
            ("dispatch_assignments", "planned_delivery_at", "TIMESTAMPTZ NULL"),
            ("dispatch_assignments", "actual_pickup_at",    "TIMESTAMPTZ NULL"),
            ("dispatch_assignments", "actual_delivery_at",  "TIMESTAMPTZ NULL"),
            ("dispatch_assignments", "accepted_at",         "TIMESTAMPTZ NULL"),
            ("dispatch_assignments", "acceptance_due_at",   "TIMESTAMPTZ NULL"),
            ("dispatch_assignments", "trip_id",             "BIGINT NULL"),
            ("dispatch_assignments", "notes",               "TEXT NULL"),
            ("dispatch_assignments", "override_reason",     "VARCHAR(500) NULL"),
            ("dispatch_assignments", "safety_overridden",   "BOOLEAN NOT NULL DEFAULT false"),
            ("dispatch_assignments", "hos_overridden",      "BOOLEAN NOT NULL DEFAULT false"),
            ("dispatch_assignments", "eligibility_json",    "JSONB NULL"),
            ("dispatch_assignments", "exception_count",     "INT NOT NULL DEFAULT 0"),
            ("dispatch_assignments", "previous_status",     "VARCHAR(30) NULL"),
            ("dispatch_assignments", "branch_id",           "BIGINT NULL"),
            ("dispatch_assignments", "assignment_status",   "VARCHAR(60) NULL"),
            ("dispatch_assignments", "cancelled_at",        "TIMESTAMPTZ NULL"),
            ("dispatch_assignments", "created_at",          "TIMESTAMPTZ NOT NULL DEFAULT NOW()"),
        };

        foreach (var (table, col, def) in cols)
        {
            try
            {
                await db.ExecuteAsync(
                    $"ALTER TABLE \"{table}\" ADD COLUMN \"{col}\" {def}");
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "42701") { /* column exists */ }
            catch (Exception ex) { log.LogWarning(ex, "[DispatchSchema] ALTER {Table}.{Col} failed", table, col); }
        }
    }

    private async Task CreateTables()
    {
        // Dispatch exceptions — linked to assignment + optional trip
        await TryCreate("dispatch_exceptions", @"
CREATE TABLE IF NOT EXISTS dispatch_exceptions (
    id                  BIGINT NOT NULL GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id          BIGINT NOT NULL,
    assignment_id       BIGINT NOT NULL,
    job_id              BIGINT NULL,
    trip_id             BIGINT NULL,
    exception_type      VARCHAR(60)  NOT NULL DEFAULT 'general',
    severity            VARCHAR(30)  NOT NULL DEFAULT 'Medium',
    status              VARCHAR(30)  NOT NULL DEFAULT 'open',
    title               VARCHAR(255) NULL,
    notes               TEXT NULL,
    created_by          BIGINT NULL,
    acknowledged_by     BIGINT NULL,
    resolved_by         BIGINT NULL,
    acknowledged_at     TIMESTAMPTZ NULL,
    resolved_at         TIMESTAMPTZ NULL,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ NULL
)");

        // Proof of pickup / delivery per assignment
        await TryCreate("dispatch_proofs", @"
CREATE TABLE IF NOT EXISTS dispatch_proofs (
    id                      BIGINT NOT NULL GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id              BIGINT NOT NULL,
    assignment_id           BIGINT NOT NULL,
    proof_type              VARCHAR(30)  NOT NULL DEFAULT 'delivery',
    confirmed_at            TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    confirmed_by_user_id    BIGINT NULL,
    confirmed_by_driver_id  BIGINT NULL,
    notes                   TEXT NULL,
    evidence_hash           VARCHAR(128) NULL,
    lat                     DECIMAL(9,6) NULL,
    lng                     DECIMAL(9,6) NULL,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT NOW()
)");

        // Driver POD media must survive submission as first-class, tenant-owned rows.
        // This table previously existed only in an optional docker init script, while
        // the runtime always wrote it; migrated/hosted databases therefore failed POD.
        await TryCreate("dispatch_proof_artifacts", @"
CREATE TABLE IF NOT EXISTS dispatch_proof_artifacts (
    id            BIGINT NOT NULL GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id    BIGINT NOT NULL,
    proof_id      BIGINT NOT NULL REFERENCES dispatch_proofs(id) ON DELETE CASCADE,
    kind          VARCHAR(30) NOT NULL CHECK (kind IN ('photo','signature')),
    reference     TEXT NOT NULL,
    content_type  VARCHAR(120) NULL,
    size_bytes    BIGINT NULL,
    created_at    TIMESTAMPTZ NOT NULL DEFAULT NOW()
)");

        // Tenant-configurable eligibility thresholds
        await TryCreate("dispatch_eligibility_config", @"
CREATE TABLE IF NOT EXISTS dispatch_eligibility_config (
    id                              BIGINT NOT NULL GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id                      BIGINT NOT NULL,
    min_driver_safety_score         INT NOT NULL DEFAULT 65,
    block_on_critical_defect        BOOLEAN NOT NULL DEFAULT true,
    block_on_open_work_order        BOOLEAN NOT NULL DEFAULT true,
    block_on_oos                    BOOLEAN NOT NULL DEFAULT true,
    min_hos_hours_required          DECIMAL(4,1) NULL,
    block_on_overdue_pm             BOOLEAN NOT NULL DEFAULT false,
    created_at                      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at                      TIMESTAMPTZ NULL,
    UNIQUE (company_id)
)");
    }

    private async Task CreateIndexes()
    {
        await db.ExecuteAsync(@"UPDATE dispatch_assignments da SET branch_id=j.branch_id
            FROM jobs j WHERE da.branch_id IS NULL AND da.job_id=j.id AND da.company_id=j.company_id AND j.branch_id IS NOT NULL");
        await db.ExecuteAsync(@"UPDATE dispatch_assignments da SET branch_id=v.branch_id
            FROM vehicles v WHERE da.branch_id IS NULL AND da.vehicle_id=v.id AND da.company_id=v.company_id AND v.branch_id IS NOT NULL");
        await db.ExecuteAsync(@"WITH tokens AS (
              SELECT id, LOWER(REPLACE(REPLACE(COALESCE(assignment_status,status,'assigned'),'-','_'),' ','_')) token
              FROM dispatch_assignments),
            normalized AS (
              SELECT id, CASE token
                WHEN 'complete' THEN 'delivered' WHEN 'completed' THEN 'delivered'
                WHEN 'canceled' THEN 'cancelled' WHEN 'en_route' THEN 'en_route_pickup'
                WHEN 'at_pickup' THEN 'arrived_pickup' WHEN 'at_delivery' THEN 'arrived_delivery'
                ELSE token END canonical
              FROM tokens)
            UPDATE dispatch_assignments da SET assignment_status=n.canonical
            FROM normalized n WHERE da.id=n.id AND da.assignment_status IS DISTINCT FROM n.canonical");
        // Reconcile legacy double-bookings deterministically before enforcing the
        // active-resource invariants. Keep the newest row; close older contenders.
        await db.ExecuteAsync(@"WITH ranked AS (
            SELECT id, ROW_NUMBER() OVER (PARTITION BY company_id, vehicle_id ORDER BY assigned_at DESC, id DESC) rn
            FROM dispatch_assignments
            WHERE vehicle_id IS NOT NULL AND COALESCE(assignment_status,status) NOT IN ('delivered','cancelled','Delivered','Cancelled'))
          UPDATE dispatch_assignments SET assignment_status='cancelled', status='Cancelled', cancelled_at=COALESCE(cancelled_at,NOW())
          WHERE id IN (SELECT id FROM ranked WHERE rn>1)");
        await db.ExecuteAsync(@"WITH ranked AS (
            SELECT id, ROW_NUMBER() OVER (PARTITION BY company_id, job_id ORDER BY assigned_at DESC, id DESC) rn
            FROM dispatch_assignments
            WHERE job_id IS NOT NULL AND COALESCE(assignment_status,status) NOT IN ('delivered','cancelled','Delivered','Cancelled'))
          UPDATE dispatch_assignments SET assignment_status='cancelled', status='Cancelled', cancelled_at=COALESCE(cancelled_at,NOW())
          WHERE id IN (SELECT id FROM ranked WHERE rn>1)");
        await db.ExecuteAsync(@"WITH ranked AS (
            SELECT id, ROW_NUMBER() OVER (PARTITION BY company_id, driver_id ORDER BY assigned_at DESC, id DESC) rn
            FROM dispatch_assignments
            WHERE driver_id IS NOT NULL AND COALESCE(assignment_status,status) NOT IN ('delivered','cancelled','Delivered','Cancelled'))
          UPDATE dispatch_assignments SET assignment_status='cancelled', status='Cancelled', cancelled_at=COALESCE(cancelled_at,NOW())
          WHERE id IN (SELECT id FROM ranked WHERE rn>1)");
        await db.ExecuteAsync(@"WITH ranked AS (
            SELECT id, ROW_NUMBER() OVER (PARTITION BY company_id,assignment_id,LOWER(proof_type)
                                          ORDER BY confirmed_at DESC,id DESC) rn
            FROM dispatch_proofs)
          DELETE FROM dispatch_proofs WHERE id IN (SELECT id FROM ranked WHERE rn>1)");
        var indexes = new[]
        {
            ("dispatch_exceptions", "idx_dex_company_assignment", "company_id, assignment_id"),
            ("dispatch_exceptions", "idx_dex_status",             "company_id, status"),
            ("dispatch_proofs",     "idx_dp_assignment",          "assignment_id"),
            ("dispatch_proofs",     "idx_dp_company",             "company_id, proof_type"),
            ("dispatch_proof_artifacts", "idx_dpa_proof",         "proof_id"),
            ("dispatch_proof_artifacts", "idx_dpa_company",       "company_id"),
            ("dispatch_assignments","idx_da_company_status",      "company_id, assignment_status"),
            ("dispatch_assignments","idx_da_driver",              "driver_id"),
            ("dispatch_assignments","idx_da_vehicle",             "vehicle_id"),
            ("dispatch_assignments","idx_da_trip",                "trip_id"),
            ("dispatch_assignments","idx_da_branch",              "company_id, branch_id, created_at"),
            ("dispatch_assignments","idx_da_acceptance_due",      "company_id, assignment_status, acceptance_due_at"),
        };

        foreach (var (table, name, cols) in indexes)
        {
            try
            {
                await db.ExecuteAsync($"CREATE INDEX IF NOT EXISTS \"{name}\" ON \"{table}\" ({cols})");
            }
            catch (Exception ex) { log.LogWarning(ex, "[DispatchSchema] Index {Name} failed", name); }
        }

        await db.ExecuteAsync("CREATE UNIQUE INDEX IF NOT EXISTS uq_da_active_vehicle ON dispatch_assignments (company_id, vehicle_id) WHERE vehicle_id IS NOT NULL AND assignment_status NOT IN ('delivered','cancelled')");
        await db.ExecuteAsync("CREATE UNIQUE INDEX IF NOT EXISTS uq_da_active_driver ON dispatch_assignments (company_id, driver_id) WHERE driver_id IS NOT NULL AND assignment_status NOT IN ('delivered','cancelled')");
        await db.ExecuteAsync("CREATE UNIQUE INDEX IF NOT EXISTS uq_da_active_job ON dispatch_assignments (company_id, job_id) WHERE job_id IS NOT NULL AND assignment_status NOT IN ('delivered','cancelled')");
        await db.ExecuteAsync("CREATE UNIQUE INDEX IF NOT EXISTS uq_dispatch_proof_type ON dispatch_proofs (company_id,assignment_id,LOWER(proof_type))");
    }

    private async Task TryCreate(string table, string ddl)
    {
        try { await db.ExecuteAsync(ddl); }
        catch (Exception ex) { log.LogWarning(ex, "[DispatchSchema] Create {Table} failed", table); }
    }
}
