namespace Opstrax.Telematics.Gateway.Security.Replay;

/// <summary>
/// The three terminal verdicts a replay guard can return for a single frame.
/// </summary>
public enum ReplayOutcome
{
    /// <summary>The frame is novel and in-order; the caller should process it.</summary>
    Accept,

    /// <summary>
    /// The exact triple <c>(deviceId, serial, contentHash)</c> has already been seen inside the
    /// guard's memory/dedup window. This is a byte-for-byte replay and MUST be dropped.
    /// </summary>
    DuplicateReplay,

    /// <summary>
    /// The frame carries a protocol serial strictly behind the device's high-water mark and is
    /// not a recognised duplicate. It is either a stale/reordered packet or a replay whose window
    /// entry has already been evicted. Either way it MUST be dropped. <see cref="ReplayDecision.LastSeenSerial"/>
    /// carries the high-water serial that made this frame out-of-order.
    /// </summary>
    OutOfOrder,
}

/// <summary>
/// The immutable result of a single <see cref="ITelemetryReplayGuard.Check"/> call. Models the
/// closed set <c>{ Accept | DuplicateReplay | OutOfOrder(lastSeen) }</c>: only the
/// <see cref="ReplayOutcome.OutOfOrder"/> arm carries a payload (<see cref="LastSeenSerial"/>).
/// </summary>
public readonly record struct ReplayDecision
{
    private ReplayDecision(ReplayOutcome outcome, long? lastSeenSerial)
    {
        Outcome = outcome;
        LastSeenSerial = lastSeenSerial;
    }

    /// <summary>Which of the three verdicts this decision represents.</summary>
    public ReplayOutcome Outcome { get; }

    /// <summary>
    /// For <see cref="ReplayOutcome.OutOfOrder"/>, the device's high-water serial at the moment of
    /// the check (the value the rejected frame fell behind). <see langword="null"/> otherwise.
    /// </summary>
    public long? LastSeenSerial { get; }

    /// <summary><see langword="true"/> only when the frame should be processed.</summary>
    public bool IsAccepted => Outcome == ReplayOutcome.Accept;

    /// <summary>The frame is novel and in-order.</summary>
    public static ReplayDecision Accept() => new(ReplayOutcome.Accept, null);

    /// <summary>The exact <c>(deviceId, serial, contentHash)</c> triple was already seen.</summary>
    public static ReplayDecision DuplicateReplay() => new(ReplayOutcome.DuplicateReplay, null);

    /// <summary>The serial fell behind the device high-water mark <paramref name="lastSeenSerial"/>.</summary>
    public static ReplayDecision OutOfOrder(long lastSeenSerial) => new(ReplayOutcome.OutOfOrder, lastSeenSerial);

    /// <inheritdoc />
    public override string ToString() => Outcome switch
    {
        ReplayOutcome.OutOfOrder => $"OutOfOrder(lastSeen={LastSeenSerial})",
        _ => Outcome.ToString(),
    };
}

/// <summary>
/// Per-device replay and sequence defense for decoded telemetry frames. Answers one question for
/// every inbound frame: <em>have I already seen this, and is it in order?</em>
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> The gps-ingest path (see
/// <c>docs/telematics/security/threat-model.md</c> §1.2, rows D2 and the "packet replay" row)
/// relies today on a <em>process-local, non-durable</em> replay cache: it reopens its window on
/// every restart or scale-out event, and a burst of distinct nonces can balloon process memory.
/// This seam replaces that with (a) a bounded, thread-safe in-memory guard for dev/test and
/// (b) a durable, shared, Postgres-backed guard whose <c>UNIQUE</c> constraint gives the same
/// atomic replay guarantee as the strong path's <c>telemetry_nonces</c> table.
/// </para>
/// <para>
/// <b>Two defenses, one call.</b> The guard combines
/// </para>
/// <list type="bullet">
///   <item><description>a <b>bounded dedup window</b> keyed on the exact triple
///     <c>(deviceId, serial, contentHash)</c> — catches byte-for-byte replays near the
///     high-water mark and returns <see cref="ReplayOutcome.DuplicateReplay"/>; and</description></item>
///   <item><description>a <b>per-device monotonic serial high-water mark</b> — a serial that has
///     fallen strictly behind the mark (and is not a known duplicate) is
///     <see cref="ReplayOutcome.OutOfOrder"/>.</description></item>
/// </list>
/// <para>
/// Together these give a durable safety property: a replay of a previously accepted frame is
/// <em>always</em> rejected — as <see cref="ReplayOutcome.DuplicateReplay"/> while it is still in
/// the window, and as <see cref="ReplayOutcome.OutOfOrder"/> after its window entry is evicted
/// (its serial is by then below the high-water mark).
/// </para>
/// <para>
/// <b>The serial.</b> For GT06 this is the frame's 16-bit information serial number
/// (<c>Gt06Adapter</c> exposes it as the <c>"serial"</c> field / <c>DecodedMessage.ProtocolMessageId</c>).
/// Because that counter wraps at 65 536, implementations may be constructed with a wraparound
/// modulus so a legitimate wrap (e.g. 65 530 → 3) is treated as forward progress rather than
/// out-of-order. The contract itself is protocol-agnostic: <paramref name="protocolSerial"/> is a
/// plain 64-bit monotonic token and <paramref name="contentHash"/> is an opaque digest of the
/// frame payload the caller wishes to deduplicate on.
/// </para>
/// <para><b>Thread-safety.</b> Implementations MUST be safe for concurrent calls across devices
/// and for concurrent calls for the same device.</para>
/// </remarks>
public interface ITelemetryReplayGuard
{
    /// <summary>
    /// Records and classifies a single decoded frame. The call is atomic per device:
    /// on <see cref="ReplayOutcome.Accept"/> the frame is durably/locally remembered before the
    /// method returns, so a concurrent duplicate cannot also be accepted.
    /// </summary>
    /// <param name="deviceId">
    /// The stable identifier the guard partitions on. This should be the <em>resolved</em> device
    /// id where available; an untrusted claim (e.g. IMEI) still yields correct per-key dedup but
    /// carries no ownership meaning.
    /// </param>
    /// <param name="protocolSerial">The protocol's own frame serial / sequence number.</param>
    /// <param name="contentHash">
    /// An opaque hex digest of the frame content to deduplicate on (e.g. SHA-256 of the raw frame
    /// or of its canonical payload). Must be non-empty.
    /// </param>
    /// <param name="deviceFixTimeUtc">
    /// The device-stamped time of the fix carried by the frame, when known. Supplementary context
    /// for auditing and for layered fix-time monotonicity checks; the canonical ordering token is
    /// <paramref name="protocolSerial"/>.
    /// </param>
    /// <returns>The verdict for this frame.</returns>
    ReplayDecision Check(string deviceId, long protocolSerial, string contentHash, DateTime deviceFixTimeUtc);
}
