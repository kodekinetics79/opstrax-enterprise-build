using System.Diagnostics;
using Opstrax.Api.Controllers;

namespace Opstrax.Api.Observability;

// ─────────────────────────────────────────────────────────────────────────────
// RequestTelemetryMiddleware — the single entry point that gives every request
// a trace identity, times it, records its metrics, and logs it as JSON.
//
// Placed FIRST in the pipeline (before error-handling, auth, RLS) so:
//   • it reads/continues the inbound W3C traceparent + correlation_id headers,
//   • binds a TelemetryContext as ambient for the whole async flow (so services,
//     DB calls, and the JSON logger all stamp the SAME trace_id),
//   • echoes trace_id / correlation_id back on the response so the frontend can
//     display them and a failed call is traceable end-to-end,
//   • on completion records (endpoint, status, duration) into ApiMetricsService
//     and emits one structured access log, enriching tenant_id/user_id/role that
//     downstream auth middleware wrote into HttpContext.Items.
//
// Health probes are still traced (cheap) but logged at Debug to avoid noise.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class RequestTelemetryMiddleware(
    RequestDelegate next,
    ApiMetricsService metrics,
    ILogger<RequestTelemetryMiddleware> logger)
{
    public const string TraceHeader        = "traceparent";
    public const string CorrelationHeader  = "X-Correlation-Id";
    public const string TraceIdHeader      = "X-Trace-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "/";

        // Continue an inbound trace if the frontend/proxy sent one; else start a root.
        var inboundTrace = context.Request.Headers.TryGetValue(TraceHeader, out var tp) ? tp.ToString() : null;
        var inboundCorr  = context.Request.Headers.TryGetValue(CorrelationHeader, out var cid) ? cid.ToString() : null;

        var ctx = TelemetryContext.FromTraceParent(inboundTrace, inboundCorr);
        ctx.Endpoint = path;
        ctx.Method   = context.Request.Method;

        context.Items[TelemetryContext.HttpItemKey] = ctx;

        // Echo trace ids back so the client can surface them and correlate errors.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[TraceIdHeader]     = ctx.TraceId;
            context.Response.Headers[CorrelationHeader] = ctx.CorrelationId;
            context.Response.Headers["X-Deployment-Version"] = ctx.DeploymentVersion;
            return Task.CompletedTask;
        });

        // Bind ambient for the whole request flow + start a root Activity so any
        // OpenTelemetry exporter (OTLP) added later picks it up automatically.
        using var scope = ctx.BeginScope();
        using var activity = ObservabilityActivitySource.Source.StartActivity(
            $"{context.Request.Method} {path}", ActivityKind.Server);
        activity?.SetTag("http.method", context.Request.Method);
        activity?.SetTag("http.route", path);
        activity?.SetTag("opstrax.correlation_id", ctx.CorrelationId);
        activity?.SetTag("opstrax.deployment_version", ctx.DeploymentVersion);

        var sw = Stopwatch.StartNew();
        try
        {
            await next(context);
        }
        finally
        {
            sw.Stop();

            // Enrich with identity the auth middleware resolved into Items.
            EnrichIdentity(context, ctx);

            var status = context.Response.StatusCode;
            metrics.RecordRequest(path, status, sw.Elapsed.TotalMilliseconds);
            if (status == StatusCodes.Status401Unauthorized &&
                path.Contains("/auth/login", StringComparison.OrdinalIgnoreCase))
                metrics.RecordAuthFailure();

            activity?.SetTag("http.status_code", status);
            activity?.SetTag("opstrax.tenant_id", ctx.TenantId);
            activity?.SetTag("duration_ms", sw.Elapsed.TotalMilliseconds);
            if (status >= 500) activity?.SetStatus(ActivityStatusCode.Error);

            EmitAccessLog(path, context, status, sw.Elapsed.TotalMilliseconds);
        }
    }

    private static void EnrichIdentity(HttpContext context, TelemetryContext ctx)
    {
        if (context.Items.TryGetValue(EndpointMappings.AuthCompanyIdItemKey, out var cid) && cid is not null)
            ctx.TenantId = cid.ToString();
        if (context.Items.TryGetValue(EndpointMappings.AuthUserIdItemKey, out var uid) && uid is not null)
            ctx.UserId = uid.ToString();
        if (context.Items.TryGetValue(EndpointMappings.AuthRoleItemKey, out var role) && role is not null)
            ctx.Role = role.ToString();
    }

    private void EmitAccessLog(string path, HttpContext context, int status, double durationMs)
    {
        var isProbe = path.StartsWith("/health", StringComparison.OrdinalIgnoreCase) ||
                      path.StartsWith("/ready",  StringComparison.OrdinalIgnoreCase) ||
                      path == "/metrics";

        var level = status >= 500 ? LogLevel.Error
                  : status >= 400 ? LogLevel.Warning
                  : isProbe       ? LogLevel.Debug
                  :                 LogLevel.Information;

        // The JSON logger stamps trace/correlation/tenant automatically from ambient ctx.
        logger.Log(level, new EventId(status, "http_request"),
            "{Method} {Path} -> {Status} in {DurationMs}ms",
            context.Request.Method, path, status, Math.Round(durationMs, 1));
    }
}

// Shared ActivitySource so future OTLP export lights up with zero code churn.
public static class ObservabilityActivitySource
{
    public const string Name = "Opstrax.Api";
    public static readonly ActivitySource Source = new(Name, BuildInfo.Version);
}
