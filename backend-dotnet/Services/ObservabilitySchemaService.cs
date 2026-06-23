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
                id                  BIGINT       PRIMARY KEY AUTO_INCREMENT,
                service_name        VARCHAR(100) NOT NULL,
                status              ENUM('running','succeeded','failed','degraded')
                                    NOT NULL DEFAULT 'running',
                started_at          DATETIME     NOT NULL DEFAULT NOW(),
                finished_at         DATETIME     NULL,
                duration_ms         INT          NULL,
                processed_count     INT          NOT NULL DEFAULT 0,
                failed_count        INT          NOT NULL DEFAULT 0,
                error_code          VARCHAR(100) NULL,
                error_message_safe  TEXT         NULL,
                next_run_at         DATETIME     NULL,
                heartbeat_at        DATETIME     NULL,
                INDEX idx_srh_service  (service_name),
                INDEX idx_srh_started  (started_at),
                INDEX idx_srh_status   (status)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
            """);

        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS service_heartbeats (
                service_name          VARCHAR(100) PRIMARY KEY,
                last_heartbeat_at     DATETIME     NOT NULL DEFAULT NOW(),
                last_run_at           DATETIME     NULL,
                last_run_status       VARCHAR(50)  NULL,
                consecutive_failures  INT          NOT NULL DEFAULT 0,
                last_error_safe       TEXT         NULL,
                updated_at            DATETIME     NOT NULL DEFAULT NOW()
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
            """);

        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS platform_incidents (
                id               BIGINT       PRIMARY KEY AUTO_INCREMENT,
                company_id       BIGINT       NULL,
                severity         ENUM('critical','high','medium','low','info')
                                 NOT NULL DEFAULT 'medium',
                source_service   VARCHAR(100) NOT NULL,
                source_event     VARCHAR(200) NOT NULL,
                status           ENUM('open','investigating','mitigated','resolved')
                                 NOT NULL DEFAULT 'open',
                title            VARCHAR(500) NOT NULL,
                safe_description TEXT         NULL,
                opened_at        DATETIME     NOT NULL DEFAULT NOW(),
                resolved_at      DATETIME     NULL,
                assigned_to      VARCHAR(200) NULL,
                INDEX idx_pi_status  (status),
                INDEX idx_pi_service (source_service),
                INDEX idx_pi_opened  (opened_at)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
            """);
    }
}
