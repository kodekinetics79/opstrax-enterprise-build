using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
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
using Xunit.Abstractions;

namespace Opstrax.Telematics.IntegrationTests;

/// <summary>
/// Certification load tests. Opt-in: they bind thousands of real loopback sockets and are not part
/// of the ordinary suite. Enable with <c>OPSTRAX_CERT_LOAD=1</c>.
/// </summary>
[Collection(TelematicsObservabilityCollection.Name)]
public sealed class CertificationLoadTests
{
    private readonly ITestOutputHelper _out;

    public CertificationLoadTests(ITestOutputHelper output) => _out = output;

    private static bool Enabled => Environment.GetEnvironmentVariable("OPSTRAX_CERT_LOAD") == "1";

    [Fact] public Task Load_00100_devices() => RunAsync(100);
    [Fact] public Task Load_01000_devices() => RunAsync(1_000);
    [Fact] public Task Load_05000_devices() => RunAsync(5_000);
    [Fact] public Task Load_10000_devices() => RunAsync(10_000);

    private async Task RunAsync(int deviceCount)
    {
        if (!Enabled)
        {
            // Gated rather than skipped: adding a Skippable package to the candidate's project just
            // to run a certification harness would change the artifact under review.
            _out.WriteLine($"SKIPPED {deviceCount}-device load test (set OPSTRAX_CERT_LOAD=1 to run).");
            return;
        }

        var options = new GatewayOptions
        {
            ListenPort = 0,
            MaxConnections = deviceCount + 256,
            MaxInFlightPerConnection = 16,
            MaxFrameBytes = 2048,
            IdleTimeout = TimeSpan.FromMinutes(5),
            DrainTimeout = TimeSpan.FromSeconds(30),
            ReadBufferBytes = 1024,
        };

        var seed = new List<KeyValuePair<string, ResolvedDeviceTrust>>(deviceCount);
        for (int i = 0; i < deviceCount; i++)
            seed.Add(new(Imei(i), new ResolvedDeviceTrust(
                new ResolvedDeviceOwner(Tenant(i), 1000 + i, Device(i), 5000 + i,
                    DeviceLifecycleState.Online, $"vault://cert/{i}"),
                DeviceTrustPolicy.ImeiAllowlistBaseline(), CredentialMaterial.None)));

        var bus = new InMemoryEventBackbone();
        var metrics = new GatewayMetrics();
        var sessions = new ActiveDeviceSessionRegistry();
        var replay = new InMemoryReplayGuard(serialModulus: 65_536);
        var plausibility = new FixPlausibilityGuard();

        var svc = new TcpGatewayService(
            options, bus, new InMemoryDeviceRegistry(seed),
            new DefaultDeviceAuthenticator(new DenyAll()), replay,
            new InMemoryPositionProjectionStore(), new Gt06Adapter(options.MaxFrameBytes),
            new InMemoryStoreAndForwardBuffer(), metrics, NullLoggerFactory.Instance,
            sessions, plausibility);

        await svc.StartAsync(CancellationToken.None);

        var process = Process.GetCurrentProcess();
        long rssBefore = process.WorkingSet64;
        long heapBefore = GC.GetTotalMemory(forceFullCollection: true);
        int[] gcBefore = { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) };
        TimeSpan cpuBefore = process.TotalProcessorTime;
        var wall = Stopwatch.StartNew();

        var clients = new TcpClient[deviceCount];
        var connectErrors = new System.Collections.Concurrent.ConcurrentDictionary<string, int>();
        var loginAckMicros = new double[deviceCount];
        int connectFailures = 0, loginFailures = 0;

