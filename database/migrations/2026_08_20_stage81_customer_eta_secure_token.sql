-- Stage 81 — Customer ETA secure tracking token (protected-environment repair)
--
-- Protected Staging and Production deliberately skip runtime schema initialization
-- (ShouldRunSchemaInitAsync returns false for the restricted opstrax_app identity), so
-- anything created only by a boot-time schema service never exists there. customer_eta_links
-- .secure_token was created ONLY by the CustomerEtaSecureToken step in Program.cs, so in
-- production the column was absent and GET /api/customer-eta/track/{code} — a public,
-- unauthenticated endpoint — returned HTTP 500 on EVERY request (42703 undefined column),
-- taking the entire customer-facing tracking surface offline.
--
-- This migration moves that step into the migration chain, where protected environments
-- actually apply it. Additive and idempotent; mirrors Program.cs exactly, including the
-- security cutover: legacy links minted against the enumerable jobs.tracking_code
-- ('ETA-'||job_code) carry no secure token and are disabled so those guessable codes stop
-- resolving. Re-runnable: already-tokenised links are untouched.

BEGIN;

-- The base SQL predecessor names this concept `status`; protected databases that
-- previously ran Batch2SchemaService already have `public_status`. Make the owner
-- migration self-contained for clean/predecessor-only databases instead of relying
-- on a runtime schema initializer that protected environments intentionally skip.
ALTER TABLE customer_eta_links
  ADD COLUMN IF NOT EXISTS public_status VARCHAR(80) NOT NULL DEFAULT 'Active';

ALTER TABLE customer_eta_links ADD COLUMN IF NOT EXISTS secure_token VARCHAR(80) NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_customer_eta_links_secure_token
  ON customer_eta_links (secure_token) WHERE secure_token IS NOT NULL;

UPDATE customer_eta_links
   SET public_status = 'Disabled'
 WHERE secure_token IS NULL
   AND public_status <> 'Disabled';

INSERT INTO schema_migrations (version, description)
VALUES ('2026_08_20_stage81_customer_eta_secure_token', 'Customer ETA secure tracking token column, index and legacy-link cutover')
ON CONFLICT (version) DO NOTHING;

COMMIT;
