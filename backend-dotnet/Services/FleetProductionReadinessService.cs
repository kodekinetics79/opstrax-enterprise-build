using Opstrax.Api.Data;
using Opstrax.Api.Observability;

namespace Opstrax.Api.Services;

/// <summary>
/// Fail-closed production proof for the database contract used by all nine Fleet
/// routes. A successful SELECT 1 is not readiness: the runtime must be the
/// restricted role, Stage 50 must be ledgered, every required table must exist,
/// and tenant tables must have ENABLE + FORCE RLS, both canonical policies and
/// runtime DML grants.
/// </summary>
public sealed class FleetProductionReadinessService
{
    internal static readonly string[] CriticalWorkerNames =
    [
        "TelemetryBackgroundService",
        "SafetyBackgroundService",
        "TripBackgroundService",
        "MaintenanceBackgroundService",
        "EscalationBackgroundService",
        "ScheduledReportBackgroundService",
        "RetentionEnforcementService",
    ];

    internal static readonly TimeSpan CriticalWorkerStartupGrace = TimeSpan.FromMinutes(2);
    internal static readonly TimeSpan CriticalWorkerFreshness = TimeSpan.FromMinutes(10);
    internal static int CriticalWorkerFailureThreshold(string serviceName) =>
        string.Equals(serviceName, "RetentionEnforcementService", StringComparison.Ordinal) ? 1 : 3;

    private readonly Database db;
    private readonly ILogger<FleetProductionReadinessService> log;
    private readonly TimeProvider timeProvider;
    private readonly DateTimeOffset processStartedAt;

    public FleetProductionReadinessService(Database db, ILogger<FleetProductionReadinessService> log)
        : this(db, log, TimeProvider.System, new DateTimeOffset(BuildInfo.StartedAtUtc)) { }

    internal FleetProductionReadinessService(
        Database db,
        ILogger<FleetProductionReadinessService> log,
        TimeProvider timeProvider,
        DateTimeOffset processStartedAt)
    {
        this.db = db;
        this.log = log;
        this.timeProvider = timeProvider;
        this.processStartedAt = processStartedAt;
    }

    internal bool CriticalWorkerStartupGraceActive =>
        timeProvider.GetUtcNow() - processStartedAt < CriticalWorkerStartupGrace;

    internal int CriticalWorkerStartupGraceRemainingSeconds => Math.Max(0,
        (int)Math.Ceiling((CriticalWorkerStartupGrace - (timeProvider.GetUtcNow() - processStartedAt)).TotalSeconds));

    public async Task<FleetProductionContractResult> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            // Readiness must inspect cross-tenant data invariants (for example,
            // workforce ownership and encrypted-licence backfill completeness).
            // A plain restricted-role query has no tenant GUC and would see zero
            // RLS rows, falsely passing those checks. Use the canonical short-lived
            // system transaction while retaining the restricted database identity.
            var row = await db.QuerySingleInSystemScopeAsync(Sql, command =>
            {
                command.Parameters.AddWithValue("@criticalWorkers", CriticalWorkerNames);
                command.Parameters.AddWithValue("@processStartedAt", processStartedAt.UtcDateTime);
            }, ct);
            if (row is null)
                return FleetProductionContractResult.Failed("fleet_contract_no_result");

            static bool Bool(Dictionary<string, object?> r, string key) => r.GetValueOrDefault(key) is true;
            static int Int(Dictionary<string, object?> r, string key) => Convert.ToInt32(r.GetValueOrDefault(key) ?? -1);

            var rawWorkerViolations = Int(row, "criticalWorkerViolations");
            var startupGraceActive = CriticalWorkerStartupGraceActive;
            var result = new FleetProductionContractResult(
                Bool(row, "roleRestricted"),
                Int(row, "missingTables"),
                Int(row, "rlsViolations"),
                Int(row, "grantViolations"),
                Int(row, "tenantCoverageViolations"),
                Int(row, "tenantGrantViolations"),
                Int(row, "defaultPrivilegeViolations"),
                Int(row, "runtimeRouteColumnViolations"),
                Int(row, "runtimeRouteObjectViolations"),
                Int(row, "fleetIntegrityObjectViolations"),
                Int(row, "workforceContractViolations"),
                Bool(row, "migrationApplied"),
                Bool(row, "runtimeSupportMigrationApplied"),
                Bool(row, "tenantCoverageMigrationApplied"),
                Bool(row, "coldChainIntegrityMigrationApplied"),
                Bool(row, "runtimeRouteMigrationApplied"),
                Bool(row, "assetTypeIntegrityMigrationApplied"),
                Bool(row, "workforceScheduleIntegrityMigrationApplied"),
                Bool(row, "tenantTicketMigrationApplied"),
                Bool(row, "dataProtectionKeyRingMigrationApplied"),
                Bool(row, "marketCatalogReady"),
                Bool(row, "indexesReady"),
                startupGraceActive ? 0 : rawWorkerViolations,
                rawWorkerViolations,
                Int(row, "missingCriticalWorkers"),
                Int(row, "staleCriticalWorkers"),
                Int(row, "failedCriticalWorkers"),
                startupGraceActive,
                CriticalWorkerStartupGraceRemainingSeconds,
                null);

