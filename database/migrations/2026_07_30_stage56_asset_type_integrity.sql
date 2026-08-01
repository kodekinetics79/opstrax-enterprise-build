-- Stage 56 — Returnable-asset type identity integrity
--
-- Asset type codes are tenant-wide configuration identities. Historical duplicate
-- groups are reported and never auto-deleted or merged.

BEGIN;

DO $asset_type_preflight$
DECLARE
  duplicate_groups BIGINT;
  duplicate_examples TEXT;
BEGIN
  IF to_regclass('public.fleet_tms_asset_types') IS NULL THEN
    RAISE EXCEPTION 'Stage 56 requires fleet_tms_asset_types; apply the Fleet TMS assets foundation first';
  END IF;

  SELECT COUNT(*) INTO duplicate_groups
  FROM (
    SELECT company_id, LOWER(BTRIM(code))
    FROM fleet_tms_asset_types
    GROUP BY company_id, LOWER(BTRIM(code))
    HAVING COUNT(*) > 1
  ) duplicates;
  IF duplicate_groups > 0 THEN
    SELECT string_agg(example, '; ' ORDER BY example) INTO duplicate_examples
    FROM (
      SELECT format('company_id=%s asset_type_code=%L ids=%s',
               company_id, LOWER(BTRIM(code)), array_agg(id ORDER BY id)::text) AS example
      FROM fleet_tms_asset_types
      GROUP BY company_id, LOWER(BTRIM(code))
      HAVING COUNT(*) > 1
      ORDER BY company_id, LOWER(BTRIM(code))
      LIMIT 10
    ) examples;
    RAISE EXCEPTION USING
      MESSAGE = format('Stage 56 blocked: %s tenant/asset-type-code duplicate group(s) require reconciliation', duplicate_groups),
      DETAIL = duplicate_examples,
      HINT = 'Retain the canonical asset type, repoint fleet_tms_assets.asset_type_id, remove duplicates, then rerun Stage 56.';
  END IF;
END
$asset_type_preflight$;

-- Rebuild the named contract index transactionally so a same-name definition
-- drift is repaired rather than silently retained behind IF NOT EXISTS.
DROP INDEX IF EXISTS uq_ftms_atype_tenant_code_norm;
CREATE UNIQUE INDEX uq_ftms_atype_tenant_code_norm
  ON fleet_tms_asset_types (company_id, LOWER(BTRIM(code)));

DO $asset_type_verify$
DECLARE
  idx RECORD;
BEGIN
  SELECT i.*, c.relname, n.nspname INTO idx
  FROM pg_index i
  JOIN pg_class c ON c.oid=i.indexrelid
  JOIN pg_namespace n ON n.oid=c.relnamespace
  WHERE n.nspname='public' AND c.relname='uq_ftms_atype_tenant_code_norm';
  IF idx IS NULL
     OR idx.indrelid <> 'public.fleet_tms_asset_types'::regclass
     OR NOT idx.indisunique OR NOT idx.indisvalid OR NOT idx.indisready
     OR idx.indnkeyatts <> 2 OR idx.indnatts <> 2
     OR pg_get_indexdef(idx.indexrelid,1,true) <> 'company_id'
     OR pg_get_indexdef(idx.indexrelid,2,true) <> 'lower(btrim(code::text))'
     OR idx.indpred IS NOT NULL THEN
    RAISE EXCEPTION 'Stage 56 verification failed: uq_ftms_atype_tenant_code_norm does not match the required unique tenant/asset-type-code contract';
  END IF;
END
$asset_type_verify$;

INSERT INTO schema_migrations(version,description)
VALUES ('2026_07_30_stage56_asset_type_integrity',
        'Returnable-asset type tenant-wide normalized code uniqueness')
ON CONFLICT(version) DO NOTHING;

COMMIT;
