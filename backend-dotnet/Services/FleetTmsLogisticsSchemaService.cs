using Microsoft.Extensions.Logging;
using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

// Fleet TMS (PR3) — schema bootstrap for the last-mile logistics workspace
// (dispatch orders, delivery routes, last-mile stops), ported from the Zayra
// opstrax-codex-backup branch. Net-new, `fleet_tms_`-prefixed, company-scoped,
// idempotent. Orders/routes/stops are linked denormally by order_number/route_code
// (matching the source model), so there are no cross-table FK constraints.
public sealed class FleetTmsLogisticsSchemaService(Database db, ILogger<FleetTmsLogisticsSchemaService> log)
{
    public async Task EnsureAsync()
    {
        await CreateTables();
        await CreateIndexes();
    }

    private async Task CreateTables()
    {
        await TryCreate("fleet_tms_dispatch_orders", @"
CREATE TABLE IF NOT EXISTS fleet_tms_dispatch_orders (
    id                BIGINT NOT NULL GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id        BIGINT NOT NULL,
    order_number      VARCHAR(60)  NOT NULL DEFAULT '',
    customer_name     VARCHAR(255) NOT NULL DEFAULT '',
    customer_segment  VARCHAR(80)  NOT NULL DEFAULT 'Retail',
    sales_channel     VARCHAR(80)  NOT NULL DEFAULT 'Portal',
    city              VARCHAR(120) NOT NULL DEFAULT '',
    area              VARCHAR(120) NOT NULL DEFAULT '',
    status            VARCHAR(30)  NOT NULL DEFAULT 'Queued',
    priority          VARCHAR(30)  NOT NULL DEFAULT 'Normal',
    item_count        INT          NOT NULL DEFAULT 1,
    order_value       NUMERIC(14,2) NOT NULL DEFAULT 0,
    route_code        VARCHAR(60)  NOT NULL DEFAULT '',
    driver_name       VARCHAR(255) NOT NULL DEFAULT '',
    vehicle_number    VARCHAR(60)  NOT NULL DEFAULT '',
    dispatch_notes    TEXT         NOT NULL DEFAULT '',
    created_at_utc    TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    promised_at_utc   TIMESTAMPTZ NULL,
    dispatched_at_utc TIMESTAMPTZ NULL,
    delivered_at_utc  TIMESTAMPTZ NULL,
    updated_at_utc    TIMESTAMPTZ NULL
)");

        await TryCreate("fleet_tms_delivery_routes", @"
CREATE TABLE IF NOT EXISTS fleet_tms_delivery_routes (
    id                 BIGINT NOT NULL GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id         BIGINT NOT NULL,
    route_code         VARCHAR(60)  NOT NULL DEFAULT '',
    hub                VARCHAR(120) NOT NULL DEFAULT '',
    territory          VARCHAR(120) NOT NULL DEFAULT '',
    driver_name        VARCHAR(255) NOT NULL DEFAULT '',
    vehicle_number     VARCHAR(60)  NOT NULL DEFAULT '',
    status             VARCHAR(30)  NOT NULL DEFAULT 'Planned',
    planned_stops      INT          NOT NULL DEFAULT 0,
    completed_stops    INT          NOT NULL DEFAULT 0,
    distance_km        NUMERIC(10,2) NOT NULL DEFAULT 0,
    completion_percent NUMERIC(6,2)  NOT NULL DEFAULT 0,
    current_stop       VARCHAR(255) NOT NULL DEFAULT '',
    next_stop          VARCHAR(255) NOT NULL DEFAULT '',
    planned_for_date   DATE         NOT NULL DEFAULT CURRENT_DATE,
    departure_time_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    eta_complete_utc   TIMESTAMPTZ NULL,
    notes              TEXT         NOT NULL DEFAULT ''
)");

        await TryCreate("fleet_tms_last_mile_stops", @"
CREATE TABLE IF NOT EXISTS fleet_tms_last_mile_stops (
    id                                   BIGINT NOT NULL GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id                           BIGINT NOT NULL,
    order_number                         VARCHAR(60)  NOT NULL DEFAULT '',
    route_code                           VARCHAR(60)  NOT NULL DEFAULT '',
    customer_name                        VARCHAR(255) NOT NULL DEFAULT '',
    address_line                         VARCHAR(255) NOT NULL DEFAULT '',
    city                                 VARCHAR(120) NOT NULL DEFAULT '',
    region                               VARCHAR(120) NOT NULL DEFAULT '',
    postal_code                          VARCHAR(20)  NOT NULL DEFAULT '',
    country                              VARCHAR(80)  NOT NULL DEFAULT '',
    saudi_national_address_building_no   VARCHAR(40)  NOT NULL DEFAULT '',
    saudi_national_address_additional_no VARCHAR(40)  NOT NULL DEFAULT '',
    saudi_national_address_district      VARCHAR(120) NOT NULL DEFAULT '',
    status                               VARCHAR(30)  NOT NULL DEFAULT 'OutForDelivery',
    proof_status                         VARCHAR(30)  NOT NULL DEFAULT 'None',
    recipient_name                       VARCHAR(255) NOT NULL DEFAULT '',
    attempt_count                        INT          NOT NULL DEFAULT 0,
    rider_name                           VARCHAR(255) NOT NULL DEFAULT '',
    time_window                          VARCHAR(80)  NOT NULL DEFAULT '',
    eta_utc                              TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    delivered_at_utc                     TIMESTAMPTZ NULL,
    exception_reason                     TEXT         NOT NULL DEFAULT '',
    created_at_utc                       TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at_utc                       TIMESTAMPTZ NULL
)");
    }

    private async Task CreateIndexes()
    {
        var indexes = new[]
        {
            ("fleet_tms_dispatch_orders", "idx_ftms_dorders_company_status", "company_id, status"),
            ("fleet_tms_dispatch_orders", "idx_ftms_dorders_number",         "company_id, order_number"),
            ("fleet_tms_delivery_routes", "idx_ftms_droutes_company",        "company_id, status"),
            ("fleet_tms_delivery_routes", "idx_ftms_droutes_code",           "company_id, route_code"),
            ("fleet_tms_last_mile_stops", "idx_ftms_lmstops_company_status", "company_id, status"),
            ("fleet_tms_last_mile_stops", "idx_ftms_lmstops_route",          "company_id, route_code"),
            ("fleet_tms_last_mile_stops", "idx_ftms_lmstops_order",          "company_id, order_number"),
        };
        foreach (var (table, name, cols) in indexes)
        {
            try { await db.ExecuteAsync($"CREATE INDEX IF NOT EXISTS \"{name}\" ON \"{table}\" ({cols})"); }
            catch (Exception ex) { log.LogWarning(ex, "[FleetTmsLogisticsSchema] Index {Name} failed", name); }
        }
    }

    private async Task TryCreate(string table, string ddl)
    {
        try { await db.ExecuteAsync(ddl); }
        catch (Exception ex) { log.LogWarning(ex, "[FleetTmsLogisticsSchema] Create {Table} failed", table); }
    }
}
