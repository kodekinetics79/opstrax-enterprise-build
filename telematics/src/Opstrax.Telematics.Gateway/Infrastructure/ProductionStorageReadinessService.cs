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
            SELECT concat_ws(',',
                CASE WHEN to_regclass('eld_devices') IS NULL THEN 'eld_devices' END,
                CASE WHEN to_regclass('telematics_device_trust_policy') IS NULL THEN 'telematics_device_trust_policy' END,
                CASE WHEN NOT EXISTS (
                    SELECT 1 FROM information_schema.columns
                     WHERE table_schema=current_schema() AND table_name='eld_devices'
                       AND column_name='hmac_secret_encrypted') THEN 'eld_devices.hmac_secret_encrypted' END,
                CASE WHEN NOT EXISTS (
                    SELECT 1 FROM information_schema.columns
                     WHERE table_schema=current_schema() AND table_name='eld_devices'
                       AND column_name='hmac_key_version') THEN 'eld_devices.hmac_key_version' END)
            """,
            "platform registry",
            cancellationToken);

    private static Task RequireTelematicsLedgersAsync(string connectionString, CancellationToken cancellationToken) =>
        RequireAsync(
            connectionString,
            """
            SELECT concat_ws(',',
                CASE WHEN to_regclass('telemetry_replay_seen') IS NULL THEN 'telemetry_replay_seen' END,
                CASE WHEN to_regclass('telemetry_projection_inbox') IS NULL THEN 'telemetry_projection_inbox' END,
                CASE WHEN to_regclass('latest_vehicle_positions') IS NULL THEN 'latest_vehicle_positions' END,
                CASE WHEN to_regclass('canonical_telemetry_events') IS NULL THEN 'canonical_telemetry_events' END,
                CASE WHEN to_regclass('telemetry_store_forward') IS NULL THEN 'telemetry_store_forward' END,
                CASE WHEN to_regclass('telemetry_gateway_rejections') IS NULL THEN 'telemetry_gateway_rejections' END)
            """,
            "telematics ledger",
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
}
