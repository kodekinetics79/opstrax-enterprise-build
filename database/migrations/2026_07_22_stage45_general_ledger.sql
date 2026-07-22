-- Stage 45 — General Ledger foundation (double-entry book of record).
-- Turns the standalone AR/AP/tax/rev-rec sub-ledgers into a real GL. Additive, idempotent. company_id
-- tables are RLS-enrolled by the reconciliation step. First writer: AR-invoice posting (GeneralLedgerService).
CREATE TABLE IF NOT EXISTS chart_of_accounts (
    id              BIGINT       GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id      BIGINT       NOT NULL,
    account_code    VARCHAR(20)  NOT NULL,
    account_name    VARCHAR(120) NOT NULL,
    account_type    VARCHAR(20)  NOT NULL,
    normal_balance  VARCHAR(6)   NOT NULL,
    created_at      TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    UNIQUE (company_id, account_code)
);

CREATE TABLE IF NOT EXISTS journal_entries (
    id           BIGINT       GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id   BIGINT       NOT NULL,
    entry_date   DATE         NOT NULL,
    source_type  VARCHAR(40)  NOT NULL,
    source_ref   VARCHAR(80)  NOT NULL,
    memo         VARCHAR(240) NULL,
    posted_at    TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    UNIQUE (company_id, source_type, source_ref)
);

CREATE TABLE IF NOT EXISTS journal_lines (
    id                BIGINT        GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id        BIGINT        NOT NULL,
    journal_entry_id  BIGINT        NOT NULL REFERENCES journal_entries(id) ON DELETE CASCADE,
    account_code      VARCHAR(20)   NOT NULL,
    debit             NUMERIC(18,2) NOT NULL DEFAULT 0,
    credit            NUMERIC(18,2) NOT NULL DEFAULT 0
);

CREATE INDEX IF NOT EXISTS ix_journal_lines_entry ON journal_lines (journal_entry_id);
CREATE INDEX IF NOT EXISTS ix_journal_lines_account ON journal_lines (company_id, account_code);
