using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

// ─────────────────────────────────────────────────────────────────────────────
// MARKET-PACK ENGINE — reusable, revenue-controlled regional capability layer.
//
// Market packs (Canada/North America, Saudi/GCC) are PAID add-ons that drive
// address formats, document types, driver/vehicle requirements, inspection
// templates, tax/fuel readiness, units, currencies, languages and regulatory
// labels — WITHOUT hardcoding regional logic into core fleet modules.
//
// Maps onto the Sprint-1 revenue foundation: each market pack has a row in
// module_packages (catalog) + usage meters + (optionally) pricing. Tenant access
// is DENY-BY-DEFAULT via tenant_market_packs (paid opt-in), distinct from the
// allow-unless-disabled semantics used for core fleet modules.
//
// All DDL is idempotent (CREATE … IF NOT EXISTS). Reference data (packs, features,
// requirements, templates, unit/currency/language settings) is always seeded;
// tenant assignments + demo compliance rows live in the gated MarketPackSeeder.
// Reuses the existing fleet_tms_saudi_regions reference table for Saudi geography.
// ─────────────────────────────────────────────────────────────────────────────
public sealed class MarketPackSchemaService(Database db)
{
    public static class Packs
    {
        public const string CanadaNa = "canada_na";
        public const string SaudiGcc = "saudi_gcc";
    }

    public static class Features
    {
        public const string MarketCanadaNa = "market.canada_na";
        public const string MarketSaudiGcc = "market.saudi_gcc";
        public const string Documents = "compliance.documents";
        public const string ExpiryAlerts = "compliance.expiry_alerts";
        public const string Inspections = "compliance.inspections";
        public const string DriverQualification = "compliance.driver_qualification";
        public const string VehicleDocuments = "compliance.vehicle_documents";
        public const string TaxReadiness = "compliance.tax_readiness";
    }

    // Canonical pack → market feature module key. Pure + testable; used by platform
    // enable/disable to mirror a pack assignment into tenant_entitlements and by the
    // deny-by-default enforcement.
    public static string ModuleKeyForPack(string packCode) => packCode switch
    {
        Packs.CanadaNa => Features.MarketCanadaNa,
        Packs.SaudiGcc => Features.MarketSaudiGcc,
        _ => $"market.{packCode}",
    };

    public async Task EnsureAsync()
    {
        await CoreTablesAsync();
        await CanadaTablesAsync();
        await SaudiTablesAsync();
        await EnsureBranchOwnershipAsync();
        await BackfillLegacySaudiLedgerAsync();
        await SeedReferenceAsync();
        await SeedRevenueMappingAsync();
    }

    private async Task BackfillLegacySaudiLedgerAsync()
    {
        // compliance_records is the single Saudi document ledger. Preserve any
        // records created by the retired Fleet TMS readiness surface exactly once.
        await db.ExecuteAsync("""
            DO $migration$
            BEGIN
              IF to_regclass('public.fleet_tms_readiness_documents') IS NOT NULL THEN
                INSERT INTO compliance_records
                  (company_id, branch_id, pack_code, subject_type, subject_name, doc_key, document_no,
                   document_status, expiry_date, hijri_expiry_date, metadata, created_at, updated_at)
                SELECT f.company_id, f.branch_id, 'saudi_gcc', lower(f.subject_type), f.subject_name,
                       lower(f.document_type), NULLIF(f.document_number,''),
                       CASE lower(f.document_status)
                         WHEN 'expired' THEN 'expired' WHEN 'cancelled' THEN 'expired'
                         WHEN 'suspended' THEN 'expiring' ELSE 'valid' END,
                       f.gregorian_expiry_date, f.hijri_expiry_date::text,
                       jsonb_build_object('legacyFleetReadinessId', f.id, 'migratedFrom', 'fleet_tms_readiness_documents',
                         'transportDocumentNo', f.transport_document_no, 'permitNo', f.permit_no,
                         'vatNumber', f.vat_number, 'commercialRegistrationNo', f.commercial_registration_no),
                       COALESCE(f.created_at_utc, NOW()), COALESCE(f.updated_at_utc, NOW())
                FROM fleet_tms_readiness_documents f
                WHERE NOT EXISTS (
                  SELECT 1 FROM compliance_records c
                  WHERE c.company_id=f.company_id AND c.branch_id IS NOT DISTINCT FROM f.branch_id
                    AND c.pack_code='saudi_gcc' AND c.metadata->>'legacyFleetReadinessId'=f.id::text);
              END IF;
            END $migration$;
            """);
    }

    private async Task EnsureBranchOwnershipAsync()
    {
        string[] tables =
        [
            "compliance_records", "compliance_record_documents", "compliance_expiry_events",
            "vehicle_inspection_records", "inspection_defects", "jurisdiction_mileage_records",
            "jurisdiction_fuel_records", "driver_duty_status_records", "eld_device_registry",
            "market_addresses", "business_tax_readiness"
        ];
        foreach (var table in tables)
        {
            await db.ExecuteAsync($"ALTER TABLE \"{table}\" ADD COLUMN IF NOT EXISTS branch_id BIGINT NULL");
            await db.ExecuteAsync($"CREATE INDEX IF NOT EXISTS \"idx_{table}_branch\" ON \"{table}\" (company_id, branch_id)");
        }

        // The original tenant-only key cannot represent one readiness profile per
        // branch. Replace it with a NULL-safe tenant/branch/pack ownership key.
        await db.ExecuteAsync("ALTER TABLE business_tax_readiness DROP CONSTRAINT IF EXISTS business_tax_readiness_company_id_pack_code_key");
        await db.ExecuteAsync("CREATE UNIQUE INDEX IF NOT EXISTS uq_business_tax_readiness_branch_pack ON business_tax_readiness (company_id, COALESCE(branch_id, 0), pack_code)");
        await ClassifyAndBackfillBranchesAsync();
    }

