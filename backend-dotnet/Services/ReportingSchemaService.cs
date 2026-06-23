using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

// P8 Reporting + Analytics — schema additions.
// Adds:
//   saved_reports               — saved report builder definitions with per-row visibility control
//   report_execution_log        — immutable audit trail of every report run and export
//   scheduled_report_deliveries — per-delivery records for scheduled report runs
public sealed class ReportingSchemaService(Database db)
{
    public async Task EnsureAsync(CancellationToken ct = default)
    {
        foreach (var sql in Tables)
        {
            try { await db.ExecuteAsync(sql, ct: ct); }
            catch { /* already exists */ }
        }
        foreach (var sql in Indexes)
        {
            try { await db.ExecuteAsync(sql, ct: ct); }
            catch { /* already exists */ }
        }
        // Backfill missing columns on scheduled_reports (Batch7 table that P8 extends)
        var extCols = new[]
        {
            ("scheduled_reports", "saved_report_id",    "BIGINT NULL"),
            ("scheduled_reports", "owner_user_id",      "BIGINT NULL"),
            ("scheduled_reports", "format",             "VARCHAR(20) NOT NULL DEFAULT 'csv'"),
            ("scheduled_reports", "last_status",        "VARCHAR(40) NULL"),
            ("scheduled_reports", "last_error",         "TEXT NULL"),
            ("scheduled_reports", "recipient_type",     "VARCHAR(40) NOT NULL DEFAULT 'users'"),
        };
        foreach (var (table, col, def) in extCols)
        {
            try
            {
                await db.ExecuteAsync($"ALTER TABLE \"{table}\" ADD COLUMN \"{col}\" {def}", ct: ct);
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "42701") { /* column exists */ }
            catch { /* best effort */ }
        }
    }

    private static readonly string[] Tables =
    [
        // Saved report builder definitions.
        // visibility values:
        //   'private'      — visible only to owner_user_id
        //   'role_shared'  — visible to all users in company with role = shared_role
        //   'tenant_shared'— visible to all users in company with reports:view permission
        @"CREATE TABLE IF NOT EXISTS saved_reports (
            id                   BIGINT       NOT NULL GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id           BIGINT       NOT NULL,
            owner_user_id        BIGINT       NOT NULL,
            name                 VARCHAR(220) NOT NULL,
            description          TEXT         NULL,
            dataset_key          VARCHAR(100) NOT NULL,
            selected_fields_json JSONB        NOT NULL,
            filters_json         JSONB        NULL,
            sort_json            JSONB        NULL,
            group_by_json        JSONB        NULL,
            visibility           VARCHAR(40)  NOT NULL DEFAULT 'private',
            shared_role          VARCHAR(80)  NULL,
            last_run_at          TIMESTAMPTZ  NULL,
            deleted_at           TIMESTAMPTZ  NULL,
            created_at           TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
            updated_at           TIMESTAMPTZ  NULL
        )",

        // Immutable audit trail of every report execution and export.
        // Never updated after insert — append-only for compliance.
        @"CREATE TABLE IF NOT EXISTS report_execution_log (
            id               BIGINT       NOT NULL GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id       BIGINT       NOT NULL,
            user_id          BIGINT       NULL,
            dataset_key      VARCHAR(100) NOT NULL,
            saved_report_id  BIGINT       NULL,
            row_count        INT          NULL,
            execution_ms     INT          NULL,
            export_format    VARCHAR(20)  NULL,
            filters_json     JSONB        NULL,
            status           VARCHAR(40)  NOT NULL DEFAULT 'completed',
            error_message    TEXT         NULL,
            executed_at      TIMESTAMPTZ  NOT NULL DEFAULT NOW()
        )",

        // Per-delivery record for each scheduled report run.
        // delivery_method is 'in_app' unless an email provider is configured.
        @"CREATE TABLE IF NOT EXISTS scheduled_report_deliveries (
            id                   BIGINT      NOT NULL GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            scheduled_report_id  BIGINT      NOT NULL,
            company_id           BIGINT      NOT NULL,
            execution_log_id     BIGINT      NULL,
            recipient_count      INT         NOT NULL DEFAULT 0,
            delivery_method      VARCHAR(40) NOT NULL DEFAULT 'in_app',
            status               VARCHAR(40) NOT NULL DEFAULT 'pending',
            error_message        TEXT        NULL,
            scheduled_for        TIMESTAMPTZ NULL,
            delivered_at         TIMESTAMPTZ NULL,
            created_at           TIMESTAMPTZ NOT NULL DEFAULT NOW()
        )",
    ];

    private static readonly string[] Indexes =
    [
        "CREATE INDEX IF NOT EXISTS idx_sr_company_owner ON saved_reports (company_id, owner_user_id)",
        "CREATE INDEX IF NOT EXISTS idx_sr_visibility ON saved_reports (company_id, visibility)",
        "CREATE INDEX IF NOT EXISTS idx_sr_dataset ON saved_reports (company_id, dataset_key)",
        "CREATE INDEX IF NOT EXISTS idx_rel_company ON report_execution_log (company_id)",
        "CREATE INDEX IF NOT EXISTS idx_rel_dataset ON report_execution_log (company_id, dataset_key)",
        "CREATE INDEX IF NOT EXISTS idx_srd_scheduled ON scheduled_report_deliveries (scheduled_report_id)",
    ];
}
