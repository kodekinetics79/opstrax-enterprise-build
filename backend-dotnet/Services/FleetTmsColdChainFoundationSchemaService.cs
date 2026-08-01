using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

public sealed class FleetTmsColdChainFoundationSchemaService(Database db)
{
    public async Task EnsureAsync(CancellationToken ct = default)
    {
        foreach (var sql in Alters)
        {
            try
            {
                await db.ExecuteAsync(sql, ct: ct);
            }
            catch
            {
                // Additive bootstrapping only. Older local databases may miss some
                // columns; we keep startup resilient and non-destructive.
            }
        }

        foreach (var sql in Tables)
        {
            try
            {
                await db.ExecuteAsync(sql, ct: ct);
            }
            catch
            {
                // Keep cold-chain foundation startup tolerant of partial local state.
            }
        }

        foreach (var sql in Indexes)
        {
            // Idempotency and branch ownership indexes are correctness controls,
            // not optional performance hints. Fail startup if they cannot be enforced.
            await db.ExecuteAsync(sql, ct: ct);
        }
    }

    private static readonly string[] Alters =
    [
        @"ALTER TABLE fleet_tms_temperature_devices ADD COLUMN IF NOT EXISTS source_channel VARCHAR(40) NULL",
        @"ALTER TABLE fleet_tms_temperature_devices ADD COLUMN IF NOT EXISTS branch_id BIGINT NULL",
        @"ALTER TABLE fleet_tms_temperature_devices ADD COLUMN IF NOT EXISTS client_generated_id VARCHAR(120) NULL",
        @"ALTER TABLE fleet_tms_temperature_devices ADD COLUMN IF NOT EXISTS idempotency_key VARCHAR(160) NULL",
        @"ALTER TABLE fleet_tms_temperature_devices ADD COLUMN IF NOT EXISTS correlation_id VARCHAR(120) NULL",
        @"ALTER TABLE fleet_tms_temperature_devices ADD COLUMN IF NOT EXISTS causation_id VARCHAR(120) NULL",
        @"ALTER TABLE fleet_tms_temperature_devices ADD COLUMN IF NOT EXISTS metadata_json JSONB NULL",
        @"ALTER TABLE fleet_tms_temperature_readings ADD COLUMN IF NOT EXISTS source_channel VARCHAR(40) NULL",
        @"ALTER TABLE fleet_tms_temperature_readings ADD COLUMN IF NOT EXISTS branch_id BIGINT NULL",
        @"ALTER TABLE fleet_tms_temperature_readings ADD COLUMN IF NOT EXISTS client_generated_id VARCHAR(120) NULL",
        @"ALTER TABLE fleet_tms_temperature_readings ADD COLUMN IF NOT EXISTS idempotency_key VARCHAR(160) NULL",
        @"ALTER TABLE fleet_tms_temperature_readings ADD COLUMN IF NOT EXISTS correlation_id VARCHAR(120) NULL",
        @"ALTER TABLE fleet_tms_temperature_readings ADD COLUMN IF NOT EXISTS causation_id VARCHAR(120) NULL",
        @"ALTER TABLE fleet_tms_temperature_readings ADD COLUMN IF NOT EXISTS metadata_json JSONB NULL",
        @"ALTER TABLE fleet_tms_temperature_readings ADD COLUMN IF NOT EXISTS applied_policy_code VARCHAR(80) NULL",
        @"ALTER TABLE fleet_tms_temperature_readings ADD COLUMN IF NOT EXISTS applied_policy_scope VARCHAR(80) NULL",
        @"ALTER TABLE fleet_tms_temperature_readings ADD COLUMN IF NOT EXISTS applied_min_celsius NUMERIC(6,2) NULL",
        @"ALTER TABLE fleet_tms_temperature_readings ADD COLUMN IF NOT EXISTS applied_max_celsius NUMERIC(6,2) NULL",
        @"ALTER TABLE fleet_tms_temperature_alerts ADD COLUMN IF NOT EXISTS source_channel VARCHAR(40) NULL",
        @"ALTER TABLE fleet_tms_temperature_alerts ADD COLUMN IF NOT EXISTS branch_id BIGINT NULL",
        @"ALTER TABLE fleet_tms_temperature_alerts ADD COLUMN IF NOT EXISTS client_generated_id VARCHAR(120) NULL",
        @"ALTER TABLE fleet_tms_temperature_alerts ADD COLUMN IF NOT EXISTS idempotency_key VARCHAR(160) NULL",
        @"ALTER TABLE fleet_tms_temperature_alerts ADD COLUMN IF NOT EXISTS correlation_id VARCHAR(120) NULL",
        @"ALTER TABLE fleet_tms_temperature_alerts ADD COLUMN IF NOT EXISTS causation_id VARCHAR(120) NULL",
        @"ALTER TABLE fleet_tms_temperature_alerts ADD COLUMN IF NOT EXISTS metadata_json JSONB NULL",
        @"ALTER TABLE fleet_tms_temperature_alerts ADD COLUMN IF NOT EXISTS applied_policy_code VARCHAR(80) NULL",
        @"ALTER TABLE fleet_tms_temperature_alerts ADD COLUMN IF NOT EXISTS applied_policy_scope VARCHAR(80) NULL",
        @"ALTER TABLE fleet_tms_temperature_alerts ADD COLUMN IF NOT EXISTS acknowledged_at_utc TIMESTAMPTZ NULL",
        @"ALTER TABLE fleet_tms_temperature_alerts ADD COLUMN IF NOT EXISTS acknowledged_by VARCHAR(255) NULL",
        @"ALTER TABLE fleet_tms_temperature_alerts ADD COLUMN IF NOT EXISTS acknowledged_notes TEXT NULL",
        @"ALTER TABLE fleet_tms_cold_chain_reports ADD COLUMN IF NOT EXISTS source_channel VARCHAR(40) NULL",
        @"ALTER TABLE fleet_tms_cold_chain_reports ADD COLUMN IF NOT EXISTS branch_id BIGINT NULL",
        @"ALTER TABLE fleet_tms_cold_chain_reports ADD COLUMN IF NOT EXISTS client_generated_id VARCHAR(120) NULL",
        @"ALTER TABLE fleet_tms_cold_chain_reports ADD COLUMN IF NOT EXISTS idempotency_key VARCHAR(160) NULL",
        @"ALTER TABLE fleet_tms_cold_chain_reports ADD COLUMN IF NOT EXISTS correlation_id VARCHAR(120) NULL",
        @"ALTER TABLE fleet_tms_cold_chain_reports ADD COLUMN IF NOT EXISTS causation_id VARCHAR(120) NULL",
        @"ALTER TABLE fleet_tms_cold_chain_reports ADD COLUMN IF NOT EXISTS metadata_json JSONB NULL",
        @"ALTER TABLE fleet_tms_cold_chain_reports ADD COLUMN IF NOT EXISTS report_status VARCHAR(40) NOT NULL DEFAULT 'ready'"
    ];

