using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

public sealed record CostMarginEvidenceRow(
    string Id,
    string EntityType,
    long EntityId,
    string EntityLabel,
    long? CustomerId,
    string? CustomerName,
    decimal RevenueEstimate,
    decimal TotalCost,
    decimal? MarginEstimate,
    decimal? MarginPercent,
    string Currency,
    long InvoiceCount,
    long CostRecordCount,
    string Status,
    string DataOrigin);

public sealed record CostMarginCurrencySummary(
    string Currency,
    decimal RevenueEstimate,
    decimal TotalCost,
    decimal? MarginEstimate,
    long JobCount,
    long CompleteMarginCount);

public sealed record CostMarginEvidenceSummary(
    long JobsWithEvidence,
    long CompleteMargins,
    long MissingCostEvidence,
    long MissingIssuedRevenue,
    IReadOnlyList<CostMarginCurrencySummary> ByCurrency,
    string DataOrigin,
    string CalculationPolicy);

public sealed class CostMarginEvidenceService(Database db)
{
    public async Task<IReadOnlyList<CostMarginEvidenceRow>> ListJobsAsync(long companyId, CancellationToken ct = default)
    {
        var rows = await db.QueryAsync(
            @"WITH invoice_revenue AS (
                  SELECT i.company_id, i.job_id, UPPER(i.currency) currency,
                         COUNT(*) invoice_count,
                         SUM(GREATEST(i.total-COALESCE(i.credit_total,0),0)) revenue_total
                    FROM issued_invoices i
                   WHERE i.company_id=@companyId AND i.job_id IS NOT NULL
                     AND LOWER(i.status) NOT IN ('cancelled','void')
                     AND COALESCE(i.document_type,'invoice')='invoice'
                   GROUP BY i.company_id,i.job_id,UPPER(i.currency)
              ), approved_cost AS (
                  SELECT e.company_id,e.job_id,UPPER(e.currency) currency,
                         COUNT(*) cost_record_count,SUM(e.amount) cost_total
                    FROM expenses e
                   WHERE e.company_id=@companyId AND e.job_id IS NOT NULL
                     AND e.deleted_at IS NULL AND LOWER(e.approval_status)='approved'
                     AND (e.expense_number IS NULL OR e.expense_number NOT LIKE 'EXP-B5-%')
                   GROUP BY e.company_id,e.job_id,UPPER(e.currency)
              ), evidence AS (
                  SELECT COALESCE(r.company_id,x.company_id) company_id,
                         COALESCE(r.job_id,x.job_id) job_id,
                         COALESCE(r.currency,x.currency) currency,
                         COALESCE(r.invoice_count,0) invoice_count,
                         COALESCE(r.revenue_total,0) revenue_total,
                         COALESCE(x.cost_record_count,0) cost_record_count,
                         COALESCE(x.cost_total,0) cost_total
                    FROM invoice_revenue r
                    FULL OUTER JOIN approved_cost x
                      ON x.company_id=r.company_id AND x.job_id=r.job_id AND x.currency=r.currency
              )
              SELECT 'job:' || e.job_id || ':' || e.currency id,
                     'job' entity_type,e.job_id entity_id,j.job_code entity_label,
                     j.customer_id,c.name customer_name,e.revenue_total revenue_estimate,e.cost_total total_cost,
                     CASE WHEN e.invoice_count>0 AND e.cost_record_count>0 THEN e.revenue_total-e.cost_total END margin_estimate,
                     CASE WHEN e.invoice_count>0 AND e.cost_record_count>0 AND e.revenue_total<>0
                          THEN ROUND(((e.revenue_total-e.cost_total)/e.revenue_total)*100,2) END margin_percent,
                     e.currency,e.invoice_count,e.cost_record_count,
                     CASE WHEN e.invoice_count=0 THEN 'Issued revenue unavailable'
                          WHEN e.cost_record_count=0 THEN 'Cost evidence unavailable'
                          ELSE 'Calculated' END status,
                     'issued_invoices+approved_expenses' data_origin
                FROM evidence e
                JOIN jobs j ON j.id=e.job_id AND j.company_id=e.company_id AND j.deleted_at IS NULL
                LEFT JOIN customers c ON c.id=j.customer_id AND c.company_id=j.company_id AND c.deleted_at IS NULL
               ORDER BY CASE WHEN e.invoice_count>0 AND e.cost_record_count>0 THEN 1 ELSE 0 END,
                        e.revenue_total DESC,j.job_code
               LIMIT 100",
            c => c.Parameters.AddWithValue("@companyId", companyId), ct);

