using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Opstrax.Telematics.Gateway.Edge;
using Opstrax.Telematics.Gateway.Forwarding;

namespace Opstrax.Telematics.IntegrationTests;

/// <summary>
/// Covers HTTPS delivery to OpsTrax: that the signature the edge produces is byte-for-byte the one
/// the server recomputes, and that every response is classified into the right retry decision.
/// </summary>
public sealed class EdgeForwarderTests
{
    private const string Secret = "ZmFrZS1nYXRld2F5LXNlY3JldC0zMi1ieXRlcy1sb25nIQ==";
    private const string GatewayId = "khalid-gw-1";
    private const string Payload = """{"imei":"862464068456321","lat":38.9072,"lng":-77.0369}""";

    private static readonly DateTimeOffset Now = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);

    private static ForwardOptions Options() => new()
    {
        BaseUrl = "https://opstrax.example.com",
        GatewayId = GatewayId,
        Secret = Secret,
    };

    private static (HttpsOpstraxForwarder Forwarder, RecordingHandler Handler) Build(
        HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new RecordingHandler(status);
        var forwarder = new HttpsOpstraxForwarder(
            Options(), NullLogger.Instance, new HttpClient(handler), () => Now);

        return (forwarder, handler);
    }

    // ── Signing ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Signature_MatchesWhatTheServerRecomputesOverTheReceivedBytes()
    {
        (HttpsOpstraxForwarder forwarder, RecordingHandler handler) = Build();
        using (forwarder)
        {
            await forwarder.SendAsync(Payload);
        }

        string timestamp = handler.Header("X-Gateway-Timestamp")!;
        Assert.Equal(Now.ToUnixTimeSeconds().ToString(), timestamp);

        // Exactly the server's computation: HMAC-SHA256(secret, "{timestamp}.{rawBody}") over the
        // bytes it actually received, compared as lowercase hex.
        byte[] expected = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(Secret),
            Encoding.UTF8.GetBytes($"{timestamp}.{handler.Body}"));

        Assert.Equal(Convert.ToHexString(expected).ToLowerInvariant(), handler.Header("X-Gateway-Signature"));
    }

    [Fact]
    public async Task Body_IsTransmittedVerbatim_SoTheSignatureStaysValid()
    {
        (HttpsOpstraxForwarder forwarder, RecordingHandler handler) = Build();
        using (forwarder)
        {
            await forwarder.SendAsync(Payload);
        }

        Assert.Equal(Payload, handler.Body);
    }

    [Fact]
    public async Task Identifier_TravelsInTheSignedBodyOnly_NeverInAnUnsignedHeader()
    {
        // X-Device-IMEI overrides the body server-side but sits outside the HMAC, so using it would
        // let anyone able to modify the request in flight redirect a fix onto another vehicle.
        (HttpsOpstraxForwarder forwarder, RecordingHandler handler) = Build();
        using (forwarder)
        {
            await forwarder.SendAsync(Payload);
        }

        Assert.Null(handler.Header("X-Device-IMEI"));
        Assert.Contains("862464068456321", handler.Body);
        Assert.Equal(GatewayId, handler.Header("X-Gateway-Id"));
    }

    [Fact]
    public async Task Endpoint_IsTheTrustedGatewayIngestRoute()
    {
        (HttpsOpstraxForwarder forwarder, RecordingHandler handler) = Build();
        using (forwarder)
        {
            await forwarder.SendAsync(Payload);
        }

        Assert.Equal("https://opstrax.example.com/api/telemetry/gps-ingest", handler.Uri?.ToString());
    }

    // ── Response classification ────────────────────────────────────────────────

    // Expressed as names because ForwardOutcome is internal to the gateway assembly and an xUnit
    // theory parameter has to be at least as accessible as the (public) test class.
    [Theory]
    [InlineData(HttpStatusCode.OK, "Delivered")]
    [InlineData(HttpStatusCode.Accepted, "Delivered")]
    [InlineData(HttpStatusCode.Conflict, "Delivered")]           // durable replay ledger already holds it
    [InlineData(HttpStatusCode.Unauthorized, "Retryable")]       // secret/clock/gateway row: operator-fixable
    [InlineData(HttpStatusCode.ServiceUnavailable, "Retryable")] // ingest failing closed on schema topology
    [InlineData(HttpStatusCode.TooManyRequests, "Retryable")]
    [InlineData(HttpStatusCode.InternalServerError, "Retryable")]
    [InlineData(HttpStatusCode.BadRequest, "Rejected")]
    [InlineData(HttpStatusCode.Forbidden, "Rejected")]           // wrong tenant / quarantined
    [InlineData(HttpStatusCode.NotFound, "Rejected")]            // device not provisioned
    public async Task EveryStatus_LandsOnTheRightRetryDecision(HttpStatusCode status, string expected)
    {
        (HttpsOpstraxForwarder forwarder, _) = Build(status);
        using (forwarder)
        {
            ForwardResult result = await forwarder.SendAsync(Payload);
            Assert.Equal(expected, result.Outcome.ToString());
        }
    }

    [Fact]
    public async Task TransportFault_IsRetryable_BecauseThePayloadIsStillGood()
    {
        var handler = new RecordingHandler(new HttpRequestException("connection refused"));
        using var forwarder = new HttpsOpstraxForwarder(
            Options(), NullLogger.Instance, new HttpClient(handler), () => Now);

        ForwardResult result = await forwarder.SendAsync(Payload);

        Assert.Equal(ForwardOutcome.Retryable, result.Outcome);
        Assert.Null(result.StatusCode);
    }

    [Fact]
    public async Task OversizePayload_IsRejectedBeforeItCanEverReachTheOutbox()
    {
        (HttpsOpstraxForwarder forwarder, RecordingHandler handler) = Build();
        using (forwarder)
        {
            ForwardResult result = await forwarder.SendAsync(new string('x', 40_000));

            Assert.Equal(ForwardOutcome.Rejected, result.Outcome);
            Assert.Equal(0, handler.Requests);
        }
    }

    // ── Configuration validation ───────────────────────────────────────────────

    [Fact]
    public void PlainHttp_IsRefused_BecauseTheHmacAuthenticatesButDoesNotConceal()
    {
        ForwardOptions options = Options();
        options.BaseUrl = "http://opstrax.example.com";

        Assert.Contains("https", HttpsOpstraxForwarder.Validate(options), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShortSecret_IsRefused_BecauseTheServerWouldOnlyEver503()
    {
        ForwardOptions options = Options();
        options.Secret = "too-short";

        Assert.Contains("32 characters", HttpsOpstraxForwarder.Validate(options));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-url")]
    public void UnusableBaseUrl_IsRefused(string baseUrl)
    {
        ForwardOptions options = Options();
        options.BaseUrl = baseUrl;

        Assert.NotNull(HttpsOpstraxForwarder.Validate(options));
    }

    [Fact]
    public void MissingGatewayId_IsRefused()
    {
        ForwardOptions options = Options();
        options.GatewayId = "";

        Assert.Contains("GatewayId", HttpsOpstraxForwarder.Validate(options));
    }

    [Fact]
    public void ValidConfiguration_Passes() => Assert.Null(HttpsOpstraxForwarder.Validate(Options()));

    /// <summary>Captures the exact request the forwarder produced, and answers with a scripted result.</summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly Exception? _fault;

        public RecordingHandler(HttpStatusCode status) => _status = status;

        public RecordingHandler(Exception fault)
        {
            _fault = fault;
            _status = HttpStatusCode.OK;
        }

        public string Body { get; private set; } = string.Empty;

        public Uri? Uri { get; private set; }

        public int Requests { get; private set; }

        private HttpRequestMessage? _request;

        public string? Header(string name) =>
            _request is not null && _request.Headers.TryGetValues(name, out IEnumerable<string>? values)
                ? values.FirstOrDefault()
                : null;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            _request = request;
            Uri = request.RequestUri;
            Body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            if (_fault is not null) throw _fault;
            return new HttpResponseMessage(_status);
        }
    }
}