    private async Task ClassifyAndBackfillBranchesAsync()
    {
        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS market_pack_branch_migration_audit (
              id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
              company_id BIGINT NOT NULL,
              table_name VARCHAR(100) NOT NULL,
              row_id BIGINT NOT NULL,
              classification VARCHAR(40) NOT NULL,
              reason VARCHAR(300) NOT NULL,
              resolved_branch_id BIGINT NULL,
              resolved_at TIMESTAMPTZ NULL,
              created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
              UNIQUE(table_name,row_id,classification)
            );

            UPDATE compliance_record_documents d SET branch_id=r.branch_id
              FROM compliance_records r WHERE d.company_id=r.company_id AND d.record_id=r.id AND d.branch_id IS NULL AND r.branch_id IS NOT NULL;
            UPDATE compliance_expiry_events e SET branch_id=r.branch_id
              FROM compliance_records r WHERE e.company_id=r.company_id AND e.record_id=r.id AND e.branch_id IS NULL AND r.branch_id IS NOT NULL;
            UPDATE inspection_defects d SET branch_id=i.branch_id
              FROM vehicle_inspection_records i WHERE d.company_id=i.company_id AND d.inspection_id=i.id AND d.branch_id IS NULL AND i.branch_id IS NOT NULL;

            DO $branch_rollout$
            BEGIN
              IF to_regclass('public.branches') IS NOT NULL THEN
                WITH sole AS (
                  SELECT company_id, MIN(id) branch_id FROM branches
                  WHERE deleted_at IS NULL AND status='Active'
                  GROUP BY company_id HAVING COUNT(*)=1
                )
                UPDATE compliance_records x SET branch_id=s.branch_id FROM sole s WHERE x.company_id=s.company_id AND x.branch_id IS NULL;
                WITH sole AS (SELECT company_id,MIN(id) branch_id FROM branches WHERE deleted_at IS NULL AND status='Active' GROUP BY company_id HAVING COUNT(*)=1)
                UPDATE vehicle_inspection_records x SET branch_id=s.branch_id FROM sole s WHERE x.company_id=s.company_id AND x.branch_id IS NULL;
                WITH sole AS (SELECT company_id,MIN(id) branch_id FROM branches WHERE deleted_at IS NULL AND status='Active' GROUP BY company_id HAVING COUNT(*)=1)
                UPDATE jurisdiction_mileage_records x SET branch_id=s.branch_id FROM sole s WHERE x.company_id=s.company_id AND x.branch_id IS NULL;
                WITH sole AS (SELECT company_id,MIN(id) branch_id FROM branches WHERE deleted_at IS NULL AND status='Active' GROUP BY company_id HAVING COUNT(*)=1)
                UPDATE jurisdiction_fuel_records x SET branch_id=s.branch_id FROM sole s WHERE x.company_id=s.company_id AND x.branch_id IS NULL;
                WITH sole AS (SELECT company_id,MIN(id) branch_id FROM branches WHERE deleted_at IS NULL AND status='Active' GROUP BY company_id HAVING COUNT(*)=1)
                UPDATE driver_duty_status_records x SET branch_id=s.branch_id FROM sole s WHERE x.company_id=s.company_id AND x.branch_id IS NULL;
                WITH sole AS (SELECT company_id,MIN(id) branch_id FROM branches WHERE deleted_at IS NULL AND status='Active' GROUP BY company_id HAVING COUNT(*)=1)
                UPDATE eld_device_registry x SET branch_id=s.branch_id FROM sole s WHERE x.company_id=s.company_id AND x.branch_id IS NULL;
                WITH sole AS (SELECT company_id,MIN(id) branch_id FROM branches WHERE deleted_at IS NULL AND status='Active' GROUP BY company_id HAVING COUNT(*)=1)
                UPDATE business_tax_readiness x SET branch_id=s.branch_id FROM sole s WHERE x.company_id=s.company_id AND x.branch_id IS NULL;
              END IF;
            END $branch_rollout$;

            UPDATE compliance_record_documents d SET branch_id=r.branch_id FROM compliance_records r
              WHERE d.company_id=r.company_id AND d.record_id=r.id AND d.branch_id IS NULL AND r.branch_id IS NOT NULL;
            UPDATE compliance_expiry_events e SET branch_id=r.branch_id FROM compliance_records r
              WHERE e.company_id=r.company_id AND e.record_id=r.id AND e.branch_id IS NULL AND r.branch_id IS NOT NULL;
            UPDATE inspection_defects d SET branch_id=i.branch_id FROM vehicle_inspection_records i
              WHERE d.company_id=i.company_id AND d.inspection_id=i.id AND d.branch_id IS NULL AND i.branch_id IS NOT NULL;

            UPDATE market_pack_branch_migration_audit a SET resolved_branch_id=x.branch_id,resolved_at=NOW()
              FROM compliance_records x WHERE a.table_name='compliance_records' AND a.row_id=x.id AND x.branch_id IS NOT NULL AND a.resolved_at IS NULL;
            UPDATE market_pack_branch_migration_audit a SET resolved_branch_id=x.branch_id,resolved_at=NOW()
              FROM compliance_expiry_events x WHERE a.table_name='compliance_expiry_events' AND a.row_id=x.id AND x.branch_id IS NOT NULL AND a.resolved_at IS NULL;
            UPDATE market_pack_branch_migration_audit a SET resolved_branch_id=x.branch_id,resolved_at=NOW()
              FROM vehicle_inspection_records x WHERE a.table_name='vehicle_inspection_records' AND a.row_id=x.id AND x.branch_id IS NOT NULL AND a.resolved_at IS NULL;
            UPDATE market_pack_branch_migration_audit a SET resolved_branch_id=x.branch_id,resolved_at=NOW()
              FROM inspection_defects x WHERE a.table_name='inspection_defects' AND a.row_id=x.id AND x.branch_id IS NOT NULL AND a.resolved_at IS NULL;
            UPDATE market_pack_branch_migration_audit a SET resolved_branch_id=x.branch_id,resolved_at=NOW()
              FROM jurisdiction_mileage_records x WHERE a.table_name='jurisdiction_mileage_records' AND a.row_id=x.id AND x.branch_id IS NOT NULL AND a.resolved_at IS NULL;
            UPDATE market_pack_branch_migration_audit a SET resolved_branch_id=x.branch_id,resolved_at=NOW()
              FROM jurisdiction_fuel_records x WHERE a.table_name='jurisdiction_fuel_records' AND a.row_id=x.id AND x.branch_id IS NOT NULL AND a.resolved_at IS NULL;
            UPDATE market_pack_branch_migration_audit a SET resolved_branch_id=x.branch_id,resolved_at=NOW()
              FROM driver_duty_status_records x WHERE a.table_name='driver_duty_status_records' AND a.row_id=x.id AND x.branch_id IS NOT NULL AND a.resolved_at IS NULL;
            UPDATE market_pack_branch_migration_audit a SET resolved_branch_id=x.branch_id,resolved_at=NOW()
              FROM eld_device_registry x WHERE a.table_name='eld_device_registry' AND a.row_id=x.id AND x.branch_id IS NOT NULL AND a.resolved_at IS NULL;
            UPDATE market_pack_branch_migration_audit a SET resolved_branch_id=x.branch_id,resolved_at=NOW()
              FROM business_tax_readiness x WHERE a.table_name='business_tax_readiness' AND a.row_id=x.id AND x.branch_id IS NOT NULL AND a.resolved_at IS NULL;

            INSERT INTO market_pack_branch_migration_audit(company_id,table_name,row_id,classification,reason)
            SELECT company_id,'compliance_records',id,'tenant_unassigned','Multiple or zero active branches; manual ownership required' FROM compliance_records WHERE branch_id IS NULL ON CONFLICT DO NOTHING;
            INSERT INTO market_pack_branch_migration_audit(company_id,table_name,row_id,classification,reason)
            SELECT company_id,'compliance_expiry_events',id,'tenant_unassigned','Parent unresolved or event has no canonical record' FROM compliance_expiry_events WHERE branch_id IS NULL ON CONFLICT DO NOTHING;
            INSERT INTO market_pack_branch_migration_audit(company_id,table_name,row_id,classification,reason)
            SELECT company_id,'vehicle_inspection_records',id,'tenant_unassigned','Multiple or zero active branches; manual ownership required' FROM vehicle_inspection_records WHERE branch_id IS NULL ON CONFLICT DO NOTHING;
            INSERT INTO market_pack_branch_migration_audit(company_id,table_name,row_id,classification,reason)
            SELECT company_id,'inspection_defects',id,'tenant_unassigned','Parent inspection ownership unresolved' FROM inspection_defects WHERE branch_id IS NULL ON CONFLICT DO NOTHING;
            INSERT INTO market_pack_branch_migration_audit(company_id,table_name,row_id,classification,reason)
            SELECT company_id,'jurisdiction_mileage_records',id,'tenant_unassigned','Multiple or zero active branches; manual ownership required' FROM jurisdiction_mileage_records WHERE branch_id IS NULL ON CONFLICT DO NOTHING;
            INSERT INTO market_pack_branch_migration_audit(company_id,table_name,row_id,classification,reason)
            SELECT company_id,'jurisdiction_fuel_records',id,'tenant_unassigned','Multiple or zero active branches; manual ownership required' FROM jurisdiction_fuel_records WHERE branch_id IS NULL ON CONFLICT DO NOTHING;
            INSERT INTO market_pack_branch_migration_audit(company_id,table_name,row_id,classification,reason)
            SELECT company_id,'driver_duty_status_records',id,'tenant_unassigned','Multiple or zero active branches; manual ownership required' FROM driver_duty_status_records WHERE branch_id IS NULL ON CONFLICT DO NOTHING;
            INSERT INTO market_pack_branch_migration_audit(company_id,table_name,row_id,classification,reason)
            SELECT company_id,'eld_device_registry',id,'tenant_unassigned','Multiple or zero active branches; manual ownership required' FROM eld_device_registry WHERE branch_id IS NULL ON CONFLICT DO NOTHING;
            INSERT INTO market_pack_branch_migration_audit(company_id,table_name,row_id,classification,reason)
            SELECT company_id,'business_tax_readiness',id,'tenant_unassigned','Multiple or zero active branches; manual ownership required' FROM business_tax_readiness WHERE branch_id IS NULL ON CONFLICT DO NOTHING;
            """);
    }

    // ── Phase 1: market-pack core ───────────────────────────────────────────
    private async Task CoreTablesAsync()
    {
        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS market_packs (
                id               BIGINT       GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                code             VARCHAR(40)  NOT NULL UNIQUE,
                name             VARCHAR(160) NOT NULL,
                description      VARCHAR(500) NULL,
                region           VARCHAR(80)  NOT NULL,
                status           VARCHAR(20)  NOT NULL DEFAULT 'active',  -- planned|active|disabled
                default_currency VARCHAR(8)   NOT NULL DEFAULT 'USD',
                default_distance_unit VARCHAR(12) NOT NULL DEFAULT 'km',
                default_fuel_unit VARCHAR(12) NOT NULL DEFAULT 'liter',
                supported_languages JSONB     NOT NULL DEFAULT '["en"]',
                feature_keys     JSONB        NOT NULL DEFAULT '[]',
                package_key      VARCHAR(80)  NULL,
                base_price_cents BIGINT       NOT NULL DEFAULT 0,
                created_at       TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
                updated_at       TIMESTAMPTZ  NOT NULL DEFAULT NOW()
            )
            """);

        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS market_pack_features (
                id           BIGINT       GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                pack_code    VARCHAR(40)  NOT NULL,
                feature_key  VARCHAR(80)  NOT NULL,
                name         VARCHAR(160) NOT NULL,
                included     BOOLEAN      NOT NULL DEFAULT true,
                tier         VARCHAR(20)  NOT NULL DEFAULT 'included', -- included|premium
                UNIQUE (pack_code, feature_key)
            )
            """);

        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS tenant_market_packs (
                id              BIGINT       GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                company_id      BIGINT       NOT NULL REFERENCES companies(id),
                pack_code       VARCHAR(40)  NOT NULL,
                status          VARCHAR(20)  NOT NULL DEFAULT 'active', -- active|disabled
                price_override_cents BIGINT  NULL,
                enabled_by      VARCHAR(220) NULL,
                enabled_at      TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
                updated_at      TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
                UNIQUE (company_id, pack_code)
            )
            """);
        await db.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_tenant_market_packs_company ON tenant_market_packs (company_id)");

        // Reference / configuration tables (market-scoped, not tenant data).
        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS market_address_schemas (
                id          BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                pack_code   VARCHAR(40) NOT NULL,
                field_key   VARCHAR(60) NOT NULL,
                label_en    VARCHAR(120) NOT NULL,
                label_local VARCHAR(120) NULL,
                required    BOOLEAN NOT NULL DEFAULT false,
                sort_order  INT NOT NULL DEFAULT 100,
                UNIQUE (pack_code, field_key)
            )
            """);
        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS market_document_types (
                id          BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                pack_code   VARCHAR(40) NOT NULL,
                doc_key     VARCHAR(60) NOT NULL,
                name        VARCHAR(160) NOT NULL,
                applies_to  VARCHAR(20) NOT NULL DEFAULT 'driver', -- driver|vehicle|business|transport
                has_expiry  BOOLEAN NOT NULL DEFAULT true,
                UNIQUE (pack_code, doc_key)
            )
            """);
        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS market_driver_requirements (
                id          BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                pack_code   VARCHAR(40) NOT NULL,
                requirement_key VARCHAR(60) NOT NULL,
                name        VARCHAR(160) NOT NULL,
                mandatory   BOOLEAN NOT NULL DEFAULT true,
                UNIQUE (pack_code, requirement_key)
            )
            """);
        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS market_vehicle_requirements (
                id          BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                pack_code   VARCHAR(40) NOT NULL,
                requirement_key VARCHAR(60) NOT NULL,
                name        VARCHAR(160) NOT NULL,
                mandatory   BOOLEAN NOT NULL DEFAULT true,
                UNIQUE (pack_code, requirement_key)
            )
            """);
        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS market_inspection_templates (
                id          BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                pack_code   VARCHAR(40) NOT NULL,
                template_key VARCHAR(60) NOT NULL,
                name        VARCHAR(160) NOT NULL,
                description VARCHAR(400) NULL,
                UNIQUE (pack_code, template_key)
            )
            """);
        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS inspection_items (
                id          BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                template_key VARCHAR(60) NOT NULL,
                item_key    VARCHAR(60) NOT NULL,
                label       VARCHAR(200) NOT NULL,
                category    VARCHAR(60) NULL,
                sort_order  INT NOT NULL DEFAULT 100,
                UNIQUE (template_key, item_key)
            )
            """);
        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS market_tax_reporting_rules (
                id          BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                pack_code   VARCHAR(40) NOT NULL,
                rule_key    VARCHAR(60) NOT NULL,
                name        VARCHAR(160) NOT NULL,
                description VARCHAR(400) NULL,
                UNIQUE (pack_code, rule_key)
            )
            """);
        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS market_unit_settings (
                id          BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                pack_code   VARCHAR(40) NOT NULL UNIQUE,
                distance_unit VARCHAR(12) NOT NULL,
                fuel_unit   VARCHAR(12) NOT NULL,
                weight_unit VARCHAR(12) NOT NULL DEFAULT 'kg'
            )
            """);
        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS market_currency_settings (
                id          BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                pack_code   VARCHAR(40) NOT NULL,
                currency    VARCHAR(8) NOT NULL,
                is_default  BOOLEAN NOT NULL DEFAULT false,
                UNIQUE (pack_code, currency)
            )
            """);
        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS market_language_settings (
                id          BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                pack_code   VARCHAR(40) NOT NULL,
                language    VARCHAR(12) NOT NULL,
                is_default  BOOLEAN NOT NULL DEFAULT false,
                rtl         BOOLEAN NOT NULL DEFAULT false,
                UNIQUE (pack_code, language)
            )
            """);

        // Generic compliance document store — covers Canada driver/vehicle docs AND
        // Saudi transport docs. subject_type/subject_id link to a driver/vehicle when
        // relevant; hijri_expiry_date supports Saudi dual-calendar.
        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS compliance_records (
                id               BIGINT       GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                company_id       BIGINT       NOT NULL,
                pack_code        VARCHAR(40)  NOT NULL,
                subject_type     VARCHAR(20)  NOT NULL DEFAULT 'driver', -- driver|vehicle|business|transport
                subject_id       BIGINT       NULL,
                subject_name     VARCHAR(200) NULL,
                doc_key          VARCHAR(60)  NOT NULL,
                document_no      VARCHAR(120) NULL,
                document_status  VARCHAR(20)  NOT NULL DEFAULT 'valid', -- valid|expiring|expired|pending
                issuing_region   VARCHAR(80)  NULL,
                issuing_country  VARCHAR(8)   NULL,
                issue_date       DATE         NULL,
                expiry_date      DATE         NULL,
                hijri_expiry_date VARCHAR(20) NULL,
                metadata         JSONB        NULL,
                created_at       TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
                updated_at       TIMESTAMPTZ  NOT NULL DEFAULT NOW()
            )
            """);
        await db.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_compliance_records_company ON compliance_records (company_id, pack_code)");

        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS compliance_record_documents (
                id          BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                company_id  BIGINT NOT NULL,
                record_id   BIGINT NOT NULL REFERENCES compliance_records(id) ON DELETE CASCADE,
                file_name   VARCHAR(260) NULL,
                storage_ref VARCHAR(500) NULL,
                uploaded_by VARCHAR(220) NULL,
                created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
            )
            """);

        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS compliance_expiry_events (
                id          BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                company_id  BIGINT NOT NULL,
                pack_code   VARCHAR(40) NOT NULL,
                record_id   BIGINT NULL REFERENCES compliance_records(id) ON DELETE CASCADE,
                subject_type VARCHAR(20) NULL,
                subject_name VARCHAR(200) NULL,
                doc_key     VARCHAR(60) NULL,
                severity    VARCHAR(20) NOT NULL DEFAULT 'warning', -- info|warning|critical
                message     VARCHAR(400) NOT NULL,
                expiry_date DATE NULL,
                acknowledged BOOLEAN NOT NULL DEFAULT false,
                created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
            )
            """);
        await db.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_compliance_expiry_company ON compliance_expiry_events (company_id, pack_code)");
        await db.ExecuteAsync("ALTER TABLE compliance_expiry_events ADD COLUMN IF NOT EXISTS retired_at TIMESTAMPTZ NULL");
        await db.ExecuteAsync("ALTER TABLE compliance_expiry_events ADD COLUMN IF NOT EXISTS retirement_reason VARCHAR(240) NULL");
    }

    // ── Phase 2: Canada / North America operational tables ──────────────────
    private async Task CanadaTablesAsync()
    {
        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS vehicle_inspection_records (
                id              BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                company_id      BIGINT NOT NULL,
                pack_code       VARCHAR(40) NOT NULL DEFAULT 'canada_na',
                template_key    VARCHAR(60) NULL,
                vehicle_id      BIGINT NULL,
                vehicle_label   VARCHAR(160) NULL,
                inspector_name  VARCHAR(160) NULL,
                inspection_type VARCHAR(40) NOT NULL DEFAULT 'pre_trip', -- pre_trip|post_trip|annual
                status          VARCHAR(20) NOT NULL DEFAULT 'pass', -- pass|fail|conditional
                out_of_service  BOOLEAN NOT NULL DEFAULT false,
                repair_status   VARCHAR(24) NOT NULL DEFAULT 'not_required', -- not_required|open|certified
                repair_certified_at TIMESTAMPTZ NULL,
                repair_certified_by VARCHAR(160) NULL,
                inspected_at    TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                notes           VARCHAR(500) NULL,
                created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
            )
            """);
        await db.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_vir_company ON vehicle_inspection_records (company_id)");
        await db.ExecuteAsync("ALTER TABLE vehicle_inspection_records ADD COLUMN IF NOT EXISTS out_of_service BOOLEAN NOT NULL DEFAULT false");
        await db.ExecuteAsync("ALTER TABLE vehicle_inspection_records ADD COLUMN IF NOT EXISTS repair_status VARCHAR(24) NOT NULL DEFAULT 'not_required'");
        await db.ExecuteAsync("ALTER TABLE vehicle_inspection_records ADD COLUMN IF NOT EXISTS repair_certified_at TIMESTAMPTZ NULL");
        await db.ExecuteAsync("ALTER TABLE vehicle_inspection_records ADD COLUMN IF NOT EXISTS repair_certified_by VARCHAR(160) NULL");

        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS inspection_defects (
                id              BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                company_id      BIGINT NOT NULL,
                inspection_id   BIGINT NOT NULL REFERENCES vehicle_inspection_records(id) ON DELETE CASCADE,
                item_key        VARCHAR(60) NULL,
                description     VARCHAR(400) NOT NULL,
                defect_severity VARCHAR(20) NOT NULL DEFAULT 'minor', -- minor|major|critical
                repair_required BOOLEAN NOT NULL DEFAULT false,
                repair_certified_at TIMESTAMPTZ NULL,
                repair_certified_by VARCHAR(160) NULL,
                repair_notes   VARCHAR(500) NULL,
                created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
            )
            """);
        await db.ExecuteAsync("ALTER TABLE inspection_defects ADD COLUMN IF NOT EXISTS repair_certified_by VARCHAR(160) NULL");
        await db.ExecuteAsync("ALTER TABLE inspection_defects ADD COLUMN IF NOT EXISTS repair_notes VARCHAR(500) NULL");

        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS jurisdiction_mileage_records (
                id            BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                company_id    BIGINT NOT NULL,
                vehicle_id    BIGINT NULL,
                vehicle_label VARCHAR(160) NULL,
                province_state VARCHAR(20) NOT NULL,
                country       VARCHAR(8) NOT NULL DEFAULT 'CA',
                distance      NUMERIC(14,2) NOT NULL DEFAULT 0,
                distance_unit VARCHAR(12) NOT NULL DEFAULT 'km',
                tax_period    VARCHAR(20) NOT NULL,
                report_status VARCHAR(20) NOT NULL DEFAULT 'draft',
                created_at    TIMESTAMPTZ NOT NULL DEFAULT NOW()
            )
            """);
        await db.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_jur_mileage_company ON jurisdiction_mileage_records (company_id, tax_period)");

        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS jurisdiction_fuel_records (
                id            BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                company_id    BIGINT NOT NULL,
                vehicle_id    BIGINT NULL,
                vehicle_label VARCHAR(160) NULL,
                province_state VARCHAR(20) NOT NULL,
                country       VARCHAR(8) NOT NULL DEFAULT 'CA',
                fuel_volume   NUMERIC(14,2) NOT NULL DEFAULT 0,
                fuel_unit     VARCHAR(12) NOT NULL DEFAULT 'liter',
                tax_period    VARCHAR(20) NOT NULL,
                report_status VARCHAR(20) NOT NULL DEFAULT 'draft',
                created_at    TIMESTAMPTZ NOT NULL DEFAULT NOW()
            )
            """);
        await db.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_jur_fuel_company ON jurisdiction_fuel_records (company_id, tax_period)");

        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS driver_duty_status_records (
                id            BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                company_id    BIGINT NOT NULL,
                driver_id     BIGINT NULL,
                driver_name   VARCHAR(160) NULL,
                duty_status   VARCHAR(30) NOT NULL DEFAULT 'off_duty', -- off_duty|sleeper|driving|on_duty
                hos_cycle     VARCHAR(40) NULL,
                log_certification_status VARCHAR(30) NOT NULL DEFAULT 'uncertified',
                recorded_at   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                created_at    TIMESTAMPTZ NOT NULL DEFAULT NOW()
            )
            """);

        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS eld_device_registry (
                id            BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                company_id    BIGINT NOT NULL,
                eld_provider_name VARCHAR(160) NOT NULL,
                eld_device_identifier VARCHAR(120) NULL,
                eld_connection_status VARCHAR(30) NOT NULL DEFAULT 'planned', -- planned|connected|disconnected
                vehicle_id    BIGINT NULL,
                notes         VARCHAR(400) NULL,
                created_at    TIMESTAMPTZ NOT NULL DEFAULT NOW()
            )
            """);
    }

    // ── Phase 3: Saudi / GCC operational tables ─────────────────────────────
    private async Task SaudiTablesAsync()
    {
        // Reusable national-address store (NOT tied to shipment stops).
        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS market_addresses (
                id            BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                company_id    BIGINT NOT NULL,
                pack_code     VARCHAR(40) NOT NULL DEFAULT 'saudi_gcc',
                label         VARCHAR(160) NULL,
                national_address_building_no VARCHAR(20) NULL,
                national_address_additional_no VARCHAR(20) NULL,
                district      VARCHAR(120) NULL,
                city          VARCHAR(120) NULL,
                region        VARCHAR(120) NULL,
                postal_code   VARCHAR(20) NULL,
                country       VARCHAR(8) NOT NULL DEFAULT 'SA',
                created_at    TIMESTAMPTZ NOT NULL DEFAULT NOW()
            )
            """);
        await db.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_market_addresses_company ON market_addresses (company_id)");

        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS business_tax_readiness (
                id            BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                company_id    BIGINT NOT NULL,
                pack_code     VARCHAR(40) NOT NULL DEFAULT 'saudi_gcc',
                vat_number    VARCHAR(40) NULL,
                commercial_registration_no VARCHAR(40) NULL,
                e_invoice_readiness_status VARCHAR(30) NOT NULL DEFAULT 'not_ready', -- not_ready|in_progress|ready
                updated_by    VARCHAR(220) NULL,
                updated_at    TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                UNIQUE (company_id, pack_code)
            )
            """);
        await db.ExecuteAsync("ALTER TABLE business_tax_readiness ADD COLUMN IF NOT EXISTS evidence_record_id BIGINT NULL");
    }

    // ── Reference seeds (always run; idempotent) ────────────────────────────
    private async Task SeedReferenceAsync()
    {
        await UpsertPack(Packs.CanadaNa, "Canada / North America", "Cross-border NA fleet compliance, DVIR, HOS/ELD readiness and IFTA fuel-tax foundation.",
            "North America", "CAD", "km", "liter", new[] { "en", "fr" }, "canada_na_compliance", 49900,
            new[] { Features.MarketCanadaNa, Features.Documents, Features.ExpiryAlerts, Features.Inspections, Features.DriverQualification, Features.VehicleDocuments, Features.TaxReadiness });
        await UpsertPack(Packs.SaudiGcc, "Saudi / GCC", "Saudi & GCC transport compliance, National Address, VAT / e-invoice readiness with Hijri/Gregorian expiry.",
            "Middle East", "SAR", "km", "liter", new[] { "ar", "en" }, "saudi_gcc_compliance", 49900,
            new[] { Features.MarketSaudiGcc, Features.Documents, Features.ExpiryAlerts, Features.VehicleDocuments, Features.TaxReadiness });

        // Feature catalog rows
        var canadaFeatures = new (string Key, string Name, string Tier)[]
        {
            (Features.MarketCanadaNa, "Canada / North America Market Pack", "included"),
            (Features.Documents, "Compliance Documents", "included"),
            (Features.ExpiryAlerts, "Expiry Alerts", "included"),
            (Features.Inspections, "Vehicle Inspections / DVIR", "included"),
            (Features.DriverQualification, "Driver Qualification", "included"),
            (Features.VehicleDocuments, "Vehicle Documents", "included"),
            (Features.TaxReadiness, "IFTA / Fuel-Tax Readiness", "included"),
        };
        foreach (var f in canadaFeatures) await UpsertFeature(Packs.CanadaNa, f.Key, f.Name, f.Tier);

        var saudiFeatures = new (string Key, string Name, string Tier)[]
        {
            (Features.MarketSaudiGcc, "Saudi / GCC Market Pack", "included"),
            (Features.Documents, "Compliance Documents", "included"),
            (Features.ExpiryAlerts, "Expiry Alerts", "included"),
            (Features.VehicleDocuments, "Transport Documents", "included"),
            (Features.TaxReadiness, "VAT / e-Invoice Readiness", "included"),
        };
        foreach (var f in saudiFeatures) await UpsertFeature(Packs.SaudiGcc, f.Key, f.Name, f.Tier);

        // Units / currencies / languages
        await UpsertUnits(Packs.CanadaNa, "km", "liter", "kg");
        await UpsertUnits(Packs.SaudiGcc, "km", "liter", "kg");
        await UpsertCurrency(Packs.CanadaNa, "CAD", true); await UpsertCurrency(Packs.CanadaNa, "USD", false);
        await UpsertCurrency(Packs.SaudiGcc, "SAR", true); await UpsertCurrency(Packs.SaudiGcc, "AED", false);
        await UpsertLanguage(Packs.CanadaNa, "en", true, false); await UpsertLanguage(Packs.CanadaNa, "fr", false, false);
        await UpsertLanguage(Packs.SaudiGcc, "ar", true, true); await UpsertLanguage(Packs.SaudiGcc, "en", false, false);

        // Address schemas
        await UpsertAddressField(Packs.CanadaNa, "street", "Street Address", null, true, 1);
        await UpsertAddressField(Packs.CanadaNa, "city", "City", null, true, 2);
        await UpsertAddressField(Packs.CanadaNa, "province_state", "Province / State", null, true, 3);
        await UpsertAddressField(Packs.CanadaNa, "postal_code", "Postal / ZIP Code", null, true, 4);
        await UpsertAddressField(Packs.CanadaNa, "country", "Country", null, true, 5);
        await UpsertAddressField(Packs.SaudiGcc, "national_address_building_no", "Building No.", "رقم المبنى", true, 1);
        await UpsertAddressField(Packs.SaudiGcc, "national_address_additional_no", "Additional No.", "الرقم الإضافي", false, 2);
        await UpsertAddressField(Packs.SaudiGcc, "district", "District", "الحي", true, 3);
        await UpsertAddressField(Packs.SaudiGcc, "city", "City", "المدينة", true, 4);
        await UpsertAddressField(Packs.SaudiGcc, "region", "Region", "المنطقة", true, 5);
        await UpsertAddressField(Packs.SaudiGcc, "postal_code", "Postal Code", "الرمز البريدي", true, 6);

        // Document types
        await UpsertDocType(Packs.CanadaNa, "drivers_license", "Driver's License", "driver");
        await UpsertDocType(Packs.CanadaNa, "medical_certificate", "Medical Certificate", "driver");
        await UpsertDocType(Packs.CanadaNa, "endorsement", "Endorsement", "driver");
        await UpsertDocType(Packs.CanadaNa, "vehicle_registration", "Vehicle Registration", "vehicle");
        await UpsertDocType(Packs.CanadaNa, "annual_inspection", "Annual Inspection Certificate", "vehicle");
        await UpsertDocType(Packs.SaudiGcc, "transport_permit", "Transport Permit", "transport");
        await UpsertDocType(Packs.SaudiGcc, "operating_card", "Operating Card", "vehicle");
        await UpsertDocType(Packs.SaudiGcc, "istimara", "Istimara (Registration)", "vehicle");

        // Driver / vehicle requirements
        await UpsertDriverReq(Packs.CanadaNa, "valid_license", "Valid commercial license");
        await UpsertDriverReq(Packs.CanadaNa, "medical_card", "Valid medical card");
        await UpsertDriverReq(Packs.CanadaNa, "hos_logs", "HOS duty-status logging");
        await UpsertVehicleReq(Packs.CanadaNa, "annual_inspection", "Annual safety inspection");
        await UpsertVehicleReq(Packs.CanadaNa, "dvir", "Daily vehicle inspection (DVIR)");
        await UpsertDriverReq(Packs.SaudiGcc, "transport_permit", "Valid transport permit");
        await UpsertVehicleReq(Packs.SaudiGcc, "operating_card", "Valid operating card");

        // Inspection templates + items
        await UpsertTemplate(Packs.CanadaNa, "dvir_pre_trip", "DVIR Pre-Trip Inspection", "Daily pre-trip vehicle inspection report.");
        await UpsertTemplate(Packs.CanadaNa, "annual_safety", "Annual Safety Inspection", "Annual commercial vehicle safety inspection.");
        foreach (var it in new (string Item, string Label, string Cat, int Sort)[]
        {
            ("brakes", "Brakes", "mechanical", 1), ("tires", "Tires & Wheels", "mechanical", 2),
            ("lights", "Lights & Reflectors", "electrical", 3), ("coupling", "Coupling Devices", "mechanical", 4),
            ("emergency", "Emergency Equipment", "safety", 5),
        }) await UpsertItem("dvir_pre_trip", it.Item, it.Label, it.Cat, it.Sort);

        // Tax reporting rules
        await UpsertTaxRule(Packs.CanadaNa, "ifta_quarterly", "IFTA Quarterly Fuel Tax", "Per-jurisdiction mileage & fuel reconciliation (readiness — not an official filing).");
        await UpsertTaxRule(Packs.SaudiGcc, "vat_standard", "Saudi VAT (15%)", "VAT / e-invoice readiness foundation (not an official ZATCA integration).");
    }

    // ── Revenue mapping (module_packages + usage meters + pricing) ──────────
    private async Task SeedRevenueMappingAsync()
    {
        // Two market packages in the module-package catalog.
        foreach (var (key, name, modules, price) in new (string, string, string, long)[]
        {
            ("canada_na_compliance", "Canada / North America Compliance", "[\"market.canada_na\"]", 49900),
            ("saudi_gcc_compliance", "Saudi / GCC Compliance", "[\"market.saudi_gcc\"]", 49900),
        })
        {
            await db.ExecuteAsync("""
                INSERT INTO module_packages (package_key, name, category, module_keys, is_core, base_price_cents, sort_order)
                VALUES (@k, @n, 'market', @mk::jsonb, false, @p, 200)
                ON CONFLICT (package_key) DO UPDATE SET name=EXCLUDED.name, category='market',
                    module_keys=EXCLUDED.module_keys, base_price_cents=EXCLUDED.base_price_cents
                """,
                c => { c.Parameters.AddWithValue("@k", key); c.Parameters.AddWithValue("@n", name); c.Parameters.AddWithValue("@mk", modules); c.Parameters.AddWithValue("@p", price); });
        }

        // Usage meters for compliance.
        foreach (var (key, mname, period) in new (string, string, string)[]
        {
            ("compliance_documents.count", "Compliance documents", "lifetime"),
            ("compliance_expiry_alerts.monthly", "Compliance expiry alerts / month", "monthly"),
            ("inspection_records.monthly", "Inspection records / month", "monthly"),
        })
        {
            await db.ExecuteAsync("""
                INSERT INTO usage_meters (meter_key, name, unit, aggregation, period, module_key)
                VALUES (@k, @n, 'count', 'sum', @p, 'compliance.documents')
                ON CONFLICT (meter_key) DO UPDATE SET name=EXCLUDED.name, period=EXCLUDED.period
                """,
                c => { c.Parameters.AddWithValue("@k", key); c.Parameters.AddWithValue("@n", mname); c.Parameters.AddWithValue("@p", period); });
        }

        // Pricing tier rows for the two market packs (so pricing_rules can attach).
        foreach (var (code, name, price) in new (string, string, long)[]
        {
            ("canada_na_compliance", "Canada / North America Compliance", 49900),
            ("saudi_gcc_compliance", "Saudi / GCC Compliance", 49900),
        })
        {
            var pkgId = await db.ScalarLongAsync("SELECT id FROM packages WHERE package_code=@c", c => c.Parameters.AddWithValue("@c", code));
            if (pkgId <= 0)
            {
                pkgId = await db.InsertAsync("""
                    INSERT INTO packages (package_code, name, billing_interval, currency, base_price_cents, module_keys, is_custom, active)
                    VALUES (@c, @n, 'monthly', 'USD', @p, '[]'::jsonb, false, true) RETURNING id
                    """,
                    c => { c.Parameters.AddWithValue("@c", code); c.Parameters.AddWithValue("@n", name); c.Parameters.AddWithValue("@p", price); });
            }
            if (pkgId > 0)
            {
                await db.ExecuteAsync("""
                    INSERT INTO pricing_rules (package_id, meter_key, included_quantity, unit_price_cents, currency, overage_allowed, active)
                    VALUES (@pkg, 'compliance_documents.count', 50, 200, 'USD', true, true)
                    ON CONFLICT (package_id, meter_key) DO NOTHING
                    """, c => c.Parameters.AddWithValue("@pkg", pkgId));
            }
        }
    }

    // ── tiny idempotent upsert helpers ──────────────────────────────────────
    private Task UpsertPack(string code, string name, string desc, string region, string currency, string dist, string fuel, string[] langs, string pkgKey, long price, string[] features)
    {
        var langsJson = "[" + string.Join(",", langs.Select(l => $"\"{l}\"")) + "]";
        var featJson = "[" + string.Join(",", features.Select(f => $"\"{f}\"")) + "]";
        return db.ExecuteAsync("""
            INSERT INTO market_packs (code, name, description, region, status, default_currency, default_distance_unit, default_fuel_unit, supported_languages, feature_keys, package_key, base_price_cents)
            VALUES (@c,@n,@d,@r,'active',@cur,@dist,@fuel,@langs::jsonb,@feat::jsonb,@pkg,@price)
            ON CONFLICT (code) DO UPDATE SET name=EXCLUDED.name, description=EXCLUDED.description,
                default_currency=EXCLUDED.default_currency, supported_languages=EXCLUDED.supported_languages,
                feature_keys=EXCLUDED.feature_keys, package_key=EXCLUDED.package_key, base_price_cents=EXCLUDED.base_price_cents, updated_at=NOW()
            """,
            c => { c.Parameters.AddWithValue("@c", code); c.Parameters.AddWithValue("@n", name); c.Parameters.AddWithValue("@d", desc); c.Parameters.AddWithValue("@r", region); c.Parameters.AddWithValue("@cur", currency); c.Parameters.AddWithValue("@dist", dist); c.Parameters.AddWithValue("@fuel", fuel); c.Parameters.AddWithValue("@langs", langsJson); c.Parameters.AddWithValue("@feat", featJson); c.Parameters.AddWithValue("@pkg", pkgKey); c.Parameters.AddWithValue("@price", price); });
    }

    private Task UpsertFeature(string pack, string key, string name, string tier) => db.ExecuteAsync(
        "INSERT INTO market_pack_features (pack_code, feature_key, name, tier) VALUES (@p,@k,@n,@t) ON CONFLICT (pack_code,feature_key) DO UPDATE SET name=EXCLUDED.name, tier=EXCLUDED.tier",
        c => { c.Parameters.AddWithValue("@p", pack); c.Parameters.AddWithValue("@k", key); c.Parameters.AddWithValue("@n", name); c.Parameters.AddWithValue("@t", tier); });

    private Task UpsertUnits(string pack, string dist, string fuel, string weight) => db.ExecuteAsync(
        "INSERT INTO market_unit_settings (pack_code, distance_unit, fuel_unit, weight_unit) VALUES (@p,@d,@f,@w) ON CONFLICT (pack_code) DO UPDATE SET distance_unit=EXCLUDED.distance_unit, fuel_unit=EXCLUDED.fuel_unit, weight_unit=EXCLUDED.weight_unit",
        c => { c.Parameters.AddWithValue("@p", pack); c.Parameters.AddWithValue("@d", dist); c.Parameters.AddWithValue("@f", fuel); c.Parameters.AddWithValue("@w", weight); });

    private Task UpsertCurrency(string pack, string cur, bool def) => db.ExecuteAsync(
        "INSERT INTO market_currency_settings (pack_code, currency, is_default) VALUES (@p,@c,@d) ON CONFLICT (pack_code,currency) DO UPDATE SET is_default=EXCLUDED.is_default",
        c => { c.Parameters.AddWithValue("@p", pack); c.Parameters.AddWithValue("@c", cur); c.Parameters.AddWithValue("@d", def); });

    private Task UpsertLanguage(string pack, string lang, bool def, bool rtl) => db.ExecuteAsync(
        "INSERT INTO market_language_settings (pack_code, language, is_default, rtl) VALUES (@p,@l,@d,@r) ON CONFLICT (pack_code,language) DO UPDATE SET is_default=EXCLUDED.is_default, rtl=EXCLUDED.rtl",
        c => { c.Parameters.AddWithValue("@p", pack); c.Parameters.AddWithValue("@l", lang); c.Parameters.AddWithValue("@d", def); c.Parameters.AddWithValue("@r", rtl); });

    private Task UpsertAddressField(string pack, string key, string en, string? local, bool req, int sort) => db.ExecuteAsync(
        "INSERT INTO market_address_schemas (pack_code, field_key, label_en, label_local, required, sort_order) VALUES (@p,@k,@en,@loc,@req,@s) ON CONFLICT (pack_code,field_key) DO UPDATE SET label_en=EXCLUDED.label_en, label_local=EXCLUDED.label_local, required=EXCLUDED.required, sort_order=EXCLUDED.sort_order",
        c => { c.Parameters.AddWithValue("@p", pack); c.Parameters.AddWithValue("@k", key); c.Parameters.AddWithValue("@en", en); c.Parameters.AddWithValue("@loc", (object?)local ?? DBNull.Value); c.Parameters.AddWithValue("@req", req); c.Parameters.AddWithValue("@s", sort); });

    private Task UpsertDocType(string pack, string key, string name, string applies) => db.ExecuteAsync(
        "INSERT INTO market_document_types (pack_code, doc_key, name, applies_to) VALUES (@p,@k,@n,@a) ON CONFLICT (pack_code,doc_key) DO UPDATE SET name=EXCLUDED.name, applies_to=EXCLUDED.applies_to",
        c => { c.Parameters.AddWithValue("@p", pack); c.Parameters.AddWithValue("@k", key); c.Parameters.AddWithValue("@n", name); c.Parameters.AddWithValue("@a", applies); });

    private Task UpsertDriverReq(string pack, string key, string name) => db.ExecuteAsync(
        "INSERT INTO market_driver_requirements (pack_code, requirement_key, name) VALUES (@p,@k,@n) ON CONFLICT (pack_code,requirement_key) DO UPDATE SET name=EXCLUDED.name",
        c => { c.Parameters.AddWithValue("@p", pack); c.Parameters.AddWithValue("@k", key); c.Parameters.AddWithValue("@n", name); });

    private Task UpsertVehicleReq(string pack, string key, string name) => db.ExecuteAsync(
        "INSERT INTO market_vehicle_requirements (pack_code, requirement_key, name) VALUES (@p,@k,@n) ON CONFLICT (pack_code,requirement_key) DO UPDATE SET name=EXCLUDED.name",
        c => { c.Parameters.AddWithValue("@p", pack); c.Parameters.AddWithValue("@k", key); c.Parameters.AddWithValue("@n", name); });

    private Task UpsertTemplate(string pack, string key, string name, string desc) => db.ExecuteAsync(
        "INSERT INTO market_inspection_templates (pack_code, template_key, name, description) VALUES (@p,@k,@n,@d) ON CONFLICT (pack_code,template_key) DO UPDATE SET name=EXCLUDED.name, description=EXCLUDED.description",
        c => { c.Parameters.AddWithValue("@p", pack); c.Parameters.AddWithValue("@k", key); c.Parameters.AddWithValue("@n", name); c.Parameters.AddWithValue("@d", desc); });

    private Task UpsertItem(string template, string key, string label, string cat, int sort) => db.ExecuteAsync(
        "INSERT INTO inspection_items (template_key, item_key, label, category, sort_order) VALUES (@t,@k,@l,@c,@s) ON CONFLICT (template_key,item_key) DO UPDATE SET label=EXCLUDED.label, category=EXCLUDED.category",
        c => { c.Parameters.AddWithValue("@t", template); c.Parameters.AddWithValue("@k", key); c.Parameters.AddWithValue("@l", label); c.Parameters.AddWithValue("@c", cat); c.Parameters.AddWithValue("@s", sort); });

    private Task UpsertTaxRule(string pack, string key, string name, string desc) => db.ExecuteAsync(
        "INSERT INTO market_tax_reporting_rules (pack_code, rule_key, name, description) VALUES (@p,@k,@n,@d) ON CONFLICT (pack_code,rule_key) DO UPDATE SET name=EXCLUDED.name, description=EXCLUDED.description",
        c => { c.Parameters.AddWithValue("@p", pack); c.Parameters.AddWithValue("@k", key); c.Parameters.AddWithValue("@n", name); c.Parameters.AddWithValue("@d", desc); });
}
