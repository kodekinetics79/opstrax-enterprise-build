using Microsoft.Extensions.Logging;
using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

// Fleet TMS (PR2) — schema bootstrap for cold chain, returnable assets and Saudi
// fleet-readiness, ported from the Zayra opstrax-codex-backup branch. Same discipline
// as PR1: every table is net-new, `fleet_tms_`-prefixed, tenant-scoped via
// `company_id BIGINT` (the Saudi region reference is global, no company_id), and all
// DDL is idempotent (runs at startup).
public sealed class FleetTmsColdChainSchemaService(Database db, ILogger<FleetTmsColdChainSchemaService> log)
{
    public async Task EnsureAsync()
    {
        await CreateColdChain();
        await CreateAssets();
        await CreateReadiness();
        await EnsureBranchOwnership();
        await ClassifyLegacyBranchRows();
        await EnsureAssetIntegrity();
        await CreateIndexes();
    }

    // Operational Fleet records are branch-owned when created by a branch-scoped
    // account. Legacy NULL rows remain tenant-level and are intentionally invisible
    // to branch accounts; tenant-wide operators retain full visibility.
    private async Task EnsureBranchOwnership()
    {
        string[] tables =
        [
            "fleet_tms_temperature_zones", "fleet_tms_temperature_devices",
            "fleet_tms_temperature_readings", "fleet_tms_temperature_alerts",
            "fleet_tms_cold_chain_reports", "fleet_tms_refrigeration_unit_health",
            "fleet_tms_asset_types", "fleet_tms_assets", "fleet_tms_asset_assignments",
            "fleet_tms_asset_events", "fleet_tms_barcode_scan_events", "fleet_tms_rfid_events",
            "fleet_tms_readiness_documents"
        ];

        foreach (var table in tables)
        {
            await db.ExecuteAsync($"ALTER TABLE \"{table}\" ADD COLUMN IF NOT EXISTS branch_id BIGINT NULL");
            await db.ExecuteAsync($"CREATE INDEX IF NOT EXISTS \"idx_{table}_branch\" ON \"{table}\" (company_id, branch_id)");
        }
    }

    private async Task EnsureAssetIntegrity()
    {
        const string sql = @"
DO $asset_quantity_constraints$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='ck_ftms_assets_quantity_positive') THEN
    ALTER TABLE fleet_tms_assets ADD CONSTRAINT ck_ftms_assets_quantity_positive CHECK (quantity > 0);
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='ck_ftms_asset_assignments_quantity_positive') THEN
    ALTER TABLE fleet_tms_asset_assignments ADD CONSTRAINT ck_ftms_asset_assignments_quantity_positive CHECK (quantity > 0);
  END IF;
END $asset_quantity_constraints$;

CREATE UNIQUE INDEX IF NOT EXISTS ux_ftms_assets_branch_tag_normalized
  ON fleet_tms_assets (company_id, COALESCE(branch_id, 0), lower(btrim(asset_tag)));

