using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

// Financial config envelope (ADR-008 P1 meta-layer) — the versioned, effective-dated, per-tenant config
// substrate every financial module will resolve from. Ships as scaffolding: no pricing/billing/pay/rev-
// rec path reads it yet, so it changes zero numbers today. It lands the envelope header + typed-JSON
// child documents + an append-only, hash-chained change log + nullable config_set_id pin columns on the
// money artifacts (so a closed period can reproduce its numbers regardless of later config edits).
//
// All tables are company_id-scoped (RLS auto-enrolled). Published config sets + their documents are
// append-only (fin_config_documents FK is ON DELETE RESTRICT — config sets are never deleted).
public sealed class FinancialConfigSchemaService(Database db)
{
    public async Task EnsureAsync(CancellationToken ct = default)
    {
        foreach (var sql in Tables)
            await db.ExecuteAsync(sql, ct: ct);
        foreach (var sql in Alters.Concat(Indexes))
        {
            try { await db.ExecuteAsync(sql, ct: ct); }
            catch { /* additive */ }
        }
    }

    private static readonly string[] Tables =
    [
        @"CREATE TABLE IF NOT EXISTS fin_config_sets (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            config_set_no VARCHAR(80) NOT NULL,
            version_no INT NOT NULL DEFAULT 1,
            archetype VARCHAR(40) NOT NULL DEFAULT 'custom',
            title VARCHAR(220) NOT NULL,
            status VARCHAR(40) NOT NULL DEFAULT 'draft',
            source VARCHAR(20) NOT NULL DEFAULT 'user',
            effective_from DATE NULL,
            effective_to DATE NULL,
            content_hash VARCHAR(64) NULL,
            template_key VARCHAR(80) NULL,
            based_on_config_set_id BIGINT NULL,
            author_user_id BIGINT NULL,
            published_by_user_id BIGINT NULL,
            published_at TIMESTAMPTZ NULL,
            correlation_id VARCHAR(120) NULL,
            causation_id VARCHAR(120) NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NULL
        )",
        @"CREATE TABLE IF NOT EXISTS fin_config_documents (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            config_set_id BIGINT NOT NULL REFERENCES fin_config_sets(id) ON DELETE RESTRICT,
            doc_type VARCHAR(60) NOT NULL,
            doc_key VARCHAR(120) NOT NULL,
            content_json JSONB NOT NULL,
            content_hash VARCHAR(64) NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        )",
        @"CREATE TABLE IF NOT EXISTS fin_config_change_log (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            config_set_id BIGINT NOT NULL,
            action VARCHAR(40) NOT NULL,
            from_status VARCHAR(40) NULL,
            to_status VARCHAR(40) NULL,
            actor_user_id BIGINT NULL,
            approval_request_id BIGINT NULL,
            diff_json JSONB NULL,
            reason TEXT NULL,
            content_hash VARCHAR(64) NULL,
            prev_hash VARCHAR(64) NULL,
            row_hash VARCHAR(64) NULL,
            correlation_id VARCHAR(120) NULL,
            causation_id VARCHAR(120) NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        )"
    ];

    // Nullable config_set_id pins on the money artifacts (no FK — matches job_charges.rate_card_id, keeps
    // the migration order-independent). A closed period's rows carry the config version they were computed
    // under. Additive: NULL today. (revenue_recognition_entries is owned by its own P0 migration.)
    private static readonly string[] Alters =
    [
        "ALTER TABLE job_charges ADD COLUMN IF NOT EXISTS config_set_id BIGINT NULL",
        "ALTER TABLE invoice_drafts ADD COLUMN IF NOT EXISTS config_set_id BIGINT NULL",
        "ALTER TABLE issued_invoices ADD COLUMN IF NOT EXISTS config_set_id BIGINT NULL",
        "ALTER TABLE settlement_statements ADD COLUMN IF NOT EXISTS config_set_id BIGINT NULL",
        "ALTER TABLE settlement_lines ADD COLUMN IF NOT EXISTS config_set_id BIGINT NULL",
        "ALTER TABLE revenue_recognition_entries ADD COLUMN IF NOT EXISTS config_set_id BIGINT NULL"
    ];

    private static readonly string[] Indexes =
    [
        "CREATE UNIQUE INDEX IF NOT EXISTS uq_fin_config_sets_no_ver ON fin_config_sets (company_id, config_set_no, version_no)",
        "CREATE INDEX IF NOT EXISTS idx_fin_config_sets_lookup ON fin_config_sets (company_id, status, effective_from DESC)",
        // At most one published set per config_set_no effective at a time is a service-level invariant;
        // this index accelerates the resolver.
        "CREATE INDEX IF NOT EXISTS idx_fin_config_sets_published ON fin_config_sets (company_id, config_set_no, status, effective_from DESC)",
        "CREATE UNIQUE INDEX IF NOT EXISTS uq_fin_config_docs ON fin_config_documents (company_id, config_set_id, doc_type, doc_key)",
        "CREATE INDEX IF NOT EXISTS idx_fin_config_docs_type ON fin_config_documents (company_id, config_set_id, doc_type)",
        "CREATE INDEX IF NOT EXISTS idx_fin_config_change_log ON fin_config_change_log (company_id, config_set_id, created_at)",
        "CREATE INDEX IF NOT EXISTS idx_job_charges_config_set ON job_charges (company_id, config_set_id)",
        "CREATE INDEX IF NOT EXISTS idx_invoice_drafts_config_set ON invoice_drafts (company_id, config_set_id)",
        "CREATE INDEX IF NOT EXISTS idx_issued_invoices_config_set ON issued_invoices (company_id, config_set_id)"
    ];
}
