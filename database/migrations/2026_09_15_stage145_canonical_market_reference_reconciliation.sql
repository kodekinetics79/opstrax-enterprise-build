-- Stage 145 — canonical market reference reconciliation
--
-- Runtime schema bootstrapping is disabled once Stage88 is ledgered, so global
-- country/compliance reference rows must be migration-owned. Reconcile the
-- Saudi TGA and Canadian federal identities without relying on generated IDs.

BEGIN;

DO $preflight$
BEGIN
  IF to_regclass('public.countries') IS NULL
     OR to_regclass('public.compliance_profiles') IS NULL
     OR to_regclass('public.compliance_rules') IS NULL THEN
    RAISE EXCEPTION 'Stage145 requires countries, compliance_profiles and compliance_rules';
  END IF;
END
$preflight$;

INSERT INTO countries(code,name,currency,distance_unit,volume_unit,hos_ruleset,rtl) VALUES
  ('US','United States','USD','Miles','Gallons','FMCSA 395.3',FALSE),
  ('CA','Canada','CAD','Kilometers','Liters','CVHOSR SOR/2005-313',FALSE),
  ('SA','Saudi Arabia','SAR','Kilometers','Liters','TGA Goods Transport HOS',TRUE),
  ('AE','United Arab Emirates','AED','Kilometers','Liters','UAE RTA',TRUE),
  ('PK','Pakistan','PKR','Kilometers','Liters','NHA Regulations',FALSE)
ON CONFLICT (code) DO UPDATE SET
  name=EXCLUDED.name,
  currency=EXCLUDED.currency,
  distance_unit=EXCLUDED.distance_unit,
  volume_unit=EXCLUDED.volume_unit,
  hos_ruleset=EXCLUDED.hos_ruleset,
  rtl=EXCLUDED.rtl;

UPDATE compliance_profiles
   SET is_active=FALSE, updated_at=NOW()
 WHERE (country_code='CA' AND profile_name='Transport Canada NSC')
    OR (country_code='SA' AND (profile_name='Saudi Arabia HOS' OR authority ILIKE '%SASO%'));

UPDATE compliance_rules
   SET is_active=FALSE
 WHERE rule_code IN ('TC-HOS-13H','TC-NSC-CARRIER','SA-HOS-10H');

WITH seed(country_code,profile_name,authority,hos_ruleset,eld_required,max_driving_hours,max_duty_hours,rest_requirement_hours,notes) AS (VALUES
  ('CA','Canada Federal HOS - South of 60N','Transport Canada / Provincial-Territorial Enforcement','SOR/2005-313 ss.11-29',TRUE,13::decimal,14::decimal,10::decimal,
   'Federal south-of-60 HOS baseline. Carrier credentials/safety-fitness obligations are province/territory specific. ELD production use requires an exact currently certified hardware/software boundary.'),
  ('SA','Saudi TGA Goods Transport HOS','Transport General Authority (TGA)','TGA Goods Transport HOS',FALSE,9::decimal,NULL::decimal,11::decimal,
   'TGA goods-transport baseline: 9 driving hours/24h; extension to 10 hours permitted no more than twice per week; 56h/week; 90h/two consecutive weeks; 45-minute break after 4.5h continuous driving; 11h daily rest; 48h weekly rest; maximum 6 consecutive working days. Tracking-provider/authority-link requirements are separate evidence gates.')
), updated AS (
  UPDATE compliance_profiles p SET
    authority=s.authority,
    hos_ruleset=s.hos_ruleset,
    eld_required=s.eld_required,
    max_driving_hours=s.max_driving_hours,
    max_duty_hours=s.max_duty_hours,
    rest_requirement_hours=s.rest_requirement_hours,
    notes=s.notes,
    is_active=TRUE,
    updated_at=NOW()
  FROM seed s
  WHERE p.country_code=s.country_code AND p.profile_name=s.profile_name
  RETURNING p.id
)
INSERT INTO compliance_profiles
  (country_code,profile_name,authority,hos_ruleset,eld_required,max_driving_hours,max_duty_hours,rest_requirement_hours,notes,is_active)
SELECT s.country_code,s.profile_name,s.authority,s.hos_ruleset,s.eld_required,
       s.max_driving_hours,s.max_duty_hours,s.rest_requirement_hours,s.notes,TRUE
  FROM seed s
 WHERE NOT EXISTS (
   SELECT 1 FROM compliance_profiles p
    WHERE p.country_code=s.country_code AND p.profile_name=s.profile_name
 );

-- An earlier fixed-ID compatibility wrapper could create a second canonical
-- profile after Stage101 had already generated one. Collapse those duplicates
-- onto the oldest stable identity and repoint every compliance reference before
-- deleting the redundant row.
DO $dedupe_profiles$
DECLARE
  d RECORD;
