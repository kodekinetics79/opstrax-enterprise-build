using Opstrax.Telematics.Contracts.Diagnostics;
using Opstrax.Telematics.Contracts.Identity;
using Opstrax.Telematics.Contracts.Lifecycle;
using Opstrax.Telematics.Contracts.Provenance;
using Opstrax.Telematics.Protocols.J1939;

namespace Opstrax.Telematics.Protocols.Tests;

public sealed class J1939DiagnosticCanonicalEventFactoryTests
{
    private static readonly DateTimeOffset CapturedAt =
        new(2026, 9, 8, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Dm1_retains_structured_lamps_dtc_identity_and_registry_ownership()
    {
        J1939AcquiredMessage message = Message(
            J1939DiagnosticDecoder.Dm1Pgn,
            [0b01_00_11_01, 0b00_01_11_00, 0x34, 0x12, 0x05, 0x07]);

        var canonical = J1939DiagnosticCanonicalEventFactory.Create(message, Context());

        Assert.Equal("diagnostic-device-17", canonical.DeviceId);
        Assert.Equal(TelemetrySource.DirectDevice, canonical.Source);
        Assert.Equal("J1939", canonical.ProtocolName);
        Assert.Equal(["SPN-4660-FMI-5"], canonical.DtcCodes);
        Assert.NotNull(canonical.Diagnostic);
        Assert.True(canonical.Diagnostic.IsActive);
        Assert.Equal(J1939DiagnosticDecoder.Dm1Pgn, canonical.Diagnostic.Pgn);
        Assert.Equal(0x31, canonical.Diagnostic.SourceAddress);
        Assert.Equal(DiagnosticLampState.On, canonical.Diagnostic.Lamps.Protect);
        Assert.Equal(DiagnosticLampState.NotAvailable, canonical.Diagnostic.Lamps.RedStop);
        Assert.Equal(DiagnosticLampState.On, canonical.Diagnostic.Lamps.MalfunctionIndicator);
        DiagnosticTroubleCode dtc = Assert.Single(canonical.Diagnostic.TroubleCodes);
        Assert.Equal("J1939:SA:49:SPN:4660:FMI:5", dtc.CanonicalIdentity);
        Assert.Equal(7, dtc.OccurrenceCount);
        Assert.False(dtc.ConversionMethod);
    }

    [Fact]
    public void Dm2_is_historical_evidence_and_never_appears_as_an_active_or_clear_instruction()
    {
        J1939AcquiredMessage message = Message(
            J1939DiagnosticDecoder.Dm2Pgn,
            [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF]);

        var canonical = J1939DiagnosticCanonicalEventFactory.Create(message, Context());

        Assert.NotNull(canonical.Diagnostic);
        Assert.False(canonical.Diagnostic.IsActive);
        Assert.Empty(canonical.Diagnostic.TroubleCodes);
        Assert.Empty(canonical.DtcCodes);
    }

    [Fact]
    public void Diagnostic_freshness_uses_capture_completion_and_never_changes_the_dtc_evidence()
    {
        J1939AcquiredMessage message = Message(
            J1939DiagnosticDecoder.Dm1Pgn,
            [0, 0, 1, 0, 0, 1]);
        J1939CanonicalizationContext stale = Context() with
        {
            NormalizedAtUtc = CapturedAt.UtcDateTime.AddMinutes(2),
            FreshnessBudget = TimeSpan.FromMinutes(1),
        };

        var canonical = J1939DiagnosticCanonicalEventFactory.Create(message, stale);

        Assert.True(canonical.Quality.IsStale);
        Assert.Equal(["SPN-1-FMI-0"], canonical.DtcCodes);
    }

    private static J1939CanonicalizationContext Context() => new(
        new ResolvedDeviceOwner(
            Guid.Parse("82000000-0000-0000-0000-000000000008"),
            CompanyId: 8200,
            DeviceId: "diagnostic-device-17",
            VehicleId: 8217,
            LifecycleState: DeviceLifecycleState.Online,
            CredentialHandle: "physical-boundary"),
        EventId: Guid.Parse("83000000-0000-0000-0000-000000000008"),
        CorrelationId: Guid.Parse("84000000-0000-0000-0000-000000000008"),
        Source: TelemetrySource.DirectDevice,
        NormalizedAtUtc: CapturedAt.UtcDateTime.AddSeconds(1),
        FreshnessBudget: TimeSpan.FromMinutes(1),
        TrustScore: 0.25,
        Confidence: 0.75);

    private static J1939AcquiredMessage Message(int pgn, byte[] payload)
    {
        var frame = new J1939CanFrameEnvelope(
            RawIdentifier: pgn == J1939DiagnosticDecoder.Dm1Pgn ? 0x18FECA31u : 0x18FECB31u,
            Priority: 6,
            Pgn: pgn,
            SourceAddress: 0x31,
            DestinationAddress: J1939CanIdentifier.GlobalAddress,
            IsPeerToPeer: false,
            Data: payload,
            CapturedAt,
            AdapterType: "socketcan-exact-adapter",
            Channel: "can0",
            CaptureReference: "capture-diagnostic-001");
        return new J1939AcquiredMessage(
            pgn,
            SourceAddress: 0x31,
            DestinationAddress: J1939CanIdentifier.GlobalAddress,
            Payload: payload,
            IsTransported: false,
            MessagePriority: 6,
            FirstFrameAt: CapturedAt,
            CompletedAt: CapturedAt,
            Frames: Array.AsReadOnly([frame]));
    }
}
