-- Stage 54 — Cold-chain device identity and retry integrity
--
-- Additive/idempotent and fail-fast. Historical duplicates are never guessed or
-- deleted: an operator must reconcile them, then rerun this migration.

BEGIN;

DO $cold_chain_device_preflight$
DECLARE
  duplicate_groups BIGINT;
  duplicate_examples TEXT;
BEGIN
  IF to_regclass('public.fleet_tms_temperature_devices') IS NULL THEN
    RAISE EXCEPTION 'Stage 54 requires fleet_tms_temperature_devices; apply the Fleet TMS foundation first';
  END IF;

  SELECT COUNT(*) INTO duplicate_groups
  FROM (
    SELECT company_id, LOWER(BTRIM(device_code))
    FROM fleet_tms_temperature_devices
    GROUP BY company_id, LOWER(BTRIM(device_code))
    HAVING COUNT(*) > 1
  ) duplicates;
  IF duplicate_groups > 0 THEN
    SELECT string_agg(example, '; ' ORDER BY example) INTO duplicate_examples
    FROM (
      SELECT format('company_id=%s device_code=%L ids=%s',
               company_id, LOWER(BTRIM(device_code)), array_agg(id ORDER BY id)::text) AS example
      FROM fleet_tms_temperature_devices
      GROUP BY company_id, LOWER(BTRIM(device_code))
      HAVING COUNT(*) > 1
      ORDER BY company_id, LOWER(BTRIM(device_code))
      LIMIT 10
    ) examples;
    RAISE EXCEPTION USING
      MESSAGE = format('Stage 54 blocked: %s tenant/device-code duplicate group(s) require reconciliation', duplicate_groups),
      DETAIL = duplicate_examples,
      HINT = 'Retain the canonical device, repoint dependent readings/alerts, remove duplicates, then rerun Stage 54.';
  END IF;

  SELECT COUNT(*) INTO duplicate_groups
  FROM (
    SELECT company_id, COALESCE(branch_id,0), idempotency_key
    FROM fleet_tms_temperature_devices
    WHERE NULLIF(BTRIM(idempotency_key),'') IS NOT NULL
    GROUP BY company_id, COALESCE(branch_id,0), idempotency_key
    HAVING COUNT(*) > 1
  ) duplicates;
  IF duplicate_groups > 0 THEN
    SELECT string_agg(example, '; ' ORDER BY example) INTO duplicate_examples
    FROM (
      SELECT format('company_id=%s branch_scope=%s idempotency_key=%L ids=%s',
               company_id, COALESCE(branch_id,0), idempotency_key,
               array_agg(id ORDER BY id)::text) AS example
      FROM fleet_tms_temperature_devices
      WHERE NULLIF(BTRIM(idempotency_key),'') IS NOT NULL
      GROUP BY company_id, COALESCE(branch_id,0), idempotency_key
      HAVING COUNT(*) > 1
      ORDER BY company_id, COALESCE(branch_id,0), idempotency_key
      LIMIT 10
    ) examples;
    RAISE EXCEPTION USING
      MESSAGE = format('Stage 54 blocked: %s tenant/branch/device-idempotency duplicate group(s) require reconciliation', duplicate_groups),
      DETAIL = duplicate_examples,
      HINT = 'Retain the original request result, repoint dependent rows, remove replay duplicates, then rerun Stage 54.';
  END IF;
END
$cold_chain_device_preflight$;

-- Rebuild the named contract indexes transactionally. This repairs a same-name
-- definition drift instead of silently retaining it behind IF NOT EXISTS.
DROP INDEX IF EXISTS uq_ftms_tdev_tenant_code_norm;
CREATE UNIQUE INDEX uq_ftms_tdev_tenant_code_norm
  ON fleet_tms_temperature_devices
     (company_id, LOWER(BTRIM(device_code)));

DROP INDEX IF EXISTS uq_ftms_tdev_branch_idem;
CREATE UNIQUE INDEX uq_ftms_tdev_branch_idem
  ON fleet_tms_temperature_devices
     (company_id, COALESCE(branch_id,0), idempotency_key)
  WHERE NULLIF(BTRIM(idempotency_key),'') IS NOT NULL;

DO $cold_chain_device_verify$
DECLARE
  idx RECORD;
BEGIN
  SELECT i.*, c.relname, n.nspname INTO idx
  FROM pg_index i
  JOIN pg_class c ON c.oid=i.indexrelid
  JOIN pg_namespace n ON n.oid=c.relnamespace
  WHERE n.nspname='public' AND c.relname='uq_ftms_tdev_tenant_code_norm';
  IF idx IS NULL
     OR idx.indrelid <> 'public.fleet_tms_temperature_devices'::regclass
     OR NOT idx.indisunique OR NOT idx.indisvalid OR NOT idx.indisready
     OR idx.indnkeyatts <> 2 OR idx.indnatts <> 2
     OR pg_get_indexdef(idx.indexrelid,1,true) <> 'company_id'
     OR pg_get_indexdef(idx.indexrelid,2,true) <> 'lower(btrim(device_code::text))'
     OR idx.indpred IS NOT NULL THEN
    RAISE EXCEPTION 'Stage 54 verification failed: uq_ftms_tdev_tenant_code_norm does not match the required unique tenant/device-code contract';
  END IF;

  SELECT i.*, c.relname, n.nspname INTO idx
  FROM pg_index i
  JOIN pg_class c ON c.oid=i.indexrelid
  JOIN pg_namespace n ON n.oid=c.relnamespace
  WHERE n.nspname='public' AND c.relname='uq_ftms_tdev_branch_idem';
  IF idx IS NULL
     OR idx.indrelid <> 'public.fleet_tms_temperature_devices'::regclass
     OR NOT idx.indisunique OR NOT idx.indisvalid OR NOT idx.indisready
     OR idx.indnkeyatts <> 3 OR idx.indnatts <> 3
     OR pg_get_indexdef(idx.indexrelid,1,true) <> 'company_id'
     OR pg_get_indexdef(idx.indexrelid,2,true) <> 'COALESCE(branch_id, 0::bigint)'
     OR pg_get_indexdef(idx.indexrelid,3,true) <> 'idempotency_key'
     OR pg_get_expr(idx.indpred,idx.indrelid) <> '(NULLIF(btrim((idempotency_key)::text), ''''::text) IS NOT NULL)' THEN
    RAISE EXCEPTION 'Stage 54 verification failed: uq_ftms_tdev_branch_idem does not match the required unique branch/idempotency contract';
  END IF;
END
$cold_chain_device_verify$;

INSERT INTO schema_migrations(version,description)
VALUES ('2026_07_30_stage54_cold_chain_device_integrity',
        'Cold-chain device normalized identity and idempotent retry uniqueness')
ON CONFLICT(version) DO NOTHING;

COMMIT;
