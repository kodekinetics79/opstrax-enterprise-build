-- Stage 24 — add company_id to compliance/HOS tables so RLS can isolate them
--
-- PURPOSE
--   The SEC-4 audit found compliance/HOS summary reads (ComplianceSummary,
--   HosSummary, driver detail compliance join) aggregate across tables that had
--   NO company_id column — so RLS could not backstop them and they were only as
--   isolated as their (often missing) WHERE clause. This adds company_id, backfills
--   it from the owning entity, and lets the Stage 22 reconcile pass RLS-enroll them.
--
-- TABLES (backfill source):
--   compliance_violations      <- drivers.company_id, else vehicles.company_id
--   driver_compliance_status   <- drivers.company_id
--   vehicle_compliance_status  <- vehicles.company_id
--   hos_clocks                 <- drivers.company_id
--   compliance_audit_packages  <- no reliable entity FK (created_by is varchar);
--                                 column added, left NULL where underivable. Under RLS
--                                 a NULL company_id row is invisible to tenants
--                                 (fail-closed) and reachable only via platform bypass.
--
-- SAFETY / REVERSIBILITY
--   Additive (ADD COLUMN IF NOT EXISTS) + data-only backfill. Idempotent.

BEGIN;

ALTER TABLE compliance_violations      ADD COLUMN IF NOT EXISTS company_id bigint;
ALTER TABLE driver_compliance_status   ADD COLUMN IF NOT EXISTS company_id bigint;
ALTER TABLE vehicle_compliance_status  ADD COLUMN IF NOT EXISTS company_id bigint;
ALTER TABLE hos_clocks                 ADD COLUMN IF NOT EXISTS company_id bigint;
ALTER TABLE compliance_audit_packages  ADD COLUMN IF NOT EXISTS company_id bigint;

UPDATE compliance_violations cv
   SET company_id = COALESCE(
         (SELECT d.company_id FROM drivers  d WHERE d.id = cv.driver_id),
         (SELECT v.company_id FROM vehicles v WHERE v.id = cv.vehicle_id))
 WHERE cv.company_id IS NULL;

UPDATE driver_compliance_status dcs
   SET company_id = d.company_id
  FROM drivers d WHERE d.id = dcs.driver_id AND dcs.company_id IS NULL;

UPDATE vehicle_compliance_status vcs
   SET company_id = v.company_id
  FROM vehicles v WHERE v.id = vcs.vehicle_id AND vcs.company_id IS NULL;

UPDATE hos_clocks hc
   SET company_id = d.company_id
  FROM drivers d WHERE d.id = hc.driver_id AND hc.company_id IS NULL;

CREATE INDEX IF NOT EXISTS idx_compliance_violations_company     ON compliance_violations (company_id);
CREATE INDEX IF NOT EXISTS idx_driver_compliance_status_company  ON driver_compliance_status (company_id);
CREATE INDEX IF NOT EXISTS idx_vehicle_compliance_status_company ON vehicle_compliance_status (company_id);
CREATE INDEX IF NOT EXISTS idx_hos_clocks_company                ON hos_clocks (company_id);

COMMIT;
