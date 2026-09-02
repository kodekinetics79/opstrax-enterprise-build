-- ─────────────────────────────────────────────────────────────────────────────
-- Re-grant column-level SELECT on eld_devices after adding columns
--
-- WHY — this one is self-inflicted, and worth understanding before it happens again
--   eld_devices is the ONLY telemetry table where opstrax_app has no table-level
--   SELECT. Its ACL is {opstrax_app=awd} — INSERT/UPDATE/DELETE but NOT read — because
--   stage76 grants read access COLUMN BY COLUMN, deliberately withholding the five
--   secret-bearing columns so tenant reads can never see HMAC or API-key material.
--
--   Column-level grants do not extend to columns created later. Script 05 added 8 new
--   columns (device_category, provider_external_id, manufacturer, sim_iccid, sim_imsi,
--   commissioned_at, retired_at, notes), and every one of them landed ungranted. The
--   device list selects device_category, so GET /api/telemetry/devices began failing
--   with "42501: permission denied for table eld_devices" — a *worse-looking* error
--   than the missing column it replaced, from a fix that was otherwise correct.
--
--   LESSON: after ALTER TABLE ... ADD COLUMN on a table with column-level grants,
--   the grants must be recomputed. Check pg_class.relacl for the table first.
--
-- WHAT IT DOES
--   Re-runs stage76's own grant computation (2026_08_11_stage76:215-223) against the
--   CURRENT column set. Because it is expressed as "every column except the secret
--   ones", it repairs the columns added by script 05 and is self-correcting for any
--   column added in future. The exclusion list is copied verbatim — do not shorten it.
--
-- RUN:  psql "$NEON_PG_URI" -f tools/pt40/09-eld-devices-column-grants.sql
--
-- Idempotent. Grants only; nothing is revoked, no data is touched.
-- ─────────────────────────────────────────────────────────────────────────────

SET lock_timeout = '3s';

DO $$
DECLARE
  granted_columns TEXT;
BEGIN
  -- Tenant reads receive inventory/lifecycle metadata, never hashes, plaintext, current
  -- encrypted material, or grace-period encrypted material.
  SELECT string_agg(quote_ident(column_name), ',' ORDER BY ordinal_position)
    INTO granted_columns
    FROM information_schema.columns
   WHERE table_schema='public' AND table_name='eld_devices'
     AND column_name NOT IN (
       'api_key_hash','api_key_previous_hash','hmac_secret',
       'hmac_secret_encrypted','hmac_previous_secret_encrypted');

  IF granted_columns IS NULL THEN
    RAISE EXCEPTION 'Could not build a safe eld_devices column grant';
  END IF;

  EXECUTE format('GRANT SELECT (%s) ON TABLE public.eld_devices TO opstrax_app', granted_columns);
END $$;

-- ── Verification ───────────────────────────────────────────────────────────
\echo ''
\echo '== the 8 columns added by script 05 (expect all t) =='
SELECT column_name,
       has_column_privilege('opstrax_app','eld_devices',column_name,'SELECT') AS app_can_select
FROM information_schema.columns
WHERE table_schema='public' AND table_name='eld_devices'
  AND column_name IN ('device_category','provider_external_id','manufacturer','sim_iccid',
                      'sim_imsi','commissioned_at','retired_at','notes')
ORDER BY column_name;

\echo ''
\echo '== secret columns MUST all remain f =='
SELECT column_name,
       has_column_privilege('opstrax_app','eld_devices',column_name,'SELECT') AS app_can_select
FROM information_schema.columns
WHERE table_schema='public' AND table_name='eld_devices'
  AND column_name IN ('api_key_hash','api_key_previous_hash','hmac_secret',
                      'hmac_secret_encrypted','hmac_previous_secret_encrypted')
ORDER BY column_name;
