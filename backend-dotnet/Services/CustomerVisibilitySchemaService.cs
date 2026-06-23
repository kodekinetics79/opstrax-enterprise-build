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
    id                      BIGINT NOT NULL GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id              BIGINT NOT NULL,
    customer_id             BIGINT NULL,
    shipment_id             BIGINT NULL,
    dispatch_assignment_id  BIGINT NULL,
    trip_id                 BIGINT NULL,
    public_tracking_token   VARCHAR(64) NOT NULL,
    visibility_status       VARCHAR(30) NOT NULL DEFAULT 'active',
    share_enabled           BOOLEAN NOT NULL DEFAULT true,
    expires_at              TIMESTAMPTZ NOT NULL,
    created_by              BIGINT NULL,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at              TIMESTAMPTZ NULL,
    UNIQUE (public_tracking_token)
)");

        // ETA snapshots — one row per assignment, updated by background or API compute.
        await TryCreate("customer_eta_snapshots", @"
CREATE TABLE IF NOT EXISTS customer_eta_snapshots (
    id                      BIGINT NOT NULL GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id              BIGINT NOT NULL,
    dispatch_assignment_id  BIGINT NOT NULL,
    eta_at                  TIMESTAMPTZ NULL,
    confidence              VARCHAR(10) NOT NULL DEFAULT 'unknown',
    risk                    VARCHAR(20) NOT NULL DEFAULT 'unknown',
    reason_codes            JSONB NULL,
    explanation             TEXT NULL,
    telemetry_stale         BOOLEAN NOT NULL DEFAULT false,
    last_position_at        TIMESTAMPTZ NULL,
    computed_at             TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (dispatch_assignment_id)
)");
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
                await db.ExecuteAsync($"CREATE INDEX IF NOT EXISTS \"{name}\" ON \"{table}\" ({cols})");
            }
            catch (Exception ex) { log.LogWarning(ex, "[CvSchema] Index {Name} failed", name); }
        }
    }

    private async Task TryCreate(string table, string ddl)
    {
        try { await db.ExecuteAsync(ddl); }
        catch (Exception ex) { log.LogWarning(ex, "[CvSchema] Create {Table} failed", table); }
    }
}
