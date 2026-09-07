using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

public sealed record CameraProviderOperationalStatus(
    string Status,
    string VerificationStatus,
    string CertificationStatus,
    bool ProviderVerified,
    bool MediaAvailable,
    long ObservedEventCount,
    long MatchedEventCount,
    long UnmatchedEventCount,
    long QuarantinedEventCount,
    long PendingMediaCount,
    long ExpiredMediaCount,
    DateTime? LastOpsTraxIntakeUtc);

public sealed record CameraProviderPendingEvent(
    string IntakeReference,
    string EventType,
    DateTime OccurredAtUtc,
    DateTime ReceivedAtUtc,
    string ReconciliationStatus,
    string ProcessingStatus,
    string VerificationStatus,
    bool ProviderVerified,
    bool MediaAvailable,
    string? VehicleCode,
    string? DriverName,
    int MediaReferenceCount);

/// <summary>
/// Returns deliberately small tenant/branch-scoped operational projections from
/// the system-only camera intake ledger. Provider identities, event identifiers,
/// payload hashes and media identifiers never cross this boundary.
/// </summary>
public sealed class CameraProviderStatusService(Database database)
{
    public async Task<IReadOnlyList<CameraProviderPendingEvent>> ReadPendingEventsAsync(
        long companyId,
        long? branchId,
        CancellationToken ct = default)
    {
        ValidateScope(companyId, branchId);
        return await database.RunInSystemTransactionAsync(async () =>
        {
            var rows = await database.QueryAsync("""
                SELECT 'intake-' || e.id::text intake_reference,
                       CASE WHEN e.processing_status='Quarantined' THEN 'Unavailable' ELSE e.event_type END event_type,
                       e.occurred_at_utc,e.first_received_at_utc received_at_utc,
                       e.reconciliation_status,e.processing_status,
                       v.vehicle_code,d.full_name driver_name,
                       COUNT(m.id)::INTEGER media_reference_count
                FROM camera_provider_event_inbox e
                LEFT JOIN vehicles v
                  ON v.company_id=e.company_id AND v.id=e.vehicle_id AND v.deleted_at IS NULL
                LEFT JOIN drivers d
                  ON d.company_id=e.company_id AND d.id=e.driver_id AND d.deleted_at IS NULL
                LEFT JOIN camera_provider_media_references m
                  ON m.company_id=e.company_id AND m.provider_event_inbox_id=e.id
                WHERE e.company_id=@company AND (@branch::BIGINT IS NULL OR e.branch_id=@branch)
                GROUP BY e.id,e.event_type,e.occurred_at_utc,e.first_received_at_utc,
                         e.reconciliation_status,e.processing_status,v.vehicle_code,d.full_name
                ORDER BY e.occurred_at_utc DESC,e.id DESC
                LIMIT 200
                """, command =>
                {
                    command.Parameters.AddWithValue("company", companyId);
                    command.Parameters.AddWithValue("branch", (object?)branchId ?? DBNull.Value);
                }, ct);

            return (IReadOnlyList<CameraProviderPendingEvent>)rows.Select(row =>
                new CameraProviderPendingEvent(
                    Required(row, "intakeReference"),
                    Required(row, "eventType"),
                    Date(row, "occurredAtUtc"),
                    Date(row, "receivedAtUtc"),
                    Required(row, "reconciliationStatus"),
                    Required(row, "processingStatus"),
                    CameraProviderIngestService.ExternalHold,
                    ProviderVerified: false,
                    MediaAvailable: false,
                    Optional(row, "vehicleCode"),
                    Optional(row, "driverName"),
                    Convert.ToInt32(row["mediaReferenceCount"]))).ToArray();
        }, ct);
    }

    public async Task<CameraProviderOperationalStatus> ReadAsync(
        long companyId,
        long? branchId,
        CancellationToken ct = default)
    {
        ValidateScope(companyId, branchId);

        return await database.RunInSystemTransactionAsync(async () =>
        {
            var row = await database.QuerySingleAsync("""
                SELECT COUNT(DISTINCT e.id) observed_event_count,
                       COUNT(DISTINCT e.id) FILTER (
                         WHERE processing_status='PendingVerification' AND reconciliation_status='Matched'
                       ) matched_event_count,
                       COUNT(DISTINCT e.id) FILTER (
                         WHERE processing_status='PendingVerification' AND reconciliation_status='Pending'
                       ) unmatched_event_count,
                       COUNT(DISTINCT e.id) FILTER (WHERE processing_status='Quarantined') quarantined_event_count,
                       COUNT(m.id) FILTER (WHERE m.retrieval_status='ProviderPending') pending_media_count,
                       COUNT(m.id) FILTER (WHERE m.retrieval_status='Expired') expired_media_count,
                       MAX(e.first_received_at_utc) last_ops_trax_intake_utc
                FROM camera_provider_event_inbox e
                LEFT JOIN camera_provider_media_references m
                  ON m.company_id=e.company_id AND m.provider_event_inbox_id=e.id
                WHERE e.company_id=@company AND (@branch::BIGINT IS NULL OR e.branch_id=@branch)
                """, command =>
                {
                    command.Parameters.AddWithValue("company", companyId);
                    command.Parameters.AddWithValue("branch", (object?)branchId ?? DBNull.Value);
                }, ct) ?? throw new InvalidOperationException("Camera provider status did not return a result.");

            var observed = Long(row, "observedEventCount");
            var matched = Long(row, "matchedEventCount");
            var unmatched = Long(row, "unmatchedEventCount");
            var quarantined = Long(row, "quarantinedEventCount");
            var status = observed == 0 ? "AwaitingProviderConnection"
                : quarantined > 0 && matched == 0 ? "AttentionRequired"
                : "ProviderDataPendingVerification";

            return new CameraProviderOperationalStatus(
                status,
                CameraProviderIngestService.ExternalHold,
                CameraProviderIngestService.ExternalHold,
                ProviderVerified: false,
                MediaAvailable: false,
                observed,
                matched,
                unmatched,
                quarantined,
                Long(row, "pendingMediaCount"),
                Long(row, "expiredMediaCount"),
                row["lastOpsTraxIntakeUtc"] is DateTime received ? received : null);
        }, ct);
    }

    private static void ValidateScope(long companyId, long? branchId)
    {
        if (companyId <= 0 || branchId is <= 0)
            throw new ArgumentOutOfRangeException(nameof(companyId), "Camera provider status requires valid tenant and branch identities.");
    }

    private static string Required(Dictionary<string, object?> row, string key) =>
        row[key]?.ToString() ?? throw new InvalidOperationException($"Camera provider projection is missing {key}.");
    private static string? Optional(Dictionary<string, object?> row, string key) =>
        row[key] is null or DBNull ? null : row[key]?.ToString();
    private static DateTime Date(Dictionary<string, object?> row, string key) =>
        row[key] is DateTime value ? value : throw new InvalidOperationException($"Camera provider projection is missing {key}.");
    private static long Long(Dictionary<string, object?> row, string key) => Convert.ToInt64(row[key]);
}
