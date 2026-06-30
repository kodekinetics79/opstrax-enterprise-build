using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

public sealed class FinanceActivationSchemaService(Database db)
{
    public async Task EnsureAsync(CancellationToken ct = default)
    {
        await db.ExecuteAsync("CREATE EXTENSION IF NOT EXISTS pgcrypto", ct: ct);

        foreach (var sql in Tables)
        {
            await db.ExecuteAsync(sql, ct: ct);
        }

        foreach (var sql in Indexes)
        {
            try
            {
                await db.ExecuteAsync(sql, ct: ct);
            }
            catch
            {
                // Local startup stays additive even if the database already has the shape.
            }
        }
    }

    private static readonly string[] Tables =
    [
        "ALTER TABLE invoice_drafts ADD COLUMN IF NOT EXISTS approval_request_id BIGINT NULL",
        @"CREATE TABLE IF NOT EXISTS issued_invoices (
            id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            company_id BIGINT NOT NULL,
            customer_id BIGINT NOT NULL,
            contract_id BIGINT NULL,
            job_id BIGINT NULL,
            approval_request_id BIGINT NULL,
            source_invoice_draft_id UUID NOT NULL,
            source_invoice_draft_no VARCHAR(80) NOT NULL,
            invoice_number VARCHAR(80) NOT NULL,
            status VARCHAR(40) NOT NULL DEFAULT 'issued',
            currency VARCHAR(10) NOT NULL DEFAULT 'USD',
            subtotal NUMERIC(18,2) NOT NULL DEFAULT 0,
            tax_total NUMERIC(18,2) NOT NULL DEFAULT 0,
            total NUMERIC(18,2) NOT NULL DEFAULT 0,
            amount_paid NUMERIC(18,2) NOT NULL DEFAULT 0,
            balance_due NUMERIC(18,2) NOT NULL DEFAULT 0,
            payment_status VARCHAR(40) NOT NULL DEFAULT 'unpaid',
            issued_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            due_at TIMESTAMPTZ NULL,
            paid_at TIMESTAMPTZ NULL,
            issued_by_actor_type VARCHAR(40) NULL,
            issued_by_actor_id VARCHAR(120) NULL,
            correlation_id VARCHAR(120) NULL,
            causation_id VARCHAR(120) NULL,
            idempotency_key VARCHAR(160) NULL,
            metadata_json JSONB NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NULL,
            CONSTRAINT fk_issued_invoices_draft FOREIGN KEY (source_invoice_draft_id) REFERENCES invoice_drafts(id),
            CONSTRAINT fk_issued_invoices_customer FOREIGN KEY (customer_id) REFERENCES customers(id),
            CONSTRAINT fk_issued_invoices_contract FOREIGN KEY (contract_id) REFERENCES contracts(id),
            CONSTRAINT fk_issued_invoices_job FOREIGN KEY (job_id) REFERENCES jobs(id),
            CONSTRAINT fk_issued_invoices_approval FOREIGN KEY (approval_request_id) REFERENCES approval_requests(id)
        )",
        @"CREATE TABLE IF NOT EXISTS issued_invoice_lines (
            id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            company_id BIGINT NOT NULL,
            issued_invoice_id UUID NOT NULL REFERENCES issued_invoices(id) ON DELETE CASCADE,
            source_invoice_draft_line_id UUID NULL REFERENCES invoice_draft_lines(id),
            job_charge_id BIGINT NULL REFERENCES job_charges(id),
            line_no INT NOT NULL,
            description TEXT NOT NULL,
            charge_code VARCHAR(80) NULL,
            quantity NUMERIC(18,3) NOT NULL DEFAULT 1,
            unit VARCHAR(40) NULL,
            unit_rate NUMERIC(18,4) NOT NULL DEFAULT 0,
            amount NUMERIC(18,2) NOT NULL DEFAULT 0,
            metadata_json JSONB NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NULL
        )",
        @"CREATE TABLE IF NOT EXISTS invoice_payments (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            issued_invoice_id UUID NOT NULL REFERENCES issued_invoices(id) ON DELETE CASCADE,
            payment_reference VARCHAR(120) NOT NULL,
            payment_method VARCHAR(40) NOT NULL DEFAULT 'manual',
            currency VARCHAR(10) NOT NULL DEFAULT 'USD',
            amount NUMERIC(18,2) NOT NULL,
            received_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            status VARCHAR(40) NOT NULL DEFAULT 'posted',
            metadata_json JSONB NULL,
            correlation_id VARCHAR(120) NULL,
            causation_id VARCHAR(120) NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        )"
    ];

    private static readonly string[] Indexes =
    [
        "CREATE UNIQUE INDEX IF NOT EXISTS uq_issued_invoices_company_invoice_no ON issued_invoices (company_id, invoice_number)",
        "CREATE UNIQUE INDEX IF NOT EXISTS uq_issued_invoices_company_source_draft ON issued_invoices (company_id, source_invoice_draft_id)",
        "CREATE INDEX IF NOT EXISTS idx_issued_invoices_company_status ON issued_invoices (company_id, status, issued_at DESC)",
        "CREATE INDEX IF NOT EXISTS idx_issued_invoices_company_customer ON issued_invoices (company_id, customer_id, issued_at DESC)",
        "CREATE INDEX IF NOT EXISTS idx_issued_invoices_company_due_at ON issued_invoices (company_id, due_at)",
        "CREATE INDEX IF NOT EXISTS idx_issued_invoice_lines_company_invoice ON issued_invoice_lines (company_id, issued_invoice_id, line_no)",
        "CREATE INDEX IF NOT EXISTS idx_invoice_payments_company_invoice ON invoice_payments (company_id, issued_invoice_id, received_at DESC)",
        "CREATE INDEX IF NOT EXISTS idx_invoice_payments_company_reference ON invoice_payments (company_id, payment_reference)"
    ];
}
