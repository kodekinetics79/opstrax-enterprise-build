using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

public sealed class CommercialFoundationSchemaService(Database db)
{
    public async Task EnsureAsync(CancellationToken ct = default)
    {
        foreach (var sql in Tables)
        {
            await db.ExecuteAsync(sql, ct: ct);
        }

        foreach (var sql in Indexes)
        {
            try
            {
                await db.ExecuteAsync(sql, ct: ct);
            }
            catch
            {
                // Local schema bootstrapping must stay additive and resilient.
            }
        }
    }

    private static readonly string[] Tables =
    [
        @"ALTER TABLE contracts ADD COLUMN IF NOT EXISTS source_channel VARCHAR(40) NULL",
        @"ALTER TABLE contracts ADD COLUMN IF NOT EXISTS client_generated_id VARCHAR(120) NULL",
        @"ALTER TABLE contracts ADD COLUMN IF NOT EXISTS idempotency_key VARCHAR(160) NULL",
        @"ALTER TABLE contracts ADD COLUMN IF NOT EXISTS correlation_id VARCHAR(120) NULL",
        @"ALTER TABLE contracts ADD COLUMN IF NOT EXISTS causation_id VARCHAR(120) NULL",
        @"ALTER TABLE contracts ADD COLUMN IF NOT EXISTS metadata_json JSONB NULL",
        @"CREATE TABLE IF NOT EXISTS customer_sites (
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
        )",
        @"CREATE TABLE IF NOT EXISTS contract_versions (
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
        )"
    ];

    private static readonly string[] Indexes =
    [
        "CREATE INDEX IF NOT EXISTS idx_contracts_company_number ON contracts (company_id, contract_number)",
        "CREATE UNIQUE INDEX IF NOT EXISTS uq_contracts_company_idem ON contracts (company_id, idempotency_key) WHERE idempotency_key IS NOT NULL",
        "CREATE INDEX IF NOT EXISTS idx_customer_sites_company_customer_status ON customer_sites (company_id, customer_id, status, created_at DESC)",
        "CREATE UNIQUE INDEX IF NOT EXISTS uq_customer_sites_company_customer_code ON customer_sites (company_id, customer_id, site_code)",
        "CREATE UNIQUE INDEX IF NOT EXISTS uq_customer_sites_company_idem ON customer_sites (company_id, idempotency_key) WHERE idempotency_key IS NOT NULL",
        "CREATE INDEX IF NOT EXISTS idx_contract_versions_company_contract_version ON contract_versions (company_id, contract_id, version_no DESC)",
        "CREATE UNIQUE INDEX IF NOT EXISTS uq_contract_versions_company_contract_version ON contract_versions (company_id, contract_id, version_no)",
        "CREATE UNIQUE INDEX IF NOT EXISTS uq_contract_versions_company_current ON contract_versions (company_id, contract_id) WHERE is_current",
        "CREATE UNIQUE INDEX IF NOT EXISTS uq_contract_versions_company_idem ON contract_versions (company_id, idempotency_key) WHERE idempotency_key IS NOT NULL"
    ];
}
