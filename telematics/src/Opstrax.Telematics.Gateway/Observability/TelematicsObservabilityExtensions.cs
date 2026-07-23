using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Opstrax.Telematics.Gateway.Observability;

/// <summary>
/// Wires the gateway's <see cref="TelematicsInstrumentation"/> source/meter into a
/// <see cref="TracerProvider"/> and a <see cref="MeterProvider"/> and registers both as
/// container-owned singletons (so the DI scope flushes and disposes them on shutdown).
/// </summary>
/// <remarks>
/// <para>
/// <b>Exporter policy.</b> The OTLP endpoint is read from configuration
/// (<c>Telematics:Otlp:Endpoint</c>) or the standard <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>
/// environment variable. When it is <b>set</b>, an OTLP exporter is attached. When it is
/// <b>unset</b>, the providers are still built and still record spans/measurements
/// in-process, but <b>no</b> exporter is attached — a safe no-op default for local/dev/test
/// runs that never opens a socket to a collector that isn't there.
/// </para>
/// <para>
/// <b>Exemplars.</b> The meter provider runs the trace-based exemplar filter so every
/// rejection/latency series carries a sampled <c>trace_id</c>, giving the one-click
/// metric→trace pivot the SLOs depend on.
/// </para>
/// <para>
/// <b>Only three packages.</b> This deliberately uses the raw <see cref="Sdk"/> builders (from
/// the core <c>OpenTelemetry</c> package) rather than the hosting integration, so the Gateway
/// takes on only <c>OpenTelemetry</c>, <c>OpenTelemetry.Exporter.OpenTelemetryProtocol</c> and
/// <c>System.Diagnostics.DiagnosticSource</c>. The providers are registered by factory so the
/// container creates and therefore disposes them; resolve them at startup to activate.
/// </para>
/// </remarks>
public static class TelematicsObservabilityExtensions
{
    /// <summary>Configuration section root for the gateway's OTLP settings.</summary>
    public const string OtlpEndpointKey = "Telematics:Otlp:Endpoint";

    /// <summary>Optional head-sampling ratio (0..1). Defaults to 1.0 for the gateway; collector tail-sampling keeps failures.</summary>
    public const string OtlpSampleRatioKey = "Telematics:Otlp:SampleRatio";

    /// <summary>The service name stamped on the OTel resource for every span/metric the gateway emits.</summary>
    public const string ServiceName = "opstrax.telematics.gateway";

    private const string EnvEndpoint = "OTEL_EXPORTER_OTLP_ENDPOINT";

    /// <summary>Decode-latency histogram bucket boundaries (ms), per metrics.md.</summary>
    private static readonly double[] DecodeLatencyBucketsMs =
        { 1, 2, 5, 10, 25, 50, 100, 250, 500, 1000 };

    /// <summary>End-to-end latency histogram bucket boundaries (ms) — wider tail, to 5000.</summary>
    private static readonly double[] E2eLatencyBucketsMs =
        { 5, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000 };

    /// <summary>
    /// Registers the gateway's tracer and meter providers, exporting via OTLP when an endpoint is
    /// configured and running as an in-process no-op otherwise.
    /// </summary>
    /// <param name="services">The DI container to register the providers in.</param>
    /// <param name="configuration">
    /// Optional configuration source for the OTLP endpoint / sample ratio. When
    /// <see langword="null"/>, only the <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> environment variable is
    /// consulted.
    /// </param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddTelematicsObservability(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        string? endpoint = configuration?[OtlpEndpointKey];
        if (string.IsNullOrWhiteSpace(endpoint))
            endpoint = Environment.GetEnvironmentVariable(EnvEndpoint);

        double sampleRatio = 1.0;
        if (double.TryParse(configuration?[OtlpSampleRatioKey], out double parsed) && parsed is >= 0 and <= 1)
            sampleRatio = parsed;

        ResourceBuilder BuildResource() => ResourceBuilder.CreateDefault()
            .AddService(ServiceName, serviceVersion: TelematicsInstrumentation.Version);

        services.AddSingleton(_ =>
        {
            TracerProviderBuilder builder = Sdk.CreateTracerProviderBuilder()
                .SetResourceBuilder(BuildResource())
                .AddSource(TelematicsInstrumentation.ActivitySourceName)
                .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(sampleRatio)));

            if (!string.IsNullOrWhiteSpace(endpoint))
                builder.AddOtlpExporter(o => o.Endpoint = new Uri(endpoint));

            return builder.Build();
        });

        services.AddSingleton(_ =>
        {
            MeterProviderBuilder builder = Sdk.CreateMeterProviderBuilder()
                .SetResourceBuilder(BuildResource())
                .AddMeter(TelematicsInstrumentation.MeterName)
                // Exemplars link a spiking series straight to a trace_id.
                .SetExemplarFilter(ExemplarFilterType.TraceBased)
                .AddView(
                    TelematicsInstrumentation.DecodeLatencyMs.Name,
                    new ExplicitBucketHistogramConfiguration { Boundaries = DecodeLatencyBucketsMs })
                .AddView(
                    TelematicsInstrumentation.E2eLatencyMs.Name,
                    new ExplicitBucketHistogramConfiguration { Boundaries = E2eLatencyBucketsMs });

            if (!string.IsNullOrWhiteSpace(endpoint))
                builder.AddOtlpExporter(o => o.Endpoint = new Uri(endpoint));

            return builder.Build();
        });

        return services;
    }
}
