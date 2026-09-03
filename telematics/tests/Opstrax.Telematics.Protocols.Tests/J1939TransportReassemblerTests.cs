using Opstrax.Telematics.Protocols.J1939;

namespace Opstrax.Telematics.Protocols.Tests;

public sealed class J1939TransportReassemblerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 3, 19, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Bam_reassembles_payload_and_existing_dm1_decoder_accepts_it()
    {
        var payload = new byte[]
        {
            0x40, 0x00,
            0x34, 0x12, 0x05, 0x01,
            0x78, 0x56, 0x03, 0x02,
        };
        var sut = new J1939TransportReassembler();

        Assert.Null(sut.Accept(Cm(
            J1939TransportReassembler.BamControl,
            payload.Length,
            2,
            J1939DiagnosticDecoder.Dm1Pgn,
            source: 0x2A,
            destination: J1939TransportReassembler.GlobalAddress,
            T0)));
        Assert.Equal(1, sut.ActiveSessionCount);

        Assert.Null(sut.Accept(Dt(1, payload.AsSpan(0, 7), 0x2A, 0xFF, T0.AddMilliseconds(10))));
        var completed = sut.Accept(Dt(2, payload.AsSpan(7), 0x2A, 0xFF, T0.AddMilliseconds(20)));

        Assert.NotNull(completed);
        Assert.Equal(J1939DiagnosticDecoder.Dm1Pgn, completed!.Pgn);
        Assert.Equal((byte)0x2A, completed.SourceAddress);
        Assert.Equal((byte)0xFF, completed.DestinationAddress);
        Assert.True(completed.IsBroadcast);
        Assert.Equal(payload, completed.Payload);
        Assert.Equal(0, sut.ActiveSessionCount);

        var decoded = J1939DiagnosticDecoder.Decode(completed.Pgn, completed.Payload);
        Assert.True(decoded.IsActive);
        Assert.Equal(2, decoded.Dtcs.Count);
    }

    [Fact]
    public void Passive_rts_cts_capture_keeps_session_and_reassembles_sender_data()
    {
        var payload = Enumerable.Range(1, 12).Select(i => (byte)i).ToArray();
        var sut = new J1939TransportReassembler();

        Assert.Null(sut.Accept(Cm(
            J1939TransportReassembler.RtsControl,
            payload.Length,
            2,
            J1939DiagnosticDecoder.Dm2Pgn,
            source: 0x80,
            destination: 0x90,
            T0,
            byte4: 2)));

        // CTS travels receiver -> sender. Passive observation must not destroy the
        // sender -> receiver reassembly session.
        Assert.Null(sut.Accept(Cm(
            J1939TransportReassembler.CtsControl,
            payload.Length,
            2,
            J1939DiagnosticDecoder.Dm2Pgn,
            source: 0x90,
            destination: 0x80,
            T0.AddMilliseconds(5),
            byte4: 1)));
        Assert.Equal(1, sut.ActiveSessionCount);

        Assert.Null(sut.Accept(Dt(1, payload.AsSpan(0, 7), 0x80, 0x90, T0.AddMilliseconds(10))));
        var completed = sut.Accept(Dt(2, payload.AsSpan(7), 0x80, 0x90, T0.AddMilliseconds(20)));

        Assert.NotNull(completed);
        Assert.False(completed!.IsBroadcast);
        Assert.Equal(payload, completed.Payload);
        Assert.Equal(0, sut.ActiveSessionCount);
    }

    [Fact]
    public void Out_of_order_data_fails_closed_and_discards_session()
    {
        var sut = StartBam();

        var failure = Assert.Throws<J1939TransportException>(() =>
            sut.Accept(Dt(2, new byte[] { 1, 2, 3 }, 0x2A, 0xFF, T0.AddMilliseconds(10))));

        Assert.Contains("expected 1", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, sut.ActiveSessionCount);
        Assert.Throws<J1939TransportException>(() =>
            sut.Accept(Dt(1, new byte[] { 1, 2, 3, 4, 5, 6, 7 }, 0x2A, 0xFF, T0.AddMilliseconds(20))));
    }

    [Fact]
    public void Duplicate_start_does_not_replace_inflight_session()
    {
        var sut = StartBam();

        Assert.Throws<J1939TransportException>(() => sut.Accept(Cm(
            J1939TransportReassembler.BamControl,
            10,
            2,
            J1939DiagnosticDecoder.Dm2Pgn,
            0x2A,
            0xFF,
            T0.AddMilliseconds(1))));

        Assert.Equal(1, sut.ActiveSessionCount);
    }

    [Fact]
    public void Stale_session_expires_before_late_data_is_accepted()
    {
        var sut = new J1939TransportReassembler(TimeSpan.FromSeconds(30));
        sut.Accept(Cm(
            J1939TransportReassembler.BamControl,
            10,
            2,
            J1939DiagnosticDecoder.Dm1Pgn,
            0x2A,
            0xFF,
            T0));

        Assert.Throws<J1939TransportException>(() =>
            sut.Accept(Dt(1, new byte[] { 1, 2, 3, 4, 5, 6, 7 }, 0x2A, 0xFF, T0.AddSeconds(31))));
        Assert.Equal(0, sut.ActiveSessionCount);
    }

    [Fact]
    public void Abort_from_receiver_removes_directionally_reversed_rts_session()
    {
        var sut = new J1939TransportReassembler();
        sut.Accept(Cm(
            J1939TransportReassembler.RtsControl,
            10,
            2,
            J1939DiagnosticDecoder.Dm2Pgn,
            0x80,
            0x90,
            T0,
            byte4: 2));

        Assert.Null(sut.Accept(Abort(
            J1939DiagnosticDecoder.Dm2Pgn,
            source: 0x90,
            destination: 0x80,
            T0.AddMilliseconds(5))));
        Assert.Equal(0, sut.ActiveSessionCount);
    }

    [Theory]
    [InlineData(8, 2)]
    [InlineData(1786, 255)]
    [InlineData(10, 3)]
    public void Invalid_advertised_size_or_packet_count_is_rejected(int payloadBytes, byte packets)
    {
        var sut = new J1939TransportReassembler();

        Assert.Throws<J1939TransportException>(() => sut.Accept(Cm(
            J1939TransportReassembler.BamControl,
            payloadBytes,
            packets,
            J1939DiagnosticDecoder.Dm1Pgn,
            0x2A,
            0xFF,
            T0)));
        Assert.Equal(0, sut.ActiveSessionCount);
    }

    [Fact]
    public void Rts_requires_specific_destination_and_nonzero_cts_window()
    {
        var sut = new J1939TransportReassembler();

        Assert.Throws<J1939TransportException>(() => sut.Accept(Cm(
            J1939TransportReassembler.RtsControl,
            10,
            2,
            J1939DiagnosticDecoder.Dm1Pgn,
            0x2A,
            0xFF,
            T0,
            byte4: 2)));

        Assert.Throws<J1939TransportException>(() => sut.Accept(Cm(
            J1939TransportReassembler.RtsControl,
            10,
            2,
            J1939DiagnosticDecoder.Dm1Pgn,
            0x2A,
            0x90,
            T0,
            byte4: 0)));
    }

    [Fact]
    public void Bam_requires_global_destination()
    {
        var sut = new J1939TransportReassembler();
        Assert.Throws<J1939TransportException>(() => sut.Accept(Cm(
            J1939TransportReassembler.BamControl,
            10,
            2,
            J1939DiagnosticDecoder.Dm1Pgn,
            0x2A,
            0x90,
            T0)));
    }

    [Fact]
    public void Orphan_dt_wrong_frame_length_and_non_tp_pgn_fail_closed()
    {
        var sut = new J1939TransportReassembler();

        Assert.Throws<J1939TransportException>(() =>
            sut.Accept(Dt(1, new byte[] { 1, 2 }, 0x2A, 0xFF, T0)));

        Assert.Throws<J1939TransportException>(() => sut.Accept(new J1939TransportFrame(
            J1939TransportReassembler.TpCmPgn,
            0x2A,
            0xFF,
            new byte[7],
            T0)));

        Assert.Throws<ArgumentOutOfRangeException>(() => sut.Accept(new J1939TransportFrame(
            J1939DiagnosticDecoder.Dm1Pgn,
            0x2A,
            0xFF,
            new byte[8],
            T0)));
    }

    [Fact]
    public void Separate_sources_may_hold_independent_broadcast_sessions()
    {
        var sut = new J1939TransportReassembler();
        sut.Accept(Cm(J1939TransportReassembler.BamControl, 10, 2, J1939DiagnosticDecoder.Dm1Pgn, 0x10, 0xFF, T0));
        sut.Accept(Cm(J1939TransportReassembler.BamControl, 10, 2, J1939DiagnosticDecoder.Dm2Pgn, 0x11, 0xFF, T0));

        Assert.Equal(2, sut.ActiveSessionCount);
    }

    private static J1939TransportReassembler StartBam()
    {
        var sut = new J1939TransportReassembler();
        sut.Accept(Cm(
            J1939TransportReassembler.BamControl,
            10,
            2,
            J1939DiagnosticDecoder.Dm1Pgn,
            0x2A,
            0xFF,
            T0));
        return sut;
    }

    private static J1939TransportFrame Cm(
        byte control,
        int payloadBytes,
        byte packets,
        int targetPgn,
        byte source,
        byte destination,
        DateTimeOffset at,
        byte byte4 = 0xFF)
    {
        var data = new byte[8];
        data[0] = control;
        data[1] = (byte)(payloadBytes & 0xFF);
        data[2] = (byte)((payloadBytes >> 8) & 0xFF);
        data[3] = packets;
        data[4] = byte4;
        data[5] = (byte)(targetPgn & 0xFF);
        data[6] = (byte)((targetPgn >> 8) & 0xFF);
        data[7] = (byte)((targetPgn >> 16) & 0xFF);
        return new J1939TransportFrame(J1939TransportReassembler.TpCmPgn, source, destination, data, at);
    }

    private static J1939TransportFrame Abort(int targetPgn, byte source, byte destination, DateTimeOffset at)
    {
        var data = new byte[]
        {
            J1939TransportReassembler.AbortControl, 1, 0xFF, 0xFF, 0xFF,
            (byte)(targetPgn & 0xFF),
            (byte)((targetPgn >> 8) & 0xFF),
            (byte)((targetPgn >> 16) & 0xFF),
        };
        return new J1939TransportFrame(J1939TransportReassembler.TpCmPgn, source, destination, data, at);
    }

    private static J1939TransportFrame Dt(
        byte sequence,
        ReadOnlySpan<byte> payload,
        byte source,
        byte destination,
        DateTimeOffset at)
    {
        var data = Enumerable.Repeat((byte)0xFF, 8).ToArray();
        data[0] = sequence;
        payload.CopyTo(data.AsSpan(1, Math.Min(payload.Length, 7)));
        return new J1939TransportFrame(J1939TransportReassembler.TpDtPgn, source, destination, data, at);
    }
}