BEGIN;

ALTER TABLE telemetry_rules
  ADD COLUMN IF NOT EXISTS policy_origin VARCHAR(40) NOT NULL DEFAULT 'legacy_unverified',
  ADD COLUMN IF NOT EXISTS approval_status VARCHAR(40) NOT NULL DEFAULT 'unapproved',
  ADD COLUMN IF NOT EXISTS approved_by BIGINT NULL,
  ADD COLUMN IF NOT EXISTS approved_at TIMESTAMPTZ NULL;

ALTER TABLE telemetry_rules ALTER COLUMN enabled SET DEFAULT FALSE;

-- Existing rules carrying a recorded actor came from the authenticated rule
-- workflow. Old startup-created defaults have no actor and must fail closed.
UPDATE telemetry_rules
SET policy_origin='user_workflow',
    approval_status='approved',
    approved_by=created_by,
    approved_at=COALESCE(updated_at,created_at,NOW())
WHERE policy_origin='legacy_unverified'
  AND approval_status='unapproved'
  AND created_by IS NOT NULL
  AND created_by>0;

UPDATE telemetry_rules
SET enabled=FALSE,
    approved_by=NULL,
    approved_at=NULL
WHERE enabled=TRUE
  AND NOT (
    policy_origin='user_workflow'
    AND approval_status='approved'
    AND approved_by IS NOT NULL
    AND approved_by>0
    AND approved_at IS NOT NULL
  );

ALTER TABLE telemetry_rules DROP CONSTRAINT IF EXISTS ck_telemetry_rules_approved_activation;
ALTER TABLE telemetry_rules ADD CONSTRAINT ck_telemetry_rules_approved_activation CHECK (
  enabled=FALSE OR (
    policy_origin='user_workflow'
    AND approval_status='approved'
    AND approved_by IS NOT NULL
    AND approved_by>0
    AND approved_at IS NOT NULL
  )
);

COMMIT;
