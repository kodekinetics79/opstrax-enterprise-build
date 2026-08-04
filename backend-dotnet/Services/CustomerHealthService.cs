using Npgsql;
using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

// Customer health scoring — computed from REAL delivery history, never invented.
//
// Before this service, customers.sla_health_score / delivery_experience_score / risk_score
// were literals written once at INSERT time (94 / 92 / 18) and never recomputed, so every
// "SLA Health" number in the product was fiction. Here the three scores are derived from
// data the tenant actually generated:
//
//   sla_health_score          on-time delivery rate  (delivered_at <= promised_at) over the
//                             completed jobs in the window.
//   delivery_experience_score POD completeness + (1 - exception rate) + customer feedback
//                             rating, weight-renormalised over whichever inputs exist.
//   risk_score                inverse of the two above, penalised by OPEN exceptions,
//                             OVERDUE invoices, and a negative recent trend.
//
// HONESTY RULE: a customer with less than MinCompletedJobs completed jobs in the window has
// no evidence to score. It returns NULL for all three scores and State="insufficient_data",
// and the UI shows "Not enough data". There is no fallback number, ever.
//
// Every statement is scoped by company_id (multi-tenant).
public sealed class CustomerHealthService(Database db)
{
    private const int WindowDays = 180;
    private const int RecentDays = 30;
    private const int MinCompletedJobs = 3;      // below this: unrated, no invented score
    private const int MinMeasurableJobs = 3;     // below this: no on-time rate (no promise dates)
    private const int RefreshStalenessMinutes = 5;

    // Schema capability probe — the deployed DB is assembled by several *SchemaService
    // classes plus SQL migrations, and the optional evidence tables/columns are not
    // guaranteed to exist in every environment. Probed once per process; a missing input
    // is simply dropped from the model (never faked).
    private static Capabilities? _caps;
    private static readonly SemaphoreSlim ProbeLock = new(1, 1);

    private sealed record Capabilities(
        bool JobsSlaDueAt, bool JobsSlaWindowEnd, bool JobsEta, bool JobsUpdatedAt,
        bool JobsProofStatus, bool JobsSlaStatus, bool JobsDeletedAt,
        bool Feedback, bool Invoices);

    public sealed record CustomerHealth(
        long CustomerId,
        decimal? SlaHealthScore,
        decimal? DeliveryExperienceScore,
        decimal? RiskScore,
        string State,
        int TotalJobs,
        int CompletedJobs,
        int MeasurableJobs,
        int OnTimeJobs,
        decimal? OnTimeRate,
        decimal? PodCompletionRate,
        decimal? ExceptionRate,
        decimal? AvgFeedbackRating,
        int FeedbackCount,
        int OpenExceptions,
        int OverdueInvoices,
        decimal? RecentOnTimeRate,
        int WindowDaysUsed);

    // ── public API ────────────────────────────────────────────────────────────────

    /// <summary>Health for one customer. NULL when the customer is not in this tenant.</summary>
    public async Task<CustomerHealth?> GetAsync(long companyId, long customerId, CancellationToken ct = default)
    {
        var rows = await ComputeAsync(companyId, customerId, ct);
        return rows.Count == 0 ? null : rows[0];
    }

    /// <summary>
    /// Recomputes + persists health for every customer of the tenant, but only when the
    /// stored scores are stale (older than <see cref="RefreshStalenessMinutes"/>) — this is
    /// the cheap materialised path the customer list read uses, so the list never renders a
    /// score older than 5 minutes and never pays the aggregate on every keystroke.
    /// </summary>
    public async Task<int> RefreshCompanyAsync(long companyId, CancellationToken ct = default, bool force = false)
    {
        if (!force)
        {
            var stale = await db.ScalarLongAsync(
                @"SELECT COUNT(*) FROM customers
                  WHERE company_id=@cid AND deleted_at IS NULL
                    AND (health_computed_at IS NULL OR health_computed_at < NOW() - make_interval(mins => @mins))",
                c =>
                {
                    c.Parameters.AddWithValue("@cid", companyId);
                    c.Parameters.AddWithValue("@mins", RefreshStalenessMinutes);
                }, ct);
            if (stale == 0) return 0;
        }

        var health = await ComputeAsync(companyId, null, ct);
        if (health.Count == 0) return 0;
        await PersistAsync(companyId, health, ct);
        return health.Count;
    }

    // ── computation ───────────────────────────────────────────────────────────────

    private async Task<List<CustomerHealth>> ComputeAsync(long companyId, long? customerId, CancellationToken ct)
    {
        var caps = await ProbeAsync(ct);
        var sql = BuildSql(caps, customerId is not null);

        var rows = await db.QueryAsync(sql, c =>
        {
            c.Parameters.AddWithValue("@cid", companyId);
            c.Parameters.AddWithValue("@window", WindowDays);
            c.Parameters.AddWithValue("@recent", RecentDays);
            if (customerId is not null) c.Parameters.AddWithValue("@id", customerId.Value);
        }, ct);

        return rows.Select(Score).ToList();
    }

