using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Opstrax.Api.Data;
using Opstrax.Api.Security;

namespace Opstrax.Api.Services;

public sealed record DeviceConnectivityObservationEnvelope(
    string SourceProvider,
    string SourceAccountReference,
    string SourceObservationId,
    string Iccid,
    string SubscriptionStatus,
    string NetworkRegistrationStatus,
    string DataSessionStatus,
    long? UsageBytes,
    bool? Roaming,
    DateTimeOffset ObservedAtUtc);

public enum DeviceConnectivityObservationDisposition { Accepted, Replay }

public sealed record DeviceConnectivityObservationResult(
    long ObservationId,
    long DeviceId,
    long ConnectivityProfileId,
    DeviceConnectivityObservationDisposition Disposition,
    string SourceProvider,
    string SourceAuthenticationStatus,
    string SubscriptionStatus,
    string NetworkRegistrationStatus,
    string DataSessionStatus,
    DateTimeOffset ObservedAtUtc,
    bool ProviderVerifiedClaim,
    bool PhysicalConnectivityClaim,
    bool CertificationClaim);

public sealed class DeviceConnectivityObservationValidationException(string message) : ArgumentException(message);

/// <summary>
/// Records a response received through an authenticated provider adapter and binds
/// it to the exact current ICCID profile. The provider response remains a software
/// observation; it never becomes proof of RF attachment, telemetry or certification.
/// </summary>
public sealed partial class DeviceConnectivityObservationService(
    Database database,
    PiiProtectionService pii,
    TimeProvider clock,
    ILogger<DeviceConnectivityObservationService> logger)
{
    internal const int MaximumPayloadBytes = 1024 * 1024;

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,79}$", RegexOptions.CultureInvariant)]
    private static partial Regex ProviderPattern();

    [GeneratedRegex("^[0-9]{18,22}$", RegexOptions.CultureInvariant)]
    private static partial Regex IccidPattern();

    internal sealed record PreparedObservation(
        string SourceProvider,
        string SourceAccountReference,
        string SourceObservationId,
        string Iccid,
        string PayloadSha256,
        string SubscriptionStatus,
        string NetworkRegistrationStatus,
        string DataSessionStatus,
        long? UsageBytes,
        bool? Roaming,
        DateTime ObservedAtUtc,
        DateTime ReceivedAtUtc);

    internal static PreparedObservation Prepare(
        DeviceConnectivityObservationEnvelope envelope,
        ReadOnlyMemory<byte> authenticatedPayload,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (now.Offset != TimeSpan.Zero) throw Invalid("The intake clock must be UTC.");
        if (authenticatedPayload.IsEmpty || authenticatedPayload.Length > MaximumPayloadBytes)
            throw Invalid("The authenticated provider payload must contain 1 byte to 1 MiB.");

        var provider = Required(envelope.SourceProvider, 80, "source provider").ToLowerInvariant();
        if (!ProviderPattern().IsMatch(provider)) throw Invalid("The source provider is invalid.");
        var account = Required(envelope.SourceAccountReference, 240, "source account reference");
        var observation = Required(envelope.SourceObservationId, 240, "source observation ID");
        var iccid = Required(envelope.Iccid, 22, "ICCID");
        if (!IccidPattern().IsMatch(iccid)) throw Invalid("The ICCID must contain 18-22 digits.");

        var subscription = Choice(envelope.SubscriptionStatus,
            ["Unknown", "Active", "Suspended", "Deactivated"], "subscription status");
        var network = Choice(envelope.NetworkRegistrationStatus,
            ["Unknown", "Registered", "Roaming", "Denied", "Detached"], "network registration status");
        var session = Choice(envelope.DataSessionStatus,
            ["Unknown", "Attached", "Detached", "Blocked"], "data session status");
        if (envelope.UsageBytes is < 0) throw Invalid("Usage bytes cannot be negative.");

        var observed = Utc(envelope.ObservedAtUtc, "observation time");
        if (observed.Ticks % 10 != 0)
            throw Invalid("The observation time must have microsecond precision or coarser.");
        var received = now.UtcDateTime;
        if (observed > received.AddMinutes(5)) throw Invalid("The observation time is in the future.");

        return new(
            provider,
            account,
            observation,
            iccid,
            Convert.ToHexString(SHA256.HashData(authenticatedPayload.Span)).ToLowerInvariant(),
            subscription, network, session, envelope.UsageBytes, envelope.Roaming,
            observed, received);
    }

    public async Task<DeviceConnectivityObservationResult> RecordAuthenticatedAsync(
        long companyId,
        DeviceConnectivityObservationEnvelope envelope,
        ReadOnlyMemory<byte> authenticatedPayload,
        CancellationToken ct = default)
    {
        if (companyId <= 0) throw Invalid("The company ID is invalid.");
        if (!pii.Enabled) throw Invalid("PII blind-index protection is unavailable. No provider observation was stored.");
        var prepared = Prepare(envelope, authenticatedPayload, clock.GetUtcNow());
        var iccidBidx = pii.BlindIndex(prepared.Iccid)
            ?? throw Invalid("The ICCID blind index could not be produced.");
        var accountBidx = pii.BlindIndexExact(
                $"provider-account:{companyId}:{prepared.SourceProvider}:{prepared.SourceAccountReference}")
            ?? throw Invalid("The provider account blind index could not be produced.");
        var observationBidx = pii.BlindIndexExact(
                $"provider-observation:{prepared.SourceProvider}:{accountBidx}:{prepared.SourceObservationId}")
            ?? throw Invalid("The provider observation blind index could not be produced.");

        var outcome = await database.RunInSystemTransactionAsync(async () =>
        {
            await database.ExecuteAsync(
                "SELECT pg_advisory_xact_lock(hashtextextended(@identity,0))",
                command => command.Parameters.AddWithValue("@identity",
                    $"connectivity-observation:{companyId}:{prepared.SourceProvider}:{accountBidx}:{observationBidx}"), ct);

            var currentProfile = await database.QuerySingleAsync(
                @"SELECT p.id profile_id,p.device_id,p.branch_id,p.iccid_last4
                    FROM device_connectivity_profiles p
                    JOIN eld_devices e ON e.company_id=p.company_id AND e.id=p.device_id AND e.deleted_at IS NULL
                   WHERE p.company_id=@company AND p.iccid_bidx=@iccid
                     AND p.branch_id IS NOT DISTINCT FROM e.branch_id
                     AND p.assignment_status='Assigned' AND p.effective_to IS NULL
                   FOR SHARE OF p",
                command => { command.Parameters.AddWithValue("@company", companyId); command.Parameters.AddWithValue("@iccid", iccidBidx); }, ct);
            if (currentProfile is null)
                throw Invalid("The provider observation does not match an exact current SIM/eSIM profile.");

            var existing = await database.QuerySingleAsync(
                @"SELECT id,device_id,connectivity_profile_id,payload_sha256,subscription_status,
                         network_registration_status,data_session_status,usage_bytes,roaming,observed_at
                    FROM device_connectivity_observations
                   WHERE company_id=@company AND source_provider=@provider
                     AND source_account_bidx=@account AND source_observation_bidx=@observation
                   FOR SHARE",
                command => BindIdentity(command, companyId, prepared.SourceProvider, accountBidx, observationBidx), ct);
            if (existing is not null)
            {
                if (!ReplayMatches(existing, Convert.ToInt64(currentProfile["deviceId"]),
                    Convert.ToInt64(currentProfile["profileId"]), prepared))
                    throw Invalid("The provider observation identity was reused with different content or profile mapping.");
                return RowResult(existing, prepared, DeviceConnectivityObservationDisposition.Replay);
            }

            var id = await database.InsertAsync(
                @"INSERT INTO device_connectivity_observations
                    (company_id,branch_id,device_id,connectivity_profile_id,profile_iccid_bidx_snapshot,
                     profile_iccid_last4,source_provider,source_account_bidx,source_observation_bidx,
                     payload_sha256,source_authentication_status,subscription_status,
                     network_registration_status,data_session_status,usage_bytes,roaming,
                     observed_at,received_at,reconciliation_status,provider_verified_claim,
                     physical_connectivity_claim,certification_claim)
                  VALUES
                    (@company,@branch,@device,@profile,@iccid,@last4,@provider,@account,@observation,
                     @payload,'Authenticated',@subscription,@network,@session,@usage,@roaming,
                     @observed,@received,'ExactCurrentProfile',FALSE,FALSE,FALSE)",
                command =>
                {
                    BindIdentity(command, companyId, prepared.SourceProvider, accountBidx, observationBidx);
                    command.Parameters.AddWithValue("@branch", currentProfile["branchId"] is null or DBNull ? DBNull.Value : currentProfile["branchId"]!);
                    command.Parameters.AddWithValue("@device", currentProfile["deviceId"]!);
                    command.Parameters.AddWithValue("@profile", currentProfile["profileId"]!);
                    command.Parameters.AddWithValue("@iccid", iccidBidx);
                    command.Parameters.AddWithValue("@last4", currentProfile["iccidLast4"]!);
                    command.Parameters.AddWithValue("@payload", prepared.PayloadSha256);
                    command.Parameters.AddWithValue("@subscription", prepared.SubscriptionStatus);
                    command.Parameters.AddWithValue("@network", prepared.NetworkRegistrationStatus);
                    command.Parameters.AddWithValue("@session", prepared.DataSessionStatus);
                    command.Parameters.AddWithValue("@usage", (object?)prepared.UsageBytes ?? DBNull.Value);
                    command.Parameters.AddWithValue("@roaming", (object?)prepared.Roaming ?? DBNull.Value);
                    command.Parameters.AddWithValue("@observed", prepared.ObservedAtUtc);
                    command.Parameters.AddWithValue("@received", prepared.ReceivedAtUtc);
                }, ct);
            return new DeviceConnectivityObservationResult(
                id, Convert.ToInt64(currentProfile["deviceId"]), Convert.ToInt64(currentProfile["profileId"]),
                DeviceConnectivityObservationDisposition.Accepted, prepared.SourceProvider, "Authenticated",
                prepared.SubscriptionStatus, prepared.NetworkRegistrationStatus, prepared.DataSessionStatus,
                new DateTimeOffset(prepared.ObservedAtUtc, TimeSpan.Zero), false, false, false);
        });

        logger.LogInformation(
            "Connectivity provider observation {Disposition} for company {CompanyId}, device {DeviceId}, provider {Provider}",
            outcome.Disposition, companyId, outcome.DeviceId, prepared.SourceProvider);
        return outcome;
    }

    private static void BindIdentity(
        Npgsql.NpgsqlCommand command,
        long companyId,
        string provider,
        string accountBidx,
        string observationBidx)
    {
        command.Parameters.AddWithValue("@company", companyId);
        command.Parameters.AddWithValue("@provider", provider);
        command.Parameters.AddWithValue("@account", accountBidx);
        command.Parameters.AddWithValue("@observation", observationBidx);
    }

    private static bool ReplayMatches(Dictionary<string, object?> row, long deviceId, long profileId, PreparedObservation prepared) =>
        Convert.ToInt64(row["deviceId"]) == deviceId && Convert.ToInt64(row["connectivityProfileId"]) == profileId &&
        string.Equals(row["payloadSha256"]?.ToString(), prepared.PayloadSha256, StringComparison.Ordinal) &&
        string.Equals(row["subscriptionStatus"]?.ToString(), prepared.SubscriptionStatus, StringComparison.Ordinal) &&
        string.Equals(row["networkRegistrationStatus"]?.ToString(), prepared.NetworkRegistrationStatus, StringComparison.Ordinal) &&
        string.Equals(row["dataSessionStatus"]?.ToString(), prepared.DataSessionStatus, StringComparison.Ordinal) &&
        NullableLong(row.GetValueOrDefault("usageBytes")) == prepared.UsageBytes &&
        NullableBool(row.GetValueOrDefault("roaming")) == prepared.Roaming &&
        Timestamp(row["observedAt"]) == prepared.ObservedAtUtc;

    private static DeviceConnectivityObservationResult RowResult(
        Dictionary<string, object?> row, PreparedObservation prepared, DeviceConnectivityObservationDisposition disposition) => new(
        Convert.ToInt64(row["id"]), Convert.ToInt64(row["deviceId"]), Convert.ToInt64(row["connectivityProfileId"]),
        disposition, prepared.SourceProvider, "Authenticated", prepared.SubscriptionStatus,
        prepared.NetworkRegistrationStatus, prepared.DataSessionStatus,
        new DateTimeOffset(prepared.ObservedAtUtc, TimeSpan.Zero), false, false, false);

    private static long? NullableLong(object? value) => value is null or DBNull ? null : Convert.ToInt64(value);
    private static bool? NullableBool(object? value) => value is null or DBNull ? null : Convert.ToBoolean(value);
    private static DateTime Timestamp(object? value) => value switch
    {
        DateTime time => DateTime.SpecifyKind(time, DateTimeKind.Utc),
        DateTimeOffset offset => offset.UtcDateTime,
        _ => DateTime.MinValue,
    };
    private static DateTime Utc(DateTimeOffset value, string field)
    {
        if (value.Offset != TimeSpan.Zero) throw Invalid($"The {field} must use UTC.");
        return value.UtcDateTime;
    }
    private static string Required(string? value, int max, string field)
    {
        var cleaned = value?.Trim();
        if (string.IsNullOrWhiteSpace(cleaned) || cleaned.Length > max)
            throw Invalid($"The {field} is required and cannot exceed {max} characters.");
        return cleaned;
    }
    private static string Choice(string? value, IReadOnlyCollection<string> accepted, string field)
    {
        var cleaned = value?.Trim();
        if (cleaned is null || !accepted.Contains(cleaned, StringComparer.Ordinal))
            throw Invalid($"The {field} is invalid.");
        return cleaned;
    }
    private static DeviceConnectivityObservationValidationException Invalid(string message) => new(message);
}
