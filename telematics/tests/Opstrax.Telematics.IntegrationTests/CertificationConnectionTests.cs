using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Opstrax.Telematics.Contracts;
using Opstrax.Telematics.Contracts.Adapters;
using Opstrax.Telematics.Contracts.Eventing;
using Opstrax.Telematics.Contracts.Identity;
using Opstrax.Telematics.Contracts.Lifecycle;
using Opstrax.Telematics.Gateway;
using Opstrax.Telematics.Gateway.Buffering;
using Opstrax.Telematics.Gateway.Identity;
using Opstrax.Telematics.Gateway.Projection;
using Opstrax.Telematics.Gateway.Quality;
using Opstrax.Telematics.Gateway.Security;
using Opstrax.Telematics.Gateway.Security.Auth;
using Opstrax.Telematics.Gateway.Security.Replay;
using Opstrax.Telematics.Protocols.Gt06;

namespace Opstrax.Telematics.IntegrationTests;

/// <summary>
/// INDEPENDENT CERTIFICATION of the socket layer at candidate SHA 7bf66aa: session identity,
/// duplicate-IMEI policy, byte-stream framing, connection lifecycle, and multi-device isolation.
/// </summary>
/// <remarks>
/// Written against the gateway's observable behaviour over real loopback sockets, deliberately not
/// reusing the candidate's own harness or its expectations.
/// </remarks>
public sealed class CertificationConnectionTests : IAsyncLifetime
{
    private static readonly TimeSpan Io = TimeSpan.FromSeconds(5);
    private CertHarness _gw = null!;

    public async Task InitializeAsync() => _gw = await CertHarness.StartAsync();

    public async Task DisposeAsync() => await _gw.DisposeAsync();

    // ── A. Connection / session identity ──────────────────────────────────────

    [Fact]
    public async Task A_Session_identity_is_immutable_and_no_packet_is_attributable_to_the_second_claim()
    {
        IEventSubscription<CanonicalTelemetryEvent> bus = _gw.Subscribe();
        using TcpClient client = await _gw.ConnectAsync();
        NetworkStream s = client.GetStream();

        // Login IMEI A, then data.
        byte[] loginA = Login(CertHarness.ImeiA, 1);
        await s.WriteAsync(loginA);
        Assert.Equal(Ack(loginA), await ReadExactly(s, 10));
        await s.WriteAsync(Location(2, Fix, 38.0, 77.0));
        CanonicalTelemetryEvent first = await Next(bus);
        Assert.Equal(CertHarness.DeviceA, first.DeviceId);

        // Login IMEI B on the SAME socket, then a further anonymous packet.
        await s.WriteAsync(Login(CertHarness.ImeiB, 3));
        await s.WriteAsync(Location(4, Fix.AddSeconds(30), 38.1, 77.1));

        // Policy: refuse, do not acknowledge, close.
        byte[] tail = await ReadUntilClosed(s);
        Assert.Empty(tail);

        // No event, ever, under B.
        var seen = new List<CanonicalTelemetryEvent>();
        while (await ReadOne(bus, TimeSpan.FromMilliseconds(400)) is { } e) seen.Add(e);
        Assert.All(seen, e =>
        {
            Assert.Equal(CertHarness.DeviceA, e.DeviceId);
            Assert.Equal(CertHarness.TenantA, e.TenantId);
        });
        Assert.Equal(1, _gw.Metrics.SessionIdentityViolations);
    }

    [Fact]
    public async Task A_Re_login_as_the_same_device_is_idempotent()
    {
        using TcpClient client = await _gw.ConnectAsync();
        NetworkStream s = client.GetStream();

        byte[] a = Login(CertHarness.ImeiA, 1);
        await s.WriteAsync(a);
        Assert.Equal(Ack(a), await ReadExactly(s, 10));

        byte[] again = Login(CertHarness.ImeiA, 2);
        await s.WriteAsync(again);
        Assert.Equal(Ack(again), await ReadExactly(s, 10));
        Assert.Equal(0, _gw.Metrics.SessionIdentityViolations);
    }

    // ── B. Duplicate IMEI on two sockets ──────────────────────────────────────

