namespace Opstrax.Telematics.Gateway.Security.Replay;

/// <summary>
/// The three terminal verdicts a replay guard can return for a single frame.
/// </summary>
public enum ReplayOutcome
{
    /// <summary>The frame is novel and in-order; the caller should process it.</summary>
    Accept,

    /// <summary>
    /// The exact occurrence <c>(deviceId, unwrappedSerial, contentHash)</c> has already been seen
    /// inside the guard's dedup ledger/window. It must not create a second logical event; an ingest
    /// boundary may idempotently re-run persistence with <see cref="ReplayDecision.EventId"/> to
    /// repair an earlier attempt that failed before acknowledgement.
    /// </summary>
    DuplicateReplay,

    /// <summary>
    /// The frame carries a protocol serial strictly behind the device's high-water mark and is
    /// not a recognised duplicate. It is either a stale/reordered packet or a replay whose window
    /// entry has already been evicted. It must not advance authoritative latest state; callers may
    /// retain it as explicitly flagged historical evidence. <see cref="ReplayDecision.LastSeenSerial"/>
    /// carries the raw high-water serial that made this frame out-of-order.
    /// </summary>
    OutOfOrder,
}

/// <summary>
/// The immutable result of a single <see cref="ITelemetryReplayGuard.Check"/> call. Models the
/// closed set <c>{ Accept | DuplicateReplay | OutOfOrder(lastSeen) }</c>. Every durable decision
/// carries the replay occurrence's stable <see cref="EventId"/>; only the out-of-order arm also
/// carries <see cref="LastSeenSerial"/>.
/// </summary>
public readonly record struct ReplayDecision
{
    private ReplayDecision(ReplayOutcome outcome, long? lastSeenSerial, Guid? eventId)
    {
        Outcome = outcome;
        LastSeenSerial = lastSeenSerial;
        EventId = eventId;
    }

    /// <summary>Which of the three verdicts this decision represents.</summary>
    public ReplayOutcome Outcome { get; }

    /// <summary>
    /// For <see cref="ReplayOutcome.OutOfOrder"/>, the device's high-water serial at the moment of
    /// the check (the value the rejected frame fell behind). <see langword="null"/> otherwise.
    /// </summary>
    public long? LastSeenSerial { get; }

    /// <summary>
    /// Durable event identity assigned to this replay-window occurrence. Exact retries receive
    /// the same value; a later protocol-counter generation receives a new value.
    /// </summary>
    public Guid? EventId { get; }

    /// <summary><see langword="true"/> only when the frame should be processed.</summary>
    public bool IsAccepted => Outcome == ReplayOutcome.Accept;

    /// <summary>The frame is novel and in-order.</summary>
    public static ReplayDecision Accept(Guid? eventId = null) => new(ReplayOutcome.Accept, null, eventId);

    /// <summary>The exact <c>(deviceId, unwrappedSerial, contentHash)</c> occurrence was already seen.</summary>
    public static ReplayDecision DuplicateReplay(Guid? eventId = null) => new(ReplayOutcome.DuplicateReplay, null, eventId);

    /// <summary>The serial fell behind the device high-water mark <paramref name="lastSeenSerial"/>.</summary>
    public static ReplayDecision OutOfOrder(long lastSeenSerial, Guid? eventId = null) =>
        new(ReplayOutcome.OutOfOrder, lastSeenSerial, eventId);

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
///   <item><description>a <b>dedup ledger/window</b> keyed on the exact occurrence
///     <c>(deviceId, unwrappedSerial, contentHash)</c> — catches byte-for-byte retries within one
///     counter generation and returns <see cref="ReplayOutcome.DuplicateReplay"/>; and</description></item>
///   <item><description>a <b>per-device monotonic serial high-water mark</b> — a serial that has
///     fallen strictly behind the mark (and is not a known duplicate) is
///     <see cref="ReplayOutcome.OutOfOrder"/>.</description></item>
/// </list>
/// <para>
/// Together these give a durable safety property: a retry in the same unwrapped counter generation
/// receives the same event identity and cannot double-apply. A later legitimate counter wrap is a
/// new generation and receives a new identity; delayed pre-wrap frames remain out-of-order.
/// </para>
/// <para>
/// <b>The serial.</b> For GT06 this is the frame's 16-bit information serial number
/// (<c>Gt06Adapter</c> exposes it as the <c>"serial"</c> field / <c>DecodedMessage.ProtocolMessageId</c>).
/// Because that counter wraps at 65 536, implementations may be constructed with a wraparound
/// modulus so a legitimate wrap (e.g. 65 530 → 3) is treated as forward progress rather than
/// out-of-order. The contract itself is protocol-agnostic: <paramref name="protocolSerial"/> is a
/// raw or monotonic 64-bit protocol token and <paramref name="contentHash"/> is an opaque digest
/// of the frame payload the caller wishes to deduplicate on.
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

    /// <summary>
    /// Asynchronous equivalent of <see cref="Check"/>. Connection-servicing call sites (the gateway
    /// read loop) MUST use this: the durable implementation does a DB round-trip per frame, and doing
    /// that synchronously parks a thread-pool thread per in-flight fix, starving the pool under fleet
    /// load. In-memory guards complete synchronously and return an already-completed task.
    /// </summary>
    System.Threading.Tasks.Task<ReplayDecision> CheckAsync(
        string deviceId, long protocolSerial, string contentHash, DateTime deviceFixTimeUtc,
        System.Threading.CancellationToken cancellationToken = default);
}
