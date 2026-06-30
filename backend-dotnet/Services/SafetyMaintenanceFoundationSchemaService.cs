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
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NULL,
            UNIQUE (company_id, scope_type, scope_value, snapshot_date)
        )"
    ];

    private static readonly string[] Indexes =
    [
        "CREATE INDEX IF NOT EXISTS idx_fhs_company_date ON fleet_health_snapshots(company_id, snapshot_date DESC)",
        "CREATE INDEX IF NOT EXISTS idx_fhs_company_score ON fleet_health_snapshots(company_id, fleet_health_score DESC, snapshot_date DESC)",
        "CREATE INDEX IF NOT EXISTS idx_fhs_company_scope ON fleet_health_snapshots(company_id, scope_type, scope_value)"
    ];
}
