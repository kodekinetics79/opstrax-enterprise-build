namespace Opstrax.Api.Services;

// Classifies the position fix itself, independently from transport connectivity.
// A freshly received buffered event can update last_seen_at while its old GPS fix
// remains stale; callers must never use receipt time to label that position healthy.
internal static class TelemetryFixFreshness
{
    internal static (string TelemetryStatus, string RiskLevel) Classify(
        DateTimeOffset deviceFixUtc,
        DateTimeOffset receivedUtc)
    {
        var age = receivedUtc.ToUniversalTime() - deviceFixUtc.ToUniversalTime();
        if (age < TimeSpan.FromMinutes(-5)) return ("unknown", "unknown");
        return age <= TimeSpan.FromMinutes(5) ? ("healthy", "low") : ("stale", "unknown");
    }

    internal static (string TelemetryStatus, string RiskLevel) Classify(
        DateTime deviceFixUtc,
        DateTime receivedUtc)
        => Classify(new DateTimeOffset(ToUtc(deviceFixUtc)), new DateTimeOffset(ToUtc(receivedUtc)));

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