    [Fact]
    public async Task B_Duplicate_imei_policy_is_latest_wins_with_no_cross_socket_contamination()
    {
        IEventSubscription<CanonicalTelemetryEvent> bus = _gw.Subscribe();

        using TcpClient first = await _gw.ConnectAsync();
        NetworkStream s1 = first.GetStream();
        byte[] l1 = Login(CertHarness.ImeiA, 1);
        await s1.WriteAsync(l1);
        Assert.Equal(Ack(l1), await ReadExactly(s1, 10));

        using TcpClient second = await _gw.ConnectAsync();
        NetworkStream s2 = second.GetStream();
        byte[] l2 = Login(CertHarness.ImeiA, 1);
        await s2.WriteAsync(l2);
        byte[] ack2 = await ReadExactly(s2, 10);

        // Policy: the newest admitted login wins and the older socket is closed.
        Assert.Equal(Ack(l2), ack2);
        Assert.True(await ClosedWithin(s1, Io), "older socket must be closed under latest-wins");
        Assert.Equal(1, _gw.Metrics.DuplicateSessionsDisplaced);
        Assert.Equal(1, _gw.Sessions.ActiveSessionCount);

        // The survivor still works, and its ACK came back on ITS socket only.
        await s2.WriteAsync(Location(2, Fix, 38.0, 77.0));
        Assert.Equal(CertHarness.DeviceA, (await Next(bus)).DeviceId);
    }

    // ── C. TCP byte-stream framing matrix ─────────────────────────────────────

    [Fact]
    public async Task C1_One_complete_frame() => await ExpectAccepted(w => w(Location(2, Fix, 38.0, 77.0)), 1);

    [Fact]
    public async Task C2_Two_frames_in_one_write() =>
        await ExpectAccepted(w => w(Location(2, Fix, 38.0, 77.0).Concat(Location(3, Fix.AddSeconds(10), 38.1, 77.0)).ToArray()), 2);

    [Fact]
    public async Task C3_Frame_split_into_two_reads() => await ExpectAccepted(async w =>
    {
        byte[] f = Location(2, Fix, 38.0, 77.0);
        await w(f[..7]); await Task.Delay(80); await w(f[7..]);
    }, 1);

    [Fact]
    public async Task C4_Frame_delivered_one_byte_at_a_time() => await ExpectAccepted(async w =>
    {
        foreach (byte b in Location(2, Fix, 38.0, 77.0)) { await w(new[] { b }); }
    }, 1);

    [Fact]
    public async Task C5_Two_frames_plus_partial_third_then_the_remainder() => await ExpectAccepted(async w =>
    {
        byte[] a = Location(2, Fix, 38.0, 77.0);
        byte[] b = Location(3, Fix.AddSeconds(10), 38.1, 77.0);
        byte[] c = Location(4, Fix.AddSeconds(20), 38.2, 77.0);
        await w(a.Concat(b).Concat(c[..9]).ToArray());
        await Task.Delay(80);
        await w(c[9..]);
    }, 3);

    [Fact]
    public async Task C6_Bad_crc_between_valid_frames_is_skipped_and_the_connection_survives()
    {
        IEventSubscription<CanonicalTelemetryEvent> bus = _gw.Subscribe();
        using TcpClient client = await _gw.ConnectAsync();
        NetworkStream s = client.GetStream();
        await LoginOk(s);

        byte[] good1 = Location(2, Fix, 38.0, 77.0);
        byte[] bad = Location(3, Fix.AddSeconds(10), 38.1, 77.0);
        bad[^3] ^= 0xFF;
        byte[] good2 = Location(4, Fix.AddSeconds(20), 38.2, 77.0);
        await s.WriteAsync(good1.Concat(bad).Concat(good2).ToArray());

        Assert.NotNull(await ReadOne(bus, Io));
        Assert.NotNull(await ReadOne(bus, Io));
        Assert.Null(await ReadOne(bus, TimeSpan.FromMilliseconds(400)));
        Assert.False(await ClosedWithin(s, TimeSpan.FromMilliseconds(300)), "one bad CRC must not drop the connection");
        Assert.Equal(1, _gw.Metrics.CrcFailures);
    }

