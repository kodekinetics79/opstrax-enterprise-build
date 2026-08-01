namespace Opstrax.Api;

// Holds the SSE stream-ticket signing key so both Program.cs middleware and
// EndpointMappings can access the same key without circular references.
internal static class TelemetryKeyStore
{
    internal static readonly byte[] SseTicketKey =
        System.Text.Encoding.UTF8.GetBytes(
            (Environment.GetEnvironmentVariable("OPSTRAX_SSE_TICKET_KEY")
             // Deployment manifests and ConfigValidationService use .NET's
             // hierarchical environment-variable spelling. Consume the same key
             // so replicas do not silently generate different process-local keys.
             ?? Environment.GetEnvironmentVariable("Sse__TicketKey")
             ?? Environment.GetEnvironmentVariable("Telemetry__SseTicketKey")) is { Length: > 0 } k
                ? k : Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"));
}
