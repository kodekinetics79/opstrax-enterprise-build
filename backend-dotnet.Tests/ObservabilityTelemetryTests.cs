using Opstrax.Api.Observability;
using Xunit;

namespace Opstrax.Tests;

// ── Observability core primitives — pure-logic tests (no DB) ───────────────────
// Covers the reliability/observability layer added for enterprise health
// monitoring: trace context (W3C), log redaction, API metrics math, and SLO
// error-budget evaluation.

// ══════════════════════════════════════════════════════════════════════════════
// 1. TelemetryContext — W3C trace context
// ══════════════════════════════════════════════════════════════════════════════

public class TelemetryContextTests
{
    [Fact]
    public void NewContext_MintsValidTraceAndSpanIds()
    {
        var ctx = new TelemetryContext();
        Assert.Equal(32, ctx.TraceId.Length);   // 16 bytes hex
        Assert.Equal(16, ctx.SpanId.Length);     // 8 bytes hex
        Assert.Matches("^[0-9a-f]{32}$", ctx.TraceId);
        Assert.False(string.IsNullOrEmpty(ctx.CorrelationId));
    }

    [Fact]
    public void ToTraceParent_IsWellFormedW3C()
    {
        var ctx = new TelemetryContext();
        var tp = ctx.ToTraceParent();
        var parts = tp.Split('-');
        Assert.Equal(4, parts.Length);
        Assert.Equal("00", parts[0]);
        Assert.Equal(ctx.TraceId, parts[1]);
        Assert.Equal(ctx.SpanId, parts[2]);
    }

