using System.Buffers.Binary;
using Opstrax.Telematics.Contracts.Signals;

namespace Opstrax.Telematics.Protocols.J1939;

/// <summary>
/// Decodes only the PGN/SPN mappings in <see cref="SupportedSignals"/>. A mapping
/// in this software catalog is not evidence that a particular ECU, adapter, vehicle,
/// firmware version, or installation has been physically verified.
/// </summary>
public static class J1939SignalDecoder
{
    public const int ElectronicEngineController1Pgn = 61444;
    public const int EngineHoursRevolutionsPgn = 65253;
    public const int VehicleElectricalPower1Pgn = 65271;
    public const int EngineSpeedSpn = 190;
    public const int EngineTotalHoursSpn = 247;
    public const int BatteryPotentialSpn = 168;

    private const byte MaximumValidMostSignificantByte = 0xFA;
    private const byte MinimumParameterSpecificMostSignificantByte = 0xFB;
    private const byte MaximumParameterSpecificMostSignificantByte = 0xFD;
    private const byte ErrorIndicatorMostSignificantByte = 0xFE;
    private const byte NotAvailableMostSignificantByte = 0xFF;

    private static readonly IReadOnlyList<J1939SignalDefinition> Catalog = Array.AsReadOnly(
    [
        new J1939SignalDefinition(
            ElectronicEngineController1Pgn,
            "Electronic Engine Controller 1 (EEC1)",
            PgnLengthBytes: 8,
            EngineSpeedSpn,
            "Engine Speed",
            VssSignals.EngineSpeed,
            StartByteOneBased: 4,
            LengthBytes: 2,
            Resolution: 0.125d,
            Offset: 0d,
            Unit: "rpm",
            Endianness: J1939SignalEndianness.LittleEndian,
            SpecificationReference: "SAE J1939DA; Cummins A079E226 section 6.3.4"),
        new J1939SignalDefinition(
            EngineHoursRevolutionsPgn,
            "Engine Hours, Revolutions (HOURS)",
            PgnLengthBytes: 8,
            EngineTotalHoursSpn,
            "Engine Total Hours of Operation",
            VssSignals.EngineHours,
            StartByteOneBased: 1,
            LengthBytes: 4,
            Resolution: 0.05d,
            Offset: 0d,
            Unit: "h",
            Endianness: J1939SignalEndianness.LittleEndian,
            SpecificationReference: "SAE J1939DA; Cummins A079E226 section 6.3.4"),
        new J1939SignalDefinition(
            VehicleElectricalPower1Pgn,
            "Vehicle Electrical Power 1 (VEP1)",
            PgnLengthBytes: 8,
            BatteryPotentialSpn,
            "Battery Potential / Power Input 1",
            VssSignals.BatteryVoltage,
            StartByteOneBased: 5,
            LengthBytes: 2,
            Resolution: 0.05d,
            Offset: 0d,
            Unit: "V",
            Endianness: J1939SignalEndianness.LittleEndian,
            SpecificationReference: "SAE J1939DA; Cummins A079E226 section 6.3.4"),
    ]);

