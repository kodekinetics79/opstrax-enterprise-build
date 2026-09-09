using Opstrax.Telematics.Contracts.Signals;
using Opstrax.Telematics.Protocols.J1939;

namespace Opstrax.Telematics.Protocols.Tests;

public sealed class J1939SignalDecoderTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Catalog_declares_only_the_three_reviewed_signal_mappings()
    {
        Assert.Equal(3, J1939SignalDecoder.SupportedSignals.Count);
        var definition = Assert.Single(
            J1939SignalDecoder.SupportedSignals,
            entry => entry.Spn == J1939SignalDecoder.EngineSpeedSpn);

        Assert.Equal(J1939SignalDecoder.ElectronicEngineController1Pgn, definition.Pgn);
        Assert.Equal("Electronic Engine Controller 1 (EEC1)", definition.PgnName);
        Assert.Equal(8, definition.PgnLengthBytes);
        Assert.Equal(J1939SignalDecoder.EngineSpeedSpn, definition.Spn);
        Assert.Equal("Engine Speed", definition.SignalName);
        Assert.Equal(VssSignals.EngineSpeed, definition.CanonicalPath);
        Assert.Equal(4, definition.StartByteOneBased);
        Assert.Equal(2, definition.LengthBytes);
        Assert.Equal(0.125d, definition.Resolution);
        Assert.Equal(0d, definition.Offset);
        Assert.Equal("rpm", definition.Unit);
        Assert.Equal(J1939SignalEndianness.LittleEndian, definition.Endianness);
        Assert.Contains("SAE J1939DA", definition.SpecificationReference, StringComparison.Ordinal);
    }

    [Fact]
    public void Valid_engine_speed_is_decoded_little_endian_with_message_provenance()
    {
        var message = Message([0xFF, 0xFF, 0xFF, 0xE0, 0x2E, 0xFF, 0xFF, 0xFF]);

        var supported = J1939SignalDecoder.TryDecode(message, out var result);

        Assert.True(supported);
        Assert.NotNull(result);
        Assert.Same(message, result!.Message);
        var observation = Assert.Single(result.Observations);
        Assert.Equal(J1939SignalStatus.Valid, observation.Status);
        Assert.Equal(12000UL, observation.RawValue);
        Assert.Equal(1500d, observation.Value);
        Assert.Equal(VssSignals.EngineSpeed, observation.Definition.CanonicalPath);
        Assert.Equal("capture-eec1", Assert.Single(result.Message.Frames).CaptureReference);
        Assert.Equal((byte)0x00, result.Message.SourceAddress);
        Assert.Equal(T0, result.Message.CompletedAt);
    }

    [Fact]
    public void Four_byte_engine_hours_are_scaled_without_losing_precision()
    {
        var message = Message(
            [0x72, 0x60, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF],
            J1939SignalDecoder.EngineHoursRevolutionsPgn);

        Assert.True(J1939SignalDecoder.TryDecode(message, out var result));

        var observation = Assert.Single(result!.Observations);
        Assert.Equal(J1939SignalDecoder.EngineTotalHoursSpn, observation.Definition.Spn);
        Assert.Equal(VssSignals.EngineHours, observation.Definition.CanonicalPath);
        Assert.Equal(J1939SignalStatus.Valid, observation.Status);
        Assert.Equal(24690UL, observation.RawValue);
        Assert.Equal(1234.5d, observation.Value);
        Assert.Equal("h", observation.Definition.Unit);
    }

    [Fact]
    public void Battery_potential_maps_to_the_existing_canonical_voltage_signal()
    {
        var message = Message(
            [0xFF, 0xFF, 0xFF, 0xFF, 0xFC, 0x00, 0xFF, 0xFF],
            J1939SignalDecoder.VehicleElectricalPower1Pgn);

        Assert.True(J1939SignalDecoder.TryDecode(message, out var result));

        var observation = Assert.Single(result!.Observations);
        Assert.Equal(J1939SignalDecoder.BatteryPotentialSpn, observation.Definition.Spn);
        Assert.Equal(VssSignals.BatteryVoltage, observation.Definition.CanonicalPath);
        Assert.Equal(J1939SignalStatus.Valid, observation.Status);
        Assert.Equal(252UL, observation.RawValue);
        Assert.Equal(12.6d, observation.Value!.Value, precision: 6);
        Assert.Equal("V", observation.Definition.Unit);
    }

    [Theory]
    [InlineData(0xFAFFFFFF, J1939SignalStatus.Valid, 210554060.75d)]
    [InlineData(0xFB000000, J1939SignalStatus.ParameterSpecificIndicator, null)]
    [InlineData(0xFDFFFFFF, J1939SignalStatus.ParameterSpecificIndicator, null)]
    [InlineData(0xFE000001, J1939SignalStatus.ErrorIndicator, null)]
    [InlineData(0xFEFFFFFF, J1939SignalStatus.ErrorIndicator, null)]
    [InlineData(0xFF000001, J1939SignalStatus.NotAvailable, null)]
    [InlineData(0xFFFFFFFF, J1939SignalStatus.NotAvailable, null)]
    public void Four_byte_indicator_ranges_are_classified_before_scaling(
        uint rawValue,
        J1939SignalStatus expectedStatus,
        double? expectedValue)
    {
        var payload = new byte[]
        {
            (byte)(rawValue & 0xFF),
            (byte)((rawValue >> 8) & 0xFF),
            (byte)((rawValue >> 16) & 0xFF),
            (byte)(rawValue >> 24),
            0xFF, 0xFF, 0xFF, 0xFF,
        };

        Assert.True(J1939SignalDecoder.TryDecode(
            Message(payload, J1939SignalDecoder.EngineHoursRevolutionsPgn),
            out var result));

        var observation = Assert.Single(result!.Observations);
        Assert.Equal(expectedStatus, observation.Status);
        Assert.Equal((ulong)rawValue, observation.RawValue);
        Assert.Equal(expectedValue, observation.Value);
    }

    [Fact]
    public void Genuine_zero_engine_speed_remains_a_valid_zero()
    {
        Assert.True(J1939SignalDecoder.TryDecode(
            Message([0xFF, 0xFF, 0xFF, 0x00, 0x00, 0xFF, 0xFF, 0xFF]),
            out var result));

        var observation = Assert.Single(result!.Observations);
        Assert.Equal(J1939SignalStatus.Valid, observation.Status);
        Assert.Equal(0d, observation.Value);
    }

    [Theory]
    [InlineData(0xFB00, J1939SignalStatus.ParameterSpecificIndicator)]
    [InlineData(0xFDFF, J1939SignalStatus.ParameterSpecificIndicator)]
    [InlineData(0xFE00, J1939SignalStatus.ErrorIndicator)]
    [InlineData(0xFEFF, J1939SignalStatus.ErrorIndicator)]
    [InlineData(0xFF00, J1939SignalStatus.NotAvailable)]
    [InlineData(0xFFFF, J1939SignalStatus.NotAvailable)]
    public void Indicator_ranges_never_become_numeric_engine_speed(
        ushort rawValue,
        J1939SignalStatus expectedStatus)
    {
        var payload = new byte[]
        {
            0xFF, 0xFF, 0xFF,
            (byte)(rawValue & 0xFF),
            (byte)(rawValue >> 8),
            0xFF, 0xFF, 0xFF,
        };

        Assert.True(J1939SignalDecoder.TryDecode(Message(payload), out var result));

        var observation = Assert.Single(result!.Observations);
        Assert.Equal(expectedStatus, observation.Status);
        Assert.Equal((ulong)rawValue, observation.RawValue);
        Assert.Null(observation.Value);
    }

    [Fact]
    public void Unlisted_pgn_is_unsupported_even_when_payload_looks_like_engine_speed()
    {
        var message = Message([0xFF, 0xFF, 0xFF, 0xE0, 0x2E, 0xFF, 0xFF, 0xFF]) with
        {
            Pgn = 65262,
        };

        Assert.False(J1939SignalDecoder.TryDecode(message, out var result));
        Assert.Null(result);
    }

    [Fact]
    public void Wrong_length_for_supported_pgn_fails_closed_with_bounded_provenance()
    {
        var failure = Assert.Throws<J1939SignalDecodeException>(() =>
            J1939SignalDecoder.TryDecode(
                Message([0xFF, 0xFF, 0xFF, 0xE0, 0x2E, 0xFF, 0xFF]),
                out _));

        Assert.Equal(J1939SignalDecoder.ElectronicEngineController1Pgn, failure.Pgn);
        Assert.Equal((byte)0x00, failure.SourceAddress);
        Assert.Equal(T0, failure.FirstFrameAt);
        Assert.Equal(T0, failure.CompletedAt);
        Assert.Equal(new[] { "capture-eec1" }, failure.CaptureReferences);
        Assert.Contains("requires exactly 8", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("2E", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static J1939AcquiredMessage Message(
        byte[] payload,
        int pgn = J1939SignalDecoder.ElectronicEngineController1Pgn)
    {
        var frame = new J1939CanFrameEnvelope(
            RawIdentifier: 0x0CF00400,
            Priority: 3,
            Pgn: pgn,
            SourceAddress: 0x00,
            DestinationAddress: J1939CanIdentifier.GlobalAddress,
            IsPeerToPeer: false,
            Data: payload,
            CapturedAt: T0,
            AdapterType: "socketcan",
            Channel: "can0",
            CaptureReference: "capture-eec1");

        return new J1939AcquiredMessage(
            pgn,
            SourceAddress: 0x00,
            DestinationAddress: J1939CanIdentifier.GlobalAddress,
            Payload: payload,
            IsTransported: false,
            MessagePriority: 3,
            FirstFrameAt: T0,
            CompletedAt: T0,
            Frames: Array.AsReadOnly([frame]));
    }
}
