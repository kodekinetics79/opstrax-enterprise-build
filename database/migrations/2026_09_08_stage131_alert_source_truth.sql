-- Stage 131 — identify the source ledger for alert follow-up tasks.
--
-- The customer Alerts Center now uses production-written telemetry_alerts rather
-- than the legacy seed-only ai_insights queue. Existing task links retain their
-- legacy identity; new telemetry task links cannot collide with legacy alert ids.

BEGIN;
SET LOCAL lock_timeout = '10s';

ALTER TABLE alert_follow_up_tasks
  ADD COLUMN IF NOT EXISTS source_type VARCHAR(40) NOT NULL DEFAULT 'LegacyInsight';

CREATE INDEX IF NOT EXISTS idx_alert_tasks_source_alert
  ON alert_follow_up_tasks (source_type, alert_id, company_id);

COMMENT ON COLUMN alert_follow_up_tasks.source_type IS
  'Source alert ledger for an id: LegacyInsight for historical rows or Telemetry for telemetry_alerts.';

COMMIT;
