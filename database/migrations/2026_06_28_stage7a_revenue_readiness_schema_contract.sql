-- Stage 7A Revenue Readiness Schema Contract
-- Review-only contract for the revenue readiness slice.
-- Additive, tenant-scoped, and safe for local review.

CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS invoice_drafts (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    company_id BIGINT NOT NULL,
    customer_id BIGINT NOT NULL,
    contract_id BIGINT NULL,
    job_id BIGINT NULL,
    invoice_draft_no VARCHAR(80) NOT NULL,
    status VARCHAR(40) NOT NULL DEFAULT 'draft',
    currency VARCHAR(10) NOT NULL DEFAULT 'USD',
    subtotal NUMERIC(18,2) NOT NULL DEFAULT 0,
    tax_total NUMERIC(18,2) NOT NULL DEFAULT 0,
    total NUMERIC(18,2) NOT NULL DEFAULT 0,
    source VARCHAR(40) NOT NULL DEFAULT 'system',
    metadata_json JSONB NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NULL,
    created_by UUID NULL,
    updated_by UUID NULL,
    CONSTRAINT fk_invoice_drafts_customer FOREIGN KEY (customer_id) REFERENCES customers(id),
    CONSTRAINT fk_invoice_drafts_contract FOREIGN KEY (contract_id) REFERENCES contracts(id),
    CONSTRAINT fk_invoice_drafts_job FOREIGN KEY (job_id) REFERENCES jobs(id)
);

CREATE TABLE IF NOT EXISTS invoice_draft_lines (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    company_id BIGINT NOT NULL,
    invoice_draft_id UUID NOT NULL REFERENCES invoice_drafts(id) ON DELETE CASCADE,
    job_charge_id BIGINT NULL,
    line_no INT NOT NULL,
    description TEXT NOT NULL,
    charge_code VARCHAR(80) NULL,
    quantity NUMERIC(18,3) NOT NULL DEFAULT 1,
    unit VARCHAR(40) NULL,
    unit_rate NUMERIC(18,4) NOT NULL DEFAULT 0,
    amount NUMERIC(18,2) NOT NULL DEFAULT 0,
    metadata_json JSONB NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NULL,
    CONSTRAINT fk_invoice_draft_lines_job_charge FOREIGN KEY (job_charge_id) REFERENCES job_charges(id)
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_invoice_drafts_company_invoice_no
    ON invoice_drafts (company_id, invoice_draft_no);

CREATE INDEX IF NOT EXISTS idx_invoice_drafts_company_status
    ON invoice_drafts (company_id, status, created_at DESC);

CREATE INDEX IF NOT EXISTS idx_invoice_drafts_company_job
    ON invoice_drafts (company_id, job_id, created_at DESC);

CREATE INDEX IF NOT EXISTS idx_invoice_draft_lines_company_draft
    ON invoice_draft_lines (company_id, invoice_draft_id, line_no);

CREATE INDEX IF NOT EXISTS idx_invoice_draft_lines_company_job_charge
    ON invoice_draft_lines (company_id, job_charge_id);

CREATE INDEX IF NOT EXISTS idx_invoice_draft_lines_company_charge
    ON invoice_draft_lines (company_id, charge_code);
