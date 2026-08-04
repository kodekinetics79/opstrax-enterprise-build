using System.Globalization;
using Npgsql;
using Opstrax.Api.Data;
using Opstrax.Api.Foundation;

namespace Opstrax.Api.Services;

public enum TaxMode { Preview, Commit }

public sealed record TaxLineResult(
    Guid DraftLineId, long? JobChargeId, string Regime, string TaxCode, string? TaxCategory,
    string? ExemptionReasonCode, string? Jurisdiction, decimal TaxableAmount, decimal Rate,
    decimal TaxAmount, bool PriceInclusive, string? ExemptReason);

public sealed record TaxComputationOutcome(
    bool Applied, string? Reason, decimal TaxTotal, decimal Subtotal, decimal Total,
    long? TaxProfileId, IReadOnlyList<TaxLineResult> Lines);

// Tax engine (VAT / GST / ZATCA-VAT) — ADR-008 P3. Computes a per-line tax breakdown for an invoice
// draft from a published tax_profile + its decision-table rules, sets the draft tax_total/total, is
// re-run at issue-time inside the issue transaction, and is snapshotted immutably into the issued
// invoice. Fail-closed like RatingService/SettlementService: no published profile reproduces today's
// tax_total=0/total=subtotal exactly; an unsupported regime, an unregistered seller, or a line with no
// matching rule BLOCKS (never silently zeroes a real tax). Phase 1: vat|gst|zatca_vat only.
//
// Phase-1 known limitations (header totals are always correct; these are per-line/UX only):
//  - The per-line charge_type fact reads invoice_draft_lines.unit (where CreateInvoiceDraftFromJobAsync
//    stores charge_type). Any draft-line writer MUST keep that convention or match_charge_type rules miss.
//  - Under price_inclusive, issued_invoice_lines.amount stays gross, so Σ lines == total (gross), not the
//    net header subtotal; consumers must read the net subtotal from the header, not by summing lines.
public sealed class TaxService(Database db, IApprovalWorkflowService? approval = null)
{
    // ── compute (public entry points) ──

    public async Task<TaxComputationOutcome> ComputeForDraftAsync(long companyId, Guid draftId, TaxMode mode, CancellationToken ct = default)
    {
        var draft = await LoadDraftAsync(companyId, draftId, ct);
        if (draft is null) return Fail("draft_not_found", 0, 0);
        if (draft.Status == "issued") return Fail("draft_locked", draft.Subtotal, draft.Subtotal);

        var taxPoint = mode == TaxMode.Commit ? Today() : draft.CreatedOn;
        var (outcome, plan) = await ComputeCoreAsync(companyId, draft, taxPoint, ct);

        if (mode == TaxMode.Preview) return outcome;

        // Commit ALWAYS persists: applied => the computed lines; not-applied (no_tax_profile OR a hard
        // block) => the zero baseline (delete-and-recompute clears any stale tax from a prior config).
        // A blocked draft is thus zeroed AND un-issuable — the issue path re-gates and blocks again, so
        // an invoice can never issue with stale/blocked tax.
        await db.WithTransactionAsync(async (conn, tx) =>
        {
            await PersistAsync(conn, tx, companyId, draftId, taxPoint, outcome, plan, ct);
            return true;
        }, ct);
        return outcome;
    }

    public async Task<TaxComputationOutcome> RecalculateDraftTaxAsync(long companyId, Guid draftId, CancellationToken ct = default)
        => await ComputeForDraftAsync(companyId, draftId, TaxMode.Commit, ct);

