using Microsoft.Extensions.Logging;
using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

// Fleet TMS (PR1) — schema bootstrap for the shipment lifecycle, POD workflow and
// public customer tracking ported from the Zayra opstrax-codex-backup branch.
//
// DESIGN: every table is net-new and prefixed `fleet_tms_` to guarantee zero
// collision with the existing OpsTrax schema. Tenant ownership uses the repo's
// `company_id BIGINT` convention (NOT the Zayra `tenant_id` Guid). All DDL is
// idempotent (CREATE TABLE/INDEX IF NOT EXISTS) because this runs at startup.
public sealed class FleetTmsSchemaService(Database db, ILogger<FleetTmsSchemaService> log)
{
    public async Task EnsureAsync()
    {
        await CreateTables();
        await CreateIndexes();
    }

    private async Task CreateTables()
    {
        await TryCreate("fleet_tms_shipments", @"
CREATE TABLE IF NOT EXISTS fleet_tms_shipments (
    id                                       BIGINT NOT NULL GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id                               BIGINT NOT NULL,
    shipment_number                          VARCHAR(60)  NOT NULL,
    customer_name                            VARCHAR(255) NOT NULL DEFAULT '',
    customer_segment                         VARCHAR(80)  NOT NULL DEFAULT '',
    origin                                   VARCHAR(255) NOT NULL DEFAULT '',
    destination                              VARCHAR(255) NOT NULL DEFAULT '',
    city                                     VARCHAR(120) NOT NULL DEFAULT '',
    status                                   VARCHAR(40)  NOT NULL DEFAULT 'Booked',
    priority                                 VARCHAR(30)  NOT NULL DEFAULT 'Normal',
    mode                                     VARCHAR(30)  NOT NULL DEFAULT 'Road',
    piece_count                              INT          NOT NULL DEFAULT 0,
    weight_kg                                NUMERIC(14,2) NOT NULL DEFAULT 0,
    volume_cbm                               NUMERIC(14,2) NOT NULL DEFAULT 0,
    declared_value                           NUMERIC(14,2) NOT NULL DEFAULT 0,
    carrier_name                             VARCHAR(255) NOT NULL DEFAULT '',
    customer_vat_number                      VARCHAR(60)  NOT NULL DEFAULT '',
    customer_commercial_registration_no      VARCHAR(60)  NOT NULL DEFAULT '',
    customer_national_address_building_no     VARCHAR(40)  NOT NULL DEFAULT '',
    customer_national_address_additional_no   VARCHAR(40)  NOT NULL DEFAULT '',
    customer_national_address_district        VARCHAR(120) NOT NULL DEFAULT '',
    customer_national_address_city            VARCHAR(120) NOT NULL DEFAULT '',
    customer_national_address_region          VARCHAR(120) NOT NULL DEFAULT '',
    customer_national_address_postal_code     VARCHAR(20)  NOT NULL DEFAULT '',
    customer_national_address_country         VARCHAR(80)  NOT NULL DEFAULT '',
    driver_name                              VARCHAR(255) NOT NULL DEFAULT '',
    vehicle_number                           VARCHAR(60)  NOT NULL DEFAULT '',
    route_code                               VARCHAR(60)  NOT NULL DEFAULT '',
    pod_status                               VARCHAR(30)  NOT NULL DEFAULT 'Pending',
    temperature_range                        VARCHAR(60)  NOT NULL DEFAULT '',
    notes                                    TEXT         NOT NULL DEFAULT '',
    is_invoice_ready                         BOOLEAN      NOT NULL DEFAULT false,
    invoice_ready_at_utc                     TIMESTAMPTZ NULL,
    invoice_readiness_notes                  TEXT         NOT NULL DEFAULT '',
    pickup_scheduled_at_utc                  TIMESTAMPTZ NULL,
    picked_up_at_utc                         TIMESTAMPTZ NULL,
    delivered_at_utc                         TIMESTAMPTZ NULL,
    created_at_utc                           TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at_utc                           TIMESTAMPTZ NULL
)");

        await TryCreate("fleet_tms_shipment_stops", @"
CREATE TABLE IF NOT EXISTS fleet_tms_shipment_stops (
    id                                    BIGINT NOT NULL GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id                            BIGINT NOT NULL,
    shipment_id                           BIGINT NOT NULL,
    stop_type                             VARCHAR(30)  NOT NULL DEFAULT 'Pickup',
    sequence_no                           INT          NOT NULL DEFAULT 1,
    location_name                         VARCHAR(255) NOT NULL DEFAULT '',
    contact_name                          VARCHAR(255) NOT NULL DEFAULT '',
    contact_phone                         VARCHAR(60)  NOT NULL DEFAULT '',
    address_line1                         VARCHAR(255) NOT NULL DEFAULT '',
    address_line2                         VARCHAR(255) NOT NULL DEFAULT '',
    city                                  VARCHAR(120) NOT NULL DEFAULT '',
    region                                VARCHAR(120) NOT NULL DEFAULT '',
    postal_code                           VARCHAR(20)  NOT NULL DEFAULT '',
    country                               VARCHAR(80)  NOT NULL DEFAULT '',
    saudi_national_address_building_no    VARCHAR(40)  NOT NULL DEFAULT '',
    saudi_national_address_additional_no  VARCHAR(40)  NOT NULL DEFAULT '',
    saudi_national_address_district       VARCHAR(120) NOT NULL DEFAULT '',
    latitude                              NUMERIC(10,6) NULL,
    longitude                             NUMERIC(10,6) NULL,
    planned_arrival_at                    TIMESTAMPTZ NULL,
    actual_arrival_at                     TIMESTAMPTZ NULL,
    completed_at                          TIMESTAMPTZ NULL,
    status                                VARCHAR(30)  NOT NULL DEFAULT 'Planned',
    notes                                 TEXT         NOT NULL DEFAULT '',
    created_at                            TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at                            TIMESTAMPTZ NOT NULL DEFAULT NOW()
)");

        await TryCreate("fleet_tms_pods", @"
CREATE TABLE IF NOT EXISTS fleet_tms_pods (
    id                     BIGINT NOT NULL GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id             BIGINT NOT NULL,
    shipment_id            BIGINT NOT NULL,
    stop_id                BIGINT NOT NULL,
    captured_by_user_id    BIGINT NULL,
    driver_id              BIGINT NULL,
    vehicle_id             BIGINT NULL,
    recipient_name         VARCHAR(255) NOT NULL DEFAULT '',
    recipient_phone        VARCHAR(60)  NOT NULL DEFAULT '',
    signature_url          TEXT         NOT NULL DEFAULT '',
    photo_url              TEXT         NOT NULL DEFAULT '',
    document_url           TEXT         NOT NULL DEFAULT '',
    notes                  TEXT         NOT NULL DEFAULT '',
    delivery_condition     VARCHAR(40)  NOT NULL DEFAULT 'Good',
    captured_latitude      NUMERIC(10,6) NULL,
    captured_longitude     NUMERIC(10,6) NULL,
    captured_at            TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    verified_at            TIMESTAMPTZ NULL,
    verified_by_user_id    BIGINT NULL,
    status                 VARCHAR(30)  NOT NULL DEFAULT 'Draft',
    created_at             TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at             TIMESTAMPTZ NOT NULL DEFAULT NOW()
)");

        await TryCreate("fleet_tms_tracking_links", @"
CREATE TABLE IF NOT EXISTS fleet_tms_tracking_links (
    id              BIGINT NOT NULL GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id      BIGINT NOT NULL,
    shipment_id     BIGINT NOT NULL,
    token           VARCHAR(120) NOT NULL,
    expires_at_utc  TIMESTAMPTZ NOT NULL DEFAULT (NOW() + INTERVAL '7 days'),
    is_revoked      BOOLEAN      NOT NULL DEFAULT false,
    shared_by       VARCHAR(255) NOT NULL DEFAULT '',
    created_at_utc  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    revoked_at_utc  TIMESTAMPTZ NULL,
    updated_at_utc  TIMESTAMPTZ NULL
)");

        await TryCreate("fleet_tms_shipment_events", @"
CREATE TABLE IF NOT EXISTS fleet_tms_shipment_events (
    id              BIGINT NOT NULL GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id      BIGINT NOT NULL,
    shipment_id     BIGINT NOT NULL,
    event_type      VARCHAR(60)  NOT NULL DEFAULT '',
    message         TEXT         NOT NULL DEFAULT '',
    actor_name      VARCHAR(255) NOT NULL DEFAULT 'system',
    occurred_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    visibility      VARCHAR(20)  NOT NULL DEFAULT 'Private',
    created_at_utc  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at_utc  TIMESTAMPTZ NULL
)");

        await TryCreate("fleet_tms_driver_tasks", @"
CREATE TABLE IF NOT EXISTS fleet_tms_driver_tasks (
    id               BIGINT NOT NULL GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id       BIGINT NOT NULL,
    shipment_id      BIGINT NOT NULL,
    stop_id          BIGINT NULL,
    task_type        VARCHAR(30)  NOT NULL DEFAULT 'Pickup',
    title            VARCHAR(255) NOT NULL DEFAULT '',
    description      TEXT         NOT NULL DEFAULT '',
    status           VARCHAR(30)  NOT NULL DEFAULT 'Open',
    driver_name      VARCHAR(255) NOT NULL DEFAULT '',
    vehicle_number   VARCHAR(60)  NOT NULL DEFAULT '',
    due_at_utc       TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    completed_at_utc TIMESTAMPTZ NULL,
    notes            TEXT         NOT NULL DEFAULT '',
    created_at_utc   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at_utc   TIMESTAMPTZ NULL
)");

        await TryCreate("fleet_tms_vehicles", @"
CREATE TABLE IF NOT EXISTS fleet_tms_vehicles (
    id                  BIGINT NOT NULL GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id          BIGINT NOT NULL,
    vehicle_number      VARCHAR(60)  NOT NULL DEFAULT '',
    plate_number        VARCHAR(60)  NOT NULL DEFAULT '',
    type                VARCHAR(60)  NOT NULL DEFAULT '',
    status              VARCHAR(30)  NOT NULL DEFAULT 'Available',
    driver_name         VARCHAR(255) NOT NULL DEFAULT '',
    capacity_kg         NUMERIC(14,2) NOT NULL DEFAULT 0,
    capacity_cbm        NUMERIC(14,2) NOT NULL DEFAULT 0,
    current_load_kg     NUMERIC(14,2) NOT NULL DEFAULT 0,
    fuel_level_percent  NUMERIC(6,2)  NOT NULL DEFAULT 0,
    odometer_km         NUMERIC(14,2) NOT NULL DEFAULT 0,
    health_status       VARCHAR(30)  NOT NULL DEFAULT 'Healthy',
    is_refrigerated     BOOLEAN      NOT NULL DEFAULT false,
    temperature_celsius NUMERIC(6,2) NULL,
    last_known_location VARCHAR(255) NOT NULL DEFAULT '',
    last_ping_at_utc    TIMESTAMPTZ NULL,
    last_service_at_utc TIMESTAMPTZ NULL,
    next_service_at_utc TIMESTAMPTZ NULL,
    notes               TEXT         NOT NULL DEFAULT '',
    created_at_utc      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at_utc      TIMESTAMPTZ NULL
)");

        await TryCreate("fleet_tms_tracking_points", @"
CREATE TABLE IF NOT EXISTS fleet_tms_tracking_points (
    id                   BIGINT NOT NULL GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id           BIGINT NOT NULL,
    shipment_number      VARCHAR(60)  NOT NULL DEFAULT '',
    vehicle_number       VARCHAR(60)  NOT NULL DEFAULT '',
    location_label       VARCHAR(255) NOT NULL DEFAULT '',
    status               VARCHAR(30)  NOT NULL DEFAULT 'InTransit',
    geofence_name        VARCHAR(120) NOT NULL DEFAULT '',
    alert_type           VARCHAR(60)  NOT NULL DEFAULT '',
    latitude             NUMERIC(10,6) NOT NULL DEFAULT 0,
    longitude            NUMERIC(10,6) NOT NULL DEFAULT 0,
    speed_kph            NUMERIC(8,2)  NOT NULL DEFAULT 0,
    recorded_at_utc      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    estimated_arrival_utc TIMESTAMPTZ NULL,
    notes                TEXT         NOT NULL DEFAULT ''
)");

        await TryCreate("fleet_tms_maintenance_tickets", @"
CREATE TABLE IF NOT EXISTS fleet_tms_maintenance_tickets (
    id                BIGINT NOT NULL GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id        BIGINT NOT NULL,
    work_order_number VARCHAR(60)  NOT NULL DEFAULT '',
    vehicle_number    VARCHAR(60)  NOT NULL DEFAULT '',
    type              VARCHAR(60)  NOT NULL DEFAULT '',
    status            VARCHAR(30)  NOT NULL DEFAULT 'Open',
    priority          VARCHAR(30)  NOT NULL DEFAULT 'Normal',
    vendor_name       VARCHAR(255) NOT NULL DEFAULT '',
    description       TEXT         NOT NULL DEFAULT '',
    estimated_cost    NUMERIC(14,2) NOT NULL DEFAULT 0,
    actual_cost       NUMERIC(14,2) NOT NULL DEFAULT 0,
    downtime_hours    NUMERIC(10,2) NOT NULL DEFAULT 0,
    opened_at_utc     TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    due_at_utc        TIMESTAMPTZ NULL,
    closed_at_utc     TIMESTAMPTZ NULL,
    notes             TEXT         NOT NULL DEFAULT '',
    updated_at_utc    TIMESTAMPTZ NULL
)");

        await TryCreate("fleet_tms_fuel_events", @"
CREATE TABLE IF NOT EXISTS fleet_tms_fuel_events (
    id               BIGINT NOT NULL GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id       BIGINT NOT NULL,
    vehicle_number   VARCHAR(60)  NOT NULL DEFAULT '',
    fuel_card_number VARCHAR(60)  NOT NULL DEFAULT '',
    station_name     VARCHAR(255) NOT NULL DEFAULT '',
    city             VARCHAR(120) NOT NULL DEFAULT '',
    event_type       VARCHAR(30)  NOT NULL DEFAULT 'Fuel',
    anomaly_flag     BOOLEAN      NOT NULL DEFAULT false,
    liters           NUMERIC(12,2) NOT NULL DEFAULT 0,
    cost             NUMERIC(14,2) NOT NULL DEFAULT 0,
    odometer_km      NUMERIC(14,2) NOT NULL DEFAULT 0,
    notes            TEXT         NOT NULL DEFAULT '',
    recorded_at_utc  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at_utc   TIMESTAMPTZ NULL
)");
    }

    private async Task CreateIndexes()
    {
        var indexes = new[]
        {
            ("fleet_tms_shipments",            "idx_ftms_ship_company_status", "company_id, status"),
            ("fleet_tms_shipments",            "idx_ftms_ship_company_number", "company_id, shipment_number"),
            ("fleet_tms_shipment_stops",       "idx_ftms_stops_company_ship",  "company_id, shipment_id"),
            ("fleet_tms_pods",                 "idx_ftms_pods_company_ship",   "company_id, shipment_id"),
            ("fleet_tms_pods",                 "idx_ftms_pods_status",         "company_id, status"),
            ("fleet_tms_tracking_links",       "idx_ftms_links_token",         "token"),
            ("fleet_tms_tracking_links",       "idx_ftms_links_company_ship",  "company_id, shipment_id"),
            ("fleet_tms_shipment_events",      "idx_ftms_events_company_ship", "company_id, shipment_id"),
            ("fleet_tms_driver_tasks",         "idx_ftms_tasks_company",       "company_id, status"),
            ("fleet_tms_vehicles",             "idx_ftms_vehicles_company",    "company_id, status"),
            ("fleet_tms_tracking_points",      "idx_ftms_track_company",       "company_id, recorded_at_utc"),
            ("fleet_tms_maintenance_tickets",  "idx_ftms_maint_company",       "company_id, status"),
            ("fleet_tms_fuel_events",          "idx_ftms_fuel_company",        "company_id, anomaly_flag"),
        };

        foreach (var (table, name, cols) in indexes)
        {
            try
            {
                await db.ExecuteAsync($"CREATE INDEX IF NOT EXISTS \"{name}\" ON \"{table}\" ({cols})");
            }
            catch (Exception ex) { log.LogWarning(ex, "[FleetTmsSchema] Index {Name} failed", name); }
        }
    }

    private async Task TryCreate(string table, string ddl)
    {
        try { await db.ExecuteAsync(ddl); }
        catch (Exception ex) { log.LogWarning(ex, "[FleetTmsSchema] Create {Table} failed", table); }
    }
}
