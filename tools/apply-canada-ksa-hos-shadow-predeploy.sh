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

migration="database/migrations/2026_09_03_stage102_hos_policy_shadow_engine.sql"
[ -f "$migration" ] || { echo "ERROR: missing $migration" >&2; exit 1; }

psql_neon() { python3 tools/psql-neon-env.py "$@"; }

applied=$(psql_neon -tA -c "SELECT COUNT(*) FROM schema_migrations WHERE version='2026_09_03_stage102_hos_policy_shadow_engine'" 2>/dev/null || echo 0)
if [ "$applied" != "1" ]; then
  echo "── applying Stage102 HOS policy/shadow evidence schema"
  psql_neon -v ON_ERROR_STOP=1 -q -f "$migration"
  psql_neon -v ON_ERROR_STOP=1 -q -c "INSERT INTO schema_migrations(version,description) VALUES ('2026_09_03_stage102_hos_policy_shadow_engine','Canada/KSA HOS policy + shadow calculation evidence') ON CONFLICT (version) DO NOTHING"
else
  echo "── Stage102 already applied (ledger)"
fi

psql_neon -v ON_ERROR_STOP=1 <<'SQL'
DO $verify_stage102$
BEGIN
  IF to_regclass('public.driver_hos_policy_assignments') IS NULL THEN
    RAISE EXCEPTION 'Stage102 verification failed: driver_hos_policy_assignments';
  END IF;
  IF to_regclass('public.hos_exception_authorizations') IS NULL THEN
    RAISE EXCEPTION 'Stage102 verification failed: hos_exception_authorizations';
  END IF;
  IF to_regclass('public.hos_shadow_clock_snapshots') IS NULL THEN
    RAISE EXCEPTION 'Stage102 verification failed: hos_shadow_clock_snapshots';
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname='trg_hos_shadow_no_update' AND NOT tgisinternal) THEN
    RAISE EXCEPTION 'Stage102 verification failed: append-only shadow evidence trigger';
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_indexes WHERE indexname='uq_driver_hos_policy_current') THEN
    RAISE EXCEPTION 'Stage102 verification failed: one-current-policy uniqueness';
  END IF;
  IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='hos_logs' AND column_name='provenance_verified') THEN
    RAISE EXCEPTION 'Stage102 verification failed: HOS provenance columns';
  END IF;
END
$verify_stage102$;
SQL

echo "Stage102 HOS policy/shadow schema: VERIFIED"
echo "Mode: SHADOW ONLY — legacy/live hos_clocks are not overwritten"
echo "Regulatory/provider/device/field acceptance: STILL REQUIRED"
