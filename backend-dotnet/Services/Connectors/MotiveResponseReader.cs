using System.Text.Json;

namespace Opstrax.Api.Services.Connectors;

/// <summary>Bound provider response bytes before parsing, including chunked bodies.</summary>
internal static class MotiveResponseReader
{
    internal const int TokenResponseBytes = 64 * 1024;
    internal const int ProbeResponseBytes = 1024 * 1024;

    internal sealed class ResponseTooLargeException() : Exception("Motive response exceeded the byte limit.");

    internal static async Task<JsonDocument> ReadJsonAsync(
        HttpContent content, int maxBytes, CancellationToken ct)
    {
        if (maxBytes is < 1 or > ProbeResponseBytes) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        ct.ThrowIfCancellationRequested();
        if (content.Headers.ContentLength > maxBytes) throw new ResponseTooLargeException();

        // The extra byte distinguishes an exact-limit body from an oversized body.
        // Content-Length is only an early rejection hint; count actual stream bytes.
        var buffer = new byte[maxBytes + 1];
        await using var stream = await content.ReadAsStreamAsync(ct);
        var count = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var read = await stream.ReadAsync(buffer.AsMemory(count), ct);
            if (read == 0) break;
            count += read;
            if (count > maxBytes) throw new ResponseTooLargeException();
        }
        ct.ThrowIfCancellationRequested();
        return JsonDocument.Parse(buffer.AsMemory(0, count));
    }
}
