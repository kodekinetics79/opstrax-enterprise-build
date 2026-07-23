using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

// ─────────────────────────────────────────────────────────────────────────────
// FeatureFlagSchemaService — creates the table the REAL flag system reads/writes.
//
// The flag feature (FeatureFlagService, the Program.cs route kill-switch, and the
// UI's GET /api/feature-flags/evaluate) shipped WITHOUT a table owner, so on any
// database where the owner migration had not been hand-applied the first flag read
// died with `relation "feature_flags" does not exist` — taking tenant provisioning
// (TenantCreate → SeedDefaultsAsync) and the kill-switch middleware down with it.
//
// This service is the runtime owner: CREATE IF NOT EXISTS makes it self-healing on
// every boot (dev, SIT, prod). The matching owner migration
// (database/migrations/2026_07_14_feature_flags.sql) covers restricted-role prod
// environments where boot-time DDL is disabled.
//
// feature_flags is TENANT-SCOPED (company_id BIGINT) so it is enrolled into RLS by
// the Stage-19/22 tenant_isolation reconciliation like every other tenant table.
// ─────────────────────────────────────────────────────────────────────────────
public sealed class FeatureFlagSchemaService(Database db)
{
    public async Task EnsureAsync(CancellationToken ct = default)
    {
        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS feature_flags (
                id           BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                company_id   BIGINT       NOT NULL,
                flag_key     VARCHAR(120) NOT NULL,
                name         VARCHAR(200) NOT NULL,
                description  TEXT         NULL,
                enabled      BOOLEAN      NOT NULL DEFAULT TRUE,
                rollout_pct  INT          NOT NULL DEFAULT 100,
                environment  VARCHAR(40)  NOT NULL DEFAULT 'production',
                created_at   TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
                updated_at   TIMESTAMPTZ  NULL,
                CONSTRAINT uq_feature_flags_company_key UNIQUE (company_id, flag_key)
            )
            """, ct: ct);

        // Idempotent column reconciliation for any pre-existing partial table.
        foreach (var (col, def) in new[]
        {
            ("name",        "VARCHAR(200) NOT NULL DEFAULT ''"),
            ("description", "TEXT NULL"),
            ("enabled",     "BOOLEAN NOT NULL DEFAULT TRUE"),
            ("rollout_pct", "INT NOT NULL DEFAULT 100"),
            ("environment", "VARCHAR(40) NOT NULL DEFAULT 'production'"),
            ("created_at",  "TIMESTAMPTZ NOT NULL DEFAULT NOW()"),
            ("updated_at",  "TIMESTAMPTZ NULL"),
        })
        {
            await db.ExecuteAsync($"ALTER TABLE feature_flags ADD COLUMN IF NOT EXISTS {col} {def}", ct: ct);
        }

        await db.ExecuteAsync(
            "CREATE INDEX IF NOT EXISTS idx_feature_flags_company ON feature_flags (company_id, flag_key)", ct: ct);
    }
}