    [Fact]
    public void FromTraceParent_ContinuesInboundTrace()
    {
        var parentTrace = "0af7651916cd43dd8448eb211c80319c";
        var parentSpan  = "b7ad6b7169203331";
        var inbound = $"00-{parentTrace}-{parentSpan}-01";

        var ctx = TelemetryContext.FromTraceParent(inbound, "corr-123");

        Assert.Equal(parentTrace, ctx.TraceId);        // same distributed trace
        Assert.Equal(parentSpan, ctx.ParentSpanId);    // caller becomes our parent
        Assert.NotEqual(parentSpan, ctx.SpanId);       // we mint a fresh span
        Assert.Equal("corr-123", ctx.CorrelationId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("00-tooShort-b7ad6b7169203331-01")]
    [InlineData("00-00000000000000000000000000000000-b7ad6b7169203331-01")] // all-zero trace
    public void FromTraceParent_MalformedStartsNewRootTrace(string? bad)
    {
        var ctx = TelemetryContext.FromTraceParent(bad, null);
        Assert.Equal(32, ctx.TraceId.Length);
        Assert.Null(ctx.ParentSpanId);
        Assert.False(string.IsNullOrEmpty(ctx.CorrelationId));
    }

    [Fact]
    public void AmbientScope_IsRestoredOnDispose()
    {
        Assert.Null(TelemetryContext.Current);
        var ctx = new TelemetryContext();
        using (ctx.BeginScope())
        {
            Assert.Same(ctx, TelemetryContext.Current);
        }
        Assert.Null(TelemetryContext.Current);
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// 2. LogRedactor — no secrets / PII survive
// ══════════════════════════════════════════════════════════════════════════════

public class LogRedactorTests
{
    [Theory]
    [InlineData("Authorization: Bearer eyJabc.def.ghi123", "eyJabc.def.ghi123")]
    [InlineData("password=SuperSecret123", "SuperSecret123")]
    [InlineData("client_secret: hunter2value", "hunter2value")]
    [InlineData("Host=db.neon.tech;Password=pgpass;User ID=app", "pgpass")]
    [InlineData("token=abc123DEFtoken", "abc123DEFtoken")]
    public void Scrub_RemovesSecrets(string raw, string secret)
    {
        var scrubbed = LogRedactor.Scrub(raw);
        Assert.DoesNotContain(secret, scrubbed);
        Assert.Contains("REDACTED", scrubbed);
    }

    [Fact]
    public void Scrub_MasksEmail_KeepsDomainForDiagnosis()
    {
        var scrubbed = LogRedactor.Scrub("user jane.doe@acme.com failed login");
        Assert.DoesNotContain("jane.doe", scrubbed);
        Assert.Contains("acme.com", scrubbed);
    }

    [Fact]
    public void Scrub_RemovesJwt()
    {
        var jwt = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjMifQ.abcDEF123";
        var scrubbed = LogRedactor.Scrub($"session {jwt} expired");
        Assert.DoesNotContain(jwt, scrubbed);
    }

    [Fact]
    public void Scrub_RemovesCardNumber()
    {
        var scrubbed = LogRedactor.Scrub("card 4111 1111 1111 1111 declined");
        Assert.DoesNotContain("4111 1111 1111 1111", scrubbed);
    }

    [Fact]
    public void Scrub_LeavesOrdinaryTextIntact()
    {
        var scrubbed = LogRedactor.Scrub("GET /api/vehicles -> 200 in 42ms");
        Assert.Equal("GET /api/vehicles -> 200 in 42ms", scrubbed);
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// 3. ApiMetricsService — counters, rates, percentiles, endpoint normalization
// ══════════════════════════════════════════════════════════════════════════════

public class ApiMetricsServiceTests
{
    [Fact]
    public void Snapshot_ComputesRatesAndCounts()
    {
        var m = new ApiMetricsService();
        for (var i = 0; i < 98; i++) m.RecordRequest("/api/vehicles", 200, 10);
        m.RecordRequest("/api/vehicles", 404, 5);   // 1 client error
        m.RecordRequest("/api/vehicles", 500, 20);  // 1 server error

        var s = m.Snapshot();
        Assert.Equal(100, s.RequestCount);
        Assert.Equal(1, s.Count4xx);
        Assert.Equal(1, s.Count5xx);
        Assert.Equal(1.0, s.Rate5xxPct, 3);
        Assert.Equal(1.0, s.Rate4xxPct, 3);
    }

    [Fact]
    public void Snapshot_ComputesPercentiles()
    {
        var m = new ApiMetricsService();
        for (var i = 1; i <= 100; i++) m.RecordRequest("/api/x", 200, i); // 1..100 ms

        var s = m.Snapshot();
        Assert.InRange(s.LatencyP50Ms, 49, 51);
        Assert.InRange(s.LatencyP95Ms, 94, 96);
        Assert.InRange(s.LatencyP99Ms, 98, 100);
    }

    [Fact]
    public void TopEndpoints_NormalizesIdsAndRanksByErrors()
    {
        var m = new ApiMetricsService();
        m.RecordRequest("/api/tenants/42", 500, 10);
        m.RecordRequest("/api/tenants/99", 500, 10);
        m.RecordRequest("/api/health", 200, 1);

        var s = m.Snapshot();
        var top = Assert.Single(s.TopEndpoints, e => e.Endpoint == "/api/tenants/{id}");
        Assert.Equal(2, top.Count);          // both ids collapsed into one bucket
        Assert.Equal(2, top.ErrorCount);
    }

    [Fact]
    public void RecordDbQuery_TracksLatencyAndFailures()
    {
        var m = new ApiMetricsService();
        m.RecordDbQuery(10, failed: false);
        m.RecordDbQuery(30, failed: true);

        var s = m.Snapshot();
        Assert.Equal(2, s.DbQueries);
        Assert.Equal(1, s.DbFailures);
        Assert.Equal(20, s.DbAvgLatencyMs, 1);
    }

    [Fact]
    public void Prometheus_ExposesExpectedSeries()
    {
        var m = new ApiMetricsService();
        m.RecordRequest("/api/x", 500, 12);
        var text = m.ToPrometheus();
        Assert.Contains("opstrax_requests_total", text);
        Assert.Contains("opstrax_request_rate_5xx_pct", text);
        Assert.Contains("opstrax_request_latency_p95_ms", text);
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// 4. SloService — error-budget burn + status
// ══════════════════════════════════════════════════════════════════════════════

public class SloServiceTests
{
    [Fact]
    public void Definitions_CoverAllNineSlos()
    {
        Assert.Equal(9, SloService.Definitions.Count);
        Assert.Contains(SloService.Definitions, d => d.Id == "api_availability" && d.Target == 99.9);
        Assert.Contains(SloService.Definitions, d => d.Id == "api_latency_p95" && d.Target == 500);
        Assert.Contains(SloService.Definitions, d => d.Id == "api_5xx_rate" && d.Target == 0.5);
    }

    [Fact]
    public void AlertRules_CoverAllEightRules()
    {
        Assert.Equal(8, SloService.AlertRules.Count);
        Assert.Contains(SloService.AlertRules, r => r.Id == "api_down");
        Assert.Contains(SloService.AlertRules, r => r.Id == "high_5xx_rate");
        Assert.Contains(SloService.AlertRules, r => r.Id == "telematics_delay");
    }

    [Fact]
    public void Evaluate_HealthyWhenNoTraffic()
    {
        var slo = new SloService(new ApiMetricsService());
        var report = slo.Evaluate();
        // With zero traffic, availability=100 / latency=0 / 5xx=0 ⇒ not breached.
        Assert.NotEqual("breached", report.OverallStatus);
        var p95 = Assert.Single(report.Slos, r => r.Id == "api_latency_p95");
        Assert.Equal("healthy", p95.Status);
    }

    [Fact]
    public void Evaluate_Breached5xxRateShowsBurnOver100()
    {
        var metrics = new ApiMetricsService();
        // 10% 5xx rate — target is 0.5% ⇒ heavily breached.
        for (var i = 0; i < 90; i++) metrics.RecordRequest("/api/x", 200, 10);
        for (var i = 0; i < 10; i++) metrics.RecordRequest("/api/x", 500, 10);

        var report = new SloService(metrics).Evaluate();
        var fivexx = Assert.Single(report.Slos, r => r.Id == "api_5xx_rate");
        Assert.Equal("breached", fivexx.Status);
        Assert.True(fivexx.ErrorBudgetBurnPct > 100);
        Assert.Equal("breached", report.OverallStatus);
    }

    [Fact]
    public void Evaluate_LoginSignalDrivesLoginSlo()
    {
        var slo = new SloService(new ApiMetricsService());
        var report = slo.Evaluate(new SloSignals { LoginTotal = 1000, LoginSuccess = 999 });
        var login = Assert.Single(report.Slos, r => r.Id == "login_availability");
        Assert.NotEqual("unknown", login.Status);
        Assert.Equal(99.9, login.Actual!.Value, 1);  // 999/1000
    }

    [Fact]
    public void Evaluate_LoginUnknownWithoutSignal()
    {
        var report = new SloService(new ApiMetricsService()).Evaluate();
        var login = Assert.Single(report.Slos, r => r.Id == "login_availability");
        Assert.Equal("unknown", login.Status);
    }
}