    [Fact]
    public async Task C7_Truncated_frame_is_retained_and_never_decoded()
    {
        IEventSubscription<CanonicalTelemetryEvent> bus = _gw.Subscribe();
        using TcpClient client = await _gw.ConnectAsync();
        NetworkStream s = client.GetStream();
        await LoginOk(s);

        byte[] f = Location(2, Fix, 38.0, 77.0);
        await s.WriteAsync(f[..(f.Length - 4)]);

        Assert.Null(await ReadOne(bus, TimeSpan.FromMilliseconds(500)));
        Assert.False(await ClosedWithin(s, TimeSpan.FromMilliseconds(300)), "a partial frame is not an error");
    }

    [Theory]
    [InlineData("malformed length")]
    [InlineData("invalid stop bits")]
    [InlineData("oversized length")]
    [InlineData("random data")]
    public async Task C8_Unrecoverable_framing_drops_only_that_connection(string kind)
    {
        // A healthy neighbour must be entirely unaffected.
        using TcpClient healthy = await _gw.ConnectAsync();
        NetworkStream hs = healthy.GetStream();
        await LoginOk(hs, CertHarness.ImeiB, CertHarness.DeviceB);

        using TcpClient hostile = await _gw.ConnectAsync();
        NetworkStream xs = hostile.GetStream();
        await xs.WriteAsync(kind switch
        {
            "malformed length" => new byte[] { 0x78, 0x78, 0x02, 0x01, 0x00, 0x01, 0x00, 0x00, 0x0D, 0x0A },
            "invalid stop bits" => BrokenStop(Login(CertHarness.ImeiA, 1)),
            "oversized length" => new byte[] { 0x78, 0x78, 0xFF, 0x01 }.Concat(new byte[300]).ToArray(),
            _ => Enumerable.Range(0, 64).Select(i => (byte)(i * 7 + 3)).ToArray(),
        });

        Assert.True(await ClosedWithin(xs, Io), $"'{kind}' must drop the offending connection");

        // The neighbour still transacts.
        IEventSubscription<CanonicalTelemetryEvent> bus = _gw.Subscribe();
        await hs.WriteAsync(Location(9, Fix, 40.0, 74.0));
        CanonicalTelemetryEvent e = await Next(bus);
        Assert.Equal(CertHarness.DeviceB, e.DeviceId);
    }

    // ── H. Connection lifecycle ───────────────────────────────────────────────

    [Fact]
    public async Task H_Fin_and_rst_disconnects_both_release_all_session_state()
    {
        // Graceful FIN.
        using (TcpClient fin = await _gw.ConnectAsync())
        {
            await LoginOk(fin.GetStream());
            Assert.Equal(1, _gw.Sessions.ActiveSessionCount);
        }
        await Until(() => _gw.Sessions.ActiveSessionCount == 0, Io);
        Assert.Equal(0, _gw.Sessions.ActiveSessionCount);

        // Abortive RST.
        var rst = new TcpClient { NoDelay = true };
        await rst.ConnectAsync(IPAddress.Loopback, _gw.Port);
        await LoginOk(rst.GetStream());
        Assert.Equal(1, _gw.Sessions.ActiveSessionCount);
        rst.Client.LingerState = new LingerOption(true, 0);   // close() now sends RST
        rst.Close();

        await Until(() => _gw.Sessions.ActiveSessionCount == 0, Io);
        Assert.Equal(0, _gw.Sessions.ActiveSessionCount);
    }

    [Fact]
    public async Task H_Rapid_reconnect_soak_leaves_no_residue()
    {
        const int cycles = 60;
        for (int i = 0; i < cycles; i++)
        {
            using TcpClient c = await _gw.ConnectAsync();
            await LoginOk(c.GetStream());
        }

        await Until(() => _gw.Sessions.ActiveSessionCount == 0 && _gw.Metrics.ActiveConnections == 0, Io);

        Assert.Equal(0, _gw.Sessions.ActiveSessionCount);
        Assert.Equal(0, _gw.Metrics.ActiveConnections);
        Assert.Equal(cycles, _gw.Metrics.LoginPackets);

        // Displacement count is deliberately NOT pinned. Each socket is closed before the next
        // connects, so whether the server has finished releasing the previous lease by the time
        // the next login lands is a race with no correct answer — both outcomes are healthy. The
        // invariant that matters is the residue, asserted above: every lease and every connection
        // slot is returned. Bounding it still catches a runaway.
        Assert.InRange(_gw.Metrics.DuplicateSessionsDisplaced, 0, cycles - 1);
    }

