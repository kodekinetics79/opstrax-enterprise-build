using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Opstrax.Telematics.Contracts;
using Opstrax.Telematics.Contracts.Eventing;
using Opstrax.Telematics.Contracts.Identity;
using Opstrax.Telematics.Contracts.Signals;
using Opstrax.Telematics.Contracts.Lifecycle;
using Opstrax.Telematics.Gateway;
using Opstrax.Telematics.Gateway.Buffering;
using Opstrax.Telematics.Gateway.Eventing;
using Opstrax.Telematics.Gateway.Identity;
using Opstrax.Telematics.Gateway.Projection;
using Opstrax.Telematics.Gateway.Security;
using Opstrax.Telematics.Gateway.Security.Auth;
using Opstrax.Telematics.Gateway.Security.Replay;
using Opstrax.Telematics.Protocols.Gt06;

namespace Opstrax.Telematics.IntegrationTests;

/// <summary>
/// Session-identity tests for the canonical device edge, driven over real loopback sockets.
/// </summary>
/// <remarks>
/// <para>
/// Two invariants are under test, and both were violated at the audited baseline:
/// </para>
/// <list type="bullet">
///   <item><description><b>One socket → one immutable device.</b> GT06 announces its IMEI once, at
///     login; every later frame on that socket is anonymous. So if a second login could re-point a
///     bound session, everything the socket sent afterwards would be published under the second
///     device — and since the registry resolves that device to its own tenant, one operator's
///     vehicle movements would appear on another operator's map.</description></item>
///   <item><description><b>One device → one authoritative socket.</b> A tracker whose cell bearer
///     drops without a FIN reconnects while the gateway still holds the dead socket. Both are then
///     bound to the same device until an idle timeout expires.</description></item>
/// </list>
/// </remarks>
public class GatewaySessionIdentityTests
{
    private static readonly TimeSpan SocketTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan BusTimeout = TimeSpan.FromSeconds(5);

    // Two distinct devices, resolving to two DIFFERENT tenants — the cross-tenant scenario.
    private const string ImeiA = "868120303337976";
    private const string ImeiB = "868120303337977";
    private const string DeviceA = "dev-known-0001";
    private const string DeviceB = "dev-known-0002";
    private static readonly Guid TenantA = Guid.Parse("2f1c9a54-8a0e-4a7d-9f3b-6d2c1e5b7a90");
    private static readonly Guid TenantB = Guid.Parse("9e3d7b21-4c15-4e88-9a02-1b7f3c6d5e44");

    // ── P0-01: one socket → one immutable device ───────────────────────────────

    /// <summary>
    /// The exact audited scenario. Login A, a valid fix for A, then a login for B on the SAME
    /// socket, then a further anonymous fix. B must receive no login acknowledgement, the binding
    /// must stay on A, and nothing may ever be published under B.
    /// </summary>
    [Fact]
    public async Task A_second_login_for_a_different_device_cannot_re_point_a_bound_session()
    {
        await using GatewayHarness gw = await GatewayHarness.StartAsync();
        IEventSubscription<CanonicalTelemetryEvent> delivered =
            gw.Bus.Subscribe<CanonicalTelemetryEvent>(TelematicsTopics.TelemetryNormalized);

        using TcpClient client = await gw.ConnectAsync();
        NetworkStream stream = client.GetStream();

        // Login A, acknowledged.
        byte[] loginA = BuildLoginFrame(ImeiA, serial: 1);
        await stream.WriteAsync(loginA);
        Assert.Equal(AckForFrame(loginA), await ReadExactlyAsync(stream, 10));

        // A valid fix for A.
        await stream.WriteAsync(BuildLocationFrame(2, BaseFix, lat: 35.0, lng: 139.0));
        CanonicalTelemetryEvent firstFix = await NextEventAsync(delivered);
        Assert.Equal(DeviceA, firstFix.DeviceId);
        Assert.Equal(TenantA, firstFix.TenantId);

        // Login B on the SAME socket. It must NOT be acknowledged.
        await stream.WriteAsync(BuildLoginFrame(ImeiB, serial: 3));

        // A further ANONYMOUS fix. This is the frame that would be misattributed.
        await stream.WriteAsync(BuildLocationFrame(4, BaseFix.AddSeconds(30), lat: 36.0, lng: 140.0));

        // The gateway closes the connection rather than serving a session that tried to re-identify.
        Assert.True(await PeerClosedWithinAsync(stream, SocketTimeout),
            "the gateway must close a connection whose login tried to change its device identity");

        // Whatever else was published — if anything — is still device A under tenant A. The
        // anonymous frame that followed the refused login must never carry B's identity.
        DeliveredEvent<CanonicalTelemetryEvent>? trailing =
            await ReadOneAsync(delivered, TimeSpan.FromMilliseconds(500));
        if (trailing is { } extra)
        {
            Assert.Equal(DeviceA, extra.Envelope.Payload.DeviceId);
            Assert.Equal(TenantA, extra.Envelope.Payload.TenantId);
            Assert.NotEqual(DeviceB, extra.Envelope.Payload.DeviceId);
            Assert.NotEqual(TenantB, extra.Envelope.Payload.TenantId);
        }

        Assert.Equal(1, gw.Metrics.SessionIdentityViolations);
    }

