namespace Opstrax.Api.Observability;

// ─────────────────────────────────────────────────────────────────────────────
// SloService — Service Level Objectives + alerting rules, as code.
//
// The SLO targets (Requirement 7) and alert thresholds (Requirement 6) live here
// so they are versioned, testable, and surfaced verbatim in the Reliability
// Center. Evaluation is driven by the live ApiMetricsService window, so the
// admin sees real error-budget burn — not a static doc.
//
// Availability model: a request counts against the availability budget when it
// returns 5xx (server fault). p95 latency comes straight from the rolling window.
// Error budget = allowed bad-request fraction; burn = consumed/allowed.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class SloService(ApiMetricsService metrics)
{
    // ── SLO catalogue (Requirement 7) ────────────────────────────────────────────
    public static readonly IReadOnlyList<SloDefinition> Definitions =
    [
        new("api_availability",       "API availability",                 "availability", 99.9,  "%",  "30d"),
        new("api_latency_p95",        "API p95 latency",                  "latency",      500,   "ms", "window"),
        new("api_5xx_rate",           "API 5xx error rate",               "error_rate",   0.5,   "%",  "window"),
        new("login_availability",     "Login availability",               "availability", 99.5,  "%",  "30d"),
        new("fleet_location_fresh",   "Fleet location updates < 60s",     "freshness",    95.0,  "%",  "window"),
        new("telematics_processed",   "Telematics events processed < 2m", "throughput",   99.0,  "%",  "window"),
        new("p1_detection",           "P1 incident detection",            "mttd",         60,    "s",  "per-incident"),
        new("p1_acknowledgement",     "P1 incident acknowledgement",      "mtta",         300,   "s",  "per-incident"),
        new("p1_recovery",            "P1 incident recovery",             "mttr",         1800,  "s",  "per-incident"),
    ];

    // ── Alert rules (Requirement 6) ──────────────────────────────────────────────
    public static readonly IReadOnlyList<AlertRule> AlertRules =
    [
        new("api_down",              "API down > 60s",                     "critical", "External monitor: no 2xx from /health/live for 60s"),
        new("high_5xx_rate",         "5xx rate > 1% for 5m",               "critical", "rate_5xx_pct > 1 sustained 5m"),
        new("high_latency_p95",      "p95 latency > 1s for 5m",            "high",     "latency_p95_ms > 1000 sustained 5m"),
        new("db_failures",           "DB failures over threshold",         "critical", "db_failures increasing / connection unavailable"),
        new("queue_delay",           "Queue/worker delay > 2m",            "high",     "background service heartbeat stale > 2m"),
        new("auth_failure_spike",    "Login/auth failure spike",           "high",     "auth_failures rate abnormal"),
        new("telematics_delay",      "Telematics ingestion delay > 2m",    "high",     "latest telemetry received_at older than 2m"),
        new("critical_workflow_fail","Critical create/update workflow fail","critical","5xx on POST/PUT/PATCH create-update routes"),
    ];

    /// <summary>Evaluate every SLO against the live metrics window + supplied signals.</summary>
    public SloReport Evaluate(SloSignals? signals = null)
    {
        signals ??= new SloSignals();
        var m = metrics.Snapshot();
        var results = new List<SloResult>();

        // API availability — inverse of 5xx rate over the window.
        var availabilityPct = 100.0 - m.Rate5xxPct;
        results.Add(EvalMin("api_availability", availabilityPct, m.RequestCount));

        // p95 latency (lower is better).
        results.Add(EvalMax("api_latency_p95", m.LatencyP95Ms, m.RequestCount));

        // 5xx error rate (lower is better).
        results.Add(EvalMax("api_5xx_rate", m.Rate5xxPct, m.RequestCount));

        // Login availability — from supplied login success ratio when known.
        if (signals.LoginTotal > 0)
        {
            var loginPct = 100.0 * signals.LoginSuccess / signals.LoginTotal;
            results.Add(EvalMin("login_availability", loginPct, (int)Math.Min(signals.LoginTotal, int.MaxValue)));
        }
        else results.Add(Unknown("login_availability"));

        // Fleet location freshness.
        results.Add(signals.FleetFreshPct is { } ff
            ? EvalMin("fleet_location_fresh", ff, signals.FleetSampleCount)
            : Unknown("fleet_location_fresh"));

        // Telematics processed-in-time.
        results.Add(signals.TelematicsProcessedPct is { } tp
            ? EvalMin("telematics_processed", tp, signals.TelematicsSampleCount)
            : Unknown("telematics_processed"));

        // P1 timing SLOs are per-incident targets — reported as definitions,
        // measured off the incident audit trail (see ReliabilityService).
        results.Add(DefinitionOnly("p1_detection"));
        results.Add(DefinitionOnly("p1_acknowledgement"));
        results.Add(DefinitionOnly("p1_recovery"));

        var breached = results.Count(r => r.Status == "breached");
        var atRisk   = results.Count(r => r.Status == "at_risk");
        var overall  = breached > 0 ? "breached" : atRisk > 0 ? "at_risk" : "healthy";

        return new SloReport(overall, results, m.CapturedUtc);
    }

    // For a "min" SLO (higher actual is better, e.g. availability): budget is the
    // allowed shortfall below 100; burn = observed shortfall / allowed shortfall.
    private static SloResult EvalMin(string id, double actual, int sample)
    {
        var def = Def(id);
        var allowedShortfall = 100.0 - def.Target;                       // e.g. 0.1 for 99.9
        var observedShortfall = Math.Max(0, 100.0 - actual);
        var burn = allowedShortfall <= 0 ? 0 : observedShortfall / allowedShortfall;
        var status = actual >= def.Target ? (burn > 0.75 ? "at_risk" : "healthy") : "breached";
        return new SloResult(id, def.Name, def.Target, Math.Round(actual, 3), def.Unit,
            status, Math.Round(Math.Min(burn, 9.99) * 100, 1), sample);
    }

    // For a "max" SLO (lower actual is better, e.g. latency/error-rate).
    private static SloResult EvalMax(string id, double actual, int sample)
    {
        var def = Def(id);
        var burn = def.Target <= 0 ? 0 : actual / def.Target;
        var status = actual <= def.Target ? (burn > 0.75 ? "at_risk" : "healthy") : "breached";
        return new SloResult(id, def.Name, def.Target, Math.Round(actual, 3), def.Unit,
            status, Math.Round(Math.Min(burn, 9.99) * 100, 1), sample);
    }

    private static SloResult Unknown(string id)
    {
        var def = Def(id);
        return new SloResult(id, def.Name, def.Target, null, def.Unit, "unknown", 0, 0);
    }

    private static SloResult DefinitionOnly(string id)
    {
        var def = Def(id);
        return new SloResult(id, def.Name, def.Target, null, def.Unit, "defined", 0, 0);
    }

    private static SloDefinition Def(string id) =>
        Definitions.First(d => d.Id == id);

    // ── Error-budget burn rate (predict outage BEFORE the budget is exhausted) ────
    // The SRE burn-rate signal: how fast the current 5xx rate is consuming the
    // monthly availability budget. A 14.4x burn exhausts a 30-day budget in ~2h — a
    // fast-burn page; 6x is a slow-burn warning. This is the "downtime reporting
    // ahead of any outage" capability — it fires while the service is still up but
    // trending to breach, not after it is already down.
    public BurnRateStatus EvaluateBurnRate()
    {
        var m = metrics.Snapshot();
        var target = Def("api_availability").Target;         // 99.9
        var allowedBadRate = (100.0 - target) / 100.0;        // 0.001 (0.1%)
        var observedBadRate = m.Rate5xxPct / 100.0;
        var burn = allowedBadRate <= 0 ? 0 : observedBadRate / allowedBadRate;

        // Google SRE multi-window thresholds.
        var (severity, action) =
            burn >= 14.4 ? ("critical", "page_now")   // budget gone in ~2h
          : burn >= 6.0  ? ("high",     "alert")       // budget gone in ~6h
          : burn >= 3.0  ? ("medium",   "watch")       // elevated
          :                ("ok",       "none");

        return new BurnRateStatus(
            BurnRate:        Math.Round(burn, 2),
            Severity:        severity,
            RecommendedAction: action,
            ObservedErrorRatePct: m.Rate5xxPct,
            BudgetErrorRatePct:   Math.Round(allowedBadRate * 100, 3),
            SampleCount:     m.RequestCount);
    }
}

