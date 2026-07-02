-- Stage 23 — schema_migrations ledger (first step toward a schema source of truth)
--
-- PURPOSE
--   Today schema truth = "whatever the 35 runtime *SchemaService.EnsureAsync() blocks
--   create at startup", with a parallel, partial set of SQL files in database/migrations/
--   and NO tracking table. That has caused environment drift + column-drift bugs
--   (see OPSTRAX_ARCHITECTURE_NOTES.md §4).
--
--   This introduces a lightweight `schema_migrations` ledger and backfills the existing
--   migration files as the recorded baseline, so future changes can be applied and
--   tracked as versioned migrations rather than more ad-hoc EnsureAsync blocks.
--
-- SAFETY / REVERSIBILITY
--   ADDITIVE, idempotent, re-runnable. Creates one tracking table + inserts version rows.
--   Touches no application data. RLS: this is a control-plane table, not tenant-scoped.

BEGIN;

CREATE TABLE IF NOT EXISTS schema_migrations (
    version      text PRIMARY KEY,
    applied_at   timestamptz NOT NULL DEFAULT now(),
    description  text
);

-- Backfill the known migration history as the baseline (idempotent).
INSERT INTO schema_migrations (version, description) VALUES
    ('2026_06_27_stage5_p0b1a_foundation',              'Foundation schema'),
    ('2026_06_28_stage5b_p0b1a2_persistence_hardening', 'Persistence hardening'),
    ('2026_06_28_stage5d_p0b1a3_dispatcher',            'Dispatcher'),
    ('2026_06_28_stage6_p0b1b_business_spine',          'Business spine'),
    ('2026_06_28_stage7a_revenue_readiness_schema_contract', 'Revenue readiness contract'),
    ('2026_06_28_stage8_finance_activation',            'Finance activation'),
    ('2026_06_28_stage12a_telemetry_live_state',        'Telemetry live state'),
    ('2026_06_28_stage13b_safety_maintenance_foundation','Safety + maintenance foundation'),
    ('2026_06_29_stage18_commercial_foundation',        'Commercial foundation'),
    ('2026_06_30_stage19_row_level_security',           'RLS policies (dormant)'),
    ('2026_06_30_stage20_rls_force_and_app_role',       'RLS FORCE + restricted app role'),
    ('2026_06_30_stage21_customer_portal',              'Customer portal'),
    ('2026_07_01_stage22_rls_reconcile_coverage',       'RLS coverage reconciliation'),
    ('2026_07_02_stage23_schema_migration_ledger',      'Schema migration ledger (this)')
ON CONFLICT (version) DO NOTHING;

COMMIT;
