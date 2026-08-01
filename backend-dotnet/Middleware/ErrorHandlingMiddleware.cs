using System.Net;
using Opstrax.Api.DTOs;
using Opstrax.Api.Observability;

namespace Opstrax.Api.Middleware;

public sealed class ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (BadHttpRequestException)
        {
            // Minimal-API JSON binding raises BadHttpRequestException before the
            // endpoint delegate runs. It is a safe client error, not a server fault.
            // Do not log or return the parser exception because it can contain body
            // fragments, CLR type names, or implementation details.
            logger.LogWarning(new EventId(400, "invalid_request_body"),
                "Invalid API request body on {Method} {Path}",
                context.Request.Method, context.Request.Path.Value);

            if (context.Response.HasStarted) return;

            var traceHeader = context.Response.Headers[RequestTelemetryMiddleware.TraceIdHeader].ToString();
            var correlationHeader = context.Response.Headers[RequestTelemetryMiddleware.CorrelationHeader].ToString();
            var telemetry = TelemetryContext.Current;
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            RestoreTelemetryHeader(context, RequestTelemetryMiddleware.TraceIdHeader,
                traceHeader, telemetry?.TraceId);
            RestoreTelemetryHeader(context, RequestTelemetryMiddleware.CorrelationHeader,
                correlationHeader, telemetry?.CorrelationId);
            await context.Response.WriteAsJsonAsync(ApiResponse<object>.Fail("Invalid request body"));
        }
        catch (Exception ex)
        {
            var ctx = TelemetryContext.Current;
            var traceId       = ctx?.TraceId ?? "n/a";
            var correlationId = ctx?.CorrelationId ?? "n/a";

            // Structured error log — error_code + module + endpoint + tenant are all
            // stamped by the JSON logger from the ambient TelemetryContext; the
            // EventId name becomes the machine-readable error_code.
            logger.LogError(new EventId(500, "unhandled_exception"), ex,
                "Unhandled API error on {Method} {Path}",
                context.Request.Method, context.Request.Path.Value);

            if (context.Response.HasStarted)
            {
                // Response already flushing — cannot rewrite; the log above is the record.
                return;
            }

            context.Response.Clear();
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            // Surface the trace id to the caller (never the exception detail) so a
            // user-reported failure can be pivoted to its server-side trace in <60s.
            await context.Response.WriteAsJsonAsync(ApiResponse<object>.Fail(
                "Internal server error",
                $"An unexpected error occurred. Reference: {correlationId} (trace {traceId})."));
        }
    }

    private static void RestoreTelemetryHeader(HttpContext context, string name, string existing, string? ambient)
    {
        var value = string.IsNullOrWhiteSpace(existing) ? ambient : existing;
        if (!string.IsNullOrWhiteSpace(value)) context.Response.Headers[name] = value;
    }
}
