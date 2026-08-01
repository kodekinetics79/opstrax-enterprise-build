-- Stage 60 — Dispatch/Trip customer-pilot runtime contract.
-- Idempotent; terminal Stage58 is re-applied by the predeploy runner afterwards
-- to enroll newly-created tenant tables in signed-ticket RLS and exact grants.
BEGIN;

ALTER TABLE trips ADD COLUMN IF NOT EXISTS compliance_score DECIMAL(5,2) NULL;
ALTER TABLE trips ADD COLUMN IF NOT EXISTS started_at TIMESTAMPTZ NULL;
ALTER TABLE trips ADD COLUMN IF NOT EXISTS completed_at TIMESTAMPTZ NULL;
ALTER TABLE trips ALTER COLUMN status SET DEFAULT 'planned';
UPDATE trips SET status='active',updated_at=COALESCE(updated_at,NOW())
WHERE LOWER(status) IN ('in progress','in_progress');

-- Persist the runtime resource invariants enforced by TripSchemaService. Abort on
-- conflicting production data rather than silently cancelling or deleting trips.
CREATE UNIQUE INDEX IF NOT EXISTS ux_trips_route
ON trips(company_id,route_id) WHERE route_id IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS ux_trips_active_vehicle
ON trips(company_id,vehicle_id) WHERE vehicle_id IS NOT NULL AND LOWER(status)='active';
CREATE UNIQUE INDEX IF NOT EXISTS ux_trips_active_driver
ON trips(company_id,driver_id) WHERE driver_id IS NOT NULL AND LOWER(status)='active';

-- TripBackgroundService writes the explainable deviation narrative. Historical
-- Stage29 added this on some databases but is not part of the current predeploy
-- sequence, so Stage60 must own the pilot contract on a fresh production build.
ALTER TABLE safety_events ADD COLUMN IF NOT EXISTS system_insight TEXT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS ux_safety_open_route_deviation
ON safety_events(company_id,(meta_json->>'tripId'),(meta_json->>'stopId'))
WHERE event_type='route_deviation' AND status NOT IN ('resolved','dismissed') AND meta_json IS NOT NULL;

-- Required by the authenticated mobile-driver dispatch path. Production startup does
-- not run optional schema bootstrappers, so this identity link must be migrated.
ALTER TABLE drivers ADD COLUMN IF NOT EXISTS user_id BIGINT NULL;
WITH ranked AS (
  SELECT id,ROW_NUMBER() OVER(PARTITION BY user_id ORDER BY id DESC) rn
  FROM drivers WHERE user_id IS NOT NULL
)
UPDATE drivers SET user_id=NULL WHERE id IN (SELECT id FROM ranked WHERE rn>1);
CREATE UNIQUE INDEX IF NOT EXISTS uq_drivers_user_id ON drivers(user_id) WHERE user_id IS NOT NULL;

ALTER TABLE dispatch_assignments ALTER COLUMN job_id DROP NOT NULL;
ALTER TABLE dispatch_assignments ADD COLUMN IF NOT EXISTS route_id BIGINT NULL;
ALTER TABLE dispatch_assignments ADD COLUMN IF NOT EXISTS trailer_id BIGINT NULL;
ALTER TABLE dispatch_assignments ADD COLUMN IF NOT EXISTS planned_pickup_at TIMESTAMPTZ NULL;
ALTER TABLE dispatch_assignments ADD COLUMN IF NOT EXISTS planned_delivery_at TIMESTAMPTZ NULL;
ALTER TABLE dispatch_assignments ADD COLUMN IF NOT EXISTS actual_pickup_at TIMESTAMPTZ NULL;
ALTER TABLE dispatch_assignments ADD COLUMN IF NOT EXISTS actual_delivery_at TIMESTAMPTZ NULL;
ALTER TABLE dispatch_assignments ADD COLUMN IF NOT EXISTS accepted_at TIMESTAMPTZ NULL;
ALTER TABLE dispatch_assignments ADD COLUMN IF NOT EXISTS acceptance_due_at TIMESTAMPTZ NULL;
ALTER TABLE dispatch_assignments ADD COLUMN IF NOT EXISTS trip_id BIGINT NULL;
ALTER TABLE dispatch_assignments ADD COLUMN IF NOT EXISTS notes TEXT NULL;
ALTER TABLE dispatch_assignments ADD COLUMN IF NOT EXISTS override_reason VARCHAR(500) NULL;
ALTER TABLE dispatch_assignments ADD COLUMN IF NOT EXISTS safety_overridden BOOLEAN NOT NULL DEFAULT false;
ALTER TABLE dispatch_assignments ADD COLUMN IF NOT EXISTS hos_overridden BOOLEAN NOT NULL DEFAULT false;
ALTER TABLE dispatch_assignments ADD COLUMN IF NOT EXISTS eligibility_json JSONB NULL;
ALTER TABLE dispatch_assignments ADD COLUMN IF NOT EXISTS cancelled_at TIMESTAMPTZ NULL;
ALTER TABLE dispatch_assignments ADD COLUMN IF NOT EXISTS created_at TIMESTAMPTZ NOT NULL DEFAULT NOW();

