using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Opstrax.Api.Middleware;
using Opstrax.Api.Observability;

namespace Opstrax.Tests;

public sealed class ErrorHandlingMiddlewareTests
{
    [Fact]
    public async Task BadHttpRequest_ReturnsSafeStandard400Envelope_AndPreservesTraceHeaders()
    {
        const string secretDetail = "JSON parser leaked /srv/private/RequestDto.cs and SECRET-VALUE";
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/operations/proof-packages";
        context.Response.Body = new MemoryStream();
        context.Response.Headers[RequestTelemetryMiddleware.TraceIdHeader] = "trace-before-clear";
        context.Response.Headers[RequestTelemetryMiddleware.CorrelationHeader] = "correlation-before-clear";
        var middleware = new ErrorHandlingMiddleware(
            _ => throw new BadHttpRequestException(secretDetail),
            NullLogger<ErrorHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal("trace-before-clear", context.Response.Headers[RequestTelemetryMiddleware.TraceIdHeader]);
        Assert.Equal("correlation-before-clear", context.Response.Headers[RequestTelemetryMiddleware.CorrelationHeader]);
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.DoesNotContain(secretDetail, body, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestDto", body, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(body);
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("Invalid request body", json.RootElement.GetProperty("message").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("data").ValueKind);
        Assert.Empty(json.RootElement.GetProperty("errors").EnumerateArray());
    }

    [Fact]
    public async Task MalformedJson_OnDispatchAndProofRoutes_Is400TraceableAndCountedAsClientError()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<ApiMetricsService>();
        builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
        var app = builder.Build();
        app.UseMiddleware<RequestTelemetryMiddleware>();
        app.UseMiddleware<ErrorHandlingMiddleware>();
        app.MapPost("/api/dispatch/assignments", (RepresentativeDispatchBody body) => Results.Ok(body));
        app.MapPost("/api/operations/proof-packages", (RepresentativeProofBody body) => Results.Ok(body));

        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features
                .Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            foreach (var path in new[] { "/api/dispatch/assignments", "/api/operations/proof-packages" })
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, path)
                {
                    Content = new StringContent("{\"value\":", Encoding.UTF8, "application/json"),
                };
                request.Headers.Add(RequestTelemetryMiddleware.CorrelationHeader, "pilot-malformed-json");
                using var response = await client.SendAsync(request);

                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                Assert.Equal("pilot-malformed-json",
                    response.Headers.GetValues(RequestTelemetryMiddleware.CorrelationHeader).Single());
                Assert.False(string.IsNullOrWhiteSpace(
                    response.Headers.GetValues(RequestTelemetryMiddleware.TraceIdHeader).Single()));
                var body = await response.Content.ReadAsStringAsync();
                Assert.DoesNotContain("JsonException", body, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Representative", body, StringComparison.OrdinalIgnoreCase);
                using var json = JsonDocument.Parse(body);
                Assert.False(json.RootElement.GetProperty("success").GetBoolean());
                Assert.Equal("Invalid request body", json.RootElement.GetProperty("message").GetString());
                Assert.Empty(json.RootElement.GetProperty("errors").EnumerateArray());
            }

            var metrics = app.Services.GetRequiredService<ApiMetricsService>().Snapshot();
            Assert.Equal(2, metrics.Count4xx);
            Assert.Equal(0, metrics.Count5xx);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    public sealed record RepresentativeDispatchBody(long? JobId, long VehicleId, long DriverId);
    public sealed record RepresentativeProofBody(long JobId, string ProofType);
}
