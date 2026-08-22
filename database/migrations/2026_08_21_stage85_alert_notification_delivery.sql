-- ─────────────────────────────────────────────────────────────────────────────
-- Stage 85 — alert notification delivery spine (protected-environment contract)
--
-- Settings → Notifications stores a per-user channel matrix (user_notification_prefs)
-- that nothing consumed: telemetry alerts never fanned out to email/SMS, so the
-- Email/SMS toggles were dead switches. The runtime now bridges telemetry_alerts
-- through the outbox ('alert.notification.requested') into per-user email/SMS
-- delivery. Protected environments skip owner-capable runtime schema init, so the
-- three objects that path needs are materialized here:
--
--   1. users.phone                       — SMS target for ops users (falls back to
--                                          drivers.phone for driver-linked users)
--   2. alert_notification_deliveries     — per-(alert, user, channel) send claims;
--                                          the UNIQUE constraint is the idempotency
--                                          guard that makes at-least-once outbox
--                                          redelivery safe
--   3. ux_outbox_alert_notification      — partial unique index the bridge's
--                                          INSERT ... ON CONFLICT claim targets
--                                          (detention stage47 pattern)
--
-- Requires the OutboxDispatcher to be enabled on the deployment
-- (OutboxDispatcher__Enabled=true, OutboxDispatcher__AllowProduction=true) —
-- without it messages queue as 'pending' and email/SMS never leaves.
--
-- Apply as the database OWNER (same flow as every stage file):
--   psql "$NEON_OWNER_URL" -f database/migrations/2026_08_21_stage85_alert_notification_delivery.sql
--
-- Idempotent: IF NOT EXISTS everywhere; policy/grant re-application is guarded.
-- ─────────────────────────────────────────────────────────────────────────────
BEGIN;

ALTER TABLE public.users ADD COLUMN IF NOT EXISTS phone VARCHAR(50);

CREATE TABLE IF NOT EXISTS public.alert_notification_deliveries (
  id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id bigint NOT NULL,
  alert_id bigint NOT NULL,
  user_id bigint NOT NULL,
  channel varchar(20) NOT NULL,
  recipient varchar(255),
  status varchar(20) NOT NULL DEFAULT 'pending',
  error text,
  sent_at timestamptz,
  created_at timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT uq_alert_notif_delivery UNIQUE (company_id, alert_id, user_id, channel)
);
CREATE INDEX IF NOT EXISTS idx_alert_notif_deliveries_alert
  ON public.alert_notification_deliveries(company_id, alert_id);

-- Claim target for the alert bridge's exactly-once outbox enqueue.
CREATE UNIQUE INDEX IF NOT EXISTS ux_outbox_alert_notification
  ON public.outbox_messages (tenant_id, aggregate_id)
  WHERE event_type = 'alert.notification.requested';

-- ── RLS enrolment — tenant-scoped delivery ledger (stage84 boilerplate) ──────
ALTER TABLE public.alert_notification_deliveries ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.alert_notification_deliveries FORCE ROW LEVEL SECURITY;
-- Policy/grant enrolment is guarded (stage65 pattern): on a fresh database the
-- opstrax_security schema (stage58) may not exist yet; the RLS reconciliation
-- pass enrolls this table once the security cutover has run.
DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_app')
     AND to_regprocedure('opstrax_security.current_tenant_id()') IS NOT NULL THEN
    DROP POLICY IF EXISTS tenant_ticket_app ON public.alert_notification_deliveries;
    CREATE POLICY tenant_ticket_app ON public.alert_notification_deliveries
      AS PERMISSIVE FOR ALL TO opstrax_app
      USING (company_id=(SELECT opstrax_security.current_tenant_id()))
      WITH CHECK (company_id=(SELECT opstrax_security.current_tenant_id()));
    REVOKE ALL ON TABLE public.alert_notification_deliveries FROM opstrax_app;
    GRANT SELECT,INSERT,UPDATE,DELETE ON TABLE public.alert_notification_deliveries TO opstrax_app;
    GRANT USAGE,SELECT ON SEQUENCE public.alert_notification_deliveries_id_seq TO opstrax_app;
  END IF;
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_system') THEN
    DROP POLICY IF EXISTS system_control_plane ON public.alert_notification_deliveries;
    CREATE POLICY system_control_plane ON public.alert_notification_deliveries
      AS PERMISSIVE FOR ALL TO opstrax_system USING (true) WITH CHECK (true);
    GRANT SELECT,INSERT,UPDATE,DELETE ON TABLE public.alert_notification_deliveries TO opstrax_system;
    GRANT USAGE,SELECT ON SEQUENCE public.alert_notification_deliveries_id_seq TO opstrax_system;
  END IF;
END $$;

INSERT INTO public.schema_migrations(version,description)
VALUES ('2026_08_21_stage85_alert_notification_delivery',
        'Alert notification delivery spine: users.phone, per-recipient delivery claims, outbox claim index for alert.notification.requested')
ON CONFLICT(version) DO NOTHING;
COMMIT;
