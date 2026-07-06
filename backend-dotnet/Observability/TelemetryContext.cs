using System.Security.Cryptography;

namespace Opstrax.Api.Observability;

// ─────────────────────────────────────────────────────────────────────────────
// TelemetryContext — the per-request tracing identity.
//
// Holds W3C trace-context ids (trace_id / span_id / parent_span_id) plus the
// OpsTrax correlation_id and request dimensions (tenant, user, role, endpoint,
// deployment version). One instance lives in HttpContext.Items for the request
// and is exposed as an ambient AsyncLocal so any service method, DB call, or
// background continuation on the request's async flow can enrich its logs/spans
// with the same trace_id — satisfying the "trace one request frontend→DB" goal.
//
// trace_id  : 16-byte (32 hex) — stable for the whole distributed trace.
// span_id   :  8-byte (16 hex) — this hop's span; children mint their own.
// correlation_id : caller-supplied or minted; survives across service calls and
//                  is the human-facing id surfaced in error responses.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class TelemetryContext
{
    public const string HttpItemKey = "opstrax.telemetry.context";

    public string TraceId          { get; init; } = NewTraceId();
    public string SpanId           { get; init; } = NewSpanId();
    public string? ParentSpanId    { get; init; }
    public string CorrelationId    { get; set; }  = Guid.NewGuid().ToString("n");
    public byte   TraceFlags       { get; init; } = 0x01; // sampled

    // Request dimensions — populated by middleware as auth resolves.
    public string?  TenantId       { get; set; }
    public string?  UserId         { get; set; }
    public string?  Role           { get; set; }
    public string?  Endpoint       { get; set; }
    public string?  Method         { get; set; }
    public string   DeploymentVersion { get; init; } = BuildInfo.Version;

    private static readonly AsyncLocal<TelemetryContext?> AmbientHolder = new();

    /// <summary>Ambient context for the current async flow (null outside a request).</summary>
    public static TelemetryContext? Current => AmbientHolder.Value;

    /// <summary>Binds this context as ambient; returns a scope that restores the prior one.</summary>
    public IDisposable BeginScope()
    {
        var previous = AmbientHolder.Value;
        AmbientHolder.Value = this;
        return new Restore(() => AmbientHolder.Value = previous);
    }

    // ── W3C traceparent (version-traceid-spanid-flags) ──────────────────────────

    /// <summary>Renders the W3C traceparent header value for outbound propagation.</summary>
    public string ToTraceParent() =>
        $"00-{TraceId}-{SpanId}-{TraceFlags:x2}";

    /// <summary>
    /// Parses an inbound traceparent. On a well-formed header the returned context
    /// continues the caller's trace (same trace_id, new span_id, parent = caller span).
    /// On any malformed/absent header a brand-new root trace is started.
    /// </summary>
    public static TelemetryContext FromTraceParent(string? traceparent, string? correlationId)
    {
        var cid = string.IsNullOrWhiteSpace(correlationId)
            ? Guid.NewGuid().ToString("n")
            : correlationId.Trim();

        if (!string.IsNullOrWhiteSpace(traceparent))
        {
            // format: 00-<32hex>-<16hex>-<2hex>
            var parts = traceparent.Trim().Split('-');
            if (parts.Length == 4 &&
                parts[0].Length == 2 &&
                IsHex(parts[1]) && parts[1].Length == 32 && parts[1] != AllZero32 &&
                IsHex(parts[2]) && parts[2].Length == 16 && parts[2] != AllZero16)
            {
                return new TelemetryContext
                {
                    TraceId       = parts[1].ToLowerInvariant(),
                    SpanId        = NewSpanId(),
                    ParentSpanId  = parts[2].ToLowerInvariant(),
                    CorrelationId = cid,
                };
            }
        }

        return new TelemetryContext { CorrelationId = cid };
    }

    // ── id minting ──────────────────────────────────────────────────────────────

    private static string NewTraceId()
    {
        Span<byte> b = stackalloc byte[16];
        RandomNumberGenerator.Fill(b);
        return Convert.ToHexString(b).ToLowerInvariant();
    }

    private static string NewSpanId()
    {
        Span<byte> b = stackalloc byte[8];
        RandomNumberGenerator.Fill(b);
        return Convert.ToHexString(b).ToLowerInvariant();
    }

    private const string AllZero32 = "00000000000000000000000000000000";
    private const string AllZero16 = "0000000000000000";

    private static bool IsHex(string s)
    {
        foreach (var c in s)
            if (!Uri.IsHexDigit(c)) return false;
        return true;
    }

    private sealed class Restore(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}
