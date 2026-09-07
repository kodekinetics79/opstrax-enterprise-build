using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

public sealed class TelemetrySchemaService(Database db)
{
    public async Task EnsureAsync(CancellationToken ct = default)
    {
        foreach (var sql in Tables) await db.ExecuteAsync(sql, ct: ct);
        foreach (var col in Columns) await EnsureColumnAsync(col.Table, col.Name, col.Definition, ct);
        foreach (var sql in Indexes) { try { await db.ExecuteAsync(sql, ct: ct); } catch { } }
        foreach (var sql in CredentialHardening) await db.ExecuteAsync(sql, ct: ct);
        foreach (var sql in Seeds) await db.ExecuteAsync(sql, ct: ct);
    }

    private async Task EnsureColumnAsync(string table, string column, string definition, CancellationToken ct)
    {
        var exists = await db.ScalarLongAsync(
            @"SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=current_schema() AND table_name=@table AND column_name=@column",
            c => { c.Parameters.AddWithValue("@table", table); c.Parameters.AddWithValue("@column", column); }, ct);
        if (exists == 0) await db.ExecuteAsync($"ALTER TABLE {table} ADD COLUMN {column} {definition}", ct: ct);
    }

    private sealed record ColumnDefinition(string Table, string Name, string Definition);

    private static readonly ColumnDefinition[] Columns =
    [
        // eld_devices security + lifecycle columns
        new("eld_devices", "company_id",   "BIGINT NOT NULL DEFAULT 1"),
        // Operator-recorded hardware identity. Presence alone never grants a
        // compatibility tier; the Stage115 registry stays ExternalHold-only.
        new("eld_devices", "manufacturer", "VARCHAR(120) NULL"),
        new("eld_devices", "hardware_revision", "VARCHAR(120) NULL"),
        // IMEI is the hardware GPS-tracker identifier (GT06/Concox/PT40-class) the trusted
        // gateway resolves a device by. An identifier, never a credential. Also created by
        // migration 2026_07_11_stage32_device_imei.sql for restricted-role prod that skips
        // this ensure; kept here so owner-capable envs self-heal and provisioning can write it.
        new("eld_devices", "imei",         "VARCHAR(32) NULL"),
        new("eld_devices", "api_key_hash", "VARCHAR(64) NULL"),
        new("eld_devices", "api_key_previous_hash", "VARCHAR(64) NULL"),
        new("eld_devices", "api_key_previous_valid_until", "TIMESTAMPTZ NULL"),
        new("eld_devices", "hmac_secret",  "VARCHAR(128) NULL"),
        // Diagnostics ingest reads only the envelope-encrypted credentials. The legacy
        // plaintext column remains temporarily for the older location ingest contract.
        new("eld_devices", "hmac_secret_encrypted", "TEXT NULL"),
        new("eld_devices", "hmac_previous_secret_encrypted", "TEXT NULL"),
        new("eld_devices", "hmac_previous_valid_until", "TIMESTAMPTZ NULL"),
        new("eld_devices", "last_seen_at", "TIMESTAMPTZ NULL"),
        new("eld_devices", "revoked_at",   "TIMESTAMPTZ NULL"),
        new("eld_devices", "updated_at",   "TIMESTAMPTZ NULL"),
        new("eld_devices", "deleted_at",   "TIMESTAMPTZ NULL"),
        // Stage119 enriches the Stage66 durable command ledger. Production gets
        // these columns from the migration; owner-capable local databases retain
        // parity when explicit runtime DDL is enabled.
        new("telematics_device_commands", "capability_id", "BIGINT NULL"),
        new("telematics_device_commands", "device_serial_snapshot", "VARCHAR(120) NULL"),
        new("telematics_device_commands", "manufacturer_snapshot", "VARCHAR(120) NULL"),
        new("telematics_device_commands", "device_model_snapshot", "VARCHAR(160) NULL"),
        new("telematics_device_commands", "hardware_revision_snapshot", "VARCHAR(120) NULL"),
        new("telematics_device_commands", "firmware_version_snapshot", "VARCHAR(120) NULL"),
        new("telematics_device_commands", "provider_snapshot", "VARCHAR(120) NULL"),
        new("telematics_device_commands", "command_class", "VARCHAR(24) NULL"),
        new("telematics_device_commands", "purpose", "VARCHAR(500) NULL"),
        new("telematics_device_commands", "source_reference", "VARCHAR(240) NULL"),
        new("telematics_device_commands", "safety_confirmation_hash", "VARCHAR(64) NULL"),
        new("telematics_device_commands", "governance_status", "VARCHAR(24) NOT NULL DEFAULT 'LegacyUnverified'"),
        new("telematics_device_commands", "provider_delivery_claim", "BOOLEAN NOT NULL DEFAULT FALSE"),
        new("telematics_device_commands", "physical_outcome_claim", "BOOLEAN NOT NULL DEFAULT FALSE"),
        // location_events telemetry enrichment
        // accuracy_meters is in the Batch1 CREATE, but a location_events table created by an
        // older path (pre-column) won't get it via CREATE IF NOT EXISTS — backfill idempotently
        // so the trip-breadcrumbs replay query doesn't 42703 on such DBs.
        new("location_events", "accuracy_meters", "DECIMAL(8,2) NULL"),
        new("location_events", "device_id",   "BIGINT NULL"),
        new("location_events", "received_at", "TIMESTAMPTZ NOT NULL DEFAULT NOW()"),
        new("location_events", "source",      "VARCHAR(40) NOT NULL DEFAULT 'device'"),
        new("location_events", "nonce",       "VARCHAR(128) NULL"),
        new("location_events", "source_channel", "VARCHAR(40) NULL"),
        new("location_events", "correlation_id", "VARCHAR(120) NULL"),
        new("location_events", "causation_id", "VARCHAR(120) NULL"),
        new("location_events", "client_generated_id", "VARCHAR(120) NULL"),
        new("location_events", "idempotency_key", "VARCHAR(120) NULL"),
        new("location_events", "ingest_fingerprint", "VARCHAR(64) NULL"),
        // Keep owner-capable fresh installs aligned with the committed polygon
        // geofence migration. GeofenceEvaluator always selects this column.
        new("geofences", "polygon_json", "JSONB NULL"),
        // Stage 66 made geofences branch-scoped. Keep the owner-capable startup
        // schema path aligned as well: GeofenceEvaluator and the geofence API
        // select branch_id unconditionally, so an older development database
        // must self-heal before the background worker begins polling.
        new("geofences", "branch_id", "BIGINT NULL"),
        new("telemetry_alerts", "correlation_id", "VARCHAR(120) NULL"),
        new("telemetry_alerts", "causation_id", "VARCHAR(120) NULL"),
        new("telemetry_alerts", "source_channel", "VARCHAR(40) NULL"),
        new("telemetry_alerts", "client_generated_id", "VARCHAR(120) NULL"),
        new("telemetry_alerts", "ai_recommendation_id", "BIGINT NULL"),
        new("latest_vehicle_positions", "source_event_id", "BIGINT NULL"),
        new("latest_vehicle_positions", "correlation_id", "VARCHAR(120) NULL"),
        new("latest_vehicle_positions", "causation_id", "VARCHAR(120) NULL"),
        new("latest_vehicle_positions", "source_channel", "VARCHAR(40) NULL"),
        new("latest_vehicle_positions", "telemetry_status", "VARCHAR(40) NULL"),
        new("latest_vehicle_positions", "risk_level", "VARCHAR(40) NULL"),
        new("latest_vehicle_positions", "alert_count", "INT NOT NULL DEFAULT 0"),
        new("latest_vehicle_positions", "open_alert_count", "INT NOT NULL DEFAULT 0"),
        new("latest_vehicle_positions", "next_action", "VARCHAR(160) NULL"),
        new("latest_vehicle_positions", "summary_json", "JSONB NULL"),
        new("latest_vehicle_positions", "updated_at", "TIMESTAMPTZ NULL"),
        // Provenance & trust metadata — EXACTLY mirrors migration
        // database/migrations/telematics/001_latest_position_provenance.sql so
        // owner-capable environments auto-create them here and production (which
        // runs as a restricted role and SKIPS this startup init) gets them from
        // migration 001. correlation_id is intentionally NOT re-declared (already
        // present above). Every read/write guards on
        // TelemetryProvenance.ColumnsAvailableAsync, so an environment where these
        // are still absent never 42703s ("column ... does not exist").
        new("latest_vehicle_positions", "source",              "TEXT NULL"),
        new("latest_vehicle_positions", "provider",            "TEXT NULL"),
        new("latest_vehicle_positions", "protocol",            "TEXT NULL"),
        new("latest_vehicle_positions", "adapter_version",     "TEXT NULL"),
        new("latest_vehicle_positions", "device_fix_time",     "TIMESTAMPTZ NULL"),
        new("latest_vehicle_positions", "gateway_received_at", "TIMESTAMPTZ NULL"),
        new("latest_vehicle_positions", "normalized_at",       "TIMESTAMPTZ NULL"),
        new("latest_vehicle_positions", "confidence",          "NUMERIC(4,3) NULL"),
        new("latest_vehicle_positions", "trust_score",         "NUMERIC(4,3) NULL"),
        new("latest_vehicle_positions", "quality_flags",       "JSONB NULL"),
    ];

