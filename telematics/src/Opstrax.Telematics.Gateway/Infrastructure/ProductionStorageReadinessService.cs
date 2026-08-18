using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Opstrax.Telematics.Gateway.Infrastructure;

internal sealed record ProductionStorageReadinessOptions(
    string PlatformRegistryConnectionString,
    string TelematicsConnectionString);

/// <summary>
/// Refuses to open the device listener when the production registry or durable ledgers are absent.
/// This converts schema drift and misdirected credentials into a startup failure instead of an
/// ingest-time fallback to process memory.
/// </summary>
internal sealed class ProductionStorageReadinessService(
    ProductionStorageReadinessOptions options,
    ILogger<ProductionStorageReadinessService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await RequirePlatformRegistryAsync(options.PlatformRegistryConnectionString, cancellationToken)
            .ConfigureAwait(false);
        await RequireTelematicsLedgersAsync(options.TelematicsConnectionString, cancellationToken)
            .ConfigureAwait(false);
        logger.LogInformation("Production telematics registry and durable ledger readiness checks passed.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static Task RequirePlatformRegistryAsync(string connectionString, CancellationToken cancellationToken) =>
        RequireAsync(
            connectionString,
            """
            WITH required_tables(name) AS (VALUES
                ('eld_devices'),('telematics_device_trust_policy'),('device_installations')
            ), required_privileges(table_name,privilege) AS (VALUES
                ('eld_devices','SELECT'),
                ('telematics_device_trust_policy','SELECT'),
                ('device_installations','SELECT')
            ), missing AS (
                SELECT name AS item FROM required_tables WHERE to_regclass(name) IS NULL
                UNION ALL
                SELECT table_name || ':' || privilege
                  FROM required_privileges
                 WHERE to_regclass(table_name) IS NOT NULL
                   AND has_table_privilege(current_user,to_regclass(table_name),privilege) IS NOT TRUE
                UNION ALL
                SELECT 'owner:' || required.name
                  FROM required_tables required
                  JOIN pg_class object ON object.oid=to_regclass(required.name)
                 WHERE pg_has_role(current_user,object.relowner,'MEMBER')
                UNION ALL
                SELECT 'eld_devices.hmac_secret_encrypted'
                 WHERE NOT EXISTS (
                    SELECT 1 FROM information_schema.columns
                     WHERE table_schema=current_schema() AND table_name='eld_devices'
                       AND column_name='hmac_secret_encrypted')
                UNION ALL
                SELECT 'eld_devices.hmac_key_version'
                 WHERE NOT EXISTS (
                    SELECT 1 FROM information_schema.columns
                     WHERE table_schema=current_schema() AND table_name='eld_devices'
                       AND column_name='hmac_key_version')
                UNION ALL
                SELECT 'device_installations.' || required.column_name
                  FROM (VALUES ('effective_from'),('effective_to'),('status')) required(column_name)
                 WHERE NOT EXISTS (
                    SELECT 1 FROM information_schema.columns
                     WHERE table_schema=current_schema() AND table_name='device_installations'
                       AND column_name=required.column_name)
            )
            SELECT COALESCE(string_agg(item,',' ORDER BY item),'') FROM missing
            """,
            "platform registry",
            cancellationToken);

    private static Task RequireTelematicsLedgersAsync(string connectionString, CancellationToken cancellationToken) =>
        RequireAsync(
            connectionString,
            """
            WITH required_tables(name) AS (VALUES
                ('telemetry_replay_seen'),('telemetry_replay_device_state'),
                ('telemetry_projection_inbox'),
                ('canonical_telemetry_events'),('telemetry_store_forward'),
                ('telemetry_gateway_rejections'),('eld_devices'),('vehicles'),
                ('location_events'),('latest_vehicle_positions'),('telemetry_alerts'),
                ('telemetry_rules'),('geofences'),('device_installations'),
                ('dispatch_assignments')
            ), required_columns(table_name,column_name) AS (VALUES
                ('telemetry_replay_seen','unwrapped_serial'),
                ('telemetry_replay_seen','event_id'),
                ('telemetry_replay_device_state','last_raw_serial'),
                ('telemetry_replay_device_state','high_water_unwrapped'),
                ('canonical_telemetry_events','installation_id'),
                ('eld_devices','last_seen_at'),('eld_devices','last_heartbeat_at'),
                ('eld_devices','updated_at'),('location_events','device_id'),
                ('location_events','driver_id'),('location_events','engine_status'),
                ('location_events','fuel_level'),('location_events','odometer_miles'),
                ('location_events','source'),('location_events','source_channel'),
                ('location_events','idempotency_key'),('location_events','observed_at'),
                ('location_events','normalized_at'),('location_events','received_at'),
                ('location_events','installation_id'),('location_events','assignment_id'),
                ('location_events','trip_id'),
                ('device_installations','effective_from'),('device_installations','effective_to'),
                ('device_installations','status'),
                ('dispatch_assignments','assigned_at'),('dispatch_assignments','cancelled_at'),
                ('dispatch_assignments','completed_at'),('dispatch_assignments','actual_delivery_at'),
                ('dispatch_assignments','trip_id'),
                ('latest_vehicle_positions','source_event_id'),
                ('latest_vehicle_positions','installation_id'),
                ('latest_vehicle_positions','assignment_id'),('latest_vehicle_positions','trip_id'),
                ('latest_vehicle_positions','source_channel'),
                ('latest_vehicle_positions','telemetry_status'),
                ('latest_vehicle_positions','risk_level'),
                ('latest_vehicle_positions','device_fix_time'),
                ('telemetry_alerts','source_event_id'),
                ('telemetry_alerts','installation_id'),('telemetry_alerts','assignment_id'),
                ('telemetry_alerts','trip_id'),
                ('telemetry_alerts','source_channel'),
                ('telemetry_rules','threshold_value'),('geofences','branch_id'),
                ('geofences','polygon_json')
            ), required_privileges(table_name,privilege) AS (VALUES
                ('telemetry_replay_seen','SELECT'),('telemetry_replay_seen','INSERT'),
                ('telemetry_replay_device_state','SELECT'),
                ('telemetry_replay_device_state','INSERT'),
                ('telemetry_replay_device_state','UPDATE'),
                ('telemetry_projection_inbox','SELECT'),('telemetry_projection_inbox','INSERT'),
                ('canonical_telemetry_events','SELECT'),('canonical_telemetry_events','INSERT'),
                ('telemetry_store_forward','SELECT'),('telemetry_store_forward','INSERT'),
                ('telemetry_store_forward','UPDATE'),('telemetry_store_forward','DELETE'),
                ('telemetry_gateway_rejections','SELECT'),('telemetry_gateway_rejections','INSERT'),
                ('eld_devices','SELECT'),('eld_devices','UPDATE'),('vehicles','SELECT'),
                ('location_events','SELECT'),('location_events','INSERT'),
                ('latest_vehicle_positions','SELECT'),('latest_vehicle_positions','INSERT'),
                ('latest_vehicle_positions','UPDATE'),
                ('telemetry_alerts','SELECT'),('telemetry_alerts','INSERT'),
                ('telemetry_rules','SELECT'),('geofences','SELECT'),
                ('device_installations','SELECT'),('dispatch_assignments','SELECT')
            ), required_sequences(name) AS (VALUES
                ('telemetry_replay_seen_id_seq'),('canonical_telemetry_events_id_seq'),
                ('telemetry_store_forward_id_seq'),('telemetry_gateway_rejections_id_seq'),
                ('location_events_id_seq'),('latest_vehicle_positions_id_seq'),
                ('telemetry_alerts_id_seq')
            ), missing AS (
                SELECT name AS item FROM required_tables WHERE to_regclass(name) IS NULL
                UNION ALL
                SELECT table_name || '.' || column_name
                  FROM required_columns required
                 WHERE NOT EXISTS (
                       SELECT 1 FROM information_schema.columns actual
                        WHERE actual.table_schema=current_schema()
                          AND actual.table_name=required.table_name
                          AND actual.column_name=required.column_name)
                UNION ALL
                SELECT table_name || ':' || privilege
                  FROM required_privileges
                 WHERE to_regclass(table_name) IS NOT NULL
                   AND has_table_privilege(current_user,to_regclass(table_name),privilege) IS NOT TRUE
                UNION ALL
                SELECT 'sequence:' || name || ':USAGE'
                  FROM required_sequences
                 WHERE to_regclass(name) IS NULL
                    OR has_sequence_privilege(current_user,to_regclass(name),'USAGE') IS NOT TRUE
                UNION ALL
                SELECT 'owner:' || required.name
                  FROM required_tables required
                  JOIN pg_class object ON object.oid=to_regclass(required.name)
                 WHERE pg_has_role(current_user,object.relowner,'MEMBER')
                UNION ALL
                SELECT 'uq_telemetry_replay_seen_unwrapped'
                 WHERE to_regclass('uq_telemetry_replay_seen_unwrapped') IS NULL
                UNION ALL
                SELECT 'telemetry_replay_device_state:DELETE'
                 WHERE to_regclass('telemetry_replay_device_state') IS NOT NULL
                   AND has_table_privilege(
                     current_user,to_regclass('telemetry_replay_device_state'),'DELETE')
            )
            SELECT COALESCE(string_agg(item,',' ORDER BY item),'') FROM missing
            """,
            "telematics projection topology",
            cancellationToken);

    private static async Task RequireAsync(
        string connectionString,
        string probeSql,
        string component,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await RequireRuntimeIdentityAsync(connection, transaction, component, cancellationToken)
            .ConfigureAwait(false);
        await using (var scope = new NpgsqlCommand(
            "SELECT set_config('app.platform_admin','on',true)", connection, transaction))
        {
            await scope.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var probe = new NpgsqlCommand(probeSql, connection, transaction);
        string missing = (string?)await probe.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(missing))
            throw new InvalidOperationException($"Production {component} schema is not ready. Missing: {missing}.");

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task RequireRuntimeIdentityAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string component,
        CancellationToken cancellationToken)
    {
        const string identitySql = """
            SELECT concat_ws(',',
                CASE WHEN current_user<>'opstrax_system' THEN 'current_user' END,
                CASE WHEN session_user<>'opstrax_system' THEN 'session_user' END,
                CASE WHEN NOT EXISTS (
                    SELECT 1 FROM pg_roles role
                     WHERE role.rolname=current_user AND role.rolcanlogin
                       AND NOT role.rolsuper AND NOT role.rolbypassrls
                       AND NOT role.rolcreatedb AND NOT role.rolcreaterole
                       AND NOT role.rolinherit AND NOT role.rolreplication)
                     THEN 'role_attributes' END,
                CASE WHEN has_database_privilege(current_user,current_database(),'CREATE')
                       OR has_database_privilege(current_user,current_database(),'TEMPORARY')
                     THEN 'database_privileges' END,
                CASE WHEN has_schema_privilege(current_user,'public','CREATE')
                     THEN 'schema_create' END,
                CASE WHEN pg_has_role(current_user,
                    (SELECT database.datdba FROM pg_database database
                      WHERE database.datname=current_database()),'MEMBER')
                     THEN 'database_owner' END,
                CASE WHEN pg_has_role(current_user,
                    (SELECT namespace.nspowner FROM pg_namespace namespace
                      WHERE namespace.nspname='public'),'MEMBER')
                     THEN 'schema_owner' END,
                CASE WHEN EXISTS (
                    SELECT 1 FROM pg_roles elevated
                     WHERE elevated.rolname<>current_user
                       AND pg_has_role(current_user,elevated.oid,'MEMBER')
                       AND (elevated.rolsuper OR elevated.rolbypassrls OR elevated.rolcreatedb
                         OR elevated.rolcreaterole OR elevated.rolreplication))
                     THEN 'elevated_membership' END)
            """;
        await using var command = new NpgsqlCommand(identitySql, connection, transaction);
        string violations = (string?)await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)
            ?? "identity_probe";
        if (!string.IsNullOrWhiteSpace(violations))
            throw new InvalidOperationException(
                $"Production {component} runtime identity is unsafe. Violations: {violations}.");
    }
}
