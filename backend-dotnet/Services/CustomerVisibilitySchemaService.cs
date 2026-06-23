using Microsoft.Extensions.Logging;
using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

// P5 Customer Visibility + ETA Risk Engine — schema bootstrap.
// Creates:
//   customer_visibility   — per-shipment tracking token and share controls
//   customer_eta_snapshots — cached ETA calculations per assignment
public sealed class CustomerVisibilitySchemaService(Database db, ILogger<CustomerVisibilitySchemaService> log)
{
    public async Task EnsureAsync()
    {
        await CreateTables();
        await CreateIndexes();
    }

    private async Task CreateTables()
    {
        // Core customer-facing visibility entity.
        // public_tracking_token is 64-char hex (32 random bytes), unique, expiring, revocable.
        await TryCreate("customer_visibility", @"
CREATE TABLE IF NOT EXISTS customer_visibility (
    id                      BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    company_id              BIGINT NOT NULL,
    customer_id             BIGINT NULL,
    shipment_id             BIGINT NULL,
    dispatch_assignment_id  BIGINT NULL,
    trip_id                 BIGINT NULL,
    public_tracking_token   VARCHAR(64) NOT NULL,
    visibility_status       VARCHAR(30) NOT NULL DEFAULT 'active',
    share_enabled           TINYINT(1) NOT NULL DEFAULT 1,
    expires_at              DATETIME NOT NULL,
    created_by              BIGINT NULL,
    created_at              TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at              TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    UNIQUE KEY uq_cv_token (public_tracking_token)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // ETA snapshots — one row per assignment, updated by background or API compute.
        await TryCreate("customer_eta_snapshots", @"
CREATE TABLE IF NOT EXISTS customer_eta_snapshots (
    id                      BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    company_id              BIGINT NOT NULL,
    dispatch_assignment_id  BIGINT NOT NULL,
    eta_at                  DATETIME NULL,
    confidence              VARCHAR(10) NOT NULL DEFAULT 'unknown',
    risk                    VARCHAR(20) NOT NULL DEFAULT 'unknown',
    reason_codes            JSON NULL,
    explanation             TEXT NULL,
    telemetry_stale         TINYINT(1) NOT NULL DEFAULT 0,
    last_position_at        DATETIME NULL,
    computed_at             TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    UNIQUE KEY uq_eta_assignment (dispatch_assignment_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
    }

    private async Task CreateIndexes()
    {
        var indexes = new[]
        {
            ("customer_visibility",    "idx_cv_company_customer",  "company_id, customer_id"),
            ("customer_visibility",    "idx_cv_assignment",        "dispatch_assignment_id"),
            ("customer_visibility",    "idx_cv_shipment",          "company_id, shipment_id"),
            ("customer_eta_snapshots", "idx_ces_company",          "company_id"),
            ("customer_eta_snapshots", "idx_ces_risk",             "company_id, risk"),
        };

        foreach (var (table, name, cols) in indexes)
        {
            try
            {
                await db.ExecuteAsync($"ALTER TABLE `{table}` ADD INDEX `{name}` ({cols})");
            }
            catch (MySqlConnector.MySqlException ex) when (ex.Number is 1061 or 1062) { /* exists */ }
            catch (Exception ex) { log.LogWarning(ex, "[CvSchema] Index {Name} failed", name); }
        }
    }

    private async Task TryCreate(string table, string ddl)
    {
        try { await db.ExecuteAsync(ddl); }
        catch (Exception ex) { log.LogWarning(ex, "[CvSchema] Create {Table} failed", table); }
    }
}
