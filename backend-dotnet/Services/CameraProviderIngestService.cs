using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Npgsql;
using NpgsqlTypes;
using Opstrax.Api.Data;
using Opstrax.Api.Services.Connectors;

namespace Opstrax.Api.Services;

public sealed record CameraProviderMediaReference(
    string CameraRole,
    string MediaKind,
    string ContentType,
    string ProviderMediaId,
    DateTimeOffset? CapturedAtUtc,
    int? DurationMilliseconds,
    DateTimeOffset? ProviderExpiresAtUtc,
    string? RecordingMode,
    string RetentionClass,
    string PrivacyPolicyVersion);

public sealed record CameraProviderEventEnvelope(
    string ProviderKey,
    string ProviderAccountReference,
    string ProviderEventId,
    string PayloadSchemaVersion,
    string EventType,
    DateTimeOffset OccurredAtUtc,
    DateTimeOffset ProviderReceivedAtUtc,
    long? BranchId,
    long? VehicleId,
    long? DriverId,
    long? TripId,
    string? VehicleExternalId,
    string? DriverExternalId,
    string? TripExternalId,
    IReadOnlyList<CameraProviderMediaReference> MediaReferences);

public enum CameraProviderIngestDisposition
{
    Accepted,
    Replay,
    Quarantined
}

public sealed record CameraProviderIngestResult(
    long InboxId,
    CameraProviderIngestDisposition Disposition,
    string PayloadSha256,
    string ProviderVerificationStatus,
    string ReconciliationStatus,
    int SeenCount,
    int MediaReferenceCount,
    string? QuarantineReason);

public sealed class CameraProviderEventValidationException(string message) : ArgumentException(message);

