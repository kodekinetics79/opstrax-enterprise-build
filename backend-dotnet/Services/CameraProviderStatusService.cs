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
    DateTime? LastProviderReceiptUtc);

/// <summary>
/// Returns a deliberately small tenant/branch-scoped operational projection from
/// the system-only camera intake ledger. Provider identities, event identifiers,
/// payload hashes and media identifiers never cross this boundary.
/// </summary>
public sealed class CameraProviderStatusService(Database database)
{
    public async Task<CameraProviderOperationalStatus> ReadAsync(
        long companyId,
        long? branchId,
        CancellationToken ct = default)
    {
        if (companyId <= 0 || branchId is <= 0)
            throw new ArgumentOutOfRangeException(nameof(companyId), "Camera provider status requires valid tenant and branch identities.");

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
                       MAX(e.provider_received_at_utc) last_provider_receipt_utc
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
                row["lastProviderReceiptUtc"] is DateTime received ? received : null);
        }, ct);
    }

    private static long Long(Dictionary<string, object?> row, string key) => Convert.ToInt64(row[key]);
}
