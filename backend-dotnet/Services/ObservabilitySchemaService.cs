using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

// ─────────────────────────────────────────────────────────────────────────────
// P9 Observability Schema
// Creates:
//   service_run_history  — append-only per-cycle execution log for all
//                          background hosted services
//   service_heartbeats   — one row per service, updated on every successful run
//                          and each in-cycle heartbeat
//   platform_incidents   — operational incidents auto-created when services
//                          report repeated consecutive failures
// ─────────────────────────────────────────────────────────────────────────────

public sealed class ObservabilitySchemaService(Database db)
{
    public async Task EnsureAsync()
    {
        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS service_run_history (
                id                  BIGINT       GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                service_name        VARCHAR(100) NOT NULL,
                status              VARCHAR(20)
                                    NOT NULL DEFAULT 'running'
                                    CHECK (status IN ('running','succeeded','failed','degraded')),
                started_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
                finished_at         TIMESTAMPTZ  NULL,
                duration_ms         INT          NULL,
                processed_count     INT          NOT NULL DEFAULT 0,
                failed_count        INT          NOT NULL DEFAULT 0,
                error_code          VARCHAR(100) NULL,
                error_message_safe  TEXT         NULL,
                next_run_at         TIMESTAMPTZ  NULL,
                heartbeat_at        TIMESTAMPTZ  NULL
            )
            """);

        await db.ExecuteAsync("""
            CREATE INDEX IF NOT EXISTS idx_srh_service ON service_run_history (service_name)
            """);
        await db.ExecuteAsync("""
            CREATE INDEX IF NOT EXISTS idx_srh_started ON service_run_history (started_at)
            """);
        await db.ExecuteAsync("""
            CREATE INDEX IF NOT EXISTS idx_srh_status ON service_run_history (status)
            """);

        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS service_heartbeats (
                service_name          VARCHAR(100) PRIMARY KEY,
                last_heartbeat_at     TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
                last_run_at           TIMESTAMPTZ  NULL,
                last_run_status       VARCHAR(50)  NULL,
                consecutive_failures  INT          NOT NULL DEFAULT 0,
                last_error_safe       TEXT         NULL,
                updated_at            TIMESTAMPTZ  NOT NULL DEFAULT NOW()
            )
            """);

        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS platform_incidents (
                id               BIGINT       GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                company_id       BIGINT       NULL,
                severity         VARCHAR(20)
                                 NOT NULL DEFAULT 'medium'
                                 CHECK (severity IN ('critical','high','medium','low','info')),
                source_service   VARCHAR(100) NOT NULL,
                source_event     VARCHAR(200) NOT NULL,
                status           VARCHAR(20)
                                 NOT NULL DEFAULT 'open'
                                 CHECK (status IN ('open','investigating','mitigated','resolved')),
                title            VARCHAR(500) NOT NULL,
                safe_description TEXT         NULL,
                opened_at        TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
                resolved_at      TIMESTAMPTZ  NULL,
                assigned_to      VARCHAR(200) NULL
            )
            """);

        await db.ExecuteAsync("""
            CREATE INDEX IF NOT EXISTS idx_pi_status ON platform_incidents (status)
            """);
        await db.ExecuteAsync("""
            CREATE INDEX IF NOT EXISTS idx_pi_service ON platform_incidents (source_service)
            """);
        await db.ExecuteAsync("""
            CREATE INDEX IF NOT EXISTS idx_pi_opened ON platform_incidents (opened_at)
            """);

        // ── Incident audit-trail columns (Requirement 9) ─────────────────────────
        // Additive-only ALTERs so existing incident rows are preserved. Every
        // incident can now record its full lifecycle (ack → resolve), the affected
        // service/tenants, root cause + actions taken, and be linked back to the
        // trace_id and deployment_version that produced it — closing the loop from
        // an alert to the exact request that failed.
        foreach (var alter in new[]
        {
            "ALTER TABLE platform_incidents ADD COLUMN IF NOT EXISTS acknowledged_at    TIMESTAMPTZ  NULL",
            "ALTER TABLE platform_incidents ADD COLUMN IF NOT EXISTS acknowledged_by    VARCHAR(200) NULL",
            "ALTER TABLE platform_incidents ADD COLUMN IF NOT EXISTS affected_service    VARCHAR(100) NULL",
            "ALTER TABLE platform_incidents ADD COLUMN IF NOT EXISTS affected_tenants    TEXT         NULL", // JSON array of company ids
            "ALTER TABLE platform_incidents ADD COLUMN IF NOT EXISTS root_cause          TEXT         NULL",
            "ALTER TABLE platform_incidents ADD COLUMN IF NOT EXISTS actions_taken       TEXT         NULL",
            "ALTER TABLE platform_incidents ADD COLUMN IF NOT EXISTS trace_id            VARCHAR(64)  NULL",
            "ALTER TABLE platform_incidents ADD COLUMN IF NOT EXISTS deployment_version  VARCHAR(100) NULL",
        })
        {
            await db.ExecuteAsync(alter);
        }
    }
}
