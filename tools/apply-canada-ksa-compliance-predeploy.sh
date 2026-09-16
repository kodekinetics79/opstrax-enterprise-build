#!/usr/bin/env bash
# Canada/KSA regulated-pilot predeploy wrapper.
#
# Runs the canonical protected-environment migration chain first, then verifies
# the migration-owned Canada/KSA reference identities by stable regulatory keys.
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

psql_neon() { python3 tools/psql-neon-env.py "$@"; }

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
      AND is_active
      AND max_driving_hours=13 AND max_duty_hours=14
      AND rest_requirement_hours=10
  ) THEN RAISE EXCEPTION 'Stage101 verification failed: Canada south profile identity'; END IF;

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
      AND is_active
      AND authority='Transport General Authority (TGA)'
      AND max_driving_hours=9 AND rest_requirement_hours=11
  ) THEN RAISE EXCEPTION 'Stage101 verification failed: Saudi profile identity'; END IF;

  IF NOT EXISTS (
    SELECT 1 FROM compliance_rules r JOIN compliance_profiles p ON p.id=r.profile_id
    WHERE r.rule_code='CA-S60-HOS-13H-DRIVE' AND r.is_active
      AND p.country_code='CA' AND p.profile_name='Canada Federal HOS - South of 60N'
  ) THEN
    RAISE EXCEPTION 'Stage101 verification failed: Canada 13h rule identity';
  END IF;
  IF NOT EXISTS (
    SELECT 1 FROM compliance_rules r JOIN compliance_profiles p ON p.id=r.profile_id
    WHERE r.rule_code='CA-CARRIER-SAFETY-FITNESS' AND r.is_active
      AND p.country_code='CA' AND p.profile_name='Canada Federal HOS - South of 60N'
  ) THEN
    RAISE EXCEPTION 'Stage101 verification failed: Canada carrier rule identity';
  END IF;
  IF NOT EXISTS (
    SELECT 1 FROM compliance_rules r JOIN compliance_profiles p ON p.id=r.profile_id
    WHERE r.rule_code='SA-TGA-HOS-9H-DRIVE' AND r.is_active
      AND p.country_code='SA' AND p.profile_name='Saudi TGA Goods Transport HOS'
  ) THEN
    RAISE EXCEPTION 'Stage101 verification failed: Saudi 9h rule identity';
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
echo "Migration/runtime stable-key compatibility: VERIFIED"
echo "External provider/device/certification/qualification evidence: STILL REQUIRED"
