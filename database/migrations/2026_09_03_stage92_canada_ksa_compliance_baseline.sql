-- Stage 92 — Canada + Saudi Arabia regulatory baseline hardening
--
-- Purpose
--   Correct legacy demo-era country/HOS reference data that is too coarse or
--   materially wrong for Canada and Saudi Arabia commercial pilots.
--
-- Commercial-truth boundary
--   This migration is a regulatory RULE BASELINE for product development and
--   validation. It does NOT certify OpsTrax, an ELD, a tracking device, a
--   provider, or a customer. Exact provider/device/certification/qualification
--   evidence remains an external launch gate under CAN-CERT-01 / KSA-CERT-01.
--
-- Official sources verified 2026-09-03 (refresh before pilot release):
--   Canada — Commercial Vehicle Drivers Hours of Service Regulations,
--     SOR/2005-313: https://laws-lois.justice.gc.ca/eng/regulations/SOR-2005-313/FullText.html
--   Canada — Transport Canada ELD program/certified list:
--     https://tc.canada.ca/en/road-transportation/electronic-logging-devices
--     https://tc.canada.ca/en/road-transportation/electronic-logging-devices/list-electronic-logging-devices
--   Saudi Arabia — Transport General Authority Truck Drivers Guidelines:
--     https://tga.gov.sa/Content/Uploads/Regulations/LandRegulations/Documents/en/Truck%20Driverd%20Guideline.pdf
--   Saudi Arabia — CST Tracking Services Registration:
--     https://www.cst.gov.sa/en/business/services/Tracking-Services-Registration
--
-- Apply as database owner after the runtime schema-service contract.
-- Idempotent: updates legacy reference rows and inserts missing rules by code.

BEGIN;

DO $preflight$
BEGIN
  IF to_regclass('public.countries') IS NULL
     OR to_regclass('public.compliance_profiles') IS NULL
     OR to_regclass('public.compliance_rules') IS NULL THEN
    RAISE EXCEPTION 'Stage92 requires countries, compliance_profiles and compliance_rules';
  END IF;
END
$preflight$;

-- ---------------------------------------------------------------------------
-- Country-level labels: remove the misleading implication that the Canadian
-- NSC is one HOS rule set or that Saudi HOS is a SASO rule.
-- ---------------------------------------------------------------------------
UPDATE countries
SET hos_ruleset = 'CVHOSR SOR/2005-313'
WHERE code = 'CA'
  AND hos_ruleset IS DISTINCT FROM 'CVHOSR SOR/2005-313';

UPDATE countries
SET hos_ruleset = 'TGA Goods Transport HOS'
WHERE code = 'SA'
  AND hos_ruleset IS DISTINCT FROM 'TGA Goods Transport HOS';

-- ---------------------------------------------------------------------------
-- CANADA — South of 60 degrees N (federal baseline)
-- ---------------------------------------------------------------------------
UPDATE compliance_profiles
SET profile_name = 'Canada Federal HOS - South of 60N',
    authority = 'Transport Canada / Provincial-Territorial Enforcement',
    hos_ruleset = 'SOR/2005-313 ss.11-29',
    eld_required = TRUE,
    max_driving_hours = 13,
    max_duty_hours = 14,
    rest_requirement_hours = 10,
    notes = 'Federal south-of-60 HOS baseline. Carrier credentials/safety-fitness obligations are province/territory specific. ELD production use requires an exact currently certified hardware/software boundary.',
    updated_at = NOW()
WHERE country_code = 'CA'
  AND profile_name IN ('Transport Canada NSC','Canada Federal HOS - South of 60N');

INSERT INTO compliance_profiles
  (country_code,profile_name,authority,hos_ruleset,eld_required,max_driving_hours,max_duty_hours,rest_requirement_hours,notes,is_active)
SELECT
  'CA','Canada Federal HOS - South of 60N',
  'Transport Canada / Provincial-Territorial Enforcement',
  'SOR/2005-313 ss.11-29',TRUE,13,14,10,
  'Federal south-of-60 HOS baseline. Carrier credentials/safety-fitness obligations are province/territory specific. ELD production use requires an exact currently certified hardware/software boundary.',
  TRUE
WHERE NOT EXISTS (
  SELECT 1 FROM compliance_profiles
  WHERE country_code='CA' AND profile_name='Canada Federal HOS - South of 60N'
);