    // ── I. Multi-device isolation at scale ────────────────────────────────────

    [Fact]
    public async Task I_One_hundred_devices_never_cross_attribute()
    {
        const int devices = 100;
        await using CertHarness gw = await CertHarness.StartAsync(deviceCount: devices);
        IEventSubscription<CanonicalTelemetryEvent> bus = gw.Subscribe();

        var clients = new TcpClient[devices];
        try
        {
            // Each device logs in on its own socket.
            for (int i = 0; i < devices; i++)
            {
                clients[i] = await gw.ConnectAsync();
                byte[] login = Login(CertHarness.FleetImei(i), 1);
                await clients[i].GetStream().WriteAsync(login);
                Assert.Equal(Ack(login), await ReadExactly(clients[i].GetStream(), 10));
            }

            // Each sends a fix at a latitude unique to that device, so a crossover is visible in
            // the payload and not only in the identity.
            for (int i = 0; i < devices; i++)
                await clients[i].GetStream().WriteAsync(Location(2, Fix, 10.0 + (i * 0.01), 20.0));

            var byDevice = new Dictionary<string, int>();
            for (int n = 0; n < devices; n++)
            {
                CanonicalTelemetryEvent e = await Next(bus);
                int index = CertHarness.IndexOfDevice(e.DeviceId);
                Assert.InRange(index, 0, devices - 1);

                // Identity, tenant and the coordinate must all agree on the same device.
                Assert.Equal(CertHarness.FleetTenant(index), e.TenantId);
                Assert.Equal(CertHarness.FleetCompany(index), e.CompanyId);
                Assert.Equal(10.0 + (index * 0.01), e.Location!.Value.Lat, 4);
                byDevice[e.DeviceId] = byDevice.GetValueOrDefault(e.DeviceId) + 1;
            }

            Assert.Equal(devices, byDevice.Count);
            Assert.All(byDevice.Values, v => Assert.Equal(1, v));
            Assert.Equal(devices, gw.Sessions.ActiveSessionCount);
        }
        finally
        {
            foreach (TcpClient? c in clients) c?.Dispose();
        }
    }

    // ── Plumbing ──────────────────────────────────────────────────────────────

    private async Task ExpectAccepted(Func<Func<byte[], Task>, Task> write, int expectedEvents)
    {
        IEventSubscription<CanonicalTelemetryEvent> bus = _gw.Subscribe();
        using TcpClient client = await _gw.ConnectAsync();
        NetworkStream s = client.GetStream();
        await LoginOk(s);

        await write(bytes => s.WriteAsync(bytes).AsTask());

        for (int i = 0; i < expectedEvents; i++)
            Assert.NotNull(await ReadOne(bus, Io));
        Assert.Null(await ReadOne(bus, TimeSpan.FromMilliseconds(300)));
    }

    private Task ExpectAccepted(Action<Func<byte[], Task>> write, int expectedEvents) =>
        ExpectAccepted(w => { write(w); return Task.CompletedTask; }, expectedEvents);

    private async Task LoginOk(NetworkStream s, string? imei = null, string? _ = null)
    {
        byte[] login = Login(imei ?? CertHarness.ImeiA, 1);
        await s.WriteAsync(login);
        Assert.Equal(Ack(login), await ReadExactly(s, 10));
    }

    private static byte[] BrokenStop(byte[] frame)
    {
        var copy = (byte[])frame.Clone();
        copy[^1] = 0xFF;
        return copy;
    }

    private static readonly DateTime Fix = new(2024, 1, 15, 10, 20, 30, DateTimeKind.Utc);

    private static byte[] Ack(byte[] frame) =>
        new Gt06Adapter().EncodeAck(new Gt06Adapter().Decode(frame, out _)[0]);

