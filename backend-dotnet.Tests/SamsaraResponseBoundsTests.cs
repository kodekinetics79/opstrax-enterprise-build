using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Opstrax.Api.Data;
using Opstrax.Api.Services.Connectors;

namespace Opstrax.Tests;

public sealed class SamsaraResponseBoundsTests
{
    private const int ResponseLimit = 4 * 1024 * 1024;
    private const string CompletePage = """{"data":[],"pagination":{"endCursor":"done","hasNextPage":false}}""";
    private static IReadOnlyDictionary<string, string?> Config =>
        new Dictionary<string, string?> { ["apiToken"] = "synthetic-test-token" };

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public async Task Handshake_RejectsOversizedBodyAtEitherScope(bool statistics, int lengthMode)
    {
        var stream = new SamsaraBodyFixture([], endless: true);
        using var content = new SamsaraContentFixture(stream, lengthMode == 0 ? null : lengthMode == 1 ? 1 : ResponseLimit + 1);
        var calls = 0;
        var connector = Connector(_ => Task.FromResult(++calls == 1 && statistics
            ? Json("""{"data":[{"id":"synthetic-vehicle"}]}""")
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));

        var result = await connector.TestConnectionAsync(Config, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("exceeded", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(statistics ? 2 : 1, calls);
        Assert.Equal(lengthMode == 2 ? 0 : ResponseLimit + 1, stream.BytesRead);
        Assert.False(content.WasBuffered);
        Assert.True(content.Disposed);
    }

    [Fact]
    public async Task Sync_ExactLimitAndShortUtf8ReadsAreAccepted()
    {
        var prefix = Encoding.UTF8.GetBytes("""{"note":"é🚚","data":[],"pagination":{"endCursor":"done","hasNextPage":false}}""");
        var bytes = new byte[ResponseLimit];
        bytes.AsSpan().Fill((byte)' ');
        prefix.CopyTo(bytes, 0);
        var stream = new SamsaraBodyFixture(bytes, chunkSize: 7);
        using var content = new SamsaraContentFixture(stream);
        using var body = OperationBody();
        var connector = Connector(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));

        var result = await connector.RunActionAsync("sync", Config, body.RootElement, CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.Equal("done", result.Details!["nextCursor"]);
        Assert.Equal(ResponseLimit, stream.BytesRead);
        Assert.False(content.WasBuffered);
        Assert.True(stream.Disposed);
    }

    [Theory]
    [InlineData(429)]
    [InlineData(503)]
    public async Task Sync_RetriesStatusWithoutReadingErrorBody(int status)
    {
        var stream = new SamsaraBodyFixture([], endless: true);
        using var content = new SamsaraContentFixture(stream);
        var calls = 0;
        var requests = new List<string>();
        var connector = Connector(request =>
        {
            requests.Add(request.RequestUri!.Query);
            if (++calls > 1)
            {
                Assert.True(content.Disposed);
                return Task.FromResult(Json(CompletePage));
            }
            var response = new HttpResponseMessage((HttpStatusCode)status) { Content = content };
            response.Headers.RetryAfter = new(TimeSpan.Zero);
            return Task.FromResult(response);
        });
        using var body = OperationBody("resume/&cursor");

        var result = await connector.RunActionAsync("sync", Config, body.RootElement, CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.Equal(2, calls);
        Assert.Equal(requests[0], requests[1]);
        Assert.Contains("after=resume%2F%26cursor", requests[0]);
        Assert.Equal(0, stream.BytesRead);
        Assert.False(content.WasBuffered);
    }

    [Fact]
    public async Task Sync_ExhaustedRetriesNeverReadErrorBodies()
    {
        var contents = new List<SamsaraContentFixture>();
        var connector = Connector(_ =>
        {
            var content = new SamsaraContentFixture(new SamsaraBodyFixture([], endless: true));
            contents.Add(content);
            var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = content };
            response.Headers.RetryAfter = new(TimeSpan.Zero);
            return Task.FromResult(response);
        });
        using var body = OperationBody();

        var result = await connector.RunActionAsync("sync", Config, body.RootElement, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(5, contents.Count);
        Assert.All(contents, content => { Assert.True(content.Disposed); Assert.False(content.WasBuffered); });
        Assert.Equal(0, result.Details!["pagesCommitted"]);
        Assert.Null(result.Details["nextCursor"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Sync_OversizedPageReturnsOnlyPreviouslyCompletedCursor(bool laterPage)
    {
        var stream = new SamsaraBodyFixture([], endless: true);
        using var content = new SamsaraContentFixture(stream);
        var calls = 0;
        var connector = Connector(_ => Task.FromResult(++calls == 1 && laterPage
            ? Json("""{"data":[],"pagination":{"endCursor":"complete-1","hasNextPage":true}}""")
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));
        using var body = OperationBody();

        var result = await connector.RunActionAsync("sync", Config, body.RootElement, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(laterPage ? 1 : 0, result.Details!["pagesCommitted"]);
        Assert.Equal(laterPage ? "complete-1" : null, result.Details["nextCursor"]);
        Assert.Equal(laterPage ? 2 : 1, calls);
        Assert.Equal(0, result.Details["positionsWritten"]);
        Assert.Contains("exceeded", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ResponseLimit + 1, stream.BytesRead);
        Assert.True(stream.Disposed);
        Assert.False(content.WasBuffered);
    }

    [Fact]
    public async Task Sync_CallerCancellationPropagatesFromBody()
    {
        var stream = new SamsaraBodyFixture([], stalled: true);
        using var content = new SamsaraContentFixture(stream);
        var connector = Connector(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));
        using var body = OperationBody();
        using var caller = new CancellationTokenSource();
        var run = connector.RunActionAsync("sync", Config, body.RootElement, caller.Token);
        await stream.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        caller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(stream.Disposed);
        Assert.False(content.WasBuffered);
    }

    [Fact]
    public async Task AutomaticDeadlines_CoverBodiesAndWholeHandshake()
    {
        var handshakeStream = new SamsaraBodyFixture([], stalled: true);
        var syncStream = new SamsaraBodyFixture([], stalled: true);
        var aggregateStream = new SamsaraBodyFixture([], stalled: true);
        using var handshakeContent = new SamsaraContentFixture(handshakeStream);
        using var syncContent = new SamsaraContentFixture(syncStream);
        using var aggregateContent = new SamsaraContentFixture(aggregateStream);
        var handshake = Connector(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = handshakeContent }));
        var syncCalls = 0;
        var sync = Connector(_ =>
        {
            syncCalls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = syncContent });
        });
        var aggregateCalls = 0;
        var aggregate = Connector(async _ =>
        {
            if (++aggregateCalls == 1)
            {
                await Task.Delay(TimeSpan.FromSeconds(12));
                return Json("""{"data":[]}""");
            }
            return new(HttpStatusCode.OK) { Content = aggregateContent };
        });
        using var body = OperationBody();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var handshakeRun = handshake.TestConnectionAsync(Config, CancellationToken.None);
        var syncRun = sync.RunActionAsync("sync", Config, body.RootElement, CancellationToken.None);
        var aggregateRun = aggregate.TestConnectionAsync(Config, CancellationToken.None);
        await Task.WhenAll(handshakeStream.ReadStarted.Task, syncStream.ReadStarted.Task).WaitAsync(TimeSpan.FromSeconds(5));
        await Task.WhenAll(handshakeRun, syncRun).WaitAsync(TimeSpan.FromSeconds(27));
        Assert.False((await handshakeRun).Success);
        Assert.False((await syncRun).Success);
        Assert.Contains("provider request timed out", (await syncRun).Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, syncCalls);
        await aggregateRun.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False((await aggregateRun).Success);
        Assert.Contains("timeout", (await aggregateRun).Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, aggregateCalls);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(31), $"Aggregate handshake exceeded its 25-second budget: {watch.Elapsed}.");
        Assert.True(handshakeStream.Disposed && syncStream.Disposed && aggregateStream.Disposed);
    }

    internal static SamsaraConnector Connector(Func<HttpRequestMessage, Task<HttpResponseMessage>> response, Database? database = null)
    {
        var registrations = new ServiceCollection();
        if (database is not null) registrations.AddSingleton(database);
        var services = registrations.BuildServiceProvider();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Samsara:InterPageDelayMs"] = "0",
        }).Build();
        return new SamsaraConnector(new HttpFactory(new Handler(response)), services.GetRequiredService<IServiceScopeFactory>(),
            configuration, NullLogger<SamsaraConnector>.Instance);
    }

    internal static JsonDocument OperationBody(string? cursor = null) => JsonDocument.Parse(JsonSerializer.Serialize(new
    {
        companyId = 17, integrationId = 23, operationGeneration = 0,
        operationLeaseToken = "11111111-1111-1111-1111-111111111111", cursor,
    }));

    internal static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => response(request);
    }

    private sealed class HttpFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}