-- Preserve the existing rule record identity where possible so historical
-- references do not break; correct its code/name/description instead.
UPDATE compliance_rules
SET rule_code='CA-S60-HOS-13H-DRIVE',
    rule_name='13-Hour Daily Driving Limit',
    category='HOS',
    description='South of 60N: driver shall not drive after accumulating 13 hours of driving time in a day.',
    severity='Critical',
    threshold_value=13,
    threshold_unit='Hours',
    is_active=TRUE
WHERE rule_code='TC-HOS-13H';

UPDATE compliance_rules
SET rule_code='CA-CARRIER-SAFETY-FITNESS',
    rule_name='Provincial/Territorial Carrier Safety-Fitness Requirement',
    category='Documents',
    description='National Safety Code standards are administered through applicable provincial/territorial carrier safety-fitness and credential regimes; there is not one generic Transport Canada NSC carrier registration.',
    severity='High',
    threshold_value=NULL,
    threshold_unit=NULL,
    is_active=TRUE
WHERE rule_code='TC-NSC-CARRIER';

DO $canada_south_rules$
DECLARE
  p BIGINT;
BEGIN
  SELECT id INTO p
  FROM compliance_profiles
  WHERE country_code='CA' AND profile_name='Canada Federal HOS - South of 60N'
  ORDER BY id
  LIMIT 1;

  IF p IS NULL THEN
    RAISE EXCEPTION 'Stage92 could not resolve Canada south-of-60 compliance profile';
  END IF;

  INSERT INTO compliance_rules(profile_id,rule_code,rule_name,category,description,severity,threshold_value,threshold_unit)
  SELECT p,v.rule_code,v.rule_name,v.category,v.description,v.severity,v.threshold_value,v.threshold_unit
  FROM (VALUES
    ('CA-S60-HOS-13H-DRIVE','13-Hour Daily Driving Limit','HOS','South of 60N: driver shall not drive after accumulating 13 hours of driving time in a day.','Critical',13.0,'Hours'),
    ('CA-S60-HOS-14H-DUTY','14-Hour Daily On-Duty Limit','HOS','South of 60N: driver shall not drive after accumulating 14 hours of on-duty time in a day.','Critical',14.0,'Hours'),
    ('CA-S60-HOS-16H-ELAPSED','16-Hour Elapsed-Time Limit','HOS','South of 60N: driver shall not drive after 16 hours have elapsed between the end of the most recent qualifying 8-or-more consecutive hours off duty and the start of the next qualifying off-duty period.','Critical',16.0,'Hours'),
    ('CA-S60-HOS-10H-OFFDUTY','10-Hour Daily Off-Duty Requirement','HOS','South of 60N: driver must take at least 10 hours off duty in a day, including the applicable consecutive off-duty block and additional off-duty time required by the regulations.','Critical',10.0,'Hours'),
    ('CA-S60-HOS-24H-OFF-14D','24 Consecutive Hours Off in Preceding 14 Days','HOS','Driver shall not drive unless the driver has taken at least 24 consecutive hours off duty in the preceding 14 days.','High',24.0,'Hours'),
    ('CA-S60-HOS-C1-70H-7D','Cycle 1 - 70 Hours in 7 Days','HOS','Cycle 1: driver shall not drive after accumulating 70 hours of on-duty time in any period of 7 days.','Critical',70.0,'Hours'),
    ('CA-S60-HOS-C2-120H-14D','Cycle 2 - 120 Hours in 14 Days','HOS','Cycle 2: driver shall not drive after accumulating 120 hours of on-duty time in any period of 14 days.','Critical',120.0,'Hours'),
    ('CA-S60-HOS-C2-70H-24H-OFF','Cycle 2 - 70 Hours Requires 24-Hour Off-Duty Block','HOS','Cycle 2: driver shall not drive after accumulating 70 hours of on-duty time without first taking at least 24 consecutive hours off duty.','Critical',70.0,'Hours'),
    ('CA-S60-HOS-C1-RESET-36H','Cycle 1 Reset - 36 Consecutive Hours Off','HOS','A driver may end the current Cycle 1 and begin a new cycle only after taking at least 36 consecutive hours off duty.','High',36.0,'Hours'),
    ('CA-S60-HOS-C2-RESET-72H','Cycle 2 Reset - 72 Consecutive Hours Off','HOS','A driver may end the current Cycle 2 and begin a new cycle only after taking at least 72 consecutive hours off duty.','High',72.0,'Hours')
  ) AS v(rule_code,rule_name,category,description,severity,threshold_value,threshold_unit)
  WHERE NOT EXISTS (SELECT 1 FROM compliance_rules r WHERE r.rule_code=v.rule_code);
