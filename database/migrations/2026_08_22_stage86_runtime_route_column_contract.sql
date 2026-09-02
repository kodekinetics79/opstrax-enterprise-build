-- ─────────────────────────────────────────────────────────────────────────────
-- Stage 86 — runtime route column contract (schema-service/migration split-brain)
--
-- WHY THIS MIGRATION EXISTS (RETEST-20260821-1035-R1, DEF-016/017/018/019 and the
-- schema half of DEF-020):
-- routes.sla_risk, work_orders.asset_id, maintenance_items.asset_id,
-- safety_events.event_number, dashcam_events.event_number and
-- audit_logs.severity/module_key/action_type were declared ONLY in the
-- owner-capable runtime schema services (Batch2SchemaService routes block,
-- Batch3SchemaService maintenance/work-order columns, Batch4SchemaService
-- safety/dashcam event numbers, Batch7SchemaService audit enrichment). No
-- migration created them. Protected environments deliberately skip runtime
-- schema initialization (restricted opstrax_app role + FORCE RLS, see
-- Program.cs ShouldRunSchemaInitAsync), so production never materialized these
-- columns: the endpoints selecting them 500 with 42703 while the readiness
-- report stayed green. This file is the documented owner out-of-band path that
-- ends the split-brain; the column types below mirror the Batch* declarations
-- EXACTLY so dev (schema services) and production (this migration) converge on
-- one shape.
--
-- Apply as the database OWNER (same flow as every stage file):
--   psql "$NEON_OWNER_URL" -f database/migrations/2026_08_22_stage86_runtime_route_column_contract.sql
--
-- Idempotent: ADD COLUMN IF NOT EXISTS + a keyed backfill that only touches NULL
-- rows. Plain additive columns need no RLS enrolment (their tables are already
-- enrolled); the one backfill UPDATE is guarded below because safety_events may
-- carry FORCE ROW LEVEL SECURITY (stage50/53/58): under FORCE the table OWNER is
-- also subject to policies, and no policy names the owner, so an unguarded
-- UPDATE would silently match zero rows on a cutover database.
-- ─────────────────────────────────────────────────────────────────────────────

BEGIN;

-- Batch2SchemaService: new("routes", "sla_risk", "VARCHAR(60) NOT NULL DEFAULT 'Low'")
ALTER TABLE public.routes
  ADD COLUMN IF NOT EXISTS sla_risk VARCHAR(60) NOT NULL DEFAULT 'Low';

-- Batch3SchemaService: new("work_orders", "asset_id", "BIGINT NULL")
ALTER TABLE public.work_orders
  ADD COLUMN IF NOT EXISTS asset_id BIGINT NULL;

-- Batch3SchemaService: new("maintenance_items", "asset_id", "BIGINT NULL")
ALTER TABLE public.maintenance_items
  ADD COLUMN IF NOT EXISTS asset_id BIGINT NULL;

-- Batch4SchemaService: new("safety_events", "event_number", "VARCHAR(80) NULL")
ALTER TABLE public.safety_events
  ADD COLUMN IF NOT EXISTS event_number VARCHAR(80) NULL;

-- Batch4SchemaService: new("dashcam_events", "event_number", "VARCHAR(80) NULL")
ALTER TABLE public.dashcam_events
  ADD COLUMN IF NOT EXISTS event_number VARCHAR(80) NULL;

-- Batch7SchemaService audit_logs enrichment (severity + module context)
ALTER TABLE public.audit_logs
  ADD COLUMN IF NOT EXISTS severity VARCHAR(40) NOT NULL DEFAULT 'Info';
ALTER TABLE public.audit_logs
  ADD COLUMN IF NOT EXISTS module_key VARCHAR(100) NULL;
ALTER TABLE public.audit_logs
  ADD COLUMN IF NOT EXISTS action_type VARCHAR(80) NOT NULL DEFAULT 'update';

-- Backfill safety_events.event_number for pre-existing rows so the UI's unique
-- display key renders (mirrors Batch4SchemaService's COALESCE backfill:
-- event_number=COALESCE(event_number,'SAFE-'||LPAD(id::TEXT,5,'0'))).
-- Guarded (stage83/84 pattern): if the table is under FORCE ROW LEVEL SECURITY
-- the owner is policy-constrained too, so lift FORCE only for the duration of
-- the backfill and restore the exact prior state. Everything runs inside this
-- file's single transaction — an error rolls back the toggle with the rest.
DO $stage86_backfill$
DECLARE
  was_forced boolean;
BEGIN
  SELECT c.relforcerowsecurity INTO was_forced
  FROM pg_class c WHERE c.oid = to_regclass('public.safety_events');
  IF was_forced THEN
    EXECUTE 'ALTER TABLE public.safety_events NO FORCE ROW LEVEL SECURITY';
  END IF;
  UPDATE public.safety_events
  SET event_number = 'SAFE-' || LPAD(id::TEXT, 5, '0')
  WHERE event_number IS NULL;
  IF was_forced THEN
    EXECUTE 'ALTER TABLE public.safety_events FORCE ROW LEVEL SECURITY';
  END IF;
END
$stage86_backfill$;

INSERT INTO public.schema_migrations (version, description)
VALUES ('2026_08_22_stage86_runtime_route_column_contract',
        'Materialize the runtime route/work-order/safety/audit columns previously created only by owner-capable schema services')
ON CONFLICT (version) DO NOTHING;

COMMIT;
