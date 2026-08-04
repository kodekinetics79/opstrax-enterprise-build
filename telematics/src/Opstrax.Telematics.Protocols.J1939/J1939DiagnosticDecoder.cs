namespace Opstrax.Telematics.Protocols.J1939;

/// <summary>
/// Decodes an already reassembled SAE J1939-73 DM1 (PGN 65226) or DM2 (PGN 65227)
/// payload. CAN transport-protocol reassembly is deliberately outside this type: callers
/// must supply one complete PGN payload and preserve the source address separately.
/// </summary>
public static class J1939DiagnosticDecoder
{
    public const int Dm1Pgn = 65226;
    public const int Dm2Pgn = 65227;
    public const int MaximumPayloadBytes = 1785;

    public static DiagnosticMessage Decode(int pgn, ReadOnlySpan<byte> payload)
    {
        if (pgn is not (Dm1Pgn or Dm2Pgn))
            throw new ArgumentOutOfRangeException(nameof(pgn), "Only DM1 and DM2 PGNs are supported.");
        if (payload.Length < 2)
            throw new ArgumentException("A DM1/DM2 payload must include two lamp-state bytes.", nameof(payload));
        if (payload.Length > MaximumPayloadBytes)
            throw new ArgumentException($"A reassembled J1939 payload cannot exceed {MaximumPayloadBytes} bytes.", nameof(payload));
        if ((payload.Length - 2) % 4 != 0)
            throw new ArgumentException("The DTC section must contain complete four-byte DTC entries.", nameof(payload));

        var lamps = new DiagnosticLampStates(
            DecodeLamp(payload[0], 6), DecodeLamp(payload[0], 4),
            DecodeLamp(payload[0], 2), DecodeLamp(payload[0], 0),
            DecodeLamp(payload[1], 6), DecodeLamp(payload[1], 4),
            DecodeLamp(payload[1], 2), DecodeLamp(payload[1], 0));

        var dtcs = new List<J1939Dtc>((payload.Length - 2) / 4);
        for (var offset = 2; offset < payload.Length; offset += 4)
        {
            var b0 = payload[offset];
            var b1 = payload[offset + 1];
            var b2 = payload[offset + 2];
            var b3 = payload[offset + 3];

            // J1939-73 version 4 conversion: SPN bits 1..16 are little-endian in
            // bytes 1/2 and bits 17..19 occupy bits 8..6 of byte 3. FMI is bits 5..1.
            var spn = b0 | (b1 << 8) | ((b2 & 0xE0) << 11);
            var fmi = b2 & 0x1F;
            var occurrenceCount = b3 & 0x7F;
            var conversionMethod = (b3 & 0x80) != 0;

            // All-ones is the J1939 "not available" padding value, not a real DTC.
            if (spn == 0x7FFFF && fmi == 0x1F && occurrenceCount == 0x7F && conversionMethod)
                continue;

            dtcs.Add(new J1939Dtc(spn, fmi, occurrenceCount, conversionMethod));
        }

        return new DiagnosticMessage(pgn, pgn == Dm1Pgn, lamps, dtcs);
    }

    private static LampState DecodeLamp(byte value, int shift) => (LampState)((value >> shift) & 0x03);
}

public enum LampState : byte
{
    Off = 0,
    On = 1,
    Reserved = 2,
    NotAvailable = 3,
}

public sealed record DiagnosticLampStates(
    LampState Protect,
    LampState AmberWarning,
    LampState RedStop,
    LampState MalfunctionIndicator,
    LampState ProtectFlash,
    LampState AmberWarningFlash,
    LampState RedStopFlash,
    LampState MalfunctionIndicatorFlash);

public sealed record J1939Dtc(int Spn, int Fmi, int OccurrenceCount, bool ConversionMethod);

public sealed record DiagnosticMessage(
    int Pgn,
    bool IsActive,
    DiagnosticLampStates Lamps,
    IReadOnlyList<J1939Dtc> Dtcs);
