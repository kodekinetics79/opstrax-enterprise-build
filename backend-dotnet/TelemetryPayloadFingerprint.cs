namespace Opstrax.Api;

internal enum TelemetryPayloadReplayDecision
{
    NewObservation,
    IdenticalReplay,
    Conflict,
}

internal static class TelemetryPayloadFingerprint
{
    internal static TelemetryPayloadReplayDecision Decide(
        long existingRows,
        string? existingFingerprint,
        long fingerprintedRows,
        long distinctFingerprintCount,
        string candidateFingerprint)
    {
        if (existingRows <= 0) return TelemetryPayloadReplayDecision.NewObservation;
        if (fingerprintedRows == existingRows &&
            distinctFingerprintCount == 1 &&
            !string.IsNullOrWhiteSpace(existingFingerprint) &&
            string.Equals(existingFingerprint, candidateFingerprint, StringComparison.Ordinal))
            return TelemetryPayloadReplayDecision.IdenticalReplay;
        return TelemetryPayloadReplayDecision.Conflict;
    }
}
