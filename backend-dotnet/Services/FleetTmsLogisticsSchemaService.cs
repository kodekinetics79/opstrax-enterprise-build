using Microsoft.Extensions.Logging;
using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

// Fleet TMS (PR3) — schema bootstrap for the last-mile logistics workspace
// (dispatch orders, delivery routes, last-mile stops), ported from the Zayra
// opstrax-codex-backup branch. Net-new, `fleet_tms_`-prefixed, company-scoped,
// idempotent. Business-key links are protected by uniqueness and the API performs
// branch-aware, transactional validation before every workflow transition.
public sealed class FleetTmsLogisticsSchemaService(Database db, ILogger<FleetTmsLogisticsSchemaService> log)
{
    public async Task EnsureAsync()
    {
        await CreateTables();
        await UpgradeExistingTables();
        await CreateIndexes();
        await CreateConstraints();
    }

    private async Task CreateTables()
    {
        await TryCreate("fleet_tms_dispatch_orders", @"
CREATE TABLE IF NOT EXISTS fleet_tms_dispatch_orders (
    id                BIGINT NOT NULL GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id        BIGINT NOT NULL,
    branch_id         BIGINT NULL,
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
    branch_id          BIGINT NULL,
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
    notes              TEXT         NOT NULL DEFAULT '',
    last_progress_key  VARCHAR(80) NULL
)");

        await TryCreate("fleet_tms_last_mile_stops", @"
CREATE TABLE IF NOT EXISTS fleet_tms_last_mile_stops (
    id                                   BIGINT NOT NULL GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id                           BIGINT NOT NULL,
    branch_id                            BIGINT NULL,
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
    updated_at_utc                       TIMESTAMPTZ NULL,
    last_action_key                      VARCHAR(80) NULL,
    last_action_type                     VARCHAR(30) NULL
)");
    }

    private async Task UpgradeExistingTables()
    {
        var statements = new[]
        {
            "ALTER TABLE fleet_tms_dispatch_orders ADD COLUMN IF NOT EXISTS branch_id BIGINT NULL",
            "ALTER TABLE fleet_tms_delivery_routes ADD COLUMN IF NOT EXISTS branch_id BIGINT NULL",
            "ALTER TABLE fleet_tms_delivery_routes ADD COLUMN IF NOT EXISTS last_progress_key VARCHAR(80) NULL",
            "ALTER TABLE fleet_tms_last_mile_stops ADD COLUMN IF NOT EXISTS branch_id BIGINT NULL",
            "ALTER TABLE fleet_tms_last_mile_stops ADD COLUMN IF NOT EXISTS last_action_key VARCHAR(80) NULL",
            "ALTER TABLE fleet_tms_last_mile_stops ADD COLUMN IF NOT EXISTS last_action_type VARCHAR(30) NULL",
            "ALTER TABLE fleet_tms_last_mile_stops ADD COLUMN IF NOT EXISTS proof_evidence_ref TEXT NULL",
        };
        foreach (var statement in statements)
            await TryExecute("upgrade", statement);
    }

    private async Task CreateIndexes()
    {
        var indexes = new[]
        {
            ("fleet_tms_dispatch_orders", "idx_ftms_dorders_company_status", "company_id, status"),
            ("fleet_tms_dispatch_orders", "idx_ftms_dorders_number",         "company_id, order_number"),
            ("fleet_tms_dispatch_orders", "idx_ftms_dorders_branch_status",  "company_id, branch_id, status"),
            ("fleet_tms_delivery_routes", "idx_ftms_droutes_company",        "company_id, status"),
            ("fleet_tms_delivery_routes", "idx_ftms_droutes_code",           "company_id, route_code"),
            ("fleet_tms_delivery_routes", "idx_ftms_droutes_branch_status",  "company_id, branch_id, status"),
            ("fleet_tms_last_mile_stops", "idx_ftms_lmstops_company_status", "company_id, status"),
            ("fleet_tms_last_mile_stops", "idx_ftms_lmstops_route",          "company_id, route_code"),
            ("fleet_tms_last_mile_stops", "idx_ftms_lmstops_order",          "company_id, order_number"),
            ("fleet_tms_last_mile_stops", "idx_ftms_lmstops_branch_status",  "company_id, branch_id, status"),
        };
        foreach (var (table, name, cols) in indexes)
        {
            try { await db.ExecuteAsync($"CREATE INDEX IF NOT EXISTS \"{name}\" ON \"{table}\" ({cols})"); }
            catch (Exception ex) { log.LogWarning(ex, "[FleetTmsLogisticsSchema] Index {Name} failed", name); }
        }

        await TryExecute("uq_ftms_dorders_company_number", "CREATE UNIQUE INDEX IF NOT EXISTS uq_ftms_dorders_company_number ON fleet_tms_dispatch_orders(company_id, order_number)");
        await TryExecute("uq_ftms_droutes_company_code", "CREATE UNIQUE INDEX IF NOT EXISTS uq_ftms_droutes_company_code ON fleet_tms_delivery_routes(company_id, route_code)");
        await TryExecute("uq_ftms_lmstops_company_order", "CREATE UNIQUE INDEX IF NOT EXISTS uq_ftms_lmstops_company_order ON fleet_tms_last_mile_stops(company_id, order_number)");
        await TryExecute("uq_ftms_route_progress_key", "CREATE UNIQUE INDEX IF NOT EXISTS uq_ftms_route_progress_key ON fleet_tms_delivery_routes(company_id, last_progress_key) WHERE last_progress_key IS NOT NULL");
        await TryExecute("uq_ftms_stop_action_key", "CREATE UNIQUE INDEX IF NOT EXISTS uq_ftms_stop_action_key ON fleet_tms_last_mile_stops(company_id, last_action_key) WHERE last_action_key IS NOT NULL");
        await TryExecute("uq_job_charges_last_mile", "DO $optional_job_charges$ BEGIN IF to_regclass('public.job_charges') IS NOT NULL THEN CREATE UNIQUE INDEX IF NOT EXISTS uq_job_charges_last_mile ON job_charges(company_id, job_id, charge_code) WHERE charge_code = 'LASTMILE'; END IF; END $optional_job_charges$");
        await TryExecute("uq_branches_company_id_id", "CREATE UNIQUE INDEX IF NOT EXISTS uq_branches_company_id_id ON branches(company_id, id)");
    }

    private async Task CreateConstraints()
    {
        var statements = new[]
        {
            "DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='ck_ftms_dorders_status') THEN ALTER TABLE fleet_tms_dispatch_orders ADD CONSTRAINT ck_ftms_dorders_status CHECK (status IN ('Queued','Dispatched','InTransit','Exception','Delivered','Returned')) NOT VALID; END IF; END $$",
            "DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='ck_ftms_dorders_values') THEN ALTER TABLE fleet_tms_dispatch_orders ADD CONSTRAINT ck_ftms_dorders_values CHECK (btrim(order_number) <> '' AND item_count >= 0 AND order_value >= 0) NOT VALID; END IF; END $$",
            "DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='ck_ftms_droutes_status') THEN ALTER TABLE fleet_tms_delivery_routes ADD CONSTRAINT ck_ftms_droutes_status CHECK (status IN ('Planned','Ready','Active','Delayed','Closed','Completed')) NOT VALID; END IF; END $$",
            "DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='ck_ftms_droutes_values') THEN ALTER TABLE fleet_tms_delivery_routes ADD CONSTRAINT ck_ftms_droutes_values CHECK (btrim(route_code) <> '' AND planned_stops >= 0 AND completed_stops >= 0 AND completed_stops <= planned_stops AND distance_km >= 0 AND completion_percent BETWEEN 0 AND 100) NOT VALID; END IF; END $$",
            "DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='ck_ftms_lmstops_status') THEN ALTER TABLE fleet_tms_last_mile_stops ADD CONSTRAINT ck_ftms_lmstops_status CHECK (status IN ('OutForDelivery','Attempted','Failed','Rescheduled','Delivered')) NOT VALID; END IF; END $$",
            "DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='ck_ftms_lmstops_values') THEN ALTER TABLE fleet_tms_last_mile_stops ADD CONSTRAINT ck_ftms_lmstops_values CHECK (btrim(order_number) <> '' AND btrim(route_code) <> '' AND attempt_count >= 0) NOT VALID; END IF; END $$",
            "DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='fk_ftms_dorders_company') THEN ALTER TABLE fleet_tms_dispatch_orders ADD CONSTRAINT fk_ftms_dorders_company FOREIGN KEY(company_id) REFERENCES companies(id) NOT VALID; END IF; END $$",
            "DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='fk_ftms_droutes_company') THEN ALTER TABLE fleet_tms_delivery_routes ADD CONSTRAINT fk_ftms_droutes_company FOREIGN KEY(company_id) REFERENCES companies(id) NOT VALID; END IF; END $$",
            "DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='fk_ftms_lmstops_company') THEN ALTER TABLE fleet_tms_last_mile_stops ADD CONSTRAINT fk_ftms_lmstops_company FOREIGN KEY(company_id) REFERENCES companies(id) NOT VALID; END IF; END $$",
            "DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='fk_ftms_dorders_branch') THEN ALTER TABLE fleet_tms_dispatch_orders ADD CONSTRAINT fk_ftms_dorders_branch FOREIGN KEY(company_id,branch_id) REFERENCES branches(company_id,id) NOT VALID; END IF; END $$",
            "DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='fk_ftms_droutes_branch') THEN ALTER TABLE fleet_tms_delivery_routes ADD CONSTRAINT fk_ftms_droutes_branch FOREIGN KEY(company_id,branch_id) REFERENCES branches(company_id,id) NOT VALID; END IF; END $$",
            "DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='fk_ftms_lmstops_branch') THEN ALTER TABLE fleet_tms_last_mile_stops ADD CONSTRAINT fk_ftms_lmstops_branch FOREIGN KEY(company_id,branch_id) REFERENCES branches(company_id,id) NOT VALID; END IF; END $$",
            "DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='fk_ftms_lmstops_order') THEN ALTER TABLE fleet_tms_last_mile_stops ADD CONSTRAINT fk_ftms_lmstops_order FOREIGN KEY(company_id,order_number) REFERENCES fleet_tms_dispatch_orders(company_id,order_number) NOT VALID; END IF; END $$",
            "DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='fk_ftms_lmstops_route') THEN ALTER TABLE fleet_tms_last_mile_stops ADD CONSTRAINT fk_ftms_lmstops_route FOREIGN KEY(company_id,route_code) REFERENCES fleet_tms_delivery_routes(company_id,route_code) NOT VALID; END IF; END $$",
        };
        foreach (var statement in statements)
            await TryExecute("constraint", statement);
    }

    private async Task TryCreate(string table, string ddl)
    {
        try { await db.ExecuteAsync(ddl); }
        catch (Exception ex) { log.LogWarning(ex, "[FleetTmsLogisticsSchema] Create {Table} failed", table); }
    }

    private async Task TryExecute(string name, string sql)
    {
        try { await db.ExecuteAsync(sql); }
        catch (Exception ex) { log.LogWarning(ex, "[FleetTmsLogisticsSchema] {Name} failed", name); }
    }
}
