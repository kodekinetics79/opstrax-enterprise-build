-- Stage 61 — Operational Proof Center production contract.
-- Terminal Stage58 is re-applied by the runner to enroll all company-owned tables in RLS.
BEGIN;

-- Proof artifacts reference real vault uploads. These additive columns are also
-- owned by the document module, but Stage61 makes its dependency explicit so a
-- predeploy-only schema cannot accept fabricated numeric evidence identifiers.
ALTER TABLE documents ADD COLUMN IF NOT EXISTS file_url VARCHAR(400) NULL;
ALTER TABLE documents ADD COLUMN IF NOT EXISTS deleted_at TIMESTAMPTZ NULL;

CREATE TABLE IF NOT EXISTS smart_assignment_recommendations (
 id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY, company_id BIGINT NOT NULL, job_id BIGINT NULL, trip_id BIGINT NULL,
 recommended_driver_id BIGINT NULL, recommended_vehicle_id BIGINT NULL, recommended_crew_id BIGINT NULL,
 recommendation_type VARCHAR(80) NOT NULL, score NUMERIC(6,3) NOT NULL DEFAULT 0, risk_level VARCHAR(40) NOT NULL DEFAULT 'medium',
 confidence_score NUMERIC(6,3) NOT NULL DEFAULT 0, reason_json JSONB NOT NULL DEFAULT '{}'::jsonb,
 constraint_json JSONB NOT NULL DEFAULT '{}'::jsonb, proposed_action_json JSONB NOT NULL DEFAULT '{}'::jsonb,
 status VARCHAR(40) NOT NULL DEFAULT 'draft', source_channel VARCHAR(40), client_generated_id VARCHAR(120),
 idempotency_key VARCHAR(160), created_by BIGINT, correlation_id VARCHAR(120), causation_id VARCHAR(120),
 created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(), updated_at TIMESTAMPTZ);

CREATE TABLE IF NOT EXISTS assignment_confirmations (
 id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY, company_id BIGINT NOT NULL, recommendation_id BIGINT NULL,
 job_id BIGINT, trip_id BIGINT, driver_id BIGINT, vehicle_id BIGINT, status VARCHAR(40) NOT NULL DEFAULT 'pending',
 accepted_at TIMESTAMPTZ, rejected_at TIMESTAMPTZ, rejection_reason TEXT, source_channel VARCHAR(40),
 client_generated_id VARCHAR(120), idempotency_key VARCHAR(160), device_id VARCHAR(120), mobile_app_version VARCHAR(80),
 metadata_json JSONB, correlation_id VARCHAR(120), causation_id VARCHAR(120), created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(), updated_at TIMESTAMPTZ);
ALTER TABLE assignment_confirmations ADD COLUMN IF NOT EXISTS recommendation_id BIGINT NULL;

CREATE TABLE IF NOT EXISTS site_access_requirements (
 id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY, company_id BIGINT NOT NULL, customer_id BIGINT, address_id BIGINT,
 job_id BIGINT, trip_id BIGINT, requirement_type VARCHAR(80) NOT NULL, status VARCHAR(40) NOT NULL DEFAULT 'required',
 required_before TIMESTAMPTZ, instructions TEXT, contact_name VARCHAR(160), contact_phone VARCHAR(40), source_channel VARCHAR(40),
 metadata_json JSONB, correlation_id VARCHAR(120), causation_id VARCHAR(120), created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(), updated_at TIMESTAMPTZ);

CREATE TABLE IF NOT EXISTS access_documents (
 id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY, company_id BIGINT NOT NULL, job_id BIGINT, trip_id BIGINT,
 site_access_requirement_id BIGINT, document_type VARCHAR(80) NOT NULL, document_no VARCHAR(120), status VARCHAR(40) NOT NULL DEFAULT 'required',
 issued_by VARCHAR(160), issued_to VARCHAR(160), valid_from TIMESTAMPTZ, valid_to TIMESTAMPTZ, file_id BIGINT, notes TEXT,
 source_channel VARCHAR(40), captured_at TIMESTAMPTZ, uploaded_at TIMESTAMPTZ, captured_by_user_id BIGINT, device_id VARCHAR(120),
 mobile_app_version VARCHAR(80), geo_latitude NUMERIC(10,7), geo_longitude NUMERIC(10,7), metadata_json JSONB,
 correlation_id VARCHAR(120), causation_id VARCHAR(120), idempotency_key VARCHAR(160), created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(), updated_at TIMESTAMPTZ);

CREATE TABLE IF NOT EXISTS pickup_authorizations (
 id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY, company_id BIGINT NOT NULL, job_id BIGINT, trip_id BIGINT, warehouse_id BIGINT,
 third_party_name VARCHAR(160), authorization_no VARCHAR(120), authorized_person_name VARCHAR(160), authorized_person_phone VARCHAR(40),
 status VARCHAR(40) NOT NULL DEFAULT 'required', valid_from TIMESTAMPTZ, valid_to TIMESTAMPTZ, notes TEXT, source_channel VARCHAR(40),
 captured_at TIMESTAMPTZ, uploaded_at TIMESTAMPTZ, captured_by_user_id BIGINT, device_id VARCHAR(120), mobile_app_version VARCHAR(80),
 metadata_json JSONB, correlation_id VARCHAR(120), causation_id VARCHAR(120), idempotency_key VARCHAR(160),
 created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(), updated_at TIMESTAMPTZ);

