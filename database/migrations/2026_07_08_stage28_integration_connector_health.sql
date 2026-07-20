-- Stage 28 — Integration connector health tracking
--
-- Adds columns that record the result of the last REAL provider handshake
-- (POST /api/integrations/{id}/test-connection) so operators see, per connector,
-- when credentials were last verified and what the provider returned — independently
-- of last_sync_at (which reflects data flow, not the auth check).
--
-- MUST be applied by the DB OWNER, not the app role. The runtime connects as the
-- restricted `opstrax_app` role (NOBYPASSRLS, no DDL grants) and SKIPS all schema
-- init under RLS enforcement — so Batch7SchemaService will not create these at
-- startup. Apply out-of-band (owner role) in every environment, including Render.
--
-- Idempotent: safe to run repeatedly.

ALTER TABLE integrations ADD COLUMN IF NOT EXISTS last_tested_at    TIMESTAMPTZ NULL;
ALTER TABLE integrations ADD COLUMN IF NOT EXISTS last_test_ok      BOOLEAN NULL;
ALTER TABLE integrations ADD COLUMN IF NOT EXISTS last_test_message TEXT NULL;
