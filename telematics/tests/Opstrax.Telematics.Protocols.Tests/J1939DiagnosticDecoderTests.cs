using Opstrax.Telematics.Protocols.J1939;

namespace Opstrax.Telematics.Protocols.Tests;

public sealed class J1939DiagnosticDecoderTests
{
    [Fact]
    public void DecodeDm1_extracts_lamps_spn_fmi_occurrence_and_conversion_method()
    {
        // Protect=on, amber=off, red=not-available, MIL=on; SPN 524287, FMI 5, OC 7.
        var result = J1939DiagnosticDecoder.Decode(J1939DiagnosticDecoder.Dm1Pgn,
            [0b01_00_11_01, 0b00_01_11_00, 0xFF, 0xFF, 0xE5, 0x07]);

        Assert.True(result.IsActive);
        Assert.Equal(LampState.On, result.Lamps.Protect);
        Assert.Equal(LampState.NotAvailable, result.Lamps.RedStop);
        Assert.Equal(LampState.On, result.Lamps.MalfunctionIndicator);
        var dtc = Assert.Single(result.Dtcs);
        Assert.Equal(524287, dtc.Spn);
        Assert.Equal(5, dtc.Fmi);
        Assert.Equal(7, dtc.OccurrenceCount);
        Assert.False(dtc.ConversionMethod);
    }

    [Fact]
    public void DecodeDm2_marks_message_previously_active_and_skips_na_padding()
    {
        var result = J1939DiagnosticDecoder.Decode(J1939DiagnosticDecoder.Dm2Pgn,
            [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF]);

        Assert.False(result.IsActive);
        Assert.Empty(result.Dtcs);
    }

    [Fact]
    public void Decode_supports_multiple_dtcs_and_conversion_method_bit()
    {
        // SPN 1/FMI 0/OC 1 followed by SPN 256/FMI 31/OC 2 with CM=1.
        var result = J1939DiagnosticDecoder.Decode(J1939DiagnosticDecoder.Dm1Pgn,
            [0, 0, 1, 0, 0, 1, 0, 1, 0x1F, 0x82]);

        Assert.Collection(result.Dtcs,
            first => Assert.Equal(new J1939Dtc(1, 0, 1, false), first),
            second => Assert.Equal(new J1939Dtc(256, 31, 2, true), second));
    }

    [Theory]
    [InlineData(65225, new byte[] { 0, 0 })]
    [InlineData(65226, new byte[] { 0 })]
    [InlineData(65227, new byte[] { 0, 0, 1 })]
    public void Decode_rejects_unsupported_or_malformed_payload(int pgn, byte[] payload)
        => Assert.ThrowsAny<ArgumentException>(() => J1939DiagnosticDecoder.Decode(pgn, payload));

    [Fact]
    public void Decode_rejects_payload_over_transport_protocol_limit()
        => Assert.Throws<ArgumentException>(() => J1939DiagnosticDecoder.Decode(
            J1939DiagnosticDecoder.Dm1Pgn, new byte[J1939DiagnosticDecoder.MaximumPayloadBytes + 1]));
}