    // NB: Database.QueryAsync camelCases result column names (ToCamel), so every key
    // below is the camelCase form of the SQL alias.
    private static CustomerHealth Score(Dictionary<string, object?> r)
    {
        var customerId  = I64(r, "customerId");
        var total       = (int)I64(r, "totalJobs");
        var completed   = (int)I64(r, "completedJobs");
        var measurable  = (int)I64(r, "measurableJobs");
        var onTime      = (int)I64(r, "onTimeJobs");
        var pod         = (int)I64(r, "podJobs");
        var exceptions  = (int)I64(r, "exceptionJobs");
        var openExc     = (int)I64(r, "openExceptions");
        var recentMeas  = (int)I64(r, "recentMeasurableJobs");
        var recentOnTime= (int)I64(r, "recentOnTimeJobs");
        var feedbackCnt = (int)I64(r, "feedbackCount");
        var avgRating   = Dec(r, "avgRating");
        var overdueInv  = (int)I64(r, "overdueInvoices");

        // Not enough delivery history to say anything true about this account.
        if (completed < MinCompletedJobs)
        {
            return new CustomerHealth(customerId, null, null, null, "insufficient_data",
                total, completed, measurable, onTime, null, null, null, avgRating, feedbackCnt,
                openExc, overdueInv, null, WindowDays);
        }

        decimal? onTimeRate = measurable >= MinMeasurableJobs ? (decimal)onTime / measurable : null;
        decimal? podRate    = (decimal)pod / completed;
        decimal? excRate    = total > 0 ? (decimal)exceptions / total : null;
        decimal? recentRate = recentMeas >= MinMeasurableJobs ? (decimal)recentOnTime / recentMeas : null;
        decimal? feedback   = feedbackCnt > 0 && avgRating is not null
            ? Math.Clamp((avgRating.Value - 1m) / 4m, 0m, 1m)   // 1..5 stars -> 0..1
            : null;

        // SLA health = pure on-time attainment. No promise dates on the jobs -> no score.
        decimal? sla = onTimeRate is null ? null : Round(onTimeRate.Value * 100m);

        // Delivery experience = POD completeness (.45) + clean-run rate (.30) + feedback (.25),
        // renormalised over the components that actually have evidence.
        var dx = Weighted(
            (podRate, 0.45m),
            (excRate is null ? null : 1m - excRate.Value, 0.30m),
            (feedback, 0.25m));

        // Risk = inverse of attainment, then penalised by live operational + financial debt.
        var quality = Weighted((onTimeRate, 0.6m), (dx is null ? null : dx.Value / 100m, 0.4m));
        decimal? risk = null;
        if (quality is not null)
        {
            var r0 = 100m - quality.Value;
            r0 += Math.Min(20m, openExc * 5m);                       // open exceptions right now
            r0 += Math.Min(20m, overdueInv * 5m);                    // overdue invoices
            if (recentRate is not null && onTimeRate is not null && recentRate < onTimeRate - 0.10m)
                r0 += 8m;                                            // deteriorating recent trend
            risk = Round(Math.Clamp(r0, 0m, 100m));
        }

        return new CustomerHealth(customerId, sla, dx, risk, "scored",
            total, completed, measurable, onTime,
            onTimeRate is null ? null : Round(onTimeRate.Value * 100m),
            podRate is null ? null : Round(podRate.Value * 100m),
            excRate is null ? null : Round(excRate.Value * 100m),
            avgRating is null ? null : Round(avgRating.Value),
            feedbackCnt, openExc, overdueInv,
            recentRate is null ? null : Round(recentRate.Value * 100m),
            WindowDays);
    }

    // Weighted mean over the components that exist (each 0..1), returned on a 0..100 scale.
    // NULL when no component has evidence — the caller must NOT substitute a number.
    private static decimal? Weighted(params (decimal? value, decimal weight)[] parts)
    {
        decimal sum = 0, weights = 0;
        foreach (var (value, weight) in parts)
        {
            if (value is null) continue;
            sum += Math.Clamp(value.Value, 0m, 1m) * weight;
            weights += weight;
        }
        return weights == 0 ? null : Round(sum / weights * 100m);
    }

    private static decimal Round(decimal value) => Math.Round(value, 1, MidpointRounding.AwayFromZero);

