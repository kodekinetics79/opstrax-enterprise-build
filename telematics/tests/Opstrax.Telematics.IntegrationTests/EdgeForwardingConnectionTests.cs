using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Opstrax.Telematics.Contracts.Adapters;
using Opstrax.Telematics.Gateway;
using Opstrax.Telematics.Gateway.Edge;
using Opstrax.Telematics.Gateway.Forwarding;
using Opstrax.Telematics.Gateway.Security.Replay;
using Opstrax.Telematics.Protocols.Gt06;

namespace Opstrax.Telematics.IntegrationTests;

/// <summary>
/// Drives a real TCP tracker session through the public forwarding edge, over a real socket, with
/// real CRC-valid GT06 frames.
/// </summary>
/// <remarks>
/// The properties under test are the ones an operator is betting on: an unlisted device gets
/// nothing, a listed device's fix reaches OpsTrax intact, an outage parks rather than drops, and
/// an acknowledgement is only ever sent once the fix is somewhere durable.
/// </remarks>
public sealed class EdgeForwardingConnectionTests
{
    private const string AllowedImei = "862464068456321";
    private const string UnlistedImei = "351234567890999";

    private static readonly TimeSpan SocketTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task AnAllowlistedDevicesFix_ReachesOpsTraxIntact()
    {
        await using EdgeHarness harness = await EdgeHarness.StartAsync();
        using TcpClient client = await harness.ConnectAsync();
        NetworkStream stream = client.GetStream();

        await stream.WriteAsync(BuildLoginFrame(AllowedImei));
        await ReadExactlyAsync(stream, 10); // login ack

        await stream.WriteAsync(BuildLocationFrame(serial: 2, fixTime: RecentFix()));
        await WaitForAsync(() => harness.Forwarder.Delivered.Count == 1);

        string payload = Assert.Single(harness.Forwarder.Delivered);
        Assert.Contains($"\"imei\":\"{AllowedImei}\"", payload);
        Assert.Contains("\"protocol\":\"GT06\"", payload);
        Assert.Contains("\"edgeInstance\":\"test-edge\"", payload);
        Assert.Equal(1, harness.Metrics.ObservationsDelivered);
        Assert.Equal(0, harness.Outbox.Count);
    }

    [Fact]
    public async Task AnAlarmFrame_IsAcknowledgedAndCarriesItsSafetyEvent()
    {
        // GT06 acknowledges alarm frames but not plain location frames, so the alarm path is where
        // the "acknowledge only what is durable" contract is actually observable on the wire.
        await using EdgeHarness harness = await EdgeHarness.StartAsync();
        using TcpClient client = await harness.ConnectAsync();
        NetworkStream stream = client.GetStream();

        await stream.WriteAsync(BuildLoginFrame(AllowedImei));
        await ReadExactlyAsync(stream, 10);

        await stream.WriteAsync(BuildAlarmFrame(serial: 3, fixTime: RecentFix(), alarmCode: 0x01));
        byte[] ack = await ReadExactlyAsync(stream, 10);

        Assert.Equal(0x78, ack[0]);
        string payload = Assert.Single(harness.Forwarder.Delivered);
        Assert.Contains("\"harshEvent\":\"sos\"", payload);
    }

    [Fact]
    public async Task AnUnlistedDevice_IsRefusedWithoutAnAcknowledgementAndTheConnectionCloses()
    {
        // No ack is the point: the device must not be told it is registered, and it must not be
        // left holding a session it can stream fixes down.
        await using EdgeHarness harness = await EdgeHarness.StartAsync();
        using TcpClient client = await harness.ConnectAsync();
        NetworkStream stream = client.GetStream();

        await stream.WriteAsync(BuildLoginFrame(UnlistedImei));
        byte[] response = await ReadUntilClosedAsync(stream);

        Assert.Empty(response);
        Assert.Empty(harness.Forwarder.Delivered);
        Assert.Equal(1, harness.Metrics.AllowlistRefusals);
    }

    [Fact]
    public async Task AFixOnASessionThatNeverIdentified_IsNotForwarded()
    {
        await using EdgeHarness harness = await EdgeHarness.StartAsync();
        using TcpClient client = await harness.ConnectAsync();
        NetworkStream stream = client.GetStream();

        await stream.WriteAsync(BuildLocationFrame(serial: 5, fixTime: RecentFix()));
        await ReadUntilClosedAsync(stream);

        Assert.Empty(harness.Forwarder.Delivered);
        Assert.Equal(1, harness.Metrics.AllowlistRefusals);
    }

