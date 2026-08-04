-- Stage 73: fail closed when the offboarding transaction marker is absent.
--
-- PostgreSQL boolean expressions propagate NULL. The Stage 72 certified-log
-- guard used NOT(current_setting(...)=... AND role_check), which evaluates to
-- NULL when the custom setting has never been defined; PL/pgSQL treats a NULL
-- IF condition as false and therefore allowed an ordinary certified-log delete.
-- COALESCE makes absence explicitly false. The system role and transaction
-- marker remain jointly required for tenant offboarding deletion.
BEGIN;

CREATE OR REPLACE FUNCTION stage65_prevent_certified_hos_log_delete()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
BEGIN
  IF OLD.is_certified
     AND NOT (
       COALESCE(current_setting('opstrax.offboarding', true) = 'on', FALSE)
       AND pg_has_role(current_user, 'opstrax_system', 'MEMBER')
     ) THEN
    RAISE EXCEPTION 'Certified HOS segments cannot be deleted; create a correction and recertify';
  END IF;
  RETURN OLD;
END $$;

CREATE OR REPLACE FUNCTION stage65_guard_hos_certification_snapshot()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
BEGIN
  IF TG_OP = 'DELETE'
     AND COALESCE(current_setting('opstrax.offboarding', true) = 'on', FALSE)
     AND pg_has_role(current_user, 'opstrax_system', 'MEMBER') THEN
    RETURN OLD;
  END IF;
  RAISE EXCEPTION 'HOS certification snapshots are immutable';
END $$;

INSERT INTO schema_migrations(version,description)
VALUES ('2026_08_02_stage73_hos_offboarding_null_fail_closed',
        'Fail closed when the dual-gated HOS offboarding marker is absent')
ON CONFLICT(version) DO UPDATE SET description=EXCLUDED.description;

COMMIT;