    private async Task PersistAsync(long companyId, List<CustomerHealth> health, CancellationToken ct)
    {
        foreach (var chunk in health.Chunk(200))
        {
            var values = string.Join(",", chunk.Select((_, i) =>
                $"(@id{i}::BIGINT, @sla{i}::DECIMAL(6,2), @dx{i}::DECIMAL(6,2), @risk{i}::DECIMAL(6,2), @state{i}::VARCHAR)"));

            await db.ExecuteAsync(
                $@"UPDATE customers c
                      SET sla_health_score          = v.sla,
                          delivery_experience_score = v.dx,
                          risk_score                = v.risk,
                          health_state              = v.state,
                          health_computed_at        = NOW()
                     FROM (VALUES {values}) AS v(id, sla, dx, risk, state)
                    WHERE c.id = v.id AND c.company_id = @cid",
                c =>
                {
                    c.Parameters.AddWithValue("@cid", companyId);
                    for (var i = 0; i < chunk.Length; i++)
                    {
                        var h = chunk[i];
                        c.Parameters.AddWithValue($"@id{i}", h.CustomerId);
                        c.Parameters.AddWithValue($"@sla{i}", (object?)h.SlaHealthScore ?? DBNull.Value);
                        c.Parameters.AddWithValue($"@dx{i}", (object?)h.DeliveryExperienceScore ?? DBNull.Value);
                        c.Parameters.AddWithValue($"@risk{i}", (object?)h.RiskScore ?? DBNull.Value);
                        c.Parameters.AddWithValue($"@state{i}", h.State);
                    }
                }, ct);
        }
    }

    // ── SQL ───────────────────────────────────────────────────────────────────────

    private static string BuildSql(Capabilities caps, bool single)
    {
        // Promise date: the first of the SLA/ETA columns this DB actually has.
        var promiseParts = new List<string>();
        if (caps.JobsSlaDueAt)     promiseParts.Add("j.sla_due_at");
        if (caps.JobsSlaWindowEnd) promiseParts.Add("j.sla_window_end");
        if (caps.JobsEta)          promiseParts.Add("j.eta");
        promiseParts.Add("j.scheduled_end");
        var promised = promiseParts.Count == 1 ? promiseParts[0] : $"COALESCE({string.Join(", ", promiseParts)})";

        // Delivery date: jobs carry no delivered_at column — updated_at is when the row last
        // moved (the completion transition for a Completed/Delivered job); scheduled_end is
        // the fallback basis.
        var delivered = caps.JobsUpdatedAt ? "COALESCE(j.updated_at, j.scheduled_end)" : "j.scheduled_end";

        var completedPred = "j.status IN ('Completed','Delivered')";
        var podPred = caps.JobsProofStatus
            ? "j.proof_status IN ('Captured','Verified','Complete','Completed','Approved')"
            : "FALSE";
        var slaBad = caps.JobsSlaStatus ? " OR j.sla_status IN ('Breached','At Risk','Missed')" : "";
        var exceptionPred = $"(j.status IN ('Exception','Delayed','At Risk','Failed'){slaBad})";
        var openExceptionPred = $"(j.status NOT IN ('Completed','Delivered','Cancelled') AND {exceptionPred})";
        var jobsSoftDelete = caps.JobsDeletedAt ? "AND j.deleted_at IS NULL " : "";

        var feedbackCte = caps.Feedback
            ? @"fb AS (
                  SELECT f.customer_id,
                         COUNT(*) feedback_count,
                         AVG(f.rating::numeric) avg_rating
                    FROM customer_feedback f
                   WHERE f.company_id=@cid AND f.customer_id IS NOT NULL AND f.rating IS NOT NULL
                     AND f.created_at >= NOW() - make_interval(days => @window)
                   GROUP BY f.customer_id
               ),"
            : "fb AS (SELECT NULL::BIGINT customer_id, 0::BIGINT feedback_count, NULL::numeric avg_rating WHERE FALSE),";

        var invoiceCte = caps.Invoices
            ? @"inv AS (
                  SELECT ii.customer_id, COUNT(*) overdue_invoices
                    FROM issued_invoices ii
                   WHERE ii.company_id=@cid
                     AND ii.due_at IS NOT NULL AND ii.due_at < NOW()
                     AND ii.balance_due > 0
                     AND COALESCE(ii.payment_status,'unpaid') <> 'paid'
                   GROUP BY ii.customer_id
               )"
            : "inv AS (SELECT NULL::BIGINT customer_id, 0::BIGINT overdue_invoices WHERE FALSE)";

        var scopeFilter = single ? "AND c.id=@id" : "";

        return $@"
