using Opstrax.Telematics.Contracts.Provenance;
using Opstrax.Telematics.Contracts.Quality;
using Opstrax.Telematics.Contracts.Signals;

namespace Opstrax.Telematics.Contracts;

/// <summary>
/// The single, schema-versioned, transport- and protocol-independent representation of
/// one telematics observation once it has been decoded, identity-resolved and
/// normalized. Every producer in the fabric — hardware gateways, vendor-cloud pollers,
/// mobile apps, simulators, importers — converges on this shape, and every consumer
/// (map, rules engine, storage, analytics) reads only this shape. Nothing downstream of
/// normalization should ever need to know which wire protocol produced the event.
/// </summary>
/// <remarks>
/// <para>
/// The type is an immutable record built with object-initializer syntax. Required
/// ownership/timing/provenance fields have no defaults on purpose so a half-populated
/// event fails to compile rather than silently carrying zeroed identity. The optional
/// typed fleet fields and the <see cref="Signals"/> bag both default to "absent".
/// </para>
/// <para>
/// <b>Ownership is never taken from the packet.</b> <see cref="TenantId"/>,
/// <see cref="CompanyId"/>, <see cref="DeviceId"/> and <see cref="VehicleId"/> are the
/// registry-resolved values (see <see cref="Identity.IDeviceRegistry"/>), not whatever a
/// frame claimed.
/// </para>
/// </remarks>
public sealed record CanonicalTelemetryEvent
{
    /// <summary>
    /// The schema version this event was produced against. Bumped only on a
    /// breaking change to the canonical shape; consumers gate on it to stay
    /// forward/backward compatible.
    /// </summary>
    public required int SchemaVersion { get; init; }

    /// <summary>The current canonical schema version emitted by this contract assembly.</summary>
    public const int CurrentSchemaVersion = 1;

    // ── Identity of the observation ────────────────────────────────────────────

    /// <summary>Globally unique id for this specific observation; the idempotency key for storage.</summary>
    public required Guid EventId { get; init; }

    /// <summary>
    /// Correlates every event and side-effect derived from the same originating frame or
    /// ingest request, so a fix can be traced end-to-end across pipeline stages.
    /// </summary>
    public required Guid CorrelationId { get; init; }

    // ── Timing (three distinct clocks, never conflated) ────────────────────────

    /// <summary>The device's own fix timestamp (UTC) — when the observation actually happened in the field.</summary>
    public required DateTime OccurredAtDeviceUtc { get; init; }

    /// <summary>When the gateway received the frame (UTC). Compared against the device clock to detect skew.</summary>
    public required DateTime ReceivedAtGatewayUtc { get; init; }

    /// <summary>When normalization produced this canonical event (UTC).</summary>
    public required DateTime NormalizedAtUtc { get; init; }

    // ── Registry-resolved ownership ────────────────────────────────────────────

    /// <summary>Owning tenant, resolved from the registry — the authoritative isolation scope.</summary>
    public required Guid TenantId { get; init; }

    /// <summary>Owning company within the tenant, resolved from the registry.</summary>
    public required long CompanyId { get; init; }

    /// <summary>Fabric device id the identity claim resolved to.</summary>
    public required string DeviceId { get; init; }

    /// <summary>Vehicle the device was bound to at observation time, if any.</summary>
    public long? VehicleId { get; init; }

    // ── Provenance ─────────────────────────────────────────────────────────────

    /// <summary>Origin category of the event (device, vendor cloud, simulator, seed, …).</summary>
    public required TelemetrySource Source { get; init; }

    /// <summary>Wire transport the originating frame arrived over.</summary>
    public required Transport Transport { get; init; }

    /// <summary>The on-wire protocol name, for example <c>"GT06"</c>.</summary>
    public required string ProtocolName { get; init; }

    /// <summary>The on-wire protocol version, when the protocol distinguishes one; otherwise empty.</summary>
    public string ProtocolVersion { get; init; } = string.Empty;

    /// <summary>Name of the adapter that decoded the frame (mirrors <see cref="Adapters.AdapterMetadata.Name"/>).</summary>
    public required string AdapterName { get; init; }

    /// <summary>Version of the adapter that decoded the frame (mirrors <see cref="Adapters.AdapterMetadata.Version"/>).</summary>
    public required string AdapterVersion { get; init; }

    // ── Geospatial ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The geographic fix, when this observation carried one. <see langword="null"/> for
    /// non-positional messages such as heartbeats or pure status/diagnostics reports.
    /// </summary>
    public GeoPoint? Location { get; init; }

    // ── Signals: extensible VSS/COVESA bag + first-class typed fields ───────────

    /// <summary>
    /// The extensible, dotted-namespace signal bag (VSS/COVESA-inspired), keyed by paths
    /// such as <c>"Vehicle.Powertrain.Odometer"</c>. This is the open extension point for
    /// any signal a device reports that has no first-class field. Defaults to empty.
    /// See <see cref="Signals.VssSignals"/> for well-known keys.
    /// </summary>
    public IReadOnlyDictionary<string, SignalValue> Signals { get; init; } =
        new Dictionary<string, SignalValue>();

    /// <summary>Ignition switch state, when reported.</summary>
    public bool? IgnitionOn { get; init; }

    /// <summary>Odometer reading in kilometres, when reported.</summary>
    public double? OdometerKm { get; init; }

    /// <summary>Fuel level as a percentage [0,100], when reported.</summary>
    public double? FuelPercent { get; init; }

    /// <summary>Starter battery voltage in volts, when reported.</summary>
    public double? BatteryVoltage { get; init; }

    /// <summary>Engine running state, when reported (distinct from ignition on some hardware).</summary>
    public bool? EngineOn { get; init; }

    /// <summary>Engine coolant temperature in degrees Celsius, when reported.</summary>
    public double? CoolantTempC { get; init; }

    /// <summary>Active diagnostic trouble codes reported with this observation. Defaults to empty.</summary>
    public IReadOnlyList<string> DtcCodes { get; init; } = Array.Empty<string>();

    // ── Quality ────────────────────────────────────────────────────────────────

    /// <summary>The quality observations raised for this event by the normalization stage.</summary>
    public QualityFlags Quality { get; init; }

    /// <summary>
    /// Aggregate trust in this event in the closed interval [0,1], derived from provenance
    /// and <see cref="Quality"/>. 1 is fully trusted; 0 is untrusted (for example a
    /// suspected replay or spoof). Policy — not storage — decides what a given score gates.
    /// </summary>
    public double TrustScore { get; init; } = 1.0;

    /// <summary>
    /// Aggregate confidence that the decoded values are correct, in [0,1] — a measure of
    /// decode/sensor certainty, orthogonal to <see cref="TrustScore"/> (which measures
    /// whether the source can be trusted).
    /// </summary>
    public double Confidence { get; init; } = 1.0;
}
