using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Opstrax.Telematics.Contracts;
using Opstrax.Telematics.Contracts.Eventing;
using Opstrax.Telematics.Contracts.Identity;
using Opstrax.Telematics.Contracts.Lifecycle;
using Opstrax.Telematics.Contracts.Provenance;
using Opstrax.Telematics.Gateway;
using Opstrax.Telematics.Gateway.Buffering;
using Opstrax.Telematics.Gateway.Eventing;
using Opstrax.Telematics.Gateway.Identity;
using Opstrax.Telematics.Gateway.Observability;
using Opstrax.Telematics.Gateway.Projection;
using Opstrax.Telematics.Gateway.Security.Auth;
using Opstrax.Telematics.Gateway.Security.Replay;
using Opstrax.Telematics.Protocols.Gt06;

namespace Opstrax.Telematics.IntegrationTests;

/// <summary>
/// End-to-end slice tests for the TCP device edge. These are deliberately <b>not</b> mocked:
/// the gateway binds a real <see cref="TcpListener"/> on an ephemeral port and the test drives
/// it with a real <see cref="TcpClient"/> over a real loopback socket, sending byte-accurate
/// GT06 frames from the same fixtures the protocol tests use.
/// </summary>
/// <remarks>
/// The properties under test are the ones that actually matter at an edge: the device gets the
/// exact ack bytes its firmware waits for, a fix reaches the backbone bound to the tenant the
/// <em>registry</em> named (never the one the packet claimed), an unknown IMEI is refused
/// service rather than quietly attributed to somebody, and no volume of hostile garbage can
/// take the listener down or disturb a healthy connection next to it.
/// </remarks>
public class GatewayTcpSliceTests
{
    private static readonly TimeSpan BusTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SocketTimeout = TimeSpan.FromSeconds(5);

    // ── (a) + (b): the happy path, over a real socket ──────────────────────────

    [Fact]
    public async Task Login_then_location_returns_the_exact_ack_bytes_and_publishes_a_canonical_event()
    {
        await using GatewayHarness gw = await GatewayHarness.StartAsync();

        // Subscribe BEFORE we transmit: the in-memory bus has no historical retention.
        await using IEventSubscription<CanonicalTelemetryEvent> normalized =
            gw.Bus.Subscribe<CanonicalTelemetryEvent>(TelematicsTopics.TelemetryNormalized);

        using TcpClient client = await gw.ConnectAsync();
        NetworkStream stream = client.GetStream();

        // (a) Login → the device must get back the exact GT06 server response its firmware
        //     is blocking on. A byte wrong here and a real tracker never sends a fix.
        await stream.WriteAsync(Fixture("login.hex"));

        byte[] expectedAck = Fixture("login_ack.hex");
        byte[] ack = await ReadExactlyAsync(stream, expectedAck.Length);
        Assert.Equal(expectedAck, ack);

        // (b) Location → a canonical event on the backbone.
        await stream.WriteAsync(Fixture("location_0x12.hex"));

        DeliveredEvent<CanonicalTelemetryEvent>? delivered = await ReadOneAsync(normalized);
        Assert.NotNull(delivered);

        EventEnvelope<CanonicalTelemetryEvent> envelope = delivered!.Value.Envelope;
        CanonicalTelemetryEvent evt = envelope.Payload;

        // The fix itself.
        Assert.NotNull(evt.Location);
        Assert.Equal(32.7767, evt.Location!.Value.Lat, 3);
        Assert.Equal(-96.7970, evt.Location!.Value.Lng, 3);
        Assert.Equal(60.0, evt.Location!.Value.SpeedKph!.Value);

        // Provenance.
        Assert.Equal(TelemetrySource.DirectDevice, evt.Source);
        Assert.Equal(Transport.Tcp, evt.Transport);
        Assert.Equal("GT06", evt.ProtocolName);
        Assert.Equal("GT06", evt.AdapterName);
        Assert.NotEqual(Guid.Empty, evt.CorrelationId);

        // Ownership came from the REGISTRY, not from the IMEI in the packet.
        Assert.Equal(InMemoryDeviceRegistry.KnownTenantId, evt.TenantId);
        Assert.Equal(InMemoryDeviceRegistry.KnownCompanyId, evt.CompanyId);
        Assert.Equal(InMemoryDeviceRegistry.KnownDeviceId, evt.DeviceId);
        Assert.Equal(InMemoryDeviceRegistry.KnownVehicleId, evt.VehicleId!.Value);
        Assert.NotEqual(InMemoryDeviceRegistry.KnownImei, evt.DeviceId);

        // The envelope's isolation scope mirrors the resolved ownership, and the partition key
        // pins this device's fixes to one ordering group.
        Assert.Equal(InMemoryDeviceRegistry.KnownTenantId, envelope.TenantId);
        Assert.Equal(InMemoryDeviceRegistry.KnownCompanyId, envelope.CompanyId);
        Assert.Equal(evt.CorrelationId, envelope.CorrelationId);
        Assert.Equal(
            TelematicsEventKey.ForDevice(evt.TenantId, evt.CompanyId, evt.DeviceId),
            delivered.Value.Key);

        // EventsPublished is incremented *after* the backbone accepts the event, so the
        // subscriber can legitimately observe the event a hair before the counter moves.
        await WaitUntilAsync(() => gw.Metrics.EventsPublished >= 1, TimeSpan.FromSeconds(2));
        Assert.Equal(1L, gw.Metrics.EventsPublished);
        Assert.Equal(0L, gw.Metrics.UnknownDeviceRejections);
    }

