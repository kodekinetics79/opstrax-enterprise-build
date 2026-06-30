using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

public sealed class Stage9SchemaService(Database db)
{
    public async Task EnsureAsync(CancellationToken ct = default)
    {
        await db.ExecuteAsync("CREATE EXTENSION IF NOT EXISTS pgcrypto", ct: ct);

        foreach (var sql in Tables)
        {
            await db.ExecuteAsync(sql, ct: ct);
        }

        foreach (var sql in Indexes)
        {
            try
            {
                await db.ExecuteAsync(sql, ct: ct);
            }
            catch
            {
                // Stage 9 remains additive; existing local shapes should not block startup.
            }
        }
    }

    private static readonly string[] Tables =
    [
        @"CREATE TABLE IF NOT EXISTS smart_assignment_recommendations (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            job_id BIGINT NULL,
            trip_id BIGINT NULL,
            recommended_driver_id BIGINT NULL,
            recommended_vehicle_id BIGINT NULL,
            recommended_crew_id BIGINT NULL,
            recommendation_type VARCHAR(80) NOT NULL,
            score NUMERIC(6,3) NOT NULL DEFAULT 0,
            risk_level VARCHAR(40) NOT NULL DEFAULT 'medium',
            confidence_score NUMERIC(6,3) NOT NULL DEFAULT 0,
            reason_json JSONB NOT NULL DEFAULT '{}'::jsonb,
            constraint_json JSONB NOT NULL DEFAULT '{}'::jsonb,
            proposed_action_json JSONB NOT NULL DEFAULT '{}'::jsonb,
            status VARCHAR(40) NOT NULL DEFAULT 'draft',
            source_channel VARCHAR(40) NULL,
            client_generated_id VARCHAR(120) NULL,
            idempotency_key VARCHAR(160) NULL,
            created_by BIGINT NULL,
            correlation_id VARCHAR(120) NULL,
            causation_id VARCHAR(120) NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NULL
        )",
        @"CREATE TABLE IF NOT EXISTS assignment_confirmations (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            job_id BIGINT NULL,
            trip_id BIGINT NULL,
            driver_id BIGINT NULL,
            vehicle_id BIGINT NULL,
            status VARCHAR(40) NOT NULL DEFAULT 'pending',
            accepted_at TIMESTAMPTZ NULL,
            rejected_at TIMESTAMPTZ NULL,
            rejection_reason TEXT NULL,
            source_channel VARCHAR(40) NULL,
            client_generated_id VARCHAR(120) NULL,
            idempotency_key VARCHAR(160) NULL,
            device_id VARCHAR(120) NULL,
            mobile_app_version VARCHAR(80) NULL,
            metadata_json JSONB NULL,
            correlation_id VARCHAR(120) NULL,
            causation_id VARCHAR(120) NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NULL
        )",
        @"CREATE TABLE IF NOT EXISTS site_access_requirements (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            customer_id BIGINT NULL,
            address_id BIGINT NULL,
            job_id BIGINT NULL,
            trip_id BIGINT NULL,
            requirement_type VARCHAR(80) NOT NULL,
            status VARCHAR(40) NOT NULL DEFAULT 'required',
            required_before TIMESTAMPTZ NULL,
            instructions TEXT NULL,
            contact_name VARCHAR(160) NULL,
            contact_phone VARCHAR(40) NULL,
            source_channel VARCHAR(40) NULL,
            metadata_json JSONB NULL,
            correlation_id VARCHAR(120) NULL,
            causation_id VARCHAR(120) NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NULL
        )",
        @"CREATE TABLE IF NOT EXISTS access_documents (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            job_id BIGINT NULL,
            trip_id BIGINT NULL,
            site_access_requirement_id BIGINT NULL,
            document_type VARCHAR(80) NOT NULL,
            document_no VARCHAR(120) NULL,
            status VARCHAR(40) NOT NULL DEFAULT 'required',
            issued_by VARCHAR(160) NULL,
            issued_to VARCHAR(160) NULL,
            valid_from TIMESTAMPTZ NULL,
            valid_to TIMESTAMPTZ NULL,
            file_id BIGINT NULL,
            notes TEXT NULL,
            source_channel VARCHAR(40) NULL,
            captured_at TIMESTAMPTZ NULL,
            uploaded_at TIMESTAMPTZ NULL,
            captured_by_user_id BIGINT NULL,
            device_id VARCHAR(120) NULL,
            mobile_app_version VARCHAR(80) NULL,
            geo_latitude NUMERIC(10,7) NULL,
            geo_longitude NUMERIC(10,7) NULL,
            metadata_json JSONB NULL,
            correlation_id VARCHAR(120) NULL,
            causation_id VARCHAR(120) NULL,
            idempotency_key VARCHAR(160) NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NULL
        )",
        @"CREATE TABLE IF NOT EXISTS pickup_authorizations (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            job_id BIGINT NULL,
            trip_id BIGINT NULL,
            warehouse_id BIGINT NULL,
            third_party_name VARCHAR(160) NULL,
            authorization_no VARCHAR(120) NULL,
            authorized_person_name VARCHAR(160) NULL,
            authorized_person_phone VARCHAR(40) NULL,
            status VARCHAR(40) NOT NULL DEFAULT 'required',
            valid_from TIMESTAMPTZ NULL,
            valid_to TIMESTAMPTZ NULL,
            notes TEXT NULL,
            source_channel VARCHAR(40) NULL,
            captured_at TIMESTAMPTZ NULL,
            uploaded_at TIMESTAMPTZ NULL,
            captured_by_user_id BIGINT NULL,
            device_id VARCHAR(120) NULL,
            mobile_app_version VARCHAR(80) NULL,
            metadata_json JSONB NULL,
            correlation_id VARCHAR(120) NULL,
            causation_id VARCHAR(120) NULL,
            idempotency_key VARCHAR(160) NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NULL
        )",
        @"CREATE TABLE IF NOT EXISTS warehouse_handovers (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            job_id BIGINT NULL,
            trip_id BIGINT NULL,
            warehouse_name VARCHAR(160) NULL,
            warehouse_reference_no VARCHAR(120) NULL,
            handover_type VARCHAR(80) NOT NULL,
            status VARCHAR(40) NOT NULL DEFAULT 'scheduled',
            scheduled_at TIMESTAMPTZ NULL,
            completed_at TIMESTAMPTZ NULL,
            handled_by_name VARCHAR(160) NULL,
            notes TEXT NULL,
            source_channel VARCHAR(40) NULL,
            captured_at TIMESTAMPTZ NULL,
            uploaded_at TIMESTAMPTZ NULL,
            captured_by_user_id BIGINT NULL,
            device_id VARCHAR(120) NULL,
            mobile_app_version VARCHAR(80) NULL,
            geo_latitude NUMERIC(10,7) NULL,
            geo_longitude NUMERIC(10,7) NULL,
            metadata_json JSONB NULL,
            correlation_id VARCHAR(120) NULL,
            causation_id VARCHAR(120) NULL,
            idempotency_key VARCHAR(160) NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NULL
        )",
        @"CREATE TABLE IF NOT EXISTS proof_packages (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            job_id BIGINT NULL,
            trip_id BIGINT NULL,
            proof_type VARCHAR(80) NOT NULL,
            status VARCHAR(40) NOT NULL DEFAULT 'draft',
            completed_at TIMESTAMPTZ NULL,
            completed_by_user_id BIGINT NULL,
            receiver_name VARCHAR(160) NULL,
            receiver_phone VARCHAR(40) NULL,
            receiver_signature_file_id BIGINT NULL,
            geo_latitude NUMERIC(10,7) NULL,
            geo_longitude NUMERIC(10,7) NULL,
            notes TEXT NULL,
            validation_status VARCHAR(40) NOT NULL DEFAULT 'pending',
            validation_summary TEXT NULL,
            source_channel VARCHAR(40) NULL,
            client_generated_id VARCHAR(120) NULL,
            idempotency_key VARCHAR(160) NULL,
            captured_at TIMESTAMPTZ NULL,
            uploaded_at TIMESTAMPTZ NULL,
            captured_by_user_id BIGINT NULL,
            device_id VARCHAR(120) NULL,
            mobile_app_version VARCHAR(80) NULL,
            metadata_json JSONB NULL,
            correlation_id VARCHAR(120) NULL,
            causation_id VARCHAR(120) NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NULL
        )",
        @"CREATE TABLE IF NOT EXISTS proof_artifacts (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            proof_package_id BIGINT NOT NULL,
            artifact_type VARCHAR(80) NOT NULL,
            file_id BIGINT NULL,
            captured_at TIMESTAMPTZ NULL,
            uploaded_at TIMESTAMPTZ NULL,
            captured_by_user_id BIGINT NULL,
            geo_latitude NUMERIC(10,7) NULL,
            geo_longitude NUMERIC(10,7) NULL,
            device_id VARCHAR(120) NULL,
            mobile_app_version VARCHAR(80) NULL,
            source_channel VARCHAR(40) NULL,
            notes TEXT NULL,
            metadata_json JSONB NULL,
            idempotency_key VARCHAR(160) NULL,
            correlation_id VARCHAR(120) NULL,
            causation_id VARCHAR(120) NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NULL,
            CONSTRAINT fk_proof_artifacts_package FOREIGN KEY (proof_package_id) REFERENCES proof_packages(id) ON DELETE CASCADE
        )",
        @"CREATE TABLE IF NOT EXISTS billing_confidence_records (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            job_id BIGINT NULL,
            trip_id BIGINT NULL,
            proof_package_id BIGINT NULL,
            confidence_score NUMERIC(6,3) NOT NULL DEFAULT 0,
            status VARCHAR(40) NOT NULL DEFAULT 'pending',
            reason_json JSONB NOT NULL DEFAULT '{}'::jsonb,
            summary TEXT NULL,
            correlation_id VARCHAR(120) NULL,
            causation_id VARCHAR(120) NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NULL
        )"
    ];

    private static readonly string[] Indexes =
    [
        "CREATE INDEX IF NOT EXISTS idx_sar_company_job_status ON smart_assignment_recommendations (company_id, job_id, status, created_at DESC)",
        "CREATE INDEX IF NOT EXISTS idx_sar_company_trip_status ON smart_assignment_recommendations (company_id, trip_id, status, created_at DESC)",
        "CREATE UNIQUE INDEX IF NOT EXISTS uq_sar_company_idempotency_key ON smart_assignment_recommendations (company_id, idempotency_key) WHERE idempotency_key IS NOT NULL",
        "CREATE INDEX IF NOT EXISTS idx_ac_company_job_status ON assignment_confirmations (company_id, job_id, status, created_at DESC)",
        "CREATE INDEX IF NOT EXISTS idx_ac_company_trip_status ON assignment_confirmations (company_id, trip_id, status, created_at DESC)",
        "CREATE UNIQUE INDEX IF NOT EXISTS uq_ac_company_idempotency_key ON assignment_confirmations (company_id, idempotency_key) WHERE idempotency_key IS NOT NULL",
        "CREATE INDEX IF NOT EXISTS idx_sarq_company_job_status ON site_access_requirements (company_id, job_id, status, created_at DESC)",
        "CREATE INDEX IF NOT EXISTS idx_sarq_company_trip_status ON site_access_requirements (company_id, trip_id, status, created_at DESC)",
        "CREATE INDEX IF NOT EXISTS idx_ad_company_job_status ON access_documents (company_id, job_id, status, created_at DESC)",
        "CREATE INDEX IF NOT EXISTS idx_ad_company_requirement ON access_documents (company_id, site_access_requirement_id, status)",
        "CREATE UNIQUE INDEX IF NOT EXISTS uq_ad_company_idempotency_key ON access_documents (company_id, idempotency_key) WHERE idempotency_key IS NOT NULL",
        "CREATE INDEX IF NOT EXISTS idx_pa_company_job_status ON pickup_authorizations (company_id, job_id, status, created_at DESC)",
        "CREATE INDEX IF NOT EXISTS idx_pa_company_trip_status ON pickup_authorizations (company_id, trip_id, status, created_at DESC)",
        "CREATE UNIQUE INDEX IF NOT EXISTS uq_pa_company_idempotency_key ON pickup_authorizations (company_id, idempotency_key) WHERE idempotency_key IS NOT NULL",
        "CREATE INDEX IF NOT EXISTS idx_wh_company_job_status ON warehouse_handovers (company_id, job_id, status, created_at DESC)",
        "CREATE INDEX IF NOT EXISTS idx_wh_company_trip_status ON warehouse_handovers (company_id, trip_id, status, created_at DESC)",
        "CREATE UNIQUE INDEX IF NOT EXISTS uq_wh_company_idempotency_key ON warehouse_handovers (company_id, idempotency_key) WHERE idempotency_key IS NOT NULL",
        "CREATE INDEX IF NOT EXISTS idx_pp_company_job_status ON proof_packages (company_id, job_id, status, created_at DESC)",
        "CREATE INDEX IF NOT EXISTS idx_pp_company_trip_status ON proof_packages (company_id, trip_id, status, created_at DESC)",
        "CREATE UNIQUE INDEX IF NOT EXISTS uq_pp_company_idempotency_key ON proof_packages (company_id, idempotency_key) WHERE idempotency_key IS NOT NULL",
        "CREATE UNIQUE INDEX IF NOT EXISTS uq_pp_company_client_generated_id ON proof_packages (company_id, client_generated_id) WHERE client_generated_id IS NOT NULL",
        "CREATE INDEX IF NOT EXISTS idx_paft_company_package_type ON proof_artifacts (company_id, proof_package_id, artifact_type, created_at DESC)",
        "CREATE UNIQUE INDEX IF NOT EXISTS uq_paft_company_idempotency_key ON proof_artifacts (company_id, idempotency_key) WHERE idempotency_key IS NOT NULL",
        "CREATE INDEX IF NOT EXISTS idx_bcr_company_job_created ON billing_confidence_records (company_id, job_id, created_at DESC)",
        "CREATE INDEX IF NOT EXISTS idx_bcr_company_trip_created ON billing_confidence_records (company_id, trip_id, created_at DESC)",
        "CREATE INDEX IF NOT EXISTS idx_bcr_company_package_created ON billing_confidence_records (company_id, proof_package_id, created_at DESC)"
    ];
}

