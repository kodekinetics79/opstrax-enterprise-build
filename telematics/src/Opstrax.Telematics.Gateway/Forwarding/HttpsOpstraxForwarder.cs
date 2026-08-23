using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Opstrax.Telematics.Gateway.Edge;

namespace Opstrax.Telematics.Gateway.Forwarding;

/// <summary>
/// Delivers fixes to <c>POST /api/telemetry/gps-ingest</c> over HTTPS, authenticated with the
/// per-gateway HMAC credential.
/// </summary>
/// <remarks>
/// <para>
/// <b>The signing contract, exactly.</b> OpsTrax computes
/// <c>HMAC-SHA256(secret, "{unixSeconds}.{rawBody}")</c> over <c>body.GetRawText()</c> — the bytes
/// it actually received — and compares against lowercase-hex <c>X-Gateway-Signature</c>, with
/// <c>X-Gateway-Timestamp</c> required inside ±300 seconds. This class therefore signs the caller's
/// string and writes those same bytes to the wire without re-encoding. Verified byte-identical
/// between .NET's <see cref="HMACSHA256"/> and the server's implementation; when signatures
/// mismatch, the cause is the body bytes, not the algorithm.
/// </para>
/// <para>
/// <b>The IMEI is signed, not headered.</b> The endpoint also accepts an <c>X-Device-IMEI</c>
/// header that overrides the body — but headers are outside the HMAC. Sending the identifier
/// there would let anyone who can modify the request in flight redirect a fix onto a different
/// vehicle without breaking the signature. The IMEI travels in the signed body only.
/// </para>
/// <para>
/// <b>No inline retry.</b> A tracker's TCP connection is blocked behind this call, so a failed
/// attempt returns immediately as <see cref="ForwardOutcome.Retryable"/> and the caller parks it
/// for <see cref="OutboxDrainService"/>. Retrying here would hold the socket open across a
/// multi-second backoff and push backpressure onto the device for no benefit.
/// </para>
/// </remarks>
internal sealed class HttpsOpstraxForwarder : IOpstraxForwarder, IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly byte[] _secret;
    private readonly string _gatewayId;
    private readonly Uri _endpoint;
    private readonly ILogger _logger;
    private readonly Func<DateTimeOffset> _clock;

    /// <summary>Creates a forwarder from validated options.</summary>
    /// <param name="options">Forwarding configuration. Validated by <see cref="Validate"/> at startup.</param>
    /// <param name="logger">Receives delivery diagnostics.</param>
    /// <param name="httpClient">
    /// Optional client to use instead of an owned one. Supplied by tests against a stub server;
    /// when null, this type creates and disposes its own.
    /// </param>
    /// <param name="clock">UTC clock seam for tests.</param>
    public HttpsOpstraxForwarder(
        ForwardOptions options,
        ILogger logger,
        HttpClient? httpClient = null,
        Func<DateTimeOffset>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);

        if (Validate(options) is { } problem)
            throw new InvalidOperationException(problem);

        _gatewayId = options.GatewayId.Trim();
        _secret = Encoding.UTF8.GetBytes(options.Secret);
        _endpoint = new Uri(new Uri(options.BaseUrl.TrimEnd('/') + "/"), options.IngestPath.TrimStart('/'));

        _ownsHttpClient = httpClient is null;
        _http = httpClient ?? new HttpClient
        {
            // A tracker connection waits on this; the socket-level timeout is the backstop that
            // keeps a black-holed route from pinning a connection until the idle timeout fires.
            Timeout = options.Timeout,
        };
    }

    /// <summary>
    /// Checks forwarding configuration and returns a human-readable problem, or null when usable.
    /// </summary>
    /// <remarks>
    /// Called at startup so a misconfigured edge refuses to boot rather than accepting tracker
    /// connections it can never deliver — which would look healthy from the outside while quietly
    /// filling the outbox.
    /// </remarks>
    public static string? Validate(ForwardOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.BaseUrl))
            return "Gateway:Edge:Forward:BaseUrl is required when Egress is Https.";

        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out Uri? baseUri))
            return $"Gateway:Edge:Forward:BaseUrl is not an absolute URL: '{options.BaseUrl}'.";

        // Plain http would put a real person's vehicle position on the wire in the clear. The HMAC
        // authenticates the body; it does not conceal it.
        if (baseUri.Scheme != Uri.UriSchemeHttps && !baseUri.IsLoopback)
            return $"Gateway:Edge:Forward:BaseUrl must use https (loopback excepted for testing): '{options.BaseUrl}'.";

        if (string.IsNullOrWhiteSpace(options.GatewayId))
            return "Gateway:Edge:Forward:GatewayId is required. Provision one with POST /api/telemetry/gateways.";

        // The server refuses a stored secret shorter than 32 characters outright, so a shorter one
        // here can only ever produce 503s.
        if (string.IsNullOrWhiteSpace(options.Secret) || options.Secret.Length < 32)
            return "Gateway:Edge:Forward:Secret is required and must be at least 32 characters " +
                   "(the value shown once by POST /api/telemetry/gateways).";

        if (options.Timeout <= TimeSpan.Zero)
            return "Gateway:Edge:Forward:Timeout must be positive.";

        return null;
    }

    /// <inheritdoc />
    public async Task<ForwardResult> SendAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(payloadJson);

        // The endpoint caps the body at 32 KiB and answers 413. Catching it here keeps a doomed
        // payload out of the outbox entirely.
        byte[] body = Encoding.UTF8.GetBytes(payloadJson);
        if (body.Length > 32_768)
            return new ForwardResult(ForwardOutcome.Rejected, null, "payload exceeds the 32 KiB ingest limit");

        string timestamp = _clock().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        string signature = Sign(timestamp, payloadJson);

        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint);
        request.Content = new ByteArrayContent(body);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        request.Headers.TryAddWithoutValidation("X-Gateway-Id", _gatewayId);
        request.Headers.TryAddWithoutValidation("X-Gateway-Timestamp", timestamp);
        request.Headers.TryAddWithoutValidation("X-Gateway-Signature", signature);

        try
        {
            using HttpResponseMessage response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            return Classify(response.StatusCode);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Host shutdown, not a delivery verdict. Retry after restart.
            return new ForwardResult(ForwardOutcome.Retryable, null, "cancelled");
        }
        catch (OperationCanceledException)
        {
            // HttpClient surfaces its own timeout as a cancellation with no token set.
            return new ForwardResult(ForwardOutcome.Retryable, null, $"timed out after {_http.Timeout}");
        }
        catch (HttpRequestException ex)
        {
            return new ForwardResult(ForwardOutcome.Retryable, null, $"transport fault: {ex.Message}");
        }
    }

    /// <summary>Lowercase-hex <c>HMAC-SHA256(secret, "{timestamp}.{body}")</c>, exactly as the server recomputes it.</summary>
    private string Sign(string timestamp, string payloadJson)
    {
        byte[] signed = Encoding.UTF8.GetBytes($"{timestamp}.{payloadJson}");
        return Convert.ToHexString(HMACSHA256.HashData(_secret, signed)).ToLowerInvariant();
    }

    /// <summary>Maps an ingest response to a retry decision, and logs the ones an operator must act on.</summary>
    private ForwardResult Classify(HttpStatusCode status)
    {
        int code = (int)status;

        if (code is >= 200 and < 300)
            return ForwardResult.Delivered(code);

        switch (status)
        {
            case HttpStatusCode.Conflict:
                // The durable replay ledger already holds this exact signed message. The fix is in
                // OpsTrax; re-sending would only earn another 409. That is delivery, not failure.
                return new ForwardResult(ForwardOutcome.Delivered, code, "already ingested (replay ledger hit)");

            case HttpStatusCode.Unauthorized:
                // Credential, clock skew, or a revoked gateway row. Recoverable by an operator
                // without touching the payload, so it is retried rather than discarded — but it is
                // an outage of the whole edge, so it is logged at the level that pages someone.
                _logger.LogCritical(
                    "OpsTrax rejected gateway {GatewayId} with 401. Check the gateway secret, that the " +
                    "gateway row is active, and that this host's clock is within 300s of OpsTrax. " +
                    "Fixes are being parked, not lost.",
                    _gatewayId);
                return new ForwardResult(ForwardOutcome.Retryable, code, "unauthorized");

            case HttpStatusCode.Forbidden:
                // Device belongs to another tenant, is quarantined, or is not enabled for telemetry.
                // A commissioning fault, not a transport one: retrying changes nothing.
                _logger.LogError(
                    "OpsTrax refused a fix with 403 for gateway {GatewayId}: the device is quarantined, " +
                    "not enabled for telemetry, or belongs to a different tenant than this gateway credential. " +
                    "Dropping the fix.",
                    _gatewayId);
                return new ForwardResult(ForwardOutcome.Rejected, code, "forbidden");

            case HttpStatusCode.NotFound:
                _logger.LogError(
                    "OpsTrax returned 404 for a forwarded fix: the IMEI resolves to no device, or to more " +
                    "than one. Register it in eld_devices before it will be accepted. Dropping the fix.");
                return new ForwardResult(ForwardOutcome.Rejected, code, "device not found");

            case HttpStatusCode.BadRequest:
            case HttpStatusCode.RequestEntityTooLarge:
                // The normalizer should make these impossible; one arriving means the edge and the
                // endpoint disagree about the contract, which is a bug worth surfacing.
                _logger.LogError(
                    "OpsTrax rejected a forwarded payload as invalid ({Status}). This indicates the edge " +
                    "normalizer and the ingest contract have diverged. Dropping the fix.", code);
                return new ForwardResult(ForwardOutcome.Rejected, code, "payload rejected");

            case HttpStatusCode.ServiceUnavailable:
                // The endpoint fails closed on incomplete schema topology or a missing replay ledger.
                // Genuinely transient from here; the payload is fine.
                _logger.LogWarning(
                    "OpsTrax ingest is failing closed (503) — schema topology or the replay ledger is " +
                    "incomplete. Parking fixes until it recovers.");
                return new ForwardResult(ForwardOutcome.Retryable, code, "ingest unavailable");

            default:
                return code is >= 500 or 429
                    ? new ForwardResult(ForwardOutcome.Retryable, code, $"server error {code}")
                    : new ForwardResult(ForwardOutcome.Rejected, code, $"unexpected status {code}");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(_secret);
        if (_ownsHttpClient) _http.Dispose();
    }
}