    // ── (c): an unknown IMEI must never be attributed to anyone ────────────────

    [Fact]
    public async Task Login_from_an_unknown_imei_is_rejected_and_is_never_bound_to_a_tenant()
    {
        await using GatewayHarness gw = await GatewayHarness.StartAsync();

        await using IEventSubscription<TelemetryRejection> rejected =
            gw.Bus.Subscribe<TelemetryRejection>(TelematicsTopics.TelemetryRejected);
        await using IEventSubscription<CanonicalTelemetryEvent> normalized =
            gw.Bus.Subscribe<CanonicalTelemetryEvent>(TelematicsTopics.TelemetryNormalized);

        // A perfectly well-formed, CRC-valid login — for an IMEI nobody provisioned.
        const string UnknownImei = "351234567890999";

        using TcpClient client = await gw.ConnectAsync();
        NetworkStream stream = client.GetStream();
        await stream.WriteAsync(BuildLoginFrame(UnknownImei));

        DeliveredEvent<TelemetryRejection>? delivered = await ReadOneAsync(rejected);
        Assert.NotNull(delivered);

        EventEnvelope<TelemetryRejection> envelope = delivered!.Value.Envelope;

        // THE assertion: the rejection is not bound to any tenant. The gateway would rather
        // publish an ownerless rejection than guess an owner.
        Assert.Equal(Guid.Empty, envelope.TenantId);
        Assert.Equal(0L, envelope.CompanyId);

        Assert.Equal(RejectionReasons.UnknownDevice, envelope.Payload.Reason);
        Assert.Equal("GT06", envelope.Payload.ProtocolName);
        Assert.Equal("Login", envelope.Payload.MessageType);

        // The claimed IMEI is masked even on the rejection lane.
        Assert.DoesNotContain(UnknownImei, envelope.Payload.ClaimedIdentifierMasked, StringComparison.Ordinal);
        Assert.Contains("*", envelope.Payload.ClaimedIdentifierMasked, StringComparison.Ordinal);

        Assert.Equal(1L, gw.Metrics.UnknownDeviceRejections);

        // No telemetry was admitted for it, and it was not even acked — an unprovisioned device
        // gets no service, so it cannot proceed to stream fixes.
        Assert.Null(await ReadOneAsync(normalized, TimeSpan.FromMilliseconds(400)));
        await Task.Delay(200);
        Assert.False(stream.DataAvailable, "an unknown device must not receive a login ack");
        Assert.Equal(0L, gw.Metrics.EventsPublished);
    }

    // ── Hostile input must not be able to hurt anyone ──────────────────────────

    [Fact]
    public async Task A_malformed_flood_never_takes_the_gateway_down_and_a_valid_connection_still_decodes()
    {
        await using GatewayHarness gw = await GatewayHarness.StartAsync();

        await using IEventSubscription<CanonicalTelemetryEvent> normalized =
            gw.Bus.Subscribe<CanonicalTelemetryEvent>(TelematicsTopics.TelemetryNormalized);

        // 24 hostile connections at once, in three flavours of broken.
        var flood = new List<Task>();
        for (int i = 0; i < 24; i++)
        {
            int n = i;
            flood.Add(Task.Run(async () =>
            {
                try
                {
                    using var hostile = new TcpClient();
                    await hostile.ConnectAsync(IPAddress.Loopback, gw.Port);
                    NetworkStream s = hostile.GetStream();

                    byte[] payload = (n % 3) switch
                    {
                        // No GT06 start marker anywhere: TryIdentify says "not mine".
                        0 => Garbage(512, seed: n),
                        // Valid marker, impossible length header.
                        1 => Fixture("malformed_length.hex"),
                        // Valid marker and length, shredded stop bits.
                        _ => WithBrokenStopBits(Fixture("location_0x12.hex")),
                    };

                    await s.WriteAsync(payload);
                    await s.FlushAsync();
                    await Task.Delay(50);
                }
                catch (IOException)
                {
                    // The gateway hanging up on us is the CORRECT behaviour, not a failure.
                }
                catch (SocketException)
                {
                }
            }));
        }

        await Task.WhenAll(flood);

        // The listener must still be up, and a healthy device sitting next to that flood must
        // be completely undisturbed by it.
        using TcpClient good = await gw.ConnectAsync();
        NetworkStream stream = good.GetStream();

        await stream.WriteAsync(Fixture("login.hex"));
        Assert.Equal(Fixture("login_ack.hex"), await ReadExactlyAsync(stream, 10));

        await stream.WriteAsync(Fixture("location_0x12.hex"));

        DeliveredEvent<CanonicalTelemetryEvent>? delivered = await ReadOneAsync(normalized);
        Assert.NotNull(delivered);
        Assert.Equal(32.7767, delivered!.Value.Envelope.Payload.Location!.Value.Lat, 3);
        Assert.Equal(InMemoryDeviceRegistry.KnownTenantId, delivered.Value.Envelope.TenantId);

        // The garbage was dropped at the connection boundary...
        Assert.True(
            gw.Metrics.MalformedConnectionsDropped > 0,
            "the hostile connections should have been dropped fail-closed");

        // ...and — the point of the whole exercise — not one event was fabricated out of it.
        await WaitUntilAsync(() => gw.Metrics.EventsPublished >= 1, TimeSpan.FromSeconds(2));
        Assert.Equal(1L, gw.Metrics.EventsPublished);
    }

