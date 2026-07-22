namespace Opstrax.Telematics.Contracts.Eventing;

/// <summary>
/// The single source of truth for the names of every logical topic on the telematics event
/// backbone. Producers and consumers must reference these constants rather than typing topic
/// strings, so a rename is a compile-time break instead of a silent mis-route.
/// </summary>
/// <remarks>
/// <para>
/// The vocabulary is split into three lanes that mirror the pipeline: the <b>ingest</b> lane
/// (<c>telemetry.raw → decoded/rejected → normalized</c>), the <b>enriched domain</b> lane
/// (validated positions, normalized signals, device health, trips, diagnostics, safety, media)
/// and the <b>control</b> lane (the command request/dispatch/ack/fail lifecycle). A single
/// cross-cutting <c>integration.deadletter</c> topic captures anything that could not be
/// processed on its home topic.
/// </para>
/// <para>
/// Topic <em>names</em> are broker-neutral logical identifiers. The physical mapping to a
/// Kafka/Redpanda topic (and any environment prefix such as <c>prod.</c> / <c>dev.</c>) is a
/// deployment concern applied by the backbone implementation, not encoded here.
/// </para>
/// </remarks>
public static class TelematicsTopics
{
    // ── Ingest lane ────────────────────────────────────────────────────────────

    /// <summary>Opaque, undecoded inbound frames exactly as they arrived off the wire.</summary>
    public const string TelemetryRaw = "telemetry.raw";

    /// <summary>Frames successfully decoded by a protocol adapter but not yet identity-resolved or normalized.</summary>
    public const string TelemetryDecoded = "telemetry.decoded";

    /// <summary>Frames that could not be decoded, failed the parser guard, or failed identity resolution.</summary>
    public const string TelemetryRejected = "telemetry.rejected";

    /// <summary>Fully normalized <see cref="CanonicalTelemetryEvent"/>s — the fan-out point for all downstream consumers.</summary>
    public const string TelemetryNormalized = "telemetry.normalized";

    // ── Enriched domain lane ─────────────────────────────────────────────────────

    /// <summary>Position fixes that passed geospatial/plausibility validation and are safe for the live map.</summary>
    public const string VehiclePositionValidated = "vehicle.position.validated";

    /// <summary>Individual VSS/COVESA signals normalized out of the canonical event for signal-level consumers.</summary>
    public const string VehicleSignalNormalized = "vehicle.signal.normalized";

    /// <summary>Device connectivity/battery/firmware health and heartbeat state transitions.</summary>
    public const string DeviceHealth = "device.health";

    /// <summary>Trip start/stop/segment lifecycle events derived from ignition and motion.</summary>
    public const string TripLifecycle = "trip.lifecycle";

    /// <summary>Diagnostic trouble codes and maintenance-relevant vehicle diagnostic events.</summary>
    public const string DiagnosticEvent = "diagnostic.event";

    /// <summary>Safety-relevant events: harsh braking, speeding, crash detection, panic/SOS.</summary>
    public const string SafetyEvent = "safety.event";

    /// <summary>Metadata for uploaded media (dashcam clips, photos) — pointers only, never the bytes.</summary>
    public const string MediaMetadata = "media.metadata";

    // ── Control lane (downlink command lifecycle) ───────────────────────────────

    /// <summary>An operator/system has requested a downlink command; not yet sent to a device.</summary>
    public const string CommandRequested = "command.requested";

    /// <summary>A requested command has been encoded and dispatched toward the device/gateway.</summary>
    public const string CommandDispatched = "command.dispatched";

    /// <summary>The device (or gateway) acknowledged receipt/execution of a dispatched command.</summary>
    public const string CommandAcknowledged = "command.acknowledged";

    /// <summary>A command could not be dispatched, timed out, or was rejected by the device.</summary>
    public const string CommandFailed = "command.failed";

    // ── Cross-cutting ────────────────────────────────────────────────────────────

    /// <summary>Terminal dead-letter sink for envelopes that exhausted retries on their home topic.</summary>
    public const string IntegrationDeadLetter = "integration.deadletter";

    /// <summary>
    /// Every topic name on the backbone, in pipeline order. Useful for provisioning,
    /// admin tooling and asserting that a broker has the full set of topics created.
    /// </summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        TelemetryRaw,
        TelemetryDecoded,
        TelemetryRejected,
        TelemetryNormalized,
        VehiclePositionValidated,
        VehicleSignalNormalized,
        DeviceHealth,
        TripLifecycle,
        DiagnosticEvent,
        SafetyEvent,
        MediaMetadata,
        CommandRequested,
        CommandDispatched,
        CommandAcknowledged,
        CommandFailed,
        IntegrationDeadLetter,
    };
}
