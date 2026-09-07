using Opstrax.Telematics.Protocols.J1939;

namespace Opstrax.Telematics.Protocols.Tests;

public sealed class J1939CanAcquisitionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 7, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Pdu2_identifier_preserves_priority_pgn_source_and_capture_evidence()
    {
        var data = new byte[] { 0x40, 0x00, 0x34, 0x12, 0x05, 0x01 };
        var raw = Frame(Identifier(priority: 3, J1939DiagnosticDecoder.Dm1Pgn, source: 0x2A), data);

        var parsed = J1939CanIdentifier.Parse(raw);
        data[0] = 0xFF;

        Assert.Equal((byte)3, parsed.Priority);
        Assert.Equal(J1939DiagnosticDecoder.Dm1Pgn, parsed.Pgn);
        Assert.Equal((byte)0x2A, parsed.SourceAddress);
        Assert.Equal((byte)0xFF, parsed.DestinationAddress);
        Assert.False(parsed.IsPeerToPeer);
        Assert.Equal((byte)0x40, parsed.Data.Span[0]);
        Assert.Equal("socketcan", parsed.AdapterType);
        Assert.Equal("can0", parsed.Channel);
        Assert.Equal("capture-001", parsed.CaptureReference);
    }

    [Fact]
    public void Pdu1_identifier_zeroes_destination_bits_in_pgn()
    {
        var parsed = J1939CanIdentifier.Parse(Frame(
            Identifier(priority: 7, J1939TransportReassembler.TpCmPgn, source: 0x80, destination: 0x91),
            new byte[8]));

        Assert.Equal(J1939TransportReassembler.TpCmPgn, parsed.Pgn);
        Assert.Equal((byte)0x80, parsed.SourceAddress);
        Assert.Equal((byte)0x91, parsed.DestinationAddress);
        Assert.True(parsed.IsPeerToPeer);
    }

    [Fact]
    public void Direct_dm1_message_can_be_decoded_without_inventing_transport_priority()
    {
        var sut = new J1939CanAcquisition();
        var acquired = sut.Accept(Frame(
            Identifier(priority: 6, J1939DiagnosticDecoder.Dm1Pgn, source: 0x31),
            new byte[] { 0x40, 0, 1, 0, 0, 1 }));

        Assert.NotNull(acquired);
        Assert.False(acquired!.IsTransported);
        Assert.Equal((byte)6, acquired.MessagePriority);
        Assert.Single(acquired.Frames);

        var diagnostic = J1939DiagnosticDecoder.Decode(acquired.Pgn, acquired.Payload.Span);
        Assert.True(diagnostic.IsActive);
        Assert.Single(diagnostic.Dtcs);
    }

    [Fact]
    public void Raw_tp_frames_reassemble_dm1_and_retain_each_capture_envelope()
    {
        var payload = new byte[]
        {
            0x40, 0x00,
            0x34, 0x12, 0x05, 0x01,
            0x78, 0x56, 0x03, 0x02,
        };
        var sut = new J1939CanAcquisition();

        Assert.Null(sut.Accept(Frame(
            Identifier(7, J1939TransportReassembler.TpCmPgn, 0x2A, 0xFF),
            Cm(payload.Length, packets: 2, J1939DiagnosticDecoder.Dm1Pgn),
            reference: "capture-cm")));
        Assert.Null(sut.Accept(Frame(
            Identifier(7, J1939TransportReassembler.TpDtPgn, 0x2A, 0xFF),
            Dt(1, payload.AsSpan(0, 7)),
            reference: "capture-dt-1")));
        var acquired = sut.Accept(Frame(
            Identifier(7, J1939TransportReassembler.TpDtPgn, 0x2A, 0xFF),
            Dt(2, payload.AsSpan(7)),
            reference: "capture-dt-2"));

        Assert.NotNull(acquired);
        Assert.True(acquired!.IsTransported);
        Assert.Null(acquired.MessagePriority);
        Assert.Equal(J1939DiagnosticDecoder.Dm1Pgn, acquired.Pgn);
        Assert.Equal(payload, acquired.Payload.ToArray());
        Assert.Equal(new[] { "capture-cm", "capture-dt-1", "capture-dt-2" },
            acquired.Frames.Select(frame => frame.CaptureReference));
        Assert.All(acquired.Frames, frame => Assert.Equal((byte)7, frame.Priority));
        Assert.Equal(0, sut.ActiveTransportSessionCount);

        var diagnostic = J1939DiagnosticDecoder.Decode(acquired.Pgn, acquired.Payload.Span);
        Assert.Equal(2, diagnostic.Dtcs.Count);
    }

    [Fact]
    public void Identical_addresses_on_two_can_channels_have_independent_transport_sessions()
    {
        var sut = new J1939CanAcquisition();
        var cmIdentifier = Identifier(7, J1939TransportReassembler.TpCmPgn, 0x2A, 0xFF);
        var dtIdentifier = Identifier(7, J1939TransportReassembler.TpDtPgn, 0x2A, 0xFF);
        var payloadA = Enumerable.Range(1, 10).Select(value => (byte)value).ToArray();
        var payloadB = Enumerable.Range(21, 10).Select(value => (byte)value).ToArray();

        Assert.Null(sut.Accept(Frame(cmIdentifier, Cm(10, 2, J1939DiagnosticDecoder.Dm1Pgn))));
        Assert.Null(sut.Accept(Frame(cmIdentifier, Cm(10, 2, J1939DiagnosticDecoder.Dm2Pgn)) with
        {
            Channel = "can1",
            CaptureReference = "can1-cm",
        }));
        Assert.Equal(2, sut.ActiveTransportSessionCount);

        Assert.Null(sut.Accept(Frame(dtIdentifier, Dt(1, payloadA.AsSpan(0, 7)))));
        Assert.Null(sut.Accept(Frame(dtIdentifier, Dt(1, payloadB.AsSpan(0, 7))) with
        {
            Channel = "can1",
            CaptureReference = "can1-dt1",
        }));

        var completedA = sut.Accept(Frame(dtIdentifier, Dt(2, payloadA.AsSpan(7))));
        var completedB = sut.Accept(Frame(dtIdentifier, Dt(2, payloadB.AsSpan(7))) with
        {
            Channel = "can1",
            CaptureReference = "can1-dt2",
        });

        Assert.Equal(payloadA, completedA!.Payload.ToArray());
        Assert.Equal(payloadB, completedB!.Payload.ToArray());
        Assert.All(completedA.Frames, frame => Assert.Equal("can0", frame.Channel));
        Assert.All(completedB.Frames, frame => Assert.Equal("can1", frame.Channel));
        Assert.Equal(0, sut.ActiveTransportSessionCount);
    }

    [Fact]
    public void Data_from_another_channel_cannot_complete_a_transport_session()
    {
        var sut = new J1939CanAcquisition();
        sut.Accept(Frame(
            Identifier(7, J1939TransportReassembler.TpCmPgn, 0x2A, 0xFF),
            Cm(10, 2, J1939DiagnosticDecoder.Dm1Pgn)));

        var wrongChannel = Frame(
            Identifier(7, J1939TransportReassembler.TpDtPgn, 0x2A, 0xFF),
            Dt(1, new byte[7])) with { Channel = "can1" };

        Assert.Throws<J1939TransportException>(() => sut.Accept(wrongChannel));
        Assert.Equal(1, sut.ActiveTransportSessionCount);
    }

    [Fact]
    public void Concurrent_can_channels_reassemble_without_crossing_evidence()
    {
        var sut = new J1939CanAcquisition();
        var results = new J1939AcquiredMessage?[16];

        Parallel.For(0, results.Length, index =>
        {
            var channel = $"can{index}";
            var source = (byte)(0x20 + index);
            var payload = Enumerable.Range(index * 10, 10).Select(value => (byte)value).ToArray();
            sut.Accept(Frame(
                Identifier(7, J1939TransportReassembler.TpCmPgn, source, 0xFF),
                Cm(10, 2, J1939DiagnosticDecoder.Dm1Pgn),
                reference: $"{channel}-cm") with { Channel = channel });
            sut.Accept(Frame(
                Identifier(7, J1939TransportReassembler.TpDtPgn, source, 0xFF),
                Dt(1, payload.AsSpan(0, 7)),
                reference: $"{channel}-dt1") with { Channel = channel });
            results[index] = sut.Accept(Frame(
                Identifier(7, J1939TransportReassembler.TpDtPgn, source, 0xFF),
                Dt(2, payload.AsSpan(7)),
                reference: $"{channel}-dt2") with { Channel = channel });
        });

        for (var index = 0; index < results.Length; index++)
        {
            var expected = Enumerable.Range(index * 10, 10).Select(value => (byte)value).ToArray();
            Assert.Equal(expected, results[index]!.Payload.ToArray());
            Assert.All(results[index]!.Frames, frame => Assert.Equal($"can{index}", frame.Channel));
        }
        Assert.Equal(0, sut.ActiveTransportSessionCount);
    }

    [Fact]
    public void Abandoned_transport_paths_expire_before_the_path_limit_is_enforced()
    {
        var sut = new J1939CanAcquisition(TimeSpan.FromSeconds(30));
        var identifier = Identifier(7, J1939TransportReassembler.TpCmPgn, 0x2A, 0xFF);
        for (var index = 0; index < J1939CanAcquisition.MaxActiveTransportPaths; index++)
        {
            sut.Accept(Frame(identifier, Cm(10, 2, J1939DiagnosticDecoder.Dm1Pgn)) with
            {
                Channel = $"can{index}",
                CaptureReference = $"start-{index}",
            });
        }
        Assert.Equal(J1939CanAcquisition.MaxActiveTransportPaths, sut.ActiveTransportSessionCount);

        var later = sut.Accept(Frame(identifier, Cm(10, 2, J1939DiagnosticDecoder.Dm1Pgn)) with
        {
            CapturedAt = T0.AddSeconds(31),
            Channel = "replacement-channel",
            CaptureReference = "replacement-start",
        });

        Assert.Null(later);
        Assert.Equal(1, sut.ActiveTransportSessionCount);
    }

    [Fact]
    public void Regressing_time_on_a_can_path_is_not_admitted_to_another_session()
    {
        var sut = new J1939CanAcquisition();
        sut.Accept(Frame(
            Identifier(7, J1939TransportReassembler.TpCmPgn, 0x2A, 0xFF),
            Cm(10, 2, J1939DiagnosticDecoder.Dm1Pgn)));

        var earlier = Frame(
            Identifier(7, J1939TransportReassembler.TpCmPgn, 0x2B, 0xFF),
            Cm(10, 2, J1939DiagnosticDecoder.Dm2Pgn)) with
        {
            CapturedAt = T0.AddMilliseconds(-1),
        };

        Assert.Throws<J1939AcquisitionException>(() => sut.Accept(earlier));
        Assert.Equal(1, sut.ActiveTransportSessionCount);
    }

    [Theory]
    [MemberData(nameof(InvalidFrames))]
    public void Invalid_or_unauditable_can_frames_fail_closed(J1939RawCanFrame frame)
        => Assert.Throws<J1939AcquisitionException>(() => J1939CanIdentifier.Parse(frame));

    public static IEnumerable<object[]> InvalidFrames()
    {
        yield return [Frame(0x123, new byte[8]) with { IsExtendedIdentifier = false }];
        yield return [Frame(0x20000000, new byte[8])];
        yield return [Frame(0x123, new byte[8]) with { IsRemoteTransmissionRequest = true }];
        yield return [Frame(0x123, new byte[8]) with { IsErrorFrame = true }];
        yield return [Frame(0x123, new byte[9])];
        yield return [Frame(0x123, new byte[8]) with { CapturedAt = default }];
        yield return [Frame(0x123, new byte[8]) with { AdapterType = " " }];
        yield return [Frame(0x123, new byte[8]) with { Channel = " can0" }];
        yield return [Frame(0x123, new byte[8]) with { Channel = "can0\nforged" }];
        yield return [Frame(0x123, new byte[8]) with { CaptureReference = new string('x', 257) }];
    }

    [Fact]
    public void Tp_frame_with_non_classic_payload_is_rejected_by_transport_boundary()
    {
        var sut = new J1939CanAcquisition();
        var raw = Frame(
            Identifier(7, J1939TransportReassembler.TpCmPgn, 0x2A, 0xFF),
            new byte[7]);

        Assert.Throws<J1939TransportException>(() => sut.Accept(raw));
        Assert.Equal(0, sut.ActiveTransportSessionCount);
    }

    private static J1939RawCanFrame Frame(uint identifier, byte[] data, string reference = "capture-001")
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