    // ── Real sockets fragment frames; reassembly must be honest about it ───────

    [Fact]
    public async Task Frames_split_across_tcp_segments_are_reassembled_and_decoded()
    {
        await using GatewayHarness gw = await GatewayHarness.StartAsync();

        await using IEventSubscription<CanonicalTelemetryEvent> normalized =
            gw.Bus.Subscribe<CanonicalTelemetryEvent>(TelematicsTopics.TelemetryNormalized);

        using TcpClient client = await gw.ConnectAsync();
        NetworkStream stream = client.GetStream();

        // Login torn in half mid-frame, with a pause that guarantees two separate reads.
        byte[] login = Fixture("login.hex");
        await stream.WriteAsync(login.AsMemory(0, 5));
        await stream.FlushAsync();
        await Task.Delay(100);
        await stream.WriteAsync(login.AsMemory(5));

        Assert.Equal(Fixture("login_ack.hex"), await ReadExactlyAsync(stream, 10));

        // Location dribbled one byte per segment — the pathological case for a framing loop.
        byte[] location = Fixture("location_0x12.hex");
        for (int i = 0; i < location.Length; i++)
        {
            await stream.WriteAsync(location.AsMemory(i, 1));
            await stream.FlushAsync();
        }

        DeliveredEvent<CanonicalTelemetryEvent>? delivered = await ReadOneAsync(normalized);
        Assert.NotNull(delivered);
        Assert.Equal(32.7767, delivered!.Value.Envelope.Payload.Location!.Value.Lat, 3);
        Assert.Equal(-96.7970, delivered.Value.Envelope.Payload.Location!.Value.Lng, 3);
    }

    // ── Quotas are real, not decorative ────────────────────────────────────────

    [Fact]
    public async Task Connections_beyond_the_quota_are_shed_rather_than_queued()
    {
        await using GatewayHarness gw = await GatewayHarness.StartAsync(new GatewayOptions
        {
            ListenPort = 0,
            MaxConnections = 2,
            MaxInFlightPerConnection = 8,
            IdleTimeout = TimeSpan.FromSeconds(30),
            DrainTimeout = TimeSpan.FromSeconds(5),
        });

        // Hold the quota open with two silent (but live) connections.
        using var a = await gw.ConnectAsync();
        using var b = await gw.ConnectAsync();

        await WaitUntilAsync(() => gw.Metrics.ActiveConnections == 2, TimeSpan.FromSeconds(3));

        // The third must be shed, not queued behind the other two.
        using var c = new TcpClient();
        await c.ConnectAsync(IPAddress.Loopback, gw.Port);

        await WaitUntilAsync(() => gw.Metrics.ConnectionsRejectedQuota >= 1, TimeSpan.FromSeconds(3));
        Assert.True(gw.Metrics.ConnectionsRejectedQuota >= 1);
        Assert.True(gw.Metrics.ActiveConnections <= 2);
    }

    // ── (a) Increment-2: a byte-for-byte replay is dropped and counted ─────────

    [Fact]
    public async Task A_replayed_location_frame_is_dropped_and_counted_not_published_twice()
    {
        await using GatewayHarness gw = await GatewayHarness.StartAsync();

        await using IEventSubscription<CanonicalTelemetryEvent> normalized =
            gw.Bus.Subscribe<CanonicalTelemetryEvent>(TelematicsTopics.TelemetryNormalized);

        // Observe the real OTel duplicate/replay instruments the pipeline records on a drop. The
        // instruments are written on the connection's read-loop thread, so count via Interlocked.
        long duplicates = 0, replays = 0;
        using MeterListener meter = ReplayMeterListener(
            onDuplicate: m => Interlocked.Add(ref duplicates, m),
            onReplay: m => Interlocked.Add(ref replays, m));

        using TcpClient client = await gw.ConnectAsync();
        NetworkStream stream = client.GetStream();

        await stream.WriteAsync(Fixture("login.hex"));
        await ReadExactlyAsync(stream, Fixture("login_ack.hex").Length);

        byte[] frame = BuildLocationFrame(serial: 100, fixTime: BaseFix);

        // First transmission: novel, in-order → published.
        await stream.WriteAsync(frame);
        DeliveredEvent<CanonicalTelemetryEvent>? first = await ReadOneAsync(normalized);
        Assert.NotNull(first);

        // Exact same bytes again: a byte-for-byte replay → dropped, never a second event.
        await stream.WriteAsync(frame);
        Assert.Null(await ReadOneAsync(normalized, TimeSpan.FromMilliseconds(500)));

        await WaitUntilAsync(() => gw.Metrics.EventsPublished >= 1, TimeSpan.FromSeconds(2));
        Assert.Equal(1L, gw.Metrics.EventsPublished);

        // "Counted": the replay was recorded on the security-relevant instruments.
        Assert.True(Interlocked.Read(ref duplicates) >= 1, "the duplicate-packets counter should have recorded the replay");
        Assert.True(Interlocked.Read(ref replays) >= 1, "the replay-rejections counter should have recorded the replay");
    }