    /// <summary>
    /// The refused second login produces no bytes at all on the wire. A device that received a
    /// login acknowledgement would believe it was registered and start streaming.
    /// </summary>
    [Fact]
    public async Task A_refused_re_identification_receives_no_login_acknowledgement()
    {
        await using GatewayHarness gw = await GatewayHarness.StartAsync();

        using TcpClient client = await gw.ConnectAsync();
        NetworkStream stream = client.GetStream();

        byte[] loginA = BuildLoginFrame(ImeiA, serial: 1);
        await stream.WriteAsync(loginA);
        Assert.Equal(AckForFrame(loginA), await ReadExactlyAsync(stream, 10));

        byte[] loginB = BuildLoginFrame(ImeiB, serial: 2);
        await stream.WriteAsync(loginB);

        // Everything the gateway writes from here until it hangs up. The ack for B would be a
        // 10-byte 0x7878 frame echoing protocol 0x01 and serial 2.
        byte[] remainder = await ReadUntilClosedAsync(stream);
        Assert.DoesNotContain(Convert.ToHexString(AckForFrame(loginB)), Convert.ToHexString(remainder));
        Assert.Empty(remainder);
    }

    /// <summary>
    /// A repeated login for the SAME device is idempotent: it is re-acknowledged (the device may
    /// simply not have seen the first answer) and the binding is untouched. Refusing it would wedge
    /// a device in a login retry loop for a defect it does not have.
    /// </summary>
    [Fact]
    public async Task A_repeated_login_for_the_same_device_is_idempotent_and_keeps_the_binding()
    {
        await using GatewayHarness gw = await GatewayHarness.StartAsync();
        IEventSubscription<CanonicalTelemetryEvent> delivered =
            gw.Bus.Subscribe<CanonicalTelemetryEvent>(TelematicsTopics.TelemetryNormalized);

        using TcpClient client = await gw.ConnectAsync();
        NetworkStream stream = client.GetStream();

        byte[] first = BuildLoginFrame(ImeiA, serial: 1);
        await stream.WriteAsync(first);
        Assert.Equal(AckForFrame(first), await ReadExactlyAsync(stream, 10));

        byte[] again = BuildLoginFrame(ImeiA, serial: 2);
        await stream.WriteAsync(again);
        Assert.Equal(AckForFrame(again), await ReadExactlyAsync(stream, 10));

        // Still bound, still to A, and still able to publish.
        await stream.WriteAsync(BuildLocationFrame(3, BaseFix, lat: 35.0, lng: 139.0));
        CanonicalTelemetryEvent fix = await NextEventAsync(delivered);
        Assert.Equal(DeviceA, fix.DeviceId);
        Assert.Equal(TenantA, fix.TenantId);

        Assert.Equal(0, gw.Metrics.SessionIdentityViolations);
    }

    /// <summary>
    /// A second login carrying no resolvable identity at all (a malformed packed-BCD terminal id)
    /// is treated as a re-identification attempt, not as a harmless no-op. "Unknown" is not the
    /// same device as A, so the safe answer is to refuse.
    /// </summary>
    [Fact]
    public async Task A_second_login_with_no_identity_cannot_disturb_a_bound_session()
    {
        await using GatewayHarness gw = await GatewayHarness.StartAsync();

        using TcpClient client = await gw.ConnectAsync();
        NetworkStream stream = client.GetStream();

        byte[] loginA = BuildLoginFrame(ImeiA, serial: 1);
        await stream.WriteAsync(loginA);
        Assert.Equal(AckForFrame(loginA), await ReadExactlyAsync(stream, 10));

        // Terminal id whose nibbles are not decimal digits: no resolvable claim.
        await stream.WriteAsync(BuildRawLoginFrame(
            new byte[] { 0xAB, 0xCD, 0xEF, 0xAB, 0xCD, 0xEF, 0xAB, 0xCD }, serial: 2));

        Assert.True(await PeerClosedWithinAsync(stream, SocketTimeout));
        Assert.Equal(1, gw.Metrics.SessionIdentityViolations);
    }

    // ── P0-02: one device → one authoritative socket ───────────────────────────

