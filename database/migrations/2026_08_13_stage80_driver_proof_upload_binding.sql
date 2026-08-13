-- Stage 80 — non-forgeable driver proof upload binding.
--
-- Production runs the API as opstrax_app and intentionally skips runtime DDL.
-- Apply this owner migration before deploying the handlers that register/consume
-- driver proof uploads. DispatchSchemaService mirrors the definition for local and
-- owner-capable bootstrap environments.
BEGIN;

CREATE TABLE IF NOT EXISTS dispatch_proof_uploads (
    id                    BIGINT NOT NULL GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id            BIGINT NOT NULL,
    assignment_id         BIGINT NOT NULL REFERENCES dispatch_assignments(id) ON DELETE CASCADE,
    driver_id             BIGINT NOT NULL,
    uploaded_by_user_id   BIGINT NOT NULL,
    kind                  VARCHAR(30) NOT NULL CHECK (kind IN ('photo','signature')),
    reference             TEXT NOT NULL,
    content_type          VARCHAR(120) NOT NULL,
    size_bytes            BIGINT NOT NULL CHECK (size_bytes > 0),
    created_at            TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    consumed_at           TIMESTAMPTZ NULL,
    proof_id              BIGINT NULL REFERENCES dispatch_proofs(id),
    UNIQUE (company_id, reference),
    CHECK ((consumed_at IS NULL AND proof_id IS NULL) OR
           (consumed_at IS NOT NULL AND proof_id IS NOT NULL))
);

CREATE INDEX IF NOT EXISTS idx_dpu_owned_unconsumed
  ON dispatch_proof_uploads(company_id,assignment_id,driver_id,consumed_at);
CREATE INDEX IF NOT EXISTS idx_dpu_proof ON dispatch_proof_uploads(proof_id);

ALTER TABLE dispatch_proof_uploads ENABLE ROW LEVEL SECURITY;
ALTER TABLE dispatch_proof_uploads FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON dispatch_proof_uploads;
DROP POLICY IF EXISTS platform_admin_bypass ON dispatch_proof_uploads;
DROP POLICY IF EXISTS tenant_ticket_app ON dispatch_proof_uploads;
DROP POLICY IF EXISTS system_control_plane ON dispatch_proof_uploads;
DO $stage80_rls$
BEGIN
  IF to_regprocedure('opstrax_security.current_tenant_id()') IS NOT NULL THEN
    CREATE POLICY tenant_ticket_app ON dispatch_proof_uploads FOR ALL TO opstrax_app
      USING (company_id=(SELECT opstrax_security.current_tenant_id()))
      WITH CHECK (company_id=(SELECT opstrax_security.current_tenant_id()));
    CREATE POLICY system_control_plane ON dispatch_proof_uploads FOR ALL TO opstrax_system
      USING (true) WITH CHECK (true);
  ELSE
    -- Clean-chain runners apply Stage58 after the runtime migrations. Keep this
    -- table fail-closed and tenant scoped until that terminal reconciliation
    -- replaces these legacy-GUC policies with signed tenant-ticket policies.
    CREATE POLICY tenant_isolation ON dispatch_proof_uploads FOR ALL
      USING (company_id=NULLIF(current_setting('app.current_tenant_id',true),'')::BIGINT)
      WITH CHECK (company_id=NULLIF(current_setting('app.current_tenant_id',true),'')::BIGINT);
    CREATE POLICY platform_admin_bypass ON dispatch_proof_uploads FOR ALL
      USING (NULLIF(current_setting('app.platform_admin',true),'')='on')
      WITH CHECK (NULLIF(current_setting('app.platform_admin',true),'')='on');
  END IF;
END
$stage80_rls$;

REVOKE ALL ON TABLE dispatch_proof_uploads FROM PUBLIC;
REVOKE ALL ON SEQUENCE dispatch_proof_uploads_id_seq FROM PUBLIC;
GRANT SELECT,INSERT,UPDATE,DELETE ON TABLE dispatch_proof_uploads TO opstrax_app;
GRANT USAGE,SELECT ON SEQUENCE dispatch_proof_uploads_id_seq TO opstrax_app;
GRANT SELECT,INSERT,UPDATE,DELETE ON TABLE dispatch_proof_uploads TO opstrax_system;
GRANT USAGE,SELECT ON SEQUENCE dispatch_proof_uploads_id_seq TO opstrax_system;

INSERT INTO schema_migrations(version,description)
VALUES ('2026_08_13_stage80_driver_proof_upload_binding',
        'Bind driver proof uploads to tenant, driver, assignment, user, and one-time proof consumption')
ON CONFLICT (version) DO NOTHING;

COMMIT;
