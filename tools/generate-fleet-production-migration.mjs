#!/usr/bin/env node

// Generates the owner-run Fleet schema migration from the same SQL literals used
// by the runtime schema services. Keeping this mechanical avoids a second,
// hand-maintained definition drifting away from the nine Fleet route contracts.

import fs from "node:fs";
import path from "node:path";

const root = path.resolve(import.meta.dirname, "..");
const output = path.join(root, "database/migrations/2026_07_30_stage50_fleet_production_contract.sql");
const sources = [
  "backend-dotnet/Services/FleetTmsSchemaService.cs",
  "backend-dotnet/Services/FleetTmsColdChainSchemaService.cs",
  "backend-dotnet/Services/FleetTmsColdChainFoundationSchemaService.cs",
  "backend-dotnet/Services/FleetTmsLogisticsSchemaService.cs",
  "backend-dotnet/Services/MarketPackSchemaService.cs",
].map((file) => path.join(root, file));

function literals(source) {
  const values = [];
  // Scan the alternatives in source order. Independent regex passes can start a
  // regular-string match at the closing quote of a verbatim literal, causing all
  // following literals to alternate between included and skipped as source moves.
  // Interpolated strings are deliberately excluded: generated owner migrations
  // must contain only complete, parameter-free SQL literals.
  const pattern = /\$+"""[\s\S]*?"""|(?:\$@|@\$)"(?:[^"]|"")*"|\$"(?:\\.|[^"\\])*"|"""([\s\S]*?)"""|@"((?:[^"]|"")*)"|"((?:\\.|[^"\\])*)"/g;
  for (const match of source.matchAll(pattern)) {
    if (match[1] === undefined && match[2] === undefined && match[3] === undefined) {
      continue;
    }
    let value;
    if (match[1] !== undefined) {
      value = match[1];
    } else if (match[2] !== undefined) {
      value = match[2].replaceAll('""', '"');
    } else {
      try { value = JSON.parse(`"${match[3]}"`); } catch { continue; }
    }
    values.push(value.trim());
  }
  return values;
}

const sql = sources.flatMap((file) => literals(fs.readFileSync(file, "utf8")))
  .filter((value) => /^(CREATE TABLE|ALTER TABLE|CREATE (?:UNIQUE )?INDEX|DROP INDEX|DO\s+\$)/i.test(value))
  .filter((value) => !/@[A-Za-z]/.test(value)); // parameterized reference-data helpers are emitted below.

// Some schema-service literals create a migration-audit table and then perform
// data backfills in the same batch. Run those only after every pure table create,
// otherwise their UPDATEs can reference a later table that does not yet exist.
const isPostCreateBatch = (value) => /^CREATE TABLE/i.test(value) && /\n\s*(?:UPDATE\b|INSERT\b|DO\s+\$)/i.test(value);
const creates = sql.filter((value) => /^CREATE TABLE/i.test(value) && !isPostCreateBatch(value));
const postCreateBatches = sql.filter(isPostCreateBatch);
const mutations = sql.filter((value) => /^(ALTER TABLE|DROP INDEX|DO\s+\$)/i.test(value));
const indexes = sql.filter((value) => /^CREATE (?:UNIQUE )?INDEX/i.test(value));
// Foreign keys can depend on business-key indexes emitted dynamically below.
// Match the runtime schema order: columns/backfills, indexes, then constraints.
const constraintMutations = mutations.filter((value) =>
  /^DO\s+\$/i.test(value) && /\bADD\s+CONSTRAINT\b/i.test(value));
const preIndexMutations = mutations.filter((value) => !constraintMutations.includes(value));

// SQL built dynamically in the services (branch ownership + tuple-driven indexes).
const branchTables = [
  "fleet_tms_temperature_zones", "fleet_tms_temperature_devices", "fleet_tms_temperature_readings",
  "fleet_tms_temperature_alerts", "fleet_tms_cold_chain_reports", "fleet_tms_refrigeration_unit_health",
  "fleet_tms_asset_types", "fleet_tms_assets", "fleet_tms_asset_assignments", "fleet_tms_asset_events",
  "fleet_tms_barcode_scan_events", "fleet_tms_rfid_events", "fleet_tms_readiness_documents",
  "compliance_records", "compliance_record_documents", "compliance_expiry_events",
  "vehicle_inspection_records", "inspection_defects", "jurisdiction_mileage_records",
  "jurisdiction_fuel_records", "driver_duty_status_records", "eld_device_registry",
  "market_addresses", "business_tax_readiness",
];

const dynamicIndexes = [
  ["fleet_tms_cold_chain_policies", "idx_ftms_ccpolicy_company_scope", "company_id, scope_type, scope_key, status"],
  ["fleet_tms_cold_chain_policies", "idx_ftms_ccpolicy_branch", "company_id, branch_id"],
  ["fleet_tms_cold_chain_policies", "uq_ftms_ccpolicy_branch_scope", "company_id, COALESCE(branch_id, 0), policy_code, scope_type, scope_key", true],
  ["fleet_tms_cold_chain_event_log", "idx_ftms_cclog_branch", "company_id, branch_id"],
  ["fleet_tms_cold_chain_policies", "uq_ftms_ccpolicy_branch_idem", "company_id, COALESCE(branch_id, 0), idempotency_key", true, "idempotency_key IS NOT NULL"],
  ["fleet_tms_temperature_readings", "uq_ftms_tread_branch_idem", "company_id, COALESCE(branch_id, 0), idempotency_key", true, "idempotency_key IS NOT NULL"],
  ["fleet_tms_temperature_alerts", "uq_ftms_talert_branch_idem", "company_id, COALESCE(branch_id, 0), idempotency_key", true, "idempotency_key IS NOT NULL"],
  ["fleet_tms_cold_chain_policies", "idx_ftms_ccpolicy_company_code", "company_id, policy_code"],
  ["fleet_tms_cold_chain_event_log", "idx_ftms_cclog_company_event", "company_id, event_type, occurred_at_utc DESC"],
  ["fleet_tms_cold_chain_event_log", "idx_ftms_cclog_company_agg", "company_id, aggregate_type, aggregate_id, occurred_at_utc DESC"],
  ["fleet_tms_cold_chain_event_log", "idx_ftms_cclog_company_status", "company_id, status, retry_count, occurred_at_utc DESC"],
  ["fleet_tms_cold_chain_event_log", "uq_ftms_cclog_branch_event_idem", "company_id, COALESCE(branch_id, 0), event_type, idempotency_key", true, "idempotency_key IS NOT NULL"],
  ["fleet_tms_shipments", "idx_ftms_ship_company_status", "company_id, status"],
  ["fleet_tms_shipments", "idx_ftms_ship_company_branch", "company_id, branch_id, status"],
  ["fleet_tms_shipments", "idx_ftms_ship_company_number", "company_id, shipment_number"],
  ["fleet_tms_shipment_stops", "idx_ftms_stops_company_ship", "company_id, shipment_id"],
  ["fleet_tms_shipment_stops", "idx_ftms_stops_branch_ship", "company_id, branch_id, shipment_id"],
  ["fleet_tms_pods", "idx_ftms_pods_company_ship", "company_id, shipment_id"],
  ["fleet_tms_pods", "idx_ftms_pods_status", "company_id, status"],
  ["fleet_tms_tracking_links", "idx_ftms_links_token_hash", "token_hash"],
  ["fleet_tms_tracking_links", "idx_ftms_links_company_ship", "company_id, shipment_id"],
  ["fleet_tms_shipment_events", "idx_ftms_events_company_ship", "company_id, shipment_id"],
  ["fleet_tms_driver_tasks", "idx_ftms_tasks_company", "company_id, status"],
  ["fleet_tms_driver_tasks", "idx_ftms_tasks_branch", "company_id, branch_id, status"],
  ["fleet_tms_vehicles", "idx_ftms_vehicles_company", "company_id, status"],
  ["fleet_tms_tracking_points", "idx_ftms_track_company", "company_id, recorded_at_utc"],
  ["fleet_tms_maintenance_tickets", "idx_ftms_maint_company", "company_id, status"],
  ["fleet_tms_fuel_events", "idx_ftms_fuel_company", "company_id, anomaly_flag"],
  ["fleet_tms_fuel_events", "idx_ftms_fuel_branch", "company_id, branch_id, anomaly_flag"],
  ["fleet_tms_temperature_devices", "idx_ftms_tdev_company", "company_id, status"],
  ["fleet_tms_temperature_readings", "idx_ftms_tread_company_ship", "company_id, shipment_id"],
  ["fleet_tms_temperature_readings", "idx_ftms_tread_device", "company_id, device_id"],
  ["fleet_tms_temperature_alerts", "idx_ftms_talert_company", "company_id, status"],
  ["fleet_tms_cold_chain_reports", "idx_ftms_ccr_company_ship", "company_id, shipment_id"],
  ["fleet_tms_refrigeration_unit_health", "idx_ftms_ruh_company", "company_id, status"],
  ["fleet_tms_asset_types", "idx_ftms_atype_company", "company_id, code"],
  ["fleet_tms_assets", "idx_ftms_assets_company", "company_id, status"],
  ["fleet_tms_assets", "idx_ftms_assets_tag", "company_id, asset_tag"],
  ["fleet_tms_asset_assignments", "idx_ftms_aassign_company", "company_id, asset_id"],
  ["fleet_tms_asset_events", "idx_ftms_aevent_company", "company_id, asset_id"],
  ["fleet_tms_barcode_scan_events", "idx_ftms_barcode_company", "company_id, recorded_at_utc"],
  ["fleet_tms_rfid_events", "idx_ftms_rfid_company", "company_id, recorded_at_utc"],
  ["fleet_tms_saudi_regions", "idx_ftms_saudi_sort", "sort_order"],
  ["fleet_tms_readiness_documents", "idx_ftms_readiness_company", "company_id, expiry_status"],
  ["fleet_tms_dispatch_orders", "idx_ftms_dorders_company_status", "company_id, status"],
  ["fleet_tms_dispatch_orders", "idx_ftms_dorders_number", "company_id, order_number"],
  ["fleet_tms_delivery_routes", "idx_ftms_droutes_company", "company_id, status"],
  ["fleet_tms_delivery_routes", "idx_ftms_droutes_code", "company_id, route_code"],
  ["fleet_tms_last_mile_stops", "idx_ftms_lmstops_company_status", "company_id, status"],
  ["fleet_tms_last_mile_stops", "idx_ftms_lmstops_route", "company_id, route_code"],
  ["fleet_tms_last_mile_stops", "idx_ftms_lmstops_order", "company_id, order_number"],
];

const referenceSeed = String.raw`
INSERT INTO market_packs
  (code,name,description,region,status,default_currency,default_distance_unit,default_fuel_unit,supported_languages,feature_keys,package_key,base_price_cents)
VALUES
  ('canada_na','Canada / North America','Cross-border NA fleet compliance, DVIR, HOS/ELD readiness and IFTA fuel-tax foundation.','North America','active','CAD','km','liter','["en","fr"]','["market.canada_na","compliance.documents","compliance.expiry_alerts","compliance.inspections","compliance.driver_qualification","compliance.vehicle_documents","compliance.tax_readiness"]','canada_na_compliance',49900),
  ('saudi_gcc','Saudi / GCC','Saudi & GCC transport compliance, National Address, VAT / e-invoice readiness with Hijri/Gregorian expiry.','Middle East','active','SAR','km','liter','["ar","en"]','["market.saudi_gcc","compliance.documents","compliance.expiry_alerts","compliance.vehicle_documents","compliance.tax_readiness"]','saudi_gcc_compliance',49900)
ON CONFLICT (code) DO UPDATE SET
  name=EXCLUDED.name, description=EXCLUDED.description, region=EXCLUDED.region,
  status=EXCLUDED.status, default_currency=EXCLUDED.default_currency,
  default_distance_unit=EXCLUDED.default_distance_unit, default_fuel_unit=EXCLUDED.default_fuel_unit,
  supported_languages=EXCLUDED.supported_languages, feature_keys=EXCLUDED.feature_keys,
  package_key=EXCLUDED.package_key, base_price_cents=EXCLUDED.base_price_cents, updated_at=NOW();

INSERT INTO market_pack_features(pack_code,feature_key,name,tier) VALUES
 ('canada_na','market.canada_na','Canada / North America Market Pack','included'),
 ('canada_na','compliance.documents','Compliance Documents','included'),
 ('canada_na','compliance.expiry_alerts','Expiry Alerts','included'),
 ('canada_na','compliance.inspections','Vehicle Inspections / DVIR','included'),
 ('canada_na','compliance.driver_qualification','Driver Qualification','included'),
 ('canada_na','compliance.vehicle_documents','Vehicle Documents','included'),
 ('canada_na','compliance.tax_readiness','IFTA / Fuel-Tax Readiness','included'),
 ('saudi_gcc','market.saudi_gcc','Saudi / GCC Market Pack','included'),
 ('saudi_gcc','compliance.documents','Compliance Documents','included'),
 ('saudi_gcc','compliance.expiry_alerts','Expiry Alerts','included'),
 ('saudi_gcc','compliance.vehicle_documents','Transport Documents','included'),
 ('saudi_gcc','compliance.tax_readiness','VAT / e-Invoice Readiness','included')
ON CONFLICT (pack_code,feature_key) DO UPDATE SET name=EXCLUDED.name,tier=EXCLUDED.tier;

INSERT INTO market_unit_settings(pack_code,distance_unit,fuel_unit,weight_unit) VALUES
 ('canada_na','km','liter','kg'),('saudi_gcc','km','liter','kg')
ON CONFLICT (pack_code) DO UPDATE SET distance_unit=EXCLUDED.distance_unit,fuel_unit=EXCLUDED.fuel_unit,weight_unit=EXCLUDED.weight_unit;

INSERT INTO market_currency_settings(pack_code,currency,is_default) VALUES
 ('canada_na','CAD',true),('canada_na','USD',false),('saudi_gcc','SAR',true),('saudi_gcc','AED',false)
ON CONFLICT (pack_code,currency) DO UPDATE SET is_default=EXCLUDED.is_default;

INSERT INTO market_language_settings(pack_code,language,is_default,rtl) VALUES
 ('canada_na','en',true,false),('canada_na','fr',false,false),('saudi_gcc','ar',true,true),('saudi_gcc','en',false,false)
ON CONFLICT (pack_code,language) DO UPDATE SET is_default=EXCLUDED.is_default,rtl=EXCLUDED.rtl;
`;

const rls = String.raw`
DO $fleet_rls$
DECLARE rec RECORD; tenant_col TEXT;
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_app') THEN
    RAISE EXCEPTION 'Stage 50 requires the restricted opstrax_app role; apply Stage 20 first';
  END IF;
  FOR rec IN
    SELECT name AS table_name,
      EXISTS (SELECT 1 FROM information_schema.columns c WHERE c.table_schema='public' AND c.table_name=name AND c.column_name='company_id' AND c.data_type='bigint') AS has_company,
      EXISTS (SELECT 1 FROM information_schema.columns c WHERE c.table_schema='public' AND c.table_name=name AND c.column_name='tenant_id' AND c.data_type='bigint') AS has_tenant
    FROM unnest(ARRAY[
      'fleet_tms_shipments','fleet_tms_shipment_stops','fleet_tms_pods','fleet_tms_tracking_links',
      'fleet_tms_shipment_events','fleet_tms_driver_tasks','fleet_tms_vehicles','fleet_tms_tracking_points',
      'fleet_tms_maintenance_tickets','fleet_tms_fuel_events','fleet_tms_temperature_zones',
      'fleet_tms_temperature_devices','fleet_tms_temperature_readings','fleet_tms_temperature_alerts',
      'fleet_tms_cold_chain_reports','fleet_tms_refrigeration_unit_health','fleet_tms_asset_types',
      'fleet_tms_assets','fleet_tms_asset_assignments','fleet_tms_asset_events','fleet_tms_barcode_scan_events',
      'fleet_tms_rfid_events','fleet_tms_readiness_documents','fleet_tms_cold_chain_policies',
      'fleet_tms_cold_chain_event_log','fleet_tms_dispatch_orders','fleet_tms_delivery_routes',
      'fleet_tms_last_mile_stops','tenant_market_packs','compliance_records','compliance_record_documents',
      'compliance_expiry_events','vehicle_inspection_records','inspection_defects','jurisdiction_mileage_records',
      'jurisdiction_fuel_records','driver_duty_status_records','eld_device_registry','market_addresses',
      'business_tax_readiness','market_pack_branch_migration_audit'
    ]) name
  LOOP
    tenant_col := CASE WHEN rec.has_company THEN 'company_id' ELSE 'tenant_id' END;
    EXECUTE format('ALTER TABLE public.%I ENABLE ROW LEVEL SECURITY',rec.table_name);
    EXECUTE format('ALTER TABLE public.%I FORCE ROW LEVEL SECURITY',rec.table_name);
    -- Policy names alone are not a security contract: a same-name permissive or
    -- cross-tenant policy must be repaired atomically by this migration.
    EXECUTE format('DROP POLICY IF EXISTS tenant_isolation ON public.%I',rec.table_name);
    EXECUTE format('CREATE POLICY tenant_isolation ON public.%I FOR ALL USING (%I = NULLIF(current_setting(''app.current_tenant_id'',true),'''')::bigint) WITH CHECK (%I = NULLIF(current_setting(''app.current_tenant_id'',true),'''')::bigint)',rec.table_name,tenant_col,tenant_col);
    EXECUTE format('DROP POLICY IF EXISTS platform_admin_bypass ON public.%I',rec.table_name);
    EXECUTE format('CREATE POLICY platform_admin_bypass ON public.%I FOR ALL USING (NULLIF(current_setting(''app.platform_admin'',true),'''')=''on'') WITH CHECK (NULLIF(current_setting(''app.platform_admin'',true),'''')=''on'')',rec.table_name);
  END LOOP;
  FOR rec IN
    SELECT to_regclass('public.'||name) AS object_name
    FROM unnest(ARRAY[
      'fleet_tms_shipments','fleet_tms_shipment_stops','fleet_tms_pods','fleet_tms_tracking_links',
      'fleet_tms_shipment_events','fleet_tms_driver_tasks','fleet_tms_vehicles','fleet_tms_tracking_points',
      'fleet_tms_maintenance_tickets','fleet_tms_fuel_events','fleet_tms_temperature_zones',
      'fleet_tms_temperature_devices','fleet_tms_temperature_readings','fleet_tms_temperature_alerts',
      'fleet_tms_cold_chain_reports','fleet_tms_refrigeration_unit_health','fleet_tms_asset_types',
      'fleet_tms_assets','fleet_tms_asset_assignments','fleet_tms_asset_events','fleet_tms_barcode_scan_events',
      'fleet_tms_rfid_events','fleet_tms_readiness_documents','fleet_tms_cold_chain_policies',
      'fleet_tms_cold_chain_event_log','fleet_tms_dispatch_orders','fleet_tms_delivery_routes',
      'fleet_tms_last_mile_stops','tenant_market_packs','compliance_records','compliance_record_documents',
      'compliance_expiry_events','vehicle_inspection_records','inspection_defects','jurisdiction_mileage_records',
      'jurisdiction_fuel_records','driver_duty_status_records','eld_device_registry','market_addresses',
      'business_tax_readiness','market_pack_branch_migration_audit'
    ]) name
  LOOP
    EXECUTE format('GRANT SELECT,INSERT,UPDATE,DELETE ON TABLE %s TO opstrax_app',rec.object_name);
  END LOOP;
  FOR rec IN
    SELECT c.oid::regclass AS object_name
    FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
    WHERE n.nspname='public' AND c.relkind IN ('r','p')
      AND c.relname IN
        ('fleet_tms_saudi_regions','market_packs','market_pack_features','market_address_schemas',
         'market_document_types','market_driver_requirements','market_vehicle_requirements',
         'market_inspection_templates','inspection_items','market_tax_reporting_rules',
         'market_unit_settings','market_currency_settings','market_language_settings')
  LOOP
    EXECUTE format('REVOKE INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER ON TABLE %s FROM opstrax_app',rec.object_name);
    EXECUTE format('GRANT SELECT ON TABLE %s TO opstrax_app',rec.object_name);
  END LOOP;
  FOR rec IN
    SELECT seq.oid::regclass AS object_name
    FROM pg_class tbl
    JOIN pg_namespace tbl_ns ON tbl_ns.oid=tbl.relnamespace
    JOIN pg_depend dep ON dep.refobjid=tbl.oid AND dep.refobjsubid>0 AND dep.deptype IN ('a','i')
    JOIN pg_class seq ON seq.oid=dep.objid AND seq.relkind='S'
    WHERE tbl_ns.nspname='public' AND tbl.relname=ANY(ARRAY[
      'fleet_tms_shipments','fleet_tms_shipment_stops','fleet_tms_pods','fleet_tms_tracking_links',
      'fleet_tms_shipment_events','fleet_tms_driver_tasks','fleet_tms_vehicles','fleet_tms_tracking_points',
      'fleet_tms_maintenance_tickets','fleet_tms_fuel_events','fleet_tms_temperature_zones',
      'fleet_tms_temperature_devices','fleet_tms_temperature_readings','fleet_tms_temperature_alerts',
      'fleet_tms_cold_chain_reports','fleet_tms_refrigeration_unit_health','fleet_tms_asset_types',
      'fleet_tms_assets','fleet_tms_asset_assignments','fleet_tms_asset_events','fleet_tms_barcode_scan_events',
      'fleet_tms_rfid_events','fleet_tms_readiness_documents','fleet_tms_cold_chain_policies',
      'fleet_tms_cold_chain_event_log','fleet_tms_dispatch_orders','fleet_tms_delivery_routes',
      'fleet_tms_last_mile_stops','tenant_market_packs','compliance_records','compliance_record_documents',
      'compliance_expiry_events','vehicle_inspection_records','inspection_defects','jurisdiction_mileage_records',
      'jurisdiction_fuel_records','driver_duty_status_records','eld_device_registry','market_addresses',
      'business_tax_readiness','market_pack_branch_migration_audit'
    ])
  LOOP
    EXECUTE format('GRANT USAGE,SELECT ON SEQUENCE %s TO opstrax_app',rec.object_name);
  END LOOP;
END
$fleet_rls$;
`;

const unique = (items) => [...new Set(items.map((item) => item.trim()).filter(Boolean))];
const statements = [
  "BEGIN;",
  ...unique(creates).map((value) => `${value.replace(/;?\s*$/, ";")}`),
  ...branchTables.flatMap((table) => [
    `ALTER TABLE "${table}" ADD COLUMN IF NOT EXISTS branch_id BIGINT NULL;`,
    `CREATE INDEX IF NOT EXISTS "idx_${table}_branch" ON "${table}" (company_id, branch_id);`,
  ]),
  ...unique(postCreateBatches).map((value) => `${value.replace(/;?\s*$/, ";")}`),
  ...unique(preIndexMutations).map((value) => `${value.replace(/;?\s*$/, ";")}`),
  ...unique(indexes).map((value) => `${value.replace(/;?\s*$/, ";")}`),
  "DROP INDEX IF EXISTS uq_ftms_ccpolicy_company_idem;",
  "DROP INDEX IF EXISTS uq_ftms_cclog_company_idem;",
  ...dynamicIndexes.map(([table, name, columns, uniqueIndex = false, predicate = ""]) =>
    `CREATE ${uniqueIndex ? "UNIQUE " : ""}INDEX IF NOT EXISTS "${name}" ON "${table}" (${columns})${predicate ? ` WHERE ${predicate}` : ""};`),
  ...unique(constraintMutations).map((value) => `${value.replace(/;?\s*$/, ";")}`),
  referenceSeed.trim(),
  rls.trim(),
  "COMMIT;",
];

const header = `-- Stage 50 — complete production schema contract for all Fleet routes\n-- GENERATED by tools/generate-fleet-production-migration.mjs.\n-- Owner-only, additive/idempotent, and fail-fast under psql ON_ERROR_STOP=1.\n-- Demo tenant/operational data is deliberately not seeded. Canonical Canada/Saudi\n-- market reference definitions are production configuration, not demo data.\n\n`;
fs.writeFileSync(output, `${header}${statements.join("\n\n")}\n`);
console.log(path.relative(root, output));