            if (!result.Ready)
            {
                log.LogError(new EventId(1, "fleet_production_contract_invalid"),
                    "Fleet production DB contract invalid: roleRestricted={Role}; missingTables={Missing}; " +
                    "rlsViolations={Rls}; grantViolations={Grants}; tenantCoverageViolations={TenantRls}; " +
                    "tenantGrantViolations={TenantGrants}; defaultPrivilegeViolations={DefaultGrants}; runtimeRouteColumnViolations={RouteColumns}; runtimeRouteObjectViolations={RouteObjects}; fleetIntegrityObjectViolations={IntegrityObjects}; workforceContractViolations={WorkforceContract}; migrationApplied={Migration}; " +
                    "runtimeSupportMigrationApplied={RuntimeMigration}; tenantCoverageMigrationApplied={TenantMigration}; coldChainIntegrityMigrationApplied={ColdChainMigration}; runtimeRouteMigrationApplied={RouteMigration}; assetTypeIntegrityMigrationApplied={AssetMigration}; workforceScheduleIntegrityMigrationApplied={WorkforceMigration}; tenantTicketMigrationApplied={TicketMigration}; dataProtectionKeyRingMigrationApplied={KeyRingMigration}; marketCatalogReady={Catalog}; " +
                    "indexesReady={Indexes}; criticalWorkerViolations={Workers}; rawCriticalWorkerViolations={RawWorkers}; missingCriticalWorkers={MissingWorkers}; staleCriticalWorkers={StaleWorkers}; failedCriticalWorkers={FailedWorkers}; workerStartupGraceActive={WorkerGrace}",
                    result.RoleRestricted, result.MissingTables, result.RlsViolations,
                    result.GrantViolations, result.TenantCoverageViolations, result.TenantGrantViolations,
                    result.DefaultPrivilegeViolations, result.RuntimeRouteColumnViolations, result.RuntimeRouteObjectViolations, result.FleetIntegrityObjectViolations, result.WorkforceContractViolations,
                    result.MigrationApplied, result.RuntimeSupportMigrationApplied,
                    result.TenantCoverageMigrationApplied, result.ColdChainIntegrityMigrationApplied,
                    result.RuntimeRouteMigrationApplied, result.AssetTypeIntegrityMigrationApplied, result.WorkforceScheduleIntegrityMigrationApplied, result.TenantTicketMigrationApplied, result.DataProtectionKeyRingMigrationApplied, result.MarketCatalogReady,
                    result.IndexesReady, result.CriticalWorkerViolations, result.RawCriticalWorkerViolations,
                    result.MissingCriticalWorkers, result.StaleCriticalWorkers, result.FailedCriticalWorkers,
                    result.CriticalWorkerStartupGraceActive);
            }
            return result;
        }
        catch (Exception ex)
        {
            // Never include a connection string or SQL exception text in the public
            // readiness envelope. The structured server log retains the exception
            // under a stable event name for operator diagnosis.
            log.LogError(new EventId(1, "fleet_production_contract_check_failed"), ex,
                "Fleet production DB contract check failed");
            return FleetProductionContractResult.Failed("fleet_contract_query_failed");
        }
    }

    private const string Sql = """
        WITH critical_workers(service_name) AS (
          SELECT unnest(@criticalWorkers::text[])
        ), required(name, tenant_scoped) AS (VALUES
          ('companies',true),
          ('workforce_schedules',true),
          ('vehicles',true),('drivers',true),('vehicle_assignments',true),('dispatch_assignments',true),
          ('fleet_tms_shipments',true),('fleet_tms_shipment_stops',true),('fleet_tms_pods',true),
          ('fleet_tms_tracking_links',true),('fleet_tms_shipment_events',true),('fleet_tms_driver_tasks',true),
          ('fleet_tms_vehicles',true),('fleet_tms_tracking_points',true),('fleet_tms_maintenance_tickets',true),
          ('fleet_tms_fuel_events',true),('fleet_tms_temperature_zones',true),
          ('fleet_tms_temperature_devices',true),('fleet_tms_temperature_readings',true),
          ('fleet_tms_temperature_alerts',true),('fleet_tms_cold_chain_reports',true),
          ('fleet_tms_refrigeration_unit_health',true),('fleet_tms_asset_types',true),('fleet_tms_assets',true),
          ('fleet_tms_asset_assignments',true),('fleet_tms_asset_events',true),
          ('fleet_tms_barcode_scan_events',true),('fleet_tms_rfid_events',true),
          ('fleet_tms_saudi_regions',false),('fleet_tms_readiness_documents',true),
          ('fleet_tms_cold_chain_policies',true),('fleet_tms_cold_chain_event_log',true),
          ('fleet_tms_dispatch_orders',true),('fleet_tms_delivery_routes',true),('fleet_tms_last_mile_stops',true),
          ('market_packs',false),('market_pack_features',false),('tenant_market_packs',true),
          ('market_address_schemas',false),('market_document_types',false),('market_driver_requirements',false),
          ('market_vehicle_requirements',false),('market_inspection_templates',false),('inspection_items',false),
          ('market_tax_reporting_rules',false),('market_unit_settings',false),('market_currency_settings',false),
          ('market_language_settings',false),('compliance_records',true),('compliance_record_documents',true),
          ('compliance_expiry_events',true),('vehicle_inspection_records',true),('inspection_defects',true),
          ('jurisdiction_mileage_records',true),('jurisdiction_fuel_records',true),
          ('driver_duty_status_records',true),('eld_device_registry',true),('market_addresses',true),
          ('business_tax_readiness',true),
          ('service_run_history',false),('service_heartbeats',false),('platform_incidents',true),
          ('notifications',true),('notification_recipients',true),('escalation_rules',true),
          ('saved_reports',true),('report_execution_log',true),('scheduled_report_deliveries',true),
          ('scheduled_reports',true),('routes',true),('route_stops',true),('trips',true),('trip_stops',true),
          ('location_events',true),('latest_vehicle_positions',true),('telemetry_alerts',true),
          ('telemetry_rules',true),('telemetry_nonces',false),('gps_gateway_replay',false),
          ('safety_events',true),('driver_safety_scores',true),('telemetry_live_asset_states',true),
          ('fleet_health_snapshots',true),('evidence_package_items',true),('vehicle_safety_scorecards',true),
          ('ai_recommendations',true),('maintenance_pm_rules',true),('maintenance_items',true),
          ('integrations',true),('geofences',true),('dispatch_exceptions',true)
        ), reference_tables(name) AS (VALUES
          ('fleet_tms_saudi_regions'),('market_packs'),('market_pack_features'),('market_address_schemas'),
          ('market_document_types'),('market_driver_requirements'),('market_vehicle_requirements'),
          ('market_inspection_templates'),('inspection_items'),('market_tax_reporting_rules'),
          ('market_unit_settings'),('market_currency_settings'),('market_language_settings')
        ), runtime_global(name) AS (VALUES
          ('service_run_history'),('service_heartbeats'),('telemetry_nonces'),('gps_gateway_replay')
        ), tenant_scope AS (
          SELECT c.oid,c.relname AS name,c.relrowsecurity,c.relforcerowsecurity,
            CASE
              WHEN c.relname='companies' THEN 'id'
              WHEN EXISTS (SELECT 1 FROM information_schema.columns x WHERE x.table_schema='public' AND x.table_name=c.relname AND x.column_name='company_id' AND x.data_type='bigint') THEN 'company_id'
              ELSE 'tenant_id'
            END AS tenant_col
          FROM pg_class c
          JOIN pg_namespace n ON n.oid=c.relnamespace
          WHERE n.nspname='public' AND c.relkind IN ('r','p')
            AND c.relname NOT IN ('platform_invoices','gps_gateway_replay','platform_impersonation_sessions','roles','report_catalog')
            AND (c.relname='companies' OR EXISTS (
              SELECT 1 FROM information_schema.columns x
              WHERE x.table_schema='public' AND x.table_name=c.relname
                AND x.column_name IN ('company_id','tenant_id') AND x.data_type='bigint'))
        ), tenant_privileges(name,allow_insert,allow_update,allow_delete) AS (VALUES
          ('authorization_decision_logs',true,false,false),
          ('companies',false,true,false),
          ('audit_logs',true,false,false),
          ('compliance_evidence',true,false,false),
          ('fleet_tms_shipment_events',true,false,false),
          ('fleet_tms_cold_chain_event_log',true,false,false),
          ('fleet_tms_asset_events',true,false,false),
          ('fleet_tms_barcode_scan_events',true,false,false),
          ('fleet_tms_rfid_events',true,false,false),
          ('compliance_expiry_events',true,true,false),
          ('market_pack_branch_migration_audit',false,false,false),
          ('access_review_items',true,true,false),('access_reviews',true,true,false),
          ('backup_verifications',true,false,false),('company_security_settings',true,true,false),
          ('compliance_audit_packages',true,true,false),('compliance_violations',false,true,false),
          ('data_retention_policies',true,true,false),('driver_compliance_status',false,false,false),
          ('export_requests',true,true,false),('fleet_tms_branch_migration_audit',false,false,false),
          ('hos_clocks',false,false,false),
          ('security_events',true,false,false),('sso_connections',true,true,false),
          ('tenant_market_packs',false,false,false),
          ('tenant_entitlements',false,false,false),('demo_fixture_versions',false,false,false),('tenant_subscriptions',false,false,false),
          ('vehicle_compliance_status',false,false,false),('workforce_schedules',true,true,false),
          ('mfa_login_challenge_consumptions',true,false,true)
        ), workforce_columns(column_name,data_type,not_null,column_default,identity_kind) AS (VALUES
          ('id','bigint',true,'','a'),
          ('company_id','bigint',true,'',''),('branch_id','bigint',false,'',''),
          ('driver_id','bigint',true,'',''),('week_start','date',true,'',''),
          ('monday','character varying(40)',true,'''Off''::character varying',''),
          ('tuesday','character varying(40)',true,'''Off''::character varying',''),
          ('wednesday','character varying(40)',true,'''Off''::character varying',''),
          ('thursday','character varying(40)',true,'''Off''::character varying',''),
          ('friday','character varying(40)',true,'''Off''::character varying',''),
          ('saturday','character varying(40)',true,'''Off''::character varying',''),
          ('sunday','character varying(40)',true,'''Off''::character varying',''),
          ('created_at','timestamp with time zone',true,'now()',''),
          ('updated_at','timestamp with time zone',false,'','')
        ), runtime_route_columns(table_name,column_name,data_type,not_null,column_default,identity_kind) AS (VALUES
          ('companies','country','character varying(2)',false,'',''),
          ('companies','currency','character varying(8)',false,'',''),
          ('authorization_decision_logs','id','bigint',true,'','a'),
          ('authorization_decision_logs','tenant_id','bigint',true,'',''),
          ('authorization_decision_logs','actor_type','character varying(40)',true,'',''),
          ('authorization_decision_logs','actor_id','character varying(120)',false,'',''),
          ('authorization_decision_logs','permission_key','character varying(120)',true,'',''),
          ('authorization_decision_logs','resource_type','character varying(80)',true,'',''),
          ('authorization_decision_logs','resource_id','character varying(120)',false,'',''),
          ('authorization_decision_logs','decision','character varying(32)',true,'',''),
          ('authorization_decision_logs','reason','text',true,'',''),
          ('authorization_decision_logs','correlation_id','character varying(120)',false,'',''),
          ('authorization_decision_logs','request_id','character varying(120)',false,'',''),
          ('authorization_decision_logs','created_at','timestamp with time zone',true,'now()',''),
          ('carriers','carrier_number','character varying(80)',false,'',''),
          ('carriers','contact_name','character varying(160)',false,'',''),
          ('carriers','phone','character varying(50)',false,'',''),
          ('carriers','email','character varying(220)',false,'',''),
          ('carriers','region','character varying(120)',false,'',''),
          ('carriers','compliance_status','character varying(80)',true,'''Compliant''::character varying',''),
          ('carriers','insurance_expiry','date',false,'',''),
          ('carriers','contract_status','character varying(80)',true,'''Active''::character varying',''),
          ('carriers','on_time_percent','numeric(6,2)',true,'90',''),
          ('carriers','safety_score','numeric(6,2)',true,'88',''),
          ('carriers','cost_score','numeric(6,2)',true,'82',''),
          ('carriers','performance_score','numeric(6,2)',true,'86',''),
          ('carriers','risk_score','numeric(6,2)',true,'20',''),
          ('carriers','recommended_action','character varying(260)',false,'',''),
          ('carriers','notes','text',false,'',''),
          ('carriers','updated_at','timestamp with time zone',false,'',''),
          ('carriers','deleted_at','timestamp with time zone',false,'',''),
          ('latest_vehicle_positions','source_event_id','bigint',false,'',''),
          ('latest_vehicle_positions','correlation_id','character varying(120)',false,'',''),
          ('latest_vehicle_positions','causation_id','character varying(120)',false,'',''),
          ('latest_vehicle_positions','source_channel','character varying(40)',false,'',''),
          ('latest_vehicle_positions','source','text',false,'',''),
          ('latest_vehicle_positions','provider','text',false,'',''),
          ('latest_vehicle_positions','protocol','text',false,'',''),
          ('latest_vehicle_positions','adapter_version','text',false,'',''),
          ('latest_vehicle_positions','device_fix_time','timestamp with time zone',false,'',''),
          ('latest_vehicle_positions','gateway_received_at','timestamp with time zone',false,'',''),
          ('latest_vehicle_positions','normalized_at','timestamp with time zone',false,'',''),
          ('latest_vehicle_positions','confidence','numeric(4,3)',false,'',''),
          ('latest_vehicle_positions','trust_score','numeric(4,3)',false,'',''),
          ('latest_vehicle_positions','quality_flags','jsonb',false,'',''),
          ('location_events','source','character varying(40)',true,'''device''::character varying',''),
          ('location_events','nonce','character varying(128)',false,'',''),
          ('location_events','source_channel','character varying(40)',false,'',''),
          ('location_events','correlation_id','character varying(120)',false,'',''),
          ('location_events','causation_id','character varying(120)',false,'',''),
          ('location_events','client_generated_id','character varying(120)',false,'',''),
          ('location_events','idempotency_key','character varying(120)',false,'','')
        ), identity_indexes(name, table_name, key1, key2, predicate) AS (VALUES
          ('uq_vehicles_identity_code_normalized','vehicles','company_id','lower(btrim(vehicle_code::text))',''),
          ('uq_drivers_identity_code_normalized','drivers','company_id','lower(btrim(driver_code::text))',''),
          ('uq_vehicles_active_vin_normalized','vehicles','company_id','lower(btrim(vin::text))','deleted_at IS NULL AND NULLIF(btrim(vin::text), ''''::text) IS NOT NULL'),
          ('uq_drivers_active_license_plaintext_normalized','drivers','company_id','lower(btrim(license_number::text))','deleted_at IS NULL AND NULLIF(btrim(license_number::text), ''''::text) IS NOT NULL AND NULLIF(btrim(license_number_bidx::text), ''''::text) IS NULL'),
          ('uq_drivers_active_license_bidx','drivers','company_id','license_number_bidx','deleted_at IS NULL AND NULLIF(btrim(license_number_bidx::text), ''''::text) IS NOT NULL')
        ), fleet_integrity_indexes(name,table_name,key_count,key1,key2,key3,predicate) AS (VALUES
          ('uq_ftms_tdev_tenant_code_norm','fleet_tms_temperature_devices',2,'company_id','lower(btrim(device_code::text))','',''),
          ('uq_ftms_tdev_branch_idem','fleet_tms_temperature_devices',3,'company_id','COALESCE(branch_id, 0::bigint)','idempotency_key','(NULLIF(btrim((idempotency_key)::text), ''''::text) IS NOT NULL)'),
          ('uq_ftms_atype_tenant_code_norm','fleet_tms_asset_types',2,'company_id','lower(btrim(code::text))','','')
        ), objects AS (
          SELECT r.*, c.oid, c.relrowsecurity, c.relforcerowsecurity,
            CASE
              WHEN r.name='companies' THEN 'id'
              WHEN EXISTS (SELECT 1 FROM information_schema.columns x WHERE x.table_schema='public' AND x.table_name=r.name AND x.column_name='company_id' AND x.data_type='bigint') THEN 'company_id'
              WHEN EXISTS (SELECT 1 FROM information_schema.columns x WHERE x.table_schema='public' AND x.table_name=r.name AND x.column_name='tenant_id' AND x.data_type='bigint') THEN 'tenant_id'
            END AS tenant_col
          FROM required r
          LEFT JOIN pg_class c ON c.oid=to_regclass('public.' || r.name)
        ), role_state AS (
          SELECT COUNT(*)=2 AND BOOL_AND(rolcanlogin AND NOT rolsuper AND NOT rolbypassrls
                   AND NOT rolcreatedb AND NOT rolcreaterole AND NOT rolinherit AND NOT rolreplication
                   AND has_database_privilege(rolname,current_database(),'CONNECT')
                   AND NOT has_database_privilege(rolname,current_database(),'CREATE')
                   AND NOT has_database_privilege(rolname,current_database(),'TEMPORARY')
                   AND has_schema_privilege(rolname,'public','USAGE')
                   AND NOT has_schema_privilege(rolname,'public','CREATE'))
                   -- Managed Postgres (Neon/RDS/Cloud SQL) auto-grants the database owner ADMIN
                   -- membership in every role created under it, recorded with the provider's
                   -- internal superuser as grantor, so the customer-visible owner CANNOT revoke
                   -- it. Counting that edge made this readiness proof unsatisfiable on every
                   -- managed provider. The owner already holds unrestricted access, so it grants
                   -- nothing new; every OTHER principal still fails. Matches the same relaxation
                   -- in Database.ValidateIdentity, Stage 58 and the predeploy runner.
                   AND NOT EXISTS (SELECT 1 FROM pg_auth_members membership
                     WHERE membership.member IN (SELECT oid FROM pg_roles WHERE rolname IN ('opstrax_app','opstrax_system')))
                   AND NOT EXISTS (SELECT 1 FROM pg_auth_members membership
                     WHERE membership.roleid IN (SELECT oid FROM pg_roles WHERE rolname IN ('opstrax_app','opstrax_system'))
                       AND membership.member<>(SELECT d.datdba FROM pg_database d WHERE d.datname=current_database()))
                   AND NOT has_function_privilege('opstrax_app','opstrax_security.issue_tenant_ticket(bigint,integer,bigint,integer)','EXECUTE')
                   AND has_function_privilege('opstrax_system','opstrax_security.issue_tenant_ticket(bigint,integer,bigint,integer)','EXECUTE')
                   AND has_function_privilege('opstrax_app','opstrax_security.current_tenant_id()','EXECUTE')
                   AND to_regclass('public.platform_data_protection_keys') IS NOT NULL
                   AND CASE WHEN to_regclass('public.platform_data_protection_keys') IS NULL THEN false ELSE (
                     (SELECT COUNT(*) FROM pg_attribute a
                       WHERE a.attrelid=to_regclass('public.platform_data_protection_keys')
                         AND a.attnum>0 AND NOT a.attisdropped)=4
                     AND NOT EXISTS (SELECT 1 FROM (VALUES
                       ('id','bigint',true,'a',''),
                       ('friendly_name','character varying(256)',true,'',''),
                       ('xml_payload','text',true,'',''),
                       ('created_at','timestamp with time zone',true,'','clock_timestamp()')
                     ) expected(column_name,data_type,not_null,identity_kind,column_default)
                     LEFT JOIN pg_attribute actual
                       ON actual.attrelid=to_regclass('public.platform_data_protection_keys')
                      AND actual.attname=expected.column_name AND actual.attnum>0 AND NOT actual.attisdropped
                     LEFT JOIN pg_attrdef actual_default
                       ON actual_default.adrelid=actual.attrelid AND actual_default.adnum=actual.attnum
                     WHERE actual.attname IS NULL
                       OR format_type(actual.atttypid,actual.atttypmod)<>expected.data_type
                       OR actual.attnotnull<>expected.not_null
                       OR actual.attidentity::text<>expected.identity_kind
                       OR COALESCE(pg_get_expr(actual_default.adbin,actual_default.adrelid),'')<>expected.column_default)
                     AND pg_get_serial_sequence('public.platform_data_protection_keys','id')='public.platform_data_protection_keys_id_seq'
                     AND (SELECT COUNT(*) FROM pg_constraint c
                       WHERE c.conrelid=to_regclass('public.platform_data_protection_keys'))=3
                     AND EXISTS (SELECT 1 FROM pg_constraint c
                       WHERE c.conrelid=to_regclass('public.platform_data_protection_keys') AND c.contype='p'
                         AND c.convalidated AND c.conkey=ARRAY[(SELECT attnum FROM pg_attribute
                           WHERE attrelid=to_regclass('public.platform_data_protection_keys') AND attname='id')]::smallint[])
                     AND EXISTS (SELECT 1 FROM pg_constraint c
                       WHERE c.conrelid=to_regclass('public.platform_data_protection_keys') AND c.contype='u'
                         AND c.convalidated AND c.conkey=ARRAY[(SELECT attnum FROM pg_attribute
                           WHERE attrelid=to_regclass('public.platform_data_protection_keys') AND attname='friendly_name')]::smallint[])
                     AND EXISTS (SELECT 1 FROM pg_constraint c
                       WHERE c.conrelid=to_regclass('public.platform_data_protection_keys') AND c.contype='c'
                         AND c.convalidated
                         AND regexp_replace(pg_get_constraintdef(c.oid,true),'\s+','','g')='CHECK(octet_length(xml_payload)<=1048576)')
                   ) END
                   AND (SELECT COUNT(*) FROM pg_policies WHERE schemaname='public' AND tablename='platform_data_protection_keys')=1
                   AND EXISTS(SELECT 1 FROM pg_policies WHERE schemaname='public' AND tablename='platform_data_protection_keys'
                     AND policyname='system_control_plane' AND roles='{opstrax_system}'::name[] AND cmd='ALL' AND qual='true' AND with_check='true')
                   AND NOT has_table_privilege('opstrax_app','platform_data_protection_keys','SELECT')
                   AND NOT has_table_privilege('opstrax_app','platform_data_protection_keys','INSERT')
                   AND NOT has_table_privilege('opstrax_app','platform_data_protection_keys','UPDATE')
                   AND NOT has_table_privilege('opstrax_app','platform_data_protection_keys','DELETE')
                   AND has_table_privilege('opstrax_system','platform_data_protection_keys','SELECT')
                   AND has_table_privilege('opstrax_system','platform_data_protection_keys','INSERT')
                   AND NOT has_table_privilege('opstrax_system','platform_data_protection_keys','UPDATE')
                   AND NOT has_table_privilege('opstrax_system','platform_data_protection_keys','DELETE')
                   AND NOT has_sequence_privilege('opstrax_app','platform_data_protection_keys_id_seq','USAGE')
                   AND NOT has_sequence_privilege('opstrax_app','platform_data_protection_keys_id_seq','SELECT')
                   AND NOT has_sequence_privilege('opstrax_app','platform_data_protection_keys_id_seq','UPDATE')
                   AND has_sequence_privilege('opstrax_system','platform_data_protection_keys_id_seq','USAGE')
                   AND has_sequence_privilege('opstrax_system','platform_data_protection_keys_id_seq','SELECT')
                   AND NOT has_sequence_privilege('opstrax_system','platform_data_protection_keys_id_seq','UPDATE') AS restricted
          FROM pg_roles WHERE rolname IN ('opstrax_app','opstrax_system')
        )
        SELECT
          COALESCE((SELECT restricted FROM role_state),false) AS role_restricted,
          COUNT(*) FILTER (WHERE oid IS NULL)::int AS missing_tables,
          COUNT(*) FILTER (WHERE tenant_scoped AND oid IS NOT NULL AND
            (NOT relrowsecurity OR NOT relforcerowsecurity
             OR tenant_col IS NULL
             OR (SELECT COUNT(*) FROM pg_policies p
                 WHERE p.schemaname='public' AND p.tablename=objects.name)<>2
             OR NOT EXISTS (SELECT 1 FROM pg_policies p WHERE p.schemaname='public' AND p.tablename=objects.name
                AND p.policyname='tenant_ticket_app' AND p.permissive='PERMISSIVE' AND p.roles='{opstrax_app}'::name[] AND p.cmd='ALL'
                AND p.qual LIKE '%'||objects.tenant_col||'%opstrax_security.current_tenant_id()%'
                AND p.qual LIKE '%SELECT%'
                AND p.with_check=p.qual)
             OR NOT EXISTS (SELECT 1 FROM pg_policies p WHERE p.schemaname='public' AND p.tablename=objects.name
                AND p.policyname='system_control_plane' AND p.permissive='PERMISSIVE' AND p.roles='{opstrax_system}'::name[] AND p.cmd='ALL'
                AND p.qual='true'
                AND p.with_check=p.qual)))::int AS rls_violations,
          COUNT(*) FILTER (WHERE oid IS NOT NULL AND
            ((tenant_scoped AND (((objects.name<>'eld_devices' AND NOT has_table_privilege('opstrax_app',oid,'SELECT'))
               OR (objects.name='eld_devices' AND (
                 NOT has_column_privilege('opstrax_app',oid,'device_serial','SELECT')
                 OR has_column_privilege('opstrax_app',oid,'api_key_hash','SELECT')
                 OR has_column_privilege('opstrax_app',oid,'api_key_previous_hash','SELECT')
                 OR has_column_privilege('opstrax_app',oid,'hmac_secret','SELECT')
                 OR has_column_privilege('opstrax_app',oid,'hmac_secret_encrypted','SELECT')
                 OR has_column_privilege('opstrax_app',oid,'hmac_previous_secret_encrypted','SELECT'))))
               OR has_table_privilege('opstrax_app',oid,'TRUNCATE')
               OR has_table_privilege('opstrax_app',oid,'REFERENCES')
               OR has_table_privilege('opstrax_app',oid,'TRIGGER')
               OR (EXISTS (SELECT 1 FROM tenant_privileges expected WHERE expected.name=objects.name)
                 AND EXISTS (SELECT 1 FROM tenant_privileges expected WHERE expected.name=objects.name AND (
                   has_table_privilege('opstrax_app',oid,'INSERT')<>expected.allow_insert
                   OR has_table_privilege('opstrax_app',oid,'UPDATE')<>expected.allow_update
                   OR has_table_privilege('opstrax_app',oid,'DELETE')<>expected.allow_delete)))
               OR (NOT EXISTS (SELECT 1 FROM tenant_privileges expected WHERE expected.name=objects.name)
                 AND (NOT has_table_privilege('opstrax_app',oid,'INSERT')
                   OR NOT has_table_privilege('opstrax_app',oid,'UPDATE')
                   OR NOT has_table_privilege('opstrax_app',oid,'DELETE')))))
             OR (EXISTS (SELECT 1 FROM reference_tables x WHERE x.name=objects.name)
               AND (NOT has_table_privilege('opstrax_app',oid,'SELECT')
                 OR has_table_privilege('opstrax_app',oid,'INSERT')
                 OR has_table_privilege('opstrax_app',oid,'UPDATE')
                 OR has_table_privilege('opstrax_app',oid,'DELETE')
                 OR has_table_privilege('opstrax_app',oid,'TRUNCATE')
                 OR has_table_privilege('opstrax_app',oid,'REFERENCES')
                 OR has_table_privilege('opstrax_app',oid,'TRIGGER')))
             OR (EXISTS (SELECT 1 FROM runtime_global x WHERE x.name=objects.name)
               AND (has_table_privilege('opstrax_app',oid,'SELECT')
                 OR has_table_privilege('opstrax_app',oid,'INSERT')
                 OR has_table_privilege('opstrax_app',oid,'UPDATE')
                 OR has_table_privilege('opstrax_app',oid,'DELETE')
                 OR NOT has_table_privilege('opstrax_system',oid,'SELECT')
                 OR NOT has_table_privilege('opstrax_system',oid,'INSERT')
                 OR NOT has_table_privilege('opstrax_system',oid,'UPDATE')
                 OR NOT has_table_privilege('opstrax_system',oid,'DELETE')))))::int AS grant_violations,
          ((SELECT COUNT(*) FROM tenant_scope scope WHERE
            NOT scope.relrowsecurity OR NOT scope.relforcerowsecurity
            OR (SELECT COUNT(*) FROM pg_policies p
                WHERE p.schemaname='public' AND p.tablename=scope.name)<>2
            OR NOT EXISTS (SELECT 1 FROM pg_policies p WHERE p.schemaname='public' AND p.tablename=scope.name
              AND p.policyname='tenant_ticket_app' AND p.permissive='PERMISSIVE' AND p.roles='{opstrax_app}'::name[] AND p.cmd='ALL'
              AND p.qual LIKE '%'||scope.tenant_col||'%opstrax_security.current_tenant_id()%'
              AND p.qual LIKE '%SELECT%'
              AND p.with_check=p.qual)
            OR NOT EXISTS (SELECT 1 FROM pg_policies p WHERE p.schemaname='public' AND p.tablename=scope.name
              AND p.policyname='system_control_plane' AND p.permissive='PERMISSIVE' AND p.roles='{opstrax_system}'::name[] AND p.cmd='ALL'
              AND p.qual='true'
              AND p.with_check=p.qual))
           +
           (SELECT COUNT(*) FROM (VALUES
             ('roles',5),('report_catalog',5),('role_permissions',5),
             ('user_mfa_status',2),('user_locale_preferences',2)
           ) expected(table_name,policy_count)
           WHERE (SELECT COUNT(*) FROM pg_policies p
                  WHERE p.schemaname='public' AND p.tablename=expected.table_name)<>expected.policy_count)
           +
           (SELECT COUNT(*) FROM (VALUES
             ('roles','roles_app_select','SELECT','opstrax_app'),('roles','roles_app_insert','INSERT','opstrax_app'),
             ('roles','roles_app_update','UPDATE','opstrax_app'),('roles','roles_app_delete','DELETE','opstrax_app'),('roles','system_control_plane','ALL','opstrax_system'),
             ('report_catalog','report_catalog_app_select','SELECT','opstrax_app'),('report_catalog','report_catalog_app_insert','INSERT','opstrax_app'),
             ('report_catalog','report_catalog_app_update','UPDATE','opstrax_app'),('report_catalog','report_catalog_app_delete','DELETE','opstrax_app'),('report_catalog','system_control_plane','ALL','opstrax_system'),
             ('role_permissions','role_permissions_app_select','SELECT','opstrax_app'),('role_permissions','role_permissions_app_insert','INSERT','opstrax_app'),
             ('role_permissions','role_permissions_app_update','UPDATE','opstrax_app'),('role_permissions','role_permissions_app_delete','DELETE','opstrax_app'),('role_permissions','system_control_plane','ALL','opstrax_system'),
             ('user_mfa_status','tenant_ticket_app','ALL','opstrax_app'),('user_mfa_status','system_control_plane','ALL','opstrax_system'),
             ('user_locale_preferences','tenant_ticket_app','ALL','opstrax_app'),('user_locale_preferences','system_control_plane','ALL','opstrax_system')
           ) expected(table_name,policy_name,command_name,role_name)
           WHERE NOT EXISTS(SELECT 1 FROM pg_policies p WHERE p.schemaname='public'
             AND p.tablename=expected.table_name AND p.policyname=expected.policy_name
             AND p.cmd=expected.command_name AND p.roles=ARRAY[expected.role_name]::name[]))
           +
           (SELECT COUNT(*) FROM pg_policies p
            WHERE p.schemaname='public'
              AND p.tablename IN ('roles','report_catalog','role_permissions','user_mfa_status','user_locale_preferences')
              AND (p.roles='{public}'::name[] OR COALESCE(p.qual,'') LIKE '%current_setting%'
                OR COALESCE(p.with_check,'') LIKE '%current_setting%'
                OR (p.roles='{opstrax_app}'::name[]
                  AND COALESCE(p.qual,p.with_check,'') NOT LIKE '%opstrax_security.current_tenant_id()%')))
           + CASE WHEN opstrax_security.special_policy_contract_valid() THEN 0 ELSE 1 END
           + CASE WHEN opstrax_security.generic_policy_contract_valid() THEN 0 ELSE 1 END)::int
            AS tenant_coverage_violations,
          ((SELECT COUNT(*) FROM tenant_scope scope
            LEFT JOIN tenant_privileges expected ON expected.name=scope.name
            WHERE ((scope.name<>'eld_devices' AND NOT has_table_privilege('opstrax_app',scope.oid,'SELECT'))
              OR (scope.name='eld_devices' AND (
                NOT has_column_privilege('opstrax_app',scope.oid,'device_serial','SELECT')
                OR has_column_privilege('opstrax_app',scope.oid,'api_key_hash','SELECT')
                OR has_column_privilege('opstrax_app',scope.oid,'api_key_previous_hash','SELECT')
                OR has_column_privilege('opstrax_app',scope.oid,'hmac_secret','SELECT')
                OR has_column_privilege('opstrax_app',scope.oid,'hmac_secret_encrypted','SELECT')
                OR has_column_privilege('opstrax_app',scope.oid,'hmac_previous_secret_encrypted','SELECT'))))
              OR has_table_privilege('opstrax_app',scope.oid,'TRUNCATE')
              OR has_table_privilege('opstrax_app',scope.oid,'REFERENCES')
              OR has_table_privilege('opstrax_app',scope.oid,'TRIGGER')
              OR NOT has_table_privilege('opstrax_system',scope.oid,'SELECT')
              OR NOT has_table_privilege('opstrax_system',scope.oid,'INSERT')
              OR NOT has_table_privilege('opstrax_system',scope.oid,'UPDATE')
              OR NOT has_table_privilege('opstrax_system',scope.oid,'DELETE')
              OR has_table_privilege('opstrax_system',scope.oid,'TRUNCATE')
              OR has_table_privilege('opstrax_system',scope.oid,'REFERENCES')
              OR has_table_privilege('opstrax_system',scope.oid,'TRIGGER')
              OR (expected.name IS NULL AND (
                NOT has_table_privilege('opstrax_app',scope.oid,'INSERT')
                OR NOT has_table_privilege('opstrax_app',scope.oid,'UPDATE')
                OR NOT has_table_privilege('opstrax_app',scope.oid,'DELETE')))
              OR (expected.name IS NOT NULL AND (
                has_table_privilege('opstrax_app',scope.oid,'INSERT')<>expected.allow_insert
                OR has_table_privilege('opstrax_app',scope.oid,'UPDATE')<>expected.allow_update
                OR has_table_privilege('opstrax_app',scope.oid,'DELETE')<>expected.allow_delete)))
           +
           (SELECT COUNT(*) FROM tenant_scope scope
            JOIN pg_depend dep ON dep.refobjid=scope.oid AND dep.refobjsubid>0 AND dep.deptype IN ('a','i')
            JOIN pg_class seq ON seq.oid=dep.objid AND seq.relkind='S'
            WHERE has_sequence_privilege('opstrax_app',seq.oid,'USAGE')<>
                    has_table_privilege('opstrax_app',scope.oid,'INSERT')
               OR has_sequence_privilege('opstrax_app',seq.oid,'SELECT')<>
                    has_table_privilege('opstrax_app',scope.oid,'INSERT')
               OR has_sequence_privilege('opstrax_app',seq.oid,'UPDATE')
               OR NOT has_sequence_privilege('opstrax_system',seq.oid,'USAGE')
               OR NOT has_sequence_privilege('opstrax_system',seq.oid,'SELECT')
               OR has_sequence_privilege('opstrax_system',seq.oid,'UPDATE'))
           +
           (SELECT COUNT(*) FROM (VALUES
              ('roles'),('report_catalog'),('role_permissions'),('user_mfa_status'),('user_locale_preferences')
            ) expected(table_name)
            WHERE NOT has_table_privilege('opstrax_app','public.'||expected.table_name,'SELECT')
              OR NOT has_table_privilege('opstrax_app','public.'||expected.table_name,'INSERT')
              OR NOT has_table_privilege('opstrax_app','public.'||expected.table_name,'UPDATE')
              OR NOT has_table_privilege('opstrax_app','public.'||expected.table_name,'DELETE')
              OR has_table_privilege('opstrax_app','public.'||expected.table_name,'TRUNCATE,REFERENCES,TRIGGER')
              OR NOT has_table_privilege('opstrax_system','public.'||expected.table_name,'SELECT')
              OR NOT has_table_privilege('opstrax_system','public.'||expected.table_name,'INSERT')
              OR NOT has_table_privilege('opstrax_system','public.'||expected.table_name,'UPDATE')
              OR NOT has_table_privilege('opstrax_system','public.'||expected.table_name,'DELETE')
              OR has_table_privilege('opstrax_system','public.'||expected.table_name,'TRUNCATE,REFERENCES,TRIGGER'))
           +
           (SELECT COUNT(*) FROM (VALUES
              ('roles_id_seq'),('report_catalog_id_seq'),('role_permissions_id_seq'),('user_locale_preferences_id_seq')
            ) expected(sequence_name)
            WHERE NOT has_sequence_privilege('opstrax_app','public.'||expected.sequence_name,'USAGE')
              OR NOT has_sequence_privilege('opstrax_app','public.'||expected.sequence_name,'SELECT')
              OR has_sequence_privilege('opstrax_app','public.'||expected.sequence_name,'UPDATE')
              OR NOT has_sequence_privilege('opstrax_system','public.'||expected.sequence_name,'USAGE')
              OR NOT has_sequence_privilege('opstrax_system','public.'||expected.sequence_name,'SELECT')
              OR has_sequence_privilege('opstrax_system','public.'||expected.sequence_name,'UPDATE')))::int AS tenant_grant_violations,
          (SELECT COUNT(*)::int
            FROM pg_default_acl defaults
            JOIN pg_namespace default_ns ON default_ns.oid=defaults.defaclnamespace AND default_ns.nspname='public'
            CROSS JOIN LATERAL aclexplode(defaults.defaclacl) default_acl
            JOIN pg_roles default_grantee ON default_grantee.oid=default_acl.grantee
            WHERE default_grantee.rolname IN ('opstrax_app','opstrax_system')
              AND defaults.defaclobjtype IN ('r','S')) AS default_privilege_violations,
          (SELECT COUNT(*)::int FROM runtime_route_columns expected
            LEFT JOIN pg_class route_table ON route_table.oid=to_regclass('public.'||expected.table_name)
            LEFT JOIN pg_attribute actual ON actual.attrelid=route_table.oid AND actual.attname=expected.column_name
              AND actual.attnum>0 AND NOT actual.attisdropped
            LEFT JOIN pg_attrdef route_default ON route_default.adrelid=route_table.oid AND route_default.adnum=actual.attnum
            WHERE actual.attname IS NULL
              OR format_type(actual.atttypid,actual.atttypmod)<>expected.data_type
              OR actual.attnotnull<>expected.not_null
              OR COALESCE(pg_get_expr(route_default.adbin,route_default.adrelid),'')<>expected.column_default
              OR actual.attidentity::text<>expected.identity_kind) AS runtime_route_column_violations,
          ((SELECT COUNT(*) FROM (VALUES
              ('ck_lvp_confidence_range','confidence IS NULL OR confidence >= 0::numeric AND confidence <= 1::numeric'),
              ('ck_lvp_trust_score_range','trust_score IS NULL OR trust_score >= 0::numeric AND trust_score <= 1::numeric')
            ) expected(name,expression)
            LEFT JOIN pg_constraint c ON c.conname=expected.name AND c.conrelid=to_regclass('public.latest_vehicle_positions')
            WHERE c.oid IS NULL OR NOT c.convalidated OR pg_get_expr(c.conbin,c.conrelid,true)<>expected.expression)
            +
           (SELECT COUNT(*) FROM (VALUES
              ('idx_auth_decision_tenant_created','CREATE INDEX idx_auth_decision_tenant_created ON public.authorization_decision_logs USING btree (tenant_id, created_at DESC)'),
              ('ix_b5_carriers','CREATE INDEX ix_b5_carriers ON public.carriers USING btree (company_id, status, compliance_status, risk_score)'),
              ('idx_lvp_company_correlation','CREATE INDEX idx_lvp_company_correlation ON public.latest_vehicle_positions USING btree (company_id, correlation_id) WHERE (correlation_id IS NOT NULL)'),
              ('idx_le_received','CREATE INDEX idx_le_received ON public.location_events USING btree (company_id, received_at)')
            ) expected(name,definition)
            LEFT JOIN pg_class idx ON idx.oid=to_regclass('public.'||expected.name)
            LEFT JOIN pg_index i ON i.indexrelid=idx.oid
            WHERE idx.oid IS NULL OR i.indisunique OR NOT i.indisvalid OR NOT i.indisready
              OR pg_get_indexdef(idx.oid)<>expected.definition))::int AS runtime_route_object_violations,
          (SELECT COUNT(*)::int FROM fleet_integrity_indexes expected
            LEFT JOIN pg_class idx ON idx.oid=to_regclass('public.'||expected.name)
            LEFT JOIN pg_index i ON i.indexrelid=idx.oid
            WHERE idx.oid IS NULL OR NOT i.indisunique OR NOT i.indisvalid OR NOT i.indisready
              OR i.indrelid<>to_regclass('public.'||expected.table_name)
              OR i.indnkeyatts<>expected.key_count OR i.indnatts<>expected.key_count
              OR pg_get_indexdef(i.indexrelid,1,true)<>expected.key1
              OR pg_get_indexdef(i.indexrelid,2,true)<>expected.key2
              OR (expected.key_count=3 AND pg_get_indexdef(i.indexrelid,3,true)<>expected.key3)
              OR COALESCE(pg_get_expr(i.indpred,i.indrelid),'')<>expected.predicate) AS fleet_integrity_object_violations,
          ((SELECT COUNT(*) FROM workforce_columns expected
            LEFT JOIN pg_class workforce_table ON workforce_table.oid=to_regclass('public.workforce_schedules')
            LEFT JOIN pg_attribute actual ON actual.attrelid=workforce_table.oid
              AND actual.attname=expected.column_name AND actual.attnum>0 AND NOT actual.attisdropped
            LEFT JOIN pg_attrdef workforce_default ON workforce_default.adrelid=workforce_table.oid
              AND workforce_default.adnum=actual.attnum
            WHERE actual.attname IS NULL
              OR format_type(actual.atttypid,actual.atttypmod)<>expected.data_type
              OR actual.attnotnull<>expected.not_null
              OR COALESCE(pg_get_expr(workforce_default.adbin,workforce_default.adrelid),'')<>expected.column_default
              OR actual.attidentity::text<>expected.identity_kind)
           +
           (SELECT COUNT(*) FROM (VALUES
              ('uq_drivers_company_id_id','drivers',true,2,'company_id','id',''),
              ('uq_workforce_schedules_tenant_driver_week','workforce_schedules',true,3,'company_id','driver_id','week_start'),
              ('idx_workforce_schedules_tenant_week','workforce_schedules',false,2,'company_id','week_start','')
            ) expected(name,table_name,is_unique,key_count,key1,key2,key3)
            LEFT JOIN pg_class idx ON idx.oid=to_regclass('public.'||expected.name)
            LEFT JOIN pg_index i ON i.indexrelid=idx.oid
            WHERE idx.oid IS NULL OR i.indisunique<>expected.is_unique OR NOT i.indisvalid OR NOT i.indisready
              OR i.indrelid<>to_regclass('public.'||expected.table_name)
              OR i.indnkeyatts<>expected.key_count OR i.indnatts<>expected.key_count
              OR pg_get_indexdef(i.indexrelid,1,true)<>expected.key1
              OR pg_get_indexdef(i.indexrelid,2,true)<>expected.key2
              OR (expected.key_count=3 AND pg_get_indexdef(i.indexrelid,3,true)<>expected.key3)
              OR i.indpred IS NOT NULL)
           +
           (SELECT COUNT(*) FROM (VALUES
              ('workforce_schedules_pkey','PRIMARY KEY (id)'),
              ('fk_workforce_schedules_tenant_driver','FOREIGN KEY (company_id, driver_id) REFERENCES drivers(company_id, id)')
            ) expected(name,definition)
            LEFT JOIN pg_constraint c ON c.conname=expected.name
              AND c.conrelid=to_regclass('public.workforce_schedules')
            WHERE c.oid IS NULL OR NOT c.convalidated OR pg_get_constraintdef(c.oid)<>expected.definition)
           +
           (SELECT COUNT(*) FROM workforce_schedules ws
            LEFT JOIN drivers d ON d.id=ws.driver_id
            WHERE d.id IS NULL OR ws.company_id IS DISTINCT FROM d.company_id
              OR ws.branch_id IS DISTINCT FROM d.branch_id))::int AS workforce_contract_violations,
          COALESCE((SELECT COUNT(*)=1 FROM schema_migrations WHERE version='2026_07_30_stage50_fleet_production_contract'),false) AS migration_applied,
          COALESCE((SELECT COUNT(*)=1 FROM schema_migrations WHERE version='2026_07_30_stage51_production_runtime_support'),false) AS runtime_support_migration_applied,
          COALESCE((SELECT COUNT(*)=1 FROM schema_migrations WHERE version='2026_07_30_stage53_tenant_rls_reconciliation'),false) AS tenant_coverage_migration_applied,
          COALESCE((SELECT COUNT(*)=1 FROM schema_migrations WHERE version='2026_07_30_stage54_cold_chain_device_integrity'),false) AS cold_chain_integrity_migration_applied,
          COALESCE((SELECT COUNT(*)=1 FROM schema_migrations WHERE version='2026_07_30_stage55_fleet_runtime_route_contract'),false) AS runtime_route_migration_applied,
          COALESCE((SELECT COUNT(*)=1 FROM schema_migrations WHERE version='2026_07_30_stage56_asset_type_integrity'),false) AS asset_type_integrity_migration_applied,
          COALESCE((SELECT COUNT(*)=1 FROM schema_migrations WHERE version='2026_07_30_stage57_workforce_schedule_tenant_integrity'),false) AS workforce_schedule_integrity_migration_applied,
          COALESCE((SELECT COUNT(*)=1 FROM schema_migrations WHERE version='2026_07_31_stage58_nonforgeable_tenant_ticket'),false) AS tenant_ticket_migration_applied,
          COALESCE((SELECT COUNT(*)=1 FROM schema_migrations WHERE version='2026_07_31_stage59_data_protection_key_ring'),false) AS data_protection_key_ring_migration_applied,
          COALESCE((SELECT COUNT(*)=2 FROM market_packs WHERE code IN ('canada_na','saudi_gcc') AND status='active'),false) AS market_catalog_ready,
          to_regclass('public.uq_ftms_shipment_identity') IS NOT NULL
            AND to_regclass('public.uq_ftms_vehicle_identity') IS NOT NULL
            AND to_regclass('public.ux_ftms_assets_branch_tag_normalized') IS NOT NULL
            AND to_regclass('public.uq_ftms_ccpolicy_branch_idem') IS NOT NULL
            AND COALESCE((
              SELECT COUNT(*)=5 AND BOOL_AND(
                i.indisunique AND i.indisvalid AND i.indisready
                AND i.indnkeyatts=2 AND i.indnatts=2
                AND i.indrelid=to_regclass('public.' || expected.table_name)
                AND pg_get_indexdef(i.indexrelid,1,true)=expected.key1
                AND pg_get_indexdef(i.indexrelid,2,true)=expected.key2
                AND COALESCE(pg_get_expr(i.indpred,i.indrelid,true),'')=expected.predicate)
              FROM identity_indexes expected
              JOIN pg_class idx ON idx.oid=to_regclass('public.' || expected.name)
              JOIN pg_index i ON i.indexrelid=idx.oid
            ),false)
            AND COALESCE((SELECT COUNT(*)=1 FROM schema_migrations
              WHERE version='2026_07_30_stage52_fleet_identity_uniqueness'),false)
            AND NOT EXISTS (
              SELECT 1 FROM drivers
              WHERE deleted_at IS NULL
                AND license_number LIKE 'enc:%'
                AND NULLIF(BTRIM(license_number_bidx),'') IS NULL
            ) AS indexes_ready,
          (SELECT COUNT(*)::int
             FROM critical_workers expected
             LEFT JOIN service_heartbeats heartbeat ON heartbeat.service_name=expected.service_name
            WHERE heartbeat.service_name IS NULL
               OR heartbeat.consecutive_failures >= CASE
                    WHEN expected.service_name='RetentionEnforcementService' THEN 1 ELSE 3 END
               OR heartbeat.last_heartbeat_at < NOW()-INTERVAL '10 minutes'
               OR heartbeat.last_heartbeat_at < @processStartedAt) AS critical_worker_violations,
          (SELECT COUNT(*)::int
             FROM critical_workers expected
             LEFT JOIN service_heartbeats heartbeat ON heartbeat.service_name=expected.service_name
            WHERE heartbeat.service_name IS NULL) AS missing_critical_workers,
          (SELECT COUNT(*)::int
             FROM critical_workers expected
             JOIN service_heartbeats heartbeat ON heartbeat.service_name=expected.service_name
            WHERE heartbeat.last_heartbeat_at < NOW()-INTERVAL '10 minutes'
               OR heartbeat.last_heartbeat_at < @processStartedAt) AS stale_critical_workers,
          (SELECT COUNT(*)::int
             FROM critical_workers expected
             JOIN service_heartbeats heartbeat ON heartbeat.service_name=expected.service_name
            WHERE heartbeat.consecutive_failures >= CASE
                    WHEN expected.service_name='RetentionEnforcementService' THEN 1 ELSE 3 END) AS failed_critical_workers
        FROM objects
        """;
}