BEGIN
  FOR d IN
    SELECT country_code,profile_name,MIN(id) AS keeper_id,ARRAY_AGG(id) AS all_ids
      FROM compliance_profiles
     WHERE (country_code='CA' AND profile_name='Canada Federal HOS - South of 60N')
        OR (country_code='SA' AND profile_name='Saudi TGA Goods Transport HOS')
     GROUP BY country_code,profile_name
    HAVING COUNT(*)>1
  LOOP
    UPDATE compliance_rules SET profile_id=d.keeper_id WHERE profile_id=ANY(d.all_ids) AND profile_id<>d.keeper_id;
    UPDATE driver_compliance_status SET profile_id=d.keeper_id WHERE profile_id=ANY(d.all_ids) AND profile_id<>d.keeper_id;
    UPDATE vehicle_compliance_status SET profile_id=d.keeper_id WHERE profile_id=ANY(d.all_ids) AND profile_id<>d.keeper_id;
    UPDATE compliance_violations SET profile_id=d.keeper_id WHERE profile_id=ANY(d.all_ids) AND profile_id<>d.keeper_id;
    UPDATE compliance_audit_packages SET profile_id=d.keeper_id WHERE profile_id=ANY(d.all_ids) AND profile_id<>d.keeper_id;
    UPDATE hos_logs SET profile_id=d.keeper_id WHERE profile_id=ANY(d.all_ids) AND profile_id<>d.keeper_id;
    UPDATE hos_clocks SET profile_id=d.keeper_id WHERE profile_id=ANY(d.all_ids) AND profile_id<>d.keeper_id;
    UPDATE dvir_reports SET compliance_profile_id=d.keeper_id WHERE compliance_profile_id=ANY(d.all_ids) AND compliance_profile_id<>d.keeper_id;
    DELETE FROM compliance_profiles WHERE id=ANY(d.all_ids) AND id<>d.keeper_id;
  END LOOP;
END
$dedupe_profiles$;

WITH seed(country_code,profile_name,rule_code,rule_name,category,description,severity,threshold_value,threshold_unit) AS (VALUES
  ('CA','Canada Federal HOS - South of 60N','CA-S60-HOS-13H-DRIVE','13-Hour Daily Driving Limit','HOS','South of 60N: driver shall not drive after accumulating 13 hours of driving time in a day.','Critical',13::decimal,'Hours'),
  ('CA','Canada Federal HOS - South of 60N','CA-CARRIER-SAFETY-FITNESS','Provincial/Territorial Carrier Safety-Fitness Requirement','Documents','National Safety Code standards are administered through applicable provincial/territorial carrier safety-fitness and credential regimes; there is not one generic Transport Canada NSC carrier registration.','High',NULL::decimal,NULL::text),
  ('SA','Saudi TGA Goods Transport HOS','SA-TGA-HOS-9H-DRIVE','9-Hour Daily Driving Limit','HOS','TGA goods-transport baseline: maximum 9 driving hours in 24 hours; may extend to 10 hours only twice per week.','Critical',9::decimal,'Hours')
), resolved AS (
  SELECT p.id AS profile_id,s.rule_code,s.rule_name,s.category,s.description,s.severity,s.threshold_value,s.threshold_unit
    FROM seed s
    CROSS JOIN LATERAL (
      SELECT id FROM compliance_profiles
       WHERE country_code=s.country_code AND profile_name=s.profile_name
       ORDER BY is_active DESC,id
       LIMIT 1
    ) p
), updated AS (
  UPDATE compliance_rules r SET
    profile_id=s.profile_id,
    rule_name=s.rule_name,
    category=s.category,
    description=s.description,
    severity=s.severity,
    threshold_value=s.threshold_value,
    threshold_unit=s.threshold_unit,
    is_active=TRUE
  FROM resolved s
  WHERE r.rule_code=s.rule_code
  RETURNING r.id
)
INSERT INTO compliance_rules
  (profile_id,rule_code,rule_name,category,description,severity,threshold_value,threshold_unit,is_active)
SELECT s.profile_id,s.rule_code,s.rule_name,s.category,s.description,s.severity,
       s.threshold_value,s.threshold_unit,TRUE
  FROM resolved s
 WHERE NOT EXISTS (SELECT 1 FROM compliance_rules r WHERE r.rule_code=s.rule_code);

DO $postcondition$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM countries WHERE code='CA' AND hos_ruleset='CVHOSR SOR/2005-313')
     OR NOT EXISTS (SELECT 1 FROM countries WHERE code='SA' AND hos_ruleset='TGA Goods Transport HOS') THEN
    RAISE EXCEPTION 'Stage145 failed to reconcile canonical country rulesets';
  END IF;
  IF (SELECT COUNT(*) FROM compliance_profiles WHERE country_code='CA' AND profile_name='Canada Federal HOS - South of 60N' AND is_active)<>1
     OR (SELECT COUNT(*) FROM compliance_profiles WHERE country_code='SA' AND profile_name='Saudi TGA Goods Transport HOS' AND is_active)<>1 THEN
    RAISE EXCEPTION 'Stage145 requires one active canonical Canada and Saudi profile';
  END IF;
  IF EXISTS (SELECT 1 FROM compliance_profiles WHERE country_code='SA' AND authority ILIKE '%SASO%' AND is_active)
     OR EXISTS (SELECT 1 FROM compliance_rules WHERE rule_code IN ('TC-HOS-13H','TC-NSC-CARRIER','SA-HOS-10H') AND is_active) THEN
    RAISE EXCEPTION 'Stage145 left obsolete Canada/Saudi reference data active';
  END IF;
  IF NOT EXISTS (
       SELECT 1 FROM compliance_rules r JOIN compliance_profiles p ON p.id=r.profile_id
        WHERE r.rule_code='CA-S60-HOS-13H-DRIVE' AND r.is_active
          AND p.country_code='CA' AND p.profile_name='Canada Federal HOS - South of 60N')
     OR NOT EXISTS (
       SELECT 1 FROM compliance_rules r JOIN compliance_profiles p ON p.id=r.profile_id
        WHERE r.rule_code='SA-TGA-HOS-9H-DRIVE' AND r.is_active
          AND p.country_code='SA' AND p.profile_name='Saudi TGA Goods Transport HOS') THEN
    RAISE EXCEPTION 'Stage145 failed to preserve canonical rule/profile links';
  END IF;
END
$postcondition$;

INSERT INTO schema_migrations(version,description)
VALUES ('2026_09_15_stage145_canonical_market_reference_reconciliation','Canonical Saudi/Canada market reference reconciliation')
ON CONFLICT(version) DO NOTHING;

COMMIT;
