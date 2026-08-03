-- Stage 64 — Shipments customer-pilot read-path performance contract.
-- Active Shipments is a read-only projection of canonical jobs/dispatch/POD/billing.

BEGIN;

ALTER TABLE proof_of_delivery
  ADD COLUMN IF NOT EXISTS created_at TIMESTAMPTZ NOT NULL DEFAULT NOW();

CREATE INDEX IF NOT EXISTS idx_jobs_active_projection
  ON jobs(company_id, branch_id, status, created_at DESC, id DESC)
  WHERE deleted_at IS NULL AND LOWER(status) NOT IN ('delivered','cancelled','canceled');

CREATE INDEX IF NOT EXISTS idx_dispatch_assignments_job_projection_recent
  ON dispatch_assignments(company_id, job_id, (COALESCE(updated_at,assigned_at,created_at)) DESC, id DESC);

CREATE INDEX IF NOT EXISTS idx_proof_packages_company_job_recent
  ON proof_packages(company_id, job_id, proof_type, created_at DESC, id DESC);

CREATE INDEX IF NOT EXISTS idx_location_company_vehicle_recent
  ON location_events(company_id, vehicle_id, event_time DESC, id DESC);

CREATE INDEX IF NOT EXISTS idx_pod_company_job_projection_recent
  ON proof_of_delivery(company_id, job_id, (COALESCE(captured_at,created_at)) DESC, id DESC);

COMMIT;
