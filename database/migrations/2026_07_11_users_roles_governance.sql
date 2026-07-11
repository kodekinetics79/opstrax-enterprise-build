BEGIN;

ALTER TABLE roles ADD COLUMN IF NOT EXISTS company_id BIGINT NULL REFERENCES companies(id);
ALTER TABLE roles ADD COLUMN IF NOT EXISTS is_system BOOLEAN NOT NULL DEFAULT TRUE;
ALTER TABLE roles DROP CONSTRAINT IF EXISTS roles_name_key;

CREATE UNIQUE INDEX IF NOT EXISTS ux_roles_system_name
  ON roles (LOWER(name)) WHERE company_id IS NULL;
CREATE UNIQUE INDEX IF NOT EXISTS ux_roles_tenant_name
  ON roles (company_id, LOWER(name)) WHERE company_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_roles_company ON roles (company_id);

-- System templates are readable by every tenant, while custom roles are visible
-- and mutable only inside their owning tenant. Platform control-plane scope is a
-- separate, explicit bypass. FORCE prevents accidental owner-table bypass.
ALTER TABLE roles ENABLE ROW LEVEL SECURITY;
ALTER TABLE roles FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS roles_tenant_read ON roles;
CREATE POLICY roles_tenant_read ON roles
  FOR SELECT
  USING (
    company_id IS NULL
    OR company_id = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint
  );

DROP POLICY IF EXISTS roles_tenant_insert ON roles;
CREATE POLICY roles_tenant_insert ON roles
  FOR INSERT
  WITH CHECK (
    company_id IS NOT NULL
    AND company_id = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint
  );

DROP POLICY IF EXISTS roles_tenant_update ON roles;
CREATE POLICY roles_tenant_update ON roles
  FOR UPDATE
  USING (
    company_id IS NOT NULL
    AND company_id = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint
  )
  WITH CHECK (
    company_id IS NOT NULL
    AND company_id = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint
  );

DROP POLICY IF EXISTS roles_tenant_delete ON roles;
CREATE POLICY roles_tenant_delete ON roles
  FOR DELETE
  USING (
    company_id IS NOT NULL
    AND company_id = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint
  );

DROP POLICY IF EXISTS roles_platform_admin_bypass ON roles;
CREATE POLICY roles_platform_admin_bypass ON roles
  FOR ALL
  USING (NULLIF(current_setting('app.platform_admin', true), '') = 'on')
  WITH CHECK (NULLIF(current_setting('app.platform_admin', true), '') = 'on');

GRANT SELECT, INSERT, UPDATE, DELETE ON roles TO opstrax_app;
GRANT USAGE, SELECT ON SEQUENCE roles_id_seq TO opstrax_app;

COMMIT;
