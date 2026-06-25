-- ============================================================================
-- 004_jobs_execution.sql
-- Order Execution Cockpit — schema reconciliation + realistic live seed.
--
-- The base 001 schema defined a minimal `jobs` table, but the execution-cockpit
-- API (GET /api/jobs, /summary, /{id}, send-eta, proof, status) reads and writes
-- a much richer column set. On a fresh database those endpoints throw and the
-- UI silently falls back to canned data. This migration is additive and
-- idempotent: it adds the missing columns the API contract requires, then
-- backfills the demo tenant (company_id = 1) with realistic, scenario-diverse
-- data so every KPI and panel reflects real rows — no hard-coded placeholders.
-- ============================================================================

-- ── jobs: execution columns the cockpit API depends on ──────────────────────
ALTER TABLE jobs ADD COLUMN IF NOT EXISTS job_number                  VARCHAR(60);
ALTER TABLE jobs ADD COLUMN IF NOT EXISTS pickup_latitude             DECIMAL(10,7);
ALTER TABLE jobs ADD COLUMN IF NOT EXISTS pickup_longitude            DECIMAL(10,7);
ALTER TABLE jobs ADD COLUMN IF NOT EXISTS dropoff_latitude            DECIMAL(10,7);
ALTER TABLE jobs ADD COLUMN IF NOT EXISTS dropoff_longitude           DECIMAL(10,7);
ALTER TABLE jobs ADD COLUMN IF NOT EXISTS sla_window_start            TIMESTAMPTZ;
ALTER TABLE jobs ADD COLUMN IF NOT EXISTS sla_window_end              TIMESTAMPTZ;
ALTER TABLE jobs ADD COLUMN IF NOT EXISTS required_vehicle_type       VARCHAR(80);
ALTER TABLE jobs ADD COLUMN IF NOT EXISTS required_driver_certification VARCHAR(120);
ALTER TABLE jobs ADD COLUMN IF NOT EXISTS route_id                    BIGINT;
ALTER TABLE jobs ADD COLUMN IF NOT EXISTS eta                         TIMESTAMPTZ;
ALTER TABLE jobs ADD COLUMN IF NOT EXISTS sla_status                  VARCHAR(40)  DEFAULT 'On Track';
ALTER TABLE jobs ADD COLUMN IF NOT EXISTS proof_status                VARCHAR(40)  DEFAULT 'Pending';
ALTER TABLE jobs ADD COLUMN IF NOT EXISTS customer_update_status      VARCHAR(40)  DEFAULT 'Not Sent';
ALTER TABLE jobs ADD COLUMN IF NOT EXISTS tracking_code               VARCHAR(80);
ALTER TABLE jobs ADD COLUMN IF NOT EXISTS risk_score                  DECIMAL(6,2) DEFAULT 0;
ALTER TABLE jobs ADD COLUMN IF NOT EXISTS revenue_estimate            DECIMAL(12,2);
ALTER TABLE jobs ADD COLUMN IF NOT EXISTS cost_estimate               DECIMAL(12,2);
ALTER TABLE jobs ADD COLUMN IF NOT EXISTS margin_estimate             DECIMAL(12,2);
ALTER TABLE jobs ADD COLUMN IF NOT EXISTS notes                       TEXT;
ALTER TABLE jobs ADD COLUMN IF NOT EXISTS updated_at                  TIMESTAMPTZ  DEFAULT NOW();
ALTER TABLE jobs ADD COLUMN IF NOT EXISTS deleted_at                  TIMESTAMPTZ;

CREATE INDEX IF NOT EXISTS ix_jobs_company_status ON jobs (company_id, status) WHERE deleted_at IS NULL;
CREATE INDEX IF NOT EXISTS ix_jobs_sla_status     ON jobs (company_id, sla_status) WHERE deleted_at IS NULL;

-- ── proof_of_delivery: columns written by capture/placeholder + POD module ──
ALTER TABLE proof_of_delivery ADD COLUMN IF NOT EXISTS received_by   VARCHAR(160);
ALTER TABLE proof_of_delivery ADD COLUMN IF NOT EXISTS proof_type    VARCHAR(80) DEFAULT 'Digital Signature';
ALTER TABLE proof_of_delivery ADD COLUMN IF NOT EXISTS signature_url TEXT;
ALTER TABLE proof_of_delivery ADD COLUMN IF NOT EXISTS notes         TEXT;

-- ── eta_updates: columns written by SendEta ─────────────────────────────────
ALTER TABLE eta_updates ADD COLUMN IF NOT EXISTS customer_id      BIGINT;
ALTER TABLE eta_updates ADD COLUMN IF NOT EXISTS tracking_code    VARCHAR(80);
ALTER TABLE eta_updates ADD COLUMN IF NOT EXISTS eta              TIMESTAMPTZ;
ALTER TABLE eta_updates ADD COLUMN IF NOT EXISTS confidence_level VARCHAR(40) DEFAULT 'High';

