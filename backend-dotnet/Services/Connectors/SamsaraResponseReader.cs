using System.Text.Json;

namespace Opstrax.Api.Services.Connectors;

internal static class SamsaraResponseReader
{
    // Defensive application limits, not an asserted provider page-size contract.
    internal const int MaxResponseBytes = 4 * 1024 * 1024;
    internal static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);
    internal sealed class ResponseTooLargeException() : Exception("Samsara response exceeded the allowed size.");

    internal static async Task<JsonDocument> ReadJsonAsync(HttpContent content, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (content.Headers.ContentLength > MaxResponseBytes) throw new ResponseTooLargeException();
        // Count actual bytes: a missing or false Content-Length cannot bypass the cap.
        var buffer = new byte[MaxResponseBytes + 1];
        await using var stream = await content.ReadAsStreamAsync(ct);
        var count = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var read = await stream.ReadAsync(buffer.AsMemory(count), ct);
            if (read == 0) break;
            count += read;
            if (count > MaxResponseBytes) throw new ResponseTooLargeException();
        }
        ct.ThrowIfCancellationRequested();
        return JsonDocument.Parse(buffer.AsMemory(0, count));
    }
}
