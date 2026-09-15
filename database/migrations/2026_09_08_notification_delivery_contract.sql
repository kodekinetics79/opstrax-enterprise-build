-- Reconcile both historical notifications table shapes with NotificationService.
-- Additive and idempotent: existing notification and recipient evidence is preserved.

BEGIN;

ALTER TABLE notifications ADD COLUMN IF NOT EXISTS body TEXT;
ALTER TABLE notifications ADD COLUMN IF NOT EXISTS severity VARCHAR(40) NOT NULL DEFAULT 'Medium';
ALTER TABLE notifications ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ;

COMMIT;
