-- Stage 6 P0-B1B business spine migration
-- Local-only additive PostgreSQL migration for the first canonical customer -> contract -> rate card -> job -> trip -> charge slice.

BEGIN;

CREATE TABLE IF NOT EXISTS business_surface_profiles (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id BIGINT NOT NULL UNIQUE,
    vertical_key VARCHAR(80) NOT NULL DEFAULT 'generic',
    customer_label_singular VARCHAR(80) NOT NULL DEFAULT 'Customer',
    customer_label_plural VARCHAR(80) NOT NULL DEFAULT 'Customers',
    contract_label_singular VARCHAR(80) NOT NULL DEFAULT 'Contract',
    contract_label_plural VARCHAR(80) NOT NULL DEFAULT 'Contracts',
    rate_card_label_singular VARCHAR(80) NOT NULL DEFAULT 'Rate Card',
    rate_card_label_plural VARCHAR(80) NOT NULL DEFAULT 'Rate Cards',
    job_label_singular VARCHAR(80) NOT NULL DEFAULT 'Job',
    job_label_plural VARCHAR(80) NOT NULL DEFAULT 'Jobs',
    trip_label_singular VARCHAR(80) NOT NULL DEFAULT 'Trip',
    trip_label_plural VARCHAR(80) NOT NULL DEFAULT 'Trips',
    charge_label_singular VARCHAR(80) NOT NULL DEFAULT 'Charge',
    charge_label_plural VARCHAR(80) NOT NULL DEFAULT 'Charges',
    use_generic_labels BOOLEAN NOT NULL DEFAULT TRUE,
    notes TEXT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NULL
);

CREATE TABLE IF NOT EXISTS rate_cards (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id BIGINT NOT NULL,
    customer_id BIGINT NULL,
    contract_id BIGINT NULL,
    rate_card_code VARCHAR(80) NOT NULL,
    rate_card_name VARCHAR(220) NOT NULL,
    billing_basis VARCHAR(80) NOT NULL DEFAULT 'Per Unit',
    service_scope VARCHAR(120) NULL,
    origin_zone VARCHAR(120) NULL,
    destination_zone VARCHAR(120) NULL,
    vehicle_type VARCHAR(80) NULL,
    currency VARCHAR(10) NOT NULL DEFAULT 'USD',
    base_rate DECIMAL(12,4) NOT NULL DEFAULT 0,
    minimum_charge DECIMAL(12,2) NULL,
    fuel_surcharge_percent DECIMAL(6,2) NULL,
    accessorial_type VARCHAR(120) NULL,
    effective_date DATE NOT NULL,
    expiry_date DATE NULL,
    status VARCHAR(50) NOT NULL DEFAULT 'Active',
    correlation_id VARCHAR(120) NULL,
    causation_id VARCHAR(120) NULL,
    notes TEXT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NULL
);

CREATE TABLE IF NOT EXISTS job_charges (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id BIGINT NOT NULL,
    job_id BIGINT NOT NULL,
    trip_id BIGINT NULL,
    rate_card_id BIGINT NULL,
    charge_code VARCHAR(80) NOT NULL,
    charge_name VARCHAR(220) NOT NULL,
    charge_type VARCHAR(80) NOT NULL DEFAULT 'base',
    description TEXT NULL,
    quantity DECIMAL(12,3) NOT NULL DEFAULT 1,
    unit_rate DECIMAL(12,4) NOT NULL DEFAULT 0,
    amount DECIMAL(12,2) NOT NULL DEFAULT 0,
    currency VARCHAR(10) NOT NULL DEFAULT 'USD',
    status VARCHAR(40) NOT NULL DEFAULT 'pending',
    correlation_id VARCHAR(120) NULL,
    causation_id VARCHAR(120) NULL,
    approved_by_user_id BIGINT NULL,
    approved_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NULL
);

CREATE INDEX IF NOT EXISTS idx_business_surface_profiles_company ON business_surface_profiles (company_id);
CREATE INDEX IF NOT EXISTS idx_rate_cards_company_contract_status ON rate_cards (company_id, contract_id, status, effective_date DESC);
CREATE UNIQUE INDEX IF NOT EXISTS uq_rate_cards_company_code ON rate_cards (company_id, rate_card_code);
CREATE INDEX IF NOT EXISTS idx_rate_cards_company_customer ON rate_cards (company_id, customer_id, status);
CREATE INDEX IF NOT EXISTS idx_job_charges_company_job_status ON job_charges (company_id, job_id, status, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_job_charges_company_trip ON job_charges (company_id, trip_id, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_job_charges_company_rate_card ON job_charges (company_id, rate_card_id, status);
ALTER TABLE jobs ADD COLUMN IF NOT EXISTS rate_card_id BIGINT NULL;
ALTER TABLE trips ADD COLUMN IF NOT EXISTS trip_number VARCHAR(60) NULL;
ALTER TABLE trip_stops ADD COLUMN IF NOT EXISTS address_id BIGINT NULL;
CREATE INDEX IF NOT EXISTS idx_jobs_company_rate_card ON jobs (company_id, rate_card_id, status);
CREATE INDEX IF NOT EXISTS idx_trips_company_trip_number ON trips (company_id, trip_number);
CREATE INDEX IF NOT EXISTS idx_trip_stops_company_trip_sequence ON trip_stops (company_id, trip_id, stop_sequence);

COMMIT;

-- Local rollback guide:
-- DROP TABLE IF EXISTS job_charges;
-- DROP TABLE IF EXISTS rate_cards;
-- DROP TABLE IF EXISTS business_surface_profiles;
-- ALTER TABLE jobs DROP COLUMN IF EXISTS rate_card_id;
-- ALTER TABLE trips DROP COLUMN IF EXISTS trip_number;
-- ALTER TABLE trip_stops DROP COLUMN IF EXISTS address_id;
