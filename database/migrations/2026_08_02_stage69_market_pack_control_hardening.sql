-- Stage 69 — Market-pack commercial control hardening
-- Canonical assignment states are intentionally closed: active | disabled.
-- HTTP mutation validation and Platform audit are implemented in MarketPackEndpoints;
-- this constraint protects non-HTTP writers and future imports.
-- Existing invalid state is not silently rewritten: constraint installation fails so
-- an operator must investigate and disposition the commercial assignment explicitly.

BEGIN;

DO $stage69_market_pack_status$
BEGIN
  -- Never reinterpret an existing commercial state during recovery. A NULL or
  -- unknown value requires explicit operator disposition before this migration.
  IF EXISTS (
    SELECT 1 FROM tenant_market_packs
    WHERE status IS NULL OR status NOT IN ('active','disabled')
  ) THEN
    RAISE EXCEPTION 'Stage69 blocked: unknown tenant market-pack status requires reconciliation';
  END IF;

  -- Replace a same-named but weakened/unvalidated constraint. This makes a
  -- ledgered recovery replay converge the catalog, not merely trust its name.
  ALTER TABLE tenant_market_packs
    ALTER COLUMN status SET DEFAULT 'active';
  ALTER TABLE tenant_market_packs
    ALTER COLUMN status SET NOT NULL;
  ALTER TABLE tenant_market_packs
    DROP CONSTRAINT IF EXISTS ck_tenant_market_packs_status;
  ALTER TABLE tenant_market_packs
    ADD CONSTRAINT ck_tenant_market_packs_status
    CHECK (status IN ('active','disabled'));
END
$stage69_market_pack_status$;

COMMENT ON CONSTRAINT ck_tenant_market_packs_status ON tenant_market_packs IS
  'Commercial market-pack assignment state: active or disabled only.';

INSERT INTO schema_migrations(version, description)
VALUES (
  '2026_08_02_stage69_market_pack_control_hardening',
  'Closed market-pack commercial status contract and audited Platform mutation boundary'
)
ON CONFLICT (version) DO NOTHING;

COMMIT;