        // ── Connect + login. Paced in waves: the listen backlog is 128 on this host, so an
        //    unpaced 10 000-way connect storm measures the OS accept queue, not the gateway.
        const int wave = 250;
        for (int start = 0; start < deviceCount; start += wave)
        {
            int end = Math.Min(start + wave, deviceCount);
            await Task.WhenAll(Enumerable.Range(start, end - start).Select(async i =>
            {
                try
                {
                    var c = new TcpClient { NoDelay = true };
                    await c.ConnectAsync(IPAddress.Loopback, svc.BoundPort);
                    clients[i] = c;

                    byte[] login = Login(Imei(i), 1);
                    var sw = Stopwatch.StartNew();
                    await c.GetStream().WriteAsync(login);
                    byte[] ack = await ReadExactly(c.GetStream(), 10, TimeSpan.FromSeconds(30));
                    sw.Stop();
                    loginAckMicros[i] = sw.Elapsed.TotalMicroseconds;
                    if (ack.Length != 10) Interlocked.Increment(ref loginFailures);
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref connectFailures);
                    connectErrors.TryAdd(ex is SocketException se
                        ? $"SocketException/{se.SocketErrorCode}"
                        : ex.GetType().Name, 0);
                }
            }));
        }
        TimeSpan connectPhase = wall.Elapsed;
        int connected = clients.Count(c => c is { Connected: true });

        // ── Steady state: each device sends location frames, then a heartbeat whose ACK is timed.
        const int locationsPerDevice = 5;
        var heartbeatAckMicros = new List<double>(deviceCount);
        var sendWall = Stopwatch.StartNew();

        for (int start = 0; start < deviceCount; start += wave)
        {
            int end = Math.Min(start + wave, deviceCount);
            double[] batch = await Task.WhenAll(Enumerable.Range(start, end - start).Select(async i =>
            {
                TcpClient? c = clients[i];
                if (c is not { Connected: true }) return -1d;
                try
                {
                    NetworkStream s = c.GetStream();
                    for (int n = 0; n < locationsPerDevice; n++)
                        await s.WriteAsync(Location((ushort)(2 + n), Fix.AddSeconds(n * 30), 10 + (i * 0.0001), 20));

                    byte[] hb = Heartbeat((ushort)(2 + locationsPerDevice));
                    var sw = Stopwatch.StartNew();
                    await s.WriteAsync(hb);
                    await ReadExactly(s, 10, TimeSpan.FromSeconds(30));
                    sw.Stop();
                    return sw.Elapsed.TotalMicroseconds;
                }
                catch (Exception) { return -1d; }
            }));
            heartbeatAckMicros.AddRange(batch.Where(v => v >= 0));
        }
        sendWall.Stop();

        TimeSpan cpuUsed = process.TotalProcessorTime - cpuBefore;
        process.Refresh();
        long rssPeak = process.WorkingSet64;
        long heapPeak = GC.GetTotalMemory(forceFullCollection: false);
        int[] gcAfter = { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) };
        int replayDevices = replay.TrackedDeviceCount;
        long activeAtPeak = metrics.ActiveConnections;
        int sessionsAtPeak = sessions.ActiveSessionCount;

        // ── Teardown and cleanup verification.
        foreach (TcpClient? c in clients) { try { c?.Close(); c?.Dispose(); } catch { } }
        var drain = Stopwatch.StartNew();
        while (drain.Elapsed < TimeSpan.FromSeconds(60) &&
               (metrics.ActiveConnections > 0 || sessions.ActiveSessionCount > 0))
            await Task.Delay(50);
        drain.Stop();

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();

        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        process.Refresh();
        long rssAfter = process.WorkingSet64;
        long heapAfter = GC.GetTotalMemory(forceFullCollection: true);

        long expectedFrames = (long)connected * (1 + locationsPerDevice + 1);
        double sendSeconds = Math.Max(sendWall.Elapsed.TotalSeconds, 0.000001);

        _out.WriteLine($"=== LOAD {deviceCount} devices ===");
        _out.WriteLine($"connect+login wall      : {connectPhase.TotalSeconds:F2} s");
        _out.WriteLine($"connections accepted    : {metrics.ConnectionsAccepted}");
        _out.WriteLine($"connections established : {connected}");
        _out.WriteLine($"connect failures        : {connectFailures}");
        _out.WriteLine($"connect error kinds     : {(connectErrors.IsEmpty ? "none" : string.Join(", ", connectErrors.Keys))}");
        _out.WriteLine($"login failures          : {loginFailures}");
        _out.WriteLine($"quota rejections        : {metrics.ConnectionsRejectedQuota}");
        _out.WriteLine($"active at peak          : {activeAtPeak}");
        _out.WriteLine($"sessions at peak        : {sessionsAtPeak}");
        _out.WriteLine($"frames received         : {metrics.FramesReceived} (expected {expectedFrames})");
        _out.WriteLine($"frames decoded          : {metrics.FramesDecoded}");
        _out.WriteLine($"CRC failures            : {metrics.CrcFailures}");
        _out.WriteLine($"malformed frames        : {metrics.MalformedFrames}");
        _out.WriteLine($"logins / locations / hb : {metrics.LoginPackets} / {metrics.LocationPackets} / {metrics.HeartbeatPackets}");
        _out.WriteLine($"ACKs sent               : {metrics.AcksSent}");
        _out.WriteLine($"events published        : {metrics.EventsPublished}");
        _out.WriteLine($"frames rejected         : {metrics.FramesRejected}");
        _out.WriteLine($"identity violations     : {metrics.SessionIdentityViolations}");
        _out.WriteLine($"sessions displaced      : {metrics.DuplicateSessionsDisplaced}");
        _out.WriteLine($"teleport / imposs speed : {metrics.TeleportsSuspected} / {metrics.ImpossibleSpeeds}");
        _out.WriteLine($"steady-state throughput : {expectedFrames / sendSeconds:F0} frames/s over {sendSeconds:F2} s");
        _out.WriteLine($"login  ACK p50/p95/p99  : {P(loginAckMicros.Where(v => v > 0).ToArray())}");
        _out.WriteLine($"hbeat  ACK p50/p95/p99  : {P(heartbeatAckMicros.ToArray())}");
        _out.WriteLine($"CPU (process, this test): {cpuUsed.TotalSeconds:F2} s across {Environment.ProcessorCount} cores");
        _out.WriteLine($"RSS before/peak/after   : {Mb(rssBefore)} / {Mb(rssPeak)} / {Mb(rssAfter)}");
        _out.WriteLine($"managed heap b/p/a      : {Mb(heapBefore)} / {Mb(heapPeak)} / {Mb(heapAfter)}");
        _out.WriteLine($"GC gen0/1/2             : {gcAfter[0] - gcBefore[0]} / {gcAfter[1] - gcBefore[1]} / {gcAfter[2] - gcBefore[2]}");
        _out.WriteLine($"replay devices tracked  : {replayDevices}");
        _out.WriteLine($"drain to zero           : {drain.Elapsed.TotalSeconds:F2} s");
        _out.WriteLine($"active/sessions AFTER   : {metrics.ActiveConnections} / {sessions.ActiveSessionCount}");

        // ── Certification assertions.
        Assert.Equal(0, loginFailures);
        Assert.Equal(connected, metrics.LoginPackets);
        Assert.Equal(0L, metrics.CrcFailures);
        Assert.Equal(0L, metrics.MalformedFrames);
        Assert.Equal(0L, metrics.SessionIdentityViolations);
        Assert.Equal(0L, metrics.FramesRejected);
        Assert.Equal(expectedFrames, metrics.FramesReceived);
        Assert.Equal(expectedFrames, metrics.FramesDecoded);
        Assert.Equal((long)connected * locationsPerDevice, metrics.LocationPackets);
        Assert.Equal((long)connected * 2, metrics.AcksSent);            // login + heartbeat
        // Heartbeats are published as lifecycle evidence alongside locations, so the expected
        // event count is locations + one heartbeat per device, not locations alone.
        Assert.Equal((long)connected * (locationsPerDevice + 1), metrics.EventsPublished);
        Assert.Equal(connected, replayDevices);                          // one replay entry per device
        Assert.Equal(0L, metrics.ActiveConnections);                     // full cleanup
        Assert.Equal(0, sessions.ActiveSessionCount);
    }

    private static string P(double[] v)
    {
        if (v.Length == 0) return "n/a";
        Array.Sort(v);
        double Q(double q) => v[Math.Clamp((int)Math.Ceiling(q * v.Length) - 1, 0, v.Length - 1)];
        return $"{Q(0.50) / 1000.0:F2} / {Q(0.95) / 1000.0:F2} / {Q(0.99) / 1000.0:F2} ms";
    }

    private static string Mb(long bytes) => $"{bytes / 1024.0 / 1024.0:F0} MB";

    private static string Imei(int i) => $"86812{i:D10}";
    private static string Device(int i) => $"load-dev-{i:D5}";
    private static Guid Tenant(int i) => new($"{i:D8}-0000-4000-8000-000000000000");
    private static readonly DateTime Fix = new(2024, 1, 15, 10, 20, 30, DateTimeKind.Utc);

    private static byte[] Login(string imei, ushort serial)
    {
        string p = imei.PadLeft(16, '0');
        var id = new byte[8];
        for (int i = 0; i < 8; i++) id[i] = (byte)(((p[i * 2] - '0') << 4) | (p[(i * 2) + 1] - '0'));
        var r = new List<byte> { 0x0D, 0x01 };
        r.AddRange(id);
        r.Add((byte)(serial >> 8)); r.Add((byte)(serial & 0xFF));
        return Wrap(r);
    }

    private static byte[] Heartbeat(ushort serial)
    {
        var r = new List<byte> { 0x0A, 0x13, 0x46, 0x05, 0x04, 0x00, 0x02 };
        r.Add((byte)(serial >> 8)); r.Add((byte)(serial & 0xFF));
        return Wrap(r);
    }

    private static byte[] Location(ushort serial, DateTime t, double lat, double lng)
    {
        var info = new List<byte>
        { (byte)(t.Year - 2000), (byte)t.Month, (byte)t.Day, (byte)t.Hour, (byte)t.Minute, (byte)t.Second, 0x09 };
        void Be(uint v) { info.Add((byte)(v >> 24)); info.Add((byte)(v >> 16)); info.Add((byte)(v >> 8)); info.Add((byte)v); }
        Be((uint)Math.Round(Math.Abs(lat) * 1_800_000));
        Be((uint)Math.Round(Math.Abs(lng) * 1_800_000));
        info.Add(60);
        const ushort cs = (1 << 12) | (1 << 10);
        info.Add((byte)(cs >> 8)); info.Add((byte)(cs & 0xFF));

        var r = new List<byte> { (byte)(1 + info.Count + 4), 0x12 };
        r.AddRange(info);
        r.Add((byte)(serial >> 8)); r.Add((byte)(serial & 0xFF));
        return Wrap(r);
    }

    private static byte[] Wrap(List<byte> region)
    {
        ushort crc = Gt06Adapter.Crc16Itu(region.ToArray());
        var f = new List<byte> { 0x78, 0x78 };
        f.AddRange(region);
        f.Add((byte)(crc >> 8)); f.Add((byte)(crc & 0xFF)); f.Add(0x0D); f.Add(0x0A);
        return f.ToArray();
    }

    private static async Task<byte[]> ReadExactly(NetworkStream s, int count, TimeSpan timeout)
    {
        var buf = new byte[count];
        int read = 0;
        using var cts = new CancellationTokenSource(timeout);
        while (read < count)
        {
            int n = await s.ReadAsync(buf.AsMemory(read), cts.Token);
            if (n == 0) break;
            read += n;
        }
        return read == count ? buf : Array.Empty<byte>();
    }

    private sealed class DenyAll : ICredentialKeyResolver
    {
        public ValueTask<byte[]?> ResolveHmacKeyAsync(CredentialMaterial c, CancellationToken t = default) =>
            ValueTask.FromResult<byte[]?>(null);
    }
}