    private static byte[] Login(string imei, ushort serial)
    {
        string p = imei.PadLeft(16, '0');
        var id = new byte[8];
        for (int i = 0; i < 8; i++) id[i] = (byte)(((p[i * 2] - '0') << 4) | (p[(i * 2) + 1] - '0'));
        var region = new List<byte> { 0x0D, 0x01 };
        region.AddRange(id);
        region.Add((byte)(serial >> 8)); region.Add((byte)(serial & 0xFF));
        return Wrap(region);
    }

    private static byte[] Location(ushort serial, DateTime fixTime, double lat, double lng)
    {
        var info = new List<byte>
        {
            (byte)(fixTime.Year - 2000), (byte)fixTime.Month, (byte)fixTime.Day,
            (byte)fixTime.Hour, (byte)fixTime.Minute, (byte)fixTime.Second, 0x09,
        };
        void Be(uint v) { info.Add((byte)(v >> 24)); info.Add((byte)(v >> 16)); info.Add((byte)(v >> 8)); info.Add((byte)v); }
        Be((uint)Math.Round(Math.Abs(lat) * 1_800_000));
        Be((uint)Math.Round(Math.Abs(lng) * 1_800_000));
        info.Add(60);
        const ushort cs = (1 << 12) | (1 << 10);   // positioned, North, East, real-time
        info.Add((byte)(cs >> 8)); info.Add((byte)(cs & 0xFF));

        var region = new List<byte> { (byte)(1 + info.Count + 4), 0x12 };
        region.AddRange(info);
        region.Add((byte)(serial >> 8)); region.Add((byte)(serial & 0xFF));
        return Wrap(region);
    }

    private static byte[] Wrap(List<byte> region)
    {
        ushort crc = Gt06Adapter.Crc16Itu(region.ToArray());
        var f = new List<byte> { 0x78, 0x78 };
        f.AddRange(region);
        f.Add((byte)(crc >> 8)); f.Add((byte)(crc & 0xFF)); f.Add(0x0D); f.Add(0x0A);
        return f.ToArray();
    }

    private static async Task<byte[]> ReadExactly(NetworkStream s, int count)
    {
        var buf = new byte[count];
        int read = 0;
        using var cts = new CancellationTokenSource(Io);
        while (read < count)
        {
            int n = await s.ReadAsync(buf.AsMemory(read), cts.Token);
            if (n == 0) throw new IOException("peer closed while awaiting a response");
            read += n;
        }
        return buf;
    }

    private static async Task<byte[]> ReadUntilClosed(NetworkStream s)
    {
        var all = new List<byte>();
        var buf = new byte[256];
        using var cts = new CancellationTokenSource(Io);
        try
        {
            while (true)
            {
                int n = await s.ReadAsync(buf, cts.Token);
                if (n == 0) break;
                all.AddRange(buf[..n]);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        return all.ToArray();
    }

    private static async Task<bool> ClosedWithin(NetworkStream s, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        var buf = new byte[32];
        try
        {
            while (true) { if (await s.ReadAsync(buf, cts.Token) == 0) return true; }
        }
        catch (OperationCanceledException) { return false; }
        catch (IOException) { return true; }
    }

    private static async Task<CanonicalTelemetryEvent> Next(IEventSubscription<CanonicalTelemetryEvent> bus)
    {
        CanonicalTelemetryEvent? e = await ReadOne(bus, Io);
        Assert.NotNull(e);
        return e!;
    }

    private static async Task<CanonicalTelemetryEvent?> ReadOne(
        IEventSubscription<CanonicalTelemetryEvent> bus, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await foreach (DeliveredEvent<CanonicalTelemetryEvent> d in bus.ReadAllAsync(cts.Token))
                return d.Envelope.Payload;
        }
        catch (OperationCanceledException) { }
        return null;
    }

    private static async Task Until(Func<bool> condition, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline) { if (condition()) return; await Task.Delay(20); }
    }

    /// <summary>A self-contained gateway with a fleet of independently owned devices.</summary>
    internal sealed class CertHarness : IAsyncDisposable
    {
        public const string ImeiA = "868120303337976";
        public const string ImeiB = "868120303337977";
        public const string DeviceA = "cert-dev-0000";
        public const string DeviceB = "cert-dev-0001";
        public static readonly Guid TenantA = FleetTenant(0);