/// <summary>
/// Provider-neutral camera intake. This service records an exact payload fingerprint and
/// opaque media identifiers for later provider-specific reconciliation. It deliberately
/// cannot create an Authoritative dashcam row or expose playable media.
/// </summary>
public sealed partial class CameraProviderIngestService(
    Database database,
    TimeProvider clock,
    ILogger<CameraProviderIngestService> logger)
{
    internal const int MaximumPayloadBytes = 2 * 1024 * 1024;
    internal const string ExternalHold = "ExternalHold";

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,79}$", RegexOptions.CultureInvariant)]
    private static partial Regex ProviderKeyPattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,39}$", RegexOptions.CultureInvariant)]
    private static partial Regex SchemaVersionPattern();

    internal sealed record PreparedMedia(
        string CameraRole,
        string MediaKind,
        string ContentType,
        string ProviderMediaId,
        DateTime? CapturedAtUtc,
        int? DurationMilliseconds,
        DateTime? ProviderExpiresAtUtc,
        string RetrievalStatus,
        string? RecordingMode,
        string RetentionClass,
        string PrivacyPolicyVersion);

    internal sealed record PreparedEvent(
        string ProviderKey,
        string ProviderAccountReference,
        string ProviderEventId,
        string PayloadSchemaVersion,
        string PayloadSha256,
        string EventType,
        DateTime OccurredAtUtc,
        DateTime ProviderReceivedAtUtc,
        long? BranchId,
        long? VehicleId,
        long? DriverId,
        long? TripId,
        string? VehicleExternalId,
        string? DriverExternalId,
        string? TripExternalId,
        IReadOnlyList<PreparedMedia> MediaReferences);

    private sealed record ReferenceResolution(
        long? DeviceId,
        long? DeviceInstallationId,
        long? BranchId,
        long? VehicleId,
        long? DriverId,
        long? TripId,
        string Status,
        string? QuarantineReason);

    internal static PreparedEvent Prepare(
        CameraProviderEventEnvelope envelope,
        ReadOnlyMemory<byte> authenticatedPayload,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (authenticatedPayload.IsEmpty || authenticatedPayload.Length > MaximumPayloadBytes)
            throw Invalid("The authenticated camera payload must contain 1 byte to 2 MiB.");
        if (now.Offset != TimeSpan.Zero)
            throw Invalid("The intake clock must be UTC.");

        var provider = Required(envelope.ProviderKey, 80, "provider key").ToLowerInvariant();
        if (!ProviderKeyPattern().IsMatch(provider))
            throw Invalid("The provider key is invalid.");
        var providerAccount = Required(envelope.ProviderAccountReference, 160, "provider account reference");
        var providerEventId = Required(envelope.ProviderEventId, 180, "provider event ID");
        var schemaVersion = Required(envelope.PayloadSchemaVersion, 40, "payload schema version");
        if (!SchemaVersionPattern().IsMatch(schemaVersion))
            throw Invalid("The payload schema version is invalid.");
        var eventType = Required(envelope.EventType, 120, "event type");

        var occurred = Utc(envelope.OccurredAtUtc, "event occurrence time");
        var received = Utc(envelope.ProviderReceivedAtUtc, "provider receipt time");
        if (received > now.UtcDateTime.AddMinutes(5))
            throw Invalid("The provider receipt time is in the future.");
        if (occurred > received.AddMinutes(5))
            throw Invalid("The event occurrence time is later than the provider receipt time.");

        foreach (var (value, name) in new[] {
            (envelope.BranchId,"branch ID"),(envelope.VehicleId,"vehicle ID"),
            (envelope.DriverId,"driver ID"),(envelope.TripId,"trip ID") })
            if (value is <= 0) throw Invalid($"The {name} is invalid.");

        var externalVehicle = Optional(envelope.VehicleExternalId, 180, "external vehicle ID");
        var externalDriver = Optional(envelope.DriverExternalId, 180, "external driver ID");
        var externalTrip = Optional(envelope.TripExternalId, 180, "external trip ID");
        var media = envelope.MediaReferences ?? throw Invalid("The media-reference collection is required.");
        if (media.Count > 8) throw Invalid("A camera event cannot contain more than eight media references.");

        var mediaIds = new HashSet<string>(StringComparer.Ordinal);
        var normalizedMedia = new List<PreparedMedia>(media.Count);
        foreach (var item in media)
        {
            if (item is null) throw Invalid("A camera media reference is invalid.");
            if (item.CameraRole is not ("RoadFacing" or "DriverFacing"))
                throw Invalid("The camera role is invalid.");
            if (item.MediaKind is not ("Video" or "Image"))
                throw Invalid("The media kind is invalid.");
            if (item.ContentType is not ("video/mp4" or "image/jpeg"))
                throw Invalid("The media content type is invalid.");
            var mediaId = Required(item.ProviderMediaId, 240, "provider media ID");
            if (mediaId.Contains("://", StringComparison.Ordinal) || mediaId.Contains('?') || mediaId.Contains('#'))
                throw Invalid("Provider media IDs must be opaque identifiers, not URLs or signed links.");
            if (!mediaIds.Add(mediaId))
                throw Invalid("Provider media IDs must be unique within an event.");
            if (item.DurationMilliseconds is < 0 or > 3_600_000)
                throw Invalid("The media duration is invalid.");

            var captured = item.CapturedAtUtc is { } capturedAt ? Utc(capturedAt, "media capture time") : (DateTime?)null;
            var expires = item.ProviderExpiresAtUtc is { } expiresAt ? Utc(expiresAt, "provider media expiry") : (DateTime?)null;
            if (captured is { } capturedValue && capturedValue > received.AddMinutes(5))
                throw Invalid("The media capture time is later than the provider receipt time.");
            var retention = Required(item.RetentionClass, 80, "retention class");
            var privacy = Required(item.PrivacyPolicyVersion, 80, "privacy policy version");
            var recordingMode = Optional(item.RecordingMode, 48, "recording mode");
            normalizedMedia.Add(new(
                item.CameraRole, item.MediaKind, item.ContentType, mediaId, captured,
                item.DurationMilliseconds, expires,
                expires is { } expiry && expiry <= now.UtcDateTime ? "Expired" : "ProviderPending",
                recordingMode, retention, privacy));
        }

        return new(
            provider, providerAccount, providerEventId, schemaVersion,
            Convert.ToHexString(SHA256.HashData(authenticatedPayload.Span)).ToLowerInvariant(),
            eventType, occurred, received,
            envelope.BranchId, envelope.VehicleId, envelope.DriverId, envelope.TripId,
            externalVehicle, externalDriver, externalTrip, normalizedMedia);
    }

    public Task<CameraProviderIngestResult> IngestAsync(
        long companyId,
        CameraProviderEventEnvelope envelope,
        ReadOnlyMemory<byte> authenticatedPayload,
        CancellationToken ct = default) =>
        IngestCoreAsync(companyId, envelope, authenticatedPayload, operation: null, ct);

    internal Task<CameraProviderIngestResult> IngestUnderConnectorLeaseAsync(
        ConnectorOperationContext operation,
        CameraProviderEventEnvelope envelope,
        ReadOnlyMemory<byte> authenticatedPayload,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (operation.CompanyId <= 0)
            throw Invalid("The connector operation company ID is invalid.");
        return IngestCoreAsync(operation.CompanyId, envelope, authenticatedPayload, operation, ct);
    }

    private async Task<CameraProviderIngestResult> IngestCoreAsync(
        long companyId,
        CameraProviderEventEnvelope envelope,
        ReadOnlyMemory<byte> authenticatedPayload,
        ConnectorOperationContext? operation,
        CancellationToken ct)
    {
        if (companyId <= 0) throw Invalid("The company ID is invalid.");
        var prepared = Prepare(envelope, authenticatedPayload, clock.GetUtcNow());

        var result = await database.RunInSystemTransactionAsync(async () =>
        {
            // Provider data may be persisted only while the exact connector generation
            // and lease are current. Configure/disconnect can invalidate the operation
            // while the remote response is in flight; this fence shares the transaction
            // with the ledger write so stale-generation evidence leaves no side effect.
            if (operation is not null)
                await ConnectorOperationLease.AssertCurrentForWriteAsync(database, operation, ct);

            if (await database.ScalarLongAsync("SELECT COUNT(*) FROM companies WHERE id=@company", c => c.Parameters.AddWithValue("company", companyId), ct) != 1)
                throw Invalid("The company does not exist.");

            await database.ExecuteAsync(
                "SELECT pg_advisory_xact_lock(hashtextextended(@identity,0))",
                c => c.Parameters.AddWithValue("identity", $"camera:{companyId}:{prepared.ProviderKey}:{prepared.ProviderAccountReference}:{prepared.ProviderEventId}"), ct);

            var resolution = await ResolveReferencesAsync(companyId, prepared, ct);
            var existing = await database.QuerySingleAsync("""
                SELECT id,payload_sha256,payload_schema_version,event_type,occurred_at_utc,provider_received_at_utc,
                       vehicle_external_id,driver_external_id,trip_external_id,
                       device_id,device_installation_id,branch_id,vehicle_id,
                       processing_status,reconciliation_status,quarantine_reason,seen_count
                FROM camera_provider_event_inbox
                WHERE company_id=@company AND provider_key=@provider AND provider_account_ref=@account AND provider_event_id=@event
                FOR UPDATE
                """, c => BindIdentity(c, companyId, prepared), ct);

            if (existing is not null)
            {
                var inboxId = Convert.ToInt64(existing["id"]);
                var seenCount = Convert.ToInt32(existing["seenCount"]) + 1;
                var originalHash = existing["payloadSha256"]?.ToString();
                if (!string.Equals(originalHash, prepared.PayloadSha256, StringComparison.Ordinal))
                {
                    await database.ExecuteAsync("""
                        UPDATE camera_provider_event_inbox
                        SET conflicting_payload_sha256=@hash,last_received_at_utc=GREATEST(last_received_at_utc,clock_timestamp()),seen_count=seen_count+1,
                            processing_status='Quarantined',reconciliation_status='Quarantined',
                            quarantine_reason='payload_identity_conflict',updated_at_utc=clock_timestamp()
                        WHERE id=@id AND company_id=@company
                        """, c => { c.Parameters.AddWithValue("hash", prepared.PayloadSha256); c.Parameters.AddWithValue("id", inboxId); c.Parameters.AddWithValue("company", companyId); }, ct);
                    return new CameraProviderIngestResult(
                        inboxId, CameraProviderIngestDisposition.Quarantined, prepared.PayloadSha256,
                        ExternalHold, "Quarantined", seenCount, 0, "payload_identity_conflict");
                }

                var priorQuarantineReason = existing["quarantineReason"]?.ToString();
                var priorEvidenceConflict = Equals(existing["processingStatus"], "Quarantined")
                    && priorQuarantineReason is "payload_identity_conflict" or "derived_payload_conflict" or "derived_mapping_conflict";
                if (priorEvidenceConflict)
                {
                    await database.ExecuteAsync("""
                        UPDATE camera_provider_event_inbox
                        SET last_received_at_utc=GREATEST(last_received_at_utc,clock_timestamp()),seen_count=seen_count+1,updated_at_utc=clock_timestamp()
                        WHERE id=@id AND company_id=@company
                        """, c => { c.Parameters.AddWithValue("id", inboxId); c.Parameters.AddWithValue("company", companyId); }, ct);
                    return new CameraProviderIngestResult(
                        inboxId, CameraProviderIngestDisposition.Quarantined, prepared.PayloadSha256,
                        ExternalHold, "Quarantined", seenCount, 0, priorQuarantineReason);
                }

                var providerProjectionMatches = ProviderProjectionMatches(existing, prepared);
                var storedDeviceMappingCanAdvance = StoredDeviceMappingCanAdvance(existing, resolution);
                var storedMediaMatches = await StoredMediaMatchesAsync(
                    companyId, inboxId, prepared.MediaReferences,
                    allowInitialPopulation: priorQuarantineReason is "tenant_reference_mismatch"
                        or "provider_asset_mapping_ambiguous"
                        or "provider_asset_mapping_invalid"
                        or "provider_asset_device_ineligible",
                    ct: ct);
                if (!providerProjectionMatches || !storedDeviceMappingCanAdvance || !storedMediaMatches)
                {
                    var conflictReason = providerProjectionMatches && storedMediaMatches
                        ? "derived_mapping_conflict"
                        : "derived_payload_conflict";
                    await database.ExecuteAsync("""
                        UPDATE camera_provider_event_inbox
                        SET last_received_at_utc=GREATEST(last_received_at_utc,clock_timestamp()),seen_count=seen_count+1,
                            processing_status='Quarantined',reconciliation_status='Quarantined',
                            quarantine_reason=@reason,updated_at_utc=clock_timestamp()
                        WHERE id=@id AND company_id=@company
                        """, c =>
                        {
                            c.Parameters.AddWithValue("reason", conflictReason);
                            c.Parameters.AddWithValue("id", inboxId);
                            c.Parameters.AddWithValue("company", companyId);
                        }, ct);
                    return new CameraProviderIngestResult(
                        inboxId, CameraProviderIngestDisposition.Quarantined, prepared.PayloadSha256,
                        ExternalHold, "Quarantined", seenCount, 0, conflictReason);
                }

                await database.ExecuteAsync("""
                    UPDATE camera_provider_event_inbox
                    SET device_id=@device,device_installation_id=@installation,
                        branch_id=@branch,vehicle_id=@vehicle,driver_id=@driver,trip_id=@trip,
                        reconciliation_status=@reconciliation,processing_status=@processing,
                        quarantine_reason=@reason,last_received_at_utc=GREATEST(last_received_at_utc,clock_timestamp()),seen_count=seen_count+1,updated_at_utc=clock_timestamp()
                    WHERE id=@id AND company_id=@company
                    """, c => BindResolution(c, companyId, inboxId, resolution), ct);
                var mediaCount = resolution.QuarantineReason is null
                    ? await UpsertMediaAsync(companyId, inboxId, prepared.MediaReferences, ct)
                    : 0;
                return new CameraProviderIngestResult(
                    inboxId,
                    resolution.QuarantineReason is null ? CameraProviderIngestDisposition.Replay : CameraProviderIngestDisposition.Quarantined,
                    prepared.PayloadSha256, ExternalHold, resolution.Status, seenCount, mediaCount, resolution.QuarantineReason);
            }

            var row = await database.QuerySingleAsync("""
                INSERT INTO camera_provider_event_inbox
                  (company_id,branch_id,provider_key,provider_account_ref,provider_event_id,payload_schema_version,payload_sha256,
                   event_type,occurred_at_utc,provider_received_at_utc,vehicle_external_id,driver_external_id,trip_external_id,
                   device_id,device_installation_id,vehicle_id,driver_id,trip_id,reconciliation_status,processing_status,quarantine_reason)
                VALUES
                  (@company,@branch,@provider,@account,@event,@schema,@hash,@eventType,@occurred,@received,
                   @externalVehicle,@externalDriver,@externalTrip,@device,@installation,@vehicle,@driver,@trip,@reconciliation,@processing,@reason)
                RETURNING id,seen_count
                """, c => BindInsert(c, companyId, prepared, resolution), ct)
                ?? throw new InvalidOperationException("Camera provider intake did not return its ledger identity.");
            var insertedId = Convert.ToInt64(row["id"]);
            var insertedMedia = resolution.QuarantineReason is null
                ? await UpsertMediaAsync(companyId, insertedId, prepared.MediaReferences, ct)
                : 0;
            return new CameraProviderIngestResult(
                insertedId,
                resolution.QuarantineReason is null ? CameraProviderIngestDisposition.Accepted : CameraProviderIngestDisposition.Quarantined,
                prepared.PayloadSha256, ExternalHold, resolution.Status, Convert.ToInt32(row["seenCount"]), insertedMedia,
                resolution.QuarantineReason);
        }, ct);

        logger.LogInformation(
            "Camera provider intake {Disposition}: company={CompanyId} provider={Provider} inbox={InboxId} verification={VerificationStatus} reconciliation={ReconciliationStatus} seen={SeenCount}",
            result.Disposition, companyId, prepared.ProviderKey, result.InboxId, result.ProviderVerificationStatus,
            result.ReconciliationStatus, result.SeenCount);
        return result;
    }

    private async Task<ReferenceResolution> ResolveReferencesAsync(long companyId, PreparedEvent item, CancellationToken ct)
    {
        var invalid = false;
        long? deviceId = null;
        long? installationId = null;
        long? mappedVehicleId = null;
        long? mappedBranchId = null;
        long? branch = null;
        var knownBranches = new HashSet<long>();

        if (item.VehicleExternalId is not null)
        {
            var devices = await database.QueryAsync("""
                SELECT id,status,device_state
                FROM eld_devices
                WHERE company_id=@company AND deleted_at IS NULL
                  AND LOWER(BTRIM(provider))=@provider
                  AND provider_account_ref=@account
                  AND provider_external_id=@external
                  AND NULLIF(BTRIM(provider),'') IS NOT NULL
                  AND NULLIF(BTRIM(provider_account_ref),'') IS NOT NULL
                  AND NULLIF(BTRIM(provider_external_id),'') IS NOT NULL
                FOR SHARE
                """, c =>
                {
                    c.Parameters.AddWithValue("company", companyId);
                    c.Parameters.AddWithValue("provider", item.ProviderKey);
                    c.Parameters.AddWithValue("account", item.ProviderAccountReference);
                    c.Parameters.AddWithValue("external", item.VehicleExternalId);
                }, ct);

            if (devices.Count > 1)
                return new(null, null, null, null, null, null, "Quarantined", "provider_asset_mapping_ambiguous");

            if (devices.Count == 1)
            {
                var device = devices[0];
                var status = device["status"]?.ToString()?.Trim().ToLowerInvariant();
                var state = device["deviceState"]?.ToString()?.Trim().ToLowerInvariant();
                if (status is not ("active" or "provisioning" or "pending")
                    || state is "suspended" or "quarantined" or "lost" or "decommissioning" or "decommissioned" or "retired")
                    return new(null, null, null, null, null, null, "Quarantined", "provider_asset_device_ineligible");

                var matchingDeviceId = Convert.ToInt64(device["id"]);
                var installations = await database.QueryAsync("""
                    SELECT i.id,i.vehicle_id,i.branch_id installation_branch_id
                    FROM device_installations i
                    WHERE i.company_id=@company AND i.device_id=@device
                      AND i.status IN ('Installed','Verified','Removed')
                      AND (i.status IN ('Installed','Verified')
                           OR (i.status='Removed' AND i.effective_to IS NOT NULL))
                      AND i.effective_from<=@occurred
                      AND (i.effective_to IS NULL OR i.effective_to>@occurred)
                    ORDER BY i.effective_from DESC,i.id DESC
                    LIMIT 2
                    FOR SHARE OF i
                    """, c =>
                    {
                        c.Parameters.AddWithValue("company", companyId);
                        c.Parameters.AddWithValue("device", matchingDeviceId);
                        c.Parameters.AddWithValue("occurred", NpgsqlDbType.TimestampTz, item.OccurredAtUtc);
                    }, ct);

                if (installations.Count > 1)
                    return new(null, null, null, null, null, null, "Quarantined", "provider_asset_mapping_ambiguous");

                if (installations.Count == 1)
                {
                    var installation = installations[0];
                    var installationVehicle = OptionalLong(installation, "vehicleId");
                    if (!installationVehicle.HasValue)
                        return new(null, null, null, null, null, null, "Quarantined", "provider_asset_mapping_invalid");

                    var installationBranch = OptionalLong(installation, "installationBranchId");
                    deviceId = matchingDeviceId;
                    installationId = Convert.ToInt64(installation["id"]);
                    mappedVehicleId = installationVehicle;
                    mappedBranchId = installationBranch;
                }
            }
        }

        if (item.BranchId is { } requestedBranch)
        {
            var row = await database.QuerySingleAsync(
                "SELECT id FROM branches WHERE id=@id AND company_id=@company AND deleted_at IS NULL FOR SHARE",
                c => { c.Parameters.AddWithValue("id", requestedBranch); c.Parameters.AddWithValue("company", companyId); }, ct);
            if (row is null) invalid = true;
            else { branch = requestedBranch; knownBranches.Add(requestedBranch); }
        }

        Dictionary<string, object?>? vehicle = null;
        var resolvedVehicleId = item.VehicleId ?? mappedVehicleId;
        if (resolvedVehicleId is { } vehicleId)
        {
            vehicle = await database.QuerySingleAsync(
                "SELECT id,branch_id FROM vehicles WHERE id=@id AND company_id=@company AND deleted_at IS NULL FOR SHARE",
                c => { c.Parameters.AddWithValue("id", vehicleId); c.Parameters.AddWithValue("company", companyId); }, ct);
            if (vehicle is null) invalid = true;
            else if (vehicle["branchId"] is not null) knownBranches.Add(Convert.ToInt64(vehicle["branchId"]));
        }
        if (mappedVehicleId.HasValue && item.VehicleId.HasValue && mappedVehicleId != item.VehicleId)
            invalid = true;
        if (mappedBranchId.HasValue)
            knownBranches.Add(mappedBranchId.Value);

        Dictionary<string, object?>? driver = null;
        if (item.DriverId is { } driverId)
        {
            driver = await database.QuerySingleAsync(
                "SELECT id,branch_id FROM drivers WHERE id=@id AND company_id=@company AND deleted_at IS NULL FOR SHARE",
                c => { c.Parameters.AddWithValue("id", driverId); c.Parameters.AddWithValue("company", companyId); }, ct);
            if (driver is null) invalid = true;
            else if (driver["branchId"] is not null) knownBranches.Add(Convert.ToInt64(driver["branchId"]));
        }

        Dictionary<string, object?>? trip = null;
        if (item.TripId is { } tripId)
        {
            trip = await database.QuerySingleAsync(
                "SELECT id,vehicle_id,driver_id FROM trips WHERE id=@id AND company_id=@company FOR SHARE",
                c => { c.Parameters.AddWithValue("id", tripId); c.Parameters.AddWithValue("company", companyId); }, ct);
            if (trip is null) invalid = true;
            else
            {
                if (resolvedVehicleId is { } selectedVehicle && trip["vehicleId"] is not null && Convert.ToInt64(trip["vehicleId"]) != selectedVehicle) invalid = true;
                if (item.DriverId is { } selectedDriver && trip["driverId"] is not null && Convert.ToInt64(trip["driverId"]) != selectedDriver) invalid = true;
            }
        }

        if (knownBranches.Count > 1) invalid = true;
        if (item.BranchId.HasValue && ((vehicle is not null && vehicle["branchId"] is null) || (driver is not null && driver["branchId"] is null)))
            invalid = true;
        if (!branch.HasValue && knownBranches.Count == 1) branch = knownBranches.Single();

        if (invalid)
            return new(null, null, branch, vehicle is null ? null : resolvedVehicleId, driver is null ? null : item.DriverId,
                trip is null ? null : item.TripId, "Quarantined", "tenant_reference_mismatch");
        var matched = resolvedVehicleId.HasValue || item.DriverId.HasValue || item.TripId.HasValue;
        return new(deviceId, installationId, branch, resolvedVehicleId, item.DriverId, item.TripId, matched ? "Matched" : "Pending", null);
    }

    private static long? OptionalLong(Dictionary<string, object?> row, string key) =>
        row.GetValueOrDefault(key) is { } value and not DBNull ? Convert.ToInt64(value) : null;

    private async Task<int> UpsertMediaAsync(long companyId, long inboxId, IReadOnlyList<PreparedMedia> media, CancellationToken ct)
    {
        foreach (var item in media)
        {
            await database.ExecuteAsync("""
                INSERT INTO camera_provider_media_references
                  (company_id,provider_event_inbox_id,camera_role,media_kind,content_type,provider_media_id,
                   captured_at_utc,duration_milliseconds,provider_expires_at_utc,retrieval_status,access_status,
                   recording_mode,retention_class,privacy_policy_version)
                VALUES
                  (@company,@inbox,@role,@kind,@contentType,@mediaId,@captured,@duration,@expires,@retrieval,'ExternalHold',
                   @recording,@retention,@privacy)
                ON CONFLICT (company_id,provider_event_inbox_id,camera_role,provider_media_id)
                DO UPDATE SET last_seen_at_utc=GREATEST(camera_provider_media_references.last_seen_at_utc,clock_timestamp()),updated_at_utc=clock_timestamp()
                """, c =>
                {
                    c.Parameters.AddWithValue("company", companyId);
                    c.Parameters.AddWithValue("inbox", inboxId);
                    c.Parameters.AddWithValue("role", item.CameraRole);
                    c.Parameters.AddWithValue("kind", item.MediaKind);
                    c.Parameters.AddWithValue("contentType", item.ContentType);
                    c.Parameters.AddWithValue("mediaId", item.ProviderMediaId);
                    AddNullable(c, "captured", NpgsqlDbType.TimestampTz, item.CapturedAtUtc);
                    AddNullable(c, "duration", NpgsqlDbType.Integer, item.DurationMilliseconds);
                    AddNullable(c, "expires", NpgsqlDbType.TimestampTz, item.ProviderExpiresAtUtc);
                    c.Parameters.AddWithValue("retrieval", item.RetrievalStatus);
                    AddNullable(c, "recording", NpgsqlDbType.Varchar, item.RecordingMode);
                    c.Parameters.AddWithValue("retention", item.RetentionClass);
                    c.Parameters.AddWithValue("privacy", item.PrivacyPolicyVersion);
                }, ct);
        }
        return media.Count;
    }

    private async Task<bool> StoredMediaMatchesAsync(
        long companyId,
        long inboxId,
        IReadOnlyList<PreparedMedia> expected,
        bool allowInitialPopulation,
        CancellationToken ct)
    {
        var rows = await database.QueryAsync("""
            SELECT camera_role,media_kind,content_type,provider_media_id,captured_at_utc,duration_milliseconds,
                   provider_expires_at_utc,retrieval_status,recording_mode,retention_class,privacy_policy_version
            FROM camera_provider_media_references
            WHERE company_id=@company AND provider_event_inbox_id=@inbox
            ORDER BY camera_role,provider_media_id
            """, c =>
            {
                c.Parameters.AddWithValue("company", companyId);
                c.Parameters.AddWithValue("inbox", inboxId);
            }, ct);

        // A reference-mismatch quarantine stores no media. That empty state may
        // be populated later from the same payload once internal references exist.
        if (rows.Count == 0) return expected.Count == 0 || allowInitialPopulation;
        if (rows.Count != expected.Count) return false;

        var ordered = expected.OrderBy(item => item.CameraRole, StringComparer.Ordinal)
            .ThenBy(item => item.ProviderMediaId, StringComparer.Ordinal).ToArray();
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var item = ordered[i];
            if (!Equals(row["cameraRole"], item.CameraRole)
                || !Equals(row["mediaKind"], item.MediaKind)
                || !Equals(row["contentType"], item.ContentType)
                || !Equals(row["providerMediaId"], item.ProviderMediaId)
                || !SameInstant(row["capturedAtUtc"], item.CapturedAtUtc)
                || !SameNullableInt(row["durationMilliseconds"], item.DurationMilliseconds)
                || !SameInstant(row["providerExpiresAtUtc"], item.ProviderExpiresAtUtc)
                || !Equals(row["retrievalStatus"], item.RetrievalStatus)
                || !Equals(row["recordingMode"], item.RecordingMode)
                || !Equals(row["retentionClass"], item.RetentionClass)
                || !Equals(row["privacyPolicyVersion"], item.PrivacyPolicyVersion))
                return false;
        }
        return true;
    }

    private static bool ProviderProjectionMatches(Dictionary<string, object?> stored, PreparedEvent item) =>
        Equals(stored["payloadSchemaVersion"], item.PayloadSchemaVersion)
        && Equals(stored["eventType"], item.EventType)
        && SameInstant(stored["occurredAtUtc"], item.OccurredAtUtc)
        && SameInstant(stored["providerReceivedAtUtc"], item.ProviderReceivedAtUtc)
        && Equals(stored["vehicleExternalId"], item.VehicleExternalId)
        && Equals(stored["driverExternalId"], item.DriverExternalId)
        && Equals(stored["tripExternalId"], item.TripExternalId);

    private static bool StoredDeviceMappingCanAdvance(Dictionary<string, object?> stored, ReferenceResolution resolution)
    {
        var storedDevice = OptionalLong(stored, "deviceId");
        if (!storedDevice.HasValue) return true;
        return storedDevice == resolution.DeviceId
            && OptionalLong(stored, "deviceInstallationId") == resolution.DeviceInstallationId
            && OptionalLong(stored, "vehicleId") == resolution.VehicleId
            && OptionalLong(stored, "branchId") == resolution.BranchId;
    }

    private static bool SameInstant(object? stored, DateTime? expected) =>
        stored is null ? expected is null
        : expected is not null && stored is DateTime value && value.ToUniversalTime() == DateTime.SpecifyKind(expected.Value, DateTimeKind.Utc);

    private static bool SameNullableInt(object? stored, int? expected) =>
        stored is null ? expected is null : expected is not null && Convert.ToInt32(stored) == expected.Value;

    private static void BindIdentity(NpgsqlCommand command, long companyId, PreparedEvent item)
    {
        command.Parameters.AddWithValue("company", companyId);
        command.Parameters.AddWithValue("provider", item.ProviderKey);
        command.Parameters.AddWithValue("account", item.ProviderAccountReference);
        command.Parameters.AddWithValue("event", item.ProviderEventId);
    }

    private static void BindResolution(NpgsqlCommand command, long companyId, long inboxId, ReferenceResolution resolution)
    {
        command.Parameters.AddWithValue("company", companyId);
        command.Parameters.AddWithValue("id", inboxId);
        AddNullable(command, "device", NpgsqlDbType.Bigint, resolution.DeviceId);
        AddNullable(command, "installation", NpgsqlDbType.Bigint, resolution.DeviceInstallationId);
        AddNullable(command, "branch", NpgsqlDbType.Bigint, resolution.BranchId);
        AddNullable(command, "vehicle", NpgsqlDbType.Bigint, resolution.VehicleId);
        AddNullable(command, "driver", NpgsqlDbType.Bigint, resolution.DriverId);
        AddNullable(command, "trip", NpgsqlDbType.Bigint, resolution.TripId);
        command.Parameters.AddWithValue("reconciliation", resolution.Status);
        command.Parameters.AddWithValue("processing", resolution.QuarantineReason is null ? "PendingVerification" : "Quarantined");
        AddNullable(command, "reason", NpgsqlDbType.Varchar, resolution.QuarantineReason);
    }

    private static void BindInsert(NpgsqlCommand command, long companyId, PreparedEvent item, ReferenceResolution resolution)
    {
        BindIdentity(command, companyId, item);
        AddNullable(command, "device", NpgsqlDbType.Bigint, resolution.DeviceId);
        AddNullable(command, "installation", NpgsqlDbType.Bigint, resolution.DeviceInstallationId);
        AddNullable(command, "branch", NpgsqlDbType.Bigint, resolution.BranchId);
        command.Parameters.AddWithValue("schema", item.PayloadSchemaVersion);
        command.Parameters.AddWithValue("hash", item.PayloadSha256);
        command.Parameters.AddWithValue("eventType", item.EventType);
        command.Parameters.AddWithValue("occurred", NpgsqlDbType.TimestampTz, item.OccurredAtUtc);
        command.Parameters.AddWithValue("received", NpgsqlDbType.TimestampTz, item.ProviderReceivedAtUtc);
        AddNullable(command, "externalVehicle", NpgsqlDbType.Varchar, item.VehicleExternalId);
        AddNullable(command, "externalDriver", NpgsqlDbType.Varchar, item.DriverExternalId);
        AddNullable(command, "externalTrip", NpgsqlDbType.Varchar, item.TripExternalId);
        AddNullable(command, "vehicle", NpgsqlDbType.Bigint, resolution.VehicleId);
        AddNullable(command, "driver", NpgsqlDbType.Bigint, resolution.DriverId);
        AddNullable(command, "trip", NpgsqlDbType.Bigint, resolution.TripId);
        command.Parameters.AddWithValue("reconciliation", resolution.Status);
        command.Parameters.AddWithValue("processing", resolution.QuarantineReason is null ? "PendingVerification" : "Quarantined");
        AddNullable(command, "reason", NpgsqlDbType.Varchar, resolution.QuarantineReason);
    }

    private static void AddNullable(NpgsqlCommand command, string name, NpgsqlDbType type, object? value) =>
        command.Parameters.Add(new NpgsqlParameter(name, type) { Value = value ?? DBNull.Value });

    private static DateTime Utc(DateTimeOffset value, string name)
    {
        if (value.Offset != TimeSpan.Zero) throw Invalid($"The {name} must use UTC (Z).");
        return value.UtcDateTime;
    }

    private static string Required(string? value, int maximum, string name)
    {
        if (value is null || value.Length == 0 || value.Length > maximum || value != value.Trim()
            || value.Any(char.IsControl))
            throw Invalid($"The {name} is invalid.");
        return value;
    }

    private static string? Optional(string? value, int maximum, string name) =>
        value is null ? null : Required(value, maximum, name);

    private static CameraProviderEventValidationException Invalid(string message) => new(message);
}
