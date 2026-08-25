#!/usr/bin/env bash
set -euo pipefail

: "${OPSTRAX_TEST_DB_HOST:=127.0.0.1}"
: "${OPSTRAX_TEST_DB_PORT:=59955}"
: "${OPSTRAX_TEST_DB_USER:=zayra}"
: "${OPSTRAX_TEST_DB_PASSWORD:?Set OPSTRAX_TEST_DB_PASSWORD for the disposable owner identity}"
: "${OPSTRAX_TEST_DB_APP_PASSWORD:?Set OPSTRAX_TEST_DB_APP_PASSWORD for the disposable app identity}"
: "${OPSTRAX_TEST_DB_SYSTEM_PASSWORD:?Set OPSTRAX_TEST_DB_SYSTEM_PASSWORD for the disposable system identity}"

for command_name in createdb dropdb psql openssl curl jq dotnet; do
  command -v "$command_name" >/dev/null || {
    echo "ERROR: required command is unavailable: $command_name" >&2
    exit 1
  }
done

rehearsal_db="opstrax_prod_rehearsal_${$}"
case "$rehearsal_db" in
  opstrax_prod_rehearsal_[0-9]*) ;;
  *) echo "Unsafe rehearsal database name" >&2; exit 2 ;;
esac

rehearsal_port=$((18000 + ($$ % 1000)))
rehearsal_tmp=$(mktemp -d /tmp/opstrax-prod-rehearsal.XXXXXX)
case "$rehearsal_tmp" in
  /tmp/opstrax-prod-rehearsal.*) ;;
  *) echo "Unsafe rehearsal temporary directory" >&2; exit 2 ;;
esac
api_pid=""
rehearsal_stage="initialization"
export PGPASSWORD="$OPSTRAX_TEST_DB_PASSWORD"

report_failure() {
  status=$?
  echo "ERROR: Production-shaped rehearsal failed during: $rehearsal_stage" >&2
  exit "$status"
}
trap report_failure ERR

cleanup() {
  if [ -n "$api_pid" ] && kill -0 "$api_pid" >/dev/null 2>&1; then
    kill -TERM "$api_pid" >/dev/null 2>&1 || true
    wait "$api_pid" >/dev/null 2>&1 || true
  fi
  dropdb --if-exists --force -h "$OPSTRAX_TEST_DB_HOST" -p "$OPSTRAX_TEST_DB_PORT" \
    -U "$OPSTRAX_TEST_DB_USER" "$rehearsal_db" >/dev/null 2>&1 || true
  rm -rf "$rehearsal_tmp"
}
trap cleanup EXIT

createdb -h "$OPSTRAX_TEST_DB_HOST" -p "$OPSTRAX_TEST_DB_PORT" \
  -U "$OPSTRAX_TEST_DB_USER" "$rehearsal_db"

rehearsal_stage="predecessor schema materialization"
for fixture in \
  database/init/001_schema.sql \
  database/init/002_seed.sql \
  database/init/004_jobs_execution.sql \
  database/migrations/2026_06_30_stage19_row_level_security.sql \
  database/migrations/2026_06_30_stage20_rls_force_and_app_role.sql \
  database/migrations/2026_07_01_stage22_rls_reconcile_coverage.sql
do
  psql -h "$OPSTRAX_TEST_DB_HOST" -p "$OPSTRAX_TEST_DB_PORT" \
    -U "$OPSTRAX_TEST_DB_USER" -d "$rehearsal_db" -v ON_ERROR_STOP=1 -q -f "$fixture"
done

owner_uri="postgresql://${OPSTRAX_TEST_DB_USER}:${OPSTRAX_TEST_DB_PASSWORD}@${OPSTRAX_TEST_DB_HOST}:${OPSTRAX_TEST_DB_PORT}/${rehearsal_db}?sslmode=disable"
rehearsal_stage="owner predeploy migration chain"
NEON_PG_URI="$owner_uri" tools/apply-neon-predeploy-migrations.sh \
  >"$rehearsal_tmp/predeploy.log" 2>&1