END
$canada_south_rules$;

-- ---------------------------------------------------------------------------
-- CANADA — North of 60 degrees N: separate profile; never silently reuse the
-- south-of-60 clocks. Applicability is a customer/jurisdiction decision.
-- ---------------------------------------------------------------------------
INSERT INTO compliance_profiles
  (country_code,profile_name,authority,hos_ruleset,eld_required,max_driving_hours,max_duty_hours,rest_requirement_hours,notes,is_active)
SELECT
  'CA','Canada Federal HOS - North of 60N',
  'Transport Canada / Provincial-Territorial Enforcement',
  'SOR/2005-313 ss.37-54',TRUE,15,18,8,
  'North-of-60 federal HOS profile. Must be selected only when the driver/vehicle operation falls within the applicable northern jurisdiction.',
  TRUE
WHERE NOT EXISTS (
  SELECT 1 FROM compliance_profiles
  WHERE country_code='CA' AND profile_name='Canada Federal HOS - North of 60N'
);

DO $canada_north_rules$
DECLARE
  p BIGINT;
BEGIN
  SELECT id INTO p
  FROM compliance_profiles
  WHERE country_code='CA' AND profile_name='Canada Federal HOS - North of 60N'
  ORDER BY id
  LIMIT 1;

  IF p IS NULL THEN
    RAISE EXCEPTION 'Stage92 could not resolve Canada north-of-60 compliance profile';
  END IF;

  INSERT INTO compliance_rules(profile_id,rule_code,rule_name,category,description,severity,threshold_value,threshold_unit)
  SELECT p,v.rule_code,v.rule_name,v.category,v.description,v.severity,v.threshold_value,v.threshold_unit
  FROM (VALUES
    ('CA-N60-HOS-15H-DRIVE','15-Hour Driving Limit','HOS','North of 60N: driver shall not drive after accumulating 15 hours of driving time unless the applicable qualifying off-duty requirement has been met.','Critical',15.0,'Hours'),
    ('CA-N60-HOS-18H-DUTY','18-Hour On-Duty Limit','HOS','North of 60N: driver shall not drive after accumulating 18 hours of on-duty time unless the applicable qualifying off-duty requirement has been met.','Critical',18.0,'Hours'),
    ('CA-N60-HOS-20H-ELAPSED','20-Hour Elapsed-Time Limit','HOS','North of 60N: driver shall not drive after 20 hours have elapsed from the end of the most recent qualifying 8-or-more consecutive hours off duty.','Critical',20.0,'Hours'),
    ('CA-N60-HOS-C1-80H-7D','Northern Cycle 1 - 80 Hours in 7 Days','HOS','North of 60N Cycle 1: driver shall not drive after accumulating 80 hours of on-duty time in any period of 7 days.','Critical',80.0,'Hours'),
    ('CA-N60-HOS-C2-120H-14D','Northern Cycle 2 - 120 Hours in 14 Days','HOS','North of 60N Cycle 2: driver shall not drive after accumulating 120 hours of on-duty time in any period of 14 days.','Critical',120.0,'Hours'),
    ('CA-N60-HOS-C2-80H-24H-OFF','Northern Cycle 2 - 80 Hours Requires 24-Hour Off-Duty Block','HOS','North of 60N Cycle 2: driver shall not drive after accumulating 80 hours of on-duty time without first taking at least 24 consecutive hours off duty.','Critical',80.0,'Hours'),
    ('CA-N60-HOS-C1-RESET-36H','Northern Cycle 1 Reset - 36 Consecutive Hours Off','HOS','North of 60N Cycle 1 reset requires at least 36 consecutive hours off duty.','High',36.0,'Hours'),
    ('CA-N60-HOS-C2-RESET-72H','Northern Cycle 2 Reset - 72 Consecutive Hours Off','HOS','North of 60N Cycle 2 reset requires at least 72 consecutive hours off duty.','High',72.0,'Hours')
  ) AS v(rule_code,rule_name,category,description,severity,threshold_value,threshold_unit)
  WHERE NOT EXISTS (SELECT 1 FROM compliance_rules r WHERE r.rule_code=v.rule_code);