    [Fact]
    public async Task AByteForByteRetransmission_IsSuppressedButStillAcknowledged()
    {
        // The device is retrying because it never saw an acknowledgement. Re-sending would only
        // earn a 409 from OpsTrax's durable ledger; withholding the ack would make it retry forever.
        await using EdgeHarness harness = await EdgeHarness.StartAsync();
        using TcpClient client = await harness.ConnectAsync();
        NetworkStream stream = client.GetStream();

        await stream.WriteAsync(BuildLoginFrame(AllowedImei));
        await ReadExactlyAsync(stream, 10);

        byte[] frame = BuildAlarmFrame(serial: 3, fixTime: RecentFix(), alarmCode: 0x01);
        await stream.WriteAsync(frame);
        await ReadExactlyAsync(stream, 10);

        await stream.WriteAsync(frame);
        await ReadExactlyAsync(stream, 10);

        Assert.Single(harness.Forwarder.Delivered);
        Assert.Equal(1, harness.Metrics.ReplayDuplicatesDropped);
    }

    [Fact]
    public async Task WhenOpsTraxIsUnreachable_TheFixIsParkedAndStillAcknowledged()
    {
        await using EdgeHarness harness = await EdgeHarness.StartAsync(
            forwarder => forwarder.Outcome = ForwardOutcome.Retryable);

        using TcpClient client = await harness.ConnectAsync();
        NetworkStream stream = client.GetStream();

        await stream.WriteAsync(BuildLoginFrame(AllowedImei));
        await ReadExactlyAsync(stream, 10);

        await stream.WriteAsync(BuildAlarmFrame(serial: 4, fixTime: RecentFix(), alarmCode: 0x01));

        // Acknowledged because it is durable — just not delivered yet.
        byte[] ack = await ReadExactlyAsync(stream, 10);

        Assert.Equal(0x78, ack[0]);
        Assert.Equal(1, harness.Outbox.Count);
        Assert.Equal(1, harness.Metrics.ObservationsParked);

        // Parked byte-for-byte, so a signature computed over it later is still valid.
        IReadOnlyList<OutboxEntry> parked = await harness.Outbox.PeekAsync(10);
        Assert.Contains($"\"imei\":\"{AllowedImei}\"", parked[0].PayloadJson);
    }

    [Fact]
    public async Task WhenAFixCanBeNeitherDeliveredNorParked_NoAcknowledgementIsSent()
    {
        // The device's own buffer is then the last surviving copy, and staying silent is what makes
        // it retransmit rather than discard.
        await using EdgeHarness harness = await EdgeHarness.StartAsync(
            forwarder => forwarder.Outcome = ForwardOutcome.Retryable,
            outbox: new RefusingOutbox());

        using TcpClient client = await harness.ConnectAsync();
        NetworkStream stream = client.GetStream();

        await stream.WriteAsync(BuildLoginFrame(AllowedImei));
        await ReadExactlyAsync(stream, 10);

        await stream.WriteAsync(BuildAlarmFrame(serial: 6, fixTime: RecentFix(), alarmCode: 0x01));
        byte[] response = await ReadUntilTimeoutAsync(stream, TimeSpan.FromMilliseconds(600));

        Assert.Empty(response);
    }

    [Fact]
    public async Task AStreamThatSpeaksNoInstalledProtocol_IsDroppedWithoutBeingGuessedAt()
    {
        await using EdgeHarness harness = await EdgeHarness.StartAsync();
        using TcpClient client = await harness.ConnectAsync();
        NetworkStream stream = client.GetStream();

        await stream.WriteAsync(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x11 });
        byte[] response = await ReadUntilClosedAsync(stream);