app_connection="Host=${OPSTRAX_TEST_DB_HOST};Port=${OPSTRAX_TEST_DB_PORT};Database=${rehearsal_db};Username=opstrax_app;Password=${OPSTRAX_TEST_DB_APP_PASSWORD};SSL Mode=Disable"
system_connection="Host=${OPSTRAX_TEST_DB_HOST};Port=${OPSTRAX_TEST_DB_PORT};Database=${rehearsal_db};Username=opstrax_system;Password=${OPSTRAX_TEST_DB_SYSTEM_PASSWORD};SSL Mode=Disable"
owner_connection="Host=${OPSTRAX_TEST_DB_HOST};Port=${OPSTRAX_TEST_DB_PORT};Database=${rehearsal_db};Username=${OPSTRAX_TEST_DB_USER};Password=${OPSTRAX_TEST_DB_PASSWORD};SSL Mode=Disable"

rehearsal_stage="restricted database identity verification"
PGPASSWORD="$OPSTRAX_TEST_DB_APP_PASSWORD" psql -h "$OPSTRAX_TEST_DB_HOST" \
  -p "$OPSTRAX_TEST_DB_PORT" -U opstrax_app -d "$rehearsal_db" -v ON_ERROR_STOP=1 \
  -Atc "SELECT current_user || ':' || rolsuper::int || ':' || rolbypassrls::int FROM pg_roles WHERE rolname=current_user" \
  | grep -qx 'opstrax_app:0:0'
PGPASSWORD="$OPSTRAX_TEST_DB_SYSTEM_PASSWORD" psql -h "$OPSTRAX_TEST_DB_HOST" \
  -p "$OPSTRAX_TEST_DB_PORT" -U opstrax_system -d "$rehearsal_db" -v ON_ERROR_STOP=1 \
  -Atc "SELECT current_user || ':' || rolsuper::int || ':' || rolbypassrls::int FROM pg_roles WHERE rolname=current_user" \
  | grep -qx 'opstrax_system:0:0'

dp_password=$(openssl rand -base64 24 | tr -d '\n')
openssl req -x509 -newkey rsa:2048 -sha256 -nodes -days 30 \
  -subj '/CN=opstrax-local-production-rehearsal' \
  -keyout "$rehearsal_tmp/dp.key" -out "$rehearsal_tmp/dp.crt" \
  >/dev/null 2>&1
openssl pkcs12 -export -out "$rehearsal_tmp/dp.pfx" \
  -inkey "$rehearsal_tmp/dp.key" -in "$rehearsal_tmp/dp.crt" \
  -passout "pass:${dp_password}" >/dev/null 2>&1
dp_certificate=$(openssl base64 -A -in "$rehearsal_tmp/dp.pfx")
jwt_key=$(openssl rand -base64 64 | tr -d '\n')
data_key=$(openssl rand -base64 32 | tr -d '\n')
sse_key=$(openssl rand -base64 48 | tr -d '\n')
platform_password=$(openssl rand -base64 24 | tr -d '\n')
# /health/deep and /metrics are gated (security review): probes must present the
# configured diagnostics key via the X-Diagnostics-Key header.
diagnostics_key=$(openssl rand -base64 32 | tr -d '\n')

rehearsal_stage="candidate restore and build"
dotnet restore backend-dotnet.Tests/Opstrax.Tests.csproj \
  >"$rehearsal_tmp/restore.log" 2>&1
dotnet build backend-dotnet.Tests/Opstrax.Tests.csproj --no-restore --verbosity quiet \
  >"$rehearsal_tmp/build.log" 2>&1

(
  cd backend-dotnet
  exec env \
    ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS="http://127.0.0.1:${rehearsal_port}" \
    ConnectionStrings__DefaultConnection="$app_connection" \
    ConnectionStrings__SystemConnection="$system_connection" \
    Rls__EnforceTenantContext=true \
    Rls__TenantTicketTtlSeconds=120 \
    Jwt__Key="$jwt_key" \
    DataProtection__CertificateBase64="$dp_certificate" \
    DataProtection__CertificatePassword="$dp_password" \
    DATA_ENCRYPTION_KEY="$data_key" \
    Telemetry__SseTicketKey="$sse_key" \
    PLATFORM_SUPERADMIN_PASSWORD="$platform_password" \
    DIAGNOSTICS_KEY="$diagnostics_key" \
    DemoSeed__Enabled=false \
    ENABLE_FLEET_DEMO_SEED=false \
    Telemetry__Simulator__Enabled=false \
    OutboxDispatcher__Enabled=true \
    OutboxDispatcher__AllowProduction=true \
    RetentionWorker__Enabled=true \
    Cors__AllowedOrigins="https://pilot.example.invalid" \
    ./bin/Debug/net8.0/Opstrax.Api
) >"$rehearsal_tmp/api.log" 2>&1 &
api_pid=$!

