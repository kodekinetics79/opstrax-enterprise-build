-- Stage 75 — bounded, attributable, read-only Platform support access.
-- The prior route minted an ordinary tenant session and revoked sessions by a
-- time-window heuristic. This contract gives every grant a public-safe reference,
-- binds exactly one tenant session by FK, rejects cross-tenant/user bindings and
-- preserves the Stage-58 system-only Platform ledger boundary.
BEGIN;

ALTER TABLE platform_impersonation_sessions
  ADD COLUMN IF NOT EXISTS target_user_id BIGINT NULL REFERENCES users(id),
  ADD COLUMN IF NOT EXISTS grant_ref UUID NULL DEFAULT gen_random_uuid();

-- Never guess which user an older write-capable grant represented. An operator
-- must end/remove and investigate any such historical row before rollout.
DO $legacy_grants$
BEGIN
  IF EXISTS (SELECT 1 FROM platform_impersonation_sessions WHERE target_user_id IS NULL) THEN
    RAISE EXCEPTION 'Stage75 blocked: unbound historical impersonation grants require operator reconciliation';
  END IF;
  IF EXISTS (
    SELECT 1 FROM platform_impersonation_sessions
    WHERE expires_at <= created_at OR expires_at > created_at + INTERVAL '60 minutes'
  ) THEN
    RAISE EXCEPTION 'Stage75 blocked: historical impersonation duration is outside the 5-60 minute contract';
  END IF;
END
$legacy_grants$;

UPDATE platform_impersonation_sessions SET grant_ref=gen_random_uuid() WHERE grant_ref IS NULL;
ALTER TABLE platform_impersonation_sessions
  ALTER COLUMN target_user_id SET NOT NULL,
  ALTER COLUMN grant_ref SET NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_platform_impersonation_grant_ref
  ON platform_impersonation_sessions(grant_ref);

ALTER TABLE platform_impersonation_sessions
  DROP CONSTRAINT IF EXISTS ck_platform_impersonation_expiry;
ALTER TABLE platform_impersonation_sessions
  ADD CONSTRAINT ck_platform_impersonation_expiry
  CHECK (expires_at > created_at AND expires_at <= created_at + INTERVAL '60 minutes');

ALTER TABLE user_sessions ADD COLUMN IF NOT EXISTS impersonation_grant_id BIGINT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS ux_user_sessions_impersonation_grant
  ON user_sessions(impersonation_grant_id) WHERE impersonation_grant_id IS NOT NULL;

DO $grant_fk$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='fk_user_sessions_impersonation_grant') THEN
    ALTER TABLE user_sessions ADD CONSTRAINT fk_user_sessions_impersonation_grant
      FOREIGN KEY (impersonation_grant_id)
      REFERENCES platform_impersonation_sessions(id) ON DELETE CASCADE;
  END IF;
END
$grant_fk$;

CREATE OR REPLACE FUNCTION validate_impersonation_session_binding()
RETURNS trigger LANGUAGE plpgsql AS $binding$
BEGIN
  IF NEW.impersonation_grant_id IS NOT NULL AND NOT EXISTS (
    SELECT 1 FROM platform_impersonation_sessions p
    WHERE p.id=NEW.impersonation_grant_id
      AND p.company_id=NEW.company_id AND p.target_user_id=NEW.user_id
      AND p.ended_at IS NULL AND p.expires_at>NOW()
  ) THEN
    RAISE EXCEPTION 'Invalid or inactive impersonation grant binding';
  END IF;
  RETURN NEW;
END
$binding$;

DROP TRIGGER IF EXISTS trg_validate_impersonation_session_binding ON user_sessions;
CREATE TRIGGER trg_validate_impersonation_session_binding
  BEFORE INSERT OR UPDATE OF impersonation_grant_id, user_id, company_id ON user_sessions
  FOR EACH ROW EXECUTE FUNCTION validate_impersonation_session_binding();

-- Remove the unsafe inherited capability. Deployment configuration remains an
-- independent default-off gate even for explicitly reviewed roles/super-admins.
DELETE FROM platform_role_permissions rp
USING platform_roles r
WHERE rp.role_id=r.id AND r.role_key='support_admin'
  AND rp.permission_key='platform:impersonation:start';

-- Reassert the terminal system-only control-plane policy. The tenant app role
-- must never read operator identity/reason from this ledger.
ALTER TABLE platform_impersonation_sessions ENABLE ROW LEVEL SECURITY;
ALTER TABLE platform_impersonation_sessions FORCE ROW LEVEL SECURITY;
DO $policies$
DECLARE policy_name text;
BEGIN
  FOR policy_name IN
    SELECT policyname FROM pg_policies
    WHERE schemaname='public' AND tablename='platform_impersonation_sessions'
  LOOP
    EXECUTE format('DROP POLICY %I ON platform_impersonation_sessions', policy_name);
  END LOOP;
END
$policies$;
REVOKE ALL ON platform_impersonation_sessions FROM PUBLIC, opstrax_app;
DO $system_policy$
BEGIN
  -- On a fresh chain Stage58 runs terminally after Stage75 and creates the system
  -- role/policy. On an already-secured chain reassert it here without assuming order.
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_system') THEN
    EXECUTE 'CREATE POLICY system_control_plane ON platform_impersonation_sessions FOR ALL TO opstrax_system USING(true) WITH CHECK(true)';
    EXECUTE 'GRANT SELECT,INSERT,UPDATE,DELETE ON platform_impersonation_sessions TO opstrax_system';
  END IF;
END
$system_policy$;

COMMENT ON COLUMN user_sessions.impersonation_grant_id IS
  'Unique nullable binding to one bounded Platform support-access grant.';
COMMENT ON COLUMN platform_impersonation_sessions.grant_ref IS
  'Non-PII reference safe for tenant-visible support-access audit and UI.';

DO $verify$
BEGIN
  IF has_table_privilege('opstrax_app','platform_impersonation_sessions','SELECT')
     OR (EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_system')
         AND NOT has_table_privilege('opstrax_system','platform_impersonation_sessions','SELECT,INSERT,UPDATE,DELETE')) THEN
    RAISE EXCEPTION 'Stage75 Platform support ledger privilege boundary unsafe';
  END IF;
  IF EXISTS (
    SELECT 1 FROM platform_role_permissions rp JOIN platform_roles r ON r.id=rp.role_id
    WHERE r.role_key='support_admin' AND rp.permission_key='platform:impersonation:start'
  ) THEN
    RAISE EXCEPTION 'Stage75 support_admin still inherits impersonation';
  END IF;
END
$verify$;

INSERT INTO schema_migrations(version,description)
VALUES ('2026_08_02_stage75_bounded_support_access',
        'Default-off, uniquely bound, read-only and dual-audited Platform support access')
ON CONFLICT(version) DO UPDATE SET description=EXCLUDED.description;

COMMIT;