END
$canada_north_rules$;

-- ---------------------------------------------------------------------------
-- SAUDI ARABIA — TGA goods-transport driver-hours baseline
-- Correct legacy 10-hours-every-day/SASO semantics.
-- ---------------------------------------------------------------------------
UPDATE compliance_profiles
SET profile_name='Saudi TGA Goods Transport HOS',
    authority='Transport General Authority (TGA)',
    hos_ruleset='TGA Goods Transport HOS',
    eld_required=FALSE,
    max_driving_hours=9,
    max_duty_hours=NULL,
    rest_requirement_hours=11,
    notes='TGA goods-transport baseline: 9 driving hours/24h; extension to 10 hours permitted no more than twice per week; 56h/week; 90h/two consecutive weeks; 45-minute break after 4.5h continuous driving; 11h daily rest; 48h weekly rest; maximum 6 consecutive working days. Tracking-provider/authority-link requirements are separate evidence gates.',
    updated_at=NOW()
WHERE country_code='SA'
  AND profile_name IN ('Saudi Arabia HOS','Saudi TGA Goods Transport HOS');

INSERT INTO compliance_profiles
  (country_code,profile_name,authority,hos_ruleset,eld_required,max_driving_hours,max_duty_hours,rest_requirement_hours,notes,is_active)
SELECT
  'SA','Saudi TGA Goods Transport HOS','Transport General Authority (TGA)',
  'TGA Goods Transport HOS',FALSE,9,NULL,11,
  'TGA goods-transport baseline: 9 driving hours/24h; extension to 10 hours permitted no more than twice per week; 56h/week; 90h/two consecutive weeks; 45-minute break after 4.5h continuous driving; 11h daily rest; 48h weekly rest; maximum 6 consecutive working days. Tracking-provider/authority-link requirements are separate evidence gates.',
  TRUE
WHERE NOT EXISTS (
  SELECT 1 FROM compliance_profiles
  WHERE country_code='SA' AND profile_name='Saudi TGA Goods Transport HOS'
);

UPDATE compliance_rules
SET rule_code='SA-TGA-HOS-9H-DRIVE',
    rule_name='9-Hour Daily Driving Limit',
    category='HOS',
    description='TGA goods-transport baseline: maximum 9 driving hours in 24 hours; may extend to 10 hours only twice per week.',
    severity='Critical',
    threshold_value=9,
    threshold_unit='Hours',
    is_active=TRUE
WHERE rule_code='SA-HOS-10H';

DO $saudi_rules$
DECLARE
  p BIGINT;
BEGIN
  SELECT id INTO p
  FROM compliance_profiles
  WHERE country_code='SA' AND profile_name='Saudi TGA Goods Transport HOS'
  ORDER BY id
  LIMIT 1;

  IF p IS NULL THEN
    RAISE EXCEPTION 'Stage92 could not resolve Saudi TGA goods-transport profile';
  END IF;

  INSERT INTO compliance_rules(profile_id,rule_code,rule_name,category,description,severity,threshold_value,threshold_unit)
  SELECT p,v.rule_code,v.rule_name,v.category,v.description,v.severity,v.threshold_value,v.threshold_unit
  FROM (VALUES
    ('SA-TGA-HOS-9H-DRIVE','9-Hour Daily Driving Limit','HOS','Maximum 9 driving hours in 24 hours. The 10-hour value is an exception, not the normal daily limit.','Critical',9.0,'Hours'),
    ('SA-TGA-HOS-10H-EXT-2X','10-Hour Extension - Maximum Twice Per Week','HOS','Daily driving may be extended from 9 to 10 hours no more than twice per week. The system must track extension count and reason/evidence.','Critical',2.0,'Occurrences/Week'),
    ('SA-TGA-HOS-56H-7D','56-Hour Weekly Driving Limit','HOS','Maximum driving time is 56 hours in one week.','Critical',56.0,'Hours'),
    ('SA-TGA-HOS-90H-14D','90-Hour Two-Week Driving Limit','HOS','Maximum driving time is 90 hours over two consecutive weeks.','Critical',90.0,'Hours'),
    ('SA-TGA-HOS-BREAK-4_5H','45-Minute Break After 4.5 Hours Continuous Driving','HOS','After a maximum of 4.5 hours continuous driving, the driver must take a 45-minute rest break before continuing driving, subject to the applicable split-break provisions.','Critical',4.5,'Hours Continuous Driving'),
    ('SA-TGA-HOS-DAILY-REST-11H','11-Hour Daily Rest Requirement','HOS','Daily rest must be at least 11 consecutive hours.','Critical',11.0,'Hours'),
    ('SA-TGA-HOS-WEEKLY-REST-48H','48-Hour Weekly Rest Requirement','HOS','Weekly rest must be at least 48 consecutive hours.','Critical',48.0,'Hours'),
    ('SA-TGA-HOS-MAX-6D','Maximum Six Consecutive Working Days','HOS','Driver work scheduling must not exceed six consecutive working days before the required weekly rest.','High',6.0,'Days'),
    ('SA-TGA-TRACKING-PROVIDER','Qualified Automated Tracking Provider Boundary','Telematics','Where the customer activity is subject to TGA automated-tracking linkage, use an exact TGA-qualified provider/device/authority path. OpsTrax readiness alone is not provider qualification.','Critical',NULL,NULL)
  ) AS v(rule_code,rule_name,category,description,severity,threshold_value,threshold_unit)
  WHERE NOT EXISTS (SELECT 1 FROM compliance_rules r WHERE r.rule_code=v.rule_code);
