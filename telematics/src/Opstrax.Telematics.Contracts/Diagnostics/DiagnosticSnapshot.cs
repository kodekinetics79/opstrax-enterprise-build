namespace Opstrax.Telematics.Contracts.Diagnostics;

/// <summary>Protocol-neutral lamp state retained from a diagnostic observation.</summary>
public enum DiagnosticLampState : byte
{
    /// <summary>The lamp is explicitly off.</summary>
    Off = 0,

    /// <summary>The lamp is explicitly on.</summary>
    On = 1,

    /// <summary>The protocol carried its reserved lamp-state value.</summary>
    Reserved = 2,

    /// <summary>The reporting controller marked the lamp state unavailable.</summary>
    NotAvailable = 3,
}

/// <summary>The eight J1939-73 status and flash-lamp observations.</summary>
public sealed record DiagnosticLampSnapshot(
    DiagnosticLampState Protect,
    DiagnosticLampState AmberWarning,
    DiagnosticLampState RedStop,
    DiagnosticLampState MalfunctionIndicator,
    DiagnosticLampState ProtectFlash,
    DiagnosticLampState AmberWarningFlash,
    DiagnosticLampState RedStopFlash,
    DiagnosticLampState MalfunctionIndicatorFlash);

/// <summary>One decoded trouble code with its stable source-specific identity.</summary>
public sealed record DiagnosticTroubleCode(
    string Code,
    string CanonicalIdentity,
    int? Spn,
    int? Fmi,
    int OccurrenceCount,
    bool ConversionMethod);

/// <summary>
/// Evidence retained from one complete diagnostic protocol message. For J1939,
/// <see cref="IsActive"/> distinguishes DM1 from historical DM2 evidence; a false
/// value is not a clear command.
/// </summary>
public sealed record DiagnosticSnapshot(
    string Protocol,
    int? Pgn,
    bool IsActive,
    int? SourceAddress,
    DiagnosticLampSnapshot Lamps,
    IReadOnlyList<DiagnosticTroubleCode> TroubleCodes);
