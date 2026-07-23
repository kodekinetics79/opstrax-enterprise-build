using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Opstrax.Telematics.Gateway.Observability;

namespace Opstrax.Telematics.IntegrationTests;

/// <summary>
/// Verifies the isolated OpenTelemetry primitives in <c>Gateway/Observability</c>: that the
/// concrete instruments from <c>metrics.md</c> are registered on the meter, that a
/// <see cref="PipelineTrace"/> emits a span carrying the required attributes, and that
/// <see cref="TelematicsObservabilityExtensions.AddTelematicsObservability"/> wires up both
/// providers. Everything is observed through an in-memory <see cref="ActivityListener"/> /
/// <see cref="MeterListener"/> — no exporter, no collector, no socket.
/// </summary>
public sealed class ObservabilityInstrumentationTests
{
    [Fact]
    public void AllRequiredInstruments_AreRegisteredOnTheMeter()
    {
        // Touch a static field to force the type initializer to create the Meter + instruments
        // (const references alone are inlined and would not trigger it).
        _ = TelematicsInstrumentation.Meter;

        var published = new HashSet<string>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == TelematicsInstrumentation.MeterName)
                    published.Add(instrument.Name);
            },
        };
        listener.Start();

        string[] required =
        {
            "opstrax_telematics_packets_accepted",
            "opstrax_telematics_packets_rejected",
            "opstrax_telematics_unknown_devices",
            "opstrax_telematics_auth_failures",
            "opstrax_telematics_replay_rejections",
            "opstrax_telematics_duplicate_packets",
            "opstrax_telematics_out_of_order",
            "opstrax_telematics_decode_latency_ms",
            "opstrax_telematics_e2e_latency_ms",
            "opstrax_telematics_active_connections",
        };

        Assert.All(required, name => Assert.Contains(name, published));
    }

    [Fact]
    public void Counter_Increment_IsObservedWithBoundedLabels()
    {
        _ = TelematicsInstrumentation.Meter;

        long observed = 0;
        object? seenCompany = null;
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == TelematicsInstrumentation.MeterName)
                    l.EnableMeasurementEvents(instrument);
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
        {
            if (instrument.Name != "opstrax_telematics_packets_accepted")
                return;
            observed += measurement;
            foreach (KeyValuePair<string, object?> tag in tags)
                if (tag.Key == TelematicsInstrumentation.MetricLabels.CompanyId)
                    seenCompany = tag.Value;
        });
        listener.Start();

        TelematicsInstrumentation.PacketsAccepted.Add(1, new TagList
        {
            { TelematicsInstrumentation.MetricLabels.Protocol, "gt06" },
            { TelematicsInstrumentation.MetricLabels.CompanyId, 4L },
        });

        Assert.Equal(1, observed);
        Assert.Equal(4L, seenCompany);
    }

    [Fact]
    public void PipelineTrace_EmitsSpanChain_WithRequiredAttributes()
    {
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == TelematicsInstrumentation.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);

        Guid correlationId = Guid.NewGuid();
        Guid tenantId = Guid.NewGuid();
        Guid eventId = Guid.NewGuid();

        using (PipelineTrace trace = PipelineTrace.StartPacketReceive(
                   correlationId, "conn-7", "gt06", imei: "123456789012345", gatewayInstance: "gw-1"))
        {
            trace.SetAdapter("GT06", "1.4.0");

            using (Activity? decode = trace.StartDecode())
            {
                Assert.NotNull(decode);
                Assert.Equal("decode", decode!.OperationName);
            }

            trace.ResolveOwnership(tenantId, companyId: 4L, deviceId: "device-9", vehicleId: 42L);
            trace.SetEventId(eventId);

            using (Activity? publish = trace.StartPublish())
            {
                Assert.NotNull(publish);
            }
        }

        // Root packet-receive span carries correlation + resolved ownership + adapter.
        Activity root = Assert.Single(stopped, a => a.OperationName == "packet-receive");
        Assert.Equal(correlationId.ToString(), root.GetTagItem("telematics.correlation_id"));
        Assert.Equal("conn-7", root.GetTagItem("telematics.connection_id"));
        Assert.Equal("gt06", root.GetTagItem("telematics.protocol"));
        Assert.Equal(tenantId.ToString(), root.GetTagItem("tenant.id"));
        Assert.Equal(4L, root.GetTagItem("company.id"));
        Assert.Equal("device-9", root.GetTagItem("device.id"));
        Assert.Equal(42L, root.GetTagItem("vehicle.id"));
        Assert.Equal("GT06", root.GetTagItem("adapter.name"));
        Assert.Equal("1.4.0", root.GetTagItem("adapter.version"));
        Assert.Equal(eventId.ToString(), root.GetTagItem("telematics.event_id"));

        // The publish child span was started after ownership resolved, so it carries it too.
        Activity publishSpan = Assert.Single(stopped, a => a.OperationName == "event-publish");
        Assert.Equal(ActivityKind.Producer, publishSpan.Kind);
        Assert.Equal(correlationId.ToString(), publishSpan.GetTagItem("telematics.correlation_id"));
        Assert.Equal(tenantId.ToString(), publishSpan.GetTagItem("tenant.id"));
        Assert.Equal("device-9", publishSpan.GetTagItem("device.id"));
        Assert.Equal("GT06", publishSpan.GetTagItem("adapter.name"));
        Assert.Equal("1.4.0", publishSpan.GetTagItem("adapter.version"));

        // The decode child was started before ownership resolved: adapter present, tenant absent
        // (honest attribution — never a guessed owner).
        Activity decodeSpan = Assert.Single(stopped, a => a.OperationName == "decode");
        Assert.Equal("GT06", decodeSpan.GetTagItem("adapter.name"));
        Assert.Null(decodeSpan.GetTagItem("tenant.id"));
    }

    [Fact]
    public void MarkRejected_SetsReasonAndErrorStatus()
    {
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == TelematicsInstrumentation.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);

        using (PipelineTrace trace = PipelineTrace.StartPacketReceive(Guid.NewGuid(), "conn-9", "gt06"))
        {
            using Activity? validation = trace.StartValidation();
            trace.MarkRejected(validation, "bad_checksum", "checksum mismatch");
        }

        Activity validationSpan = Assert.Single(stopped, a => a.OperationName == "validation");
        Assert.Equal("bad_checksum", validationSpan.GetTagItem("telematics.rejection_reason"));
        Assert.Equal(ActivityStatusCode.Error, validationSpan.Status);

        Activity root = Assert.Single(stopped, a => a.OperationName == "packet-receive");
        Assert.Equal("bad_checksum", root.GetTagItem("telematics.rejection_reason"));
        Assert.Equal(ActivityStatusCode.Error, root.Status);
    }

    [Fact]
    public void AddTelematicsObservability_RegistersBothProviders_NoEndpointIsNoOp()
    {
        var services = new ServiceCollection();

        // No configuration and (assuming) no OTEL_EXPORTER_OTLP_ENDPOINT set → no-op exporter path.
        services.AddTelematicsObservability(configuration: null);

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<TracerProvider>());
        Assert.NotNull(provider.GetService<MeterProvider>());
    }
}