rehearsal_stage="Production API startup and health contracts"
live_url="http://127.0.0.1:${rehearsal_port}/health/live"
ready_url="http://127.0.0.1:${rehearsal_port}/health/ready"
deep_url="http://127.0.0.1:${rehearsal_port}/health/deep"
for _ in $(seq 1 60); do
  if curl -fsS "$live_url" >"$rehearsal_tmp/live.json" 2>/dev/null; then break; fi
  if ! kill -0 "$api_pid" >/dev/null 2>&1; then
    echo "ERROR: Production rehearsal API exited during startup" >&2
    tail -80 "$rehearsal_tmp/api.log" >&2
    exit 1
  fi
  sleep 1
done
curl -fsS "$live_url" >"$rehearsal_tmp/live.json"
for _ in $(seq 1 150); do
  curl -sS "$ready_url" >"$rehearsal_tmp/ready.json"
  curl -sS -H "X-Diagnostics-Key: $diagnostics_key" "$deep_url" >"$rehearsal_tmp/deep.json"
  if jq -e '.status=="ready"' "$rehearsal_tmp/ready.json" >/dev/null 2>&1 \
    && jq -e '.checks.critical_worker_contract.status=="healthy"' "$rehearsal_tmp/deep.json" >/dev/null 2>&1; then
    break
  fi
  if jq -e '.checks.services[] | select(.name=="RetentionEnforcementService" and .status=="degraded")' \
    "$rehearsal_tmp/deep.json" >/dev/null 2>&1; then
    break
  fi
  sleep 1
done

if ! jq -e '.checks.critical_worker_contract.status=="healthy"' "$rehearsal_tmp/deep.json" >/dev/null 2>&1; then
  jq '{status, failure_reason, critical_worker_contract:.checks.critical_worker_contract,
       retention_service:[.checks.services[] | select(.name=="RetentionEnforcementService")]}' \
    "$rehearsal_tmp/deep.json" >&2
  grep -E '\[Retention\]|retention_[a-z_]+' "$rehearsal_tmp/api.log" | tail -80 >&2 || true
  exit 1
fi

jq -e '.status=="alive" and .environment=="Production"' "$rehearsal_tmp/live.json" >/dev/null
jq -e '.status=="ready" and .environment=="Production"
  and .checks.config.failures==0
  and .checks.data_protection_key_ring.status=="ready"
  and .checks.fleet_production_contract.status=="ready"
  and .checks.fleet_production_contract.role_restricted==true
  and .checks.fleet_production_contract.rls_violations==0
  and .checks.fleet_production_contract.grant_violations==0
  and .checks.fleet_production_contract.tenant_coverage_violations==0' \
  "$rehearsal_tmp/ready.json" >/dev/null
jq -e '.status=="healthy" and .environment=="Production"
  and .checks.config.failures==0
  and .checks.data_protection_key_ring.status=="ready"
  and .checks.fleet_production_contract.status=="ready"
  and .checks.critical_worker_contract.status=="healthy"
  and .checks.critical_worker_contract.expected_count==8
  and ([.checks.services[] | select(.name=="RetentionEnforcementService" and .status=="healthy")] | length)==1' \
  "$rehearsal_tmp/deep.json" >/dev/null

rehearsal_stage="restricted identity and tenant-isolation suite"
if ! OPSTRAX_TEST_DB="$owner_connection" \
OPSTRAX_TEST_DB_APP="$app_connection" \
OPSTRAX_TEST_DB_SYSTEM="$system_connection" \
dotnet test backend-dotnet.Tests/Opstrax.Tests.csproj --no-build --no-restore \
  --filter 'FullyQualifiedName~ProductionIdentityProof_ValidatesExactRolesAndCrossPoolTicketBridge|FullyQualifiedName~SignedScopes_IsolateTwoTenants_UnderConcurrentPoolReuse|FullyQualifiedName~HosReadsRequireDirectPermissionAndHideOtherBranches|FullyQualifiedName~EldMalfunctionHistoryRlsUsesSignedTenantTicket|FullyQualifiedName~HosImmutabilityAllowsOnlyDualGatedSystemOffboardingDeletes|FullyQualifiedName~TenantOffboardingPostgresTests' \
  --verbosity minimal >"$rehearsal_tmp/isolation-tests.log" 2>&1; then
  cat "$rehearsal_tmp/isolation-tests.log" >&2
  exit 1
