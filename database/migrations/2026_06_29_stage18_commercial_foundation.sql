-- Stage 18 commercial foundation
-- Additive schema for normalized customer sites and contract version history.

BEGIN;

-- Reconcile the legacy 001_schema contracts table with the current write/read
-- contract before creating indexes. CREATE TABLE IF NOT EXISTS alone cannot add
-- these columns when the earlier table already exists.
ALTER TABLE contracts ADD COLUMN IF NOT EXISTS contract_number VARCHAR(80) NULL;
ALTER TABLE contracts ADD COLUMN IF NOT EXISTS carrier_id BIGINT NULL;
ALTER TABLE contracts ADD COLUMN IF NOT EXISTS contract_type VARCHAR(80) NULL;
ALTER TABLE contracts ADD COLUMN IF NOT EXISTS expiry_date DATE NULL;
ALTER TABLE contracts ADD COLUMN IF NOT EXISTS currency VARCHAR(10) NOT NULL DEFAULT 'USD';
ALTER TABLE contracts ADD COLUMN IF NOT EXISTS base_rate DECIMAL(12,4) NOT NULL DEFAULT 0;
ALTER TABLE contracts ADD COLUMN IF NOT EXISTS fuel_surcharge_enabled BOOLEAN NOT NULL DEFAULT FALSE;
ALTER TABLE contracts ADD COLUMN IF NOT EXISTS fuel_surcharge_percent DECIMAL(6,2) NULL;
ALTER TABLE contracts ADD COLUMN IF NOT EXISTS sla_terms TEXT NULL;
ALTER TABLE contracts ADD COLUMN IF NOT EXISTS margin_risk VARCHAR(50) NOT NULL DEFAULT 'Low';
ALTER TABLE contracts ADD COLUMN IF NOT EXISTS notes TEXT NULL;
ALTER TABLE contracts ADD COLUMN IF NOT EXISTS created_at TIMESTAMPTZ NOT NULL DEFAULT NOW();
ALTER TABLE contracts ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ NULL;

-- Migration 001 and the commercial API use different names for the same
-- contract identity and expiry fields. Both shapes remain active: revenue and
-- demo-seed writers use the legacy columns while the authenticated contracts
-- API uses the commercial columns. Keep them synchronized at the database
-- boundary so either writer produces one truthful row.
CREATE OR REPLACE FUNCTION stage18_sync_contract_compatibility()
RETURNS trigger
LANGUAGE plpgsql
AS $stage18_contract_sync$
BEGIN
  IF TG_OP = 'UPDATE' THEN
    IF NEW.contract_number IS DISTINCT FROM OLD.contract_number
       AND NULLIF(BTRIM(NEW.contract_number), '') IS NOT NULL THEN
      NEW.contract_code := NEW.contract_number;
    ELSIF NEW.contract_code IS DISTINCT FROM OLD.contract_code
       AND NULLIF(BTRIM(NEW.contract_code), '') IS NOT NULL THEN
      NEW.contract_number := NEW.contract_code;
    END IF;

    IF NEW.expiry_date IS DISTINCT FROM OLD.expiry_date THEN
      NEW.expiration_date := NEW.expiry_date;
    ELSIF NEW.expiration_date IS DISTINCT FROM OLD.expiration_date THEN
      NEW.expiry_date := NEW.expiration_date;
    END IF;
  END IF;

  NEW.contract_number := COALESCE(
    NULLIF(BTRIM(NEW.contract_number), ''),
    NULLIF(BTRIM(NEW.contract_code), '')
  );
  NEW.contract_code := COALESCE(
    NULLIF(BTRIM(NEW.contract_code), ''),
    NEW.contract_number
  );
  NEW.title := COALESCE(
    NULLIF(BTRIM(NEW.title), ''),
    NEW.contract_number,
    NEW.contract_code
  );
  NEW.expiry_date := COALESCE(NEW.expiry_date, NEW.expiration_date);
  NEW.expiration_date := COALESCE(NEW.expiration_date, NEW.expiry_date);
  RETURN NEW;
END
$stage18_contract_sync$;

DROP TRIGGER IF EXISTS trg_stage18_sync_contract_compatibility ON contracts;
CREATE TRIGGER trg_stage18_sync_contract_compatibility
BEFORE INSERT OR UPDATE ON contracts
FOR EACH ROW EXECUTE FUNCTION stage18_sync_contract_compatibility();

DO $reconcile_legacy_contracts$
BEGIN
  IF EXISTS (SELECT 1 FROM information_schema.columns
             WHERE table_schema='public' AND table_name='contracts' AND column_name='contract_code') THEN
    UPDATE contracts
    SET contract_number=COALESCE(NULLIF(BTRIM(contract_code),''), 'CON-' || id::text)
    WHERE contract_number IS NULL;
  ELSE
    UPDATE contracts SET contract_number='CON-' || id::text WHERE contract_number IS NULL;
  END IF;
  IF EXISTS (SELECT 1 FROM information_schema.columns
             WHERE table_schema='public' AND table_name='contracts' AND column_name='expiration_date') THEN
    UPDATE contracts SET expiry_date=expiration_date WHERE expiry_date IS NULL;
  END IF;
END
$reconcile_legacy_contracts$;
ALTER TABLE contracts ALTER COLUMN contract_number SET NOT NULL;

