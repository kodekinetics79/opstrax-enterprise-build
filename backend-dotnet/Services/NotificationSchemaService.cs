using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

public sealed class NotificationSchemaService(Database db)
{
    public async Task EnsureAsync(CancellationToken ct = default)
    {
        foreach (var sql in Tables) await db.ExecuteAsync(sql, ct: ct);
        foreach (var sql in Indexes) { try { await db.ExecuteAsync(sql, ct: ct); } catch { } }
    }

    private static readonly string[] Tables =
    [
        @"CREATE TABLE IF NOT EXISTS notifications (
            id BIGINT AUTO_INCREMENT PRIMARY KEY,
            company_id BIGINT NOT NULL,
            source_type VARCHAR(80) NULL,
            source_id BIGINT NULL,
            event_type VARCHAR(120) NOT NULL,
            severity VARCHAR(40) NOT NULL DEFAULT 'Medium',
            title VARCHAR(255) NOT NULL,
            message TEXT NOT NULL,
            audience_type VARCHAR(80) NOT NULL DEFAULT 'dispatcher',
            channel VARCHAR(40) NOT NULL DEFAULT 'in_app',
            status VARCHAR(40) NOT NULL DEFAULT 'unread',
            dedupe_key VARCHAR(255) NULL,
            priority INT NOT NULL DEFAULT 5,
            expires_at TIMESTAMP NULL,
            delivered_at TIMESTAMP NULL,
            read_at TIMESTAMP NULL,
            acknowledged_at TIMESTAMP NULL,
            acknowledged_by BIGINT NULL,
            acknowledgement_note TEXT NULL,
            escalated_from BIGINT NULL,
            created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
        ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci",

        @"CREATE TABLE IF NOT EXISTS notification_recipients (
            id BIGINT AUTO_INCREMENT PRIMARY KEY,
            notification_id BIGINT NOT NULL,
            company_id BIGINT NOT NULL,
            user_id BIGINT NULL,
            driver_id BIGINT NULL,
            role_target VARCHAR(80) NULL,
            status VARCHAR(40) NOT NULL DEFAULT 'unread',
            delivered_at TIMESTAMP NULL,
            read_at TIMESTAMP NULL,
            acknowledged_at TIMESTAMP NULL,
            channel VARCHAR(40) NOT NULL DEFAULT 'in_app',
            external_ref VARCHAR(255) NULL,
            created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
        ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci",

        @"CREATE TABLE IF NOT EXISTS messaging_conversations (
            id BIGINT AUTO_INCREMENT PRIMARY KEY,
            company_id BIGINT NOT NULL,
            dispatch_assignment_id BIGINT NULL,
            trip_id BIGINT NULL,
            subject VARCHAR(255) NULL,
            status VARCHAR(40) NOT NULL DEFAULT 'open',
            created_by BIGINT NULL,
            driver_id BIGINT NULL,
            created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
        ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci",

        @"CREATE TABLE IF NOT EXISTS messaging_messages (
            id BIGINT AUTO_INCREMENT PRIMARY KEY,
            conversation_id BIGINT NOT NULL,
            company_id BIGINT NOT NULL,
            sender_user_id BIGINT NOT NULL,
            sender_role VARCHAR(80) NULL,
            body TEXT NOT NULL,
            sent_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
            read_at TIMESTAMP NULL,
            attachment_ref VARCHAR(500) NULL
        ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci",

        @"CREATE TABLE IF NOT EXISTS escalation_rules (
            id BIGINT AUTO_INCREMENT PRIMARY KEY,
            company_id BIGINT NOT NULL,
            rule_name VARCHAR(200) NOT NULL,
            event_type VARCHAR(120) NOT NULL,
            severity VARCHAR(40) NOT NULL DEFAULT 'Medium',
            initial_audience VARCHAR(80) NOT NULL,
            escalation_audience VARCHAR(80) NOT NULL,
            time_to_escalate_minutes INT NOT NULL DEFAULT 30,
            repeat_interval_minutes INT NOT NULL DEFAULT 60,
            max_repeats INT NOT NULL DEFAULT 3,
            enabled TINYINT(1) NOT NULL DEFAULT 1,
            created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
        ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci",
    ];

    private static readonly string[] Indexes =
    [
        "CREATE INDEX idx_notifications_company ON notifications(company_id)",
        "CREATE INDEX idx_notifications_event_type ON notifications(event_type)",
        "CREATE INDEX idx_notifications_dedupe ON notifications(company_id, dedupe_key, status)",
        "CREATE INDEX idx_notifications_status ON notifications(company_id, status, created_at)",
        "CREATE INDEX idx_notification_recipients_notif ON notification_recipients(notification_id)",
        "CREATE INDEX idx_notification_recipients_user ON notification_recipients(user_id, company_id)",
        "CREATE INDEX idx_notification_recipients_driver ON notification_recipients(driver_id)",
        "CREATE INDEX idx_messaging_conversations_company ON messaging_conversations(company_id)",
        "CREATE INDEX idx_messaging_conversations_driver ON messaging_conversations(driver_id)",
        "CREATE INDEX idx_messaging_conversations_assignment ON messaging_conversations(dispatch_assignment_id)",
        "CREATE INDEX idx_messaging_messages_conversation ON messaging_messages(conversation_id)",
        "CREATE INDEX idx_escalation_rules_company ON escalation_rules(company_id)",
        "CREATE INDEX idx_escalation_rules_event ON escalation_rules(event_type)",
    ];
}