    // ── (b) Increment-2: an out-of-order frame is FLAGGED, not dropped ──────────

    [Fact]
    public async Task An_out_of_order_frame_is_flagged_in_quality_and_still_published()
    {
        await using GatewayHarness gw = await GatewayHarness.StartAsync();

        await using IEventSubscription<CanonicalTelemetryEvent> normalized =
            gw.Bus.Subscribe<CanonicalTelemetryEvent>(TelematicsTopics.TelemetryNormalized);

        using TcpClient client = await gw.ConnectAsync();
        NetworkStream stream = client.GetStream();

        await stream.WriteAsync(Fixture("login.hex"));
        await ReadExactlyAsync(stream, Fixture("login_ack.hex").Length);

        // Serial 200 at t+10s advances the high-water mark; serial 150 at t+0s is behind it.
        await stream.WriteAsync(BuildLocationFrame(serial: 200, fixTime: BaseFix.AddSeconds(10)));
        DeliveredEvent<CanonicalTelemetryEvent>? inOrder = await ReadOneAsync(normalized);
        Assert.NotNull(inOrder);
        Assert.False(inOrder!.Value.Envelope.Payload.Quality.IsOutOfOrder);

        await stream.WriteAsync(BuildLocationFrame(serial: 150, fixTime: BaseFix));
        DeliveredEvent<CanonicalTelemetryEvent>? late = await ReadOneAsync(normalized);

        // The point of (b): it is NOT dropped — it still reaches the backbone — but it is flagged.
        Assert.NotNull(late);
        Assert.True(late!.Value.Envelope.Payload.Quality.IsOutOfOrder,
            "an out-of-order fix must be flagged, not silently dropped");

        await WaitUntilAsync(() => gw.Metrics.EventsPublished >= 2, TimeSpan.FromSeconds(2));
        Assert.Equal(2L, gw.Metrics.EventsPublished);
    }

    // ── (c) Increment-2: a quarantined device is refused AND the socket closed ──

    [Fact]
    public async Task A_quarantined_device_login_is_rejected_and_the_socket_is_closed()
    {
        const string QuarantinedImei = "864000000000007";

        var registry = new InMemoryDeviceRegistry(new[]
        {
            new KeyValuePair<string, ResolvedDeviceOwner>(
                QuarantinedImei,
                new ResolvedDeviceOwner(
                    TenantId: Guid.Parse("9a9a9a9a-1111-2222-3333-444444444444"),
                    CompanyId: 200L,
                    DeviceId: "dev-quarantined-0001",
                    VehicleId: 6001L,
                    LifecycleState: DeviceLifecycleState.Quarantined,
                    CredentialHandle: "vault://opstrax/telematics/psk/dev-quarantined-0001")),
        });

        await using GatewayHarness gw = await GatewayHarness.StartAsync(registry: registry);

        await using IEventSubscription<TelemetryRejection> rejected =
            gw.Bus.Subscribe<TelemetryRejection>(TelematicsTopics.TelemetryRejected);
        await using IEventSubscription<CanonicalTelemetryEvent> normalized =
            gw.Bus.Subscribe<CanonicalTelemetryEvent>(TelematicsTopics.TelemetryNormalized);

        using TcpClient client = await gw.ConnectAsync();
        NetworkStream stream = client.GetStream();
        await stream.WriteAsync(BuildLoginFrame(QuarantinedImei));

        // The authenticator quarantine verdict publishes an UNBOUND rejection...
        DeliveredEvent<TelemetryRejection>? delivered = await ReadOneAsync(rejected);
        Assert.NotNull(delivered);
        Assert.Equal(Guid.Empty, delivered!.Value.Envelope.TenantId);
        Assert.Equal(0L, delivered.Value.Envelope.CompanyId);
        Assert.Equal(RejectionReasons.DeviceNotIngestable, delivered.Value.Envelope.Payload.Reason);
        Assert.DoesNotContain(QuarantinedImei, delivered.Value.Envelope.Payload.ClaimedIdentifierMasked, StringComparison.Ordinal);

        // ...no telemetry is admitted...
        Assert.Null(await ReadOneAsync(normalized, TimeSpan.FromMilliseconds(400)));
        Assert.Equal(0L, gw.Metrics.EventsPublished);
        Assert.Equal(1L, gw.Metrics.UnknownDeviceRejections);

        // ...and — the Increment-1 fix — the read loop does NOT keep running: the gateway closes
        // the socket, so a read on the client side observes the peer's FIN (0 bytes).
        Assert.True(await PeerClosedWithinAsync(stream, TimeSpan.FromSeconds(3)),
            "a refused login must close the connection, not leave the read loop draining it");
    }

