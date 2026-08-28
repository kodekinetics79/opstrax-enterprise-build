using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;

namespace Opstrax.Telematics.Gateway.Observability;

/// <summary>
/// The single, process-wide home for the gateway's OpenTelemetry primitives: one
/// <see cref="System.Diagnostics.ActivitySource"/> for tracing and one
/// <see cref="System.Diagnostics.Metrics.Meter"/> for the concrete instruments catalogued in
/// <c>docs/telematics/observability/metrics.md</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why one static source/meter.</b> <see cref="ActivitySource"/> and <see cref="Meter"/>
/// are intended to be long-lived singletons registered once against a provider
/// (<c>AddSource</c> / <c>AddMeter</c>). Constructing them statically means the hot path
/// records spans and measurements with zero per-connection allocation and no DI lookup, and
/// keeps the instrument identities stable for exporters and tests to bind to.
/// </para>
/// <para>
/// <b>Name reconciliation.</b> The names below are the concrete, corrected forms of what the
/// observability docs sketched. The docs pre-dated the implemented assemblies and spelled the
/// product <c>OpsTrax.*</c>; the shipped assemblies are <c>Opstrax.*</c> (lowercase <c>t</c>),
/// so the registered source/meter names follow the assembly, not the prose. See the
/// reconciliation note at the foot of <c>metrics.md</c> / <c>tracing.md</c>.
/// </para>
/// <para>
/// <b>Cardinality discipline.</b> Per <c>metrics.md</c>, high-cardinality identifiers
/// (<c>device_id</c>, <c>imei</c>, <c>vehicle_id</c>, <c>correlation_id</c>, <c>trace_id</c>)
/// are NEVER metric labels — they live on spans and on exemplars only. The metric label set is
/// deliberately bounded: <c>company_id</c>, <c>protocol</c>, <c>adapter</c>,
/// <c>adapter_version</c>, <c>reason</c>, <c>gateway</c>. Callers build tag lists through
/// <see cref="MetricLabels"/> to stay on that contract.
/// </para>
/// </remarks>
public static class TelematicsInstrumentation
{
    /// <summary>
    /// The stable <see cref="System.Diagnostics.ActivitySource"/> name. Register it with
    /// <c>AddSource(TelematicsInstrumentation.ActivitySourceName)</c>; filter on it in Tempo/Jaeger.
    /// </summary>
    public const string ActivitySourceName = "Opstrax.Telematics.Gateway";

    /// <summary>
    /// The stable <see cref="System.Diagnostics.Metrics.Meter"/> name. Register it with
    /// <c>AddMeter(TelematicsInstrumentation.MeterName)</c>. This is the gateway-scoped meter;
    /// instrument names still carry the system-wide <c>opstrax_telematics_*</c> prefix so they
    /// aggregate cleanly across components in Prometheus.
    /// </summary>
    public const string MeterName = "Opstrax.Telematics.Gateway";

    /// <summary>Version stamped on both the source and the meter — pins a span/metric to a build.</summary>
    public static readonly string Version =
        typeof(TelematicsInstrumentation).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(TelematicsInstrumentation).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";

