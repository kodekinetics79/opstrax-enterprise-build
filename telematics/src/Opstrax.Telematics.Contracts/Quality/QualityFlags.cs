namespace Opstrax.Telematics.Contracts.Quality;

/// <summary>
/// Independent boolean quality signals raised by the normalization stage. Flags are
/// non-exclusive: a single event may, for example, be both <see cref="IsOutOfOrder"/>
/// and <see cref="ClockSkewSuspected"/>. Flags describe <em>observations</em>; the
/// downstream <see cref="Opstrax.Telematics.Contracts.CanonicalTelemetryEvent.TrustScore"/> converts them into a scalar for policy.
/// </summary>
/// <remarks>
/// The default value (all flags <see langword="false"/>) represents a clean fix and
/// is the correct starting point for a freshly decoded, not-yet-analyzed event.
/// </remarks>
public readonly record struct QualityFlags
{
    /// <summary>A byte-identical or key-identical event has already been ingested (idempotency hit).</summary>
    public bool IsDuplicate { get; init; }

    /// <summary>The device fix time is earlier than an already-processed fix for the same device.</summary>
    public bool IsOutOfOrder { get; init; }

    /// <summary>The frame reproduces a previously seen sequence/nonce, consistent with a replay attack.</summary>
    public bool IsReplay { get; init; }

    /// <summary>The fix is older than the freshness budget for its lifecycle state.</summary>
    public bool IsStale { get; init; }

    /// <summary>The gap between device fix time and gateway receive time is implausibly large.</summary>
    public bool ClockSkewSuspected { get; init; }

    /// <summary>Displacement from the previous fix is physically impossible within the elapsed time.</summary>
    public bool TeleportSuspected { get; init; }

    /// <summary>Reported or derived speed exceeds a hard physical ceiling for the vehicle class.</summary>
    public bool ImpossibleSpeed { get; init; }

    /// <summary>Signal characteristics are consistent with GPS jamming or spoofing.</summary>
    public bool GpsJammingSuspected { get; init; }

    /// <summary>
    /// <see langword="true"/> when no quality concern was raised — every flag is clear.
    /// </summary>
    public bool IsClean =>
        !IsDuplicate && !IsOutOfOrder && !IsReplay && !IsStale &&
        !ClockSkewSuspected && !TeleportSuspected && !ImpossibleSpeed && !GpsJammingSuspected;
}
