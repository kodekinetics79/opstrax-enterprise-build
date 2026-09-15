using Opstrax.Telematics.Protocols.J1939;

namespace Opstrax.Telematics.Protocols.Tests;

public sealed class J1939MessageAcquisitionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 8, 13, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Supported_eec1_frame_routes_to_the_signal_catalog()
    {
        var sut = new J1939MessageAcquisition();

        var result = sut.Accept(Frame(
            Identifier(3, J1939SignalDecoder.ElectronicEngineController1Pgn, 0x00),
            [0xFF, 0xFF, 0xFF, 0xE0, 0x2E, 0xFF, 0xFF, 0xFF],
            "eec1-1500rpm"));

        Assert.Equal(J1939MessageAcquisitionStatus.SignalsDecoded, result.Status);
        Assert.NotNull(result.Message);
        Assert.NotNull(result.Signals);
        Assert.Null(result.Diagnostic);
        Assert.Equal(1500d, Assert.Single(result.Signals!.Observations).Value);
        Assert.Same(result.Message, result.Signals.Message);
        Assert.Equal("eec1-1500rpm", Assert.Single(result.Message!.Frames).CaptureReference);
    }

    [Fact]
    public void Dm1_continues_to_route_to_the_existing_diagnostic_decoder()
    {
        var sut = new J1939MessageAcquisition();

        var result = sut.Accept(Frame(
            Identifier(6, J1939DiagnosticDecoder.Dm1Pgn, 0x31),
            [0x40, 0x00, 0x34, 0x12, 0x05, 0x01],
            "dm1"));

        Assert.Equal(J1939MessageAcquisitionStatus.DiagnosticDecoded, result.Status);
        Assert.NotNull(result.Message);
        Assert.NotNull(result.Diagnostic);
        Assert.Null(result.Signals);
        Assert.True(result.Diagnostic!.IsActive);
    }

    [Fact]
    public void Engine_hours_routes_through_the_same_signal_pipeline()
    {
        var sut = new J1939MessageAcquisition();

        var result = sut.Accept(Frame(
            Identifier(6, J1939SignalDecoder.EngineHoursRevolutionsPgn, 0x00),
            [0x72, 0x60, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF],
            "hours-1234-5"));

        Assert.Equal(J1939MessageAcquisitionStatus.SignalsDecoded, result.Status);
        Assert.Equal(1234.5d, Assert.Single(result.Signals!.Observations).Value);
        Assert.Equal("hours-1234-5", Assert.Single(result.Message!.Frames).CaptureReference);
    }

    [Fact]
    public void Unlisted_complete_pgn_remains_explicitly_unsupported()
    {
        const int unlistedPgn = 0x00F001;
        var sut = new J1939MessageAcquisition();

        var result = sut.Accept(Frame(
            Identifier(3, unlistedPgn, 0x22),
            [0xFF, 0xFF, 0xFF, 0xE0, 0x2E, 0xFF, 0xFF, 0xFF],
            "unlisted"));

        Assert.Equal(J1939MessageAcquisitionStatus.UnsupportedMessage, result.Status);
        Assert.Equal(unlistedPgn, result.Message!.Pgn);
        Assert.Null(result.Diagnostic);
        Assert.Null(result.Signals);
        Assert.Equal("unlisted", Assert.Single(result.Message.Frames).CaptureReference);
    }

    [Fact]
    public void Transport_fragment_returns_no_complete_message()
    {
        var sut = new J1939MessageAcquisition();
        var cm = new byte[]
        {
            J1939TransportReassembler.BamControl,
            9,
            0,
            2,
            0xFF,
            (byte)(J1939DiagnosticDecoder.Dm1Pgn & 0xFF),
            (byte)((J1939DiagnosticDecoder.Dm1Pgn >> 8) & 0xFF),
            (byte)((J1939DiagnosticDecoder.Dm1Pgn >> 16) & 0xFF),
        };

        var result = sut.Accept(Frame(
            Identifier(7, J1939TransportReassembler.TpCmPgn, 0x31),
            cm,
            "cm"));

        Assert.Equal(J1939MessageAcquisitionStatus.NoCompleteMessage, result.Status);
        Assert.Null(result.Message);
        Assert.Null(result.Diagnostic);
        Assert.Null(result.Signals);
        Assert.Equal(1, sut.ActiveTransportSessionCount);
    }

    [Fact]
    public void Malformed_supported_signal_message_preserves_provenance_without_payload_text()
    {
        var sut = new J1939MessageAcquisition();

        var failure = Assert.Throws<J1939SignalDecodeException>(() => sut.Accept(Frame(
            Identifier(3, J1939SignalDecoder.ElectronicEngineController1Pgn, 0x00),
            [0xFF, 0xFF, 0xFF, 0xE0, 0x2E, 0xFF, 0xFF],
            "short-eec1")));

        Assert.Equal(new[] { "short-eec1" }, failure.CaptureReferences);
        Assert.DoesNotContain("2E", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static J1939RawCanFrame Frame(uint identifier, byte[] data, string reference)
        => new(identifier, true, data, T0, "socketcan", "can0", reference);

    private static uint Identifier(byte priority, int pgn, byte source, byte destination = 0xFF)
    {
        var pduFormat = (byte)((pgn >> 8) & 0xFF);
        var pgnIdentifierBits = (uint)(pgn & 0x3FFFF) << 8;
        if (pduFormat < 240)
            pgnIdentifierBits = (pgnIdentifierBits & 0x1FFF00FF) | ((uint)destination << 8);
        return ((uint)priority << 26) | pgnIdentifierBits | source;
    }
}
