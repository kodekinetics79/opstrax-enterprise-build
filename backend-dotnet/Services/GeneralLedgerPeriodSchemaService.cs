using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

// GL period-close + export schema (blueprint period-close-erp-export). Two tenant tables (company_id
// BIGINT => auto RLS-enrolled by the boot-final reconciler) plus the HARD back-posting lock: a
// BEFORE INSERT trigger on journal_entries that rejects any entry dated inside a closed period —
// blocking every writer (AR/AP handlers, backfills, future posters), not just the service layer.
public sealed class GeneralLedgerPeriodSchemaService(Database db)
{
    public async Task EnsureAsync()
    {
        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS gl_periods (
                id                     BIGINT        GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                company_id             BIGINT        NOT NULL,
                period_code            VARCHAR(20)   NOT NULL,          -- 'YYYY-MM'
                period_start           DATE          NOT NULL,
                period_end             DATE          NOT NULL,
                status                 VARCHAR(20)   NOT NULL DEFAULT 'open',  -- open | pending_close | closed
                requested_by_user_id   BIGINT        NULL,
                requested_at           TIMESTAMPTZ   NULL,
                closed_by_user_id      BIGINT        NULL,
                closed_at              TIMESTAMPTZ   NULL,
                close_checksum         VARCHAR(80)   NULL,
                total_debits           NUMERIC(18,2) NOT NULL DEFAULT 0,
                total_credits          NUMERIC(18,2) NOT NULL DEFAULT 0,
                entry_count            INT           NOT NULL DEFAULT 0,
                created_at             TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
                updated_at             TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
                UNIQUE (company_id, period_code)
            )
            """);

        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS gl_export_runs (
                id                   BIGINT        GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                company_id           BIGINT        NOT NULL,
                period_code          VARCHAR(20)   NOT NULL,
                format               VARCHAR(20)   NOT NULL,            -- csv | quickbooks | netsuite
                row_count            INT           NOT NULL DEFAULT 0,
                total_debits         NUMERIC(18,2) NOT NULL DEFAULT 0,
                total_credits        NUMERIC(18,2) NOT NULL DEFAULT 0,
                checksum             VARCHAR(80)   NULL,
                file_name            TEXT          NULL,
                exported_by_user_id  BIGINT        NULL,
                exported_at          TIMESTAMPTZ   NOT NULL DEFAULT NOW()
            )
            """);

        await db.ExecuteAsync("CREATE INDEX IF NOT EXISTS ix_gl_periods_lookup ON gl_periods (company_id, status, period_end)");
        await db.ExecuteAsync("CREATE INDEX IF NOT EXISTS ix_gl_export_runs ON gl_export_runs (company_id, period_code)");

        // The hard lock. The explicit company_id=NEW.company_id predicate keeps the check correct even
        // when the writer runs under the platform-admin RLS bypass (the outbox dispatcher does).
        await db.ExecuteAsync("""
            CREATE OR REPLACE FUNCTION gl_enforce_period_lock() RETURNS trigger AS $$
            BEGIN
                IF EXISTS (SELECT 1 FROM gl_periods p
                           WHERE p.company_id = NEW.company_id AND p.status = 'closed'
                             AND NEW.entry_date BETWEEN p.period_start AND p.period_end) THEN
                    RAISE EXCEPTION 'gl_period_closed: entry_date % is in a locked period for company %',
                        NEW.entry_date, NEW.company_id USING ERRCODE = 'P0001';
                END IF;
                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql
            """);
        // CREATE TRIGGER has no IF NOT EXISTS — drop-then-create for idempotent boot.
        await db.ExecuteAsync("DROP TRIGGER IF EXISTS trg_gl_period_lock ON journal_entries");
        await db.ExecuteAsync("CREATE TRIGGER trg_gl_period_lock BEFORE INSERT ON journal_entries FOR EACH ROW EXECUTE FUNCTION gl_enforce_period_lock()");
    }
}
