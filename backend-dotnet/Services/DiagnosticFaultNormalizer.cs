using System.Text.Json;
using Opstrax.Telematics.Protocols.J1939;

namespace Opstrax.Api.Services;

public sealed record DiagnosticDtcInput(int Spn, int Fmi, int? OccurrenceCount = 1);

public sealed record DiagnosticFaultIngestRequest(
    string? Code = null,
    string? CodeType = "OBD",
    string? Description = null,
    string? Severity = null,
    string? SourceEventId = null,
    string? ObservedAt = null,
    string? Protocol = null,
    string? Controller = null,
    int? SourceAddress = null,
    string? Bus = null,
    int? Spn = null,
    int? Fmi = null,
    int? OccurrenceCount = 1,
    JsonElement? LampStatus = null,
    JsonElement? RawEvidence = null,
    bool Cleared = false,
    string? ClearSource = null,
    int? Pgn = null,
    string? RawPayloadBase64 = null,
    IReadOnlyList<DiagnosticDtcInput>? Dtcs = null);

public sealed record NormalizedDiagnosticDtc(
    int Ordinal,
    string Code,
    string CanonicalIdentity,
    int? Spn,
    int? Fmi,
    int OccurrenceCount,
    string Severity);

public sealed record NormalizedDiagnosticBatch(
    string Protocol,
    bool MutatesProjection,
    bool ClearsProjection,
    string LampStatusJson,
    string? RawEvidenceJson,
    IReadOnlyList<NormalizedDiagnosticDtc> Dtcs);

public static class DiagnosticFaultNormalizer
{
    public static bool TryNormalize(DiagnosticFaultIngestRequest request, out NormalizedDiagnosticBatch? batch, out string? error)
    {
        batch = null;
        error = null;
        var protocol = (request.Protocol ?? request.CodeType ?? "OBD").Trim().ToUpperInvariant();
        if (protocol is not ("OBD" or "J1939" or "OEM"))
        {
            error = "protocol must be OBD, J1939 or OEM";
            return false;
        }

        if (request.SourceAddress is < 0 or > 253)
        {
            error = "sourceAddress is outside J1939 bounds";
            return false;
        }

        if (protocol == "J1939")
            return TryNormalizeJ1939(request, out batch, out error);

        if (string.IsNullOrWhiteSpace(request.Code))
        {
            error = "code is required for OBD/OEM diagnostics";
            return false;
        }

        var code = request.Code.Trim().ToUpperInvariant();
        if (code.Length > 40)
        {
            error = "Diagnostic code exceeds accepted length";
            return false;
        }
        var origin = NormalizeOrigin(request.Controller, request.SourceAddress);
        // OBD/OEM severity is deliberately policy-derived. Until a tenant/OEM catalog says
        // otherwise, a device cannot self-promote or self-demote a fault.
        var severity = request.Cleared ? "Info" : "Warning";
        batch = new NormalizedDiagnosticBatch(protocol, true, request.Cleared, "{}",
            request.RawEvidence?.GetRawText(),
            [new NormalizedDiagnosticDtc(0, code, $"{protocol}:{origin}:{code}", null, null,
                Math.Clamp(request.OccurrenceCount ?? 1, 1, 127), severity)]);
        return true;
    }

