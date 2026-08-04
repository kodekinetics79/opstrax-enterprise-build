-- Stage 74: make the retention policy ledger part of the owner-managed
-- Production schema. Production deliberately skips runtime schema creation, so
-- the mandatory retention worker must never depend on Development bootstrapping.
BEGIN;

CREATE TABLE IF NOT EXISTS data_retention_policies (
    id                      BIGINT       GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id              BIGINT       NOT NULL UNIQUE,
    audit_log_days          INT          NOT NULL DEFAULT 90,
    telemetry_days          INT          NOT NULL DEFAULT 90,
    notification_days       INT          NOT NULL DEFAULT 30,
    report_execution_days   INT          NOT NULL DEFAULT 180,
    security_event_days     INT          NOT NULL DEFAULT 365,
    soft_delete_only        BOOLEAN      NOT NULL DEFAULT true,
    legal_hold_active       BOOLEAN      NOT NULL DEFAULT false,
    legal_hold_reason       TEXT         NULL,
    legal_hold_set_at       TIMESTAMPTZ  NULL,
    legal_hold_set_by       VARCHAR(200) NULL,
    created_at              TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at              TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_by              VARCHAR(200) NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_drp_company ON data_retention_policies(company_id);

DO $retention_bounds$
BEGIN
  IF EXISTS (
    SELECT 1 FROM data_retention_policies
    WHERE audit_log_days < 30 OR telemetry_days < 7 OR notification_days < 7
       OR report_execution_days < 30 OR security_event_days < 90
  ) THEN
    RAISE EXCEPTION 'Retention policy contains values below supported minimums; review before Stage 74';
  END IF;
END $retention_bounds$;

ALTER TABLE data_retention_policies
  DROP CONSTRAINT IF EXISTS ck_data_retention_policy_minimums;
ALTER TABLE data_retention_policies
  ADD CONSTRAINT ck_data_retention_policy_minimums CHECK (
    audit_log_days >= 30 AND telemetry_days >= 7 AND notification_days >= 7
    AND report_execution_days >= 30 AND security_event_days >= 90
  );

INSERT INTO schema_migrations(version,description)
VALUES ('2026_08_02_stage74_retention_policy_production_contract',
        'Owner-managed retention policy ledger and supported minimums')
ON CONFLICT(version) DO UPDATE SET description=EXCLUDED.description;

COMMIT;