internal sealed class SamsaraContentFixture(SamsaraBodyFixture stream, long? declaredLength = null) : HttpContent
{
    internal bool WasBuffered { get; private set; }
    internal bool Disposed { get; private set; }
    protected override bool TryComputeLength(out long length) { length = declaredLength.GetValueOrDefault(); return declaredLength.HasValue; }
    protected override Task SerializeToStreamAsync(Stream target, TransportContext? context)
    {
        WasBuffered = true;
        throw new InvalidOperationException("Samsara fixture must not be prebuffered.");
    }
    protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(stream);
    protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult<Stream>(stream); }
    protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
}

internal sealed class SamsaraBodyFixture(byte[] body, int chunkSize = int.MaxValue, bool endless = false, bool stalled = false) : Stream
{
    internal int BytesRead { get; private set; }
    internal bool Disposed { get; private set; }
    internal TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        ReadStarted.TrySetResult();
        ct.ThrowIfCancellationRequested();
        if (stalled) await Task.Delay(Timeout.Infinite, ct);
        var count = Math.Min(buffer.Length, chunkSize);
        if (endless) buffer.Span[..count].Fill((byte)' ');
        else { count = Math.Min(count, body.Length - BytesRead); body.AsMemory(BytesRead, count).CopyTo(buffer); }
        BytesRead += count;
        return count;
    }
    protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    public override bool CanRead => !Disposed;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => BytesRead; set => throw new NotSupportedException(); }
    public override void Flush() => throw new NotSupportedException();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