/// <summary>
/// Covers the durable outbox — the component that decides whether an OpsTrax outage is a delay or
/// permanent data loss.
/// </summary>
public sealed class EdgeOutboxTests : IDisposable
{
    private readonly string _directory =
        Directory.CreateTempSubdirectory("opstrax-outbox-").FullName;

    private readonly EdgeMetrics _metrics = new();

    private FileForwardOutbox Build(int maxEntries = 100, Func<DateTimeOffset>? clock = null) =>
        new(new OutboxOptions { Path = _directory, MaxEntries = maxEntries },
            _metrics, NullLogger.Instance, clock);

    [Fact]
    public async Task ParkedPayload_RoundTripsByteForByte()
    {
        // The HMAC covers these exact characters, so any transformation in storage would surface
        // later as an intermittent 401 that is very hard to trace back to here.
        const string payload = """{"imei":"862464068456321","note":"quote \" backslash \\ unicode é"}""";

        FileForwardOutbox outbox = Build();
        Assert.True(await outbox.EnqueueAsync(payload));

        IReadOnlyList<OutboxEntry> parked = await outbox.PeekAsync(10);

        Assert.Single(parked);
        Assert.Equal(payload, parked[0].PayloadJson);
    }

    [Fact]
    public async Task Entries_AreDrainedOldestFirst()
    {
        FileForwardOutbox outbox = Build();
        for (int i = 0; i < 5; i++) await outbox.EnqueueAsync($$"""{"seq":{{i}}}""");

        IReadOnlyList<OutboxEntry> parked = await outbox.PeekAsync(10);

        Assert.Equal(
            Enumerable.Range(0, 5).Select(i => $$"""{"seq":{{i}}}""").ToArray(),
            parked.Select(e => e.PayloadJson).ToArray());
    }

