using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Npgsql;
using NpgsqlTypes;
using Opstrax.Telematics.Contracts.Identity;
using Opstrax.Telematics.Contracts.Lifecycle;

namespace Opstrax.Telematics.Gateway.Identity;

/// <summary>
/// Production device registry backed by the main OpsTrax platform database. The supplied
/// connection must be the platform system identity because resolution happens before a tenant is
/// known and <c>eld_devices</c> is RLS-protected. This implementation deliberately requires the
/// existing <c>eld_devices</c> + <c>telematics_device_trust_policy</c> contract; it never invents
/// ownership or a default trust policy when either record is missing.
/// </summary>
internal sealed class PostgresDeviceRegistry(string platformSystemConnectionString) : IDeviceRegistry
{
    private readonly string _connectionString = string.IsNullOrWhiteSpace(platformSystemConnectionString)
        ? throw new ArgumentException("A platform system connection string is required.", nameof(platformSystemConnectionString))
        : platformSystemConnectionString;

    public async ValueTask<ResolvedDeviceOwner?> ResolveAsync(
        DeviceIdentityRef identity,
        CancellationToken cancellationToken = default) =>
        (await ResolveTrustAsync(identity, cancellationToken).ConfigureAwait(false))?.Owner;

    public async ValueTask<ResolvedDeviceTrust?> ResolveTrustAsync(
        DeviceIdentityRef identity,
        CancellationToken cancellationToken = default)
    {
        if (!identity.HasAnyIdentifier)
            return null;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // Resolution necessarily precedes tenant discovery. Use the narrowly scoped platform-admin
        // RLS claim only for this transaction; the company returned by the registry remains the
        // authoritative scope copied onto every downstream event and write.
        await using (var scope = new NpgsqlCommand(
            "SELECT set_config('app.platform_admin','on',true)", connection, transaction))
        {
            await scope.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        // The LATERAL policy lookup accepts the canonical numeric fabric id and the historical
        // serial form, but returns at most one policy. More than one matching device row is
        // ambiguous and therefore rejected below rather than guessed.
        const string sql = """
            SELECT e.id, e.company_id, i.vehicle_id, e.status, e.device_state,
                   i.installation_match_count,
                   p.auth_mode, p.credential_kind, p.credential_handle,
                   p.pinned_source_cidrs, p.pinned_sim_iccid, p.pinned_imsi,
                   p.require_replay_defense
              FROM eld_devices e
              JOIN LATERAL (
                    SELECT di.vehicle_id, COUNT(*) OVER() installation_match_count
                      FROM device_installations di
                     WHERE di.device_id=e.id AND di.company_id=e.company_id
                       AND LOWER(BTRIM(di.status)) IN ('installed','verified')
                       AND di.effective_from<=NOW()
                       AND (di.effective_to IS NULL OR di.effective_to>NOW())
                     ORDER BY di.effective_from DESC,di.id DESC
                     LIMIT 1
              ) i ON TRUE
              JOIN LATERAL (
                    SELECT t.auth_mode, t.credential_kind, t.credential_handle,
                           t.pinned_source_cidrs, t.pinned_sim_iccid, t.pinned_imsi,
                           t.require_replay_defense
                      FROM telematics_device_trust_policy t
                     WHERE t.device_id IN (e.id::text, e.device_serial, 'eld:' || e.id::text)
                     ORDER BY CASE t.device_id WHEN e.id::text THEN 0 WHEN 'eld:' || e.id::text THEN 1 ELSE 2 END
                     LIMIT 1
              ) p ON TRUE
             WHERE e.deleted_at IS NULL
               AND e.company_id IS NOT NULL
               AND NOT EXISTS (
                    SELECT 1 FROM device_installation_quarantine q
                     WHERE q.company_id=e.company_id AND q.device_id=e.id AND q.resolved_at IS NULL)
               AND e.vehicle_id IS NOT DISTINCT FROM i.vehicle_id
               AND ((@imei IS NOT NULL AND e.imei=@imei)
                 OR (@serial IS NOT NULL AND e.device_serial=@serial)
                 OR (@device_id IS NOT NULL AND (e.id::text=@device_id OR e.device_serial=@device_id)))
             ORDER BY e.id
             LIMIT 2;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.Add(new NpgsqlParameter("imei", NpgsqlDbType.Text)
            { Value = (object?)NullIfWhiteSpace(identity.Imei) ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("serial", NpgsqlDbType.Text)
            { Value = (object?)NullIfWhiteSpace(identity.Serial) ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("device_id", NpgsqlDbType.Text)
            { Value = (object?)NullIfWhiteSpace(identity.DeviceId) ?? DBNull.Value });

        var rows = new List<RegistryRow>(2);
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                rows.Add(Read(reader));
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        if (rows.Count != 1)
            return null;

        RegistryRow row = rows[0];
        if (row.InstallationMatchCount != 1)
            return null;
        DeviceLifecycleState lifecycle = MapLifecycle(row.Status, row.DeviceState);
        if (lifecycle is DeviceLifecycleState.Suspended or DeviceLifecycleState.Retired or DeviceLifecycleState.Quarantined)
        {
            // Return the real held state so the authenticator emits the correct refusal reason;
            // do not erase a recognised device into an "unknown" rejection.
        }

        DeviceAuthMode authMode = ParseAuthMode(row.AuthMode);
        CredentialKind credentialKind = ParseCredentialKind(row.CredentialKind);
        if (authMode != DeviceAuthMode.ImeiAllowlistOnly &&
            (credentialKind == CredentialKind.None || string.IsNullOrWhiteSpace(row.CredentialHandle)))
            return null;
        if (authMode == DeviceAuthMode.ImeiAllowlistOnly && credentialKind != CredentialKind.None)
            return null;

        string deviceId = row.Id.ToString(CultureInfo.InvariantCulture);
        var owner = new ResolvedDeviceOwner(
            TenantIdForCompany(row.CompanyId),
            row.CompanyId,
            deviceId,
            row.VehicleId,
            lifecycle,
            row.CredentialHandle ?? string.Empty);
        var policy = new DeviceTrustPolicy(
            authMode,
            row.PinnedSourceCidrs,
            row.PinnedSimIccid,
            row.PinnedImsi,
            row.RequireReplayDefense);
        var credential = credentialKind == CredentialKind.None
            ? CredentialMaterial.None
            : new CredentialMaterial(credentialKind, row.CredentialHandle);
        return new ResolvedDeviceTrust(owner, policy, credential);
    }

    private static RegistryRow Read(NpgsqlDataReader reader) => new(
        reader.GetInt64(reader.GetOrdinal("id")),
        reader.GetInt64(reader.GetOrdinal("company_id")),
        reader.IsDBNull(reader.GetOrdinal("vehicle_id")) ? null : reader.GetInt64(reader.GetOrdinal("vehicle_id")),
        reader.GetInt64(reader.GetOrdinal("installation_match_count")),
        reader.GetString(reader.GetOrdinal("status")),
        reader.IsDBNull(reader.GetOrdinal("device_state")) ? null : reader.GetString(reader.GetOrdinal("device_state")),
        reader.GetString(reader.GetOrdinal("auth_mode")),
        reader.GetString(reader.GetOrdinal("credential_kind")),
        reader.IsDBNull(reader.GetOrdinal("credential_handle")) ? null : reader.GetString(reader.GetOrdinal("credential_handle")),
        reader.IsDBNull(reader.GetOrdinal("pinned_source_cidrs")) ? null : reader.GetFieldValue<string[]>(reader.GetOrdinal("pinned_source_cidrs")),
        reader.IsDBNull(reader.GetOrdinal("pinned_sim_iccid")) ? null : reader.GetString(reader.GetOrdinal("pinned_sim_iccid")),
        reader.IsDBNull(reader.GetOrdinal("pinned_imsi")) ? null : reader.GetString(reader.GetOrdinal("pinned_imsi")),
        reader.GetBoolean(reader.GetOrdinal("require_replay_defense")));

    private static DeviceLifecycleState MapLifecycle(string status, string? state)
    {
        if (status.Equals("Suspended", StringComparison.OrdinalIgnoreCase)) return DeviceLifecycleState.Suspended;
        if (status.Equals("Revoked", StringComparison.OrdinalIgnoreCase)) return DeviceLifecycleState.Retired;
        if (status.Equals("CredentialRotationRequired", StringComparison.OrdinalIgnoreCase)) return DeviceLifecycleState.Quarantined;

        return state?.Trim().ToLowerInvariant() switch
        {
            "provisioned" or "registered" => DeviceLifecycleState.Provisioned,
            "enrolled" => DeviceLifecycleState.AwaitingConfiguration,
            "activated" => DeviceLifecycleState.AwaitingFirstConnection,
            "online" => DeviceLifecycleState.Online,
            "idle" => DeviceLifecycleState.Delayed,
            "offline" => DeviceLifecycleState.Offline,
            "degraded" or "maintenance" or "faulty" => DeviceLifecycleState.Degraded,
            "suspended" => DeviceLifecycleState.Suspended,
            "quarantined" or "lost" => DeviceLifecycleState.Quarantined,
            "decommissioning" or "decommissioned" or "retired" => DeviceLifecycleState.Retired,
            _ => DeviceLifecycleState.AwaitingFirstConnection,
        };
    }

    private static DeviceAuthMode ParseAuthMode(string value) => value.Trim() switch
    {
        "PerDeviceHmac" => DeviceAuthMode.PerDeviceHmac,
        "MutualTls" => DeviceAuthMode.MutualTls,
        "ClientCertificate" => DeviceAuthMode.ClientCertificate,
        "ImeiAllowlistOnly" => DeviceAuthMode.ImeiAllowlistOnly,
        _ => throw new InvalidOperationException("Unknown device auth mode in registry."),
    };

    private static CredentialKind ParseCredentialKind(string value) => value.Trim() switch
    {
        "PreSharedHmacKey" => CredentialKind.PreSharedHmacKey,
        "ClientCertFingerprint" => CredentialKind.ClientCertFingerprint,
        "PublicKey" => CredentialKind.PublicKey,
        "None" => CredentialKind.None,
        _ => throw new InvalidOperationException("Unknown credential kind in registry."),
    };

    // The current platform's tenant boundary is company_id and has no tenant UUID column. The
    // canonical envelope nevertheless requires a UUID, so derive a stable namespaced identifier;
    // company_id remains the authoritative RLS scope used by every database write.
    private static Guid TenantIdForCompany(long companyId)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"opstrax-company-tenant:{companyId}"));
        Span<byte> guid = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(guid);
        guid[6] = (byte)((guid[6] & 0x0F) | 0x50);
        guid[8] = (byte)((guid[8] & 0x3F) | 0x80);
        return new Guid(guid);
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record RegistryRow(
        long Id,
        long CompanyId,
        long? VehicleId,
        long InstallationMatchCount,
        string Status,
        string? DeviceState,
        string AuthMode,
        string CredentialKind,
        string? CredentialHandle,
        IReadOnlyList<string>? PinnedSourceCidrs,
        string? PinnedSimIccid,
        string? PinnedImsi,
        bool RequireReplayDefense);
}
