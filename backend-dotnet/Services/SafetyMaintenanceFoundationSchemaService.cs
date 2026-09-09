using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

public sealed class SafetyMaintenanceFoundationSchemaService(Database db)
{
    public async Task EnsureAsync(CancellationToken ct = default)
    {
        foreach (var sql in Tables) await db.ExecuteAsync(sql, ct: ct);
        foreach (var sql in Indexes) { try { await db.ExecuteAsync(sql, ct: ct); } catch { } }
    }

    private static readonly string[] Tables =
    [
        @"CREATE TABLE IF NOT EXISTS fleet_health_snapshots (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            scope_type VARCHAR(40) NOT NULL DEFAULT 'company',
            scope_value VARCHAR(120) NOT NULL DEFAULT 'company',
            snapshot_date DATE NOT NULL,
            fleet_health_score DECIMAL(6,2) NOT NULL DEFAULT 0,
            safety_score DECIMAL(6,2) NOT NULL DEFAULT 0,
            maintenance_score DECIMAL(6,2) NOT NULL DEFAULT 0,
            telemetry_score DECIMAL(6,2) NOT NULL DEFAULT 0,
            risk_level VARCHAR(40) NOT NULL DEFAULT 'medium',
            reason_json JSONB NOT NULL DEFAULT '{}'::jsonb,
            next_action VARCHAR(160) NULL,
            data_origin VARCHAR(80) NOT NULL DEFAULT 'legacy_unverified',
            verification_status VARCHAR(80) NOT NULL DEFAULT 'unverified',
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NULL,
            UNIQUE (company_id, scope_type, scope_value, snapshot_date)
        )",
        @"ALTER TABLE fleet_health_snapshots
            ADD COLUMN IF NOT EXISTS data_origin VARCHAR(80) NOT NULL DEFAULT 'legacy_unverified',
            ADD COLUMN IF NOT EXISTS verification_status VARCHAR(80) NOT NULL DEFAULT 'unverified'",
        @"DO $$ BEGIN
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='ck_fleet_health_snapshot_evidence'
                           AND conrelid='fleet_health_snapshots'::regclass) THEN
                ALTER TABLE fleet_health_snapshots ADD CONSTRAINT ck_fleet_health_snapshot_evidence CHECK (
                    (data_origin='runtime_computed' AND verification_status='calculated_from_qualified_sources')
                    OR (data_origin='legacy_unverified' AND verification_status='unverified')
                    OR (data_origin='demo_seed' AND verification_status='demo_seed')) NOT VALID;
            END IF;
          END $$"
    ];

    private static readonly string[] Indexes =
    [
        "CREATE INDEX IF NOT EXISTS idx_fhs_company_date ON fleet_health_snapshots(company_id, snapshot_date DESC)",
        "CREATE INDEX IF NOT EXISTS idx_fhs_company_score ON fleet_health_snapshots(company_id, fleet_health_score DESC, snapshot_date DESC)",
        "CREATE INDEX IF NOT EXISTS idx_fhs_company_scope ON fleet_health_snapshots(company_id, scope_type, scope_value)",
        "CREATE INDEX IF NOT EXISTS idx_fhs_company_evidence_date ON fleet_health_snapshots(company_id,data_origin,verification_status,snapshot_date DESC)"
    ];
}
