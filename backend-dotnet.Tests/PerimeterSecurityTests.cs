using System.Net;
using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Opstrax.Api.Controllers;
using Opstrax.Api.Storage;

namespace Opstrax.Tests;

public sealed class UploadPerimeterSecurityTests
{
    private static FileStorageService Service()
    {
        var root = Path.Combine(Path.GetTempPath(), "opstrax-perimeter-" + Guid.NewGuid().ToString("N"));
        return new FileStorageService(new LocalObjectStore(root), NullLogger<FileStorageService>.Instance);
    }

    [Fact]
    public async Task Upload_AcceptsValidPdf_AndCanonicalizesMetadata()
    {
        await using var content = Bytes("%PDF-1.7\n1 0 obj\n<<>>\nendobj\n%%EOF\n");
        var result = await Service().UploadAsync(
            7, "pod", "proof.PDF", "application/pdf; charset=binary", content);

        Assert.Equal("application/pdf", result.ContentType);
        Assert.EndsWith(".pdf", result.Key);
    }

    [Theory]
    [InlineData("proof.jpg", "application/pdf")]
    [InlineData("proof.pdf", "image/jpeg")]
    [InlineData("../proof.pdf", "application/pdf")]
    [InlineData("proof.exe", "application/pdf")]
    public async Task Upload_RejectsExtensionMimeOrNameMismatch(string fileName, string contentType)
    {
        await using var content = Bytes("%PDF-1.7\n%%EOF\n");
        await Assert.ThrowsAsync<ArgumentException>(
            () => Service().UploadAsync(7, "pod", fileName, contentType, content));
    }

    [Fact]
    public async Task Upload_RejectsSpoofedPdf()
    {
        await using var content = Bytes("this is not a PDF");
        await Assert.ThrowsAsync<ArgumentException>(
            () => Service().UploadAsync(7, "pod", "proof.pdf", "application/pdf", content));
    }

    [Fact]
    public async Task Upload_RejectsActivePdfPolyglot()
    {
        await using var content = Bytes("%PDF-1.7\n<script>alert(1)</script>\n%%EOF\n");
        await Assert.ThrowsAsync<ArgumentException>(
            () => Service().UploadAsync(7, "pod", "proof.pdf", "application/pdf", content));
    }

    [Fact]
    public async Task Upload_RejectsActiveMarkupAfterValidImageHeader()
    {
        var bytes = new byte[] { 0xff, 0xd8, 0xff }
            .Concat(Encoding.ASCII.GetBytes("<html><script>alert(1)</script></html>"))
            .ToArray();
        await using var content = new MemoryStream(bytes);
        await Assert.ThrowsAsync<ArgumentException>(
            () => Service().UploadAsync(7, "pod", "proof.jpg", "image/jpeg", content));
    }

    [Fact]
    public async Task Upload_CategoryCannotEscapeTenantPrefix()
    {
        await using var content = Bytes("%PDF-1.7\n%%EOF\n");
        var result = await Service().UploadAsync(
            7, "../../tenant/8", "proof.pdf", "application/pdf", content);

        Assert.StartsWith("tenant/7/", result.Key);
        Assert.DoesNotContain("..", result.Key);
        Assert.DoesNotContain("/tenant/8/", result.Key);
    }

    [Fact]
    public async Task Upload_RejectsOfficeDocumentWithExternalRelationship()
    {
        await using var content = new MemoryStream();
        using (var zip = new ZipArchive(content, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(zip, "[Content_Types].xml", "<Types/>");
            WriteEntry(zip, "word/document.xml", "<document/>");
            WriteEntry(zip, "word/_rels/document.xml.rels",
                "<Relationships><Relationship TargetMode=\"External\" Target=\"http://169.254.169.254/\"/></Relationships>");
        }
        content.Position = 0;

        await Assert.ThrowsAsync<ArgumentException>(
            () => Service().UploadAsync(7, "pod", "proof.docx",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document", content));
    }

    [Fact]
    public async Task Upload_RejectsActiveText()
    {
        await using var content = Bytes("<svg onload=alert(1)></svg>");
        await Assert.ThrowsAsync<ArgumentException>(
            () => Service().UploadAsync(7, "pod", "proof.txt", "text/plain", content));
    }

    [Fact]
    public async Task Upload_StopsReadingAtConfiguredLimit()
    {
        await using var content = new GeneratedStream(25L * 1024 * 1024 + 1);
        await Assert.ThrowsAsync<ArgumentException>(
            () => Service().UploadAsync(7, "pod", "proof.pdf", "application/pdf", content));
        Assert.True(content.BytesRead <= 25L * 1024 * 1024 + 64 * 1024);
    }

    private static MemoryStream Bytes(string value) => new(Encoding.ASCII.GetBytes(value));

    private static void WriteEntry(ZipArchive archive, string name, string value)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open(), Encoding.UTF8);
        writer.Write(value);
    }

    private sealed class GeneratedStream(long length) : Stream
    {
        private long remaining = length;
        public long BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => BytesRead; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            var count = (int)Math.Min(buffer.Length, remaining);
            if (count == 0) return ValueTask.FromResult(0);
            buffer.Span[..count].Clear();
            remaining -= count;
            BytesRead += count;
            return ValueTask.FromResult(count);
        }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

public sealed class PodAssetSsrfSecurityTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.1")]
    [InlineData("172.16.0.1")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    [InlineData("ff02::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("2001:db8::1")]
    public void AddressPolicy_BlocksNonPublicTargets(string value)
    {
        Assert.True(FleetTmsEndpoints.IsBlockedPodAssetAddress(IPAddress.Parse(value)));
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("2606:4700:4700::1111")]
    public void AddressPolicy_AllowsPublicTargets(string value)
    {
        Assert.False(FleetTmsEndpoints.IsBlockedPodAssetAddress(IPAddress.Parse(value)));
    }

    [Theory]
    [InlineData("photo", "image/jpeg", true)]
    [InlineData("signature", "image/png", true)]
    [InlineData("document", "application/pdf", true)]
    [InlineData("photo", "text/html", false)]
    [InlineData("document", "image/svg+xml", false)]
    [InlineData("document", null, false)]
    public void ContentTypePolicy_IsKindSpecific(string kind, string? contentType, bool expected)
    {
        Assert.Equal(expected, FleetTmsEndpoints.IsAllowedPodAssetContentType(kind, contentType));
    }
}