END
$saudi_rules$;

-- ---------------------------------------------------------------------------
-- Postconditions: fail the migration if the legacy material misstatements remain
-- in the active reference data or required minimum rules are missing.
-- ---------------------------------------------------------------------------
DO $postcondition$
DECLARE
  missing_count INT;
BEGIN
  IF EXISTS (SELECT 1 FROM countries WHERE code='SA' AND hos_ruleset ILIKE '%SASO%') THEN
    RAISE EXCEPTION 'Stage92 failed: Saudi country ruleset still references SASO HOS';
  END IF;

  IF EXISTS (SELECT 1 FROM compliance_profiles WHERE country_code='SA' AND authority ILIKE '%SASO%') THEN
    RAISE EXCEPTION 'Stage92 failed: Saudi compliance profile still attributes HOS authority to SASO';
  END IF;

  IF EXISTS (SELECT 1 FROM compliance_rules WHERE rule_code='SA-HOS-10H' AND is_active) THEN
    RAISE EXCEPTION 'Stage92 failed: obsolete SA-HOS-10H rule remains active';
  END IF;

  IF EXISTS (SELECT 1 FROM compliance_rules WHERE rule_code='TC-NSC-CARRIER' AND is_active) THEN
    RAISE EXCEPTION 'Stage92 failed: obsolete generic TC-NSC-CARRIER rule remains active';
  END IF;

  SELECT COUNT(*) INTO missing_count
  FROM (VALUES
    ('CA-S60-HOS-13H-DRIVE'),
    ('CA-S60-HOS-14H-DUTY'),
    ('CA-S60-HOS-16H-ELAPSED'),
    ('CA-S60-HOS-10H-OFFDUTY'),
    ('CA-S60-HOS-C1-70H-7D'),
    ('CA-S60-HOS-C2-120H-14D'),
    ('CA-N60-HOS-15H-DRIVE'),
    ('CA-N60-HOS-18H-DUTY'),
    ('CA-N60-HOS-C1-80H-7D'),
    ('SA-TGA-HOS-9H-DRIVE'),
    ('SA-TGA-HOS-10H-EXT-2X'),
    ('SA-TGA-HOS-56H-7D'),
    ('SA-TGA-HOS-90H-14D'),
    ('SA-TGA-HOS-BREAK-4_5H'),
    ('SA-TGA-HOS-DAILY-REST-11H'),
    ('SA-TGA-HOS-WEEKLY-REST-48H'),
    ('SA-TGA-HOS-MAX-6D')
  ) expected(rule_code)
  WHERE NOT EXISTS (
    SELECT 1 FROM compliance_rules r
    WHERE r.rule_code=expected.rule_code AND r.is_active
  );

  IF missing_count <> 0 THEN
    RAISE EXCEPTION 'Stage92 failed: % mandatory Canada/KSA baseline rules missing', missing_count;
  END IF;
END
$postcondition$;

COMMIT;
