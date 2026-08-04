using System.Globalization;
using System.Text.Json;
using Npgsql;
using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

public enum BillingMode { Preview, Commit }

public sealed record BillingProfileResolved(
    long? Id, int Version, string Cycle, string Consolidation, string NumberingScheme, string? NumberPrefix,
    int PaymentTermsDays, string Currency, bool RequireVat, bool IsLegacyDefault);

public sealed record ConsolidatedGroup(string GroupKey, long? JobId, string Currency, int ChargeCount, decimal Subtotal, long? DraftRunId, Guid? DraftId, string? InvoiceNo);

public sealed record ConsolidationRunOutcome(bool Generated, int GroupCount, int DraftCount, decimal Subtotal, IReadOnlyList<ConsolidatedGroup> Groups, string? Reason = null);

// Billing consolidation (ADR-008 Billing layer). Generalizes the legacy per-job draft into configurable
// consolidation: bill a customer's delivered-job charges as one invoice per load / per period / per
// contract, driven by billing_profiles. Charges are a straight projection (NO re-rating). billing_status
// on job_charges prevents the same charge being billed twice across this path and the legacy per-job path.
//
// Fail-closed + additive: no billing_profiles row => a virtual LegacyDefault (immediate/per_load/30-day/
// USD) reproduces today's per-job billing. Idempotent: a run per group (billing_consolidation_runs) with
// delete-and-recompute of the prior DRAFT; the invoice number is pinned on the run and reused on regen.
public sealed class BillingConsolidationService(Database db)
{
    public async Task<BillingProfileResolved> ResolveProfileAsync(
        long companyId, long customerId, long? contractId, DateOnly asOf, CancellationToken ct = default)
    {
        // Most-specific-wins: contract > customer > tenant; newest effective_date/version.
        var r = await db.QuerySingleAsync(
            @"SELECT id, version, cycle, consolidation, numbering_scheme, number_prefix, payment_terms_days, currency, require_vat
              FROM billing_profiles
              WHERE company_id=@c AND status='active'
                AND effective_date <= @asOf AND (expiry_date IS NULL OR expiry_date >= @asOf)
                AND ( (scope_type='contract' AND scope_id=@contract)
                   OR (scope_type='customer' AND scope_id=@customer)
                   OR (scope_type='tenant') )
              ORDER BY CASE scope_type WHEN 'contract' THEN 0 WHEN 'customer' THEN 1 ELSE 2 END,
                       effective_date DESC, version DESC
              LIMIT 1",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@asOf", asOf);
                   c.Parameters.AddWithValue("@contract", (object?)contractId ?? DBNull.Value);
                   c.Parameters.AddWithValue("@customer", customerId); }, ct);
        if (r is null)
            return new BillingProfileResolved(null, 1, "immediate", "per_load", "legacy_job", null, 30, "USD", false, true);

        var scheme = (r.GetValueOrDefault("numberingScheme")?.ToString() ?? "legacy_job").Trim().ToLowerInvariant();
        if (scheme == "sequential") scheme = "legacy_job"; // sequential numbering not supported in v1
        return new BillingProfileResolved(
            Convert.ToInt64(r["id"], CultureInfo.InvariantCulture),
            Convert.ToInt32(r.GetValueOrDefault("version") ?? 1),
            (r.GetValueOrDefault("cycle")?.ToString() ?? "immediate").Trim().ToLowerInvariant(),
            (r.GetValueOrDefault("consolidation")?.ToString() ?? "per_load").Trim().ToLowerInvariant(),
            scheme, r.GetValueOrDefault("numberPrefix")?.ToString(),
            Convert.ToInt32(r.GetValueOrDefault("paymentTermsDays") ?? 30),
            r.GetValueOrDefault("currency")?.ToString() ?? "USD",
            Convert.ToBoolean(r.GetValueOrDefault("requireVat") ?? false), false);
    }

    public async Task<ConsolidationRunOutcome> GenerateConsolidatedDraftsAsync(
        long companyId, long customerId, DateOnly periodStart, DateOnly periodEnd, long? billingProfileId,
        BillingMode mode, CancellationToken ct = default)
    {
        if (periodEnd < periodStart)
            return new ConsolidationRunOutcome(false, 0, 0, 0, Array.Empty<ConsolidatedGroup>(), "invalid_period");

        var profile = await ResolveProfileAsync(companyId, customerId, null, periodEnd, ct);

        // Billable charges: unbilled charges on the customer's delivered jobs whose delivery falls in the
        // period. Delivery date via dispatch_assignments (same source as SettlementService) so AR and AP
        // periods reconcile. Not already on any draft/issued line.
        var charges = await db.QueryAsync(
            @"SELECT jc.id, jc.job_id, jc.charge_code, jc.charge_name, jc.charge_type, jc.description,
                     jc.quantity, jc.unit_rate, jc.amount, jc.currency, j.contract_id
              FROM job_charges jc
              JOIN jobs j ON j.id = jc.job_id AND j.company_id = jc.company_id
              WHERE jc.company_id = @c
                AND j.customer_id = @cust
                AND jc.billing_status IN ('unbilled', 'drafted')
                AND EXISTS (SELECT 1 FROM dispatch_assignments da
                            WHERE da.company_id = jc.company_id AND da.job_id = jc.job_id
                              AND da.assignment_status = 'delivered'
                              AND COALESCE(da.actual_delivery_at, da.completed_at)::date BETWEEN @ps AND @pe)
                AND NOT EXISTS (SELECT 1 FROM issued_invoice_lines l WHERE l.company_id = jc.company_id AND l.job_charge_id = jc.id)
                -- Excluded if already on any draft EXCEPT a still-'draft' consolidation draft (which this
                -- run will delete-and-recompute) — so regenerate stays repeatable (D8) but a charge billed
                -- via the legacy per-job path or an issued invoice is never double-billed.
                AND NOT EXISTS (
                    SELECT 1 FROM invoice_draft_lines l
                    JOIN invoice_drafts d ON d.id = l.invoice_draft_id AND d.company_id = l.company_id
                    WHERE l.company_id = jc.company_id AND l.job_charge_id = jc.id
                      AND NOT (d.source = 'consolidation' AND d.status = 'draft'))
              ORDER BY jc.job_id, jc.id",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@cust", customerId);
                   c.Parameters.AddWithValue("@ps", periodStart); c.Parameters.AddWithValue("@pe", periodEnd); }, ct);

        if (charges.Count == 0)
            return new ConsolidationRunOutcome(false, 0, 0, 0, Array.Empty<ConsolidatedGroup>(), "no_billable_charges");

        var rows = charges.Select(ChargeRow.From).ToList();

        // Group by consolidation strategy.
        var groups = profile.Consolidation switch
        {
            "per_load" => rows.GroupBy(r => (key: $"job:{r.JobId}", job: (long?)r.JobId)),
            "contract" => rows.GroupBy(r => (key: $"contract:{r.ContractId?.ToString() ?? "none"}:{periodStart:yyyyMMdd}:{periodEnd:yyyyMMdd}", job: (long?)null)),
            _ => rows.GroupBy(r => (key: $"period:{periodStart:yyyyMMdd}:{periodEnd:yyyyMMdd}", job: (long?)null)), // period (default)
        };

        var outGroups = new List<ConsolidatedGroup>();
        var totalSubtotal = 0m;
        var draftCount = 0;

        foreach (var g in groups)
        {
            var groupRows = g.ToList();
            // Currency: per_load/legacy replicate first-non-blank-wins (bill mixed together, single-job
            // invariant preserved); consolidated (period/contract) EXCLUDE charges off the profile currency.
            string currency;
            List<ChargeRow> billable;
            if (profile.Consolidation == "per_load")
            {
                currency = groupRows.Select(r => r.Currency).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? profile.Currency;
                billable = groupRows;
            }
            else
            {
                currency = profile.Currency;
                billable = groupRows.Where(r => string.Equals(r.Currency, currency, StringComparison.OrdinalIgnoreCase)).ToList();
                if (billable.Count == 0) continue;
            }

            var subtotal = billable.Sum(r => r.Amount);
            var contractId = billable.Select(r => r.ContractId).FirstOrDefault(v => v is not null);

            if (mode == BillingMode.Preview)
            {
                outGroups.Add(new ConsolidatedGroup(g.Key.key, g.Key.job, currency, billable.Count, subtotal, null, null, null));
                totalSubtotal += subtotal;
                continue;
            }

            var committed = await CommitGroupAsync(companyId, customerId, contractId, g.Key.job, g.Key.key,
                periodStart, periodEnd, profile, currency, subtotal, billable, ct);
            if (committed is null) continue; // locked (already issued) — skip
            outGroups.Add(committed);
            totalSubtotal += subtotal;
            draftCount++;
        }

        return new ConsolidationRunOutcome(mode == BillingMode.Commit, outGroups.Count, draftCount, totalSubtotal, outGroups,
            mode == BillingMode.Preview ? "preview" : outGroups.Count == 0 ? "all_locked" : null);
    }

    private async Task<ConsolidatedGroup?> CommitGroupAsync(
        long companyId, long customerId, long? contractId, long? jobId, string groupKey,
        DateOnly periodStart, DateOnly periodEnd, BillingProfileResolved profile, string currency, decimal subtotal,
        List<ChargeRow> charges, CancellationToken ct)
    {
        // Existing run for this group? Reuse its number; delete-and-recompute its DRAFT if still a draft.
        var existing = await db.QuerySingleAsync(
            @"SELECT r.id, r.invoice_draft_id, r.allocated_invoice_no, d.status AS draft_status
              FROM billing_consolidation_runs r
              LEFT JOIN invoice_drafts d ON d.id = r.invoice_draft_id AND d.company_id = r.company_id
              WHERE r.company_id=@c AND r.billing_profile_id=@p AND r.customer_id=@cust AND r.group_key=@g AND r.source='system'",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@p", profile.Id ?? 0L);
                   c.Parameters.AddWithValue("@cust", customerId); c.Parameters.AddWithValue("@g", groupKey); }, ct);

        long? runId = null; string? invoiceNo = null; Guid? priorDraft = null; string? priorStatus = null;
        if (existing is not null)
        {
            runId = Convert.ToInt64(existing["id"], CultureInfo.InvariantCulture);
            invoiceNo = existing.GetValueOrDefault("allocatedInvoiceNo")?.ToString();
            priorDraft = existing.GetValueOrDefault("invoiceDraftId") is { } pd and not DBNull ? (Guid)pd : null;
            priorStatus = existing.GetValueOrDefault("draftStatus")?.ToString();
            // A prior draft that is no longer a plain 'draft' (issued/in-review) is immutable — skip.
            if (priorDraft is not null && priorStatus is not null && priorStatus != "draft")
                return null;
        }

        invoiceNo ??= $"CINV-{companyId}-{customerId}-{groupKey.GetHashCode() & 0x7fffffff}";
        var draftId = Guid.NewGuid();
        var resolvedConfig = JsonSerializer.Serialize(new
        {
            profile.Cycle, profile.Consolidation, profile.NumberingScheme, profile.PaymentTermsDays, currency, profile.Version
        });

        await db.WithTransactionAsync(async (conn, tx) =>
        {
            // Release the prior draft's charges and delete it (delete-and-recompute).
            if (priorDraft is { } pd)
            {
                await ExecAsync(conn, tx, "UPDATE job_charges SET billing_status='unbilled' WHERE company_id=@c AND billing_status='drafted' AND id IN (SELECT job_charge_id FROM invoice_draft_lines WHERE company_id=@c AND invoice_draft_id=@d AND job_charge_id IS NOT NULL)",
                    p => { p.AddWithValue("@c", companyId); p.AddWithValue("@d", pd); }, ct);
                await ExecAsync(conn, tx, "DELETE FROM invoice_drafts WHERE company_id=@c AND id=@d AND status='draft'",
                    p => { p.AddWithValue("@c", companyId); p.AddWithValue("@d", pd); }, ct);
            }

            if (runId is null)
            {
                // INSERT throws on the partial-unique group index if a concurrent consolidate raced us.
                await using var insRun = new NpgsqlCommand(
                    @"INSERT INTO billing_consolidation_runs
                        (company_id, billing_profile_id, billing_profile_version, customer_id, group_key, period_start, period_end,
                         invoice_draft_id, allocated_invoice_no, resolved_config_json, status, charge_count, subtotal, currency, source)
                      VALUES (@c, @p, @ver, @cust, @g, @ps, @pe, @draft, @no, @cfg::jsonb, 'draft', @cnt, @sub, @cur, 'system') RETURNING id", conn, tx);
                insRun.Parameters.AddWithValue("@c", companyId); insRun.Parameters.AddWithValue("@p", profile.Id ?? 0L);
                insRun.Parameters.AddWithValue("@ver", profile.Version); insRun.Parameters.AddWithValue("@cust", customerId);
                insRun.Parameters.AddWithValue("@g", groupKey); insRun.Parameters.AddWithValue("@ps", periodStart); insRun.Parameters.AddWithValue("@pe", periodEnd);
                insRun.Parameters.AddWithValue("@draft", draftId); insRun.Parameters.AddWithValue("@no", invoiceNo);
                insRun.Parameters.AddWithValue("@cfg", resolvedConfig); insRun.Parameters.AddWithValue("@cnt", charges.Count);
                insRun.Parameters.AddWithValue("@sub", subtotal); insRun.Parameters.AddWithValue("@cur", currency);
                runId = Convert.ToInt64((await insRun.ExecuteScalarAsync(ct))!, CultureInfo.InvariantCulture);
            }
            else
            {
                await ExecAsync(conn, tx,
                    "UPDATE billing_consolidation_runs SET invoice_draft_id=@draft, allocated_invoice_no=@no, resolved_config_json=@cfg::jsonb, charge_count=@cnt, subtotal=@sub, currency=@cur, status='draft', updated_at=NOW() WHERE company_id=@c AND id=@id",
                    p => { p.AddWithValue("@draft", draftId); p.AddWithValue("@no", invoiceNo!); p.AddWithValue("@cfg", resolvedConfig);
                           p.AddWithValue("@cnt", charges.Count); p.AddWithValue("@sub", subtotal); p.AddWithValue("@cur", currency);
                           p.AddWithValue("@c", companyId); p.AddWithValue("@id", runId!); }, ct);
            }

            var metadata = JsonSerializer.Serialize(new { source = "consolidation", groupKey, runId });
            await ExecAsync(conn, tx,
                @"INSERT INTO invoice_drafts
                    (id, company_id, customer_id, contract_id, job_id, invoice_draft_no, status, currency, subtotal, tax_total, total,
                     source, metadata_json, created_at, billing_profile_id, payment_terms_days, document_type)
                  VALUES (@id, @c, @cust, @contract, @job, @no, 'draft', @cur, @sub, 0, @sub,
                          'consolidation', @meta::jsonb, NOW(), @pid, @terms, 'invoice')",
                p => {
                    p.AddWithValue("@id", draftId); p.AddWithValue("@c", companyId); p.AddWithValue("@cust", customerId);
                    p.AddWithValue("@contract", (object?)contractId ?? DBNull.Value);
                    p.AddWithValue("@job", (object?)jobId ?? DBNull.Value); p.AddWithValue("@no", invoiceNo!);
                    p.AddWithValue("@cur", currency); p.AddWithValue("@sub", subtotal); p.AddWithValue("@meta", metadata);
                    p.AddWithValue("@pid", (object?)profile.Id ?? DBNull.Value); p.AddWithValue("@terms", profile.PaymentTermsDays);
                }, ct);

            var lineNo = 1;
            foreach (var ch in charges)
            {
                await ExecAsync(conn, tx,
                    @"INSERT INTO invoice_draft_lines
                        (id, company_id, invoice_draft_id, job_charge_id, line_no, description, charge_code, quantity, unit, unit_rate, amount, metadata_json, created_at)
                      VALUES (gen_random_uuid(), @c, @d, @jc, @ln, @desc, @code, @qty, @unit, @rate, @amt, @meta::jsonb, NOW())",
                    p => {
                        p.AddWithValue("@c", companyId); p.AddWithValue("@d", draftId); p.AddWithValue("@jc", ch.Id);
                        p.AddWithValue("@ln", lineNo++); p.AddWithValue("@desc", (object?)(ch.Description ?? ch.ChargeName) ?? DBNull.Value);
                        p.AddWithValue("@code", (object?)ch.ChargeCode ?? DBNull.Value); p.AddWithValue("@qty", ch.Quantity);
                        p.AddWithValue("@unit", (object?)ch.ChargeType ?? DBNull.Value); p.AddWithValue("@rate", ch.UnitRate);
                        p.AddWithValue("@amt", ch.Amount); p.AddWithValue("@meta", JsonSerializer.Serialize(new { chargeId = ch.Id }));
                    }, ct);
            }

            // Claim the charges (concurrency guard: only flip rows still 'unbilled').
            var ids = charges.Select(c => c.Id).ToArray();
            await ExecAsync(conn, tx,
                "UPDATE job_charges SET billing_status='drafted' WHERE company_id=@c AND billing_status='unbilled' AND id = ANY(@ids)",
                p => { p.AddWithValue("@c", companyId); p.AddWithValue("@ids", ids); }, ct);
            return true;
        }, ct);

        return new ConsolidatedGroup(groupKey, jobId, currency, charges.Count, subtotal, runId, draftId, invoiceNo);
    }

    public async Task<List<Dictionary<string, object?>>> ListRunsAsync(long companyId, long? customerId, string? status, CancellationToken ct = default)
        => await db.QueryAsync(
            @"SELECT * FROM billing_consolidation_runs
              WHERE company_id=@c AND (@cust IS NULL OR customer_id=@cust) AND (@status IS NULL OR status=@status)
              ORDER BY created_at DESC, id DESC LIMIT 500",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@cust", (object?)customerId ?? DBNull.Value);
                   c.Parameters.AddWithValue("@status", (object?)status ?? DBNull.Value); }, ct);

    public async Task<Dictionary<string, object?>?> GetRunAsync(long companyId, long runId, CancellationToken ct = default)
        => await db.QuerySingleAsync("SELECT * FROM billing_consolidation_runs WHERE company_id=@c AND id=@id",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@id", runId); }, ct);

    private static async Task ExecAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string sql, Action<NpgsqlParameterCollection> bind, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        bind(cmd.Parameters);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private sealed record ChargeRow(long Id, long JobId, string? ChargeCode, string? ChargeName, string? ChargeType,
        string? Description, decimal Quantity, decimal UnitRate, decimal Amount, string? Currency, long? ContractId)
    {
        public static ChargeRow From(Dictionary<string, object?> r) => new(
            Convert.ToInt64(r["id"], CultureInfo.InvariantCulture),
            Convert.ToInt64(r["jobId"], CultureInfo.InvariantCulture),
            Nz(r, "chargeCode"), Nz(r, "chargeName"), Nz(r, "chargeType"), Nz(r, "description"),
            Convert.ToDecimal(r.GetValueOrDefault("quantity") ?? 1m, CultureInfo.InvariantCulture),
            Convert.ToDecimal(r.GetValueOrDefault("unitRate") ?? 0m, CultureInfo.InvariantCulture),
            Convert.ToDecimal(r.GetValueOrDefault("amount") ?? 0m, CultureInfo.InvariantCulture),
            Nz(r, "currency"),
            r.GetValueOrDefault("contractId") is { } c and not DBNull ? Convert.ToInt64(c, CultureInfo.InvariantCulture) : null);
        private static string? Nz(Dictionary<string, object?> r, string k) => r.GetValueOrDefault(k) is { } v and not DBNull ? v.ToString() : null;
    }
}