public sealed record BurnRateStatus(
    double BurnRate,
    string Severity,        // ok | medium | high | critical
    string RecommendedAction,
    double ObservedErrorRatePct,
    double BudgetErrorRatePct,
    int    SampleCount);

// ── DTOs ──────────────────────────────────────────────────────────────────────

public sealed record SloDefinition(
    string Id, string Name, string Kind, double Target, string Unit, string Window);

public sealed record AlertRule(
    string Id, string Name, string Severity, string Condition);

public sealed record SloResult(
    string  Id,
    string  Name,
    double  Target,
    double? Actual,
    string  Unit,
    string  Status,          // healthy | at_risk | breached | unknown | defined
    double  ErrorBudgetBurnPct,
    int     SampleCount);

public sealed record SloReport(
    string OverallStatus,
    List<SloResult> Slos,
    DateTime CapturedUtc);

// Signals the caller can feed from DB-derived measurements (login ratio, fleet
// freshness, telematics processing). All optional — SLOs without a signal report
// "unknown" rather than a fabricated value.
public sealed class SloSignals
{
    public long LoginTotal { get; set; }
    public long LoginSuccess { get; set; }
    public double? FleetFreshPct { get; set; }
    public int FleetSampleCount { get; set; }
    public double? TelematicsProcessedPct { get; set; }
    public int TelematicsSampleCount { get; set; }
}