    /// <summary>
    /// Two sockets authenticate as the same device. The newest admitted login wins, the older
    /// socket is closed, and only one session remains authoritative.
    /// </summary>
    [Fact]
    public async Task A_second_connection_for_the_same_device_displaces_the_first()
    {
        var sessions = new ActiveDeviceSessionRegistry();
        await using GatewayHarness gw = await GatewayHarness.StartAsync(sessions: sessions);

        using TcpClient first = await gw.ConnectAsync();
        NetworkStream firstStream = first.GetStream();
        byte[] login1 = BuildLoginFrame(ImeiA, serial: 1);
        await firstStream.WriteAsync(login1);
        Assert.Equal(AckForFrame(login1), await ReadExactlyAsync(firstStream, 10));

        using TcpClient second = await gw.ConnectAsync();
        NetworkStream secondStream = second.GetStream();
        byte[] login2 = BuildLoginFrame(ImeiA, serial: 1);
        await secondStream.WriteAsync(login2);
        Assert.Equal(AckForFrame(login2), await ReadExactlyAsync(secondStream, 10));

        // The first socket is torn down; the second survives.
        Assert.True(await PeerClosedWithinAsync(firstStream, SocketTimeout),
            "the displaced connection must be closed, not left holding the device");
        Assert.Equal(1, gw.Metrics.DuplicateSessionsDisplaced);

        // Exactly one authoritative session for the device.
        await WaitUntilAsync(() => sessions.ActiveSessionCount == 1, SocketTimeout);
        Assert.Equal(1, sessions.ActiveSessionCount);
    }

    /// <summary>
    /// The ABA race the lease semantics exist for: connection A is displaced by B, and A's cleanup
    /// then runs. A must NOT remove B's registration. A naive dictionary remove keyed on the device
    /// would do exactly that, silently leaving the fleet with no authoritative session — and the
    /// next connection would then displace nobody, so two sockets would once again both be live.
    /// </summary>
    [Fact]
    public void A_displaced_sessions_cleanup_cannot_evict_the_session_that_replaced_it()
    {
        var registry = new ActiveDeviceSessionRegistry();
        using var firstCts = new CancellationTokenSource();
        using var secondCts = new CancellationTokenSource();

        SessionAcquisition first = registry.Acquire(DeviceA, firstCts);
        Assert.False(first.DisplacedAnother);

        SessionAcquisition second = registry.Acquire(DeviceA, secondCts);
        Assert.True(second.DisplacedAnother);
        Assert.Same(firstCts, second.DisplacedSession);
        Assert.NotEqual(first.LeaseId, second.LeaseId);

        // The displaced session's LATE cleanup. It must change nothing.
        Assert.False(registry.Release(DeviceA, first.LeaseId));
        Assert.True(registry.IsCurrent(DeviceA, second.LeaseId));
        Assert.Equal(1, registry.ActiveSessionCount);

        // Only the current lease holder can release it.
        Assert.True(registry.Release(DeviceA, second.LeaseId));
        Assert.Equal(0, registry.ActiveSessionCount);
    }

    /// <summary>
    /// Many sockets race to claim one device simultaneously. Exactly one lease may survive as
    /// current, and every displaced session must have been handed a cancellation source to close —
    /// no acquisition may silently drop a live socket on the floor.
    /// </summary>
    [Fact]
    public void Simultaneous_duplicate_logins_converge_on_exactly_one_authoritative_session()
    {
        var registry = new ActiveDeviceSessionRegistry();
        const int racers = 64;
        var sources = new CancellationTokenSource[racers];
        var acquisitions = new SessionAcquisition[racers];

        // DEDICATED threads, not the thread pool. A Barrier makes every racer block until the last
        // one arrives, and blocking 64 pool threads starves the pool for the whole process — which
        // shows up as unrelated tests timing out rather than as a failure here.
        using var barrier = new Barrier(racers);
        var threads = new Thread[racers];
        for (int i = 0; i < racers; i++)
        {
            int index = i;
            threads[index] = new Thread(() =>
            {
                sources[index] = new CancellationTokenSource();
                barrier.SignalAndWait();
                acquisitions[index] = registry.Acquire(DeviceA, sources[index]);
            })
            { IsBackground = true };
            threads[index].Start();
        }

        foreach (Thread thread in threads)
            Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "a racer thread did not finish");

        // Exactly one device entry, and exactly one racer still holds the current lease.
        Assert.Equal(1, registry.ActiveSessionCount);
        Assert.Single(acquisitions.Where(a => registry.IsCurrent(DeviceA, a.LeaseId)));

        // Lease ids are unique, so no two sessions can believe they are the same holder.
        Assert.Equal(racers, acquisitions.Select(a => a.LeaseId).Distinct().Count());

