namespace Opstrax.Api;

// Holds the SSE stream-ticket signing key so both Program.cs middleware and
// EndpointMappings can access the same key without circular references.
internal static class TelemetryKeyStore
{
    internal static readonly byte[] SseTicketKey =
        System.Text.Encoding.UTF8.GetBytes(
            Environment.GetEnvironmentVariable("OPSTRAX_SSE_TICKET_KEY") is { Length: > 0 } k
                ? k : Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"));
}
