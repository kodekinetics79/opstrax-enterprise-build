-- ─────────────────────────────────────────────────────────────────────────────
-- Grant the columns GET /api/telemetry/gateways reads — everything but the secret
--
-- SYMPTOM
--   GET /api/telemetry/gateways -> 500, "42501: permission denied for table
--   telemetry_gateways".
--
-- CAUSE — the same trap as eld_devices, and self-inflicted the same way
--   Script 03 granted opstrax_app exactly what FleetProductionReadinessService asserts:
--   SELECT on gateway_id, UPDATE on status, and nothing on secret_encrypted. Those are
--   the contract's *assertions*, not the endpoint's *needs*. Because the table carries no
--   table-level SELECT for opstrax_app, every column outside that grant is unreadable,
--   and TelemetryGatewayList selects six:
--       SELECT id, gateway_id, gateway_name, status, last_seen_at, created_at
--   so the read failed on id/gateway_name/last_seen_at/created_at.
--
--   LESSON (now twice): on a table with column-level grants, satisfying a schema
--   contract is not the same as satisfying the queries. Check what the handlers select.
--
-- WHAT IT DOES
--   Grants SELECT on every column EXCEPT secret_encrypted — the same "all but the secret
--   material" rule stage76 applies to eld_devices, and expressed the same way so it is
--   self-correcting if columns are added later.
--
--   The readiness contract still passes: it requires SELECT on gateway_id (granted),
--   UPDATE on status (already granted by script 03), and NO SELECT or UPDATE on
--   secret_encrypted (still withheld). The HMAC material remains readable only in
--   system scope.
--
-- RUN:  psql "$NEON_PG_URI" -f tools/pt40/11-gateway-list-column-grants.sql
--
-- Idempotent. Grants only; nothing is revoked.
-- ─────────────────────────────────────────────────────────────────────────────

SET lock_timeout = '3s';

DO $$
DECLARE
  granted_columns TEXT;
BEGIN
  SELECT string_agg(quote_ident(column_name), ',' ORDER BY ordinal_position)
    INTO granted_columns
    FROM information_schema.columns
   WHERE table_schema='public' AND table_name='telemetry_gateways'
     AND column_name <> 'secret_encrypted';

  IF granted_columns IS NULL THEN
    RAISE EXCEPTION 'Could not build a safe telemetry_gateways column grant';
  END IF;

  EXECUTE format('GRANT SELECT (%s) ON TABLE public.telemetry_gateways TO opstrax_app',
                 granted_columns);
END $$;

-- ── Verification ───────────────────────────────────────────────────────────
\echo ''
\echo '== columns TelemetryGatewayList selects (expect all t) =='
SELECT column_name,
       has_column_privilege('opstrax_app','telemetry_gateways',column_name,'SELECT') AS app_can_select
FROM information_schema.columns
WHERE table_schema='public' AND table_name='telemetry_gateways'
  AND column_name IN ('id','gateway_id','gateway_name','status','last_seen_at','created_at')
ORDER BY column_name;

\echo ''
\echo '== the secret MUST stay unreadable, and the readiness contract MUST still hold =='
SELECT has_column_privilege('opstrax_app','telemetry_gateways','secret_encrypted','SELECT') AS app_reads_secret_MUST_BE_FALSE,
       has_column_privilege('opstrax_app','telemetry_gateways','secret_encrypted','UPDATE') AS app_writes_secret_MUST_BE_FALSE,
       has_column_privilege('opstrax_app','telemetry_gateways','gateway_id','SELECT')       AS contract_reads_gateway_id_MUST_BE_TRUE,
       has_column_privilege('opstrax_app','telemetry_gateways','status','UPDATE')           AS contract_updates_status_MUST_BE_TRUE;
