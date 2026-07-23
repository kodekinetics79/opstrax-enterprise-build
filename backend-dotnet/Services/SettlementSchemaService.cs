using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

// Settlement / carrier-&-driver-pay (AP) — ADR-007 §C. The AP mirror of the AR chain
// (invoice_drafts → _lines → issued_invoices → payments):
//   pay_agreements (AP analogue of rate_cards)
//   settlement_statements → settlement_lines → settlement_payments
//
// All tables are tenant-scoped (company_id BIGINT NOT NULL) so the boot-final
// RlsReconciliationSchemaService auto-enrolls them into RLS (tenant_isolation +
// platform_admin_bypass + FORCE + grant to opstrax_app) — no hand-written policy here.
//
// Phase 1 = driver flat/per-mile statements. `basis='percent'` and carrier payees are
// modelled but not computed yet (SettlementService fails closed on them).
public sealed class SettlementSchemaService(Database db)
{
    public async Task EnsureAsync(CancellationToken ct = default)
    {
        foreach (var sql in Tables)
            await db.ExecuteAsync(sql, ct: ct);

        foreach (var sql in Indexes)
        {
            try { await db.ExecuteAsync(sql, ct: ct); }
            catch { /* additive: tolerate a pre-existing shape on local startup */ }
        }
    }

    private static readonly string[] Tables =
    [
        // Detention -> driver pay policy (the differentiator: we collect detention AND pay drivers).
        // One active policy per tenant. Fail-closed: no enabled policy => drivers are paid no detention.
        @"CREATE TABLE IF NOT EXISTS driver_detention_pay_policy (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            enabled BOOLEAN NOT NULL DEFAULT FALSE,
            trigger_state VARCHAR(20) NOT NULL DEFAULT 'collected',  -- billed | collected
            share_type VARCHAR(20) NOT NULL DEFAULT 'percent',       -- percent | flat_per_hour
            share_value DECIMAL(12,2) NOT NULL DEFAULT 0,
            currency VARCHAR(10) NOT NULL DEFAULT 'USD',
            updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            UNIQUE (company_id)
        )",
        // AP analogue of rate_cards: how a payee (driver/carrier) is paid for a load.
        @"CREATE TABLE IF NOT EXISTS pay_agreements (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            agreement_code VARCHAR(80) NOT NULL,
            agreement_name VARCHAR(220) NOT NULL,
            payee_type VARCHAR(20) NOT NULL DEFAULT 'driver',
            payee_id BIGINT NULL,
            basis VARCHAR(20) NOT NULL DEFAULT 'per_mile',
            rate DECIMAL(12,4) NOT NULL DEFAULT 0,
            min_pay DECIMAL(12,2) NULL,
            currency VARCHAR(10) NOT NULL DEFAULT 'USD',
            effective_date DATE NOT NULL,
            expiry_date DATE NULL,
            status VARCHAR(40) NOT NULL DEFAULT 'active',
            notes TEXT NULL,
            correlation_id VARCHAR(120) NULL,
            causation_id VARCHAR(120) NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NULL
        )",
        // AP analogue of invoice_drafts + issued_invoices: one payee's pay for a period.
        @"CREATE TABLE IF NOT EXISTS settlement_statements (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            statement_no VARCHAR(80) NOT NULL,
            payee_type VARCHAR(20) NOT NULL DEFAULT 'driver',
            payee_id BIGINT NOT NULL,
            period_start DATE NOT NULL,
            period_end DATE NOT NULL,
            status VARCHAR(40) NOT NULL DEFAULT 'draft',
            currency VARCHAR(10) NOT NULL DEFAULT 'USD',
            subtotal DECIMAL(18,2) NOT NULL DEFAULT 0,
            total DECIMAL(18,2) NOT NULL DEFAULT 0,
            amount_paid DECIMAL(18,2) NOT NULL DEFAULT 0,
            source VARCHAR(40) NOT NULL DEFAULT 'system',
            pay_agreement_id BIGINT NULL,
            approved_by_user_id BIGINT NULL,
            approved_at TIMESTAMPTZ NULL,
            correlation_id VARCHAR(120) NULL,
            causation_id VARCHAR(120) NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NULL
        )",
        // AP analogue of invoice_draft_lines: per-load pay, with the basis_amount kept for audit.
        @"CREATE TABLE IF NOT EXISTS settlement_lines (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            statement_id BIGINT NOT NULL REFERENCES settlement_statements(id) ON DELETE CASCADE,
            job_id BIGINT NULL,
            line_no INT NOT NULL,
            pay_code VARCHAR(80) NOT NULL DEFAULT 'linehaul',
            description TEXT NOT NULL,
            basis VARCHAR(20) NOT NULL,
            basis_amount DECIMAL(18,4) NOT NULL DEFAULT 0,
            quantity DECIMAL(18,3) NOT NULL DEFAULT 1,
            unit_rate DECIMAL(18,4) NOT NULL DEFAULT 0,
            amount DECIMAL(18,2) NOT NULL DEFAULT 0,
            pay_agreement_id BIGINT NULL,
            source VARCHAR(20) NOT NULL DEFAULT 'settlement',
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NULL
        )",
        // AP analogue of payments: money actually paid against a statement.
        @"CREATE TABLE IF NOT EXISTS settlement_payments (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            statement_id BIGINT NOT NULL REFERENCES settlement_statements(id) ON DELETE CASCADE,
            amount DECIMAL(18,2) NOT NULL,
            currency VARCHAR(10) NOT NULL DEFAULT 'USD',
            method VARCHAR(40) NULL,
            reference VARCHAR(120) NULL,
            idempotency_key VARCHAR(160) NULL,
            paid_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            created_by_user_id BIGINT NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        )"
    ];

    private static readonly string[] Indexes =
    [
        "CREATE UNIQUE INDEX IF NOT EXISTS uq_pay_agreements_company_code ON pay_agreements (company_id, agreement_code)",
        "CREATE INDEX IF NOT EXISTS idx_pay_agreements_lookup ON pay_agreements (company_id, payee_type, payee_id, status, effective_date DESC)",

        "CREATE UNIQUE INDEX IF NOT EXISTS uq_settlement_statements_company_no ON settlement_statements (company_id, statement_no)",
        "CREATE INDEX IF NOT EXISTS idx_settlement_statements_payee ON settlement_statements (company_id, payee_type, payee_id, status, created_at DESC)",
        // Idempotent regenerate: one system statement per (payee, period). A concurrent double
        // generate throws instead of duplicating; delete-and-recompute keys off this.
        "CREATE UNIQUE INDEX IF NOT EXISTS uq_settlement_statements_period ON settlement_statements (company_id, payee_type, payee_id, period_start, period_end) WHERE source = 'system'",

        "CREATE INDEX IF NOT EXISTS idx_settlement_lines_company_statement ON settlement_lines (company_id, statement_id, line_no)",
        // One settlement line per (statement, job): recompute is a no-op on unchanged inputs and a
        // concurrent double-add throws instead of paying a load twice.
        "CREATE UNIQUE INDEX IF NOT EXISTS uq_settlement_lines_job ON settlement_lines (company_id, statement_id, job_id) WHERE job_id IS NOT NULL",

        "CREATE INDEX IF NOT EXISTS idx_settlement_payments_company_statement ON settlement_payments (company_id, statement_id, paid_at DESC)",
        "CREATE UNIQUE INDEX IF NOT EXISTS uq_settlement_payments_idem ON settlement_payments (company_id, idempotency_key) WHERE idempotency_key IS NOT NULL"
    ];
}