        Assert.Empty(response);
        Assert.Empty(harness.Forwarder.Delivered);
        Assert.Equal(1, harness.Metrics.UnidentifiedProtocolConnections);
    }

    [Fact]
    public async Task ABadChecksumFrame_IsSkippedWithoutFabricatingAFix()
    {
        // The GT06 decoder's documented policy is to skip a CRC-failed frame and keep the stream:
        // one corrupted frame between good ones must not cost the rest. What matters at the edge is
        // that nothing is forwarded for it — a fix is never reconstructed from bytes that failed
        // their own integrity check.
        await using EdgeHarness harness = await EdgeHarness.StartAsync();
        using TcpClient client = await harness.ConnectAsync();
        NetworkStream stream = client.GetStream();

        await stream.WriteAsync(BuildLoginFrame(AllowedImei));
        await ReadExactlyAsync(stream, 10);

        byte[] corrupt = BuildAlarmFrame(serial: 7, fixTime: RecentFix(), alarmCode: 0x01);
        corrupt[^3] ^= 0xFF; // break the CRC
        await stream.WriteAsync(corrupt);

        // The stream survives, so a subsequent good frame still arrives and is acknowledged.
        await stream.WriteAsync(BuildAlarmFrame(serial: 8, fixTime: RecentFix(), alarmCode: 0x01));
        await ReadExactlyAsync(stream, 10);

        Assert.Single(harness.Forwarder.Delivered);
    }

    [Fact]
    public async Task UnrecoverableFraming_DropsThatConnectionOnly()
    {
        // Broken stop bits are not recoverable: the decoder cannot know where the next frame starts,
        // so it fails closed. The property under test is blast radius — one hostile peer must not
        // affect the listener or any other device.
        await using EdgeHarness harness = await EdgeHarness.StartAsync();

        using (TcpClient hostile = await harness.ConnectAsync())
        {
            NetworkStream stream = hostile.GetStream();
            await stream.WriteAsync(BuildLoginFrame(AllowedImei));
            await ReadExactlyAsync(stream, 10);

            byte[] unframed = BuildAlarmFrame(serial: 9, fixTime: RecentFix(), alarmCode: 0x01);
            unframed[^1] = 0xFF; // was 0x0A: the stop bits are gone
            await stream.WriteAsync(unframed);
            await ReadUntilClosedAsync(stream);
        }

        await WaitForAsync(() => harness.GatewayMetrics.MalformedConnectionsDropped >= 1);

        // The listener is unharmed: a second device connects and is served normally.
        using TcpClient healthy = await harness.ConnectAsync();
        NetworkStream good = healthy.GetStream();
        await good.WriteAsync(BuildLoginFrame(AllowedImei));
        await ReadExactlyAsync(good, 10);
        await good.WriteAsync(BuildLocationFrame(serial: 10, fixTime: RecentFix()));
        await WaitForAsync(() => harness.Forwarder.Delivered.Count == 1);

        Assert.Single(harness.Forwarder.Delivered);
    }

    [Fact]
    public async Task NoFixEverAssertsOwnership()
    {
        await using EdgeHarness harness = await EdgeHarness.StartAsync();
        using TcpClient client = await harness.ConnectAsync();
        NetworkStream stream = client.GetStream();

        await stream.WriteAsync(BuildLoginFrame(AllowedImei));
        await ReadExactlyAsync(stream, 10);
        await stream.WriteAsync(BuildLocationFrame(serial: 9, fixTime: RecentFix()));
        await WaitForAsync(() => harness.Forwarder.Delivered.Count == 1);

        string payload = Assert.Single(harness.Forwarder.Delivered);
        foreach (string forbidden in new[] { "companyId", "tenantId", "vehicleId", "driverId" })
            Assert.DoesNotContain(forbidden, payload, StringComparison.OrdinalIgnoreCase);
    }

    // ── Harness ────────────────────────────────────────────────────────────────

    private sealed class EdgeHarness : IAsyncDisposable
    {
        private readonly string? _outboxDirectory;

        private EdgeHarness(
            TcpGatewayService service,
            StubForwarder forwarder,
            IForwardOutbox outbox,
            EdgeMetrics metrics,
            GatewayMetrics gatewayMetrics,
            string? outboxDirectory)
        {
            Service = service;
            Forwarder = forwarder;
            Outbox = outbox;
            Metrics = metrics;
            GatewayMetrics = gatewayMetrics;
            _outboxDirectory = outboxDirectory;
        }

        public TcpGatewayService Service { get; }

        public StubForwarder Forwarder { get; }

        public IForwardOutbox Outbox { get; }

        public EdgeMetrics Metrics { get; }

        public GatewayMetrics GatewayMetrics { get; }

        public static async Task<EdgeHarness> StartAsync(
            Action<StubForwarder>? configureForwarder = null,
            IForwardOutbox? outbox = null)
        {
            var options = new GatewayOptions
            {
                ListenPort = 0, // ephemeral: never collides with a running dev gateway
                MaxConnections = 32,
                MaxFrameBytes = 2048,
                IdleTimeout = TimeSpan.FromSeconds(30),
                DrainTimeout = TimeSpan.FromSeconds(5),
            };

            var metrics = new EdgeMetrics();
            var gatewayMetrics = new GatewayMetrics();
            var forwarder = new StubForwarder();
            configureForwarder?.Invoke(forwarder);

            string? directory = null;
            if (outbox is null)
            {
                directory = Directory.CreateTempSubdirectory("opstrax-edge-").FullName;
                outbox = new FileForwardOutbox(
                    new OutboxOptions { Path = directory, MaxEntries = 100 },
                    metrics, NullLogger.Instance);
            }

            var factory = new ForwardingConnectionHandlerFactory(
                new ProtocolRouter(new IProtocolAdapter[] { new Gt06Adapter(options.MaxFrameBytes) }),
                new ImeiAllowlist(new AllowlistOptions { Imeis = { AllowedImei } }, NullLogger.Instance),
                new InMemoryReplayGuard(serialModulus: 65_536),
                forwarder,
                outbox,
                options,
                new EdgeOptions { Egress = EgressMode.Https, Forward = { EdgeInstance = "test-edge" } },
                gatewayMetrics,
                metrics,
                NullLoggerFactory.Instance);

            var service = new TcpGatewayService(options, factory, gatewayMetrics, NullLoggerFactory.Instance);
            await service.StartAsync(CancellationToken.None);

            return new EdgeHarness(service, forwarder, outbox, metrics, gatewayMetrics, directory);
        }

        public async Task<TcpClient> ConnectAsync()
        {
            var client = new TcpClient { NoDelay = true };
            await client.ConnectAsync(IPAddress.Loopback, Service.BoundPort);
            return client;
        }

        public async ValueTask DisposeAsync()
        {
            await Service.StopAsync(CancellationToken.None);
            Service.Dispose();

            if (_outboxDirectory is not null)
            {
                try { Directory.Delete(_outboxDirectory, recursive: true); }
                catch (IOException) { /* best effort */ }
            }
        }
    }

    /// <summary>Records the exact payloads the edge produced and answers with a scripted outcome.</summary>
    private sealed class StubForwarder : IOpstraxForwarder
    {
        private readonly List<string> _delivered = new();

        public ForwardOutcome Outcome { get; set; } = ForwardOutcome.Delivered;

        public IReadOnlyList<string> Delivered
        {
            get { lock (_delivered) return _delivered.ToArray(); }
        }

        public Task<ForwardResult> SendAsync(string payloadJson, CancellationToken cancellationToken = default)
        {
            if (Outcome == ForwardOutcome.Delivered)
                lock (_delivered) _delivered.Add(payloadJson);

            return Task.FromResult(new ForwardResult(Outcome, Outcome == ForwardOutcome.Delivered ? 200 : 503, "stub"));
        }
    }

    /// <summary>An outbox whose storage is unavailable, for the "cannot be made durable" path.</summary>
    private sealed class RefusingOutbox : IForwardOutbox
    {
        public int Count => 0;

        public Task<bool> EnqueueAsync(string payloadJson, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<IReadOnlyList<OutboxEntry>> PeekAsync(int max, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OutboxEntry>>(Array.Empty<OutboxEntry>());

        public Task ReleaseAsync(string id, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<int> PurgeExpiredAsync(TimeSpan maxAge, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }

    // ── Socket + frame helpers ─────────────────────────────────────────────────

    /// <summary>A fix inside the ingest window, truncated to whole seconds as GT06 encodes it.</summary>
    private static DateTime RecentFix()
    {
        DateTime now = DateTime.UtcNow.AddMinutes(-1);
        return new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, DateTimeKind.Utc);
    }

    /// <summary>
    /// Polls until <paramref name="condition"/> holds. Needed for GT06 location frames, which the
    /// protocol does NOT acknowledge — so there is no wire event to synchronise on.
    /// </summary>
    private static async Task WaitForAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow + SocketTimeout;

        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(20);
        }

        Assert.Fail("The edge did not reach the expected state before the timeout.");
    }

    private static async Task<byte[]> ReadExactlyAsync(NetworkStream stream, int count)
    {
        using var cts = new CancellationTokenSource(SocketTimeout);
        var buffer = new byte[count];
        int offset = 0;

        while (offset < count)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset), cts.Token);
            if (read == 0) throw new EndOfStreamException($"Peer closed after {offset} of {count} expected bytes.");
            offset += read;
        }

        return buffer;
    }

    private static async Task<byte[]> ReadUntilClosedAsync(NetworkStream stream)
    {
        using var cts = new CancellationTokenSource(SocketTimeout);
        var result = new List<byte>();
        var buffer = new byte[64];

        try
        {
            while (true)
            {
                int read = await stream.ReadAsync(buffer, cts.Token);
                if (read == 0) break;
                result.AddRange(buffer.AsSpan(0, read).ToArray());
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException)
        {
            // Peer reset counts as closed.
        }

        return result.ToArray();
    }

    /// <summary>Reads whatever arrives within a short window; used to assert that nothing does.</summary>
    private static async Task<byte[]> ReadUntilTimeoutAsync(NetworkStream stream, TimeSpan window)
    {
        using var cts = new CancellationTokenSource(window);
        var result = new List<byte>();
        var buffer = new byte[64];

        try
        {
            while (true)
            {
                int read = await stream.ReadAsync(buffer, cts.Token);
                if (read == 0) break;
                result.AddRange(buffer.AsSpan(0, read).ToArray());
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException)
        {
        }

        return result.ToArray();
    }

    private static byte[] BuildLoginFrame(string imei, ushort serial = 1)
    {
        string padded = imei.PadLeft(16, '0');
        var crcRegion = new List<byte> { 0x0D, 0x01 };

        for (int i = 0; i < 8; i++)
        {
            int high = padded[i * 2] - '0';
            int low = padded[(i * 2) + 1] - '0';
            crcRegion.Add((byte)((high << 4) | low));
        }

        crcRegion.Add((byte)(serial >> 8));
        crcRegion.Add((byte)(serial & 0xFF));

        return Frame(crcRegion);
    }

    private static byte[] BuildLocationFrame(ushort serial, DateTime fixTime) =>
        Frame(BuildCrcRegion(0x12, GpsBlock(fixTime), serial));

    /// <summary>
    /// Builds a CRC-valid GT06 0x26 alarm frame: the GPS block, an LBS filler, and the five-byte
    /// status/alarm tail. Unlike a location frame, the protocol requires the server to acknowledge
    /// this one — which is what makes the durability contract observable on the wire.
    /// </summary>
    private static byte[] BuildAlarmFrame(ushort serial, DateTime fixTime, byte alarmCode)
    {
        List<byte> info = GpsBlock(fixTime);

        info.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00 }); // LBS block, not decoded here
        info.Add(0x02);        // terminal info: ignition on
        info.Add(0x06);        // voltage level (coarse 0..6)
        info.Add(0x04);        // GSM signal (0..4)
        info.Add(alarmCode);   // 0x01 = SOS
        info.Add(0x01);        // language

        return Frame(BuildCrcRegion(0x26, info, serial));
    }

    /// <summary>The 18-byte GT06 GPS information block: date, satellites, lat/lng, speed, course.</summary>
    private static List<byte> GpsBlock(DateTime fixTime, double lat = 38.9072, double lng = 77.0369, byte speedKph = 60)
    {
        var info = new List<byte>
        {
            (byte)(fixTime.Year - 2000), (byte)fixTime.Month, (byte)fixTime.Day,
            (byte)fixTime.Hour, (byte)fixTime.Minute, (byte)fixTime.Second,
            0x09, // quantity: low nibble = satellites in use
        };

        AppendBigEndian(info, (uint)Math.Round(Math.Abs(lat) * 1_800_000.0));
        AppendBigEndian(info, (uint)Math.Round(Math.Abs(lng) * 1_800_000.0));
        info.Add(speedKph);

        ushort courseStatus = (1 << 12) | (1 << 11); // positioned, northern hemisphere
        info.Add((byte)(courseStatus >> 8));
        info.Add((byte)(courseStatus & 0xFF));

        return info;
    }

    private static List<byte> BuildCrcRegion(byte protocolNumber, List<byte> info, ushort serial)
    {
        int packetLength = 1 + info.Count + 2 + 2; // protocol + info + serial + crc
        var crcRegion = new List<byte> { (byte)packetLength, protocolNumber };
        crcRegion.AddRange(info);
        crcRegion.Add((byte)(serial >> 8));
        crcRegion.Add((byte)(serial & 0xFF));
        return crcRegion;
    }

    private static byte[] Frame(List<byte> crcRegion)
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

    private static void AppendBigEndian(List<byte> destination, uint value)
    {
        destination.Add((byte)((value >> 24) & 0xFF));
        destination.Add((byte)((value >> 16) & 0xFF));
        destination.Add((byte)((value >> 8) & 0xFF));
        destination.Add((byte)(value & 0xFF));
    }
}
