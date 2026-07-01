using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

public sealed class AlertWorkflowSchemaService(Database db)
{
    public async Task EnsureAsync(CancellationToken ct = default)
    {
        foreach (var sql in Tables) await db.ExecuteAsync(sql, ct: ct);
        // Reconcile module_records first: the app queries a richer schema than the base
        // 001_schema.sql shape (record_code, priority, tags, secondary_value, numeric_value,
        // notes, currency, company_id, …). No other schema step adds these, so any DB whose
        // module_records came from the base init 500s on /api/invoices, /api/payments, etc.
        // These ADDs are idempotent (IF NOT EXISTS) and must run before this service's own
        // INSERT (which reads mr.record_code / mr.secondary_value / mr.numeric_value).
        foreach (var sql in ModuleRecordColumns)
        {
            try { await db.ExecuteAsync(sql, ct: ct); } catch { }
        }
        foreach (var sql in Migrations)
        {
            try { await db.ExecuteAsync(sql, ct: ct); } catch { }
        }
        foreach (var sql in DataFixes)
        {
            try { await db.ExecuteAsync(sql, ct: ct); } catch { }
        }
        foreach (var sql in Indexes)
        {
            try { await db.ExecuteAsync(sql, ct: ct); } catch { }
        }
    }

    private static readonly string[] Tables =
    [
        // Base alert_rules table. The Migrations block below only ADD COLUMNs onto an
        // existing table, so the table itself must exist first. Columns here are the base
        // shape the Migrations/DataFixes/AlertRulesList query assume (rule_name, module_name,
        // condition_json, tenant_id, status); the operational columns (rule_key, category,
        // …) are added idempotently by Migrations.
        @"CREATE TABLE IF NOT EXISTS alert_rules (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            tenant_id BIGINT NULL,
            rule_name VARCHAR(220) NULL,
            module_name VARCHAR(120) NULL,
            condition_json JSONB NULL,
            status VARCHAR(40) NOT NULL DEFAULT 'Active',
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        )",
        @"CREATE TABLE IF NOT EXISTS alert_follow_up_tasks (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            alert_id BIGINT NOT NULL,
            title VARCHAR(220) NOT NULL,
            description TEXT NULL,
            priority VARCHAR(40) NOT NULL DEFAULT 'High',
            status VARCHAR(40) NOT NULL DEFAULT 'Open',
            assigned_to_user_id BIGINT NULL,
            created_by_user_id BIGINT NULL,
            owner_name VARCHAR(160) NULL,
            due_at TIMESTAMPTZ NULL,
            closed_at TIMESTAMPTZ NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NULL,
            deleted_at TIMESTAMPTZ NULL
        )"
    ];

    // Idempotent reconciliation of module_records to the shape the app actually queries.
    // The base 001_schema.sql table only has: id, module_key, title, status, owner_name,
    // location_name, due_at, risk_level, amount, metadata_json, created_at.
    private static readonly string[] ModuleRecordColumns =
    [
        "ALTER TABLE module_records ADD COLUMN IF NOT EXISTS company_id BIGINT NOT NULL DEFAULT 1",
        "ALTER TABLE module_records ADD COLUMN IF NOT EXISTS record_code VARCHAR(120)",
        "ALTER TABLE module_records ADD COLUMN IF NOT EXISTS priority VARCHAR(40)",
        "ALTER TABLE module_records ADD COLUMN IF NOT EXISTS tags VARCHAR(220)",
        "ALTER TABLE module_records ADD COLUMN IF NOT EXISTS secondary_value NUMERIC(14,2)",
        "ALTER TABLE module_records ADD COLUMN IF NOT EXISTS numeric_value NUMERIC(14,2)",
        "ALTER TABLE module_records ADD COLUMN IF NOT EXISTS notes TEXT",
        "ALTER TABLE module_records ADD COLUMN IF NOT EXISTS currency VARCHAR(10)",
        "ALTER TABLE module_records ADD COLUMN IF NOT EXISTS data JSONB",
        "ALTER TABLE module_records ADD COLUMN IF NOT EXISTS assigned_to_name VARCHAR(160)",
        "ALTER TABLE module_records ADD COLUMN IF NOT EXISTS entity_id BIGINT",
        "ALTER TABLE module_records ADD COLUMN IF NOT EXISTS due_date TIMESTAMPTZ",
        "ALTER TABLE module_records ADD COLUMN IF NOT EXISTS deleted_at TIMESTAMPTZ",
        "ALTER TABLE module_records ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ",
        "ALTER TABLE module_records ADD COLUMN IF NOT EXISTS created_by_user_id BIGINT",
    ];

    private static readonly string[] Migrations =
    [
        "ALTER TABLE alert_rules ADD COLUMN IF NOT EXISTS company_id BIGINT",
        "ALTER TABLE alert_rules ADD COLUMN IF NOT EXISTS rule_key VARCHAR(80)",
        "ALTER TABLE alert_rules ADD COLUMN IF NOT EXISTS category VARCHAR(120)",
        "ALTER TABLE alert_rules ADD COLUMN IF NOT EXISTS threshold_text TEXT",
        "ALTER TABLE alert_rules ADD COLUMN IF NOT EXISTS action_type VARCHAR(220)",
        "ALTER TABLE alert_rules ADD COLUMN IF NOT EXISTS channels TEXT",
        "ALTER TABLE alert_rules ADD COLUMN IF NOT EXISTS priority VARCHAR(40)",
        "ALTER TABLE alert_rules ADD COLUMN IF NOT EXISTS recipients TEXT",
        "ALTER TABLE alert_rules ADD COLUMN IF NOT EXISTS triggered_today INT NOT NULL DEFAULT 0",
        "ALTER TABLE alert_rules ADD COLUMN IF NOT EXISTS last_triggered_at TIMESTAMPTZ",
        "ALTER TABLE alert_rules ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ",
        "ALTER TABLE alert_rules ADD COLUMN IF NOT EXISTS deleted_at TIMESTAMPTZ"
    ];

    private static readonly string[] DataFixes =
    [
        "UPDATE alert_rules SET company_id = COALESCE(company_id, tenant_id, 1) WHERE company_id IS NULL",
        "UPDATE alert_rules SET rule_key = COALESCE(NULLIF(rule_key, ''), 'ALR-' || id::TEXT) WHERE rule_key IS NULL OR rule_key = ''",
        "UPDATE alert_rules SET category = COALESCE(NULLIF(category, ''), NULLIF(module_name, ''), 'Operations') WHERE category IS NULL OR category = ''",
        "UPDATE alert_rules SET threshold_text = COALESCE(NULLIF(threshold_text, ''), COALESCE(condition_json::TEXT, '')) WHERE threshold_text IS NULL",
        "UPDATE alert_rules SET action_type = COALESCE(NULLIF(action_type, ''), 'Create operational alert') WHERE action_type IS NULL OR action_type = ''",
        "UPDATE alert_rules SET channels = COALESCE(NULLIF(channels, ''), 'In-App') WHERE channels IS NULL OR channels = ''",
        "UPDATE alert_rules SET priority = COALESCE(NULLIF(priority, ''), 'Medium') WHERE priority IS NULL OR priority = ''",
        "UPDATE alert_rules SET updated_at = COALESCE(updated_at, created_at, NOW()) WHERE updated_at IS NULL",
        @"INSERT INTO alert_rules (
              tenant_id, company_id, rule_name, module_name, condition_json, status,
              rule_key, category, threshold_text, action_type, channels, priority, recipients, triggered_today, last_triggered_at, created_at, updated_at
          )
          SELECT
              COALESCE(mr.company_id, 1),
              COALESCE(mr.company_id, 1),
              mr.title,
              'operations',
              NULL,
              COALESCE(mr.status, 'Active'),
              COALESCE(NULLIF(mr.record_code, ''), 'ALR-' || mr.id::TEXT),
              COALESCE(NULLIF(mr.tags, ''), 'Operations'),
              COALESCE(mr.secondary_value, ''),
              'Create operational alert',
              'In-App',
              'Medium',
              '',
              COALESCE(mr.numeric_value::INT, 0),
              mr.updated_at,
              COALESCE(mr.created_at, NOW()),
              COALESCE(mr.updated_at, mr.created_at, NOW())
          FROM module_records mr
          WHERE mr.module_key = 'alert-rules'
            AND mr.deleted_at IS NULL
            AND NOT EXISTS (
                SELECT 1
                FROM alert_rules ar
                WHERE COALESCE(ar.company_id, ar.tenant_id, 1) = COALESCE(mr.company_id, 1)
                  AND COALESCE(ar.rule_key, '') = COALESCE(NULLIF(mr.record_code, ''), 'ALR-' || mr.id::TEXT)
            )"
    ];

    private static readonly string[] Indexes =
    [
        "CREATE INDEX IF NOT EXISTS idx_alert_rules_company_status ON alert_rules(company_id, status)",
        "CREATE INDEX IF NOT EXISTS idx_alert_rules_company_category ON alert_rules(company_id, category)",
        "CREATE INDEX IF NOT EXISTS idx_alert_rules_rule_key ON alert_rules(company_id, rule_key)",
        "CREATE INDEX IF NOT EXISTS idx_alert_tasks_company_status ON alert_follow_up_tasks(company_id, status, created_at)",
        "CREATE INDEX IF NOT EXISTS idx_alert_tasks_alert ON alert_follow_up_tasks(alert_id, company_id)",
        "CREATE INDEX IF NOT EXISTS idx_alert_tasks_assignee ON alert_follow_up_tasks(company_id, assigned_to_user_id)"
    ];
}