ALTER TABLE contracts ADD COLUMN IF NOT EXISTS source_channel VARCHAR(40) NULL;
ALTER TABLE contracts ADD COLUMN IF NOT EXISTS client_generated_id VARCHAR(120) NULL;
ALTER TABLE contracts ADD COLUMN IF NOT EXISTS idempotency_key VARCHAR(160) NULL;
ALTER TABLE contracts ADD COLUMN IF NOT EXISTS correlation_id VARCHAR(120) NULL;
ALTER TABLE contracts ADD COLUMN IF NOT EXISTS causation_id VARCHAR(120) NULL;
ALTER TABLE contracts ADD COLUMN IF NOT EXISTS metadata_json JSONB NULL;

CREATE TABLE IF NOT EXISTS customer_sites (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id BIGINT NOT NULL,
    customer_id BIGINT NOT NULL,
    site_code VARCHAR(80) NOT NULL,
    site_name VARCHAR(220) NOT NULL,
    site_type VARCHAR(80) NOT NULL DEFAULT 'service',
    address_line1 VARCHAR(300) NULL,
    address_line2 VARCHAR(300) NULL,
    city VARCHAR(120) NULL,
    state VARCHAR(80) NULL,
    postal_code VARCHAR(30) NULL,
    country_code VARCHAR(10) NOT NULL DEFAULT 'US',
    geo_latitude NUMERIC(10,7) NULL,
    geo_longitude NUMERIC(10,7) NULL,
    access_instructions TEXT NULL,
    external_reference VARCHAR(120) NULL,
    status VARCHAR(40) NOT NULL DEFAULT 'Active',
    source_channel VARCHAR(40) NULL,
    client_generated_id VARCHAR(120) NULL,
    idempotency_key VARCHAR(160) NULL,
    correlation_id VARCHAR(120) NULL,
    causation_id VARCHAR(120) NULL,
    metadata_json JSONB NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NULL
);

CREATE TABLE IF NOT EXISTS contract_versions (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id BIGINT NOT NULL,
    contract_id BIGINT NOT NULL,
    version_no INT NOT NULL,
    version_label VARCHAR(80) NULL,
    status VARCHAR(40) NOT NULL DEFAULT 'draft',
    is_current BOOLEAN NOT NULL DEFAULT FALSE,
    effective_date DATE NULL,
    expiry_date DATE NULL,
    currency VARCHAR(10) NOT NULL DEFAULT 'USD',
    base_rate DECIMAL(12,4) NOT NULL DEFAULT 0,
    rate_type VARCHAR(80) NOT NULL DEFAULT 'Per Mile',
    fuel_surcharge_enabled BOOLEAN NOT NULL DEFAULT FALSE,
    fuel_surcharge_percent DECIMAL(6,2) NULL,
    sla_terms TEXT NULL,
    margin_risk VARCHAR(50) NOT NULL DEFAULT 'Low',
    contract_snapshot_json JSONB NOT NULL DEFAULT '{}'::jsonb,
    pricing_json JSONB NULL,
    terms_json JSONB NULL,
    notes TEXT NULL,
    source_channel VARCHAR(40) NULL,
    client_generated_id VARCHAR(120) NULL,
    idempotency_key VARCHAR(160) NULL,
    correlation_id VARCHAR(120) NULL,
    causation_id VARCHAR(120) NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_contracts_company_number
    ON contracts (company_id, contract_number);

CREATE UNIQUE INDEX IF NOT EXISTS uq_contracts_company_idem
    ON contracts (company_id, idempotency_key)
    WHERE idempotency_key IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_customer_sites_company_customer_status
    ON customer_sites (company_id, customer_id, status, created_at DESC);

CREATE UNIQUE INDEX IF NOT EXISTS uq_customer_sites_company_customer_code
    ON customer_sites (company_id, customer_id, site_code);

CREATE UNIQUE INDEX IF NOT EXISTS uq_customer_sites_company_idem
    ON customer_sites (company_id, idempotency_key)
    WHERE idempotency_key IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_contract_versions_company_contract_version
    ON contract_versions (company_id, contract_id, version_no DESC);

CREATE UNIQUE INDEX IF NOT EXISTS uq_contract_versions_company_contract_version
    ON contract_versions (company_id, contract_id, version_no);

CREATE UNIQUE INDEX IF NOT EXISTS uq_contract_versions_company_current
    ON contract_versions (company_id, contract_id)
    WHERE is_current;

CREATE UNIQUE INDEX IF NOT EXISTS uq_contract_versions_company_idem
    ON contract_versions (company_id, idempotency_key)
    WHERE idempotency_key IS NOT NULL;

COMMIT;

-- Local rollback guide:
-- DROP TABLE IF EXISTS contract_versions;
-- DROP TABLE IF EXISTS customer_sites;
-- ALTER TABLE contracts DROP COLUMN IF EXISTS source_channel;
-- ALTER TABLE contracts DROP COLUMN IF EXISTS client_generated_id;
-- ALTER TABLE contracts DROP COLUMN IF EXISTS idempotency_key;
-- ALTER TABLE contracts DROP COLUMN IF EXISTS correlation_id;
-- ALTER TABLE contracts DROP COLUMN IF EXISTS causation_id;
-- ALTER TABLE contracts DROP COLUMN IF EXISTS metadata_json;
