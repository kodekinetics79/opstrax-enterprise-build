using Opstrax.Telematics.Contracts;
using Opstrax.Telematics.Contracts.Provenance;

namespace Opstrax.Telematics.IntegrationTests;

/// <summary>
/// Trivial smoke test proving the Gateway, Contracts and Gt06 projects are referenced
/// and the solution links end-to-end. Real gateway integration tests (host boot, framing
/// loop) replace this once a listener is wired.
/// </summary>
public class GatewaySmokeTests
{
    [Fact]
    public void Canonical_event_carries_current_schema_version_and_provenance()
    {
        var evt = new CanonicalTelemetryEvent
        {
            SchemaVersion = CanonicalTelemetryEvent.CurrentSchemaVersion,
            EventId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            OccurredAtDeviceUtc = DateTime.UtcNow,
            ReceivedAtGatewayUtc = DateTime.UtcNow,
            NormalizedAtUtc = DateTime.UtcNow,
            TenantId = Guid.NewGuid(),
            CompanyId = 1,
            DeviceId = "dev-1",
            Source = TelemetrySource.Simulator,
            Transport = Transport.Tcp,
            ProtocolName = "GT06",
            AdapterName = "GT06",
            AdapterVersion = "0.0.0",
        };

        Assert.Equal(1, evt.SchemaVersion);
        Assert.Equal(TelemetrySource.Simulator, evt.Source);
        Assert.True(evt.Quality.IsClean);
    }
}
