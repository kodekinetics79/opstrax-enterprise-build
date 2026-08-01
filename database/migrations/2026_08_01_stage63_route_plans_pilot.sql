-- Stage 63 — Route Plans customer-pilot integrity contract.
-- Additive/idempotent. It makes branch ownership durable before a route has resources
-- or stops, and moves duplicate/double-booking protection into PostgreSQL.

BEGIN;

ALTER TABLE routes ADD COLUMN IF NOT EXISTS branch_id BIGINT NULL;

UPDATE routes r
SET branch_id = COALESCE(
  (SELECT v.branch_id FROM vehicles v WHERE v.id=r.assigned_vehicle_id AND v.company_id=r.company_id LIMIT 1),
  (SELECT d.branch_id FROM drivers d WHERE d.id=r.assigned_driver_id AND d.company_id=r.company_id LIMIT 1),
  (SELECT j.branch_id FROM route_stops rs JOIN jobs j ON j.id=rs.job_id AND j.company_id=r.company_id
   WHERE rs.route_id=r.id AND rs.company_id=r.company_id AND j.branch_id IS NOT NULL
   ORDER BY rs.stop_sequence, rs.id LIMIT 1))
WHERE r.branch_id IS NULL;

CREATE INDEX IF NOT EXISTS idx_routes_company_branch ON routes(company_id, branch_id, status) WHERE deleted_at IS NULL;
CREATE UNIQUE INDEX IF NOT EXISTS uq_routes_company_code_active
  ON routes(company_id, route_code) WHERE deleted_at IS NULL;
CREATE UNIQUE INDEX IF NOT EXISTS uq_route_stops_company_route_sequence
  ON route_stops(company_id, route_id, stop_sequence);
CREATE UNIQUE INDEX IF NOT EXISTS uq_routes_active_driver
  ON routes(company_id, assigned_driver_id)
  WHERE deleted_at IS NULL AND assigned_driver_id IS NOT NULL AND status IN ('Active','Delayed','At Risk');
CREATE UNIQUE INDEX IF NOT EXISTS uq_routes_active_vehicle
  ON routes(company_id, assigned_vehicle_id)
  WHERE deleted_at IS NULL AND assigned_vehicle_id IS NOT NULL AND status IN ('Active','Delayed','At Risk');

COMMIT;
