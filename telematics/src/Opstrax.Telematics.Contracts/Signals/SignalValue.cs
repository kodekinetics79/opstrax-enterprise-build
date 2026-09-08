using System.Text.Json.Serialization;
using Opstrax.Telematics.Contracts.Provenance;

namespace Opstrax.Telematics.Contracts.Signals;

/// <summary>
/// A single extensible signal reading carried in the VSS/COVESA-inspired signal
/// bag of a <c>CanonicalTelemetryEvent</c>. Signals are keyed by a dotted
/// namespace path (for example <c>"Vehicle.Powertrain.Odometer"</c>) and each
/// value is self-describing so that consumers can reason about a signal without
/// out-of-band schema knowledge.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SignalValue"/> is an immutable value object. The <see cref="Value"/>
/// is stored boxed so the bag can carry heterogeneous scalar types (numeric,
/// boolean, string) without a discriminated-union type per signal. Adapters SHOULD
/// prefer primitive CLR scalars (<see cref="double"/>, <see cref="long"/>,
/// <see cref="bool"/>, <see cref="string"/>) for cross-language stability.
/// </para>
/// </remarks>
public sealed record SignalValue
{
    /// <summary>Creates a self-describing signal reading.</summary>
    /// <param name="value">The boxed scalar reading. May be <see langword="null"/> when a signal is explicitly absent.</param>
    /// <param name="unit">The unit of measure (for example <c>"km"</c>, <c>"kph"</c>, <c>"V"</c>, <c>"degC"</c>, <c>"%"</c>). Use <see cref="string.Empty"/> for unitless/boolean signals.</param>
    /// <param name="source">Provenance of this specific reading; defaults to <see cref="TelemetrySource.DirectDevice"/>.</param>
    /// <param name="confidence">Per-signal confidence in the closed interval [0,1].</param>
    /// <param name="availability">Whether consumers may use the scalar as a current measurement.</param>
    public SignalValue(
        object? value,
        string unit,
        TelemetrySource source = TelemetrySource.DirectDevice,
        double confidence = 1.0,
        SignalAvailability availability = SignalAvailability.Available)
    {
        Value = value;
        Unit = unit ?? string.Empty;
        Source = source;
        Confidence = confidence < 0 ? 0 : confidence > 1 ? 1 : confidence;
        Availability = availability;
    }

    /// <summary>The boxed scalar reading. <see langword="null"/> means "explicitly absent" rather than "unknown".</summary>
    public object? Value { get; init; }

    /// <summary>Unit of measure for <see cref="Value"/>; <see cref="string.Empty"/> when unitless.</summary>
    public string Unit { get; init; }

    /// <summary>Provenance of this individual reading, which may differ from the enclosing event's source.</summary>
    public TelemetrySource Source { get; init; }

    /// <summary>Confidence for this reading, clamped to the closed interval [0,1].</summary>
    public double Confidence { get; init; }

    /// <summary>
    /// Whether the scalar is currently usable. Consumers must not treat a stale,
    /// unavailable, error, or parameter-specific value as a current measurement.
    /// </summary>
    public SignalAvailability Availability { get; init; }
}

/// <summary>Protocol-neutral availability state carried with a canonical signal.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SignalAvailability
{
    /// <summary>The value is a current, valid measurement.</summary>
    Available,

    /// <summary>The value is valid historical evidence but older than its freshness budget.</summary>
    Stale,

    /// <summary>The wire value is a protocol-defined parameter-specific indicator not interpreted here.</summary>
    ParameterSpecific,

    /// <summary>The source reported a protocol-level error indicator.</summary>
    Error,

    /// <summary>The source explicitly reported that the signal is unavailable.</summary>
    NotAvailable,
}