    private static readonly string[] Tables =
    [
        @"CREATE TABLE IF NOT EXISTS fleet_tms_cold_chain_policies (
            id BIGINT NOT NULL GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            branch_id BIGINT NULL,
            policy_code VARCHAR(80) NOT NULL,
            scope_type VARCHAR(40) NOT NULL DEFAULT 'default',
            scope_key VARCHAR(120) NOT NULL DEFAULT '',
            min_celsius NUMERIC(6,2) NULL,
            max_celsius NUMERIC(6,2) NULL,
            humidity_min_percent NUMERIC(6,2) NULL,
            humidity_max_percent NUMERIC(6,2) NULL,
            severity VARCHAR(30) NOT NULL DEFAULT 'High',
            requires_acknowledgement BOOLEAN NOT NULL DEFAULT TRUE,
            status VARCHAR(30) NOT NULL DEFAULT 'Active',
            source_channel VARCHAR(40) NULL,
            client_generated_id VARCHAR(120) NULL,
            idempotency_key VARCHAR(160) NULL,
            correlation_id VARCHAR(120) NULL,
            causation_id VARCHAR(120) NULL,
            metadata_json JSONB NULL,
            notes TEXT NULL,
            created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at_utc TIMESTAMPTZ NULL
        )",
        @"CREATE TABLE IF NOT EXISTS fleet_tms_cold_chain_event_log (
            id BIGINT NOT NULL GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            branch_id BIGINT NULL,
            event_type VARCHAR(120) NOT NULL,
            aggregate_type VARCHAR(80) NOT NULL,
            aggregate_id VARCHAR(120) NOT NULL,
            payload_json JSONB NOT NULL DEFAULT '{}'::jsonb,
            correlation_id VARCHAR(120) NULL,
            causation_id VARCHAR(120) NULL,
            idempotency_key VARCHAR(160) NULL,
            status VARCHAR(30) NOT NULL DEFAULT 'processed',
            retry_count INT NOT NULL DEFAULT 0,
            error_message TEXT NULL,
            occurred_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            processed_at_utc TIMESTAMPTZ NULL,
            created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()
        )"
    ];

    private static readonly string[] Indexes =
    [
        "CREATE INDEX IF NOT EXISTS idx_ftms_ccpolicy_company_scope ON fleet_tms_cold_chain_policies (company_id, scope_type, scope_key, status)",
        "ALTER TABLE fleet_tms_cold_chain_policies ADD COLUMN IF NOT EXISTS branch_id BIGINT NULL",
        "ALTER TABLE fleet_tms_cold_chain_event_log ADD COLUMN IF NOT EXISTS branch_id BIGINT NULL",
        "CREATE INDEX IF NOT EXISTS idx_ftms_ccpolicy_branch ON fleet_tms_cold_chain_policies (company_id, branch_id)",
        "CREATE UNIQUE INDEX IF NOT EXISTS uq_ftms_ccpolicy_branch_scope ON fleet_tms_cold_chain_policies (company_id, COALESCE(branch_id, 0), policy_code, scope_type, scope_key)",
        "CREATE INDEX IF NOT EXISTS idx_ftms_cclog_branch ON fleet_tms_cold_chain_event_log (company_id, branch_id)",
        "DROP INDEX IF EXISTS uq_ftms_ccpolicy_company_idem",
        "CREATE UNIQUE INDEX IF NOT EXISTS uq_ftms_ccpolicy_branch_idem ON fleet_tms_cold_chain_policies (company_id, COALESCE(branch_id, 0), idempotency_key) WHERE idempotency_key IS NOT NULL",
        "CREATE UNIQUE INDEX IF NOT EXISTS uq_ftms_tread_branch_idem ON fleet_tms_temperature_readings (company_id, COALESCE(branch_id, 0), idempotency_key) WHERE idempotency_key IS NOT NULL",
        "CREATE UNIQUE INDEX IF NOT EXISTS uq_ftms_talert_branch_idem ON fleet_tms_temperature_alerts (company_id, COALESCE(branch_id, 0), idempotency_key) WHERE idempotency_key IS NOT NULL",
        "CREATE UNIQUE INDEX IF NOT EXISTS uq_ftms_tdev_tenant_code_norm ON fleet_tms_temperature_devices (company_id, LOWER(BTRIM(device_code)))",
        "CREATE UNIQUE INDEX IF NOT EXISTS uq_ftms_tdev_branch_idem ON fleet_tms_temperature_devices (company_id, COALESCE(branch_id, 0), idempotency_key) WHERE NULLIF(BTRIM(idempotency_key),'') IS NOT NULL",
        "CREATE UNIQUE INDEX IF NOT EXISTS uq_ftms_atype_tenant_code_norm ON fleet_tms_asset_types (company_id, LOWER(BTRIM(code)))",
        "CREATE INDEX IF NOT EXISTS idx_ftms_ccpolicy_company_code ON fleet_tms_cold_chain_policies (company_id, policy_code)",
        "CREATE INDEX IF NOT EXISTS idx_ftms_cclog_company_event ON fleet_tms_cold_chain_event_log (company_id, event_type, occurred_at_utc DESC)",
        "CREATE INDEX IF NOT EXISTS idx_ftms_cclog_company_agg ON fleet_tms_cold_chain_event_log (company_id, aggregate_type, aggregate_id, occurred_at_utc DESC)",
        "CREATE INDEX IF NOT EXISTS idx_ftms_cclog_company_status ON fleet_tms_cold_chain_event_log (company_id, status, retry_count, occurred_at_utc DESC)",
        "DROP INDEX IF EXISTS uq_ftms_cclog_company_idem",
        "CREATE UNIQUE INDEX IF NOT EXISTS uq_ftms_cclog_branch_event_idem ON fleet_tms_cold_chain_event_log (company_id, COALESCE(branch_id, 0), event_type, idempotency_key) WHERE idempotency_key IS NOT NULL"
    ];
}