    // ── (a2) Increment-2 fix: a device's REGISTRY policy (source-IP pin) is enforced ──
    // Proves the per-device trust policy flows from the registry THROUGH the gateway: a device
    // provisioned ImeiAllowlistOnly but with a source-IP pin that excludes loopback is refused
    // when it connects from loopback — even though its IMEI is on the allowlist. This is the
    // end-to-end guard for the "spoofed allowlisted IMEI" gap: registry pins are actually applied,
    // not a hardcoded global baseline.
    [Fact]
    public async Task A_device_pinned_to_a_foreign_source_ip_is_refused_from_loopback()
    {
        const string PinnedImei = "865000000000001";

        var registry = new InMemoryDeviceRegistry(new[]
        {
            new KeyValuePair<string, ResolvedDeviceTrust>(
                PinnedImei,
                new ResolvedDeviceTrust(
                    new ResolvedDeviceOwner(
                        TenantId: Guid.Parse("7b7b7b7b-1111-2222-3333-555555555555"),
                        CompanyId: 300L,
                        DeviceId: "dev-pinned-0001",
                        VehicleId: 7001L,
                        LifecycleState: DeviceLifecycleState.Online,
                        CredentialHandle: "vault://opstrax/telematics/psk/dev-pinned-0001"),
                    // Allowlisted, but pinned to a network the loopback test client is NOT on.
                    new DeviceTrustPolicy(
                        DeviceAuthMode.ImeiAllowlistOnly,
                        PinnedSourceCidrs: new[] { "10.0.0.0/8" }),
                    CredentialMaterial.None)),
        });

        await using GatewayHarness gw = await GatewayHarness.StartAsync(registry: registry);

        await using IEventSubscription<TelemetryRejection> rejected =
            gw.Bus.Subscribe<TelemetryRejection>(TelematicsTopics.TelemetryRejected);
        await using IEventSubscription<CanonicalTelemetryEvent> normalized =
            gw.Bus.Subscribe<CanonicalTelemetryEvent>(TelematicsTopics.TelemetryNormalized);

        using TcpClient client = await gw.ConnectAsync();
        NetworkStream stream = client.GetStream();
        await stream.WriteAsync(BuildLoginFrame(PinnedImei));

        // The registry-sourced source-IP pin refuses the loopback login: an UNBOUND rejection is
        // published, no telemetry is admitted, and the socket is closed.
        DeliveredEvent<TelemetryRejection>? delivered = await ReadOneAsync(rejected);
        Assert.NotNull(delivered);
        Assert.Equal(Guid.Empty, delivered!.Value.Envelope.TenantId);
        Assert.Equal(RejectionReasons.DeviceNotIngestable, delivered.Value.Envelope.Payload.Reason);

        Assert.Null(await ReadOneAsync(normalized, TimeSpan.FromMilliseconds(400)));
        Assert.Equal(0L, gw.Metrics.EventsPublished);

        Assert.True(await PeerClosedWithinAsync(stream, TimeSpan.FromSeconds(3)),
            "a login refused by the device's registry source-IP pin must close the connection");
    }

    // ── (d) Increment-2: per-device order survives a downstream outage ──────────

    [Fact]
    public async Task Per_device_order_is_preserved_through_a_downstream_outage_via_store_and_forward()
    {
        // The gateway publishes through an outage wrapper that is DOWN while the fixes arrive, so
        // every fix is parked in the store-and-forward buffer in decode order.
        ControllableBackbone? backbone = null;
        await using GatewayHarness gw = await GatewayHarness.StartAsync(
            publishBackboneFactory: inner => backbone = new ControllableBackbone(inner, up: false));

        using TcpClient client = await gw.ConnectAsync();
        NetworkStream stream = client.GetStream();
        await stream.WriteAsync(Fixture("login.hex"));
        await ReadExactlyAsync(stream, Fixture("login_ack.hex").Length);

        // Three in-order fixes for the one device, each a distinct serial + fix time.
        var fixTimes = new[] { BaseFix, BaseFix.AddSeconds(5), BaseFix.AddSeconds(10) };
        for (int i = 0; i < fixTimes.Length; i++)
            await stream.WriteAsync(BuildLocationFrame(serial: (ushort)(10 + i), fixTime: fixTimes[i]));

        // While down they land in the buffer, oldest-first, and nothing is published.
        await WaitUntilAsync(() => gw.ForwardBuffer.Count >= 3, TimeSpan.FromSeconds(3));
        Assert.Equal(3, gw.ForwardBuffer.Count);
        Assert.Equal(0L, gw.Metrics.EventsPublished);

        // Recover the backbone, subscribe, and drain the buffer through the real replay service.
        await using IEventSubscription<CanonicalTelemetryEvent> normalized =
            gw.Bus.Subscribe<CanonicalTelemetryEvent>(TelematicsTopics.TelemetryNormalized);
        backbone!.Up = true;

        var replay = new StoreAndForwardReplayService(
            gw.ForwardBuffer,
            backbone,
            new StoreAndForwardReplayOptions(),
            NullLogger<StoreAndForwardReplayService>.Instance,
            (_, _) => Task.CompletedTask); // deterministic: no real backoff delay
        await replay.DrainAsync(CancellationToken.None);

        // The replayed fixes arrive in the SAME per-device order they were produced in — a later
        // fix never overtakes an earlier one for the device.
        var received = new List<DateTime>();
        for (int i = 0; i < 3; i++)
        {
            DeliveredEvent<CanonicalTelemetryEvent>? d = await ReadOneAsync(normalized);
            Assert.NotNull(d);
            received.Add(d!.Value.Envelope.Payload.OccurredAtDeviceUtc);
        }

        Assert.Equal(fixTimes.Select(t => new DateTime(t.Ticks, DateTimeKind.Utc)), received);
        Assert.Equal(0, gw.ForwardBuffer.Count);
    }

