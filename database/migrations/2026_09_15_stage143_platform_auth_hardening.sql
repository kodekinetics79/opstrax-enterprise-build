-- Stage 143 — Platform authentication hardening
--
-- Persists bounded TOTP enrollment state so verification challenges expire and
-- repeated guesses lock independently of a single API process.

BEGIN;

ALTER TABLE platform_admins
  ADD COLUMN IF NOT EXISTS mfa_enrollment_started_at TIMESTAMPTZ NULL,
  ADD COLUMN IF NOT EXISTS mfa_enrollment_failed_attempts INT NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS mfa_enrollment_locked_until TIMESTAMPTZ NULL;

ALTER TABLE platform_admins
  DROP CONSTRAINT IF EXISTS ck_platform_admin_mfa_enrollment_attempts;
ALTER TABLE platform_admins
  ADD CONSTRAINT ck_platform_admin_mfa_enrollment_attempts
  CHECK (mfa_enrollment_failed_attempts >= 0 AND mfa_enrollment_failed_attempts <= 5);

INSERT INTO schema_migrations(version, description)
VALUES ('2026_09_15_stage143_platform_auth_hardening',
        'Expiring and guess-limited platform MFA enrollment')
ON CONFLICT(version) DO NOTHING;

COMMIT;
