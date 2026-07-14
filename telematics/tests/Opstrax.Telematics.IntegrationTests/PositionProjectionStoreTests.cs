using Opstrax.Telematics.Contracts;
using Opstrax.Telematics.Contracts.Provenance;
using Opstrax.Telematics.Contracts.Signals;
using Opstrax.Telematics.Gateway.Projection;

namespace Opstrax.Telematics.IntegrationTests;

/// <summary>
/// Unit tests for the idempotent + monotonic live-position projection, against the in-memory store
/// that mirrors the Postgres contract in <c>database/migrations/telematics/006_projection_inbox.sql</c>.
/// The two properties under test are the ones that keep the live map correct under at-least-once,
/// possibly-reordered delivery: a redelivered event is a no-op, and an older fix never overwrites a
/// newer one.
/// </summary>
public class PositionProjectionStoreTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const long Company = 42L;
    private const long Vehicle = 7L;

    private static readonly DateTime T0 = new(2026, 7, 12, 10, 0, 0, DateTimeKind.Utc);

    private static CanonicalTelemetryEvent Fix(
        Guid eventId,
        DateTime deviceFixUtc,
        double lat = 24.7,
        double lng = 46.7,
        long? vehicleId = Vehicle,
        GeoPoint? location = null)
    {
        return new CanonicalTelemetryEvent
        {
            SchemaVersion = CanonicalTelemetryEvent.CurrentSchemaVersion,
            EventId = eventId,
            CorrelationId = Guid.NewGuid(),
            OccurredAtDeviceUtc = deviceFixUtc,
            ReceivedAtGatewayUtc = deviceFixUtc.AddSeconds(1),
            NormalizedAtUtc = deviceFixUtc.AddSeconds(2),
            TenantId = Tenant,
            CompanyId = Company,
            DeviceId = "dev-1",
            VehicleId = vehicleId,
            Source = TelemetrySource.DirectDevice,
            Transport = Transport.Tcp,
            ProtocolName = "GT06",
            AdapterName = "GT06",
            AdapterVersion = "1.0.0",
            Location = location ?? new GeoPoint(lat, lng, SpeedKph: 50, HeadingDeg: 90),
        };
    }

    [Fact]
    public async Task Duplicate_event_id_is_a_no_op()
    {
        var store = new InMemoryPositionProjectionStore();
        Guid id = Guid.NewGuid();
        var evt = Fix(id, T0);

        ProjectionOutcome first = await store.ApplyAsync(evt);
        ProjectionOutcome second = await store.ApplyAsync(evt);

        Assert.Equal(ProjectionOutcome.Applied, first);
        Assert.Equal(ProjectionOutcome.DuplicateIgnored, second);
        Assert.Equal(1, store.SeenCount);

        // The snapshot still reflects exactly the single applied fix.
        CanonicalTelemetryEvent? latest = store.Latest(Company, Vehicle);
        Assert.NotNull(latest);
        Assert.Equal(id, latest!.EventId);
    }

    [Fact]
    public async Task Redelivery_with_a_different_event_id_but_older_fix_does_not_overwrite()
    {
        var store = new InMemoryPositionProjectionStore();

        var newer = Fix(Guid.NewGuid(), T0.AddMinutes(5), lat: 24.9, lng: 46.9);
        var older = Fix(Guid.NewGuid(), T0, lat: 24.1, lng: 46.1);

        Assert.Equal(ProjectionOutcome.Applied, await store.ApplyAsync(newer));
        // The older fix arrives late (e.g. a delayed store-and-forward replay). It must not win.
        Assert.Equal(ProjectionOutcome.StaleIgnored, await store.ApplyAsync(older));

        CanonicalTelemetryEvent? latest = store.Latest(Company, Vehicle);
        Assert.NotNull(latest);
        Assert.Equal(newer.EventId, latest!.EventId);
        Assert.Equal(T0.AddMinutes(5), latest.OccurredAtDeviceUtc);

        // Both distinct events were still recorded as seen (so either can be re-deduped).
        Assert.Equal(2, store.SeenCount);
    }

    [Fact]
    public async Task Newer_fix_overwrites_the_stored_older_one()
    {
        var store = new InMemoryPositionProjectionStore();

        var older = Fix(Guid.NewGuid(), T0);
        var newer = Fix(Guid.NewGuid(), T0.AddMinutes(1));

        Assert.Equal(ProjectionOutcome.Applied, await store.ApplyAsync(older));
        Assert.Equal(ProjectionOutcome.Applied, await store.ApplyAsync(newer));

        Assert.Equal(newer.EventId, store.Latest(Company, Vehicle)!.EventId);
    }

    [Fact]
    public async Task Equal_fix_time_is_applied_when_not_a_duplicate_event()
    {
        // Two distinct observations sharing a fix timestamp: the later application wins (>= guard),
        // which matches the DB upsert's WHERE EXCLUDED.device_fix_time >= stored.
        var store = new InMemoryPositionProjectionStore();

        var a = Fix(Guid.NewGuid(), T0, lat: 1, lng: 1);
        var b = Fix(Guid.NewGuid(), T0, lat: 2, lng: 2);

        Assert.Equal(ProjectionOutcome.Applied, await store.ApplyAsync(a));
        Assert.Equal(ProjectionOutcome.Applied, await store.ApplyAsync(b));

        Assert.Equal(b.EventId, store.Latest(Company, Vehicle)!.EventId);
    }

    [Fact]
    public async Task Positionless_event_is_deduped_but_not_projected()
    {
        var store = new InMemoryPositionProjectionStore();
        var heartbeat = Fix(Guid.NewGuid(), T0) with { Location = null };

        Assert.Equal(ProjectionOutcome.NoLocation, await store.ApplyAsync(heartbeat));
        Assert.Null(store.Latest(Company, Vehicle));
        Assert.Equal(1, store.SeenCount);

        // A redelivery of the same positionless event is still a duplicate no-op.
        Assert.Equal(ProjectionOutcome.DuplicateIgnored, await store.ApplyAsync(heartbeat));
    }

    [Fact]
    public async Task Vehicleless_event_is_deduped_but_not_projected()
    {
        var store = new InMemoryPositionProjectionStore();
        var unbound = Fix(Guid.NewGuid(), T0, vehicleId: null);

        Assert.Equal(ProjectionOutcome.NoVehicle, await store.ApplyAsync(unbound));
        Assert.Null(store.Latest(Company, Vehicle));
        Assert.Equal(1, store.SeenCount);
    }
}
