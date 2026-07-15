using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

// Revenue recognition sub-ledger (ADR-008 rev-rec layer) — the AR-side analogue of Settlement. An
// append-only accounting sub-ledger that derives recognized-revenue entries BESIDE issued_invoices
// (never mutating them). Two-tier immutability: entries are 'pending' + recomputable while their
// fiscal period is 'open'; period close freezes them to 'posted' and every later correction is a
// reversing entry, never an edit or delete.
//
// P0 supports ONLY method='accrual' + trigger='on_invoice' (recognize the full issued total at issue
// date); everything else fails closed with a durable skip signal. All tables are company_id-scoped
// (RLS auto-enrolled). Additive: no revrec_profile => no recognition (today's behavior exactly).
public sealed class RevenueRecognitionSchemaService(Database db)
{
    public async Task EnsureAsync(CancellationToken ct = default)
    {
        foreach (var sql in Tables)
            await db.ExecuteAsync(sql, ct: ct);
        foreach (var sql in Indexes)
        {
            try { await db.ExecuteAsync(sql, ct: ct); }
            catch { /* additive */ }
        }
    }

    private static readonly string[] Tables =
    [
        @"CREATE TABLE IF NOT EXISTS revrec_profiles (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            profile_code VARCHAR(80) NOT NULL,
            profile_name VARCHAR(220) NOT NULL,
            method VARCHAR(20) NOT NULL DEFAULT 'accrual',
            trigger VARCHAR(20) NOT NULL DEFAULT 'on_invoice',
            recognize_base VARCHAR(20) NOT NULL DEFAULT 'total',
            ratable_periods INT NULL,
            functional_currency VARCHAR(10) NOT NULL DEFAULT 'USD',
            revenue_account_code VARCHAR(40) NULL,
            deferred_revenue_account_code VARCHAR(40) NULL,
            calendar_id BIGINT NULL,
            status VARCHAR(20) NOT NULL DEFAULT 'published',
            is_default BOOLEAN NOT NULL DEFAULT FALSE,
            effective_from DATE NOT NULL DEFAULT DATE '1900-01-01',
            effective_to DATE NULL,
            config_set_id BIGINT NULL,
            notes TEXT NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NULL
        )",
        @"CREATE TABLE IF NOT EXISTS revrec_fiscal_calendars (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            calendar_code VARCHAR(80) NOT NULL,
            calendar_name VARCHAR(220) NOT NULL,
            period_type VARCHAR(20) NOT NULL DEFAULT 'monthly',
            is_default BOOLEAN NOT NULL DEFAULT FALSE,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NULL
        )",
        @"CREATE TABLE IF NOT EXISTS revrec_fiscal_periods (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            calendar_id BIGINT NULL,
            period_code VARCHAR(20) NOT NULL,
            period_start DATE NOT NULL,
            period_end DATE NOT NULL,
            status VARCHAR(20) NOT NULL DEFAULT 'open',
            entry_count INT NOT NULL DEFAULT 0,
            recognized_total_functional DECIMAL(18,2) NOT NULL DEFAULT 0,
            close_checksum VARCHAR(80) NULL,
            closed_at TIMESTAMPTZ NULL,
            closed_by_user_id BIGINT NULL,
            correlation_id VARCHAR(120) NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NULL
        )",
        @"CREATE TABLE IF NOT EXISTS revenue_recognition_entries (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            issued_invoice_id UUID NOT NULL,
            issued_invoice_line_id UUID NULL,
            customer_id BIGINT NULL,
            job_id BIGINT NULL,
            profile_id BIGINT NULL,
            fiscal_period_id BIGINT NULL,
            schedule_id BIGINT NULL,
            entry_type VARCHAR(20) NOT NULL DEFAULT 'recognition',
            recognition_date DATE NOT NULL,
            currency VARCHAR(10) NOT NULL DEFAULT 'USD',
            amount DECIMAL(18,2) NOT NULL,
            functional_currency VARCHAR(10) NOT NULL DEFAULT 'USD',
            fx_rate DECIMAL(18,8) NOT NULL DEFAULT 1,
            fx_date DATE NULL,
            amount_functional DECIMAL(18,2) NOT NULL,
            revenue_account_code VARCHAR(40) NULL,
            deferred_revenue_account_code VARCHAR(40) NULL,
            status VARCHAR(20) NOT NULL DEFAULT 'pending',
            source VARCHAR(20) NOT NULL DEFAULT 'system',
            reverses_entry_id BIGINT NULL,
            memo TEXT NULL,
            correlation_id VARCHAR(120) NULL,
            causation_id VARCHAR(120) NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            created_by_user_id BIGINT NULL
        )",
        @"CREATE TABLE IF NOT EXISTS revrec_schedules (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            issued_invoice_id UUID NOT NULL,
            issued_invoice_line_id UUID NULL,
            profile_id BIGINT NULL,
            method VARCHAR(20) NOT NULL,
            currency VARCHAR(10) NOT NULL DEFAULT 'USD',
            total_amount DECIMAL(18,2) NOT NULL DEFAULT 0,
            status VARCHAR(20) NOT NULL DEFAULT 'active',
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NULL
        )",
        @"CREATE TABLE IF NOT EXISTS revrec_schedule_lines (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            company_id BIGINT NOT NULL,
            schedule_id BIGINT NOT NULL REFERENCES revrec_schedules(id) ON DELETE CASCADE,
            seq INT NOT NULL,
            scheduled_date DATE NOT NULL,
            amount DECIMAL(18,2) NOT NULL,
            milestone_code VARCHAR(80) NULL,
            recognized_entry_id BIGINT NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        )"
    ];

    private static readonly string[] Indexes =
    [
        "CREATE UNIQUE INDEX IF NOT EXISTS uq_revrec_profiles_company_code ON revrec_profiles (company_id, profile_code)",
        "CREATE UNIQUE INDEX IF NOT EXISTS uq_revrec_profiles_default ON revrec_profiles (company_id) WHERE is_default",
        "CREATE INDEX IF NOT EXISTS idx_revrec_profiles_lookup ON revrec_profiles (company_id, status, effective_from DESC)",
        "CREATE UNIQUE INDEX IF NOT EXISTS uq_revrec_calendars_code ON revrec_fiscal_calendars (company_id, calendar_code)",
        "CREATE UNIQUE INDEX IF NOT EXISTS uq_revrec_periods_company_code ON revrec_fiscal_periods (company_id, period_code)",
        "CREATE INDEX IF NOT EXISTS idx_revrec_periods_range ON revrec_fiscal_periods (company_id, period_start, period_end)",
        "CREATE INDEX IF NOT EXISTS idx_revrec_periods_open ON revrec_fiscal_periods (company_id, status, period_end)",
        // P0 idempotency guard: one system recognition per invoice (invoice-level).
        "CREATE UNIQUE INDEX IF NOT EXISTS uq_revrec_entries_invoice_system ON revenue_recognition_entries (company_id, issued_invoice_id) WHERE source='system' AND entry_type='recognition' AND issued_invoice_line_id IS NULL AND schedule_id IS NULL",
        // Structural double-contra guard: one reversal per reversed entry.
        "CREATE UNIQUE INDEX IF NOT EXISTS uq_revrec_reversal ON revenue_recognition_entries (company_id, reverses_entry_id) WHERE entry_type='reversal'",
        "CREATE INDEX IF NOT EXISTS idx_revrec_entries_invoice ON revenue_recognition_entries (company_id, issued_invoice_id)",
        "CREATE INDEX IF NOT EXISTS idx_revrec_entries_period ON revenue_recognition_entries (company_id, fiscal_period_id, status)",
        "CREATE INDEX IF NOT EXISTS idx_revrec_entries_date ON revenue_recognition_entries (company_id, recognition_date)",
        "CREATE INDEX IF NOT EXISTS idx_revrec_schedule_lines_sched ON revrec_schedule_lines (company_id, schedule_id, seq)"
    ];
}