fi

rehearsal_stage="terminal database evidence assertions"
psql "$owner_uri" -v ON_ERROR_STOP=1 -At <<'SQL' >"$rehearsal_tmp/database-evidence.txt"
SELECT 'migration_ledgers=' || count(*)
FROM schema_migrations
WHERE version IN (
  '2026_07_16_stage42_telemetry_gateways',
  '2026_07_22_stage47_detention_recovery',
  '2026_07_31_stage58_nonforgeable_tenant_ticket',
  '2026_07_31_stage59_data_protection_key_ring',
  '2026_08_01_stage65_safety_pilot',
  '2026_08_02_stage66_telematics_pilot',
  '2026_08_02_stage67_telematics_diagnostics_integrity',
  '2026_08_02_stage68_entitlement_policy_mode',
  '2026_08_02_stage69_market_pack_control_hardening',
  '2026_08_02_stage70_hos_pilot_schema_reconciliation',
  '2026_08_02_stage71_coaching_evidence_reconciliation',
  '2026_08_02_stage72_hos_offboarding_immutability_reconciliation',
  '2026_08_02_stage73_hos_offboarding_null_fail_closed',
  '2026_08_02_stage74_retention_policy_production_contract',
  '2026_08_02_stage75_bounded_support_access',
  '2026_08_11_stage76_telematics_security_hardening');
SELECT 'public_policies=' || count(*) FROM pg_policies
WHERE schemaname='public' AND roles='{public}'::name[];
SELECT 'unsafe_runtime_roles=' || count(*) FROM pg_roles
WHERE rolname IN ('opstrax_app','opstrax_system') AND (rolsuper OR rolbypassrls OR rolcreatedb OR rolcreaterole);
SELECT 'stage76_secret_read_violations=' || count(*)
FROM (VALUES
  ('api_key_hash'),('api_key_previous_hash'),('hmac_secret'),
  ('hmac_secret_encrypted'),('hmac_previous_secret_encrypted')) credential(column_name)
WHERE has_column_privilege('opstrax_app','eld_devices',credential.column_name,'SELECT');
SELECT 'stage76_default_acl_violations=' || count(*)
FROM pg_default_acl defaults
LEFT JOIN pg_namespace namespace ON namespace.oid=defaults.defaclnamespace
CROSS JOIN LATERAL aclexplode(defaults.defaclacl) privilege
LEFT JOIN pg_roles grantee ON grantee.oid=privilege.grantee
WHERE (defaults.defaclnamespace=0 OR namespace.nspname='public')
  AND defaults.defaclobjtype IN ('r','S')
  AND (privilege.grantee=0 OR grantee.rolname IN ('opstrax_app','opstrax_system'));