    [Fact]
    public async Task ReleasedEntry_IsGone()
    {
        FileForwardOutbox outbox = Build();
        await outbox.EnqueueAsync("""{"a":1}""");

        IReadOnlyList<OutboxEntry> parked = await outbox.PeekAsync(10);
        await outbox.ReleaseAsync(parked[0].Id);

        Assert.Equal(0, outbox.Count);
        Assert.Empty(await outbox.PeekAsync(10));
    }

    [Fact]
    public async Task AtTheCeiling_TheOldestIsDiscardedSoTheNewestFixSurvives()
    {
        // During a long outage the freshest fix is the one that puts a truck in the right place
        // when service returns; refusing new fixes would freeze the fleet at the outage's start.
        FileForwardOutbox outbox = Build(maxEntries: 3);
        for (int i = 0; i < 5; i++) await outbox.EnqueueAsync($$"""{"seq":{{i}}}""");

        IReadOnlyList<OutboxEntry> parked = await outbox.PeekAsync(10);

        Assert.Equal(3, parked.Count);
        Assert.Equal("""{"seq":2}""", parked[0].PayloadJson);
        Assert.Equal("""{"seq":4}""", parked[^1].PayloadJson);
        Assert.Equal(2, _metrics.OutboxEntriesDiscarded);
    }

    [Fact]
    public async Task EntriesOlderThanTheAgeLimit_AreDiscardedAndCounted()
    {
        DateTimeOffset now = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
        FileForwardOutbox outbox = Build(clock: () => now);

        await outbox.EnqueueAsync("""{"old":true}""");
        now = now.AddDays(10);
        await outbox.EnqueueAsync("""{"fresh":true}""");

        int purged = await outbox.PurgeExpiredAsync(TimeSpan.FromDays(7));

        Assert.Equal(1, purged);
        IReadOnlyList<OutboxEntry> parked = await outbox.PeekAsync(10);
        Assert.Single(parked);
        Assert.Equal("""{"fresh":true}""", parked[0].PayloadJson);
    }

    [Fact]
    public async Task ParkedFixes_SurviveAProcessRestart()
    {
        FileForwardOutbox first = Build();
        await first.EnqueueAsync("""{"imei":"862464068456321"}""");

        FileForwardOutbox restarted = Build();

        Assert.Equal(1, restarted.Count);
        Assert.Equal("""{"imei":"862464068456321"}""", (await restarted.PeekAsync(10))[0].PayloadJson);
    }

    [Fact]
    public void PartialWritesFromAnUncleanShutdown_AreDiscardedOnStartup()
    {
        // A .tmp file is incomplete by construction; replaying one would be replaying a truncated fix.
        File.WriteAllText(Path.Combine(_directory, "00000000000000000001-000000001.tmp"), "{\"trunc");

        FileForwardOutbox outbox = Build();

        Assert.Equal(0, outbox.Count);
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task CorruptEntry_IsDiscardedRatherThanBlockingEverythingBehindIt()
    {
        FileForwardOutbox outbox = Build();
        await outbox.EnqueueAsync("""{"good":true}""");
        File.WriteAllText(Path.Combine(_directory, "00000000000000000000-000000000.json"), "not json at all");

        IReadOnlyList<OutboxEntry> parked = await outbox.PeekAsync(10);

        Assert.Single(parked);
        Assert.Equal("""{"good":true}""", parked[0].PayloadJson);
        Assert.Equal(1, _metrics.OutboxEntriesDiscarded);
    }

    [Fact]
    public void UnwritableDirectory_FailsAtStartup_NotDuringTheFirstOutage()
    {
        // An edge that discovers it has no durable queue only when OpsTrax goes down loses exactly
        // the fixes the queue existed for.
        string file = Path.Combine(_directory, "not-a-directory");
        File.WriteAllText(file, "x");

        Assert.Throws<InvalidOperationException>(() =>
            new FileForwardOutbox(
                new OutboxOptions { Path = file, MaxEntries = 10 }, _metrics, NullLogger.Instance));
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { /* best effort */ }
    }
}