    private static readonly string[] Tables =
    [
        @"CREATE TABLE IF NOT EXISTS latest_vehicle_positions (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            vehicle_id BIGINT NOT NULL,
            device_id BIGINT NULL,
            driver_id BIGINT NULL,
            lat DECIMAL(10,7) NOT NULL,
            lng DECIMAL(10,7) NOT NULL,
            speed_mph DECIMAL(6,2) NULL DEFAULT 0,
            heading SMALLINT NULL DEFAULT 0,
            accuracy_meters DECIMAL(8,2) NULL,
            engine_status VARCHAR(40) NULL,
            fuel_level DECIMAL(6,2) NULL,
            odometer_miles DECIMAL(12,2) NULL,
            battery_voltage DECIMAL(6,2) NULL,
            event_time TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            received_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            event_count BIGINT NOT NULL DEFAULT 1,
            UNIQUE (company_id, vehicle_id)
        )",

        @"CREATE TABLE IF NOT EXISTS telemetry_alerts (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            vehicle_id BIGINT NULL,
            device_id BIGINT NULL,
            driver_id BIGINT NULL,
            alert_type VARCHAR(60) NOT NULL,
            severity VARCHAR(40) NOT NULL DEFAULT 'Warning',
            message TEXT NOT NULL,
            source_event_id BIGINT NULL,
            status VARCHAR(40) NOT NULL DEFAULT 'Open',
            acknowledged_at TIMESTAMPTZ NULL,
            acknowledged_by VARCHAR(120) NULL,
            resolved_at TIMESTAMPTZ NULL,
            resolved_by VARCHAR(120) NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NULL
        )",

        // Durable nonce store: prevents replay within the retention window.
        // UNIQUE enforces one-use-per-device-nonce at DB level.
        // Rows older than 24 h are pruned by TelemetryBackgroundService.
        @"CREATE TABLE IF NOT EXISTS telemetry_nonces (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            device_id BIGINT NOT NULL,
            nonce VARCHAR(128) NOT NULL,
            used_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            UNIQUE (device_id, nonce)
        )",

        // Durable, cross-instance replay defense for the trusted-gateway path
        // (POST /api/telemetry/gps-ingest). The HMAC signature is the per-message identity;
        // UNIQUE(gateway_id, signature) makes 'already accepted?' atomic and shared across
        // instances/restarts. Not tenant-scoped, no RLS (infra ledger written before ownership
        // matters, like telemetry_nonces). device_id/company_id are recorded for audit scoping.
        // Rows older than the retention window are pruned by TelemetryBackgroundService.
        // Mirrored by migration 2026_07_14_stage33_gps_gateway_replay.sql for restricted prod.
        @"CREATE TABLE IF NOT EXISTS gps_gateway_replay (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            gateway_id VARCHAR(120) NOT NULL DEFAULT 'default',
            signature VARCHAR(256) NOT NULL,
            signed_at TIMESTAMPTZ NOT NULL,
            device_id BIGINT NULL,
            company_id BIGINT NULL,
            received_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            UNIQUE (gateway_id, signature)
        )",

        // Per-gateway credentials (H3): each trusted forwarding gateway has its OWN HMAC secret and is
        // bound to exactly one authorized tenant. Replaces the single shared fleet-wide secret; a device
        // resolved outside the gateway's company_id is rejected, closing the cross-tenant skeleton key.
        // secret_encrypted is envelope-encrypted (PiiProtectionService). company_id present but this is a
        // control-plane lookup table read pre-tenant-context (system scope); RLS-enrolled for defense.
        @"CREATE TABLE IF NOT EXISTS telemetry_gateways (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            gateway_id VARCHAR(120) NOT NULL,
            company_id BIGINT NOT NULL,
            gateway_name VARCHAR(220) NULL,
            secret_encrypted TEXT NOT NULL,
            status VARCHAR(20) NOT NULL DEFAULT 'active',
            last_seen_at TIMESTAMPTZ NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NULL,
            UNIQUE (gateway_id)
        )",

        // Per-tenant, per-rule configurable thresholds. Defaults seeded below.
        @"CREATE TABLE IF NOT EXISTS telemetry_rules (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            rule_type VARCHAR(60) NOT NULL,
            threshold_value DECIMAL(12,4) NOT NULL DEFAULT 65,
            severity VARCHAR(40) NOT NULL DEFAULT 'High',
            enabled BOOLEAN NOT NULL DEFAULT true,
            notes TEXT NULL,
            created_by BIGINT NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NULL,
            UNIQUE (company_id, rule_type)
        )",

        @"CREATE TABLE IF NOT EXISTS telemetry_live_asset_states (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            vehicle_id BIGINT NOT NULL,
            device_id BIGINT NULL,
            driver_id BIGINT NULL,
            vehicle_code VARCHAR(60) NULL,
            device_serial VARCHAR(120) NULL,
            driver_name VARCHAR(160) NULL,
            lat DECIMAL(10,7) NOT NULL,
            lng DECIMAL(10,7) NOT NULL,
            speed_mph DECIMAL(6,2) NULL DEFAULT 0,
            heading SMALLINT NULL DEFAULT 0,
            engine_status VARCHAR(40) NULL,
            telemetry_status VARCHAR(40) NOT NULL DEFAULT 'healthy',
            risk_level VARCHAR(40) NOT NULL DEFAULT 'low',
            alert_count INT NOT NULL DEFAULT 0,
            open_alert_count INT NOT NULL DEFAULT 0,
            stale_seconds BIGINT NOT NULL DEFAULT 0,
            last_event_time TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            received_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            source_event_id BIGINT NULL,
            correlation_id VARCHAR(120) NULL,
            causation_id VARCHAR(120) NULL,
            source_channel VARCHAR(40) NULL,
            next_action VARCHAR(160) NULL,
            summary_json JSONB NULL,
            updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            UNIQUE (company_id, vehicle_id)
        )",

        // Owner-capable empty-database bootstrap parity for Stage115. No rows are
        // seeded, and this table has no product mutation endpoint.
        @"CREATE TABLE IF NOT EXISTS device_compatibility_candidates (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            manufacturer VARCHAR(120) NOT NULL,
            device_model VARCHAR(160) NOT NULL,
            hardware_revision VARCHAR(120) NOT NULL,
            firmware_version VARCHAR(120) NOT NULL,
            software_candidate_sha VARCHAR(40) NOT NULL,
            engineering_status VARCHAR(24) NOT NULL DEFAULT 'Candidate',
            certification_status VARCHAR(24) NOT NULL DEFAULT 'ExternalHold',
            external_hold_reason VARCHAR(500) NOT NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NULL,
            CONSTRAINT ck_stage115_candidate_manufacturer CHECK (BTRIM(manufacturer) <> ''),
            CONSTRAINT ck_stage115_candidate_model CHECK (BTRIM(device_model) <> ''),
            CONSTRAINT ck_stage115_candidate_hardware_revision CHECK (BTRIM(hardware_revision) <> ''),
            CONSTRAINT ck_stage115_candidate_firmware CHECK (BTRIM(firmware_version) <> ''),
            CONSTRAINT ck_stage115_candidate_sha CHECK (software_candidate_sha ~ '^[0-9a-f]{40}$'),
            CONSTRAINT ck_stage115_candidate_engineering_status CHECK (engineering_status IN ('Candidate','Deferred','Rejected')),
            CONSTRAINT ck_stage115_candidate_external_hold CHECK (certification_status='ExternalHold'),
            CONSTRAINT ck_stage115_candidate_hold_reason CHECK (BTRIM(external_hold_reason) <> '')
        )",

        // Encrypted SIM/eSIM assignment history. Startup creates no profiles: every
        // row must come from an explicit operator action with a source reference.
        @"CREATE TABLE IF NOT EXISTS device_connectivity_profiles (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            branch_id BIGINT NULL,
            device_id BIGINT NOT NULL,
            profile_kind VARCHAR(20) NOT NULL,
            carrier_name VARCHAR(120) NOT NULL,
            iccid_encrypted TEXT NOT NULL,
            iccid_bidx VARCHAR(64) NOT NULL,
            iccid_last4 VARCHAR(4) NOT NULL,
            msisdn_encrypted TEXT NULL,
            msisdn_bidx VARCHAR(64) NULL,
            msisdn_last4 VARCHAR(4) NULL,
            apn_encrypted TEXT NULL,
            apn_bidx VARCHAR(64) NULL,
            apn_configured BOOLEAN NOT NULL DEFAULT FALSE,
            assignment_status VARCHAR(20) NOT NULL DEFAULT 'Assigned',
            effective_from TIMESTAMPTZ NOT NULL,
            effective_to TIMESTAMPTZ NULL,
            source_reference VARCHAR(240) NOT NULL,
            change_reason VARCHAR(500) NOT NULL,
            end_reason VARCHAR(500) NULL,
            idempotency_key UUID NOT NULL,
            created_by BIGINT NULL,
            ended_by BIGINT NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NULL,
            CONSTRAINT ck_stage116_profile_kind CHECK (profile_kind IN ('PhysicalSIM','eSIM')),
            CONSTRAINT ck_stage116_iccid_encrypted CHECK (iccid_encrypted LIKE 'enc:%'),
            CONSTRAINT ck_stage116_iccid_bidx CHECK (iccid_bidx ~ '^[0-9a-f]{64}$'),
            CONSTRAINT ck_stage116_iccid_last4 CHECK (iccid_last4 ~ '^[0-9]{4}$'),
            CONSTRAINT ck_stage116_assignment_lifecycle CHECK (
              (assignment_status='Assigned' AND effective_to IS NULL AND end_reason IS NULL AND ended_by IS NULL)
              OR (assignment_status='Ended' AND effective_to IS NOT NULL AND effective_to>effective_from
                  AND end_reason IS NOT NULL AND BTRIM(end_reason)<>''))
        )",

        // Firmware planning is deliberately non-executable. Both campaign and
        // target rows are fixed at ExternalHold with remote_upgrade_claim=false.
        @"CREATE TABLE IF NOT EXISTS device_firmware_campaigns (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            branch_id BIGINT NULL,
            campaign_name VARCHAR(160) NOT NULL,
            target_firmware_version VARCHAR(120) NOT NULL,
            rollback_firmware_version VARCHAR(120) NULL,
            rollout_strategy VARCHAR(20) NOT NULL,
            batch_size INT NOT NULL,
            scheduled_for TIMESTAMPTZ NOT NULL,
            maintenance_window_minutes INT NOT NULL,
            execution_status VARCHAR(24) NOT NULL DEFAULT 'ExternalHold',
            provider_capability_status VARCHAR(24) NOT NULL DEFAULT 'Unverified',
            remote_upgrade_claim BOOLEAN NOT NULL DEFAULT FALSE,
            external_hold_reason VARCHAR(500) NOT NULL,
            source_reference VARCHAR(240) NOT NULL,
            change_reason VARCHAR(500) NOT NULL,
            idempotency_key UUID NOT NULL,
            created_by BIGINT NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            CONSTRAINT ck_stage117_rollout_strategy CHECK (rollout_strategy IN ('Manual','Canary','Staged')),
            CONSTRAINT ck_stage117_batch_size CHECK (batch_size BETWEEN 1 AND 100),
            CONSTRAINT ck_stage117_window CHECK (maintenance_window_minutes BETWEEN 15 AND 720),
            CONSTRAINT ck_stage117_execution_hold CHECK (execution_status='ExternalHold'),
            CONSTRAINT ck_stage117_capability_unverified CHECK (provider_capability_status='Unverified'),
            CONSTRAINT ck_stage117_no_remote_claim CHECK (remote_upgrade_claim=FALSE),
            UNIQUE(company_id,idempotency_key)
        )",

        @"CREATE TABLE IF NOT EXISTS device_firmware_campaign_targets (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            branch_id BIGINT NULL,
            campaign_id BIGINT NOT NULL,
            device_id BIGINT NOT NULL,
            device_serial VARCHAR(120) NOT NULL,
            manufacturer VARCHAR(120) NULL,
            device_model VARCHAR(160) NULL,
            hardware_revision VARCHAR(120) NULL,
            reported_firmware_version VARCHAR(120) NULL,
            target_firmware_version VARCHAR(120) NOT NULL,
            planning_status VARCHAR(32) NOT NULL,
            planning_reason VARCHAR(500) NOT NULL,
            rollout_batch INT NOT NULL,
            delivery_status VARCHAR(24) NOT NULL DEFAULT 'ExternalHold',
            provider_capability_status VARCHAR(24) NOT NULL DEFAULT 'Unverified',
            remote_upgrade_claim BOOLEAN NOT NULL DEFAULT FALSE,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            CONSTRAINT ck_stage117_target_planning CHECK (planning_status IN ('ReadyForExternalEvidence','BlockedIdentity','AlreadyCurrent')),
            CONSTRAINT ck_stage117_target_batch CHECK (rollout_batch>=1),
            CONSTRAINT ck_stage117_target_delivery_hold CHECK (delivery_status='ExternalHold'),
            CONSTRAINT ck_stage117_target_capability_unverified CHECK (provider_capability_status='Unverified'),
            CONSTRAINT ck_stage117_target_no_remote_claim CHECK (remote_upgrade_claim=FALSE),
            UNIQUE(company_id,campaign_id,device_id)
        )",

        @"CREATE TABLE IF NOT EXISTS device_rma_cases (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL, branch_id BIGINT NULL, device_id BIGINT NOT NULL,
            device_serial VARCHAR(120) NOT NULL, manufacturer VARCHAR(120) NULL,
            device_model VARCHAR(160) NULL, hardware_revision VARCHAR(120) NULL,
            reported_firmware_version VARCHAR(120) NULL, severity VARCHAR(2) NOT NULL,
            failure_category VARCHAR(40) NOT NULL, failure_description VARCHAR(1000) NOT NULL,
            observed_at TIMESTAMPTZ NOT NULL, warranty_posture VARCHAR(32) NOT NULL,
            warranty_reference VARCHAR(240) NULL, warranty_evidence_status VARCHAR(24) NOT NULL DEFAULT 'Unverified',
            support_sla_reference VARCHAR(240) NOT NULL, response_due_at TIMESTAMPTZ NOT NULL,
            source_reference VARCHAR(240) NOT NULL, physical_evidence_claim BOOLEAN NOT NULL DEFAULT FALSE,
            idempotency_key UUID NOT NULL, created_by BIGINT NULL, created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            CONSTRAINT ck_stage118_case_severity CHECK (severity IN ('P0','P1','P2','P3')),
            CONSTRAINT ck_stage118_warranty_unverified CHECK (warranty_evidence_status='Unverified'),
            CONSTRAINT ck_stage118_case_no_physical_claim CHECK (physical_evidence_claim=FALSE),
            UNIQUE(company_id,idempotency_key)
        )",

        @"CREATE TABLE IF NOT EXISTS device_rma_events (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL, branch_id BIGINT NULL, case_id BIGINT NOT NULL, device_id BIGINT NOT NULL,
            sequence_number INT NOT NULL, event_type VARCHAR(40) NOT NULL, case_status_after VARCHAR(32) NOT NULL,
            occurred_at TIMESTAMPTZ NOT NULL, custody_location VARCHAR(240) NULL,
            tracking_reference VARCHAR(240) NULL, evidence_reference VARCHAR(240) NOT NULL,
            evidence_status VARCHAR(24) NOT NULL DEFAULT 'Unverified', notes VARCHAR(1000) NOT NULL,
            physical_completion_claim BOOLEAN NOT NULL DEFAULT FALSE, idempotency_key UUID NOT NULL,
            recorded_by BIGINT NULL, recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            CONSTRAINT ck_stage118_event_evidence_unverified CHECK (evidence_status='Unverified'),
            CONSTRAINT ck_stage118_event_no_physical_claim CHECK (physical_completion_claim=FALSE),
            UNIQUE(company_id,case_id,sequence_number), UNIQUE(company_id,idempotency_key)
        )",

        @"CREATE TABLE IF NOT EXISTS device_rma_replacements (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL, branch_id BIGINT NULL, case_id BIGINT NOT NULL,
            failed_device_id BIGINT NOT NULL, failed_device_serial VARCHAR(120) NOT NULL,
            replacement_device_id BIGINT NOT NULL, replacement_device_serial VARCHAR(120) NOT NULL,
            replacement_manufacturer VARCHAR(120) NULL, replacement_device_model VARCHAR(160) NULL,
            replacement_hardware_revision VARCHAR(120) NULL, replacement_firmware_version VARCHAR(120) NULL,
            replacement_status VARCHAR(24) NOT NULL DEFAULT 'Planned', physical_swap_status VARCHAR(24) NOT NULL DEFAULT 'ExternalHold',
            physical_swap_claim BOOLEAN NOT NULL DEFAULT FALSE, change_reason VARCHAR(500) NOT NULL,
            source_reference VARCHAR(240) NOT NULL, idempotency_key UUID NOT NULL,
            created_by BIGINT NULL, created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            CONSTRAINT ck_stage118_replacement_distinct CHECK (failed_device_id<>replacement_device_id),
            CONSTRAINT ck_stage118_replacement_status CHECK (replacement_status='Planned'),
            CONSTRAINT ck_stage118_swap_external_hold CHECK (physical_swap_status='ExternalHold'),
            CONSTRAINT ck_stage118_no_swap_claim CHECK (physical_swap_claim=FALSE),
            UNIQUE(company_id,case_id), UNIQUE(company_id,idempotency_key)
        )",

        @"CREATE TABLE IF NOT EXISTS device_command_capabilities (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL, branch_id BIGINT NULL, device_id BIGINT NOT NULL,
            device_serial VARCHAR(120) NOT NULL, manufacturer VARCHAR(120) NOT NULL,
            device_model VARCHAR(160) NOT NULL, hardware_revision VARCHAR(120) NOT NULL,
            firmware_version VARCHAR(120) NOT NULL, provider VARCHAR(120) NOT NULL,
            command_type VARCHAR(60) NOT NULL, command_class VARCHAR(24) NOT NULL,
            capability_status VARCHAR(24) NOT NULL DEFAULT 'Unverified', evidence_source VARCHAR(40) NOT NULL,
            evidence_reference VARCHAR(240) NOT NULL, observed_at TIMESTAMPTZ NOT NULL,
            expires_at TIMESTAMPTZ NOT NULL, physical_evidence_claim BOOLEAN NOT NULL DEFAULT FALSE,
            certification_claim BOOLEAN NOT NULL DEFAULT FALSE, recorded_by BIGINT NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(), updated_at TIMESTAMPTZ NULL,
            CONSTRAINT ck_stage119_capability_type CHECK (command_type IN ('RequestPosition','RequestDiagnostics','RestartDevice')),
            CONSTRAINT ck_stage119_capability_status CHECK (capability_status IN ('Unverified','Verified','Rejected','Revoked')),
            CONSTRAINT ck_stage119_capability_no_physical_claim CHECK (physical_evidence_claim=FALSE),
            CONSTRAINT ck_stage119_capability_no_certification_claim CHECK (certification_claim=FALSE)
        )",

        @"CREATE TABLE IF NOT EXISTS device_connectivity_observations (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL, branch_id BIGINT NULL, device_id BIGINT NOT NULL,
            connectivity_profile_id BIGINT NOT NULL, profile_iccid_bidx_snapshot VARCHAR(64) NOT NULL,
            profile_iccid_last4 VARCHAR(4) NOT NULL, source_provider VARCHAR(80) NOT NULL,
            source_account_bidx VARCHAR(64) NOT NULL, source_observation_bidx VARCHAR(64) NOT NULL,
            payload_sha256 VARCHAR(64) NOT NULL, source_authentication_status VARCHAR(24) NOT NULL DEFAULT 'Authenticated',
            subscription_status VARCHAR(24) NOT NULL, network_registration_status VARCHAR(24) NOT NULL,
            data_session_status VARCHAR(24) NOT NULL, usage_bytes BIGINT NULL, roaming BOOLEAN NULL,
            observed_at TIMESTAMPTZ NOT NULL, received_at TIMESTAMPTZ NOT NULL,
            reconciliation_status VARCHAR(32) NOT NULL DEFAULT 'ExactCurrentProfile',
            provider_verified_claim BOOLEAN NOT NULL DEFAULT FALSE,
            physical_connectivity_claim BOOLEAN NOT NULL DEFAULT FALSE,
            certification_claim BOOLEAN NOT NULL DEFAULT FALSE,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            CONSTRAINT ck_stage120_observation_no_provider_claim CHECK (provider_verified_claim=FALSE),
            CONSTRAINT ck_stage120_observation_no_physical_claim CHECK (physical_connectivity_claim=FALSE),
            CONSTRAINT ck_stage120_observation_no_certification_claim CHECK (certification_claim=FALSE)
        )",
    ];

    private static readonly string[] Indexes =
    [
        "CREATE INDEX IF NOT EXISTS idx_ta_company_status ON telemetry_alerts(company_id, status)",
        "CREATE INDEX IF NOT EXISTS idx_ta_vehicle ON telemetry_alerts(vehicle_id, company_id)",
        "CREATE INDEX IF NOT EXISTS idx_ta_type ON telemetry_alerts(company_id, alert_type, vehicle_id)",
        "CREATE INDEX IF NOT EXISTS idx_le_device ON location_events(device_id, event_time)",
        "CREATE INDEX IF NOT EXISTS idx_le_received ON location_events(company_id, received_at)",
        "CREATE INDEX IF NOT EXISTS idx_eld_apikey ON eld_devices(api_key_hash)",
        "CREATE INDEX IF NOT EXISTS idx_eld_company ON eld_devices(company_id, status)",
        // Globally-unique IMEI lookup (partial: many devices legitimately have no IMEI).
        // Matches ux_eld_devices_imei from migration stage32 so both provisioning paths agree.
        "CREATE UNIQUE INDEX IF NOT EXISTS ux_eld_devices_imei ON eld_devices(imei) WHERE imei IS NOT NULL",
        "CREATE INDEX IF NOT EXISTS idx_lvp_tenant ON latest_vehicle_positions(company_id, received_at)",
        // Device-first latest-position reads back the paged GPS/diagnostics workspaces;
        // keep the lateral lookup bounded at large-fleet volume.
        "CREATE INDEX IF NOT EXISTS idx_lvp_company_device_received ON latest_vehicle_positions(company_id, device_id, received_at DESC, id DESC) WHERE device_id IS NOT NULL",
        "CREATE INDEX IF NOT EXISTS idx_lvp_status ON latest_vehicle_positions(company_id, telemetry_status, risk_level)",
        "CREATE INDEX IF NOT EXISTS idx_tn_device_used ON telemetry_nonces(device_id, used_at)",
        "CREATE INDEX IF NOT EXISTS idx_ggr_received ON gps_gateway_replay(received_at)",
        "CREATE INDEX IF NOT EXISTS idx_tr_company ON telemetry_rules(company_id, rule_type, enabled)",
        "CREATE INDEX IF NOT EXISTS idx_tlsa_company_updated ON telemetry_live_asset_states(company_id, updated_at)",
        "CREATE INDEX IF NOT EXISTS idx_tlsa_company_risk ON telemetry_live_asset_states(company_id, risk_level, open_alert_count)",
        @"CREATE UNIQUE INDEX IF NOT EXISTS uq_stage115_device_compatibility_tuple_candidate
          ON device_compatibility_candidates (
            UPPER(BTRIM(manufacturer)),UPPER(BTRIM(device_model)),
            UPPER(BTRIM(hardware_revision)),UPPER(BTRIM(firmware_version)),software_candidate_sha)",
        @"CREATE INDEX IF NOT EXISTS ix_stage115_device_compatibility_lookup
          ON device_compatibility_candidates (
            UPPER(BTRIM(manufacturer)),UPPER(BTRIM(device_model)),
            UPPER(BTRIM(hardware_revision)),UPPER(BTRIM(firmware_version)),created_at DESC,id DESC)",
        @"CREATE UNIQUE INDEX IF NOT EXISTS uq_stage116_connectivity_current_device
          ON device_connectivity_profiles(company_id,device_id) WHERE effective_to IS NULL",
        @"CREATE UNIQUE INDEX IF NOT EXISTS uq_stage116_connectivity_current_iccid
          ON device_connectivity_profiles(iccid_bidx) WHERE effective_to IS NULL",
        @"CREATE UNIQUE INDEX IF NOT EXISTS uq_stage116_connectivity_idempotency
          ON device_connectivity_profiles(company_id,device_id,idempotency_key)",
        @"CREATE INDEX IF NOT EXISTS ix_stage116_connectivity_history
          ON device_connectivity_profiles(company_id,device_id,effective_from DESC,id DESC)",
        @"CREATE INDEX IF NOT EXISTS ix_stage117_campaigns_company_schedule
          ON device_firmware_campaigns(company_id,scheduled_for DESC,id DESC)",
        @"CREATE INDEX IF NOT EXISTS ix_stage117_targets_device_recent
          ON device_firmware_campaign_targets(company_id,device_id,created_at DESC,id DESC)",
        @"CREATE INDEX IF NOT EXISTS ix_stage117_targets_campaign_batch
          ON device_firmware_campaign_targets(company_id,campaign_id,rollout_batch,id)",
        @"CREATE INDEX IF NOT EXISTS ix_stage118_cases_device_recent
          ON device_rma_cases(company_id,device_id,created_at DESC,id DESC)",
        @"CREATE INDEX IF NOT EXISTS ix_stage118_cases_severity_due
          ON device_rma_cases(company_id,severity,response_due_at,id)",
        @"CREATE INDEX IF NOT EXISTS ix_stage118_events_case_sequence
          ON device_rma_events(company_id,case_id,sequence_number)",
        @"CREATE INDEX IF NOT EXISTS ix_stage118_replacements_device
          ON device_rma_replacements(company_id,replacement_device_id,created_at DESC)",
        @"CREATE UNIQUE INDEX IF NOT EXISTS uq_stage119_capability_evidence
          ON device_command_capabilities(company_id,device_id,command_type,evidence_reference)",
        @"CREATE INDEX IF NOT EXISTS ix_stage119_capability_lookup
          ON device_command_capabilities(company_id,device_id,command_type,capability_status,expires_at DESC,id DESC)",
        @"CREATE INDEX IF NOT EXISTS ix_stage119_commands_device_recent
          ON telematics_device_commands(company_id,device_id,created_at DESC,id DESC)",
        @"CREATE INDEX IF NOT EXISTS ix_stage119_commands_capability
          ON telematics_device_commands(company_id,capability_id,created_at DESC,id DESC) WHERE capability_id IS NOT NULL",
        @"CREATE UNIQUE INDEX IF NOT EXISTS uq_stage120_connectivity_profile_owner
          ON device_connectivity_profiles(company_id,id,device_id)",
        @"CREATE INDEX IF NOT EXISTS ix_stage120_observations_device_recent
          ON device_connectivity_observations(company_id,device_id,observed_at DESC,id DESC)",
        @"CREATE INDEX IF NOT EXISTS ix_stage120_observations_profile_recent
          ON device_connectivity_observations(company_id,connectivity_profile_id,observed_at DESC,id DESC)",
    ];

    private static readonly string[] Seeds =
    [
        // Seed default speeding rule for every company that has devices
        @"INSERT INTO telemetry_rules (company_id, rule_type, threshold_value, severity, enabled)
          SELECT DISTINCT company_id, 'speeding', 65, 'High', true
          FROM eld_devices WHERE company_id IS NOT NULL AND company_id > 0
          ON CONFLICT DO NOTHING",
        // Seed default stale-device rule (900 seconds = 15 minutes)
        @"INSERT INTO telemetry_rules (company_id, rule_type, threshold_value, severity, enabled)
          SELECT DISTINCT company_id, 'stale_device', 900, 'Warning', true
          FROM eld_devices WHERE company_id IS NOT NULL AND company_id > 0
          ON CONFLICT DO NOTHING",
        // Excessive-idling window in minutes (OperationalAlertDetectionService)
        @"INSERT INTO telemetry_rules (company_id, rule_type, threshold_value, severity, enabled)
          SELECT DISTINCT company_id, 'idling', 15, 'Warning', true
          FROM eld_devices WHERE company_id IS NOT NULL AND company_id > 0
          ON CONFLICT DO NOTHING",
        // Fuel-level drop (percentage points inside 45 min) that counts as an anomaly
        @"INSERT INTO telemetry_rules (company_id, rule_type, threshold_value, severity, enabled)
          SELECT DISTINCT company_id, 'fuel_drop_pct', 20, 'High', true
          FROM eld_devices WHERE company_id IS NOT NULL AND company_id > 0
          ON CONFLICT DO NOTHING",
    ];

    private static readonly string[] CredentialHardening =
    [
        @"DO $$ BEGIN
            IF EXISTS (
              SELECT api_hash FROM (
                SELECT api_key_hash api_hash FROM eld_devices WHERE api_key_hash IS NOT NULL AND deleted_at IS NULL
                UNION ALL
                SELECT api_key_previous_hash FROM eld_devices WHERE api_key_previous_hash IS NOT NULL AND deleted_at IS NULL
              ) hashes GROUP BY api_hash HAVING COUNT(*) > 1
            ) THEN RAISE EXCEPTION 'Duplicate current/previous device API-key hashes require reconciliation'; END IF;
          END $$",
        "CREATE UNIQUE INDEX IF NOT EXISTS uq_eld_devices_api_key_hash ON eld_devices(api_key_hash) WHERE api_key_hash IS NOT NULL AND deleted_at IS NULL",
        "CREATE UNIQUE INDEX IF NOT EXISTS uq_eld_devices_api_key_previous_hash ON eld_devices(api_key_previous_hash) WHERE api_key_previous_hash IS NOT NULL AND deleted_at IS NULL",
        // Never manufacture credentials during schema startup. Legacy or incomplete
        // devices are quarantined until an operator explicitly rotates credentials.
        @"UPDATE eld_devices
          SET api_key_hash = NULL,
              hmac_secret = NULL,
              status = 'CredentialRotationRequired',
              revoked_at = COALESCE(revoked_at, NOW()),
              updated_at = NOW()
          WHERE deleted_at IS NULL
            AND (
                api_key_hash IS NULL
                OR btrim(api_key_hash) = ''
                OR api_key_hash !~ '^[0-9a-fA-F]{64}$'
                OR hmac_secret_encrypted IS NULL
                OR btrim(hmac_secret_encrypted) = ''
                OR length(hmac_secret_encrypted) < 24
                OR api_key_hash = encode(sha256(('opstrax-' || 'dev-' || device_serial)::bytea), 'hex')
            )",
        "ALTER TABLE eld_devices DROP CONSTRAINT IF EXISTS ck_eld_devices_active_credentials",
        @"ALTER TABLE eld_devices ADD CONSTRAINT ck_eld_devices_active_credentials CHECK (
            LOWER(status) <> 'active' OR (
              api_key_hash IS NOT NULL AND api_key_hash ~ '^[0-9a-fA-F]{64}$'
              AND hmac_secret IS NULL
              AND hmac_secret_encrypted IS NOT NULL AND length(btrim(hmac_secret_encrypted)) >= 24
              AND hmac_key_version > 0
              AND revoked_at IS NULL
            )) NOT VALID",
        // The quarantine immediately above removes every violating active row, so
        // validation must complete before startup can advertise fleet readiness.
        // Leaving the replacement NOT VALID silently undid Stage 82 on every boot.
        "ALTER TABLE eld_devices VALIDATE CONSTRAINT ck_eld_devices_active_credentials",
    ];
}
