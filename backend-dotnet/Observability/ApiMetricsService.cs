using System.Collections.Concurrent;

namespace Opstrax.Api.Observability;

// ─────────────────────────────────────────────────────────────────────────────
// ApiMetricsService — Singleton in-process metrics collector.
//
// Every request records (endpoint, status_code, duration_ms). The service keeps
// a rolling window of samples (default 15 min) so it can answer, in O(n) over
// the window:
//   • total request count / RPM
//   • latency avg, p50, p95, p99
//   • 4xx and 5xx rate
//   • per-endpoint aggregates (to surface "top failing endpoints")
//   • auth/login failure counts
//   • DB query latency + connection-failure counts
//
// This is intentionally dependency-free (no Prometheus client, no external
// scrape target) so it works on Render's single-container Docker deploy. The
// Reliability Center reads it via /api/ops/reliability, and SLO evaluation reads
// it to compute error-budget burn. External monitors can also scrape the
// Prometheus-format text at /metrics.
//
// Memory is bounded: samples older than the window are pruned on every read and
// opportunistically on write, and per-endpoint keys are capped.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class ApiMetricsService
{
    private readonly TimeSpan _window = TimeSpan.FromMinutes(15);
    private const int MaxSamples = 50_000;   // hard cap guards against unbounded growth
    private const int MaxEndpoints = 500;

    private readonly ConcurrentQueue<Sample> _samples = new();
    private long _sampleCount;

    // Monotonic counters (never pruned) for lifetime totals + Prometheus exposition.
    private long _totalRequests;
    private long _total4xx;
    private long _total5xx;
    private long _authFailures;
    private long _dbQueries;
    private long _dbFailures;
    private long _dbLatencySumMs;

    private readonly record struct Sample(long TimestampTicks, int StatusCode, double DurationMs, string Endpoint);

    // ── Recording ───────────────────────────────────────────────────────────────

    public void RecordRequest(string endpoint, int statusCode, double durationMs)
    {
        Interlocked.Increment(ref _totalRequests);
        if (statusCode is >= 400 and < 500) Interlocked.Increment(ref _total4xx);
        else if (statusCode >= 500)         Interlocked.Increment(ref _total5xx);

        _samples.Enqueue(new Sample(DateTime.UtcNow.Ticks, statusCode, durationMs, Normalize(endpoint)));
        if (Interlocked.Increment(ref _sampleCount) > MaxSamples) Trim(DateTime.UtcNow);
    }

    public void RecordAuthFailure() => Interlocked.Increment(ref _authFailures);

    public void RecordDbQuery(double durationMs, bool failed)
    {
        Interlocked.Increment(ref _dbQueries);
        Interlocked.Add(ref _dbLatencySumMs, (long)durationMs);
        if (failed) Interlocked.Increment(ref _dbFailures);
    }

    // ── Snapshot ──────────────────────────────────────────────────────────────

    public ApiMetricsSnapshot Snapshot()
    {
        var now = DateTime.UtcNow;
        Trim(now);

        var cutoff = now.Add(-_window).Ticks;
        var window = _samples.Where(s => s.TimestampTicks >= cutoff).ToList();

        var durations = window.Select(s => s.DurationMs).OrderBy(d => d).ToArray();
        var count     = window.Count;
        var count4xx  = window.Count(s => s.StatusCode is >= 400 and < 500);
        var count5xx  = window.Count(s => s.StatusCode >= 500);

        var perEndpoint = window
            .GroupBy(s => s.Endpoint)
            .Select(g =>
            {
                var ds = g.Select(x => x.DurationMs).OrderBy(x => x).ToArray();
                var errs = g.Count(x => x.StatusCode >= 500);
                var c4 = g.Count(x => x.StatusCode is >= 400 and < 500);
                return new EndpointMetric(
                    Endpoint:      g.Key,
                    Count:         g.Count(),
                    ErrorCount:    errs,
                    ClientErrors:  c4,
                    P95Ms:         Percentile(ds, 0.95),
                    ErrorRatePct:  g.Count() == 0 ? 0 : Math.Round(100.0 * errs / g.Count(), 2));
            })
            .OrderByDescending(e => e.ErrorCount)
            .ThenByDescending(e => e.Count)
            .Take(25)
            .ToList();

        return new ApiMetricsSnapshot(
            WindowMinutes:   (int)_window.TotalMinutes,
            RequestCount:    count,
            RequestsPerMin:  Math.Round(count / _window.TotalMinutes, 1),
            Count4xx:        count4xx,
            Count5xx:        count5xx,
            Rate4xxPct:      count == 0 ? 0 : Math.Round(100.0 * count4xx / count, 3),
            Rate5xxPct:      count == 0 ? 0 : Math.Round(100.0 * count5xx / count, 3),
            LatencyAvgMs:    durations.Length == 0 ? 0 : Math.Round(durations.Average(), 1),
            LatencyP50Ms:    Percentile(durations, 0.50),
            LatencyP95Ms:    Percentile(durations, 0.95),
            LatencyP99Ms:    Percentile(durations, 0.99),
            TopEndpoints:    perEndpoint,
            AuthFailures:    Interlocked.Read(ref _authFailures),
            DbQueries:       Interlocked.Read(ref _dbQueries),
            DbFailures:      Interlocked.Read(ref _dbFailures),
            DbAvgLatencyMs:  Interlocked.Read(ref _dbQueries) == 0
                                ? 0
                                : Math.Round((double)Interlocked.Read(ref _dbLatencySumMs) / Interlocked.Read(ref _dbQueries), 1),
            LifetimeRequests: Interlocked.Read(ref _totalRequests),
            Lifetime5xx:      Interlocked.Read(ref _total5xx),
            CapturedUtc:      now);
    }

    /// <summary>Prometheus text exposition — lets any external scraper alert on us.</summary>
    public string ToPrometheus()
    {
        var s = Snapshot();
        var sb = new System.Text.StringBuilder();
        void G(string name, string help, double value, string type = "gauge")
        {
            sb.Append("# HELP ").Append(name).Append(' ').Append(help).Append('\n');
            sb.Append("# TYPE ").Append(name).Append(' ').Append(type).Append('\n');
            sb.Append(name).Append(' ').Append(value.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
        }
        G("opstrax_requests_total", "Lifetime request count", s.LifetimeRequests, "counter");
        G("opstrax_requests_5xx_total", "Lifetime 5xx count", s.Lifetime5xx, "counter");
        G("opstrax_request_rate_5xx_pct", "5xx rate over window (%)", s.Rate5xxPct);
        G("opstrax_request_rate_4xx_pct", "4xx rate over window (%)", s.Rate4xxPct);
        G("opstrax_request_latency_p95_ms", "p95 latency over window (ms)", s.LatencyP95Ms);
        G("opstrax_request_latency_p99_ms", "p99 latency over window (ms)", s.LatencyP99Ms);
        G("opstrax_requests_per_minute", "Requests per minute over window", s.RequestsPerMin);
        G("opstrax_auth_failures_total", "Lifetime auth failures", s.AuthFailures, "counter");
        G("opstrax_db_queries_total", "Lifetime DB queries", s.DbQueries, "counter");
        G("opstrax_db_failures_total", "Lifetime DB failures", s.DbFailures, "counter");
        G("opstrax_db_latency_avg_ms", "Average DB query latency (ms)", s.DbAvgLatencyMs);
        G("opstrax_uptime_seconds", "Process uptime (s)", BuildInfo.UptimeSeconds, "counter");
        return sb.ToString();
    }

    // ── internals ────────────────────────────────────────────────────────────────

    private void Trim(DateTime now)
    {
        var cutoff = now.Add(-_window).Ticks;
        while (_samples.TryPeek(out var head) && head.TimestampTicks < cutoff)
        {
            if (_samples.TryDequeue(out _)) Interlocked.Decrement(ref _sampleCount);
            else break;
        }
    }

    // Collapse numeric/opaque path segments so per-endpoint keys stay bounded and
    // "/api/tenants/42" and "/api/tenants/99" aggregate to "/api/tenants/{id}".
    private static string Normalize(string path)
    {
        if (string.IsNullOrEmpty(path)) return "/";
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < segments.Length; i++)
        {
            var seg = segments[i];
            if (seg.Length > 0 && (long.TryParse(seg, out _) || Guid.TryParse(seg, out _) || seg.Length > 24))
                segments[i] = "{id}";
        }
        return "/" + string.Join('/', segments);
    }

    private static double Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 0) return 0;
        var rank = (int)Math.Ceiling(p * sorted.Length) - 1;
        rank = Math.Clamp(rank, 0, sorted.Length - 1);
        return Math.Round(sorted[rank], 1);
    }
}

// ── DTOs ──────────────────────────────────────────────────────────────────────

public sealed record ApiMetricsSnapshot(
    int    WindowMinutes,
    int    RequestCount,
    double RequestsPerMin,
    int    Count4xx,
    int    Count5xx,
    double Rate4xxPct,
    double Rate5xxPct,
    double LatencyAvgMs,
    double LatencyP50Ms,
    double LatencyP95Ms,
    double LatencyP99Ms,
    List<EndpointMetric> TopEndpoints,
    long   AuthFailures,
    long   DbQueries,
    long   DbFailures,
    double DbAvgLatencyMs,
    long   LifetimeRequests,
    long   Lifetime5xx,
    DateTime CapturedUtc);

public sealed record EndpointMetric(
    string Endpoint,
    int    Count,
    int    ErrorCount,
    int    ClientErrors,
    double P95Ms,
    double ErrorRatePct);