SELECT 'stage76_runtime_acl_violations=' || count(*)
FROM (VALUES
  ('opstrax_app','eld_devices','SELECT',FALSE),
  ('opstrax_app','eld_devices','INSERT',FALSE),
  ('opstrax_app','eld_devices','UPDATE',FALSE),
  ('opstrax_app','eld_devices','DELETE',FALSE),
  ('opstrax_app','telemetry_gateways','SELECT',FALSE),
  ('opstrax_app','telemetry_gateways','INSERT',FALSE),
  ('opstrax_app','telemetry_gateways','UPDATE',FALSE),
  ('opstrax_app','telemetry_gateways','DELETE',FALSE),
  ('opstrax_app','telemetry_stream_ticket_nonces','SELECT',FALSE),
  ('opstrax_app','telemetry_stream_ticket_nonces','INSERT',TRUE),
  ('opstrax_app','telemetry_stream_ticket_nonces','UPDATE',FALSE),
  ('opstrax_app','telemetry_stream_ticket_nonces','DELETE',FALSE),
  ('opstrax_app','telemetry_replay_seen','SELECT',FALSE),
  ('opstrax_app','telemetry_replay_seen','INSERT',FALSE),
  ('opstrax_app','telemetry_replay_seen','UPDATE',FALSE),
  ('opstrax_app','telemetry_replay_seen','DELETE',FALSE),
  ('opstrax_app','telemetry_replay_device_state','SELECT',FALSE),
  ('opstrax_app','telemetry_replay_device_state','INSERT',FALSE),
  ('opstrax_app','telemetry_replay_device_state','UPDATE',FALSE),
  ('opstrax_app','telemetry_replay_device_state','DELETE',FALSE),
  ('opstrax_app','canonical_telemetry_events','SELECT',FALSE),
  ('opstrax_app','canonical_telemetry_events','INSERT',FALSE),
  ('opstrax_app','canonical_telemetry_events','UPDATE',FALSE),
  ('opstrax_app','canonical_telemetry_events','DELETE',FALSE),
  ('opstrax_system','telemetry_nonces','SELECT',TRUE),
  ('opstrax_system','telemetry_nonces','INSERT',TRUE),
  ('opstrax_system','telemetry_nonces','UPDATE',FALSE),
  ('opstrax_system','telemetry_nonces','DELETE',TRUE),
  ('opstrax_system','gps_gateway_replay','SELECT',TRUE),
  ('opstrax_system','gps_gateway_replay','INSERT',TRUE),
  ('opstrax_system','gps_gateway_replay','UPDATE',FALSE),
  ('opstrax_system','gps_gateway_replay','DELETE',TRUE),
  ('opstrax_system','telemetry_gateway_rejections','SELECT',TRUE),
  ('opstrax_system','telemetry_gateway_rejections','INSERT',TRUE),
  ('opstrax_system','telemetry_gateway_rejections','UPDATE',FALSE),
  ('opstrax_system','telemetry_gateway_rejections','DELETE',TRUE),
  ('opstrax_system','telemetry_replay_seen','SELECT',TRUE),
  ('opstrax_system','telemetry_replay_seen','INSERT',TRUE),
  ('opstrax_system','telemetry_replay_seen','UPDATE',FALSE),
  ('opstrax_system','telemetry_replay_seen','DELETE',TRUE),
  ('opstrax_system','telemetry_replay_device_state','SELECT',TRUE),
  ('opstrax_system','telemetry_replay_device_state','INSERT',TRUE),
  ('opstrax_system','telemetry_replay_device_state','UPDATE',TRUE),
  ('opstrax_system','telemetry_replay_device_state','DELETE',FALSE),
  ('opstrax_system','telemetry_projection_inbox','SELECT',TRUE),
  ('opstrax_system','telemetry_projection_inbox','INSERT',TRUE),
  ('opstrax_system','telemetry_projection_inbox','UPDATE',FALSE),
  ('opstrax_system','telemetry_projection_inbox','DELETE',TRUE),
  ('opstrax_system','canonical_telemetry_events','SELECT',TRUE),
  ('opstrax_system','canonical_telemetry_events','INSERT',TRUE),
  ('opstrax_system','canonical_telemetry_events','UPDATE',FALSE),
  ('opstrax_system','canonical_telemetry_events','DELETE',TRUE)
) expected(role_name,table_name,privilege_name,allowed)
WHERE has_table_privilege(
  expected.role_name::name,
  'public.'||expected.table_name,
  expected.privilege_name
)<>expected.allowed;
SELECT 'stage76_app_column_acl_violations=' || count(*)
FROM (VALUES
  ('telemetry_replay_seen'),
  ('telemetry_replay_device_state'),
  ('canonical_telemetry_events')
) expected(table_name)
WHERE has_any_column_privilege(
  'opstrax_app','public.'||expected.table_name,'SELECT,INSERT,UPDATE,REFERENCES'
);
SELECT 'stage76_lifecycle_update_violations=' || count(*)
FROM (VALUES
  ('malfunction_resolved_at',TRUE),
  ('malfunction_resolved_by',TRUE),
  ('resolution_evidence',TRUE),
  ('row_version',TRUE),
  ('id',FALSE),
  ('company_id',FALSE),
  ('api_key_hash',FALSE),
  ('api_key_previous_hash',FALSE),
  ('hmac_secret',FALSE),
  ('hmac_secret_encrypted',FALSE),
  ('hmac_previous_secret_encrypted',FALSE)
) expected(column_name,allowed)
WHERE has_column_privilege(
  'opstrax_app','public.eld_devices',expected.column_name,'UPDATE'
)<>expected.allowed;
SELECT 'stage76_sequence_acl_violations=' || count(*)
FROM (VALUES
  ('opstrax_app','telemetry_replay_seen_id_seq','USAGE',FALSE),
  ('opstrax_app','telemetry_replay_seen_id_seq','SELECT',FALSE),
  ('opstrax_app','telemetry_replay_seen_id_seq','UPDATE',FALSE),
  ('opstrax_system','telemetry_replay_seen_id_seq','USAGE',TRUE),
  ('opstrax_system','telemetry_replay_seen_id_seq','SELECT',TRUE),
  ('opstrax_system','telemetry_replay_seen_id_seq','UPDATE',FALSE),
  ('opstrax_app','canonical_telemetry_events_id_seq','USAGE',FALSE),
  ('opstrax_app','canonical_telemetry_events_id_seq','SELECT',FALSE),
  ('opstrax_app','canonical_telemetry_events_id_seq','UPDATE',FALSE),
  ('opstrax_system','canonical_telemetry_events_id_seq','USAGE',TRUE),
  ('opstrax_system','canonical_telemetry_events_id_seq','SELECT',TRUE),
  ('opstrax_system','canonical_telemetry_events_id_seq','UPDATE',FALSE)
) expected(role_name,sequence_name,privilege_name,allowed)
WHERE has_sequence_privilege(
  expected.role_name::name,
  'public.'||expected.sequence_name,
  expected.privilege_name
)<>expected.allowed;
SELECT 'stage76_replay_schema_violations=' || CASE WHEN
  to_regclass('public.telemetry_replay_device_state') IS NULL
  OR NOT EXISTS (
    SELECT 1 FROM pg_constraint
    WHERE conname='uq_telemetry_replay_seen_unwrapped'
      AND conrelid='public.telemetry_replay_seen'::regclass
      AND contype='u'
      AND pg_get_constraintdef(oid)='UNIQUE (device_id, unwrapped_serial, content_hash)'
  ) OR EXISTS (
    SELECT 1 FROM (VALUES
      ('telemetry_replay_seen','unwrapped_serial'),
      ('telemetry_replay_seen','event_id'),
      ('telemetry_replay_device_state','device_id'),
      ('telemetry_replay_device_state','last_raw_serial'),
      ('telemetry_replay_device_state','high_water_unwrapped'),
      ('telemetry_replay_device_state','updated_at')
    ) required(table_name,column_name)
    WHERE NOT EXISTS (
      SELECT 1 FROM information_schema.columns actual
      WHERE actual.table_schema='public'
        AND actual.table_name=required.table_name
        AND actual.column_name=required.column_name
        AND actual.is_nullable='NO'
    )
  ) OR EXISTS (SELECT 1 FROM telemetry_replay_device_state)
  THEN 1 ELSE 0 END;