    // ── (e) Increment-2: a successful fix emits an OTel span with the right context ──

    [Fact]
    public async Task A_successful_fix_emits_an_otel_span_with_correlation_and_device_attributes()
    {
        var spans = new List<Activity>();
        var gate = new object();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == TelematicsInstrumentation.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a => { lock (gate) spans.Add(a); },
        };
        ActivitySource.AddActivityListener(listener);

        await using GatewayHarness gw = await GatewayHarness.StartAsync();

        await using IEventSubscription<CanonicalTelemetryEvent> normalized =
            gw.Bus.Subscribe<CanonicalTelemetryEvent>(TelematicsTopics.TelemetryNormalized);

        using TcpClient client = await gw.ConnectAsync();
        NetworkStream stream = client.GetStream();

        await stream.WriteAsync(Fixture("login.hex"));
        await ReadExactlyAsync(stream, Fixture("login_ack.hex").Length);
        await stream.WriteAsync(Fixture("location_0x12.hex"));

        DeliveredEvent<CanonicalTelemetryEvent>? delivered = await ReadOneAsync(normalized);
        Assert.NotNull(delivered);
        Guid correlationId = delivered!.Value.Envelope.CorrelationId;

        // The per-packet parent span is emitted, carrying the correlation id and the
        // REGISTRY-resolved ownership (never the packet's IMEI).
        Activity? root = null;
        await WaitUntilAsync(() =>
        {
            lock (gate)
                root = spans.LastOrDefault(a =>
                    a.OperationName == "packet-receive" &&
                    (string?)a.GetTagItem("telematics.correlation_id") == correlationId.ToString());
            return root is not null;
        }, TimeSpan.FromSeconds(3));

