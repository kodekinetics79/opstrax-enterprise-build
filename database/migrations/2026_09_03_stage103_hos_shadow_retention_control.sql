-- Stage 103 — HOS shadow evidence retention/offboarding control
--
-- Stage102 made shadow snapshots runtime append-only, but its first trigger also
-- blocked database-owner DELETEs. That would prevent controlled retention/legal
-- offboarding cascades. Runtime principals still receive no UPDATE/DELETE grants;
-- this trigger adds defense in depth while preserving owner-level purge authority.
--
-- Shadow evidence remains immutable: UPDATE is never allowed through this trigger.
-- DELETE is rejected for application/control-plane runtime identities and allowed
-- only to an owner/administrative identity operating outside those runtime roles.

BEGIN;

DO $preflight$
BEGIN
  IF to_regclass('public.hos_shadow_clock_snapshots') IS NULL THEN
    RAISE EXCEPTION 'Stage103 requires hos_shadow_clock_snapshots';
  END IF;
END
$preflight$;

CREATE OR REPLACE FUNCTION prevent_hos_shadow_snapshot_mutation()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
BEGIN
  IF TG_OP = 'UPDATE' THEN
    RAISE EXCEPTION 'hos_shadow_clock_snapshots are immutable evidence; updates are prohibited';
  END IF;

  IF TG_OP = 'DELETE' AND current_user IN ('opstrax_app','opstrax_system') THEN
    RAISE EXCEPTION 'runtime principals cannot delete hos_shadow_clock_snapshots';
  END IF;

  -- Controlled owner-level retention/offboarding purge.
  RETURN OLD;
END;
$$;

DROP TRIGGER IF EXISTS trg_hos_shadow_no_update ON hos_shadow_clock_snapshots;
CREATE TRIGGER trg_hos_shadow_no_update
  BEFORE UPDATE OR DELETE ON hos_shadow_clock_snapshots
  FOR EACH ROW EXECUTE FUNCTION prevent_hos_shadow_snapshot_mutation();

DO $postcondition$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM pg_trigger
    WHERE tgname='trg_hos_shadow_no_update' AND NOT tgisinternal
  ) THEN
    RAISE EXCEPTION 'Stage103 failed: HOS shadow mutation trigger missing';
  END IF;

  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_app')
     AND (
       has_table_privilege('opstrax_app','public.hos_shadow_clock_snapshots','UPDATE')
       OR has_table_privilege('opstrax_app','public.hos_shadow_clock_snapshots','DELETE')
     ) THEN
    RAISE EXCEPTION 'Stage103 failed: opstrax_app must not mutate shadow evidence';
  END IF;

  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_system')
     AND (
       has_table_privilege('opstrax_system','public.hos_shadow_clock_snapshots','UPDATE')
       OR has_table_privilege('opstrax_system','public.hos_shadow_clock_snapshots','DELETE')
     ) THEN
    RAISE EXCEPTION 'Stage103 failed: opstrax_system must not mutate shadow evidence';
  END IF;
END
$postcondition$;

COMMIT;