        // Every racer but the first displaced somebody: no live socket is forgotten.
        Assert.Equal(racers - 1, acquisitions.Count(a => a.DisplacedAnother));

        // Every displaced session was one of the racers' own sources — never a foreign one.
        foreach (SessionAcquisition acquisition in acquisitions.Where(a => a.DisplacedAnother))
            Assert.Contains(acquisition.DisplacedSession, sources);

        // Releases from the losers change nothing.
        foreach (SessionAcquisition acquisition in acquisitions)
        {
            if (!registry.IsCurrent(DeviceA, acquisition.LeaseId))
                Assert.False(registry.Release(DeviceA, acquisition.LeaseId));
        }

        Assert.Equal(1, registry.ActiveSessionCount);
        foreach (CancellationTokenSource source in sources) source.Dispose();
    }

    /// <summary>Different devices never contend: each holds its own independent lease.</summary>
    [Fact]
    public void Distinct_devices_hold_independent_sessions()
    {
        var registry = new ActiveDeviceSessionRegistry();
        using var a = new CancellationTokenSource();
        using var b = new CancellationTokenSource();

        SessionAcquisition first = registry.Acquire(DeviceA, a);
        SessionAcquisition second = registry.Acquire(DeviceB, b);

        Assert.False(second.DisplacedAnother);
        Assert.Equal(2, registry.ActiveSessionCount);
        Assert.True(registry.IsCurrent(DeviceA, first.LeaseId));
        Assert.True(registry.IsCurrent(DeviceB, second.LeaseId));
    }

    // ── P1-09 / P1-10: CRC and packet observability ────────────────────────────

    /// <summary>
    /// The audited CRC scenario: a good frame, a CRC-corrupt frame, another good frame. Two frames
    /// are decoded, one CRC failure is counted, and the connection survives — a corrupt frame must
    /// be observable as corruption rather than as silence.
    /// </summary>
    [Fact]
    public async Task A_bad_crc_frame_between_good_ones_is_counted_and_the_connection_survives()
    {
        await using GatewayHarness gw = await GatewayHarness.StartAsync();
        IEventSubscription<CanonicalTelemetryEvent> delivered =
            gw.Bus.Subscribe<CanonicalTelemetryEvent>(TelematicsTopics.TelemetryNormalized);

        using TcpClient client = await gw.ConnectAsync();
        NetworkStream stream = client.GetStream();

        byte[] login = BuildLoginFrame(ImeiA, serial: 1);
        await stream.WriteAsync(login);
        Assert.Equal(AckForFrame(login), await ReadExactlyAsync(stream, 10));

        byte[] good1 = BuildLocationFrame(2, BaseFix, lat: 35.0, lng: 139.0);
        byte[] corrupt = BuildLocationFrame(3, BaseFix.AddSeconds(10), lat: 35.1, lng: 139.1);
        corrupt[^3] ^= 0xFF; // flip the low CRC byte; framing and stop bits stay intact
        byte[] good2 = BuildLocationFrame(4, BaseFix.AddSeconds(20), lat: 35.2, lng: 139.2);

        await stream.WriteAsync(good1.Concat(corrupt).Concat(good2).ToArray());

        CanonicalTelemetryEvent first = await NextEventAsync(delivered);
        CanonicalTelemetryEvent second = await NextEventAsync(delivered);
        Assert.All(new[] { first, second }, e => Assert.Equal(DeviceA, e.DeviceId));

        await WaitUntilAsync(() => gw.Metrics.CrcFailures == 1, SocketTimeout);

        // The corrupt frame produced NO third event, and the connection is still open.
        Assert.Null(await ReadOneAsync(delivered, TimeSpan.FromMilliseconds(500)));
        Assert.False(await PeerClosedWithinAsync(stream, TimeSpan.FromMilliseconds(300)),
            "one corrupt frame must not drop a connection whose framing is still trustworthy");

        Assert.Equal(1, gw.Metrics.CrcFailures);
        // FramesReceived partitions exactly: login + 3 location frames, of which one failed CRC.
        Assert.Equal(4, gw.Metrics.FramesReceived);
        Assert.Equal(3, gw.Metrics.FramesDecoded);
        Assert.Equal(gw.Metrics.FramesDecoded + gw.Metrics.CrcFailures, gw.Metrics.FramesReceived);
    }

    /// <summary>Per-type counters and the ACK counter track what actually crossed the wire.</summary>
    [Fact]
    public async Task Packet_counters_partition_the_traffic_by_type()
    {
        await using GatewayHarness gw = await GatewayHarness.StartAsync();
        IEventSubscription<CanonicalTelemetryEvent> delivered =
            gw.Bus.Subscribe<CanonicalTelemetryEvent>(TelematicsTopics.TelemetryNormalized);

        using TcpClient client = await gw.ConnectAsync();
        NetworkStream stream = client.GetStream();

        byte[] login = BuildLoginFrame(ImeiA, serial: 1);
        await stream.WriteAsync(login);
        Assert.Equal(AckForFrame(login), await ReadExactlyAsync(stream, 10));

        await stream.WriteAsync(BuildLocationFrame(2, BaseFix, lat: 35.0, lng: 139.0));
        await NextEventAsync(delivered);
        await stream.WriteAsync(BuildLocationFrame(3, BaseFix.AddSeconds(10), lat: 35.1, lng: 139.1));
        await NextEventAsync(delivered);

        await WaitUntilAsync(() => gw.Metrics.LocationPackets == 2, SocketTimeout);

        Assert.Equal(1, gw.Metrics.LoginPackets);
        Assert.Equal(2, gw.Metrics.LocationPackets);
        Assert.Equal(0, gw.Metrics.HeartbeatPackets);
        Assert.Equal(0, gw.Metrics.AlarmPackets);
        Assert.Equal(0, gw.Metrics.UnknownPackets);
        Assert.Equal(0, gw.Metrics.CrcFailures);

        // Only the login required an answer; GT06 location frames do not.
        Assert.Equal(1, gw.Metrics.AcksSent);
    }


    // ── Adversarial: attacks on the invariants above ───────────────────────────

    /// <summary>
    /// The batching attack. Login A, login B and an anonymous fix arrive in a SINGLE TCP write, so
    /// all three are decoded from one buffer before any of them is handled. If the handler loop
    /// kept draining the batch after refusing the re-identification, the trailing fix would still
    /// be processed — by a session the gateway has already decided to tear down.
    /// </summary>
    [Fact]
    public async Task A_re_identification_buried_in_one_write_cannot_smuggle_a_trailing_frame()
    {
        await using GatewayHarness gw = await GatewayHarness.StartAsync();
        IEventSubscription<CanonicalTelemetryEvent> delivered =
            gw.Bus.Subscribe<CanonicalTelemetryEvent>(TelematicsTopics.TelemetryNormalized);

        using TcpClient client = await gw.ConnectAsync();
        NetworkStream stream = client.GetStream();

        byte[] loginA = BuildLoginFrame(ImeiA, serial: 1);
        await stream.WriteAsync(loginA);
        Assert.Equal(AckForFrame(loginA), await ReadExactlyAsync(stream, 10));

        // One write, three frames.
        byte[] batch = BuildLoginFrame(ImeiB, serial: 2)
            .Concat(BuildLocationFrame(3, BaseFix, lat: 36.0, lng: 140.0))
            .Concat(BuildLocationFrame(4, BaseFix.AddSeconds(10), lat: 36.1, lng: 140.1))
            .ToArray();
        await stream.WriteAsync(batch);

        Assert.True(await PeerClosedWithinAsync(stream, SocketTimeout));

        // Whatever was published is device A. Nothing in the batch may carry B.
        while (await ReadOneAsync(delivered, TimeSpan.FromMilliseconds(300)) is { } published)
        {
            Assert.Equal(DeviceA, published.Envelope.Payload.DeviceId);
            Assert.NotEqual(DeviceB, published.Envelope.Payload.DeviceId);
        }

        Assert.Equal(1, gw.Metrics.SessionIdentityViolations);
    }

    /// <summary>
    /// A CRC-invalid frame that WOULD have required an acknowledgement gets none. The ACK is a
    /// durability promise — a tracker drops a frame from its own buffer once the server answers —
    /// so acknowledging a frame we could not even verify destroys the last copy of it.
    /// </summary>
    [Fact]
    public async Task A_crc_invalid_frame_is_never_acknowledged()
    {
        await using GatewayHarness gw = await GatewayHarness.StartAsync();

        using TcpClient client = await gw.ConnectAsync();
        NetworkStream stream = client.GetStream();

        byte[] login = BuildLoginFrame(ImeiA, serial: 1);
        await stream.WriteAsync(login);
        Assert.Equal(AckForFrame(login), await ReadExactlyAsync(stream, 10));

        long acksBefore = gw.Metrics.AcksSent;

        // A heartbeat REQUIRES an ack — but this one's checksum is broken.
        byte[] heartbeat = BuildHeartbeatFrame(serial: 2);
        heartbeat[^3] ^= 0xFF;
        await stream.WriteAsync(heartbeat);

        await WaitUntilAsync(() => gw.Metrics.CrcFailures == 1, SocketTimeout);

        // Nothing came back, and the ACK counter did not move.
        Assert.Empty(await ReadAvailableAsync(stream, TimeSpan.FromMilliseconds(500)));
        Assert.Equal(acksBefore, gw.Metrics.AcksSent);
        Assert.Equal(1, gw.Metrics.CrcFailures);
        Assert.Equal(0, gw.Metrics.HeartbeatPackets);
    }

    /// <summary>
    /// A valid heartbeat IS acknowledged, so the previous test is proving the CRC gate rather than
    /// simply proving heartbeats are never answered.
    /// </summary>
    [Fact]
    public async Task A_valid_heartbeat_is_acknowledged()
    {
        await using GatewayHarness gw = await GatewayHarness.StartAsync();

        using TcpClient client = await gw.ConnectAsync();
        NetworkStream stream = client.GetStream();

        byte[] login = BuildLoginFrame(ImeiA, serial: 1);
        await stream.WriteAsync(login);
        Assert.Equal(AckForFrame(login), await ReadExactlyAsync(stream, 10));

        byte[] heartbeat = BuildHeartbeatFrame(serial: 2);
        await stream.WriteAsync(heartbeat);

        Assert.Equal(AckForFrame(heartbeat), await ReadExactlyAsync(stream, 10));
        Assert.Equal(1, gw.Metrics.HeartbeatPackets);
        Assert.Equal(2, gw.Metrics.AcksSent);
    }

    /// <summary>
    /// Two devices streaming concurrently on two sockets. Neither may ever appear in the other's
    /// events, and each socket's acknowledgements must come back on that socket alone.
    /// </summary>
    [Fact]
    public async Task Two_devices_on_two_sockets_never_cross_attribute()
    {
        await using GatewayHarness gw = await GatewayHarness.StartAsync();
        IEventSubscription<CanonicalTelemetryEvent> delivered =
            gw.Bus.Subscribe<CanonicalTelemetryEvent>(TelematicsTopics.TelemetryNormalized);

        using TcpClient clientA = await gw.ConnectAsync();
        using TcpClient clientB = await gw.ConnectAsync();
        NetworkStream streamA = clientA.GetStream();
        NetworkStream streamB = clientB.GetStream();

        byte[] loginA = BuildLoginFrame(ImeiA, serial: 1);
        byte[] loginB = BuildLoginFrame(ImeiB, serial: 1);
        await streamA.WriteAsync(loginA);
        Assert.Equal(AckForFrame(loginA), await ReadExactlyAsync(streamA, 10));
        await streamB.WriteAsync(loginB);
        Assert.Equal(AckForFrame(loginB), await ReadExactlyAsync(streamB, 10));

        // Interleave fixes. A's are around Tokyo, B's around New York, so a crossover is visible
        // in the coordinates as well as in the identity.
        for (int i = 0; i < 8; i++)
        {
            await streamA.WriteAsync(BuildLocationFrame((ushort)(10 + i), BaseFix.AddSeconds(i), lat: 35.0, lng: 139.0));
            await streamB.WriteAsync(BuildLocationFrame((ushort)(10 + i), BaseFix.AddSeconds(i), lat: 40.0, lng: 74.0));
        }

        var byDevice = new Dictionary<string, int>();
        for (int i = 0; i < 16; i++)
        {
            DeliveredEvent<CanonicalTelemetryEvent>? published = await ReadOneAsync(delivered);
            Assert.NotNull(published);
            CanonicalTelemetryEvent evt = published!.Value.Envelope.Payload;

            // Identity and tenant must agree, and the coordinates must match the right device.
            if (evt.DeviceId == DeviceA)
            {
                Assert.Equal(TenantA, evt.TenantId);
                Assert.Equal(35.0, evt.Location!.Value.Lat, 3);
            }
            else
            {
                Assert.Equal(DeviceB, evt.DeviceId);
                Assert.Equal(TenantB, evt.TenantId);
                Assert.Equal(40.0, evt.Location!.Value.Lat, 3);
            }

            byDevice[evt.DeviceId] = byDevice.GetValueOrDefault(evt.DeviceId) + 1;
        }

        Assert.Equal(8, byDevice[DeviceA]);
        Assert.Equal(8, byDevice[DeviceB]);
    }

    /// <summary>
    /// Stream reassembly must still work with the frame counters in place: a frame split across two
    /// reads is counted once, not once per read, and not at all until it is complete.
    /// </summary>
    [Fact]
    public async Task A_frame_split_across_reads_is_counted_exactly_once()
    {
        await using GatewayHarness gw = await GatewayHarness.StartAsync();

        using TcpClient client = await gw.ConnectAsync();
        NetworkStream stream = client.GetStream();

        byte[] login = BuildLoginFrame(ImeiA, serial: 1);

        // Half the login, then a pause, then the rest.
        await stream.WriteAsync(login.AsMemory(0, 6));
        await Task.Delay(150);
        Assert.Equal(0, gw.Metrics.FramesReceived);   // a partial frame is not yet a frame

        await stream.WriteAsync(login.AsMemory(6));
        Assert.Equal(AckForFrame(login), await ReadExactlyAsync(stream, 10));

        Assert.Equal(1, gw.Metrics.FramesReceived);
        Assert.Equal(1, gw.Metrics.FramesDecoded);
        Assert.Equal(1, gw.Metrics.LoginPackets);
        Assert.Equal(0, gw.Metrics.CrcFailures);
    }

    /// <summary>Reads whatever the peer has sent inside the window; an empty result means silence.</summary>
    private static async Task<byte[]> ReadAvailableAsync(NetworkStream stream, TimeSpan window)
    {
        var collected = new List<byte>();
        var buffer = new byte[256];
        using var cts = new CancellationTokenSource(window);
        try
        {
            while (true)
            {
                int read = await stream.ReadAsync(buffer, cts.Token);
                if (read == 0) break;
                collected.AddRange(buffer[..read]);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        return collected.ToArray();
    }

    /// <summary>A CRC-valid 0x13 heartbeat, which GT06 requires the server to acknowledge.</summary>
    private static byte[] BuildHeartbeatFrame(ushort serial)
    {
        var crcRegion = new List<byte> { 0x0A, 0x13, 0x46, 0x05, 0x04, 0x00, 0x02 };
        crcRegion.Add((byte)(serial >> 8));
        crcRegion.Add((byte)(serial & 0xFF));
        return Wrap(crcRegion);
    }

    // ── Harness ────────────────────────────────────────────────────────────────

    private sealed class GatewayHarness : IAsyncDisposable
    {
        private GatewayHarness(TcpGatewayService service, InMemoryEventBackbone bus, GatewayMetrics metrics)
        {
            Service = service;
            Bus = bus;
            Metrics = metrics;
        }

        public TcpGatewayService Service { get; }

        public InMemoryEventBackbone Bus { get; }

        public GatewayMetrics Metrics { get; }

        public int Port => Service.BoundPort;

        public static async Task<GatewayHarness> StartAsync(ActiveDeviceSessionRegistry? sessions = null)
        {
            var options = new GatewayOptions
            {
                ListenPort = 0,
                MaxConnections = 64,
                MaxInFlightPerConnection = 16,
                MaxFrameBytes = 2048,
                IdleTimeout = TimeSpan.FromSeconds(30),
                DrainTimeout = TimeSpan.FromSeconds(5),
            };

            var bus = new InMemoryEventBackbone();
            var metrics = new GatewayMetrics();

            // Two devices resolving to two DIFFERENT tenants: the cross-tenant scenario is only
            // meaningful if a successful re-point would actually cross an ownership boundary.
            var registry = new InMemoryDeviceRegistry(new[]
            {
                Trust(ImeiA, TenantA, companyId: 100L, DeviceA, vehicleId: 5501L),
                Trust(ImeiB, TenantB, companyId: 200L, DeviceB, vehicleId: 6602L),
            });

            var service = new TcpGatewayService(
                options,
                bus,
                registry,
                new DefaultDeviceAuthenticator(new DenyAllKeyResolver()),
                new InMemoryReplayGuard(serialModulus: 65_536),
                new InMemoryPositionProjectionStore(),
                new Gt06Adapter(options.MaxFrameBytes),
                new InMemoryStoreAndForwardBuffer(),
                metrics,
                NullLoggerFactory.Instance,
                sessions ?? new ActiveDeviceSessionRegistry());

            await service.StartAsync(CancellationToken.None);
            return new GatewayHarness(service, bus, metrics);
        }

        private static KeyValuePair<string, ResolvedDeviceTrust> Trust(
            string imei, Guid tenantId, long companyId, string deviceId, long vehicleId) =>
            new(imei, new ResolvedDeviceTrust(
                new ResolvedDeviceOwner(
                    TenantId: tenantId,
                    CompanyId: companyId,
                    DeviceId: deviceId,
                    VehicleId: vehicleId,
                    LifecycleState: DeviceLifecycleState.Online,
                    CredentialHandle: $"vault://opstrax/telematics/psk/{deviceId}"),
                DeviceTrustPolicy.ImeiAllowlistBaseline(),
                CredentialMaterial.None));

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

    /// <summary>Refuses every credential lookup: the seeded devices are the honest allowlist-only baseline.</summary>
    private sealed class DenyAllKeyResolver : ICredentialKeyResolver
    {
        public ValueTask<byte[]?> ResolveHmacKeyAsync(CredentialMaterial credential, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<byte[]?>(null);
    }

    // ── Socket / frame helpers ─────────────────────────────────────────────────

    /// <summary>Takes the next published event, failing the test if none arrives inside the window.</summary>
    private static async Task<CanonicalTelemetryEvent> NextEventAsync(
        IEventSubscription<CanonicalTelemetryEvent> subscription)
    {
        DeliveredEvent<CanonicalTelemetryEvent>? delivered = await ReadOneAsync(subscription);
        Assert.NotNull(delivered);
        return delivered!.Value.Envelope.Payload;
    }

    /// <summary>Reads one event, or null when nothing arrives — some tests expect exactly nothing.</summary>
    private static async Task<DeliveredEvent<CanonicalTelemetryEvent>?> ReadOneAsync(
        IEventSubscription<CanonicalTelemetryEvent> subscription, TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? BusTimeout);
        try
        {
            await foreach (DeliveredEvent<CanonicalTelemetryEvent> delivered in subscription.ReadAllAsync(cts.Token))
                return delivered;
        }
        catch (OperationCanceledException)
        {
        }

        return null;
    }

    private static async Task<byte[]> ReadExactlyAsync(NetworkStream stream, int count)
    {
        var buffer = new byte[count];
        int read = 0;
        using var cts = new CancellationTokenSource(SocketTimeout);
        while (read < count)
        {
            int chunk = await stream.ReadAsync(buffer.AsMemory(read), cts.Token);
            if (chunk == 0) throw new IOException("peer closed while awaiting a response");
            read += chunk;
        }
        return buffer;
    }

    private static async Task<byte[]> ReadUntilClosedAsync(NetworkStream stream)
    {
        var collected = new List<byte>();
        var buffer = new byte[256];
        using var cts = new CancellationTokenSource(SocketTimeout);
        try
        {
            while (true)
            {
                int read = await stream.ReadAsync(buffer, cts.Token);
                if (read == 0) break;
                collected.AddRange(buffer[..read]);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        return collected.ToArray();
    }

    private static async Task<bool> PeerClosedWithinAsync(NetworkStream stream, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        var buffer = new byte[16];
        try
        {
            while (true)
            {
                int read = await stream.ReadAsync(buffer, cts.Token);
                if (read == 0) return true;
            }
        }
        catch (OperationCanceledException) { return false; }
        catch (IOException) { return true; }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(20);
        }
    }

    private static byte[] AckForFrame(byte[] frame) =>
        new Gt06Adapter().EncodeAck(new Gt06Adapter().Decode(frame, out _)[0]);

    private static readonly DateTime BaseFix = new(2024, 1, 15, 10, 20, 30, DateTimeKind.Utc);

    private static byte[] BuildLoginFrame(string imei, ushort serial)
    {
        string padded = imei.PadLeft(16, '0');
        var terminalId = new byte[8];
        for (int i = 0; i < 8; i++)
            terminalId[i] = (byte)(((padded[i * 2] - '0') << 4) | (padded[(i * 2) + 1] - '0'));
        return BuildRawLoginFrame(terminalId, serial);
    }

    private static byte[] BuildRawLoginFrame(byte[] terminalId, ushort serial)
    {
        var crcRegion = new List<byte> { 0x0D, 0x01 };
        crcRegion.AddRange(terminalId);
        crcRegion.Add((byte)(serial >> 8));
        crcRegion.Add((byte)(serial & 0xFF));
        return Wrap(crcRegion);
    }

    private static byte[] BuildLocationFrame(ushort serial, DateTime fixTime, double lat, double lng, int speed = 60)
    {
        var info = new List<byte>
        {
            (byte)(fixTime.Year - 2000), (byte)fixTime.Month, (byte)fixTime.Day,
            (byte)fixTime.Hour, (byte)fixTime.Minute, (byte)fixTime.Second,
            0x09,
        };
        AppendBigEndian(info, (uint)Math.Round(Math.Abs(lat) * 1_800_000));
        AppendBigEndian(info, (uint)Math.Round(Math.Abs(lng) * 1_800_000));
        info.Add((byte)speed);

        // Course/status: bit12 positioned, bit10 North (=> +lat), bit11 clear (=> East, +lng),
        // bit13 clear (=> real-time GPS). Written from the vendor bit table, not from the parser.
        const ushort courseStatus = (1 << 12) | (1 << 10);
        info.Add((byte)(courseStatus >> 8));
        info.Add((byte)(courseStatus & 0xFF));

        int packetLength = 1 + info.Count + 2 + 2;
        var crcRegion = new List<byte> { (byte)packetLength, 0x12 };
        crcRegion.AddRange(info);
        crcRegion.Add((byte)(serial >> 8));
        crcRegion.Add((byte)(serial & 0xFF));
        return Wrap(crcRegion);
    }

    private static byte[] Wrap(List<byte> crcRegion)
    {
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
}
