using Opstrax.Telematics.Protocols.J1939;

namespace Opstrax.Telematics.Protocols.Tests;

public sealed class J1939DiagnosticAcquisitionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 7, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Direct_dm1_is_decoded_with_the_original_frame_evidence()
    {
        var sut = new J1939DiagnosticAcquisition();

        var result = sut.Accept(Frame(
            Identifier(priority: 6, J1939DiagnosticDecoder.Dm1Pgn, source: 0x31),
            [0x40, 0x00, 0x34, 0x12, 0x05, 0x01],
            "direct-dm1"));

        Assert.Equal(J1939DiagnosticAcquisitionStatus.DiagnosticDecoded, result.Status);
        Assert.NotNull(result.Message);
        Assert.NotNull(result.Diagnostic);
        Assert.True(result.Diagnostic!.IsActive);
        Assert.Single(result.Diagnostic.Dtcs);
        Assert.Equal((byte)6, result.Message!.MessagePriority);
        Assert.Equal((byte)0x31, result.Message.SourceAddress);
        Assert.Equal("direct-dm1", Assert.Single(result.Message.Frames).CaptureReference);
    }

    [Fact]
    public void Transport_fragments_remain_pending_until_complete_dm2_is_decoded()
    {
        var sut = new J1939DiagnosticAcquisition();
        var payload = new byte[]
        {
            0x00, 0x00,
            0x34, 0x12, 0x05, 0x01,
            0x78, 0x56, 0x03, 0x02,
        };

        var start = sut.Accept(Frame(
            Identifier(7, J1939TransportReassembler.TpCmPgn, 0x2A),
            Cm(payload.Length, 2, J1939DiagnosticDecoder.Dm2Pgn),
            "dm2-cm"));
        var first = sut.Accept(Frame(
            Identifier(7, J1939TransportReassembler.TpDtPgn, 0x2A),
            Dt(1, payload.AsSpan(0, 7)),
            "dm2-dt-1") with
        { CapturedAt = T0.AddMilliseconds(1) });
        var completed = sut.Accept(Frame(
            Identifier(7, J1939TransportReassembler.TpDtPgn, 0x2A),
            Dt(2, payload.AsSpan(7)),
            "dm2-dt-2") with
        { CapturedAt = T0.AddMilliseconds(2) });

        Assert.Equal(J1939DiagnosticAcquisitionStatus.NoCompleteMessage, start.Status);
        Assert.Null(start.Message);
        Assert.Null(start.Diagnostic);
        Assert.Equal(J1939DiagnosticAcquisitionStatus.NoCompleteMessage, first.Status);
        Assert.Equal(J1939DiagnosticAcquisitionStatus.DiagnosticDecoded, completed.Status);
        Assert.NotNull(completed.Message);
        Assert.False(completed.Diagnostic!.IsActive);
        Assert.Equal(2, completed.Diagnostic.Dtcs.Count);
        Assert.True(completed.Message!.IsTransported);
        Assert.Null(completed.Message.MessagePriority);
        Assert.Equal(new[] { "dm2-cm", "dm2-dt-1", "dm2-dt-2" },
            completed.Message.Frames.Select(frame => frame.CaptureReference));
        Assert.Equal(0, sut.ActiveTransportSessionCount);
    }

    [Fact]
    public void Complete_non_diagnostic_pgn_is_classified_without_losing_evidence()
    {
        const int nonDiagnosticPgn = 0x00F000;
        var sut = new J1939DiagnosticAcquisition();

        var result = sut.Accept(Frame(
            Identifier(3, nonDiagnosticPgn, 0x00),
            [0xA5, 0x5A, 0xA5, 0x5A, 0xA5, 0x5A, 0xA5, 0x5A],
            "non-diagnostic"));

        Assert.Equal(J1939DiagnosticAcquisitionStatus.NonDiagnosticMessage, result.Status);
        Assert.Equal(nonDiagnosticPgn, result.Message!.Pgn);
        Assert.Equal("non-diagnostic", Assert.Single(result.Message.Frames).CaptureReference);
        Assert.Null(result.Diagnostic);
    }

    [Fact]
    public void Standalone_transport_control_does_not_claim_a_pending_session()
    {
        var sut = new J1939DiagnosticAcquisition();
        var cts = new byte[]
        {
            J1939TransportReassembler.CtsControl,
            1,
            1,
            0xFF,
            0xFF,
            (byte)(J1939DiagnosticDecoder.Dm1Pgn & 0xFF),
            (byte)((J1939DiagnosticDecoder.Dm1Pgn >> 8) & 0xFF),
            (byte)((J1939DiagnosticDecoder.Dm1Pgn >> 16) & 0xFF),
        };

        var result = sut.Accept(Frame(
            Identifier(7, J1939TransportReassembler.TpCmPgn, 0x91, 0x2A),
            cts,
            "standalone-cts"));

        Assert.Equal(J1939DiagnosticAcquisitionStatus.NoCompleteMessage, result.Status);
        Assert.Null(result.Message);
        Assert.Null(result.Diagnostic);
        Assert.Equal(0, sut.ActiveTransportSessionCount);
    }

    [Fact]
    public void Malformed_complete_dm1_fails_closed_with_bounded_provenance_not_payload()
    {
        var sut = new J1939DiagnosticAcquisition();

        var failure = Assert.Throws<J1939DiagnosticAcquisitionException>(() => sut.Accept(Frame(
            Identifier(6, J1939DiagnosticDecoder.Dm1Pgn, 0x31),
            [0x40],
            "bad-dm1")));

        Assert.Equal(J1939DiagnosticDecoder.Dm1Pgn, failure.Pgn);
        Assert.Equal((byte)0x31, failure.SourceAddress);
        Assert.Equal(T0, failure.FirstFrameAt);
        Assert.Equal(T0, failure.CompletedAt);
        Assert.Equal(new[] { "bad-dm1" }, failure.CaptureReferences);
        Assert.DoesNotContain("40", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.IsType<ArgumentException>(failure.InnerException);
        Assert.Equal(0, sut.ActiveTransportSessionCount);
    }

    [Fact]
    public void Malformed_transport_dm1_discards_the_completed_session()
    {
        var sut = new J1939DiagnosticAcquisition();
        var malformed = Enumerable.Range(0, 9).Select(value => (byte)value).ToArray();

        Assert.Equal(J1939DiagnosticAcquisitionStatus.NoCompleteMessage, sut.Accept(Frame(
            Identifier(7, J1939TransportReassembler.TpCmPgn, 0x44),
            Cm(malformed.Length, 2, J1939DiagnosticDecoder.Dm1Pgn),
            "bad-cm")).Status);
        Assert.Equal(J1939DiagnosticAcquisitionStatus.NoCompleteMessage, sut.Accept(Frame(
            Identifier(7, J1939TransportReassembler.TpDtPgn, 0x44),
            Dt(1, malformed.AsSpan(0, 7)),
            "bad-dt-1") with
        { CapturedAt = T0.AddMilliseconds(1) }).Status);

        var failure = Assert.Throws<J1939DiagnosticAcquisitionException>(() => sut.Accept(Frame(
            Identifier(7, J1939TransportReassembler.TpDtPgn, 0x44),
            Dt(2, malformed.AsSpan(7)),
            "bad-dt-2") with
        { CapturedAt = T0.AddMilliseconds(2) }));

        Assert.Equal(new[] { "bad-cm", "bad-dt-1", "bad-dt-2" }, failure.CaptureReferences);
        Assert.Equal(0, sut.ActiveTransportSessionCount);
    }

    [Fact]
    public void Invalid_can_frame_is_rejected_at_the_existing_acquisition_boundary()
    {
        var sut = new J1939DiagnosticAcquisition();
        var invalid = Frame(0x123, new byte[8], "standard-can") with
        {
            IsExtendedIdentifier = false,
        };

        Assert.Throws<J1939AcquisitionException>(() => sut.Accept(invalid));
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

    private static byte[] Cm(int payloadBytes, byte packets, int targetPgn)
        =>
        [
            J1939TransportReassembler.BamControl,
            (byte)(payloadBytes & 0xFF),
            (byte)((payloadBytes >> 8) & 0xFF),
            packets,
            0xFF,
            (byte)(targetPgn & 0xFF),
            (byte)((targetPgn >> 8) & 0xFF),
            (byte)((targetPgn >> 16) & 0xFF),
        ];

    private static byte[] Dt(byte sequence, ReadOnlySpan<byte> payload)
    {
        var data = Enumerable.Repeat((byte)0xFF, 8).ToArray();
        data[0] = sequence;
        payload.CopyTo(data.AsSpan(1, Math.Min(payload.Length, 7)));
        return data;
    }
}