WITH scope AS (
    SELECT c.id FROM customers c
     WHERE c.company_id=@cid AND c.deleted_at IS NULL {scopeFilter}
),
j AS (
    SELECT j.customer_id, j.status, j.created_at,
           {promised} AS promised_at,
           {delivered} AS delivered_at,
           ({completedPred}) AS is_completed,
           ({podPred})       AS has_pod,
           {exceptionPred}   AS is_exception,
           {openExceptionPred} AS is_open_exception
      FROM jobs j
     WHERE j.company_id=@cid {jobsSoftDelete}AND j.customer_id IS NOT NULL
       AND j.created_at >= NOW() - make_interval(days => @window)
),
agg AS (
    SELECT s.id AS customer_id,
           COUNT(j.customer_id) AS total_jobs,
           COUNT(*) FILTER (WHERE j.is_completed) AS completed_jobs,
           COUNT(*) FILTER (WHERE j.is_completed AND j.promised_at IS NOT NULL AND j.delivered_at IS NOT NULL) AS measurable_jobs,
           COUNT(*) FILTER (WHERE j.is_completed AND j.promised_at IS NOT NULL AND j.delivered_at IS NOT NULL
                                  AND j.delivered_at <= j.promised_at) AS on_time_jobs,
           COUNT(*) FILTER (WHERE j.is_completed AND j.has_pod) AS pod_jobs,
           COUNT(*) FILTER (WHERE j.is_exception) AS exception_jobs,
           COUNT(*) FILTER (WHERE j.is_open_exception) AS open_exceptions,
           COUNT(*) FILTER (WHERE j.is_completed AND j.promised_at IS NOT NULL AND j.delivered_at IS NOT NULL
                                  AND j.created_at >= NOW() - make_interval(days => @recent)) AS recent_measurable_jobs,
           COUNT(*) FILTER (WHERE j.is_completed AND j.promised_at IS NOT NULL AND j.delivered_at IS NOT NULL
                                  AND j.delivered_at <= j.promised_at
                                  AND j.created_at >= NOW() - make_interval(days => @recent)) AS recent_on_time_jobs
      FROM scope s
      LEFT JOIN j ON j.customer_id = s.id
     GROUP BY s.id
),
{feedbackCte}
{invoiceCte}
SELECT a.customer_id, a.total_jobs, a.completed_jobs, a.measurable_jobs, a.on_time_jobs,
       a.pod_jobs, a.exception_jobs, a.open_exceptions,
       a.recent_measurable_jobs, a.recent_on_time_jobs,
       COALESCE(fb.feedback_count, 0) AS feedback_count,
       fb.avg_rating,
       COALESCE(inv.overdue_invoices, 0) AS overdue_invoices
  FROM agg a
  LEFT JOIN fb  ON fb.customer_id  = a.customer_id
  LEFT JOIN inv ON inv.customer_id = a.customer_id
 ORDER BY a.customer_id";
    }

    // ── schema probe ──────────────────────────────────────────────────────────────

    private async Task<Capabilities> ProbeAsync(CancellationToken ct)
    {
        if (_caps is not null) return _caps;
        await ProbeLock.WaitAsync(ct);
        try
        {
            if (_caps is not null) return _caps;

            var rows = await db.QueryAsync(
                @"SELECT table_name, column_name FROM information_schema.columns
                   WHERE table_schema = current_schema()
                     AND table_name IN ('jobs','customer_feedback','issued_invoices')",
                ct: ct);

            bool Has(string table, string column) => rows.Any(r =>
                string.Equals(r["tableName"]?.ToString(), table, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(r["columnName"]?.ToString(), column, StringComparison.OrdinalIgnoreCase));

            _caps = new Capabilities(
                JobsSlaDueAt:     Has("jobs", "sla_due_at"),
                JobsSlaWindowEnd: Has("jobs", "sla_window_end"),
                JobsEta:          Has("jobs", "eta"),
                JobsUpdatedAt:    Has("jobs", "updated_at"),
                JobsProofStatus:  Has("jobs", "proof_status"),
                JobsSlaStatus:    Has("jobs", "sla_status"),
                JobsDeletedAt:    Has("jobs", "deleted_at"),
                // customer_feedback has two historical shapes; only the one carrying both
                // customer_id and rating can feed the model.
                Feedback: Has("customer_feedback", "customer_id") && Has("customer_feedback", "rating")
                          && Has("customer_feedback", "company_id") && Has("customer_feedback", "created_at"),
                Invoices: Has("issued_invoices", "customer_id") && Has("issued_invoices", "company_id")
                          && Has("issued_invoices", "due_at") && Has("issued_invoices", "balance_due"));
            return _caps;
        }
        finally
        {
            ProbeLock.Release();
        }
    }

    private static long I64(Dictionary<string, object?> row, string key)
        => row.TryGetValue(key, out var v) && v is not null and not DBNull ? Convert.ToInt64(v) : 0L;

    private static decimal? Dec(Dictionary<string, object?> row, string key)
        => row.TryGetValue(key, out var v) && v is not null and not DBNull ? Convert.ToDecimal(v) : null;
}
