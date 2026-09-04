#!/usr/bin/env bash
# Canada/KSA regulated-pilot predeploy wrapper.
#
# Runs the canonical protected-environment migration chain first, establishes the
# minimum non-tenant Canada/KSA reference identities required to coexist with the
# runtime Batch6 fixed-ID reference seeder, then applies Stage101.
#
# Commercial truth: this creates regulatory reference data only. It does not
# create provider/device certification, TGA qualification, or customer approval.
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

# ---------------------------------------------------------------------------
# Runtime-reference compatibility bootstrap
# ---------------------------------------------------------------------------
# Batch6SchemaService owns global reference seeds at application boot and uses
# fixed IDs for the original country compliance profiles/rules. Owner migrations
# intentionally run before the application starts. On a fresh protected database,
# Stage101 must therefore reserve/correct the Canada/KSA fixed identities before
# it creates any generated IDs, otherwise the later runtime seed could collide
# with Stage101 or reintroduce the obsolete Saudi/Canada reference rows.
#
# Existing environments are preserved: fixed-ID inserts are DO NOTHING on
# conflict; Stage101 then corrects the recognized legacy Canada/KSA records. The
# verifier below fails closed if fixed IDs 3/4 do not resolve to the expected
# Canada/KSA profiles after migration.
psql_neon -v ON_ERROR_STOP=1 -q <<'SQL'
INSERT INTO countries (code,name,currency,distance_unit,volume_unit,hos_ruleset,rtl)
VALUES
  ('CA','Canada','CAD','Kilometers','Liters','CVHOSR SOR/2005-313',false),
  ('SA','Saudi Arabia','SAR','Kilometers','Liters','TGA Goods Transport HOS',true)
ON CONFLICT (code) DO NOTHING;

INSERT INTO compliance_profiles
  (id,country_code,profile_name,authority,hos_ruleset,eld_required,max_driving_hours,max_duty_hours,rest_requirement_hours,is_active)
OVERRIDING SYSTEM VALUE
VALUES
  (3,'CA','Canada Federal HOS - South of 60N','Transport Canada / Provincial-Territorial Enforcement','SOR/2005-313 ss.11-29',true,13,14,10,true),
  (4,'SA','Saudi TGA Goods Transport HOS','Transport General Authority (TGA)','TGA Goods Transport HOS',false,9,NULL,11,true)
ON CONFLICT (id) DO NOTHING;

INSERT INTO compliance_rules
  (id,profile_id,rule_code,rule_name,category,description,severity,threshold_value,threshold_unit,is_active)
OVERRIDING SYSTEM VALUE
VALUES
  (6,3,'CA-S60-HOS-13H-DRIVE','13-Hour Daily Driving Limit','HOS','South of 60N: driver shall not drive after accumulating 13 hours of driving time in a day.','Critical',13,'Hours',true),
  (7,3,'CA-CARRIER-SAFETY-FITNESS','Provincial/Territorial Carrier Safety-Fitness Requirement','Documents','National Safety Code standards are administered through applicable provincial/territorial carrier safety-fitness and credential regimes; there is not one generic Transport Canada NSC carrier registration.','High',NULL,NULL,true),
  (8,4,'SA-TGA-HOS-9H-DRIVE','9-Hour Daily Driving Limit','HOS','TGA goods-transport baseline: maximum 9 driving hours in 24 hours; may extend to 10 hours only twice per week.','Critical',9,'Hours',true)
ON CONFLICT (id) DO NOTHING;

-- Preserve the runtime seeder's original reserved ID range (profiles 1..6,
-- rules 1..10). Stage101-generated Canada north/rule rows begin above it.
SELECT setval(
  pg_get_serial_sequence('compliance_profiles','id'),
  GREATEST((SELECT COALESCE(MAX(id),0) FROM compliance_profiles), 10),
  true
);
SELECT setval(
  pg_get_serial_sequence('compliance_rules','id'),
  GREATEST((SELECT COALESCE(MAX(id),0) FROM compliance_rules), 10),
  true
);
SQL

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
    WHERE id=3 AND country_code='CA'
      AND profile_name='Canada Federal HOS - South of 60N'
      AND max_driving_hours=13 AND max_duty_hours=14
      AND rest_requirement_hours=10
  ) THEN RAISE EXCEPTION 'Stage101 verification failed: fixed Canada south profile identity'; END IF;

  IF NOT EXISTS (
    SELECT 1 FROM compliance_profiles
    WHERE country_code='CA'
      AND profile_name='Canada Federal HOS - North of 60N'
      AND max_driving_hours=15 AND max_duty_hours=18
  ) THEN RAISE EXCEPTION 'Stage101 verification failed: Canada north profile'; END IF;

  IF NOT EXISTS (
    SELECT 1 FROM compliance_profiles
    WHERE id=4 AND country_code='SA'
      AND profile_name='Saudi TGA Goods Transport HOS'
      AND authority='Transport General Authority (TGA)'
      AND max_driving_hours=9 AND rest_requirement_hours=11
  ) THEN RAISE EXCEPTION 'Stage101 verification failed: fixed Saudi profile identity'; END IF;

  IF NOT EXISTS (SELECT 1 FROM compliance_rules WHERE id=6 AND rule_code='CA-S60-HOS-13H-DRIVE' AND profile_id=3 AND is_active) THEN
    RAISE EXCEPTION 'Stage101 verification failed: fixed Canada 13h rule identity';
  END IF;
  IF NOT EXISTS (SELECT 1 FROM compliance_rules WHERE id=7 AND rule_code='CA-CARRIER-SAFETY-FITNESS' AND profile_id=3 AND is_active) THEN
    RAISE EXCEPTION 'Stage101 verification failed: fixed Canada carrier rule identity';
  END IF;
  IF NOT EXISTS (SELECT 1 FROM compliance_rules WHERE id=8 AND rule_code='SA-TGA-HOS-9H-DRIVE' AND profile_id=4 AND is_active) THEN
    RAISE EXCEPTION 'Stage101 verification failed: fixed Saudi 9h rule identity';
  END IF;

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
echo "Runtime fixed-ID compatibility: VERIFIED"
echo "External provider/device/certification/qualification evidence: STILL REQUIRED"