        Assert.NotNull(root);
        Assert.Equal(correlationId.ToString(), root!.GetTagItem("telematics.correlation_id"));
        Assert.Equal(InMemoryDeviceRegistry.KnownDeviceId, root.GetTagItem("device.id"));
        Assert.Equal(InMemoryDeviceRegistry.KnownTenantId.ToString(), root.GetTagItem("tenant.id"));
        Assert.Equal(InMemoryDeviceRegistry.KnownCompanyId, root.GetTagItem("company.id"));
        Assert.Equal("gt06", root.GetTagItem("telematics.protocol"));
        Assert.Equal("GT06", root.GetTagItem("adapter.name"));
    }

    // ── Harness ────────────────────────────────────────────────────────────────

    /// <summary>Boots a real gateway on an ephemeral loopback port with in-memory collaborators.</summary>
    private sealed class GatewayHarness : IAsyncDisposable
    {
        private GatewayHarness(
            TcpGatewayService service,
            InMemoryEventBackbone bus,
            IEventBackbone publishBackbone,
            GatewayMetrics metrics,
            InMemoryStoreAndForwardBuffer forwardBuffer,
            InMemoryPositionProjectionStore projectionStore)
        {
            Service = service;
            Bus = bus;
            PublishBackbone = publishBackbone;
            Metrics = metrics;
            ForwardBuffer = forwardBuffer;
            ProjectionStore = projectionStore;
        }

        public TcpGatewayService Service { get; }

        /// <summary>The in-memory bus tests SUBSCRIBE on. Note: the gateway may PUBLISH through a wrapper (see <see cref="PublishBackbone"/>).</summary>
        public InMemoryEventBackbone Bus { get; }

        /// <summary>The backbone the gateway publishes through (defaults to <see cref="Bus"/>; a test may inject an outage wrapper).</summary>
        public IEventBackbone PublishBackbone { get; }

        public GatewayMetrics Metrics { get; }

        public InMemoryStoreAndForwardBuffer ForwardBuffer { get; }

        public InMemoryPositionProjectionStore ProjectionStore { get; }

        public int Port => Service.BoundPort;

        public static async Task<GatewayHarness> StartAsync(
            GatewayOptions? options = null,
            IDeviceRegistry? registry = null,
            Func<InMemoryEventBackbone, IEventBackbone>? publishBackboneFactory = null)
        {
            options ??= new GatewayOptions
            {
                ListenPort = 0, // ephemeral: never collides with a running dev gateway
                MaxConnections = 64,
                MaxInFlightPerConnection = 16,
                MaxFrameBytes = 2048,
                IdleTimeout = TimeSpan.FromSeconds(30),
                DrainTimeout = TimeSpan.FromSeconds(5),
            };

            var bus = new InMemoryEventBackbone();
            IEventBackbone publishBackbone = publishBackboneFactory?.Invoke(bus) ?? bus;
            var metrics = new GatewayMetrics();
            var forwardBuffer = new InMemoryStoreAndForwardBuffer();
            var projectionStore = new InMemoryPositionProjectionStore();

            // Same wiring the composition root uses: the honest GT06 authenticator with a fail-closed
            // key resolver, and the wrap-aware replay guard (GT06's 16-bit serial modulus).
            var authenticator = new DefaultDeviceAuthenticator(new DenyAllKeyResolver());
            var replayGuard = new InMemoryReplayGuard(serialModulus: 65536);

            var service = new TcpGatewayService(
                options,
                publishBackbone,
                registry ?? InMemoryDeviceRegistry.SeededDefault(),
                authenticator,
                replayGuard,
                projectionStore,
                new Gt06Adapter(options.MaxFrameBytes),
                forwardBuffer,
                metrics,
                NullLoggerFactory.Instance);

            await service.StartAsync(CancellationToken.None);

            return new GatewayHarness(service, bus, publishBackbone, metrics, forwardBuffer, projectionStore);
        }

        public async Task<TcpClient> ConnectAsync()
        {
            var client = new TcpClient { NoDelay = true };
            await client.ConnectAsync(IPAddress.Loopback, Port);
            return client;
        }

        public async ValueTask DisposeAsync()
        {
            await Service.StopAsync(CancellationToken.None);
            Service.Dispose();
        }
    }

    // ── Socket / bus helpers ───────────────────────────────────────────────────

    private static async Task<byte[]> ReadExactlyAsync(NetworkStream stream, int count)
    {
        using var cts = new CancellationTokenSource(SocketTimeout);
        var buffer = new byte[count];
        int offset = 0;

        while (offset < count)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset), cts.Token);
            if (read == 0)
                throw new EndOfStreamException($"Peer closed after {offset} of {count} expected bytes.");

            offset += read;
        }

        return buffer;
    }

    private static async Task<DeliveredEvent<T>?> ReadOneAsync<T>(
        IEventSubscription<T> subscription,
        TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? BusTimeout);

        try
        {
            await foreach (DeliveredEvent<T> delivered in subscription.ReadAllAsync(cts.Token))
                return delivered;
        }
        catch (OperationCanceledException)
        {
            // Nothing arrived inside the window — for some tests that is the expected result.
        }

        return null;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;

            await Task.Delay(25);
        }
    }

    // ── Frame builders ─────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a real, CRC-valid GT06 login frame for an arbitrary IMEI, using the decoder's own
    /// <see cref="Gt06Adapter.Crc16Itu"/>. This is how the unknown-device test presents a login
    /// that is beyond reproach on the wire and still must be refused: the frame is perfect, the
    /// identity simply is not ours.
    /// </summary>
    private static byte[] BuildLoginFrame(string imei, ushort serial = 1)
    {
        string padded = imei.PadLeft(16, '0');

        // The CRC region is [length .. serial] inclusive — everything between start and CRC.
        var crcRegion = new List<byte> { 0x0D, 0x01 }; // packet length (13), protocol 0x01 (login)

        for (int i = 0; i < 8; i++)
        {
            int high = padded[i * 2] - '0';
            int low = padded[(i * 2) + 1] - '0';
            crcRegion.Add((byte)((high << 4) | low)); // packed BCD terminal id
        }

        crcRegion.Add((byte)(serial >> 8));
        crcRegion.Add((byte)(serial & 0xFF));

        ushort crc = Gt06Adapter.Crc16Itu(crcRegion.ToArray());

        var frame = new List<byte> { 0x78, 0x78 };
        frame.AddRange(crcRegion);
        frame.Add((byte)(crc >> 8));
        frame.Add((byte)(crc & 0xFF));
        frame.Add(0x0D);
        frame.Add(0x0A);

        return frame.ToArray();
    }

    /// <summary>Base device fix time for synthesized location frames (GT06 resolves to whole seconds).</summary>
    private static readonly DateTime BaseFix = new(2024, 1, 15, 10, 20, 30, DateTimeKind.Utc);

    /// <summary>
    /// Builds a real, CRC-valid GT06 0x12 location frame carrying a fix at <paramref name="fixTime"/>
    /// with the given information serial. Distinct serials and fix times yield distinct content, so
    /// the replay guard treats them as separate frames (only a byte-identical resend is a replay).
    /// </summary>
    private static byte[] BuildLocationFrame(ushort serial, DateTime fixTime, double lat = 10.0, double lng = 20.0, int speed = 60)
    {
        // Info block (18 bytes): date(6) quantity(1) lat(4) lng(4) speed(1) course/status(2).
        var info = new List<byte>
        {
            (byte)(fixTime.Year - 2000),
            (byte)fixTime.Month,
            (byte)fixTime.Day,
            (byte)fixTime.Hour,
            (byte)fixTime.Minute,
            (byte)fixTime.Second,
            0x09, // quantity: low nibble = satellites in use (9)
        };

        AppendBigEndian(info, (uint)Math.Round(Math.Abs(lat) * 1800000.0));
        AppendBigEndian(info, (uint)Math.Round(Math.Abs(lng) * 1800000.0));
        info.Add((byte)speed);

        // Course/status word: bit12 = positioned, bit11 = north (=> +lat), bit10 clear (=> east, +lng).
        ushort courseStatus = (1 << 12) | (1 << 11);
        info.Add((byte)(courseStatus >> 8));
        info.Add((byte)(courseStatus & 0xFF));

        // PacketLength = protocol(1) + info(N) + serial(2) + crc(2); CRC region = [length .. serial].
        int packetLength = 1 + info.Count + 2 + 2;
        var crcRegion = new List<byte> { (byte)packetLength, 0x12 };
        crcRegion.AddRange(info);
        crcRegion.Add((byte)(serial >> 8));
        crcRegion.Add((byte)(serial & 0xFF));

        ushort crc = Gt06Adapter.Crc16Itu(crcRegion.ToArray());

        var frame = new List<byte> { 0x78, 0x78 };
        frame.AddRange(crcRegion);
        frame.Add((byte)(crc >> 8));
        frame.Add((byte)(crc & 0xFF));
        frame.Add(0x0D);
        frame.Add(0x0A);
        return frame.ToArray();
    }

    private static void AppendBigEndian(List<byte> dst, uint value)
    {
        dst.Add((byte)((value >> 24) & 0xFF));
        dst.Add((byte)((value >> 16) & 0xFF));
        dst.Add((byte)((value >> 8) & 0xFF));
        dst.Add((byte)(value & 0xFF));
    }

    /// <summary>Returns true once the peer (the gateway) closes the connection — a 0-length read or reset.</summary>
    private static async Task<bool> PeerClosedWithinAsync(NetworkStream stream, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        var buffer = new byte[16];
        try
        {
            while (true)
            {
                int read = await stream.ReadAsync(buffer, cts.Token);
                if (read == 0)
                    return true; // clean FIN: the gateway closed the socket.
            }
        }
        catch (OperationCanceledException)
        {
            return false; // still open after the timeout — the read loop kept draining it.
        }
        catch (IOException)
        {
            return true; // reset: the peer is gone.
        }
    }

    /// <summary>A <see cref="MeterListener"/> over the gateway's replay/duplicate instruments.</summary>
    private static MeterListener ReplayMeterListener(Action<long> onDuplicate, Action<long> onReplay)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == TelematicsInstrumentation.MeterName)
                    l.EnableMeasurementEvents(instrument);
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, _, _) =>
        {
            switch (instrument.Name)
            {
                case "opstrax_telematics_duplicate_packets":
                    onDuplicate(measurement);
                    break;
                case "opstrax_telematics_replay_rejections":
                    onReplay(measurement);
                    break;
            }
        });
        listener.Start();
        return listener;
    }

    /// <summary>
    /// An <see cref="IEventBackbone"/> whose publish path can be toggled off to simulate a
    /// downstream outage: while <see cref="Up"/> is false every publish throws, forcing the
    /// gateway's store-and-forward path. Subscriptions always pass through to the inner bus.
    /// </summary>
    private sealed class ControllableBackbone : IEventBackbone
    {
        private readonly IEventBackbone _inner;

        public ControllableBackbone(IEventBackbone inner, bool up = true)
        {
            _inner = inner;
            Up = up;
        }

        /// <summary>When false, every <see cref="PublishAsync{T}"/> throws (the broker is "down").</summary>
        public volatile bool Up;

        public Task PublishAsync<T>(string topic, string key, EventEnvelope<T> envelope, CancellationToken cancellationToken = default)
        {
            if (!Up)
                throw new InvalidOperationException("backbone is down");
            return _inner.PublishAsync(topic, key, envelope, cancellationToken);
        }

        public IEventSubscription<T> Subscribe<T>(string topic, Guid? tenantFilter = null) =>
            _inner.Subscribe<T>(topic, tenantFilter);
    }

    /// <summary>Fail-closed credential resolver for the test harness: no vault, so no key ever resolves.</summary>
    private sealed class DenyAllKeyResolver : ICredentialKeyResolver
    {
        public ValueTask<byte[]?> ResolveHmacKeyAsync(CredentialMaterial credential, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<byte[]?>(null);
    }

    /// <summary>Bytes that contain no GT06 start marker at the head, so the stream is unidentifiable.</summary>
    private static byte[] Garbage(int length, int seed)
    {
        var random = new Random(seed);
        var bytes = new byte[length];
        random.NextBytes(bytes);

        // Guarantee the opening bytes cannot be mistaken for a GT06 start marker.
        bytes[0] = 0x00;
        bytes[1] = 0x00;

        return bytes;
    }

    /// <summary>Takes a valid frame and destroys its stop bits, making the framing unrecoverable.</summary>
    private static byte[] WithBrokenStopBits(byte[] frame)
    {
        var corrupted = (byte[])frame.Clone();
        corrupted[^1] = 0xFF; // was 0x0A
        return corrupted;
    }

    // ── Fixture loading (same fixtures the protocol tests use) ─────────────────

    private static readonly string FixtureDir = LocateFixtureDir();

    private static string LocateFixtureDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "telematics", "fixtures", "gt06");
            if (File.Exists(Path.Combine(candidate, "login.hex")))
                return candidate;

            string candidate2 = Path.Combine(dir.FullName, "fixtures", "gt06");
            if (File.Exists(Path.Combine(candidate2, "login.hex")))
                return candidate2;

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate telematics/fixtures/gt06 from " + AppContext.BaseDirectory);
    }

    private static byte[] Fixture(string name) =>
        FromHex(File.ReadAllText(Path.Combine(FixtureDir, name)));

    private static byte[] FromHex(string hex)
    {
        string clean = new(hex.Where(Uri.IsHexDigit).ToArray());
        Assert.True(clean.Length % 2 == 0, "hex fixture has odd length");

        var bytes = new byte[clean.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = byte.Parse(clean.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        return bytes;
    }
}