    // Issue-time overload — runs delete-and-recompute + draft UPDATE inside the CALLER's transaction
    // against the pinned tax point (issue date), so there is no header/lines desync window.
    public async Task<TaxComputationOutcome> ComputeForDraftInTxAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, long companyId, Guid draftId, DateOnly taxPointDate, CancellationToken ct = default)
    {
        var draft = await LoadDraftAsync(companyId, draftId, ct);
        if (draft is null) return Fail("draft_not_found", 0, 0);

        var (outcome, plan) = await ComputeCoreAsync(companyId, draft, taxPointDate, ct);
        if (!outcome.Applied && outcome.Reason != "no_tax_profile")
            return outcome; // caller aborts the tx

        await PersistAsync(conn, tx, companyId, draftId, taxPointDate, outcome, plan, ct);
        return outcome;
    }

    // Inside the issue tx: copy the mutable breakdown into the immutable snapshot. Asserts the persisted
    // line sum still foots to the draft tax_total and fail-closes on mismatch.
    public async Task SnapshotIssuedTaxLinesAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, long companyId, Guid draftId, Guid issuedInvoiceId, CancellationToken ct = default)
    {
        await using (var check = new NpgsqlCommand(
            @"SELECT COALESCE(SUM(tax_amount),0) FROM invoice_tax_lines WHERE company_id=@c AND invoice_draft_id=@d", conn, tx))
        {
            check.Parameters.AddWithValue("@c", companyId); check.Parameters.AddWithValue("@d", draftId);
            var lineSum = Convert.ToDecimal((await check.ExecuteScalarAsync(ct))!, CultureInfo.InvariantCulture);
            await using var hdr = new NpgsqlCommand("SELECT tax_total FROM invoice_drafts WHERE company_id=@c AND id=@d", conn, tx);
            hdr.Parameters.AddWithValue("@c", companyId); hdr.Parameters.AddWithValue("@d", draftId);
            var header = Convert.ToDecimal((await hdr.ExecuteScalarAsync(ct))!, CultureInfo.InvariantCulture);
            if (lineSum != header)
                throw new InvalidOperationException($"Tax snapshot aborted: line sum {lineSum} != draft tax_total {header}");
        }

        await using var ins = new NpgsqlCommand(
            @"INSERT INTO issued_invoice_tax_lines
                (company_id, issued_invoice_id, source_invoice_tax_line_id, job_charge_id, regime, tax_code,
                 tax_category, exemption_reason_code, jurisdiction, taxable_amount, rate, tax_amount,
                 price_inclusive, exempt_reason, tax_profile_id, tax_point_date)
              SELECT company_id, @iid, id, job_charge_id, regime, tax_code,
                     tax_category, exemption_reason_code, jurisdiction, taxable_amount, rate, tax_amount,
                     price_inclusive, exempt_reason, tax_profile_id, tax_point_date
              FROM invoice_tax_lines WHERE company_id=@c AND invoice_draft_id=@d", conn, tx);
        ins.Parameters.AddWithValue("@iid", issuedInvoiceId); ins.Parameters.AddWithValue("@c", companyId);
        ins.Parameters.AddWithValue("@d", draftId);
        await ins.ExecuteNonQueryAsync(ct);
    }

    // ── compute core (pure; reads committed data via db) ──

    private async Task<(TaxComputationOutcome, List<TaxLineResult>)> ComputeCoreAsync(
        long companyId, DraftHeader draft, DateOnly taxPoint, CancellationToken ct)
    {
        var none = new List<TaxLineResult>();
        var profile = await ResolveProfileAsync(companyId, draft.Currency, taxPoint, ct);
        if (profile is null)
            return (new TaxComputationOutcome(false, "no_tax_profile", 0, draft.Subtotal, draft.Subtotal, null, none), none);

        // Regime gates — Phase 1 computes the VAT family only; everything else BLOCKS (never silent-zero).
        if (profile.Regime == "us_sales_tax")
            return (Fail("regime_unsupported_phase1:us_sales_tax", draft.Subtotal, draft.Subtotal), none);
        if (profile.Regime == "zatca_vat" && !string.Equals(draft.Currency, "SAR", StringComparison.OrdinalIgnoreCase))
            return (Fail("fx_vat_unsupported_phase1", draft.Subtotal, draft.Subtotal), none);
        if (profile.Regime is not ("vat" or "gst" or "zatca_vat"))
            return (Fail($"regime_unsupported_phase1:{profile.Regime}", draft.Subtotal, draft.Subtotal), none);

        var rules = await LoadRulesAsync(companyId, profile.Id, ct);
        var customer = await LoadCustomerStatusAsync(companyId, draft.CustomerId, taxPoint, ct);
        var lines = await LoadDraftLinesAsync(companyId, draft.Id, ct);

        var results = new List<TaxLineResult>(lines.Count);
        foreach (var line in lines)
        {
            var jurisdiction = customer?.Jurisdiction;
            var rule = ResolveRule(rules, draft.CustomerId, line.ChargeCode, line.ChargeType, jurisdiction);
            if (rule is null)
                return (Fail($"no_rule_for_line:{line.ChargeCode}", draft.Subtotal, draft.Subtotal), none);

            string taxCode; string? taxCategory; string? exemptReason; string? exemptCode; decimal rate; decimal taxable; decimal taxAmount;

            if (customer?.TaxExempt == true)
            {
                taxCode = "EXEMPT"; taxCategory = rule.TaxCategory ?? "E"; rate = 0m;
                taxable = line.Amount; taxAmount = 0m;
                exemptReason = customer.ExemptionReason; exemptCode = customer.ExemptionReasonCode ?? rule.ExemptionReasonCode;
            }
            else if (string.Equals(rule.TaxCode, "REVERSE_CHARGE", StringComparison.OrdinalIgnoreCase))
            {
                return (Fail("regime_unsupported_phase1:reverse_charge", draft.Subtotal, draft.Subtotal), none);
            }
            else if (!rule.Taxable || (!profile.FreightTaxable && IsFreight(line)))
            {
                // Non-taxable by rule, or a freight line under a profile that zero-rates freight
                // (freight_taxable=false) — many jurisdictions treat separately-stated freight as exempt.
                taxCode = "OUT_OF_SCOPE"; taxCategory = rule.TaxCategory ?? "O"; rate = 0m;
                taxable = line.Amount; taxAmount = 0m;
                exemptReason = null; exemptCode = rule.ExemptionReasonCode;
            }
            else
            {
                rate = rule.Rate;
                taxCode = rule.TaxCode;
                taxCategory = rule.TaxCategory ?? (rate == 0m ? "Z" : "S");
                exemptReason = null; exemptCode = rate == 0m ? rule.ExemptionReasonCode : null;
                if (profile.PriceInclusive)
                {
                    taxable = Round(line.Amount / (1m + rate));
                    taxAmount = line.Amount - taxable;
                }
                else
                {
                    taxable = line.Amount;
                    taxAmount = Round(line.Amount * rate);
                }
            }

            results.Add(new TaxLineResult(line.Id, line.JobChargeId, profile.Regime, taxCode, taxCategory,
                exemptCode, jurisdiction, taxable, rate, taxAmount, profile.PriceInclusive, exemptReason));
        }

        var taxTotal = results.Sum(r => r.TaxAmount);

        // A registered seller is required to actually charge indirect tax (VAT/GST/ZATCA-VAT) — but only
        // if tax is non-zero (a fully zero-rated/exempt seller need not be registered to invoice).
        if (taxTotal > 0 && profile.Regime is "vat" or "gst" or "zatca_vat")
        {
            var registered = await HasSellerRegistrationAsync(companyId, profile.Regime, taxPoint, ct);
            if (!registered)
                return (Fail("seller_not_registered", draft.Subtotal, draft.Subtotal), none);
        }

        decimal subtotal, total;
        if (profile.PriceInclusive)
        {
            subtotal = results.Sum(r => r.TaxableAmount);
            total = results.Sum(r => r.TaxableAmount + r.TaxAmount);
        }
        else
        {
            subtotal = draft.Subtotal;
            total = draft.Subtotal + taxTotal;
        }

        return (new TaxComputationOutcome(true, null, taxTotal, subtotal, total, profile.Id, results), results);
    }

    // Delete-and-recompute the draft breakdown + update the draft header, using the caller's conn/tx.
    private async Task PersistAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, long companyId, Guid draftId, DateOnly taxPoint,
        TaxComputationOutcome outcome, List<TaxLineResult> plan, CancellationToken ct)
    {
        await using (var del = new NpgsqlCommand(
            "DELETE FROM invoice_tax_lines WHERE company_id=@c AND invoice_draft_id=@d", conn, tx))
        {
            del.Parameters.AddWithValue("@c", companyId); del.Parameters.AddWithValue("@d", draftId);
            await del.ExecuteNonQueryAsync(ct);
        }

        foreach (var l in plan)
        {
            await using var ins = new NpgsqlCommand(
                @"INSERT INTO invoice_tax_lines
                    (company_id, invoice_draft_id, invoice_draft_line_id, job_charge_id, regime, tax_code,
                     tax_category, exemption_reason_code, jurisdiction, taxable_amount, rate, tax_amount,
                     price_inclusive, exempt_reason, tax_profile_id, tax_point_date)
                  VALUES (@c, @d, @dl, @jc, @regime, @code, @cat, @erc, @jur, @taxable, @rate, @amt,
                          @incl, @exreason, @pid, @tp)", conn, tx);
            ins.Parameters.AddWithValue("@c", companyId); ins.Parameters.AddWithValue("@d", draftId);
            ins.Parameters.AddWithValue("@dl", l.DraftLineId);
            ins.Parameters.AddWithValue("@jc", (object?)l.JobChargeId ?? DBNull.Value);
            ins.Parameters.AddWithValue("@regime", l.Regime); ins.Parameters.AddWithValue("@code", l.TaxCode);
            ins.Parameters.AddWithValue("@cat", (object?)l.TaxCategory ?? DBNull.Value);
            ins.Parameters.AddWithValue("@erc", (object?)l.ExemptionReasonCode ?? DBNull.Value);
            ins.Parameters.AddWithValue("@jur", (object?)l.Jurisdiction ?? DBNull.Value);
            ins.Parameters.AddWithValue("@taxable", l.TaxableAmount); ins.Parameters.AddWithValue("@rate", l.Rate);
            ins.Parameters.AddWithValue("@amt", l.TaxAmount); ins.Parameters.AddWithValue("@incl", l.PriceInclusive);
            ins.Parameters.AddWithValue("@exreason", (object?)l.ExemptReason ?? DBNull.Value);
            ins.Parameters.AddWithValue("@pid", (object?)outcome.TaxProfileId ?? DBNull.Value);
            ins.Parameters.AddWithValue("@tp", (object)taxPoint);
            await ins.ExecuteNonQueryAsync(ct);
        }

        await using var upd = new NpgsqlCommand(
            @"UPDATE invoice_drafts
              SET tax_total=@tax, total=@total, subtotal=@sub, tax_profile_id=@pid, tax_point_date=@tp, updated_at=NOW()
              WHERE company_id=@c AND id=@d", conn, tx);
        upd.Parameters.AddWithValue("@tax", outcome.TaxTotal);
        upd.Parameters.AddWithValue("@total", outcome.Total);
        upd.Parameters.AddWithValue("@sub", outcome.Subtotal);
        upd.Parameters.AddWithValue("@pid", (object?)outcome.TaxProfileId ?? DBNull.Value);
        upd.Parameters.AddWithValue("@tp", outcome.Applied ? (object)taxPoint : DBNull.Value);
        upd.Parameters.AddWithValue("@c", companyId); upd.Parameters.AddWithValue("@d", draftId);
        await upd.ExecuteNonQueryAsync(ct);
    }

    // ── rule matching ──

    // Most-specific match wins: every non-NULL match_* must equal the line fact; score = count of
    // matched constraints. Rules arrive priority DESC so a strict `>` keeps the highest-priority winner
    // on a score tie. A catch-all rule (all match_* NULL) always matches with score 0.
    private static TaxRule? ResolveRule(IReadOnlyList<TaxRule> rules, long customerId, string? chargeCode, string? chargeType, string? jurisdiction)
    {
        TaxRule? best = null; var bestScore = -1;
        foreach (var r in rules)
        {
            var score = 0;
            if (r.MatchCustomerId is { } mc) { if (mc != customerId) continue; score++; }
            if (r.MatchChargeCode is { } cc) { if (!Eq(cc, chargeCode)) continue; score++; }
            if (r.MatchChargeType is { } cty) { if (!Eq(cty, chargeType)) continue; score++; }
            if (r.MatchJurisdiction is { } mj) { if (!Eq(mj, jurisdiction)) continue; score++; }
            if (score > bestScore) { best = r; bestScore = score; }
        }
        return best;
    }

    private static bool Eq(string a, string? b) => b is not null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    // A line is freight when its charge type or code names it so — used only by the profile-level
    // freight_taxable=false coarse switch (fine-grained freight rules remain rule-driven).
    private static bool IsFreight(DraftLine line)
        => (line.ChargeType is { } t && t.Contains("freight", StringComparison.OrdinalIgnoreCase))
        || (line.ChargeCode is { } c && c.Contains("freight", StringComparison.OrdinalIgnoreCase));

    // ── reads ──

    private async Task<DraftHeader?> LoadDraftAsync(long companyId, Guid draftId, CancellationToken ct)
    {
        var r = await db.QuerySingleAsync(
            "SELECT customer_id, currency, status, subtotal, created_at FROM invoice_drafts WHERE company_id=@c AND id=@d",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@d", draftId); }, ct);
        if (r is null) return null;
        return new DraftHeader(
            draftId,
            Convert.ToInt64(r["customerId"], CultureInfo.InvariantCulture),
            r.GetValueOrDefault("currency")?.ToString() ?? "USD",
            r.GetValueOrDefault("status")?.ToString() ?? "draft",
            Convert.ToDecimal(r.GetValueOrDefault("subtotal") ?? 0m, CultureInfo.InvariantCulture),
            DateOnly.FromDateTime(Convert.ToDateTime(r["createdAt"], CultureInfo.InvariantCulture)));
    }

    private async Task<TaxProfile?> ResolveProfileAsync(long companyId, string currency, DateOnly taxPoint, CancellationToken ct)
    {
        var r = await db.QuerySingleAsync(
            @"SELECT id, regime, price_inclusive, freight_taxable, currency FROM tax_profiles
              WHERE company_id=@c AND status='published'
                AND effective_date <= @tp AND (expiry_date IS NULL OR expiry_date >= @tp)
                AND (currency IS NULL OR currency = @cur)
              ORDER BY (currency IS NOT NULL) DESC, effective_date DESC LIMIT 1",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@tp", taxPoint);
                   c.Parameters.AddWithValue("@cur", currency); }, ct);
        if (r is null) return null;
        return new TaxProfile(
            Convert.ToInt64(r["id"], CultureInfo.InvariantCulture),
            (r.GetValueOrDefault("regime")?.ToString() ?? "vat").Trim().ToLowerInvariant(),
            Convert.ToBoolean(r.GetValueOrDefault("priceInclusive") ?? false),
            Convert.ToBoolean(r.GetValueOrDefault("freightTaxable") ?? true));
    }

    private async Task<IReadOnlyList<TaxRule>> LoadRulesAsync(long companyId, long profileId, CancellationToken ct)
    {
        var rows = await db.QueryAsync(
            "SELECT * FROM tax_rules WHERE company_id=@c AND tax_profile_id=@p ORDER BY priority DESC, id",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@p", profileId); }, ct);
        return rows.Select(r => new TaxRule(
            r.GetValueOrDefault("matchCustomerId") is { } mc and not DBNull ? Convert.ToInt64(mc, CultureInfo.InvariantCulture) : null,
            Nz(r, "matchChargeCode"), Nz(r, "matchChargeType"), Nz(r, "matchJurisdiction"),
            (r.GetValueOrDefault("taxCode")?.ToString() ?? "STANDARD"),
            Nz(r, "taxCategory"), Nz(r, "exemptionReasonCode"),
            Convert.ToDecimal(r.GetValueOrDefault("rate") ?? 0m, CultureInfo.InvariantCulture),
            Convert.ToBoolean(r.GetValueOrDefault("taxable") ?? true))).ToList();
    }

    private async Task<CustomerStatus?> LoadCustomerStatusAsync(long companyId, long customerId, DateOnly taxPoint, CancellationToken ct)
    {
        var r = await db.QuerySingleAsync(
            @"SELECT tax_exempt, exemption_reason, exemption_reason_code, jurisdiction FROM customer_tax_status
              WHERE company_id=@c AND customer_id=@cust
                AND effective_date <= @tp AND (expiry_date IS NULL OR expiry_date >= @tp)
              ORDER BY effective_date DESC LIMIT 1",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@cust", customerId);
                   c.Parameters.AddWithValue("@tp", taxPoint); }, ct);
        if (r is null) return null;
        return new CustomerStatus(
            Convert.ToBoolean(r.GetValueOrDefault("taxExempt") ?? false),
            Nz(r, "exemptionReason"), Nz(r, "exemptionReasonCode"), Nz(r, "jurisdiction"));
    }

    private async Task<bool> HasSellerRegistrationAsync(long companyId, string regime, DateOnly taxPoint, CancellationToken ct)
        => (await db.ScalarLongAsync(
            @"SELECT COUNT(*) FROM seller_tax_registration
              WHERE company_id=@c AND regime=@r AND effective_date <= @tp AND (expiry_date IS NULL OR expiry_date >= @tp)",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@r", regime);
                   c.Parameters.AddWithValue("@tp", taxPoint); }, ct)) > 0;

    private async Task<List<DraftLine>> LoadDraftLinesAsync(long companyId, Guid draftId, CancellationToken ct)
    {
        var rows = await db.QueryAsync(
            "SELECT id, charge_code, unit, amount, job_charge_id FROM invoice_draft_lines WHERE company_id=@c AND invoice_draft_id=@d ORDER BY line_no",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@d", draftId); }, ct);
        return rows.Select(r => new DraftLine(
            (Guid)r["id"]!, Nz(r, "chargeCode"), Nz(r, "unit"),
            Convert.ToDecimal(r.GetValueOrDefault("amount") ?? 0m, CultureInfo.InvariantCulture),
            r.GetValueOrDefault("jobChargeId") is { } jc and not DBNull ? Convert.ToInt64(jc, CultureInfo.InvariantCulture) : null)).ToList();
    }

    public async Task<List<Dictionary<string, object?>>> GetDraftTaxLinesAsync(long companyId, Guid draftId, CancellationToken ct = default)
        => await db.QueryAsync("SELECT * FROM invoice_tax_lines WHERE company_id=@c AND invoice_draft_id=@d ORDER BY created_at",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@d", draftId); }, ct);

    public async Task<List<Dictionary<string, object?>>> GetIssuedTaxLinesAsync(long companyId, Guid issuedInvoiceId, CancellationToken ct = default)
        => await db.QueryAsync("SELECT * FROM issued_invoice_tax_lines WHERE company_id=@c AND issued_invoice_id=@i ORDER BY created_at",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@i", issuedInvoiceId); }, ct);

    // ── config CRUD ──

    public async Task<List<Dictionary<string, object?>>> ListProfilesAsync(long companyId, CancellationToken ct = default)
        => await db.QueryAsync("SELECT * FROM tax_profiles WHERE company_id=@c ORDER BY created_at DESC, id DESC",
            c => c.Parameters.AddWithValue("@c", companyId), ct);

    public async Task<Dictionary<string, object?>?> GetProfileAsync(long companyId, long profileId, CancellationToken ct = default)
        => await db.QuerySingleAsync("SELECT * FROM tax_profiles WHERE company_id=@c AND id=@p",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@p", profileId); }, ct);

    public sealed record TaxActionOutcome(bool Ok, long? Id, string Status, string? Reason = null);

    // Create a draft profile, or update a DRAFT one. A published profile is append-only: a rate change
    // is a new draft profile, never an UPDATE — reject it.
    public async Task<TaxActionOutcome> UpsertProfileAsync(long companyId, Dictionary<string, object?> body, long userId, CancellationToken ct = default)
    {
        var id = LongOf(body, "id");
        if (id is { } pid)
        {
            var status = (await db.QuerySingleAsync("SELECT status FROM tax_profiles WHERE company_id=@c AND id=@p",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@p", pid); }, ct))
                ?.GetValueOrDefault("status")?.ToString();
            if (status is null) return new TaxActionOutcome(false, null, "missing", "not_found");
            if (status != "draft") return new TaxActionOutcome(false, pid, status, "profile_published_immutable");
            await db.ExecuteAsync(
                @"UPDATE tax_profiles SET profile_name=@name, regime=@regime, price_inclusive=@incl, freight_taxable=@freight,
                     currency=@cur, effective_date=@eff, expiry_date=@exp, updated_at=NOW()
                  WHERE company_id=@c AND id=@p AND status='draft'",
                c => BindProfile(c, companyId, body, pid), ct);
            return new TaxActionOutcome(true, pid, "draft");
        }

        var newId = await db.InsertAsync(
            @"INSERT INTO tax_profiles (company_id, profile_code, profile_name, regime, price_inclusive, freight_taxable,
                 currency, effective_date, expiry_date, status, author_user_id)
              VALUES (@c, @code, @name, @regime, @incl, @freight, @cur, @eff, @exp, 'draft', @author) RETURNING id",
            c => { BindProfile(c, companyId, body, null);
                   c.Parameters.AddWithValue("@code", Str(body, "profileCode") ?? $"TAX-{companyId}-{Str(body, "profileName")}");
                   c.Parameters.AddWithValue("@author", userId); }, ct);
        return new TaxActionOutcome(true, newId, "draft");
    }

    public async Task<TaxActionOutcome> UpsertRuleAsync(long companyId, long profileId, Dictionary<string, object?> body, CancellationToken ct = default)
    {
        var status = (await db.QuerySingleAsync("SELECT status FROM tax_profiles WHERE company_id=@c AND id=@p",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@p", profileId); }, ct))
            ?.GetValueOrDefault("status")?.ToString();
        if (status is null) return new TaxActionOutcome(false, null, "missing", "not_found");
        if (status != "draft") return new TaxActionOutcome(false, profileId, status, "profile_published_immutable");

        var id = await db.InsertAsync(
            @"INSERT INTO tax_rules (company_id, tax_profile_id, match_customer_id, match_charge_code, match_charge_type,
                 match_jurisdiction, tax_code, tax_category, exemption_reason_code, rate, taxable, priority)
              VALUES (@c, @p, @mc, @mcc, @mct, @mj, @code, @cat, @erc, @rate, @taxable, @prio) RETURNING id",
            c => {
                c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@p", profileId);
                c.Parameters.AddWithValue("@mc", (object?)LongOf(body, "matchCustomerId") ?? DBNull.Value);
                c.Parameters.AddWithValue("@mcc", (object?)Str(body, "matchChargeCode") ?? DBNull.Value);
                c.Parameters.AddWithValue("@mct", (object?)Str(body, "matchChargeType") ?? DBNull.Value);
                c.Parameters.AddWithValue("@mj", (object?)Str(body, "matchJurisdiction") ?? DBNull.Value);
                c.Parameters.AddWithValue("@code", Str(body, "taxCode") ?? "STANDARD");
                c.Parameters.AddWithValue("@cat", (object?)Str(body, "taxCategory") ?? DBNull.Value);
                c.Parameters.AddWithValue("@erc", (object?)Str(body, "exemptionReasonCode") ?? DBNull.Value);
                c.Parameters.AddWithValue("@rate", DecOf(body, "rate") ?? 0m);
                c.Parameters.AddWithValue("@taxable", BoolOf(body, "taxable") ?? true);
                c.Parameters.AddWithValue("@prio", (int)(LongOf(body, "priority") ?? 0));
            }, ct);
        return new TaxActionOutcome(true, id, "draft");
    }

    // Maker-checker + real approval gate. Publishing a rate schedule is money-affecting.
    public async Task<TaxActionOutcome> PublishProfileAsync(long companyId, long profileId, long userId, CancellationToken ct = default)
    {
        var row = await db.QuerySingleAsync(
            "SELECT status, author_user_id, approval_request_id FROM tax_profiles WHERE company_id=@c AND id=@p",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@p", profileId); }, ct);
        if (row is null) return new TaxActionOutcome(false, null, "missing", "not_found");
        var status = row.GetValueOrDefault("status")?.ToString() ?? "draft";
        if (status == "published") return new TaxActionOutcome(true, profileId, "published"); // idempotent
        if (status != "draft") return new TaxActionOutcome(false, profileId, status, "not_publishable");

        var author = row.GetValueOrDefault("authorUserId") is { } a and not DBNull ? Convert.ToInt64(a, CultureInfo.InvariantCulture) : (long?)null;
        if (author is null) return new TaxActionOutcome(false, profileId, status, "no_author_maker_checker");
        if (author == userId) return new TaxActionOutcome(false, profileId, status, "author_cannot_publish");

        // Real approval gate: the HighRiskActions catalog entry alone does not gate (it is only enforced
        // on the AI dispatcher path), so a publish requires an explicit approval request => approved.
        if (approval is null) return new TaxActionOutcome(false, profileId, status, "approval_unavailable");
        var existing = row.GetValueOrDefault("approvalRequestId")?.ToString();
        if (string.IsNullOrEmpty(existing))
        {
            var req = approval.CreateRequest(companyId.ToString(CultureInfo.InvariantCulture), ActorTypes.TenantUser,
                userId.ToString(CultureInfo.InvariantCulture), "finance.tax_profile.publish", "tax_profile",
                profileId.ToString(CultureInfo.InvariantCulture), "{}", "high");
            await db.ExecuteAsync("UPDATE tax_profiles SET approval_request_id=@r, status='pending_approval', updated_at=NOW() WHERE company_id=@c AND id=@p",
                c => { c.Parameters.AddWithValue("@r", req.Id.ToString(CultureInfo.InvariantCulture)); c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@p", profileId); }, ct);
            return new TaxActionOutcome(false, profileId, "pending_approval", "approval_requested");
        }

        var approved = await db.ScalarLongAsync(
            "SELECT COUNT(*) FROM approval_requests WHERE id=@r AND status IN ('approved','Approved')",
            c => c.Parameters.AddWithValue("@r", Convert.ToInt64(existing, CultureInfo.InvariantCulture)), ct);
        if (approved == 0) return new TaxActionOutcome(false, profileId, "pending_approval", "awaiting_approval");

        await db.ExecuteAsync(
            "UPDATE tax_profiles SET status='published', published_by_user_id=@u, published_at=NOW(), updated_at=NOW() WHERE company_id=@c AND id=@p",
            c => { c.Parameters.AddWithValue("@u", userId); c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@p", profileId); }, ct);
        return new TaxActionOutcome(true, profileId, "published");
    }

    public async Task<Dictionary<string, object?>?> GetCustomerTaxStatusAsync(long companyId, long customerId, CancellationToken ct = default)
        => await db.QuerySingleAsync(
            "SELECT * FROM customer_tax_status WHERE company_id=@c AND customer_id=@cust ORDER BY effective_date DESC LIMIT 1",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@cust", customerId); }, ct);

    public async Task UpsertCustomerTaxStatusAsync(long companyId, Dictionary<string, object?> body, CancellationToken ct = default)
        => await db.ExecuteAsync(
            @"INSERT INTO customer_tax_status (company_id, customer_id, tax_exempt, exemption_reason, exemption_reason_code,
                 exemption_certificate, tax_registration_no, jurisdiction, effective_date, expiry_date)
              VALUES (@c, @cust, @exempt, @reason, @rc, @cert, @trn, @jur, @eff, @exp)
              ON CONFLICT (company_id, customer_id, effective_date) DO UPDATE SET
                 tax_exempt=EXCLUDED.tax_exempt, exemption_reason=EXCLUDED.exemption_reason,
                 exemption_reason_code=EXCLUDED.exemption_reason_code, exemption_certificate=EXCLUDED.exemption_certificate,
                 tax_registration_no=EXCLUDED.tax_registration_no, jurisdiction=EXCLUDED.jurisdiction,
                 expiry_date=EXCLUDED.expiry_date, updated_at=NOW()",
            c => {
                c.Parameters.AddWithValue("@c", companyId);
                c.Parameters.AddWithValue("@cust", LongOf(body, "customerId") ?? 0);
                c.Parameters.AddWithValue("@exempt", BoolOf(body, "taxExempt") ?? false);
                c.Parameters.AddWithValue("@reason", (object?)Str(body, "exemptionReason") ?? DBNull.Value);
                c.Parameters.AddWithValue("@rc", (object?)Str(body, "exemptionReasonCode") ?? DBNull.Value);
                c.Parameters.AddWithValue("@cert", (object?)Str(body, "exemptionCertificate") ?? DBNull.Value);
                c.Parameters.AddWithValue("@trn", (object?)Str(body, "taxRegistrationNo") ?? DBNull.Value);
                c.Parameters.AddWithValue("@jur", (object?)Str(body, "jurisdiction") ?? DBNull.Value);
                c.Parameters.AddWithValue("@eff", DateOf(body, "effectiveDate") ?? Today());
                c.Parameters.AddWithValue("@exp", (object?)DateOf(body, "expiryDate") ?? DBNull.Value);
            }, ct);

    public async Task<Dictionary<string, object?>?> GetSellerRegistrationAsync(long companyId, string jurisdiction, string regime, CancellationToken ct = default)
        => await db.QuerySingleAsync(
            @"SELECT * FROM seller_tax_registration WHERE company_id=@c AND jurisdiction=@j AND regime=@r
              ORDER BY effective_date DESC LIMIT 1",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@j", jurisdiction); c.Parameters.AddWithValue("@r", regime); }, ct);

    public async Task UpsertSellerRegistrationAsync(long companyId, Dictionary<string, object?> body, CancellationToken ct = default)
        => await db.ExecuteAsync(
            @"INSERT INTO seller_tax_registration (company_id, jurisdiction, regime, tax_registration_no, legal_name, effective_date, expiry_date)
              VALUES (@c, @j, @r, @trn, @name, @eff, @exp)
              ON CONFLICT (company_id, jurisdiction, regime, effective_date) DO UPDATE SET
                 tax_registration_no=EXCLUDED.tax_registration_no, legal_name=EXCLUDED.legal_name,
                 expiry_date=EXCLUDED.expiry_date, updated_at=NOW()",
            c => {
                c.Parameters.AddWithValue("@c", companyId);
                c.Parameters.AddWithValue("@j", Str(body, "jurisdiction") ?? "SA");
                c.Parameters.AddWithValue("@r", Str(body, "regime") ?? "zatca_vat");
                c.Parameters.AddWithValue("@trn", Str(body, "taxRegistrationNo") ?? "");
                c.Parameters.AddWithValue("@name", (object?)Str(body, "legalName") ?? DBNull.Value);
                c.Parameters.AddWithValue("@eff", DateOf(body, "effectiveDate") ?? Today());
                c.Parameters.AddWithValue("@exp", (object?)DateOf(body, "expiryDate") ?? DBNull.Value);
            }, ct);

    // ── helpers ──

    private void BindProfile(NpgsqlCommand c, long companyId, Dictionary<string, object?> body, long? id)
    {
        c.Parameters.AddWithValue("@c", companyId);
        if (id is { } pid) c.Parameters.AddWithValue("@p", pid);
        c.Parameters.AddWithValue("@name", Str(body, "profileName") ?? "Tax profile");
        c.Parameters.AddWithValue("@regime", Str(body, "regime") ?? "vat");
        c.Parameters.AddWithValue("@incl", BoolOf(body, "priceInclusive") ?? false);
        c.Parameters.AddWithValue("@freight", BoolOf(body, "freightTaxable") ?? true);
        c.Parameters.AddWithValue("@cur", (object?)Str(body, "currency") ?? DBNull.Value);
        c.Parameters.AddWithValue("@eff", DateOf(body, "effectiveDate") ?? Today());
        c.Parameters.AddWithValue("@exp", (object?)DateOf(body, "expiryDate") ?? DBNull.Value);
    }

    private static TaxComputationOutcome Fail(string reason, decimal subtotal, decimal total)
        => new(false, reason, 0, subtotal, total, null, Array.Empty<TaxLineResult>());

    private static decimal Round(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);
    private static string? Nz(Dictionary<string, object?> r, string k) => r.GetValueOrDefault(k) is { } v and not DBNull ? v.ToString() : null;
    private static string? Str(Dictionary<string, object?> b, string k) => b.TryGetValue(k, out var v) && v is not null ? v.ToString() : null;
    private static long? LongOf(Dictionary<string, object?> b, string k) => Str(b, k) is { } s && long.TryParse(s, out var v) ? v : null;
    private static decimal? DecOf(Dictionary<string, object?> b, string k) => Str(b, k) is { } s && decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;
    private static bool? BoolOf(Dictionary<string, object?> b, string k) => Str(b, k) is { } s && bool.TryParse(s, out var v) ? v : null;
    private static DateOnly? DateOf(Dictionary<string, object?> b, string k) => Str(b, k) is { } s && DateOnly.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var v) ? v : null;

    private sealed record DraftHeader(Guid Id, long CustomerId, string Currency, string Status, decimal Subtotal, DateOnly CreatedOn);
    private sealed record TaxProfile(long Id, string Regime, bool PriceInclusive, bool FreightTaxable);
    private sealed record TaxRule(long? MatchCustomerId, string? MatchChargeCode, string? MatchChargeType, string? MatchJurisdiction,
        string TaxCode, string? TaxCategory, string? ExemptionReasonCode, decimal Rate, bool Taxable);
    private sealed record CustomerStatus(bool TaxExempt, string? ExemptionReason, string? ExemptionReasonCode, string? Jurisdiction);
    private sealed record DraftLine(Guid Id, string? ChargeCode, string? ChargeType, decimal Amount, long? JobChargeId);
}