CREATE TABLE IF NOT EXISTS dispatch_proofs (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  assignment_id BIGINT NOT NULL,
  proof_type VARCHAR(30) NOT NULL DEFAULT 'delivery',
  confirmed_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  confirmed_by_user_id BIGINT NULL,
  confirmed_by_driver_id BIGINT NULL,
  notes TEXT NULL,
  evidence_hash VARCHAR(128) NULL,
  lat DECIMAL(9,6) NULL,
  lng DECIMAL(9,6) NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS dispatch_proof_artifacts (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  proof_id BIGINT NOT NULL REFERENCES dispatch_proofs(id) ON DELETE CASCADE,
  kind VARCHAR(30) NOT NULL CHECK (kind IN ('photo','signature')),
  reference TEXT NOT NULL,
  content_type VARCHAR(120) NULL,
  size_bytes BIGINT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

WITH tokens AS (
  SELECT id,LOWER(REPLACE(REPLACE(COALESCE(assignment_status,status,'assigned'),'-','_'),' ','_')) token
  FROM dispatch_assignments
), normalized AS (
  SELECT id,CASE token
    WHEN 'complete' THEN 'delivered' WHEN 'completed' THEN 'delivered'
    WHEN 'canceled' THEN 'cancelled' WHEN 'en_route' THEN 'en_route_pickup'
    WHEN 'at_pickup' THEN 'arrived_pickup' WHEN 'at_delivery' THEN 'arrived_delivery'
    ELSE token END canonical
  FROM tokens
)
UPDATE dispatch_assignments da SET assignment_status=n.canonical
FROM normalized n WHERE da.id=n.id AND da.assignment_status IS DISTINCT FROM n.canonical;

WITH ranked AS (
  SELECT id,ROW_NUMBER() OVER(PARTITION BY company_id,vehicle_id ORDER BY assigned_at DESC,id DESC) rn
  FROM dispatch_assignments
  WHERE vehicle_id IS NOT NULL AND assignment_status NOT IN ('delivered','cancelled')
)
UPDATE dispatch_assignments SET assignment_status='cancelled',status='Cancelled',cancelled_at=COALESCE(cancelled_at,NOW())
WHERE id IN (SELECT id FROM ranked WHERE rn>1);

WITH ranked AS (
  SELECT id,ROW_NUMBER() OVER(PARTITION BY company_id,driver_id ORDER BY assigned_at DESC,id DESC) rn
  FROM dispatch_assignments
  WHERE driver_id IS NOT NULL AND assignment_status NOT IN ('delivered','cancelled')
)
UPDATE dispatch_assignments SET assignment_status='cancelled',status='Cancelled',cancelled_at=COALESCE(cancelled_at,NOW())
WHERE id IN (SELECT id FROM ranked WHERE rn>1);

WITH ranked AS (
  SELECT id,ROW_NUMBER() OVER(PARTITION BY company_id,job_id ORDER BY assigned_at DESC,id DESC) rn
  FROM dispatch_assignments
  WHERE job_id IS NOT NULL AND assignment_status NOT IN ('delivered','cancelled')
)
UPDATE dispatch_assignments SET assignment_status='cancelled',status='Cancelled',cancelled_at=COALESCE(cancelled_at,NOW())
WHERE id IN (SELECT id FROM ranked WHERE rn>1);

WITH ranked AS (
  SELECT id,ROW_NUMBER() OVER(PARTITION BY company_id,assignment_id,LOWER(proof_type)
                              ORDER BY confirmed_at DESC,id DESC) rn
  FROM dispatch_proofs
)
DELETE FROM dispatch_proofs WHERE id IN (SELECT id FROM ranked WHERE rn>1);

CREATE UNIQUE INDEX IF NOT EXISTS uq_da_active_vehicle ON dispatch_assignments(company_id,vehicle_id)
WHERE vehicle_id IS NOT NULL AND assignment_status NOT IN ('delivered','cancelled');
CREATE UNIQUE INDEX IF NOT EXISTS uq_da_active_driver ON dispatch_assignments(company_id,driver_id)
WHERE driver_id IS NOT NULL AND assignment_status NOT IN ('delivered','cancelled');
CREATE UNIQUE INDEX IF NOT EXISTS uq_da_active_job ON dispatch_assignments(company_id,job_id)
WHERE job_id IS NOT NULL AND assignment_status NOT IN ('delivered','cancelled');
CREATE UNIQUE INDEX IF NOT EXISTS uq_dispatch_proof_type
ON dispatch_proofs(company_id,assignment_id,LOWER(proof_type));
CREATE INDEX IF NOT EXISTS idx_da_acceptance_due
ON dispatch_assignments(company_id,assignment_status,acceptance_due_at);
CREATE INDEX IF NOT EXISTS idx_dpa_proof ON dispatch_proof_artifacts(proof_id);
CREATE INDEX IF NOT EXISTS idx_dpa_company ON dispatch_proof_artifacts(company_id);

COMMIT;
