#!/usr/bin/env bash
# Canada/KSA regulated-pilot predeploy wrapper.
#
# Runs the canonical protected-environment migration chain first, then Stage101.
# This exists so the compliance baseline cannot be treated as a documentation-only
# change while the canonical runner enrollment is reviewed. Once Stage101 is
# enrolled directly in apply-neon-predeploy-migrations.sh, this wrapper can remain
# as a pilot-specific verification entry point.
set -euo pipefail

if [ -z "${NEON_PG_URI:-}" ]; then
  echo "ERROR: set NEON_PG_URI before running this script." >&2
  exit 1
fi

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

./tools/apply-neon-predeploy-migrations.sh

migration="database/migrations/2026_09_03_stage101_canada_ksa_compliance_baseline.sql"
[ -f "$migration" ] || { echo "ERROR: missing $migration" >&2; exit 1; }

psql_neon() { python3 tools/psql-neon-env.py "$@"; }

applied=$(psql_neon -tA -c "SELECT CASE WHEN to_regclass('public.schema_migrations') IS NOT NULL THEN COUNT(*) ELSE 0 END FROM schema_migrations WHERE version='2026_09_03_stage101_canada_ksa_compliance_baseline'" 2>/dev/null || echo 0)
if [ "$applied" != "1" ]; then
  echo "── applying Stage101 Canada/KSA compliance baseline"
  psql_neon -v ON_ERROR_STOP=1 -q -f "$migration"
  psql_neon -v ON_ERROR_STOP=1 -q -c "INSERT INTO schema_migrations(version,description) VALUES ('2026_09_03_stage101_canada_ksa_compliance_baseline','Canada/KSA regulatory baseline hardening') ON CONFLICT (version) DO NOTHING"
else
  echo "── Stage101 already applied (ledger)"
fi

# Release-facing fail-closed verification. These checks intentionally test
# reference truth only; they do not manufacture provider/device certification.
psql_neon -v ON_ERROR_STOP=1 <<'SQL'
DO $verify_stage101$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM countries
    WHERE code='CA' AND hos_ruleset='CVHOSR SOR/2005-313'
  ) THEN RAISE EXCEPTION 'Stage101 verification failed: Canada ruleset label'; END IF;

  IF NOT EXISTS (
    SELECT 1 FROM countries
    WHERE code='SA' AND hos_ruleset='TGA Goods Transport HOS'
  ) THEN RAISE EXCEPTION 'Stage101 verification failed: Saudi ruleset label'; END IF;

  IF NOT EXISTS (
    SELECT 1 FROM compliance_profiles
    WHERE country_code='CA'
      AND profile_name='Canada Federal HOS - South of 60N'
      AND max_driving_hours=13 AND max_duty_hours=14
      AND rest_requirement_hours=10
  ) THEN RAISE EXCEPTION 'Stage101 verification failed: Canada south profile'; END IF;

  IF NOT EXISTS (
    SELECT 1 FROM compliance_profiles
    WHERE country_code='CA'
      AND profile_name='Canada Federal HOS - North of 60N'
      AND max_driving_hours=15 AND max_duty_hours=18
  ) THEN RAISE EXCEPTION 'Stage101 verification failed: Canada north profile'; END IF;

  IF NOT EXISTS (
    SELECT 1 FROM compliance_profiles
    WHERE country_code='SA'
      AND profile_name='Saudi TGA Goods Transport HOS'
      AND authority='Transport General Authority (TGA)'
      AND max_driving_hours=9 AND rest_requirement_hours=11
  ) THEN RAISE EXCEPTION 'Stage101 verification failed: Saudi profile'; END IF;

  IF EXISTS (
    SELECT 1 FROM compliance_rules
    WHERE is_active AND rule_code IN ('SA-HOS-10H','TC-NSC-CARRIER')
  ) THEN RAISE EXCEPTION 'Stage101 verification failed: obsolete active compliance rule'; END IF;

  IF (SELECT COUNT(*) FROM compliance_rules WHERE is_active AND rule_code IN (
    'CA-S60-HOS-13H-DRIVE','CA-S60-HOS-14H-DUTY','CA-S60-HOS-16H-ELAPSED',
    'CA-S60-HOS-10H-OFFDUTY','CA-S60-HOS-C1-70H-7D','CA-S60-HOS-C2-120H-14D',
    'CA-N60-HOS-15H-DRIVE','CA-N60-HOS-18H-DUTY','CA-N60-HOS-C1-80H-7D',
    'SA-TGA-HOS-9H-DRIVE','SA-TGA-HOS-10H-EXT-2X','SA-TGA-HOS-56H-7D',
    'SA-TGA-HOS-90H-14D','SA-TGA-HOS-BREAK-4_5H','SA-TGA-HOS-DAILY-REST-11H',
    'SA-TGA-HOS-WEEKLY-REST-48H','SA-TGA-HOS-MAX-6D'
  )) <> 17 THEN
    RAISE EXCEPTION 'Stage101 verification failed: mandatory rule set incomplete';
  END IF;
END
$verify_stage101$;
SQL

echo "Canada/KSA Stage101 reference baseline: VERIFIED"
echo "External provider/device/certification/qualification evidence: STILL REQUIRED"
