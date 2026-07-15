using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

// Billing profiles + consolidation (ADR-008 Billing layer) — tenant-configurable AR billing on top of
// the existing revenue chain. billing_profiles (effective-dated, append-only, most-specific-wins) drive
// how a customer's delivered-job charges consolidate into invoice_drafts; billing_consolidation_runs is
// the idempotency anchor (one run per group, invoice number pinned on the run).
//
// Additive by construction: a tenant with no billing_profiles behaves exactly like today (a virtual
// LegacyDefault). job_charges.billing_status prevents the same charge being billed twice across the
// legacy per-job path and the consolidation path. All tables are company_id-scoped (RLS auto-enrolled).
public sealed class BillingProfileSchemaService(Database db)
{
    public async Task EnsureAsync(CancellationToken ct = default)
    {
        foreach (var sql in Tables)
            await db.ExecuteAsync(sql, ct: ct);

        foreach (var sql in Alters.Concat(Indexes))
        {
            try { await db.ExecuteAsync(sql, ct: ct); }
            catch { /* additive: tolerate a pre-existing shape on local startup */ }
        }
    }

    private static readonly string[] Tables =
    [
        @"CREATE TABLE IF NOT EXISTS billing_profiles (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            profile_code VARCHAR(80) NOT NULL,
            profile_name VARCHAR(220) NOT NULL,
            scope_type VARCHAR(20) NOT NULL DEFAULT 'tenant',
            scope_id BIGINT NULL,
            cycle VARCHAR(20) NOT NULL DEFAULT 'immediate',
            consolidation VARCHAR(20) NOT NULL DEFAULT 'per_load',
            numbering_scheme VARCHAR(40) NOT NULL DEFAULT 'legacy_job',
            number_prefix VARCHAR(40) NULL,
            payment_terms_days INT NOT NULL DEFAULT 30,
            currency VARCHAR(10) NOT NULL DEFAULT 'USD',
            require_vat BOOLEAN NOT NULL DEFAULT FALSE,
            memo_default TEXT NULL,
            dunning_offsets_json JSONB NULL,
            version INT NOT NULL DEFAULT 1,
            status VARCHAR(40) NOT NULL DEFAULT 'active',
            effective_date DATE NOT NULL,
            expiry_date DATE NULL,
            config_set_id BIGINT NULL,
            correlation_id VARCHAR(120) NULL,
            causation_id VARCHAR(120) NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NULL
        )",
        @"CREATE TABLE IF NOT EXISTS billing_consolidation_runs (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            billing_profile_id BIGINT NOT NULL,
            billing_profile_version INT NOT NULL,
            customer_id BIGINT NOT NULL,
            group_key VARCHAR(200) NOT NULL,
            period_start DATE NULL,
            period_end DATE NULL,
            invoice_draft_id UUID NULL,
            allocated_invoice_no VARCHAR(80) NULL,
            resolved_config_json JSONB NOT NULL DEFAULT '{}'::jsonb,
            status VARCHAR(40) NOT NULL DEFAULT 'draft',
            charge_count INT NOT NULL DEFAULT 0,
            subtotal DECIMAL(18,2) NOT NULL DEFAULT 0,
            currency VARCHAR(10) NOT NULL DEFAULT 'USD',
            source VARCHAR(40) NOT NULL DEFAULT 'system',
            correlation_id VARCHAR(120) NULL,
            causation_id VARCHAR(120) NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NULL
        )"
    ];

    // Additive pins on the existing revenue artifacts. document_type defaults 'invoice' so legacy rows
    // are byte-identical; payment_terms_days NULL => the legacy 30-day default.
    private static readonly string[] Alters =
    [
        "ALTER TABLE invoice_drafts ADD COLUMN IF NOT EXISTS billing_profile_id BIGINT NULL",
        "ALTER TABLE invoice_drafts ADD COLUMN IF NOT EXISTS payment_terms_days INT NULL",
        "ALTER TABLE invoice_drafts ADD COLUMN IF NOT EXISTS document_type VARCHAR(20) NOT NULL DEFAULT 'invoice'",
        "ALTER TABLE invoice_drafts ADD COLUMN IF NOT EXISTS adjusts_invoice_id UUID NULL",
        "ALTER TABLE issued_invoices ADD COLUMN IF NOT EXISTS document_type VARCHAR(20) NOT NULL DEFAULT 'invoice'",
        "ALTER TABLE issued_invoices ADD COLUMN IF NOT EXISTS adjusts_invoice_id UUID NULL",
        "ALTER TABLE issued_invoices ADD COLUMN IF NOT EXISTS payment_terms_days INT NULL",
        "ALTER TABLE job_charges ADD COLUMN IF NOT EXISTS billing_status VARCHAR(20) NOT NULL DEFAULT 'unbilled'"
    ];

    private static readonly string[] Indexes =
    [
        "CREATE UNIQUE INDEX IF NOT EXISTS uq_billing_profiles_code_ver ON billing_profiles (company_id, profile_code, effective_date, version)",
        "CREATE INDEX IF NOT EXISTS idx_billing_profiles_lookup ON billing_profiles (company_id, scope_type, scope_id, status, effective_date DESC)",
        // Concurrent double-consolidate of the same group throws instead of duplicating.
        "CREATE UNIQUE INDEX IF NOT EXISTS uq_billing_runs_group ON billing_consolidation_runs (company_id, billing_profile_id, customer_id, group_key) WHERE source='system'",
        "CREATE INDEX IF NOT EXISTS idx_billing_runs_customer ON billing_consolidation_runs (company_id, customer_id, status, created_at DESC)",
        "CREATE INDEX IF NOT EXISTS idx_job_charges_billing_status ON job_charges (company_id, billing_status, job_id)"
    ];
}
