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
}