    private static readonly IReadOnlyDictionary<int, IReadOnlyList<J1939SignalDefinition>> CatalogByPgn =
        Catalog
            .GroupBy(definition => definition.Pgn)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<J1939SignalDefinition>)Array.AsReadOnly(group.ToArray()));

    /// <summary>
    /// Immutable catalog of mappings this decoder is allowed to interpret. Absence
    /// from this list means unsupported, regardless of whether a value appears plausible.
    /// </summary>
    public static IReadOnlyList<J1939SignalDefinition> SupportedSignals => Catalog;

    /// <summary>
    /// Attempts to decode one already acquired message. Unsupported PGNs return false.
    /// A supported PGN with the wrong fixed payload length fails closed.
    /// </summary>
    public static bool TryDecode(J1939AcquiredMessage message, out J1939SignalDecodeResult? result)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!CatalogByPgn.TryGetValue(message.Pgn, out var definitions))
        {
            result = null;
            return false;
        }

        var requiredLength = definitions.Select(definition => definition.PgnLengthBytes).Distinct().Single();
        if (message.Payload.Length != requiredLength)
        {
            throw new J1939SignalDecodeException(
                message.Pgn,
                message.SourceAddress,
                message.FirstFrameAt,
                message.CompletedAt,
                message.Frames.Select(frame => frame.CaptureReference).ToArray(),
                $"Supported J1939 PGN {message.Pgn} requires exactly {requiredLength} payload bytes; received {message.Payload.Length}.");
        }

        var observations = definitions
            .Select(definition => Decode(message.Payload.Span, definition))
            .ToArray();
        result = new J1939SignalDecodeResult(message, Array.AsReadOnly(observations));
        return true;
    }

    private static J1939SignalObservation Decode(
        ReadOnlySpan<byte> payload,
        J1939SignalDefinition definition)
    {
        if (definition.LengthBytes is not (2 or 4) ||
            definition.Endianness != J1939SignalEndianness.LittleEndian)
            throw new InvalidOperationException("The J1939 signal catalog contains a decoder shape that is not implemented.");

        var offset = definition.StartByteOneBased - 1;
        var encoded = payload.Slice(offset, definition.LengthBytes);
        var rawValue = definition.LengthBytes switch
        {
            2 => BinaryPrimitives.ReadUInt16LittleEndian(encoded),
            4 => BinaryPrimitives.ReadUInt32LittleEndian(encoded),
            _ => throw new InvalidOperationException("The J1939 signal catalog contains an unsupported numeric width."),
        };
        var mostSignificantByte = encoded[^1];
        var status = mostSignificantByte switch
        {
            <= MaximumValidMostSignificantByte => J1939SignalStatus.Valid,
            >= MinimumParameterSpecificMostSignificantByte and <= MaximumParameterSpecificMostSignificantByte =>
                J1939SignalStatus.ParameterSpecificIndicator,
            ErrorIndicatorMostSignificantByte => J1939SignalStatus.ErrorIndicator,
            NotAvailableMostSignificantByte => J1939SignalStatus.NotAvailable,
        };

        var value = status == J1939SignalStatus.Valid
            ? rawValue * definition.Resolution + definition.Offset
            : (double?)null;

        return new J1939SignalObservation(definition, status, rawValue, value);
    }
}

public enum J1939SignalEndianness
{
    LittleEndian,
}

/// <summary>
/// Interpretation outcome for one mapped SPN. Indicator states never carry a
/// numeric value and therefore cannot be mistaken for a genuine zero reading.
/// </summary>
public enum J1939SignalStatus
{
    Valid,
    ParameterSpecificIndicator,
    ErrorIndicator,
    NotAvailable,
}

public sealed record J1939SignalDefinition(
    int Pgn,
    string PgnName,
    int PgnLengthBytes,
    int Spn,
    string SignalName,
    string CanonicalPath,
    int StartByteOneBased,
    int LengthBytes,
    double Resolution,
    double Offset,
    string Unit,
    J1939SignalEndianness Endianness,
    string SpecificationReference);

public sealed record J1939SignalObservation(
    J1939SignalDefinition Definition,
    J1939SignalStatus Status,
    ulong RawValue,
    double? Value);

/// <summary>
/// Decoded signal observations paired with the complete acquired message so source
/// address, capture time, adapter, channel, and per-frame references remain attached.
/// </summary>
public sealed record J1939SignalDecodeResult(
    J1939AcquiredMessage Message,
    IReadOnlyList<J1939SignalObservation> Observations);

/// <summary>
/// A supported, evidence-bearing PGN failed semantic decoding. Raw payload bytes are
/// deliberately omitted from the exception text and fields.
/// </summary>
public sealed class J1939SignalDecodeException : Exception
{
    public J1939SignalDecodeException(
        int pgn,
        byte sourceAddress,
        DateTimeOffset firstFrameAt,
        DateTimeOffset completedAt,
        IReadOnlyList<string> captureReferences,
        string message)
        : base(message)
    {
        Pgn = pgn;
        SourceAddress = sourceAddress;
        FirstFrameAt = firstFrameAt;
        CompletedAt = completedAt;
        CaptureReferences = Array.AsReadOnly(captureReferences.ToArray());
    }

    public int Pgn { get; }
    public byte SourceAddress { get; }
    public DateTimeOffset FirstFrameAt { get; }
    public DateTimeOffset CompletedAt { get; }
    public IReadOnlyList<string> CaptureReferences { get; }
}