CREATE TABLE IF NOT EXISTS warehouse_handovers (
 id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY, company_id BIGINT NOT NULL, job_id BIGINT, trip_id BIGINT,
 warehouse_name VARCHAR(160), warehouse_reference_no VARCHAR(120), handover_type VARCHAR(80) NOT NULL,
 status VARCHAR(40) NOT NULL DEFAULT 'scheduled', scheduled_at TIMESTAMPTZ, completed_at TIMESTAMPTZ, handled_by_name VARCHAR(160),
 notes TEXT, source_channel VARCHAR(40), captured_at TIMESTAMPTZ, uploaded_at TIMESTAMPTZ, captured_by_user_id BIGINT,
 device_id VARCHAR(120), mobile_app_version VARCHAR(80), geo_latitude NUMERIC(10,7), geo_longitude NUMERIC(10,7), metadata_json JSONB,
 correlation_id VARCHAR(120), causation_id VARCHAR(120), idempotency_key VARCHAR(160), created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(), updated_at TIMESTAMPTZ);

CREATE TABLE IF NOT EXISTS proof_packages (
 id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY, company_id BIGINT NOT NULL, job_id BIGINT, trip_id BIGINT,
 proof_type VARCHAR(80) NOT NULL, status VARCHAR(40) NOT NULL DEFAULT 'draft', completed_at TIMESTAMPTZ, completed_by_user_id BIGINT,
 receiver_name VARCHAR(160), receiver_phone VARCHAR(40), receiver_signature_file_id BIGINT, geo_latitude NUMERIC(10,7), geo_longitude NUMERIC(10,7),
 notes TEXT, validation_status VARCHAR(40) NOT NULL DEFAULT 'pending', validation_summary TEXT, source_channel VARCHAR(40),
 client_generated_id VARCHAR(120), idempotency_key VARCHAR(160), captured_at TIMESTAMPTZ, uploaded_at TIMESTAMPTZ,
 captured_by_user_id BIGINT, device_id VARCHAR(120), mobile_app_version VARCHAR(80), metadata_json JSONB,
 correlation_id VARCHAR(120), causation_id VARCHAR(120), created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(), updated_at TIMESTAMPTZ);

CREATE TABLE IF NOT EXISTS proof_artifacts (
 id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY, company_id BIGINT NOT NULL,
 proof_package_id BIGINT NOT NULL REFERENCES proof_packages(id) ON DELETE CASCADE, artifact_type VARCHAR(80) NOT NULL,
 file_id BIGINT NOT NULL, captured_at TIMESTAMPTZ, uploaded_at TIMESTAMPTZ, captured_by_user_id BIGINT,
 geo_latitude NUMERIC(10,7), geo_longitude NUMERIC(10,7), device_id VARCHAR(120), mobile_app_version VARCHAR(80),
 source_channel VARCHAR(40), notes TEXT, metadata_json JSONB, idempotency_key VARCHAR(160), correlation_id VARCHAR(120),
 causation_id VARCHAR(120), created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(), updated_at TIMESTAMPTZ);

CREATE TABLE IF NOT EXISTS billing_confidence_records (
 id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY, company_id BIGINT NOT NULL, job_id BIGINT, trip_id BIGINT, proof_package_id BIGINT,
 confidence_score NUMERIC(6,3) NOT NULL DEFAULT 0, status VARCHAR(40) NOT NULL DEFAULT 'pending', reason_json JSONB NOT NULL DEFAULT '{}'::jsonb,
 summary TEXT, correlation_id VARCHAR(120), causation_id VARCHAR(120), created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(), updated_at TIMESTAMPTZ);

WITH ranked AS (SELECT id,ROW_NUMBER() OVER(PARTITION BY company_id,recommendation_id ORDER BY id DESC) rn FROM assignment_confirmations WHERE recommendation_id IS NOT NULL)
DELETE FROM assignment_confirmations WHERE id IN (SELECT id FROM ranked WHERE rn>1);
WITH ranked AS (SELECT id,ROW_NUMBER() OVER(PARTITION BY company_id,proof_package_id ORDER BY id DESC) rn FROM billing_confidence_records WHERE proof_package_id IS NOT NULL)
DELETE FROM billing_confidence_records WHERE id IN (SELECT id FROM ranked WHERE rn>1);

CREATE INDEX IF NOT EXISTS idx_sar_company_job_status ON smart_assignment_recommendations(company_id,job_id,status,created_at DESC);
CREATE UNIQUE INDEX IF NOT EXISTS uq_sar_company_idempotency_key ON smart_assignment_recommendations(company_id,idempotency_key) WHERE idempotency_key IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS uq_ac_company_recommendation ON assignment_confirmations(company_id,recommendation_id) WHERE recommendation_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_sarq_company_job_status ON site_access_requirements(company_id,job_id,status,created_at DESC);
CREATE UNIQUE INDEX IF NOT EXISTS uq_ad_company_idempotency_key ON access_documents(company_id,idempotency_key) WHERE idempotency_key IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS uq_pa_company_idempotency_key ON pickup_authorizations(company_id,idempotency_key) WHERE idempotency_key IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS uq_wh_company_idempotency_key ON warehouse_handovers(company_id,idempotency_key) WHERE idempotency_key IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS uq_pp_company_idempotency_key ON proof_packages(company_id,idempotency_key) WHERE idempotency_key IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS uq_pp_company_client_generated_id ON proof_packages(company_id,client_generated_id) WHERE client_generated_id IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS uq_paft_company_idempotency_key ON proof_artifacts(company_id,idempotency_key) WHERE idempotency_key IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_paft_company_package_type ON proof_artifacts(company_id,proof_package_id,artifact_type,created_at DESC);
CREATE UNIQUE INDEX IF NOT EXISTS uq_bcr_company_package ON billing_confidence_records(company_id,proof_package_id) WHERE proof_package_id IS NOT NULL;
COMMIT;
