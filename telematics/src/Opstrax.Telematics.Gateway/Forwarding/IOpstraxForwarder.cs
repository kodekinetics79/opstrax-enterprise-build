namespace Opstrax.Telematics.Gateway.Forwarding;

/// <summary>What happened to one attempt at delivering a payload to OpsTrax.</summary>
/// <remarks>
/// The distinction that matters is <b>retry or not</b>. Parking an undeliverable payload fills
/// the outbox with work that can never succeed and buries the fixes that could; discarding a
/// payload that failed for a transient reason loses a real fix from a real truck. Every HTTP
/// status the endpoint can return is deliberately assigned to one of these three.
/// </remarks>
internal enum ForwardOutcome
{
    /// <summary>OpsTrax accepted the fix, or recognised it as one it already holds.</summary>
    Delivered = 0,

    /// <summary>
    /// Transient: network fault, timeout, throttling, or a server-side outage. The payload is
    /// still good and must be retried.
    /// </summary>
    Retryable = 1,

    /// <summary>
    /// Terminal: OpsTrax understood the request and refused it — the device is not provisioned,
    /// is quarantined, belongs to another tenant, or the payload is malformed. Retrying is
    /// guaranteed to fail again, so it is dropped and counted loudly.
    /// </summary>
    Rejected = 2,
}

/// <summary>The result of one delivery attempt.</summary>
/// <param name="Outcome">Whether to retry, drop, or consider it done.</param>
/// <param name="StatusCode">HTTP status when a response was received; null on a transport fault.</param>
/// <param name="Detail">Short, log-safe explanation. Never contains a full IMEI or the gateway secret.</param>
internal readonly record struct ForwardResult(ForwardOutcome Outcome, int? StatusCode, string Detail)
{
    internal static ForwardResult Delivered(int status) => new(ForwardOutcome.Delivered, status, "accepted");
}

/// <summary>
/// Delivers a signed, normalized fix to the OpsTrax trusted-gateway ingest endpoint.
/// </summary>
/// <remarks>
/// Takes the payload as a <see cref="string"/>, not an object, on purpose: the HMAC covers the
/// exact bytes OpsTrax will read, so the rendered body has to travel unmodified from
/// normalization through the outbox to the socket. An interface that accepted a model would
/// re-serialize somewhere and break signatures in a way that only shows up as intermittent 401s.
/// </remarks>
internal interface IOpstraxForwarder
{
    /// <summary>Signs and POSTs one already-rendered payload.</summary>
    /// <param name="payloadJson">The exact JSON body to sign and send.</param>
    /// <param name="cancellationToken">Cancels the attempt.</param>
    Task<ForwardResult> SendAsync(string payloadJson, CancellationToken cancellationToken = default);
}
