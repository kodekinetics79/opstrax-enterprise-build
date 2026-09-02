using System.Net;
using System.Text;
using System.Text.Json;
using Opstrax.Api.Services.Connectors;

namespace Opstrax.Tests;

public sealed class MotiveResponseReaderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExactByteLimit_ParsesUtf8AcrossShortReads(bool declaredLength)
    {
        var bytes = Encoding.UTF8.GetBytes("{\"name\":\"é🚚\"}");
        var stream = new MotiveReadFixture(bytes, chunkSize: 1);
        using var content = new MotiveStreamingFixture(stream, declaredLength ? bytes.Length : null);
        using var json = await MotiveResponseReader.ReadJsonAsync(content, bytes.Length, CancellationToken.None);
        Assert.Equal("é🚚", json.RootElement.GetProperty("name").GetString());
        Assert.Equal(bytes.Length, stream.BytesRead);
        Assert.True(stream.Disposed);
        Assert.False(content.WasBuffered);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1L)]
    public async Task MissingOrUnderstatedLength_CannotBypassActualByteLimit(long? declaredLength)
    {
        var stream = new MotiveReadFixture(Encoding.UTF8.GetBytes("{}" + new string(' ', 100)), chunkSize: 3);
        using var content = new MotiveStreamingFixture(stream, declaredLength);
        await Assert.ThrowsAsync<MotiveResponseReader.ResponseTooLargeException>(() =>
            MotiveResponseReader.ReadJsonAsync(content, 16, CancellationToken.None));
        Assert.Equal(17, stream.BytesRead);
        Assert.True(stream.Disposed);
        Assert.False(content.WasBuffered);
    }

    [Fact]
    public async Task DeclaredOversize_IsRejectedWithoutReadingTheStream()
    {
        var stream = new MotiveReadFixture([]);
        using var content = new MotiveStreamingFixture(stream, 17);
        await Assert.ThrowsAsync<MotiveResponseReader.ResponseTooLargeException>(() =>
            MotiveResponseReader.ReadJsonAsync(content, 16, CancellationToken.None));
        Assert.Equal(0, stream.BytesRead);
        Assert.False(stream.ReadStarted.Task.IsCompleted);
        Assert.False(content.WasBuffered);
    }

    [Fact]
    public async Task UnendingBody_StopsAtLimitPlusOneByte()
    {
        var stream = new MotiveReadFixture([], unending: true);
        using var content = new MotiveStreamingFixture(stream);
        await Assert.ThrowsAsync<MotiveResponseReader.ResponseTooLargeException>(() =>
            MotiveResponseReader.ReadJsonAsync(content, 16, CancellationToken.None));
        Assert.Equal(17, stream.BytesRead);
        Assert.True(stream.Disposed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{broken}")]
    public async Task InvalidJson_IsRejectedAndStreamIsDisposed(string body)
    {
        var stream = new MotiveReadFixture(Encoding.UTF8.GetBytes(body));
        using var content = new MotiveStreamingFixture(stream);
        await Assert.ThrowsAnyAsync<JsonException>(() =>
            MotiveResponseReader.ReadJsonAsync(content, 32, CancellationToken.None));
        Assert.True(stream.Disposed);
    }

    [Fact]
    public async Task CancellationAfterHeaders_InterruptsStalledBodyRead()
    {
        var stream = new MotiveReadFixture([], stalled: true);
        using var content = new MotiveStreamingFixture(stream);
        using var cts = new CancellationTokenSource();
        var read = MotiveResponseReader.ReadJsonAsync(content, 16, cts.Token);
        await stream.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(stream.Disposed);
    }
}

// These fixtures deliberately fail if HttpClient tries to prebuffer a response.
// They model unknown/dishonest length, short UTF-8 reads and stalled bodies locally.
internal sealed class MotiveStreamingFixture(MotiveReadFixture stream, long? declaredLength = null) : HttpContent
{
    internal bool WasBuffered { get; private set; }

    protected override bool TryComputeLength(out long length)
    {
        length = declaredLength.GetValueOrDefault();
        return declaredLength.HasValue;
    }

    protected override Task SerializeToStreamAsync(Stream target, TransportContext? context)
    {
        WasBuffered = true;
        throw new InvalidOperationException("The provider fixture must not be prebuffered.");
    }

    protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(stream);
    protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<Stream>(stream);
    }
}

internal sealed class MotiveReadFixture(
    byte[] body, int chunkSize = int.MaxValue, bool unending = false, bool stalled = false) : Stream
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
        if (unending) buffer.Span[..count].Fill((byte)' ');
        else
        {
            count = Math.Min(count, body.Length - BytesRead);
            body.AsMemory(BytesRead, count).CopyTo(buffer);
        }
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
