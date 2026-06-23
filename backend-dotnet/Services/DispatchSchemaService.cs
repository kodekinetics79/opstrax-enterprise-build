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
        var cols = new[]
        {
            ("dispatch_assignments", "route_id",            "BIGINT NULL"),
            ("dispatch_assignments", "trailer_id",          "BIGINT NULL"),
            ("dispatch_assignments", "planned_pickup_at",   "DATETIME NULL"),
            ("dispatch_assignments", "planned_delivery_at", "DATETIME NULL"),
            ("dispatch_assignments", "actual_pickup_at",    "DATETIME NULL"),
            ("dispatch_assignments", "actual_delivery_at",  "DATETIME NULL"),
            ("dispatch_assignments", "accepted_at",         "DATETIME NULL"),
            ("dispatch_assignments", "trip_id",             "BIGINT NULL"),
            ("dispatch_assignments", "notes",               "TEXT NULL"),
            ("dispatch_assignments", "override_reason",     "VARCHAR(500) NULL"),
            ("dispatch_assignments", "safety_overridden",   "TINYINT(1) NOT NULL DEFAULT 0"),
            ("dispatch_assignments", "hos_overridden",      "TINYINT(1) NOT NULL DEFAULT 0"),
            ("dispatch_assignments", "eligibility_json",    "JSON NULL"),
            ("dispatch_assignments", "exception_count",     "INT NOT NULL DEFAULT 0"),
            ("dispatch_assignments", "previous_status",     "VARCHAR(30) NULL"),
        };

        foreach (var (table, col, def) in cols)
        {
            try
            {
                await db.ExecuteAsync(
                    $"ALTER TABLE `{table}` ADD COLUMN `{col}` {def}");
            }
            catch (MySqlConnector.MySqlException ex) when (ex.Number == 1060) { /* column exists */ }
            catch (Exception ex) { log.LogWarning(ex, "[DispatchSchema] ALTER {Table}.{Col} failed", table, col); }
        }
    }

    private async Task CreateTables()
    {
        // Dispatch exceptions — linked to assignment + optional trip
        await TryCreate("dispatch_exceptions", @"
CREATE TABLE IF NOT EXISTS dispatch_exceptions (
    id                  BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
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
    acknowledged_at     DATETIME NULL,
    resolved_at         DATETIME NULL,
    created_at          TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at          TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // Proof of pickup / delivery per assignment
        await TryCreate("dispatch_proofs", @"
CREATE TABLE IF NOT EXISTS dispatch_proofs (
    id                      BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    company_id              BIGINT NOT NULL,
    assignment_id           BIGINT NOT NULL,
    proof_type              VARCHAR(30)  NOT NULL DEFAULT 'delivery',
    confirmed_at            DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    confirmed_by_user_id    BIGINT NULL,
    confirmed_by_driver_id  BIGINT NULL,
    notes                   TEXT NULL,
    evidence_hash           VARCHAR(128) NULL,
    lat                     DECIMAL(9,6) NULL,
    lng                     DECIMAL(9,6) NULL,
    created_at              TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // Tenant-configurable eligibility thresholds
        await TryCreate("dispatch_eligibility_config", @"
CREATE TABLE IF NOT EXISTS dispatch_eligibility_config (
    id                              BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    company_id                      BIGINT NOT NULL,
    min_driver_safety_score         INT NOT NULL DEFAULT 65,
    block_on_critical_defect        TINYINT(1) NOT NULL DEFAULT 1,
    block_on_open_work_order        TINYINT(1) NOT NULL DEFAULT 1,
    block_on_oos                    TINYINT(1) NOT NULL DEFAULT 1,
    min_hos_hours_required          DECIMAL(4,1) NULL,
    block_on_overdue_pm             TINYINT(1) NOT NULL DEFAULT 0,
    created_at                      TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at                      TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    UNIQUE KEY uq_dispatch_config (company_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
    }

    private async Task CreateIndexes()
    {
        var indexes = new[]
        {
            ("dispatch_exceptions", "idx_dex_company_assignment", "company_id, assignment_id"),
            ("dispatch_exceptions", "idx_dex_status",             "company_id, status"),
            ("dispatch_proofs",     "idx_dp_assignment",          "assignment_id"),
            ("dispatch_proofs",     "idx_dp_company",             "company_id, proof_type"),
            ("dispatch_assignments","idx_da_company_status",      "company_id, assignment_status"),
            ("dispatch_assignments","idx_da_driver",              "driver_id"),
            ("dispatch_assignments","idx_da_vehicle",             "vehicle_id"),
            ("dispatch_assignments","idx_da_trip",                "trip_id"),
        };

        foreach (var (table, name, cols) in indexes)
        {
            try
            {
                await db.ExecuteAsync($"ALTER TABLE `{table}` ADD INDEX `{name}` ({cols})");
            }
            catch (MySqlConnector.MySqlException ex) when (ex.Number is 1061 or 1062) { /* index exists */ }
            catch (Exception ex) { log.LogWarning(ex, "[DispatchSchema] Index {Name} failed", name); }
        }
    }

    private async Task TryCreate(string table, string ddl)
    {
        try { await db.ExecuteAsync(ddl); }
        catch (Exception ex) { log.LogWarning(ex, "[DispatchSchema] Create {Table} failed", table); }
    }
}
