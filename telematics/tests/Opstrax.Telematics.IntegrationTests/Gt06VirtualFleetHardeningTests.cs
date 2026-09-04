using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Opstrax.Telematics.Contracts.Adapters;
using Opstrax.Telematics.Gateway;
using Opstrax.Telematics.Gateway.Edge;
using Opstrax.Telematics.Gateway.Forwarding;
using Opstrax.Telematics.Gateway.Quality;
using Opstrax.Telematics.Gateway.Security;
using Opstrax.Telematics.Gateway.Security.Replay;
using Opstrax.Telematics.Protocols.Gt06;

namespace Opstrax.Telematics.IntegrationTests;

/// <summary>
/// Pre-hardware certification tests that drive independent virtual GT06 trackers through the real
/// TCP gateway and forwarding edge. These tests deliberately build the GT06 wire format here from
/// the documented bit table instead of calling production packet-building code, so an encoder and
/// decoder cannot accidentally agree on the same defect.
/// </summary>
public sealed class Gt06VirtualFleetHardeningTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    // GT06 course/status word. These constants are copied from the vendor bit table, not from
    // Gt06Adapter: bit 10 = latitude North, bit 11 = longitude West, bit 12 = positioned.
    private const ushort LatitudeNorth = 1 << 10;
    private const ushort LongitudeWest = 1 << 11;
    private const ushort Positioned = 1 << 12;

    [Fact]
    public async Task FourIndependentTrackers_PreserveAllCoordinateQuadrantsEndToEnd()
    {
        (string Imei, double Lat, double Lng)[] cases =
        {
            ("868120300000101",  35.6762,  139.6503), // Tokyo: North / East
            ("868120300000102",  40.7128,  -74.0060), // New York: North / West
            ("868120300000103", -33.8688,  151.2093), // Sydney: South / East
            ("868120300000104", -34.6037,  -58.3816), // Buenos Aires: South / West
        };

        await using VirtualFleetHarness harness = await VirtualFleetHarness.StartAsync(
            cases.Select(c => c.Imei).ToArray(), maxConnections: 16);

        await Task.WhenAll(cases.Select(async testCase =>
        {
            using TcpClient client = await harness.ConnectAsync();
            NetworkStream stream = client.GetStream();

            await stream.WriteAsync(BuildLoginFrame(testCase.Imei, serial: 1));
            await ReadExactlyAsync(stream, 10);
            await stream.WriteAsync(BuildLocationFrame(
                serial: 2,
                fixTime: RecentFix(),
                latitude: testCase.Lat,
                longitude: testCase.Lng));
        }));

        await WaitForAsync(() => harness.Forwarder.Delivered.Count == cases.Length);

        Dictionary<string, (double Lat, double Lng)> delivered = harness.Forwarder.Delivered
            .Select(ParsePosition)
            .ToDictionary(x => x.Imei, x => (x.Lat, x.Lng), StringComparer.Ordinal);

        Assert.Equal(cases.Length, delivered.Count);
        foreach ((string imei, double lat, double lng) in cases)
        {
            Assert.True(delivered.TryGetValue(imei, out var actual), $"No forwarded fix for {imei}.");
            Assert.Equal(lat, actual.Lat, precision: 4);
            Assert.Equal(lng, actual.Lng, precision: 4);
        }

        Assert.Equal(0, harness.GatewayMetrics.SessionIdentityViolations);
        Assert.Equal(0, harness.GatewayMetrics.DuplicateSessionsDisplaced);
    }

    [Fact]
    public async Task OneHundredConcurrentTrackers_StayIsolatedAcrossRealTcpSessions()
    {
        string[] imeis = Enumerable.Range(1, 100)
            .Select(i => (868120300100000L + i).ToString("D15"))
            .ToArray();

        await using VirtualFleetHarness harness = await VirtualFleetHarness.StartAsync(
            imeis, maxConnections: 128);

        DateTime fixTime = RecentFix();
        await Task.WhenAll(imeis.Select((imei, index) => SendOneAsync(
            harness,
            imei,
            latitude: 37.0 + (index * 0.001),
            longitude: -122.0 - (index * 0.001),
            fixTime)));

        await WaitForAsync(() => harness.Forwarder.Delivered.Count == imeis.Length);

        string[] payloads = harness.Forwarder.Delivered.ToArray();
        Assert.Equal(100, payloads.Length);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string payload in payloads)
        {
            (string imei, double lat, double lng) = ParsePosition(payload);
            Assert.Contains(imei, imeis);
            Assert.True(seen.Add(imei), $"Device {imei} produced more than one forwarded observation.");
            Assert.InRange(lat, 37.0, 37.0991);
            Assert.InRange(lng, -122.0991, -122.0);

            // The public edge is a translator, never a tenant/vehicle authority.
            foreach (string forbidden in new[] { "tenantId", "companyId", "vehicleId", "driverId" })
                Assert.DoesNotContain(forbidden, payload, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(100, seen.Count);
        Assert.Equal(0, harness.GatewayMetrics.SessionIdentityViolations);
        Assert.Equal(0, harness.GatewayMetrics.DuplicateSessionsDisplaced);
        Assert.Equal(100, harness.Metrics.ObservationsDelivered);
    }

    private static async Task SendOneAsync(
        VirtualFleetHarness harness,
        string imei,
        double latitude,
        double longitude,
        DateTime fixTime)
    {
        using TcpClient client = await harness.ConnectAsync();
        NetworkStream stream = client.GetStream();

        await stream.WriteAsync(BuildLoginFrame(imei, serial: 1));
        byte[] ack = await ReadExactlyAsync(stream, 10);
        Assert.Equal(0x78, ack[0]);
        Assert.Equal(0x78, ack[1]);

        await stream.WriteAsync(BuildLocationFrame(
            serial: 2,
            fixTime,
            latitude,
            longitude));
    }

    private static (string Imei, double Lat, double Lng) ParsePosition(string payload)
    {
        using JsonDocument json = JsonDocument.Parse(payload);
        JsonElement root = json.RootElement;
        return (
            root.GetProperty("imei").GetString()!,
            root.GetProperty("lat").GetDouble(),
            root.GetProperty("lng").GetDouble());
    }

    private static DateTime RecentFix()
    {
        DateTime now = DateTime.UtcNow.AddMinutes(-1);
        return new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, DateTimeKind.Utc);
    }

    private static byte[] BuildLoginFrame(string imei, ushort serial)
    {
        Assert.Matches("^[0-9]{15}$", imei);
        string padded = imei.PadLeft(16, '0');
        var information = new byte[8];
        for (int i = 0; i < information.Length; i++)
        {
            int high = padded[i * 2] - '0';
            int low = padded[(i * 2) + 1] - '0';
            information[i] = (byte)((high << 4) | low);
        }

        return BuildFrame(0x01, information, serial);
    }

    private static byte[] BuildLocationFrame(
        ushort serial,
        DateTime fixTime,
        double latitude,
        double longitude,
        byte speedKph = 60,
        ushort course = 90)
    {
        if (Math.Abs(latitude) > 90) throw new ArgumentOutOfRangeException(nameof(latitude));
        if (Math.Abs(longitude) > 180) throw new ArgumentOutOfRangeException(nameof(longitude));
        if (course > 359) throw new ArgumentOutOfRangeException(nameof(course));

        var info = new List<byte>
        {
            (byte)(fixTime.Year - 2000), (byte)fixTime.Month, (byte)fixTime.Day,
            (byte)fixTime.Hour, (byte)fixTime.Minute, (byte)fixTime.Second,
            0x09,
        };

        AppendBigEndian(info, (uint)Math.Round(Math.Abs(latitude) * 1_800_000.0));
        AppendBigEndian(info, (uint)Math.Round(Math.Abs(longitude) * 1_800_000.0));
        info.Add(speedKph);

        ushort courseStatus = (ushort)(Positioned | (course & 0x03FF));
        if (latitude >= 0) courseStatus |= LatitudeNorth;
        if (longitude < 0) courseStatus |= LongitudeWest;
        info.Add((byte)(courseStatus >> 8));
        info.Add((byte)(courseStatus & 0xFF));

        return BuildFrame(0x12, info.ToArray(), serial);
    }

    private static byte[] BuildFrame(byte protocol, byte[] information, ushort serial)
    {
        int packetLength = 1 + information.Length + 2 + 2;
        if (packetLength > byte.MaxValue) throw new ArgumentOutOfRangeException(nameof(information));

        var crcRegion = new List<byte>(packetLength - 1)
        {
            (byte)packetLength,
            protocol,
        };
        crcRegion.AddRange(information);
        crcRegion.Add((byte)(serial >> 8));
        crcRegion.Add((byte)(serial & 0xFF));

        ushort crc = IndependentCrc16X25(crcRegion);
        var frame = new List<byte>(crcRegion.Count + 6) { 0x78, 0x78 };
        frame.AddRange(crcRegion);
        frame.Add((byte)(crc >> 8));
        frame.Add((byte)(crc & 0xFF));
        frame.Add(0x0D);
        frame.Add(0x0A);
        return frame.ToArray();
    }

    // Independent implementation of CRC-16/X-25 (GT06 CRC-ITU). Do not call Gt06Adapter.Crc16Itu
    // here: this test is intended to catch a production CRC regression, not repeat it.
    private static ushort IndependentCrc16X25(IEnumerable<byte> bytes)
    {
        ushort crc = 0xFFFF;
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
                crc = (ushort)(((crc & 1) != 0) ? ((crc >> 1) ^ 0x8408) : (crc >> 1));
        }
        return (ushort)~crc;
    }

    private static void AppendBigEndian(List<byte> destination, uint value)
    {
        destination.Add((byte)((value >> 24) & 0xFF));
        destination.Add((byte)((value >> 16) & 0xFF));
        destination.Add((byte)((value >> 8) & 0xFF));
        destination.Add((byte)(value & 0xFF));
    }

    private static async Task<byte[]> ReadExactlyAsync(NetworkStream stream, int count)
    {
        using var cts = new CancellationTokenSource(Timeout);
        byte[] buffer = new byte[count];
        int offset = 0;
        while (offset < count)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset), cts.Token);
            if (read == 0) throw new EndOfStreamException($"Peer closed after {offset}/{count} bytes.");
            offset += read;
        }
        return buffer;
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(20);
        }
        Assert.Fail("Virtual-fleet gateway did not reach the expected state before timeout.");
    }

    private sealed class VirtualFleetHarness : IAsyncDisposable
    {
        private readonly string _outboxDirectory;

        private VirtualFleetHarness(
            TcpGatewayService service,
            RecordingForwarder forwarder,
            EdgeMetrics metrics,
            GatewayMetrics gatewayMetrics,
            string outboxDirectory)
        {
            Service = service;
            Forwarder = forwarder;
            Metrics = metrics;
            GatewayMetrics = gatewayMetrics;
            _outboxDirectory = outboxDirectory;
        }

        public TcpGatewayService Service { get; }
        public RecordingForwarder Forwarder { get; }
        public EdgeMetrics Metrics { get; }
        public GatewayMetrics GatewayMetrics { get; }

        public static async Task<VirtualFleetHarness> StartAsync(
            IReadOnlyCollection<string> allowlistedImeis,
            int maxConnections)
        {
            var gatewayOptions = new GatewayOptions
            {
                ListenPort = 0,
                MaxConnections = maxConnections,
                MaxFrameBytes = 2048,
                IdleTimeout = TimeSpan.FromSeconds(30),
                DrainTimeout = TimeSpan.FromSeconds(5),
            };

            var allowlistOptions = new AllowlistOptions();
            foreach (string imei in allowlistedImeis)
                allowlistOptions.Imeis.Add(imei);

            var edgeMetrics = new EdgeMetrics();
            var gatewayMetrics = new GatewayMetrics();
            var forwarder = new RecordingForwarder();
            string outboxDirectory = Directory.CreateTempSubdirectory("opstrax-virtual-fleet-").FullName;
            var outbox = new FileForwardOutbox(
                new OutboxOptions { Path = outboxDirectory, MaxEntries = 1_000 },
                edgeMetrics,
                NullLogger.Instance,
                RandomNumberGenerator.GetBytes(32));

            var factory = new ForwardingConnectionHandlerFactory(
                new ProtocolRouter(new IProtocolAdapter[] { new Gt06Adapter(gatewayOptions.MaxFrameBytes) }),
                new ImeiAllowlist(allowlistOptions, NullLogger.Instance),
                new InMemoryReplayGuard(serialModulus: 65_536),
                forwarder,
                outbox,
                gatewayOptions,
                new EdgeOptions { Egress = EgressMode.Https, Forward = { EdgeInstance = "virtual-fleet-gate" } },
                new ActiveDeviceSessionRegistry(),
                new FixPlausibilityGuard(),
                gatewayMetrics,
                edgeMetrics,
                NullLoggerFactory.Instance);

            var service = new TcpGatewayService(
                gatewayOptions,
                factory,
                gatewayMetrics,
                NullLoggerFactory.Instance);
            await service.StartAsync(CancellationToken.None);

            return new VirtualFleetHarness(service, forwarder, edgeMetrics, gatewayMetrics, outboxDirectory);
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
            try { Directory.Delete(_outboxDirectory, recursive: true); }
            catch (IOException) { }
        }
    }

    private sealed class RecordingForwarder : IOpstraxForwarder
    {
        private readonly List<string> _delivered = new();

        public IReadOnlyList<string> Delivered
        {
            get { lock (_delivered) return _delivered.ToArray(); }
        }

        public Task<ForwardResult> SendAsync(string payloadJson, CancellationToken cancellationToken = default)
        {
            lock (_delivered) _delivered.Add(payloadJson);
            return Task.FromResult(new ForwardResult(ForwardOutcome.Delivered, 200, "virtual-fleet"));
        }
    }
}
