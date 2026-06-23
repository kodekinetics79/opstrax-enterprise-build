using Microsoft.Extensions.Logging;
using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

// P6 Mobile Driver Workflow — schema bootstrap.
// Adds:
//   drivers.user_id             — links a driver record to an auth user session
//   coaching_tasks.acknowledged_note — driver acknowledgement note field
//   driver_offline_queue        — idempotency-keyed pending action queue for offline drafts
public sealed class DriverSchemaService(Database db, ILogger<DriverSchemaService> log)
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
            // Ties a driver record to an auth user account (user_id from users table).
            // Allows server-side derivation of driver identity from session without payload trust.
            ("drivers",        "user_id",             "BIGINT NULL"),
            // Driver can leave a note when acknowledging a coaching task.
            ("coaching_tasks", "acknowledged_note",   "TEXT NULL"),
        };

        foreach (var (table, col, def) in cols)
        {
            try
            {
                await db.ExecuteAsync($"ALTER TABLE \"{table}\" ADD COLUMN \"{col}\" {def}");
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "42701") { /* column exists */ }
            catch (Exception ex) { log.LogWarning(ex, "[DriverSchema] ALTER {Table}.{Col} failed", table, col); }
        }
    }

    private async Task CreateTables()
    {
        // Idempotency-keyed offline action queue.
        // Stores DVIR drafts, proof drafts, exception drafts, and note drafts when offline.
        // Actions that mutate the dispatch state machine (accept, major status change, final delivery)
        // are NOT queued here — they require live backend validation.
        await TryCreate("driver_offline_queue", @"
CREATE TABLE IF NOT EXISTS driver_offline_queue (
    id                  BIGINT NOT NULL GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id          BIGINT NOT NULL,
    driver_id           BIGINT NOT NULL,
    idempotency_key     VARCHAR(64) NOT NULL,
    action_type         VARCHAR(60) NOT NULL,
    payload_json        JSONB NOT NULL,
    status              VARCHAR(20) NOT NULL DEFAULT 'pending',
    processed_at        TIMESTAMPTZ NULL,
    error_message       TEXT NULL,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (idempotency_key)
)");
    }

    private async Task CreateIndexes()
    {
        var indexes = new[]
        {
            ("drivers",             "idx_drivers_user_id",     "user_id"),
            ("driver_offline_queue","idx_dq_driver_status",    "driver_id, status"),
            ("driver_offline_queue","idx_dq_company",          "company_id"),
        };

        foreach (var (table, name, cols) in indexes)
        {
            try
            {
                await db.ExecuteAsync($"CREATE INDEX IF NOT EXISTS \"{name}\" ON \"{table}\" ({cols})");
            }
            catch (Exception ex) { log.LogWarning(ex, "[DriverSchema] Index {Name} failed", name); }
        }
    }

    private async Task TryCreate(string table, string ddl)
    {
        try { await db.ExecuteAsync(ddl); }
        catch (Exception ex) { log.LogWarning(ex, "[DriverSchema] Create {Table} failed", table); }
    }
}
