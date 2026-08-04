namespace Opstrax.Telematics.Contracts.Adapters;

/// <summary>
/// Self-describing metadata a protocol adapter publishes so the gateway can select,
/// version and audit it as a plugin. Metadata is immutable and cheap to read; it is
/// queried during adapter discovery and included in provenance for every event the
/// adapter produces.
/// </summary>
/// <param name="Name">
/// Stable adapter identifier, for example <c>"GT06"</c>. Written into
/// <c>CanonicalTelemetryEvent.AdapterName</c> and used for routing/telemetry.
/// </param>
/// <param name="Version">
/// The adapter implementation's semantic version, for example <c>"1.4.0"</c>. This
/// versions the <em>decoder</em>, distinct from the on-wire protocol version.
/// </param>
/// <param name="SupportedModels">Device models this adapter is validated against (may be empty if model-agnostic).</param>
/// <param name="SupportedFirmware">Firmware ranges/identifiers this adapter is validated against (may be empty).</param>
public readonly record struct AdapterMetadata(
    string Name,
    string Version,
    IReadOnlyList<string> SupportedModels,
    IReadOnlyList<string> SupportedFirmware);
