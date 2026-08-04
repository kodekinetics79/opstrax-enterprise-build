-- Stage 41 — Financial config envelope (ADR-008 P1 meta-layer)
-- The versioned, effective-dated, per-tenant config substrate every financial module will resolve from.
-- Scaffolding: no money path reads it yet, so it changes zero numbers today. Lands the envelope header +
-- typed-JSON documents + an append-only, hash-chained change log + nullable config_set_id pin columns on
-- the money artifacts (so a closed period reproduces its numbers regardless of later config edits).
--
-- Owner migration for restricted-role prod. IF NOT EXISTS / idempotent. Published sets + documents are
-- append-only (fin_config_documents FK is ON DELETE RESTRICT). RLS-enrolls the new tables.

CREATE TABLE IF NOT EXISTS fin_config_sets (
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
);

CREATE TABLE IF NOT EXISTS fin_config_documents (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id BIGINT NOT NULL,
    config_set_id BIGINT NOT NULL REFERENCES fin_config_sets(id) ON DELETE RESTRICT,
    doc_type VARCHAR(60) NOT NULL,
    doc_key VARCHAR(120) NOT NULL,
    content_json JSONB NOT NULL,
    content_hash VARCHAR(64) NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS fin_config_change_log (
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
);

-- Nullable config_set_id pins on the money artifacts (no FK — order-independent, matches rate_card_id).
ALTER TABLE job_charges ADD COLUMN IF NOT EXISTS config_set_id BIGINT NULL;
ALTER TABLE invoice_drafts ADD COLUMN IF NOT EXISTS config_set_id BIGINT NULL;
ALTER TABLE issued_invoices ADD COLUMN IF NOT EXISTS config_set_id BIGINT NULL;
ALTER TABLE settlement_statements ADD COLUMN IF NOT EXISTS config_set_id BIGINT NULL;
ALTER TABLE settlement_lines ADD COLUMN IF NOT EXISTS config_set_id BIGINT NULL;
ALTER TABLE revenue_recognition_entries ADD COLUMN IF NOT EXISTS config_set_id BIGINT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS uq_fin_config_sets_no_ver ON fin_config_sets (company_id, config_set_no, version_no);
CREATE INDEX IF NOT EXISTS idx_fin_config_sets_lookup ON fin_config_sets (company_id, status, effective_from DESC);
CREATE INDEX IF NOT EXISTS idx_fin_config_sets_published ON fin_config_sets (company_id, config_set_no, status, effective_from DESC);
CREATE UNIQUE INDEX IF NOT EXISTS uq_fin_config_docs ON fin_config_documents (company_id, config_set_id, doc_type, doc_key);
CREATE INDEX IF NOT EXISTS idx_fin_config_docs_type ON fin_config_documents (company_id, config_set_id, doc_type);
CREATE INDEX IF NOT EXISTS idx_fin_config_change_log ON fin_config_change_log (company_id, config_set_id, created_at);
CREATE INDEX IF NOT EXISTS idx_job_charges_config_set ON job_charges (company_id, config_set_id);
CREATE INDEX IF NOT EXISTS idx_invoice_drafts_config_set ON invoice_drafts (company_id, config_set_id);
CREATE INDEX IF NOT EXISTS idx_issued_invoices_config_set ON issued_invoices (company_id, config_set_id);

DO $rls$
DECLARE
    t text;
    tbls text[] := ARRAY['fin_config_sets','fin_config_documents','fin_config_change_log'];
BEGIN
    FOREACH t IN ARRAY tbls LOOP
        EXECUTE format('ALTER TABLE public.%I ENABLE ROW LEVEL SECURITY', t);
        EXECUTE format('ALTER TABLE public.%I FORCE ROW LEVEL SECURITY', t);
        IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE schemaname='public' AND tablename=t AND policyname='tenant_isolation') THEN
            EXECUTE format($p$
                CREATE POLICY tenant_isolation ON public.%I FOR ALL
                USING (company_id = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint)
                WITH CHECK (company_id = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint)
            $p$, t);
        END IF;
        IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE schemaname='public' AND tablename=t AND policyname='platform_admin_bypass') THEN
            EXECUTE format($p$
                CREATE POLICY platform_admin_bypass ON public.%I FOR ALL
                USING (NULLIF(current_setting('app.platform_admin', true), '') = 'on')
                WITH CHECK (NULLIF(current_setting('app.platform_admin', true), '') = 'on')
            $p$, t);
        END IF;
    END LOOP;
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_app') THEN
        GRANT SELECT, INSERT, UPDATE, DELETE ON fin_config_sets, fin_config_documents, fin_config_change_log TO opstrax_app;
        GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO opstrax_app;
    END IF;
END
$rls$;
