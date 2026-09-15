namespace Opstrax.Telematics.Protocols.J1939;

internal static class J1939CanonicalizationGuard
{
    internal static void Validate(J1939CanonicalizationContext context, J1939AcquiredMessage message)
    {
        if (context.Owner.TenantId == Guid.Empty)
            throw new ArgumentException("Registry-resolved tenant identity is required.", nameof(context));
        if (context.Owner.CompanyId <= 0)
            throw new ArgumentException("Registry-resolved company identity is required.", nameof(context));
        if (string.IsNullOrWhiteSpace(context.Owner.DeviceId) ||
            !string.Equals(context.Owner.DeviceId, context.Owner.DeviceId.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("Registry-resolved device identity is required without surrounding whitespace.", nameof(context));
        if (context.EventId == Guid.Empty || context.CorrelationId == Guid.Empty)
            throw new ArgumentException("Non-empty event and correlation identities are required.", nameof(context));
        if (!Enum.IsDefined(context.Source))
            throw new ArgumentOutOfRangeException(nameof(context), "Telemetry source is invalid.");
        if (context.NormalizedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Normalization time must be UTC.", nameof(context));
        if (context.FreshnessBudget <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(context), "Freshness budget must be positive.");
        if (context.NormalizedAtUtc < message.CompletedAt.UtcDateTime)
            throw new ArgumentException("Normalization time cannot precede CAN capture completion.", nameof(context));
        if (!double.IsFinite(context.TrustScore) || context.TrustScore is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(context), "Trust score must be finite and inside [0,1].");
        if (!double.IsFinite(context.Confidence) || context.Confidence is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(context), "Confidence must be finite and inside [0,1].");
        if (message.Frames.Count == 0)
            throw new ArgumentException("Acquisition frame evidence is required.", nameof(message));
    }
}