    private static bool TryNormalizeJ1939(DiagnosticFaultIngestRequest request,
        out NormalizedDiagnosticBatch? batch, out string? error)
    {
        batch = null;
        error = null;
        if (request.Cleared)
        {
            error = "DM2 is historical evidence, not a clear command; J1939 clear state must come from an authoritative lifecycle event";
            return false;
        }
        if (request.Pgn is not (J1939DiagnosticDecoder.Dm1Pgn or J1939DiagnosticDecoder.Dm2Pgn))
        {
            error = "J1939 diagnostics require pgn 65226 (DM1) or 65227 (DM2)";
            return false;
        }

        DiagnosticLampStates lamps;
        IReadOnlyList<J1939Dtc> decodedDtcs;
        string? evidence;
        if (!string.IsNullOrWhiteSpace(request.RawPayloadBase64))
        {
            byte[] payload;
            try { payload = Convert.FromBase64String(request.RawPayloadBase64); }
            catch (FormatException)
            {
                error = "rawPayloadBase64 is not valid base64";
                return false;
            }
            try
            {
                var decoded = J1939DiagnosticDecoder.Decode(request.Pgn.Value, payload);
                lamps = decoded.Lamps;
                decodedDtcs = decoded.Dtcs;
            }
            catch (ArgumentException ex)
            {
                error = ex.Message;
                return false;
            }
            evidence = JsonSerializer.Serialize(new
            {
                pgn = request.Pgn.Value,
                rawPayloadBase64 = Convert.ToBase64String(payload),
                suppliedEvidence = request.RawEvidence
            });
        }
        else
        {
            if (request.Dtcs is not { Count: > 0 })
            {
                error = "J1939 diagnostics require a complete raw payload or a non-empty dtcs array";
                return false;
            }
            if (!TryReadLamps(request.LampStatus, out lamps))
            {
                error = "normalized J1939 diagnostics require all eight valid lamp states";
                return false;
            }
            if (request.Dtcs.Any(d => d.Spn is < 0 or > 524287 || d.Fmi is < 0 or > 31 || d.OccurrenceCount is < 1 or > 127))
            {
                error = "J1939 DTC value is outside protocol bounds";
                return false;
            }
            decodedDtcs = request.Dtcs.Select(d => new J1939Dtc(d.Spn, d.Fmi, d.OccurrenceCount ?? 1, false)).ToArray();
            evidence = request.RawEvidence?.GetRawText();
        }

        if (decodedDtcs.Count == 0)
        {
            error = "J1939 diagnostic message contains no DTCs";
            return false;
        }

        var lampJson = JsonSerializer.Serialize(lamps);
        var severity = DeriveJ1939Severity(lamps);
        var origin = NormalizeOrigin(request.Controller, request.SourceAddress);
        var dtcs = decodedDtcs.Select((d, ordinal) =>
            new NormalizedDiagnosticDtc(ordinal, $"SPN-{d.Spn}-FMI-{d.Fmi}",
                $"J1939:{origin}:SPN:{d.Spn}:FMI:{d.Fmi}", d.Spn, d.Fmi,
                Math.Clamp(d.OccurrenceCount, 1, 127), severity)).ToArray();
        batch = new NormalizedDiagnosticBatch("J1939", request.Pgn == J1939DiagnosticDecoder.Dm1Pgn,
            false, lampJson, evidence, dtcs);
        return true;
    }

    public static string DeriveJ1939Severity(DiagnosticLampStates lamps)
    {
        if (lamps.RedStop == LampState.On || lamps.RedStopFlash == LampState.On)
            return "Critical";
        if (lamps.Protect == LampState.On || lamps.AmberWarning == LampState.On ||
            lamps.MalfunctionIndicator == LampState.On || lamps.ProtectFlash == LampState.On ||
            lamps.AmberWarningFlash == LampState.On || lamps.MalfunctionIndicatorFlash == LampState.On)
            return "Warning";
        return "Info";
    }

    private static string NormalizeOrigin(string? controller, int? sourceAddress)
    {
        if (!string.IsNullOrWhiteSpace(controller))
            return controller.Trim().ToUpperInvariant().Replace(':', '_');
        return sourceAddress.HasValue ? $"SA:{sourceAddress.Value:D2}" : "UNKNOWN";
    }

    private static bool TryReadLamps(JsonElement? element, out DiagnosticLampStates lamps)
    {
        lamps = default!;
        if (element is not { ValueKind: JsonValueKind.Object } value) return false;
        if (!TryLamp(value, "protect", out var protect) ||
            !TryLamp(value, "amberWarning", out var amber) ||
            !TryLamp(value, "redStop", out var red) ||
            !TryLamp(value, "malfunctionIndicator", out var mil) ||
            !TryLamp(value, "protectFlash", out var protectFlash) ||
            !TryLamp(value, "amberWarningFlash", out var amberFlash) ||
            !TryLamp(value, "redStopFlash", out var redFlash) ||
            !TryLamp(value, "malfunctionIndicatorFlash", out var milFlash))
            return false;
        lamps = new DiagnosticLampStates(protect, amber, red, mil, protectFlash, amberFlash, redFlash, milFlash);
        return true;
    }

    private static bool TryLamp(JsonElement root, string name, out LampState state)
    {
        state = default;
        JsonElement value = default;
        var present = false;
        foreach (var property in root.EnumerateObject())
        {
            if (!property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            value = property.Value;
            present = true;
            break;
        }
        if (!present) return false;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetByte(out var number) && number <= 3)
        {
            state = (LampState)number;
            return true;
        }
        return value.ValueKind == JsonValueKind.String &&
               Enum.TryParse(value.GetString(), true, out state) && Enum.IsDefined(state);
    }
}