public sealed record FleetProductionContractResult(
    bool RoleRestricted,
    int MissingTables,
    int RlsViolations,
    int GrantViolations,
    int TenantCoverageViolations,
    int TenantGrantViolations,
    int DefaultPrivilegeViolations,
    int RuntimeRouteColumnViolations,
    int RuntimeRouteObjectViolations,
    int FleetIntegrityObjectViolations,
    int WorkforceContractViolations,
    bool MigrationApplied,
    bool RuntimeSupportMigrationApplied,
    bool TenantCoverageMigrationApplied,
    bool ColdChainIntegrityMigrationApplied,
    bool RuntimeRouteMigrationApplied,
    bool AssetTypeIntegrityMigrationApplied,
    bool WorkforceScheduleIntegrityMigrationApplied,
    bool TenantTicketMigrationApplied,
    bool DataProtectionKeyRingMigrationApplied,
    bool MarketCatalogReady,
    bool IndexesReady,
    int CriticalWorkerViolations,
    int RawCriticalWorkerViolations,
    int MissingCriticalWorkers,
    int StaleCriticalWorkers,
    int FailedCriticalWorkers,
    bool CriticalWorkerStartupGraceActive,
    int CriticalWorkerStartupGraceRemainingSeconds,
    string? FailureCode)
{
    public bool Ready => RoleRestricted && MissingTables == 0 && RlsViolations == 0 && GrantViolations == 0
                         && TenantCoverageViolations == 0 && TenantGrantViolations == 0 && DefaultPrivilegeViolations == 0
                         && RuntimeRouteColumnViolations == 0 && RuntimeRouteObjectViolations == 0 && FleetIntegrityObjectViolations == 0
                         && WorkforceContractViolations == 0
                         && MigrationApplied && RuntimeSupportMigrationApplied && TenantCoverageMigrationApplied
                         && ColdChainIntegrityMigrationApplied && RuntimeRouteMigrationApplied && AssetTypeIntegrityMigrationApplied
                         && WorkforceScheduleIntegrityMigrationApplied && TenantTicketMigrationApplied && DataProtectionKeyRingMigrationApplied
                         && MarketCatalogReady && IndexesReady
                         && CriticalWorkerViolations == 0 && FailureCode is null;

    public static FleetProductionContractResult Failed(string code) =>
        new(false, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, false, false, false, false, false, false, false, false, false, false, false, -1, -1, -1, -1, -1, false, 0, code);
}