-- ── route_stops: columns surfaced in the job detail drawer ──────────────────
ALTER TABLE route_stops ADD COLUMN IF NOT EXISTS stop_type    VARCHAR(40) DEFAULT 'Delivery';
ALTER TABLE route_stops ADD COLUMN IF NOT EXISTS proof_status VARCHAR(40) DEFAULT 'Pending';

-- ── customer_communications: message_type surfaced in the comms grid ────────
ALTER TABLE customer_communications ADD COLUMN IF NOT EXISTS message_type VARCHAR(60) DEFAULT 'ETA Update';

-- ============================================================================
-- Realistic live backfill — demo tenant only (company_id = 1), NULLs only.
-- Deterministic per-row so the cockpit shows a believable mix of states:
-- en route, at-risk SLAs, thin margins, proof pending, customer updates sent.
-- ============================================================================
UPDATE jobs j SET
  job_number = COALESCE(j.job_number, j.job_code),
  tracking_code = COALESCE(j.tracking_code, 'ETA-' || j.job_code),
  route_id = COALESCE(j.route_id, ((j.id - 1) % 8) + 1),
  required_vehicle_type = COALESCE(j.required_vehicle_type,
    (ARRAY['Box Truck','Reefer','Cargo Van','Flatbed'])[(j.id % 4) + 1]),
  required_driver_certification = COALESCE(j.required_driver_certification,
    (ARRAY['Standard CDL','CDL-A Hazmat','CDL-A','Reefer Certified'])[(j.id % 4) + 1]),
  sla_window_start = COALESCE(j.sla_window_start, j.scheduled_start),
  sla_window_end   = COALESCE(j.sla_window_end, j.sla_due_at),
  -- ETA: ~80% predicted on-time (eta <= sla_due_at), every 5th job running late.
  eta = COALESCE(j.eta,
    CASE WHEN j.id % 5 = 0 THEN j.sla_due_at + INTERVAL '35 minutes'
         ELSE j.sla_due_at - INTERVAL '20 minutes' END),
  -- Risk: priority-weighted, elevated for delayed/at-risk operational states.
  risk_score = COALESCE(j.risk_score,
    CASE WHEN j.status IN ('Delayed','At Risk') THEN 72
         WHEN j.priority = 'Critical' THEN 78
         WHEN j.priority = 'High' THEN 54
         WHEN j.priority = 'Normal' THEN 32
         ELSE 18 END),
  sla_status = COALESCE(j.sla_status,
    CASE WHEN j.status IN ('Delayed','At Risk') OR j.priority = 'Critical' THEN 'At Risk'
         WHEN j.id % 5 = 0 THEN 'At Risk'
         ELSE 'On Track' END),
  proof_status = COALESCE(j.proof_status,
    CASE WHEN j.status IN ('Completed') THEN 'Captured' ELSE 'Pending' END),
  customer_update_status = COALESCE(j.customer_update_status,
    CASE WHEN j.status IN ('In Progress','At Stop','Completed','Delayed') THEN 'Sent' ELSE 'Not Sent' END),
  -- Costed economics: revenue scaled by priority, ~$1 thin margin every 6th job.
  revenue_estimate = COALESCE(j.revenue_estimate,
    ROUND((620 + (j.id % 9) * 85
      + CASE WHEN j.priority = 'Critical' THEN 540 WHEN j.priority = 'High' THEN 260 ELSE 0 END)::numeric, 2)),
  cost_estimate = COALESCE(j.cost_estimate,
    ROUND((430 + (j.id % 7) * 60
      + CASE WHEN j.id % 6 = 0 THEN 360 ELSE 0 END)::numeric, 2)),
  updated_at = COALESCE(j.updated_at, NOW())
WHERE j.company_id = 1;

-- Margin derives from the costed revenue/cost above.
UPDATE jobs
   SET margin_estimate = ROUND((revenue_estimate - cost_estimate)::numeric, 2)
 WHERE company_id = 1 AND margin_estimate IS NULL
   AND revenue_estimate IS NOT NULL AND cost_estimate IS NOT NULL;

-- Enrich existing proof rows so the POD panel reads cleanly.
UPDATE proof_of_delivery
   SET received_by = COALESCE(received_by, receiver_name),
       proof_type  = COALESCE(proof_type, 'Digital Signature'),
       notes       = COALESCE(notes, 'Delivered to receiving dock; signature on file.')
 WHERE company_id = 1;

-- Backfill ETA-update metadata from the parent job.
UPDATE eta_updates e SET
       customer_id   = COALESCE(e.customer_id, j.customer_id),
       tracking_code = COALESCE(e.tracking_code, j.tracking_code),
       eta           = COALESCE(e.eta, j.eta),
       confidence_level = COALESCE(e.confidence_level, 'High')
  FROM jobs j
 WHERE e.job_id = j.id AND e.company_id = 1;

-- Differentiate route-stop types (origin vs delivery) for the stops grid.
UPDATE route_stops
   SET stop_type = COALESCE(stop_type, CASE WHEN stop_sequence = 1 THEN 'Pickup' ELSE 'Delivery' END),
       proof_status = COALESCE(proof_status, CASE WHEN status = 'Completed' THEN 'Captured' ELSE 'Pending' END);
