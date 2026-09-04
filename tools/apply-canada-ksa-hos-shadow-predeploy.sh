#!/usr/bin/env bash
# Canada/KSA HOS shadow-engine predeploy chain.
#
# This intentionally does NOT replace the production release command yet.
# It is the controlled migration/test entry point for PR #202. Promotion into the
# production release workflow requires Regulatory + SDET acceptance of shadow
# results against authentic provider/device data.
set -euo pipefail

if [ -z "${NEON_PG_URI:-}" ]; then
  echo "ERROR: set NEON_PG_URI before running this script." >&2
  exit 1
fi

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

bash ./tools/apply-canada-ksa-compliance-predeploy.sh

psql_neon() { python3 tools/psql-neon-env.py "$@"; }

apply_migration() {
  local version="$1"
  local file="$2"
  local description="$3"
  [ -f "$file" ] || { echo "ERROR: missing $file" >&2; exit 1; }
  local applied
  applied=$(psql_neon -tA -c "SELECT COUNT(*) FROM schema_migrations WHERE version='${version}'" 2>/dev/null || echo 0)
  if [ "$applied" != "1" ]; then
    echo "── applying ${version}"
    psql_neon -v ON_ERROR_STOP=1 -q -f "$file"
    psql_neon -v ON_ERROR_STOP=1 -q -c "INSERT INTO schema_migrations(version,description) VALUES ('${version}','${description}') ON CONFLICT (version) DO NOTHING"
  else
    echo "── ${version} already applied (ledger)"
  fi
}

apply_migration \
  "2026_09_03_stage102_hos_policy_shadow_engine" \
  "database/migrations/2026_09_03_stage102_hos_policy_shadow_engine.sql" \
  "Canada/KSA HOS policy + shadow calculation evidence"

apply_migration \
  "2026_09_03_stage103_hos_shadow_retention_control" \
  "database/migrations/2026_09_03_stage103_hos_shadow_retention_control.sql" \
  "HOS shadow evidence controlled retention/offboarding"

psql_neon -v ON_ERROR_STOP=1 <<'SQL'
DO $verify_shadow$
DECLARE isolated_count INTEGER;
BEGIN
  IF to_regclass('public.driver_hos_policy_assignments') IS NULL THEN
    RAISE EXCEPTION 'HOS shadow verification failed: driver_hos_policy_assignments';
  END IF;
  IF to_regclass('public.hos_exception_authorizations') IS NULL THEN
    RAISE EXCEPTION 'HOS shadow verification failed: hos_exception_authorizations';
  END IF;
  IF to_regclass('public.hos_shadow_clock_snapshots') IS NULL THEN
    RAISE EXCEPTION 'HOS shadow verification failed: hos_shadow_clock_snapshots';
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname='trg_hos_shadow_no_update' AND NOT tgisinternal) THEN
    RAISE EXCEPTION 'HOS shadow verification failed: mutation/retention trigger';
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_indexes WHERE indexname='uq_driver_hos_policy_current') THEN
    RAISE EXCEPTION 'HOS shadow verification failed: one-current-policy uniqueness';
  END IF;
  IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='hos_logs' AND column_name='provenance_verified') THEN
    RAISE EXCEPTION 'HOS shadow verification failed: HOS provenance columns';
  END IF;
  SELECT COUNT(*) INTO isolated_count
    FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
   WHERE n.nspname='public'
     AND c.relname IN ('driver_hos_policy_assignments','hos_exception_authorizations','hos_shadow_clock_snapshots')
     AND c.relrowsecurity AND c.relforcerowsecurity;
  IF isolated_count <> 3 THEN
    RAISE EXCEPTION 'HOS shadow verification failed: all evidence tables must FORCE RLS';
  END IF;
END
$verify_shadow$;
SQL

echo "Stage102+103 HOS policy/shadow/retention chain: VERIFIED"
echo "Mode: SHADOW ONLY — legacy/live hos_clocks are not overwritten"
echo "Regulatory/provider/device/field acceptance: STILL REQUIRED"
