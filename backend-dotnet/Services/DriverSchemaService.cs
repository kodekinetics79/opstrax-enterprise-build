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
                await db.ExecuteAsync($"ALTER TABLE `{table}` ADD COLUMN `{col}` {def}");
            }
            catch (MySqlConnector.MySqlException ex) when (ex.Number == 1060) { /* column exists */ }
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
    id                  BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    company_id          BIGINT NOT NULL,
    driver_id           BIGINT NOT NULL,
    idempotency_key     VARCHAR(64) NOT NULL,
    action_type         VARCHAR(60) NOT NULL,
    payload_json        JSON NOT NULL,
    status              VARCHAR(20) NOT NULL DEFAULT 'pending',
    processed_at        DATETIME NULL,
    error_message       TEXT NULL,
    created_at          TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE KEY uq_dq_idempotency (idempotency_key)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
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
                await db.ExecuteAsync($"ALTER TABLE `{table}` ADD INDEX `{name}` ({cols})");
            }
            catch (MySqlConnector.MySqlException ex) when (ex.Number is 1061 or 1062) { /* exists */ }
            catch (Exception ex) { log.LogWarning(ex, "[DriverSchema] Index {Name} failed", name); }
        }
    }

    private async Task TryCreate(string table, string ddl)
    {
        try { await db.ExecuteAsync(ddl); }
        catch (Exception ex) { log.LogWarning(ex, "[DriverSchema] Create {Table} failed", table); }
    }
}
