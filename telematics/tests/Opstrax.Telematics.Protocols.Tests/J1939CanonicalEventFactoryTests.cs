using System.Text.Json;
using Opstrax.Telematics.Contracts.Identity;
using Opstrax.Telematics.Contracts.Lifecycle;
using Opstrax.Telematics.Contracts.Provenance;
using Opstrax.Telematics.Contracts.Signals;
using Opstrax.Telematics.Protocols.J1939;

namespace Opstrax.Telematics.Protocols.Tests;

public sealed class J1939CanonicalEventFactoryTests
{
    private static readonly DateTimeOffset CapturedAt = new(2026, 9, 8, 14, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid EventId = Guid.Parse("20000000-0000-0000-0000-000000000002");
    private static readonly Guid CorrelationId = Guid.Parse("30000000-0000-0000-0000-000000000003");

    [Fact]
    public void Fresh_engine_speed_becomes_a_registry_owned_canonical_can_signal()
    {
        var context = Context(CapturedAt.UtcDateTime.AddSeconds(1));

        var evt = J1939CanonicalEventFactory.Create(
            Message(
                J1939SignalDecoder.ElectronicEngineController1Pgn,
                [0xFF, 0xFF, 0xFF, 0xE0, 0x2E, 0xFF, 0xFF, 0xFF]),
            context);

        Assert.Equal(TenantId, evt.TenantId);
        Assert.Equal(42, evt.CompanyId);
        Assert.Equal("can-gateway-17", evt.DeviceId);
        Assert.Equal(701, evt.VehicleId);
        Assert.Equal(EventId, evt.EventId);
        Assert.Equal(CorrelationId, evt.CorrelationId);
        Assert.Equal(CapturedAt.UtcDateTime, evt.OccurredAtDeviceUtc);
        Assert.Equal(CapturedAt.UtcDateTime, evt.ReceivedAtGatewayUtc);
        Assert.Equal(context.NormalizedAtUtc, evt.NormalizedAtUtc);
        Assert.Equal(TelemetrySource.Simulator, evt.Source);
        Assert.Equal(Transport.Can, evt.Transport);
        Assert.Equal("J1939", evt.ProtocolName);
        Assert.Equal("J1939SignalCatalog", evt.AdapterName);
        Assert.False(evt.Quality.IsStale);

        var signal = Assert.Single(evt.Signals).Value;
        Assert.Equal(1500d, signal.Value);
        Assert.Equal("rpm", signal.Unit);
        Assert.Equal(TelemetrySource.Simulator, signal.Source);
        Assert.Equal(0.75d, signal.Confidence);
        Assert.Equal(SignalAvailability.Available, signal.Availability);
    }

    [Fact]
    public void Stale_battery_value_is_retained_as_evidence_but_not_promoted_to_typed_current_value()
    {
        var context = Context(CapturedAt.UtcDateTime.AddMinutes(16)) with
        {
            FreshnessBudget = TimeSpan.FromMinutes(15),
        };

        var evt = J1939CanonicalEventFactory.Create(
            Message(
                J1939SignalDecoder.VehicleElectricalPower1Pgn,
                [0xFF, 0xFF, 0xFF, 0xFF, 0xFC, 0x00, 0xFF, 0xFF]),
            context);

        var signal = Assert.Single(evt.Signals).Value;
        Assert.Equal(12.6d, Assert.IsType<double>(signal.Value), precision: 6);
        Assert.Equal(SignalAvailability.Stale, signal.Availability);
        Assert.True(evt.Quality.IsStale);
        Assert.Null(evt.BatteryVoltage);
    }

    [Theory]
    [InlineData(0xFB00, SignalAvailability.ParameterSpecific)]
    [InlineData(0xFE00, SignalAvailability.Error)]
    [InlineData(0xFF00, SignalAvailability.NotAvailable)]
    public void J1939_indicator_state_becomes_an_explicit_nonnumeric_canonical_signal(
        ushort rawValue,
        SignalAvailability expectedAvailability)
    {
        var payload = new byte[]
        {
            0xFF, 0xFF, 0xFF,
            (byte)(rawValue & 0xFF),
            (byte)(rawValue >> 8),
            0xFF, 0xFF, 0xFF,
        };

        var evt = J1939CanonicalEventFactory.Create(
            Message(J1939SignalDecoder.ElectronicEngineController1Pgn, payload),
            Context(CapturedAt.UtcDateTime.AddSeconds(1)));

        var signal = Assert.Single(evt.Signals).Value;
        Assert.Null(signal.Value);
        Assert.Equal(expectedAvailability, signal.Availability);
        Assert.False(evt.Quality.IsStale);
    }

    [Fact]
    public void Fresh_battery_value_is_available_on_both_extensible_and_typed_surfaces()
    {
        var evt = J1939CanonicalEventFactory.Create(
            Message(
                J1939SignalDecoder.VehicleElectricalPower1Pgn,
                [0xFF, 0xFF, 0xFF, 0xFF, 0xFC, 0x00, 0xFF, 0xFF]),
            Context(CapturedAt.UtcDateTime.AddSeconds(1)));

        Assert.Equal(12.6d, evt.BatteryVoltage!.Value, precision: 6);
        Assert.Equal(SignalAvailability.Available, Assert.Single(evt.Signals).Value.Availability);
    }

    [Fact]
    public void Canonical_json_persists_named_unavailable_state_instead_of_a_false_value()
    {
        var evt = J1939CanonicalEventFactory.Create(
            Message(
                J1939SignalDecoder.ElectronicEngineController1Pgn,
                [0xFF, 0xFF, 0xFF, 0x00, 0xFF, 0xFF, 0xFF, 0xFF]),
            Context(CapturedAt.UtcDateTime.AddSeconds(1)));

        var json = JsonSerializer.Serialize(evt);

        Assert.Contains("\"Availability\":\"NotAvailable\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Value\":null", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Value\":0", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Unsupported_pgn_and_untrusted_context_shapes_fail_closed()
    {
        var unsupported = Message(65262, [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF]);
        Assert.Throws<ArgumentException>(() => J1939CanonicalEventFactory.Create(
            unsupported,
            Context(CapturedAt.UtcDateTime.AddSeconds(1))));

        var supported = Message(
            J1939SignalDecoder.ElectronicEngineController1Pgn,
            [0xFF, 0xFF, 0xFF, 0xE0, 0x2E, 0xFF, 0xFF, 0xFF]);
        Assert.Throws<ArgumentException>(() => J1939CanonicalEventFactory.Create(
            supported,
            Context(CapturedAt.UtcDateTime.AddSeconds(1)) with { EventId = Guid.Empty }));
        Assert.Throws<ArgumentException>(() => J1939CanonicalEventFactory.Create(
            supported,
            Context(CapturedAt.UtcDateTime.AddTicks(-1))));
        Assert.Throws<ArgumentOutOfRangeException>(() => J1939CanonicalEventFactory.Create(
            supported,
            Context(CapturedAt.UtcDateTime.AddSeconds(1)) with { Confidence = double.NaN }));
    }

    private static J1939CanonicalizationContext Context(DateTime normalizedAtUtc) => new(
        new ResolvedDeviceOwner(
            TenantId,
            CompanyId: 42,
            DeviceId: "can-gateway-17",
            VehicleId: 701,
            LifecycleState: DeviceLifecycleState.Online,
            CredentialHandle: "opaque-handle"),
        EventId,
        CorrelationId,
        TelemetrySource.Simulator,
        normalizedAtUtc,
        FreshnessBudget: TimeSpan.FromMinutes(15),
        TrustScore: 0.5d,
        Confidence: 0.75d);

    private static J1939AcquiredMessage Message(int pgn, byte[] payload)
    {
        var frame = new J1939CanFrameEnvelope(
            RawIdentifier: 0x0CF00400,
            Priority: 3,
            Pgn: pgn,
            SourceAddress: 0x00,
            DestinationAddress: J1939CanIdentifier.GlobalAddress,
            IsPeerToPeer: false,
            Data: payload,
            CapturedAt,
            AdapterType: "socketcan",
            Channel: "can0",
            CaptureReference: "capture-j1939-001");
        return new J1939AcquiredMessage(
            pgn,
            SourceAddress: 0x00,
            DestinationAddress: J1939CanIdentifier.GlobalAddress,
            Payload: payload,
            IsTransported: false,
            MessagePriority: 3,
            FirstFrameAt: CapturedAt,
            CompletedAt: CapturedAt,
            Frames: Array.AsReadOnly([frame]));
    }
}