CREATE UNIQUE INDEX IF NOT EXISTS ux_ftms_asset_active_custody
  ON fleet_tms_asset_assignments (company_id, COALESCE(branch_id, 0), asset_id)
  WHERE released_at_utc IS NULL AND status IN ('Assigned','CheckedOut','InUse');";
        await db.ExecuteAsync(sql);
    }

    private async Task ClassifyLegacyBranchRows()
    {
        const string sql = @"
CREATE TABLE IF NOT EXISTS fleet_tms_branch_migration_audit (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  source_table VARCHAR(120) NOT NULL,
  source_id BIGINT NOT NULL,
  classification VARCHAR(60) NOT NULL,
  reason TEXT NOT NULL DEFAULT '',
  classified_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_ftms_branch_migration_source
  ON fleet_tms_branch_migration_audit(company_id, source_table, source_id);

UPDATE fleet_tms_temperature_readings x SET branch_id=d.branch_id FROM fleet_tms_temperature_devices d
 WHERE x.company_id=d.company_id AND x.device_id=d.id AND x.branch_id IS NULL AND d.branch_id IS NOT NULL;
UPDATE fleet_tms_temperature_alerts x SET branch_id=d.branch_id FROM fleet_tms_temperature_devices d
 WHERE x.company_id=d.company_id AND x.device_id=d.id AND x.branch_id IS NULL AND d.branch_id IS NOT NULL;
UPDATE fleet_tms_asset_assignments x SET branch_id=a.branch_id FROM fleet_tms_assets a
 WHERE x.company_id=a.company_id AND x.asset_id=a.id AND x.branch_id IS NULL AND a.branch_id IS NOT NULL;
UPDATE fleet_tms_asset_events x SET branch_id=a.branch_id FROM fleet_tms_assets a
 WHERE x.company_id=a.company_id AND x.asset_id=a.id AND x.branch_id IS NULL AND a.branch_id IS NOT NULL;
UPDATE fleet_tms_barcode_scan_events x SET branch_id=a.branch_id FROM fleet_tms_assets a
 WHERE x.company_id=a.company_id AND x.asset_id=a.id AND x.branch_id IS NULL AND a.branch_id IS NOT NULL;
UPDATE fleet_tms_rfid_events x SET branch_id=a.branch_id FROM fleet_tms_assets a
 WHERE x.company_id=a.company_id AND x.asset_id=a.id AND x.branch_id IS NULL AND a.branch_id IS NOT NULL;

DO $specialized_sole_branch$
BEGIN
 IF to_regclass('public.branches') IS NOT NULL THEN
   CREATE TEMP TABLE ftms_sole_branch ON COMMIT DROP AS
     SELECT company_id, MIN(id) branch_id FROM branches
     WHERE deleted_at IS NULL AND status='Active' GROUP BY company_id HAVING COUNT(*)=1;
   UPDATE fleet_tms_temperature_devices x SET branch_id=b.branch_id FROM ftms_sole_branch b WHERE x.company_id=b.company_id AND x.branch_id IS NULL;
   UPDATE fleet_tms_assets x SET branch_id=b.branch_id FROM ftms_sole_branch b WHERE x.company_id=b.company_id AND x.branch_id IS NULL;
   UPDATE fleet_tms_readiness_documents x SET branch_id=b.branch_id FROM ftms_sole_branch b WHERE x.company_id=b.company_id AND x.branch_id IS NULL;
 END IF;
END $specialized_sole_branch$;

UPDATE fleet_tms_temperature_readings x SET branch_id=d.branch_id FROM fleet_tms_temperature_devices d
 WHERE x.company_id=d.company_id AND x.device_id=d.id AND x.branch_id IS NULL AND d.branch_id IS NOT NULL;
UPDATE fleet_tms_temperature_alerts x SET branch_id=d.branch_id FROM fleet_tms_temperature_devices d
 WHERE x.company_id=d.company_id AND x.device_id=d.id AND x.branch_id IS NULL AND d.branch_id IS NOT NULL;
UPDATE fleet_tms_asset_assignments x SET branch_id=a.branch_id FROM fleet_tms_assets a
 WHERE x.company_id=a.company_id AND x.asset_id=a.id AND x.branch_id IS NULL AND a.branch_id IS NOT NULL;
UPDATE fleet_tms_asset_events x SET branch_id=a.branch_id FROM fleet_tms_assets a
 WHERE x.company_id=a.company_id AND x.asset_id=a.id AND x.branch_id IS NULL AND a.branch_id IS NOT NULL;
UPDATE fleet_tms_barcode_scan_events x SET branch_id=a.branch_id FROM fleet_tms_assets a
 WHERE x.company_id=a.company_id AND x.asset_id=a.id AND x.branch_id IS NULL AND a.branch_id IS NOT NULL;
UPDATE fleet_tms_rfid_events x SET branch_id=a.branch_id FROM fleet_tms_assets a
 WHERE x.company_id=a.company_id AND x.asset_id=a.id AND x.branch_id IS NULL AND a.branch_id IS NOT NULL;

INSERT INTO fleet_tms_branch_migration_audit(company_id,source_table,source_id,classification,reason)
 SELECT company_id,'fleet_tms_temperature_devices',id,'tenant_unassigned','Multiple/no active branches; requires operator mapping.'
 FROM fleet_tms_temperature_devices WHERE branch_id IS NULL ON CONFLICT DO NOTHING;
INSERT INTO fleet_tms_branch_migration_audit(company_id,source_table,source_id,classification,reason)
 SELECT company_id,'fleet_tms_assets',id,'tenant_unassigned','Multiple/no active branches; requires operator mapping.'
 FROM fleet_tms_assets WHERE branch_id IS NULL ON CONFLICT DO NOTHING;
INSERT INTO fleet_tms_branch_migration_audit(company_id,source_table,source_id,classification,reason)
 SELECT company_id,'fleet_tms_readiness_documents',id,'tenant_unassigned','Multiple/no active branches; requires operator mapping.'
 FROM fleet_tms_readiness_documents WHERE branch_id IS NULL ON CONFLICT DO NOTHING;";
        await db.ExecuteAsync(sql);
    }

    private async Task CreateColdChain()
    {
        await TryCreate("fleet_tms_temperature_zones", @"
CREATE TABLE IF NOT EXISTS fleet_tms_temperature_zones (
    id             BIGINT NOT NULL GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id     BIGINT NOT NULL,
    code           VARCHAR(40)  NOT NULL DEFAULT '',
    name           VARCHAR(120) NOT NULL DEFAULT '',
    min_celsius    NUMERIC(6,2) NOT NULL DEFAULT 0,
    max_celsius    NUMERIC(6,2) NOT NULL DEFAULT 0,
    color          VARCHAR(30)  NOT NULL DEFAULT '',
    is_active      BOOLEAN      NOT NULL DEFAULT true,
    notes          TEXT         NOT NULL DEFAULT '',
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at_utc TIMESTAMPTZ NULL
)");

        await TryCreate("fleet_tms_temperature_devices", @"
CREATE TABLE IF NOT EXISTS fleet_tms_temperature_devices (
    id                                BIGINT NOT NULL GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id                        BIGINT NOT NULL,
    device_code                       VARCHAR(60)  NOT NULL DEFAULT '',
    name                              VARCHAR(120) NOT NULL DEFAULT '',
    zone_id                           BIGINT NULL,
    shipment_id                       BIGINT NULL,
    vehicle_number                    VARCHAR(60)  NOT NULL DEFAULT '',
    status                            VARCHAR(30)  NOT NULL DEFAULT 'Active',
    last_reported_temperature_celsius NUMERIC(6,2) NOT NULL DEFAULT 0,
    battery_percent                   NUMERIC(6,2) NOT NULL DEFAULT 0,
    last_ping_at_utc                  TIMESTAMPTZ NULL,
    notes                             TEXT         NOT NULL DEFAULT '',
    created_at_utc                    TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at_utc                    TIMESTAMPTZ NULL
)");

        await TryCreate("fleet_tms_temperature_readings", @"
CREATE TABLE IF NOT EXISTS fleet_tms_temperature_readings (
    id                  BIGINT NOT NULL GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id          BIGINT NOT NULL,
    device_id           BIGINT NOT NULL,
    shipment_id         BIGINT NULL,
    zone_id             BIGINT NULL,
    temperature_celsius NUMERIC(6,2) NOT NULL DEFAULT 0,
    humidity_percent    NUMERIC(6,2) NULL,
    latitude            NUMERIC(10,6) NULL,
    longitude           NUMERIC(10,6) NULL,
    source              VARCHAR(30)  NOT NULL DEFAULT 'Sensor',
    status              VARCHAR(30)  NOT NULL DEFAULT 'Normal',
    notes               TEXT         NOT NULL DEFAULT '',
    recorded_at_utc     TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_at_utc      TIMESTAMPTZ NOT NULL DEFAULT NOW()
)");

        await TryCreate("fleet_tms_temperature_alerts", @"
CREATE TABLE IF NOT EXISTS fleet_tms_temperature_alerts (
    id                   BIGINT NOT NULL GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id           BIGINT NOT NULL,
    device_id            BIGINT NOT NULL,
    shipment_id          BIGINT NULL,
    reading_id           BIGINT NOT NULL DEFAULT 0,
    alert_type           VARCHAR(60)  NOT NULL DEFAULT 'TemperatureBreach',
    severity             VARCHAR(30)  NOT NULL DEFAULT 'High',
    status               VARCHAR(30)  NOT NULL DEFAULT 'Open',
    threshold_min        NUMERIC(6,2) NOT NULL DEFAULT 0,
    threshold_max        NUMERIC(6,2) NOT NULL DEFAULT 0,
    measured_temperature NUMERIC(6,2) NOT NULL DEFAULT 0,
    triggered_at_utc     TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    resolved_at_utc      TIMESTAMPTZ NULL,
    resolved_by          VARCHAR(255) NOT NULL DEFAULT '',
    resolution_notes     TEXT         NOT NULL DEFAULT '',
    notes                TEXT         NOT NULL DEFAULT ''
)");

        await TryCreate("fleet_tms_cold_chain_reports", @"
CREATE TABLE IF NOT EXISTS fleet_tms_cold_chain_reports (
    id                      BIGINT NOT NULL GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id              BIGINT NOT NULL,
    shipment_id             BIGINT NOT NULL,
    shipment_number         VARCHAR(60)  NOT NULL DEFAULT '',
    generated_at_utc        TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    compliance_percent      NUMERIC(6,2) NOT NULL DEFAULT 0,
    min_temperature_celsius NUMERIC(6,2) NOT NULL DEFAULT 0,
    max_temperature_celsius NUMERIC(6,2) NOT NULL DEFAULT 0,
    total_readings          INT          NOT NULL DEFAULT 0,
    breach_count            INT          NOT NULL DEFAULT 0,
    summary_json            JSONB        NOT NULL DEFAULT '{}',
    notes                   TEXT         NOT NULL DEFAULT ''
)");

        await TryCreate("fleet_tms_refrigeration_unit_health", @"
CREATE TABLE IF NOT EXISTS fleet_tms_refrigeration_unit_health (
    id                          BIGINT NOT NULL GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id                  BIGINT NOT NULL,
    vehicle_number              VARCHAR(60)  NOT NULL DEFAULT '',
    unit_serial                 VARCHAR(80)  NOT NULL DEFAULT '',
    status                      VARCHAR(30)  NOT NULL DEFAULT 'Healthy',
    compressor_hours            NUMERIC(12,2) NOT NULL DEFAULT 0,
    last_service_at_utc         TIMESTAMPTZ NULL,
    next_service_due_at_utc     TIMESTAMPTZ NULL,
    temperature_deviation_count INT          NOT NULL DEFAULT 0,
    notes                       TEXT         NOT NULL DEFAULT '',
    created_at_utc              TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at_utc              TIMESTAMPTZ NULL
)");
    }

    private async Task CreateAssets()
    {
        await TryCreate("fleet_tms_asset_types", @"
CREATE TABLE IF NOT EXISTS fleet_tms_asset_types (
    id             BIGINT NOT NULL GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id     BIGINT NOT NULL,
    code           VARCHAR(40)  NOT NULL DEFAULT '',
    name           VARCHAR(120) NOT NULL DEFAULT '',
    description    TEXT         NOT NULL DEFAULT '',
    is_returnable  BOOLEAN      NOT NULL DEFAULT true,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at_utc TIMESTAMPTZ NULL
)");

        await TryCreate("fleet_tms_assets", @"
CREATE TABLE IF NOT EXISTS fleet_tms_assets (
    id               BIGINT NOT NULL GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id       BIGINT NOT NULL,
    asset_type_id    BIGINT NOT NULL DEFAULT 0,
    asset_tag        VARCHAR(80)  NOT NULL DEFAULT '',
    name             VARCHAR(160) NOT NULL DEFAULT '',
    status           VARCHAR(30)  NOT NULL DEFAULT 'Available',
    current_location VARCHAR(160) NOT NULL DEFAULT '',
    condition        VARCHAR(30)  NOT NULL DEFAULT 'Good',
    is_returnable    BOOLEAN      NOT NULL DEFAULT true,
    quantity         NUMERIC(12,2) NOT NULL DEFAULT 1,
    unit_of_measure  VARCHAR(30)  NOT NULL DEFAULT 'Each',
    notes            TEXT         NOT NULL DEFAULT '',
    last_seen_at_utc TIMESTAMPTZ NULL,
    created_at_utc   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at_utc   TIMESTAMPTZ NULL
)");

        await TryCreate("fleet_tms_asset_assignments", @"
CREATE TABLE IF NOT EXISTS fleet_tms_asset_assignments (
    id              BIGINT NOT NULL GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id      BIGINT NOT NULL,
    asset_id        BIGINT NOT NULL,
    shipment_id     BIGINT NULL,
    carrier_id      BIGINT NULL,
    assignee_type   VARCHAR(40)  NOT NULL DEFAULT 'Shipment',
    assignee_name   VARCHAR(255) NOT NULL DEFAULT '',
    quantity        NUMERIC(12,2) NOT NULL DEFAULT 1,
    status          VARCHAR(30)  NOT NULL DEFAULT 'Assigned',
    assigned_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    released_at_utc TIMESTAMPTZ NULL,
    notes           TEXT         NOT NULL DEFAULT ''
)");

        await TryCreate("fleet_tms_asset_events", @"
CREATE TABLE IF NOT EXISTS fleet_tms_asset_events (
    id              BIGINT NOT NULL GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id      BIGINT NOT NULL,
    asset_id        BIGINT NOT NULL,
    event_type      VARCHAR(60)  NOT NULL DEFAULT '',
    quantity        NUMERIC(12,2) NOT NULL DEFAULT 1,
    location        VARCHAR(160) NOT NULL DEFAULT '',
    actor_name      VARCHAR(255) NOT NULL DEFAULT '',
    occurred_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    notes           TEXT         NOT NULL DEFAULT ''
)");

        await TryCreate("fleet_tms_barcode_scan_events", @"
CREATE TABLE IF NOT EXISTS fleet_tms_barcode_scan_events (
    id              BIGINT NOT NULL GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id      BIGINT NOT NULL,
    asset_id        BIGINT NULL,
    shipment_id     BIGINT NULL,
    scanned_value   VARCHAR(255) NOT NULL DEFAULT '',
    scanner_id      VARCHAR(80)  NOT NULL DEFAULT '',
    event_type      VARCHAR(40)  NOT NULL DEFAULT 'Scan',
    status          VARCHAR(30)  NOT NULL DEFAULT 'Captured',
    recorded_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    notes           TEXT         NOT NULL DEFAULT ''
)");

        await TryCreate("fleet_tms_rfid_events", @"
CREATE TABLE IF NOT EXISTS fleet_tms_rfid_events (
    id              BIGINT NOT NULL GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id      BIGINT NOT NULL,
    asset_id        BIGINT NULL,
    shipment_id     BIGINT NULL,
    tag_id          VARCHAR(120) NOT NULL DEFAULT '',
    reader_id       VARCHAR(80)  NOT NULL DEFAULT '',
    event_type      VARCHAR(40)  NOT NULL DEFAULT 'Read',
    status          VARCHAR(30)  NOT NULL DEFAULT 'Captured',
    recorded_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    notes           TEXT         NOT NULL DEFAULT ''
)");
    }

    private async Task CreateReadiness()
    {
        // Global reference data (no company_id) — Saudi/GCC regions.
        await TryCreate("fleet_tms_saudi_regions", @"
CREATE TABLE IF NOT EXISTS fleet_tms_saudi_regions (
    id             BIGINT NOT NULL GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    code           VARCHAR(40)  NOT NULL DEFAULT '',
    name_en        VARCHAR(120) NOT NULL DEFAULT '',
    name_ar        VARCHAR(120) NOT NULL DEFAULT '',
    country_code   VARCHAR(8)   NOT NULL DEFAULT 'SA',
    cities_json    JSONB        NOT NULL DEFAULT '[]',
    sort_order     INT          NOT NULL DEFAULT 0,
    is_gcc_ready   BOOLEAN      NOT NULL DEFAULT true,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()
)");

        await TryCreate("fleet_tms_readiness_documents", @"
CREATE TABLE IF NOT EXISTS fleet_tms_readiness_documents (
    id                            BIGINT NOT NULL GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id                    BIGINT NOT NULL,
    kind                          VARCHAR(40)  NOT NULL DEFAULT 'Compliance',
    subject_type                  VARCHAR(40)  NOT NULL DEFAULT '',
    subject_id                    VARCHAR(80)  NOT NULL DEFAULT '',
    subject_name                  VARCHAR(255) NOT NULL DEFAULT '',
    document_type                 VARCHAR(120) NOT NULL DEFAULT '',
    document_number               VARCHAR(120) NOT NULL DEFAULT '',
    transport_document_no         VARCHAR(120) NOT NULL DEFAULT '',
    permit_no                     VARCHAR(120) NOT NULL DEFAULT '',
    vat_number                    VARCHAR(60)  NOT NULL DEFAULT '',
    commercial_registration_no    VARCHAR(60)  NOT NULL DEFAULT '',
    country_code                  VARCHAR(8)   NOT NULL DEFAULT 'SA',
    national_address_building_no  VARCHAR(40)  NOT NULL DEFAULT '',
    national_address_additional_no VARCHAR(40) NOT NULL DEFAULT '',
    district                      VARCHAR(120) NOT NULL DEFAULT '',
    city                          VARCHAR(120) NOT NULL DEFAULT '',
    region                        VARCHAR(120) NOT NULL DEFAULT '',
    postal_code                   VARCHAR(20)  NOT NULL DEFAULT '',
    document_status               VARCHAR(30)  NOT NULL DEFAULT 'Active',
    expiry_status                 VARCHAR(30)  NOT NULL DEFAULT 'Healthy',
    issue_date                    DATE NULL,
    hijri_expiry_date             DATE NULL,
    gregorian_expiry_date         DATE NULL,
    notes                         TEXT         NOT NULL DEFAULT '',
    created_at_utc                TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at_utc                TIMESTAMPTZ NULL
)");
    }

    private async Task CreateIndexes()
    {
        var indexes = new[]
        {
            ("fleet_tms_temperature_devices",   "idx_ftms_tdev_company",      "company_id, status"),
            ("fleet_tms_temperature_readings",  "idx_ftms_tread_company_ship","company_id, shipment_id"),
            ("fleet_tms_temperature_readings",  "idx_ftms_tread_device",      "company_id, device_id"),
            ("fleet_tms_temperature_alerts",    "idx_ftms_talert_company",    "company_id, status"),
            ("fleet_tms_cold_chain_reports",    "idx_ftms_ccr_company_ship",  "company_id, shipment_id"),
            ("fleet_tms_refrigeration_unit_health", "idx_ftms_ruh_company",   "company_id, status"),
            ("fleet_tms_asset_types",           "idx_ftms_atype_company",     "company_id, code"),
            ("fleet_tms_assets",                "idx_ftms_assets_company",    "company_id, status"),
            ("fleet_tms_assets",                "idx_ftms_assets_tag",        "company_id, asset_tag"),
            ("fleet_tms_asset_assignments",     "idx_ftms_aassign_company",   "company_id, asset_id"),
            ("fleet_tms_asset_events",          "idx_ftms_aevent_company",    "company_id, asset_id"),
            ("fleet_tms_barcode_scan_events",   "idx_ftms_barcode_company",   "company_id, recorded_at_utc"),
            ("fleet_tms_rfid_events",           "idx_ftms_rfid_company",      "company_id, recorded_at_utc"),
            ("fleet_tms_saudi_regions",         "idx_ftms_saudi_sort",        "sort_order"),
            ("fleet_tms_readiness_documents",   "idx_ftms_readiness_company", "company_id, expiry_status"),
        };

        foreach (var (table, name, cols) in indexes)
        {
            try { await db.ExecuteAsync($"CREATE INDEX IF NOT EXISTS \"{name}\" ON \"{table}\" ({cols})"); }
            catch (Exception ex) { log.LogWarning(ex, "[FleetTmsColdChainSchema] Index {Name} failed", name); }
        }
    }

    private async Task TryCreate(string table, string ddl)
    {
        try { await db.ExecuteAsync(ddl); }
        catch (Exception ex) { log.LogWarning(ex, "[FleetTmsColdChainSchema] Create {Table} failed", table); }
    }
}
