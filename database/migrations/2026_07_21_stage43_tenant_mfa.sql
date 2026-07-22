-- Stage 43 — tenant-user MFA enrollment secret.
-- Closes the audit P0 where a Company Admin enabling "require MFA" permanently locked out every
-- affected user because there was no enrollment path (user_mfa_status could never flip to enabled).
-- The encrypted TOTP secret mirrors platform_admins.mfa_secret; enrollment is via /api/auth/mfa/enroll
-- + /api/auth/mfa/verify. Additive and idempotent.
ALTER TABLE user_mfa_status ADD COLUMN IF NOT EXISTS mfa_secret TEXT NULL;