SQL
grep -qx 'migration_ledgers=16' "$rehearsal_tmp/database-evidence.txt"
grep -qx 'public_policies=0' "$rehearsal_tmp/database-evidence.txt"
grep -qx 'unsafe_runtime_roles=0' "$rehearsal_tmp/database-evidence.txt"
grep -qx 'stage76_secret_read_violations=0' "$rehearsal_tmp/database-evidence.txt"
grep -qx 'stage76_default_acl_violations=0' "$rehearsal_tmp/database-evidence.txt"
grep -qx 'stage76_runtime_acl_violations=0' "$rehearsal_tmp/database-evidence.txt"
grep -qx 'stage76_app_column_acl_violations=0' "$rehearsal_tmp/database-evidence.txt"
grep -qx 'stage76_lifecycle_update_violations=0' "$rehearsal_tmp/database-evidence.txt"
grep -qx 'stage76_sequence_acl_violations=0' "$rehearsal_tmp/database-evidence.txt"
grep -qx 'stage76_replay_schema_violations=0' "$rehearsal_tmp/database-evidence.txt"

trap - ERR
echo "Production-shaped local rehearsal passed:"
echo "  owner migrations + Stage76 terminal reconciliation: passed"
echo "  restricted identities: opstrax_app + opstrax_system"
echo "  /health/live, /health/ready, /health/deep: 200 and contract-valid"
echo "  signed-ticket tenant isolation + branch isolation: focused tests passed"
echo "  migration ledgers: 16/16; PUBLIC policies: 0; unsafe runtime roles: 0; Stage76 ACL violations: 0"
