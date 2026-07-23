using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

// General Ledger foundation — turns the standalone AR/AP/tax/rev-rec sub-ledgers into a real double-entry
// book of record. Nothing here is destructive: CREATE TABLE/INDEX IF NOT EXISTS only. company_id tables are
// auto-enrolled in RLS by the boot-final reconciliation step, so tenant isolation is enforced.
public sealed class GeneralLedgerSchemaService(Database db)
{
    public async Task EnsureAsync()
    {
        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS chart_of_accounts (
                id              BIGINT       GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                company_id      BIGINT       NOT NULL,
                account_code    VARCHAR(20)  NOT NULL,
                account_name    VARCHAR(120) NOT NULL,
                account_type    VARCHAR(20)  NOT NULL,   -- asset | liability | equity | revenue | expense
                normal_balance  VARCHAR(6)   NOT NULL,   -- debit | credit
                created_at      TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
                UNIQUE (company_id, account_code)
            )
            """);

        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS journal_entries (
                id           BIGINT       GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                company_id   BIGINT       NOT NULL,
                entry_date   DATE         NOT NULL,
                source_type  VARCHAR(40)  NOT NULL,      -- invoice | settlement | tax | manual | ...
                source_ref   VARCHAR(80)  NOT NULL,      -- source id as text (uuid or bigint)
                memo         VARCHAR(240) NULL,
                posted_at    TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
                UNIQUE (company_id, source_type, source_ref)   -- one entry per source event (idempotency)
            )
            """);

        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS journal_lines (
                id                BIGINT        GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                company_id        BIGINT        NOT NULL,
                journal_entry_id  BIGINT        NOT NULL REFERENCES journal_entries(id) ON DELETE CASCADE,
                account_code      VARCHAR(20)   NOT NULL,
                debit             NUMERIC(18,2) NOT NULL DEFAULT 0,
                credit            NUMERIC(18,2) NOT NULL DEFAULT 0
            )
            """);

        await db.ExecuteAsync("CREATE INDEX IF NOT EXISTS ix_journal_lines_entry ON journal_lines (journal_entry_id)");
        await db.ExecuteAsync("CREATE INDEX IF NOT EXISTS ix_journal_lines_account ON journal_lines (company_id, account_code)");
    }
}