        return rows.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<CostMarginEvidenceRow>> ListApprovedCostsByEntityAsync(
        long companyId, string entityType, CancellationToken ct = default)
    {
        var (idColumn, table, labelExpression, joinColumn) = entityType switch
        {
            "route" => ("route_id", "routes", "COALESCE(entity.route_code,entity.route_name,entity.name)", "route_id"),
            "vehicle" => ("vehicle_id", "vehicles", "entity.vehicle_code", "vehicle_id"),
            _ => throw new ArgumentOutOfRangeException(nameof(entityType))
        };
        var rows = await db.QueryAsync(
            $@"SELECT @entityType || ':' || e.{idColumn} || ':' || UPPER(e.currency) id,
                       @entityType entity_type,e.{idColumn} entity_id,
                       {labelExpression} entity_label,NULL::BIGINT customer_id,NULL::TEXT customer_name,
                       0::numeric revenue_estimate,SUM(e.amount)::numeric total_cost,
                       NULL::numeric margin_estimate,NULL::numeric margin_percent,UPPER(e.currency) currency,
                       0::BIGINT invoice_count,COUNT(*) cost_record_count,'Cost evidence only' status,
                       'approved_expenses' data_origin
                  FROM expenses e
                  JOIN {table} entity ON entity.id=e.{joinColumn} AND entity.company_id=e.company_id AND entity.deleted_at IS NULL
                 WHERE e.company_id=@companyId AND e.{idColumn} IS NOT NULL
                   AND e.deleted_at IS NULL AND LOWER(e.approval_status)='approved'
                   AND (e.expense_number IS NULL OR e.expense_number NOT LIKE 'EXP-B5-%')
                 GROUP BY e.{idColumn},{labelExpression},UPPER(e.currency)
                 ORDER BY SUM(e.amount) DESC
                 LIMIT 100",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@entityType", entityType); }, ct);
        return rows.Select(Map).ToList();
    }

    public async Task<CostMarginEvidenceSummary> SummaryAsync(long companyId, CancellationToken ct = default)
    {
        var jobs = await ListJobsAsync(companyId, ct);
        var byCurrency = jobs.GroupBy(row => row.Currency, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var complete = group.Where(row => row.MarginEstimate.HasValue).ToList();
                return new CostMarginCurrencySummary(
                    group.Key,
                    group.Sum(row => row.RevenueEstimate),
                    group.Sum(row => row.TotalCost),
                    complete.Count > 0 ? complete.Sum(row => row.MarginEstimate!.Value) : null,
                    group.Select(row => row.EntityId).Distinct().LongCount(),
                    complete.LongCount());
            }).ToList();
        return new CostMarginEvidenceSummary(
            jobs.Select(row => row.EntityId).Distinct().LongCount(),
            jobs.LongCount(row => row.MarginEstimate.HasValue),
            jobs.LongCount(row => row.InvoiceCount > 0 && row.CostRecordCount == 0),
            jobs.LongCount(row => row.InvoiceCount == 0 && row.CostRecordCount > 0),
            byCurrency,
            "issued_invoices+approved_expenses",
            "Margins are calculated only when an issued invoice and at least one approved, non-demo expense exist for the same job and currency. Currencies are never combined.");
    }

    private static CostMarginEvidenceRow Map(Dictionary<string, object?> row) => new(
        row["id"]?.ToString() ?? string.Empty,
        row["entityType"]?.ToString() ?? string.Empty,
        Convert.ToInt64(row["entityId"]),
        row["entityLabel"]?.ToString() ?? string.Empty,
        row["customerId"] is null or DBNull ? null : Convert.ToInt64(row["customerId"]),
        row["customerName"]?.ToString(),
        Convert.ToDecimal(row["revenueEstimate"] ?? 0),
        Convert.ToDecimal(row["totalCost"] ?? 0),
        row["marginEstimate"] is null or DBNull ? null : Convert.ToDecimal(row["marginEstimate"]),
        row["marginPercent"] is null or DBNull ? null : Convert.ToDecimal(row["marginPercent"]),
        row["currency"]?.ToString() ?? string.Empty,
        Convert.ToInt64(row["invoiceCount"] ?? 0),
        Convert.ToInt64(row["costRecordCount"] ?? 0),
        row["status"]?.ToString() ?? string.Empty,
        row["dataOrigin"]?.ToString() ?? string.Empty);
}