        private CertHarness(TcpGatewayService svc, InMemoryEventBackbone bus,
            GatewayMetrics metrics, ActiveDeviceSessionRegistry sessions)
        { _svc = svc; _bus = bus; Metrics = metrics; Sessions = sessions; }

        private readonly TcpGatewayService _svc;
        private readonly InMemoryEventBackbone _bus;

        public GatewayMetrics Metrics { get; }
        public ActiveDeviceSessionRegistry Sessions { get; }
        public int Port => _svc.BoundPort;

        public static string FleetImei(int i) => $"86812030333{i:D4}";
        public static string FleetDevice(int i) => $"cert-dev-{i:D4}";
        public static Guid FleetTenant(int i) => new($"{i:D8}-0000-4000-8000-000000000000");
        public static long FleetCompany(int i) => 1000 + i;
        public static int IndexOfDevice(string deviceId) =>
            int.TryParse(deviceId.AsSpan("cert-dev-".Length), out int i) ? i : -1;

        public static async Task<CertHarness> StartAsync(int deviceCount = 8)
        {
            var options = new GatewayOptions
            {
                ListenPort = 0,
                MaxConnections = 512,
                MaxInFlightPerConnection = 16,
                MaxFrameBytes = 2048,
                IdleTimeout = TimeSpan.FromSeconds(30),
                DrainTimeout = TimeSpan.FromSeconds(5),
            };

            var seed = new List<KeyValuePair<string, ResolvedDeviceTrust>>();
            for (int i = 0; i < Math.Max(deviceCount, 2); i++)
            {
                // Device 0 and 1 also answer to the two named IMEIs used by the focused tests.
                string imei = i switch { 0 => ImeiA, 1 => ImeiB, _ => FleetImei(i) };
                seed.Add(new(imei, new ResolvedDeviceTrust(
                    new ResolvedDeviceOwner(FleetTenant(i), FleetCompany(i), FleetDevice(i),
                        VehicleId: 5000 + i, LifecycleState: DeviceLifecycleState.Online,
                        CredentialHandle: $"vault://cert/{i}"),
                    DeviceTrustPolicy.ImeiAllowlistBaseline(), CredentialMaterial.None)));
                if (i >= 2) continue;
                seed.Add(new(FleetImei(i), new ResolvedDeviceTrust(
                    new ResolvedDeviceOwner(FleetTenant(i), FleetCompany(i), FleetDevice(i),
                        VehicleId: 5000 + i, LifecycleState: DeviceLifecycleState.Online,
                        CredentialHandle: $"vault://cert/{i}"),
                    DeviceTrustPolicy.ImeiAllowlistBaseline(), CredentialMaterial.None)));
            }

            var bus = new InMemoryEventBackbone();
            var metrics = new GatewayMetrics();
            var sessions = new ActiveDeviceSessionRegistry();

            var svc = new TcpGatewayService(
                options, bus, new InMemoryDeviceRegistry(seed),
                new DefaultDeviceAuthenticator(new DenyAll()),
                new InMemoryReplayGuard(serialModulus: 65_536),
                new InMemoryPositionProjectionStore(),
                new Gt06Adapter(options.MaxFrameBytes),
                new InMemoryStoreAndForwardBuffer(),
                metrics, NullLoggerFactory.Instance, sessions, new FixPlausibilityGuard());

            await svc.StartAsync(CancellationToken.None);
            return new CertHarness(svc, bus, metrics, sessions);
        }

        public IEventSubscription<CanonicalTelemetryEvent> Subscribe() =>
            _bus.Subscribe<CanonicalTelemetryEvent>(TelematicsTopics.TelemetryNormalized);

        public async Task<TcpClient> ConnectAsync()
        {
            var c = new TcpClient { NoDelay = true };
            await c.ConnectAsync(IPAddress.Loopback, Port);
            return c;
        }

        public async ValueTask DisposeAsync()
        {
            await _svc.StopAsync(CancellationToken.None);
            _svc.Dispose();
        }

        private sealed class DenyAll : ICredentialKeyResolver
        {
            public ValueTask<byte[]?> ResolveHmacKeyAsync(CredentialMaterial c, CancellationToken t = default) =>
                ValueTask.FromResult<byte[]?>(null);
        }
    }
}