    /// <summary>The gateway's tracing source. Every pipeline span originates here.</summary>
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, Version);

    /// <summary>The gateway's metrics meter. Every instrument below is created against it.</summary>
    public static readonly Meter Meter = new(MeterName, Version);

    // ── Counters (monotonic) ─────────────────────────────────────────────────────

    /// <summary>Packets that passed every validation stage and were published. Labels: company_id, protocol, adapter.</summary>
    public static readonly Counter<long> PacketsAccepted =
        Meter.CreateCounter<long>("opstrax_telematics_packets_accepted", unit: "{packet}",
            description: "Packets that passed all validation and were accepted for publish.");

    /// <summary>Packets rejected at any stage. Labels: protocol, adapter, reason (+ company_id when resolved).</summary>
    public static readonly Counter<long> PacketsRejected =
        Meter.CreateCounter<long>("opstrax_telematics_packets_rejected", unit: "{packet}",
            description: "Packets rejected at any stage; reason matches the span rejection_reason enum.");

    /// <summary>Well-formed packets whose IMEI/key resolved to no provisioned device. Labels: gateway, protocol.</summary>
    public static readonly Counter<long> UnknownDevices =
        Meter.CreateCounter<long>("opstrax_telematics_unknown_devices", unit: "{packet}",
            description: "Authenticated-ish packets whose identity resolves to no provisioned device.");

    /// <summary>HMAC / gateway-signature rejects. Labels: gateway, protocol, reason.</summary>
    public static readonly Counter<long> AuthFailures =
        Meter.CreateCounter<long>("opstrax_telematics_auth_failures", unit: "{packet}",
            description: "HMAC / gateway-signature verification failures.");

    /// <summary>Timestamp outside the freshness window / nonce reuse. Labels: protocol, company_id.</summary>
    public static readonly Counter<long> Replays =
        Meter.CreateCounter<long>("opstrax_telematics_replay_rejections", unit: "{packet}",
            description: "Packets rejected as replays (stale timestamp or reused nonce). Security-relevant.");

    /// <summary>Same event_id/nonce seen again. Labels: protocol, company_id.</summary>
    public static readonly Counter<long> Duplicates =
        Meter.CreateCounter<long>("opstrax_telematics_duplicate_packets", unit: "{packet}",
            description: "Packets whose event_id / nonce was already seen.");

    /// <summary>Reading older than the last committed position for that vehicle. Labels: protocol, company_id.</summary>
    public static readonly Counter<long> OutOfOrder =
        Meter.CreateCounter<long>("opstrax_telematics_out_of_order", unit: "{packet}",
            description: "Readings older than the last committed position for the vehicle.");

    // ── Frame integrity and session safety ──────────────────────────────────────
    // These were previously counted only in GatewayMetrics, which is a process-local, free-running
    // struct that no exporter scrapes. Counters an operator cannot reach are not observability, so
    // the ones worth waking somebody for are emitted here as well.

    /// <summary>
    /// Complete frames read off the wire, CRC-valid and CRC-invalid alike. The denominator for a
    /// corruption rate. Labels: protocol.
    /// </summary>
    public static readonly Counter<long> FramesReceived =
        Meter.CreateCounter<long>("opstrax_telematics_frames_received", unit: "{frame}",
            description: "Complete protocol frames read off the wire, whether or not their checksum verified.");

    /// <summary>
    /// Frames whose CRC did not verify. Such a frame yields no message and never reaches
    /// normalization, storage or an acknowledgement. Labels: protocol.
    /// </summary>
    /// <remarks>
    /// This is the series that separates "the device is silent" from "the device is shouting
    /// through noise". Alert on the RATE against <see cref="FramesReceived"/>, not the absolute
    /// count: a cellular fleet always corrupts a few frames, and a fixed threshold either never
    /// fires or fires constantly depending on fleet size.
    /// </remarks>
    public static readonly Counter<long> CrcFailures =
        Meter.CreateCounter<long>("opstrax_telematics_crc_failures", unit: "{frame}",
            description: "Frames rejected because their CRC did not verify.");

    /// <summary>
    /// Logins that tried to change the device identity of an already-bound socket. Labels: protocol.
    /// </summary>
    /// <remarks>
    /// Not a protocol event. A device does not re-introduce itself as a different device, so any
    /// non-zero value is a badly broken tracker or an attempt to attribute traffic to another
    /// tenant's vehicle. This series should sit flat at zero and page when it does not.
    /// </remarks>
    public static readonly Counter<long> SessionIdentityViolations =
        Meter.CreateCounter<long>("opstrax_telematics_session_identity_violations", unit: "{login}",
            description: "Logins that attempted to re-identify an already-bound connection.");

    /// <summary>Sessions torn down because the same device authenticated on a newer socket. Labels: protocol.</summary>
    /// <remarks>
    /// Routine on a roaming fleet: the bearer dropped without a FIN and the tracker redialled. A
    /// sustained spike means devices are reconnect-looping, which is a carrier or power problem.
    /// </remarks>
    public static readonly Counter<long> DuplicateSessionsDisplaced =
        Meter.CreateCounter<long>("opstrax_telematics_duplicate_sessions_displaced", unit: "{session}",
            description: "Sessions closed because the device authenticated on a newer socket.");

    /// <summary>
    /// Fixes whose displacement from the device's previous position is impossible in the elapsed
    /// time. Labels: protocol.
    /// </summary>
    /// <remarks>
    /// A slow trickle is spoofing, tampering, or a device moved between vehicles — investigate per
    /// device. A sudden spike <b>across many distinct devices at once</b> is not a fleet of
    /// teleporting trucks: it is a decoder or coordinate-handling regression that just shipped, and
    /// it is the single highest-value alert in this file. Alert on distinct devices affected, not
    /// on the raw count, so one tampered unit reporting continuously cannot masquerade as a fleet
    /// event and a genuine fleet event cannot hide inside one device's noise.
    /// </remarks>
    public static readonly Counter<long> TeleportSuspected =
        Meter.CreateCounter<long>("opstrax_telematics_teleport_suspected", unit: "{packet}",
            description: "Fixes whose displacement from the previous position is physically impossible.");

    /// <summary>Fixes whose device-reported ground speed exceeds the physical ceiling. Labels: protocol.</summary>
    public static readonly Counter<long> ImpossibleSpeed =
        Meter.CreateCounter<long>("opstrax_telematics_impossible_speed", unit: "{packet}",
            description: "Fixes whose device-reported ground speed exceeds the physical ceiling.");

    // ── Histograms (latency distributions) ───────────────────────────────────────

    /// <summary>
    /// Decode-span duration. Labels: protocol, adapter, adapter_version. Bucket boundaries are
    /// applied by the provider view in <c>AddTelematicsObservability</c> (1..1000 ms).
    /// </summary>
    public static readonly Histogram<double> DecodeLatencyMs =
        Meter.CreateHistogram<double>("opstrax_telematics_decode_latency_ms", unit: "ms",
            description: "Adapter decode-stage wall time distribution.");

    /// <summary>
    /// End-to-end gateway latency: packet-receive → publish (the span the gateway can actually
    /// observe; the full receive→DB-commit pipeline histogram lives in the projection worker as
    /// <c>opstrax_telematics_processing_latency_ms</c>). Labels: protocol, company_id.
    /// Bucket boundaries applied by the provider view (5..5000 ms).
    /// </summary>
    public static readonly Histogram<double> E2eLatencyMs =
        Meter.CreateHistogram<double>("opstrax_telematics_e2e_latency_ms", unit: "ms",
            description: "Gateway end-to-end (receive→publish) wall time distribution.");

    // ── Up-down counters (gauge-like) ────────────────────────────────────────────

    /// <summary>Currently-open device sockets. Labels: gateway, protocol. Increment on accept, decrement on close.</summary>
    public static readonly UpDownCounter<long> ActiveConnections =
        Meter.CreateUpDownCounter<long>("opstrax_telematics_active_connections", unit: "{connection}",
            description: "Currently-open device sockets (gauge-like).");

    /// <summary>
    /// Distinct devices flagged for an impossible displacement in the last 10 minutes. Registered
    /// by the composition root against the live <c>FixPlausibilityGuard</c>.
    /// </summary>
    /// <remarks>
    /// A gauge rather than a counter because the question is "how many vehicles are affected right
    /// now", which is what separates one tampered unit from a decoder regression. Aggregated
    /// in-process so the metric keeps a single series; a <c>device_id</c> label would let whoever
    /// chooses the IMEIs choose the cardinality.
    /// </remarks>
    public const string TeleportDistinctDevicesName = "opstrax_telematics_teleport_distinct_devices";

    /// <summary>The window the <see cref="TeleportDistinctDevicesName"/> gauge reports over.</summary>
    public static readonly TimeSpan TeleportDistinctDevicesWindow = TimeSpan.FromMinutes(10);

    // ── Bounded metric-label keys (NOT span-attribute keys) ─────────────────────

    /// <summary>Bounded metric label keys. Distinct from the dotted span-attribute keys in <see cref="TelematicsAttributes"/>.</summary>
    public static class MetricLabels
    {
        /// <summary><c>company_id</c> — bounded (4 real tenants); makes every series tenant-sliceable.</summary>
        public const string CompanyId = "company_id";

        /// <summary><c>protocol</c> — pt40|gt06|j1939|obd2|mqtt.</summary>
        public const string Protocol = "protocol";

        /// <summary><c>adapter</c> — adapter name.</summary>
        public const string Adapter = "adapter";

        /// <summary><c>adapter_version</c> — adapter/codec version.</summary>
        public const string AdapterVersion = "adapter_version";

        /// <summary><c>reason</c> — matches the span <c>rejection_reason</c> enum.</summary>
        public const string Reason = "reason";

        /// <summary><c>gateway</c> — gateway instance / pod id.</summary>
        public const string Gateway = "gateway";
    }
}
