using System.Text.Json;
using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

// ─────────────────────────────────────────────────────────────────────────────
// PLATFORM BILLING ENGINE — itemized OpsTrax → tenant invoicing with indirect tax
//
// Composition of a platform invoice, in the order a controller would build it:
//
//   1. Subscription base       — the package's recurring fee.
//   2. Seats                   — billable users above the package's included seats.
//   3. Feature plan items      — the per-tenant commercial terms. This is where
//                                "free for this client / flat fee / per event"
//                                lives (tenant_billing_plan_items).
//   4. Market packs            — active regional add-ons.
//   5. Package meter overage   — pricing_rules + tenant_contract_overrides, but
//                                ONLY for meters no plan item already prices.
//                                A meter is billed once or not at all.
//   6. Tax                     — determined per line from the tenant's country of
//                                activation, the seller's registration in that
//                                country, and the buyer's tax ID.
//
// Rounding: every line rounds its own tax half-away-from-zero to the currency's
// minor unit, and the header tax total is the SUM OF LINE TAX. Computing tax on
// the header subtotal instead produces cent-level disagreement with the line
// detail — the first thing an auditor reconciles and the first thing that fails.
//
// Immutability: a draft is freely editable. Issuing allocates a gap-free document
// number and freezes the document. After that the only lawful correction is a
// credit note, which is a new document referencing the original.
// ─────────────────────────────────────────────────────────────────────────────
public sealed class PlatformBillingService(Database db)
{
    // ════════════════════════════ Currency ═══════════════════════════════════

    // ISO 4217 exponents that are not 2. Rendering a stored minor-unit amount with
    // the wrong exponent misstates the invoice by orders of magnitude — ¥5,000 shown
    // as ¥50.00, or 5.000 KWD shown as 50.00 KWD.
    private static readonly Dictionary<string, int> MinorUnitOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
        ["JPY"] = 0, ["KRW"] = 0, ["CLP"] = 0, ["ISK"] = 0, ["VND"] = 0, ["XOF"] = 0, ["XAF"] = 0,
        ["KWD"] = 3, ["BHD"] = 3, ["OMR"] = 3, ["JOD"] = 3, ["TND"] = 3, ["IQD"] = 3, ["LYD"] = 3,
    };

    // Amounts are ALWAYS held in the currency's minor unit, so rounding is simply to
    // a whole stored integer. This exponent is what the UI needs to render and parse
    // that integer: 5000 is ¥5,000 in JPY, $50.00 in USD and 5.000 KWD in Kuwait.
    public static int MinorUnits(string? currency) =>
        currency is not null && MinorUnitOverrides.TryGetValue(currency, out var u) ? u : 2;

    // ════════════════════════════ Types ══════════════════════════════════════

    public sealed record TaxDecision(
        string Treatment, string TaxCode, string TaxCategory, decimal Rate,
        string? ReasonText, string RuleKey);

    public sealed record TaxContext(
        string CountryCode, string CountryName, string Regime, string TaxLabel,
        bool SellerRegistered, string? SellerLegalName, string? SellerTaxNo,
        string? BuyerLegalName, string? BuyerTaxNo, string Currency, int MinorUnits,
        string? InvoicingScheme, string PlaceOfSupply, TaxDecision Decision);

    public sealed class DraftLine
    {
        public string Source { get; init; } = "manual";
        public string? FeatureKey { get; init; }
        public string? MeterKey { get; init; }
        public string Description { get; init; } = "";
        public string ChargeModel { get; init; } = "flat";
        public decimal Quantity { get; init; } = 1;
        public string? Unit { get; init; }
        public long UnitPriceCents { get; init; }
        public long GrossAmountCents { get; set; }
        public long DiscountCents { get; init; }
        public long NetAmountCents { get; set; }
        public string? TaxCode { get; set; }
        public string? TaxCategory { get; set; }
        public string TaxTreatment { get; set; } = "standard";
        public decimal TaxRate { get; set; }
        public long TaxAmountCents { get; set; }
        public string? ExemptionReason { get; set; }
        public long TotalCents { get; set; }
        public DateOnly? PeriodStart { get; init; }
        public DateOnly? PeriodEnd { get; init; }
    }

    public sealed record Draft(
        long CompanyId, string TenantName, DateOnly PeriodStart, DateOnly PeriodEnd,
        TaxContext Tax, List<DraftLine> Lines,
        long SubtotalCents, long DiscountCents, long TaxTotalCents, long TotalCents,
        List<string> Notes);

    // ════════════════════════════ Tax determination ══════════════════════════

    // Resolves the whole tax posture for one tenant: where the supply lands, who
    // the seller is there, whether the buyer self-accounts, and which rule decided.
    public async Task<TaxContext> ResolveTaxAsync(long companyId, CancellationToken ct = default)
    {
        var tenant = await db.QuerySingleAsync("""
            SELECT c.name, c.legal_name, c.country, c.currency, c.tax_id,
                   cp.country_name, cp.default_currency, cp.default_tax_rate,
                   cp.tax_id_label, cp.invoicing_scheme,
                   s.billing_currency
            FROM companies c
            LEFT JOIN country_profiles cp ON cp.country_code = c.country
            LEFT JOIN tenant_subscriptions s ON s.company_id = c.id
            WHERE c.id = @c
            """, cmd => cmd.Parameters.AddWithValue("@c", companyId), ct);

        var country = tenant?["country"]?.ToString();
        var buyerTaxNo = tenant?["taxId"]?.ToString();
        var currency = FirstNonEmpty(
            tenant?["billingCurrency"]?.ToString(),
            tenant?["currency"]?.ToString(),
            tenant?["defaultCurrency"]?.ToString()) ?? "USD";

        Dictionary<string, object?>? reg = null;
        if (!string.IsNullOrWhiteSpace(country))
            reg = await db.QuerySingleAsync(
                "SELECT * FROM platform_tax_registrations WHERE country_code=@c AND (effective_to IS NULL OR effective_to >= CURRENT_DATE)",
                cmd => cmd.Parameters.AddWithValue("@c", country!), ct);

        var registered = reg?["registered"] is bool b && b;
        var regime = reg?["regime"]?.ToString()
                     ?? (tenant?["defaultTaxRate"] is null or DBNull ? "none" : "vat");
        var standardRate = reg?["standardRate"] is { } sr and not DBNull ? Convert.ToDecimal(sr)
                         : tenant?["defaultTaxRate"] is { } dr and not DBNull ? Convert.ToDecimal(dr)
                         : 0m;
        var taxLabel = FirstNonEmpty(reg?["taxLabel"]?.ToString(), tenant?["taxIdLabel"]?.ToString()) ?? "Tax";

        var decision = await DecideAsync(country, registered, !string.IsNullOrWhiteSpace(buyerTaxNo),
            regime, standardRate, ct);

        return new TaxContext(
            CountryCode: country ?? "",
            CountryName: tenant?["countryName"]?.ToString() ?? country ?? "Unspecified",
            Regime: regime,
            TaxLabel: taxLabel,
            SellerRegistered: registered,
            SellerLegalName: reg?["legalName"]?.ToString(),
            SellerTaxNo: reg?["taxRegistrationNo"]?.ToString(),
            BuyerLegalName: FirstNonEmpty(tenant?["legalName"]?.ToString(), tenant?["name"]?.ToString()),
            BuyerTaxNo: buyerTaxNo,
            Currency: currency,
            MinorUnits: MinorUnits(currency),
            InvoicingScheme: FirstNonEmpty(reg?["invoicingScheme"]?.ToString(), tenant?["invoicingScheme"]?.ToString()),
            PlaceOfSupply: tenant?["countryName"]?.ToString() ?? country ?? "Unspecified",
            Decision: decision);
    }

    // Walks the determination table in priority order. A rule matches when every
    // non-NULL match_* column equals the tenant's facts; NULL means "any". The
    // first match wins, exactly like a VAT decision table on paper.
    private async Task<TaxDecision> DecideAsync(
        string? country, bool registered, bool hasTaxId, string regime, decimal standardRate, CancellationToken ct)
    {
        // No indirect tax regime in the place of supply short-circuits the ladder:
        // there is no rate to apply and no registration that could create one.
        if (string.Equals(regime, "none", StringComparison.OrdinalIgnoreCase))
            return new TaxDecision("out_of_scope", "O", "O", 0m,
                "Outside the scope of VAT/GST in the place of supply.", "no_regime");

        var rules = await db.QueryAsync(
            "SELECT * FROM platform_tax_rules WHERE active ORDER BY priority, id", ct: ct);

        foreach (var r in rules)
        {
            var mc = r["matchCountry"]?.ToString();
            if (!string.IsNullOrWhiteSpace(mc) && !string.Equals(mc, country, StringComparison.OrdinalIgnoreCase)) continue;
            if (r["matchRegistered"] is bool mr && mr != registered) continue;
            if (r["matchHasTaxId"] is bool mt && mt != hasTaxId) continue;

            var rate = r["rate"] is { } rr and not DBNull ? Convert.ToDecimal(rr) : standardRate;
            return new TaxDecision(
                r["treatment"]?.ToString() ?? "standard",
                r["taxCode"]?.ToString() ?? "STD",
                r["taxCategory"]?.ToString() ?? "S",
                rate,
                r["reasonText"]?.ToString(),
                r["ruleKey"]?.ToString() ?? "unmatched");
        }

        // Nothing matched. Fail SAFE for the seller's compliance position by
        // charging the standard rate rather than silently zero-rating a supply.
        return new TaxDecision("standard", "STD", "S", standardRate, null, "fallback_standard");
    }

    // ════════════════════════════ Draft assembly ═════════════════════════════

    public async Task<Draft> BuildDraftAsync(
        long companyId, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct = default)
    {
        var tax = await ResolveTaxAsync(companyId, ct);
        var notes = new List<string>();
        var lines = new List<DraftLine>();

        var tenantName = await db.QuerySingleAsync("SELECT name FROM companies WHERE id=@c",
            c => c.Parameters.AddWithValue("@c", companyId), ct);

        var sub = await db.QuerySingleAsync("""
            SELECT s.status, s.seat_limit, s.billing_currency, s.package_id, s.contract_start,
                   p.name package_name, p.base_price_cents, p.seat_price_cents,
                   p.included_seats, p.billing_interval
            FROM tenant_subscriptions s
            LEFT JOIN packages p ON p.id = s.package_id
            WHERE s.company_id = @c
            """, c => c.Parameters.AddWithValue("@c", companyId), ct);

        // ── 1. Subscription base ─────────────────────────────────────────────
        if (sub is not null && sub["packageId"] is not (null or DBNull))
        {
            var basePrice = AsLong(sub["basePriceCents"]);
            var interval = sub["billingInterval"]?.ToString() ?? "monthly";
            var annual = string.Equals(interval, "annual", StringComparison.OrdinalIgnoreCase)
                      || string.Equals(interval, "yearly", StringComparison.OrdinalIgnoreCase);

            // An annual package is charged once, in the period containing its
            // contract anniversary. Billing it from every monthly run would invoice
            // twelve times the contract value before anyone noticed.
            var anniversaryMonth = AsDate(sub["contractStart"])?.Month ?? 1;
            var dueThisPeriod = !annual || periodStart.Month == anniversaryMonth;

            if (dueThisPeriod)
            {
                lines.Add(new DraftLine
                {
                    Source = "subscription",
                    FeatureKey = "subscription.base",
                    Description = annual
                        ? $"Subscription — {sub["packageName"] ?? "Plan"} (annual term from {periodStart:yyyy-MM-dd})"
                        : $"Subscription — {sub["packageName"] ?? "Plan"} ({periodStart:yyyy-MM-dd} → {periodEnd:yyyy-MM-dd})",
                    ChargeModel = "flat",
                    Quantity = 1,
                    UnitPriceCents = basePrice,
                    PeriodStart = periodStart,
                    PeriodEnd = annual ? periodStart.AddYears(1).AddDays(-1) : periodEnd,
                });
            }
            else
            {
                notes.Add($"{sub["packageName"] ?? "Plan"} is billed annually and was already charged for this term — no subscription line this period.");
            }

            // ── 2. Seats above the included allowance ────────────────────────
            var seatPrice = AsLong(sub["seatPriceCents"]);
            var includedSeats = (int)AsLong(sub["includedSeats"]);
            if (seatPrice > 0)
            {
                var billableUsers = await db.ScalarLongAsync(
                    "SELECT COUNT(*) FROM users WHERE company_id=@c AND COALESCE(status,'Active') NOT IN ('Deleted','Disabled')",
                    c => c.Parameters.AddWithValue("@c", companyId), ct);
                var chargeable = billableUsers - includedSeats;
                if (chargeable > 0)
                    lines.Add(new DraftLine
                    {
                        Source = "subscription",
                        FeatureKey = "subscription.seats",
                        Description = $"Additional seats ({billableUsers} active, {includedSeats} included)",
                        ChargeModel = "per_seat",
                        Quantity = chargeable,
                        Unit = "seat",
                        UnitPriceCents = seatPrice,
                        PeriodStart = periodStart,
                        PeriodEnd = periodEnd,
                    });
            }
        }
        else
        {
            notes.Add("No package is assigned to this tenant — no subscription line was generated.");
        }

        // ── 3. Per-feature commercial terms ──────────────────────────────────
        var planItems = await db.QueryAsync("""
            SELECT * FROM tenant_billing_plan_items
            WHERE company_id=@c AND active
              AND effective_from <= @pe
              AND (effective_to IS NULL OR effective_to >= @ps)
            ORDER BY feature_key
            """,
            c =>
            {
                c.Parameters.AddWithValue("@c", companyId);
                c.Parameters.AddWithValue("@ps", periodStart.ToDateTime(TimeOnly.MinValue));
                c.Parameters.AddWithValue("@pe", periodEnd.ToDateTime(TimeOnly.MinValue));
            }, ct);

        var pricedMeters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in planItems)
        {
            var featureKey = item["featureKey"]?.ToString() ?? "";
            var label = FirstNonEmpty(item["featureLabel"]?.ToString(), featureKey) ?? featureKey;
            var model = item["chargeModel"]?.ToString() ?? "included";
            var meterKey = item["meterKey"]?.ToString();
            if (!string.IsNullOrWhiteSpace(meterKey)) pricedMeters.Add(meterKey!);

            var line = await BuildPlanLineAsync(companyId, item, featureKey, label, model, meterKey,
                periodStart, periodEnd, ct);
            if (line is not null) lines.Add(line);
        }

        // ── 4. Market packs ──────────────────────────────────────────────────
        var packs = await db.QueryAsync("""
            SELECT t.pack_code, mp.name, COALESCE(t.price_override_cents, mp.base_price_cents) AS price
            FROM tenant_market_packs t JOIN market_packs mp ON mp.code = t.pack_code
            WHERE t.company_id=@c AND t.status='active'
            ORDER BY mp.name
            """, c => c.Parameters.AddWithValue("@c", companyId), ct);
        foreach (var p in packs)
        {
            var packCode = p["packCode"]?.ToString() ?? "";
            // A plan item for the same pack overrides the catalog price entirely.
            if (planItems.Any(i => string.Equals(i["featureKey"]?.ToString(), $"market_pack.{packCode}", StringComparison.OrdinalIgnoreCase)))
                continue;
            lines.Add(new DraftLine
            {
                Source = "market_pack",
                FeatureKey = $"market_pack.{packCode}",
                Description = $"Market Pack — {p["name"]}",
                ChargeModel = "flat",
                Quantity = 1,
                UnitPriceCents = AsLong(p["price"]),
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
            });
        }

        // ── 5. Package meter overage (only meters no plan item already prices) ─
        var packageId = sub?["packageId"] is { } pid and not DBNull ? Convert.ToInt64(pid) : (long?)null;
        if (packageId is not null)
        {
            var period = EntitlementService.CurrentPeriodKey();
            var rules = await db.QueryAsync("""
                SELECT pr.meter_key, pr.included_quantity, pr.unit_price_cents, pr.overage_allowed,
                       m.name meter_name, m.unit,
                       COALESCE(uc.value,0) AS used,
                       o.included_quantity AS o_included, o.unit_price_cents AS o_unit,
                       o.flat_discount_cents AS o_disc
                FROM pricing_rules pr
                LEFT JOIN usage_meters m ON m.meter_key = pr.meter_key
                LEFT JOIN usage_counters uc
                  ON uc.meter_key = pr.meter_key AND uc.company_id = @c
                 AND uc.period_key = CASE WHEN m.period='lifetime' THEN 'lifetime' ELSE @p END
                LEFT JOIN tenant_contract_overrides o ON o.company_id=@c AND o.meter_key=pr.meter_key
                WHERE pr.package_id=@pkg AND pr.active
                ORDER BY pr.meter_key
                """,
                c =>
                {
                    c.Parameters.AddWithValue("@c", companyId);
                    c.Parameters.AddWithValue("@p", period);
                    c.Parameters.AddWithValue("@pkg", packageId.Value);
                }, ct);

            foreach (var r in rules)
            {
                var meterKey = r["meterKey"]?.ToString() ?? "";
                if (pricedMeters.Contains(meterKey))
                {
                    notes.Add($"Meter '{meterKey}' is priced by a tenant plan item — the package overage rule was skipped to avoid double billing.");
                    continue;
                }
                if (r["overageAllowed"] is bool oa && !oa) continue;

                var used = AsDecimal(r["used"]);
                var included = r["oIncluded"] is not (null or DBNull) ? AsDecimal(r["oIncluded"]) : AsDecimal(r["includedQuantity"]);
                var unit = r["oUnit"] is not (null or DBNull) ? AsLong(r["oUnit"]) : AsLong(r["unitPriceCents"]);
                var disc = AsLong(r["oDisc"]);

                var overage = used > included ? used - included : 0m;
                if (overage <= 0) continue;

                lines.Add(new DraftLine
                {
                    Source = "usage",
                    FeatureKey = $"meter.{meterKey}",
                    MeterKey = meterKey,
                    Description = $"Usage overage — {FirstNonEmpty(r["meterName"]?.ToString(), meterKey)} ({used:0.##} used, {included:0.##} included)",
                    ChargeModel = "per_unit",
                    Quantity = overage,
                    Unit = r["unit"]?.ToString(),
                    UnitPriceCents = unit,
                    DiscountCents = disc,
                    PeriodStart = periodStart,
                    PeriodEnd = periodEnd,
                });
            }
        }

        Price(lines, tax);

        var subtotal = lines.Sum(l => l.NetAmountCents);
        var discount = lines.Sum(l => l.DiscountCents);
        var taxTotal = lines.Sum(l => l.TaxAmountCents);

        if (lines.Count == 0)
            notes.Add("Nothing billable for this period. Assign a package or add billing plan items for this tenant.");
        if (!tax.SellerRegistered && tax.Decision.Treatment == "standard")
            notes.Add($"Tax is being charged at the {tax.CountryName} standard rate, but no OpsTrax {tax.TaxLabel} registration number is on file for {tax.CountryCode}. Record it under Tax Registrations before issuing.");

        return new Draft(
            companyId,
            tenantName?["name"]?.ToString() ?? $"Tenant {companyId}",
            periodStart, periodEnd, tax, lines,
            subtotal, discount, taxTotal, subtotal + taxTotal, notes);
    }

    // One plan item → at most one invoice line. `free` and `included` deliberately
    // still emit a line at zero: an entitlement given away silently is invisible to
    // revenue analysis, and the customer should see what they are not being charged.
    private async Task<DraftLine?> BuildPlanLineAsync(
        long companyId, Dictionary<string, object?> item, string featureKey, string label,
        string model, string? meterKey, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct)
    {
        var flat = AsLong(item["flatPriceCents"]);
        var unitPrice = AsLong(item["unitPriceCents"]);
        var included = AsDecimal(item["includedQuantity"]);
        var minimum = item["minimumCents"] is { } mn and not DBNull ? Convert.ToInt64(mn) : (long?)null;
        var cap = item["capCents"] is { } cp and not DBNull ? Convert.ToInt64(cp) : (long?)null;

        DraftLine Line(string desc, decimal qty, long unit, long gross, string? unitLabel = null) => new()
        {
            Source = "feature",
            FeatureKey = featureKey,
            MeterKey = meterKey,
            Description = desc,
            ChargeModel = model,
            Quantity = qty,
            Unit = unitLabel,
            UnitPriceCents = unit,
            GrossAmountCents = gross,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
        };

        switch (model)
        {
            case "free":
                return Line($"{label} — included at no charge", 1, 0, 0);

            case "included":
                return Line($"{label} — bundled in subscription", 1, 0, 0);

            case "flat":
            {
                // Same rule as the package base: an annual term bills on its
                // anniversary month, not every time the monthly run fires.
                var interval = item["billingInterval"]?.ToString() ?? "monthly";
                if (string.Equals(interval, "annual", StringComparison.OrdinalIgnoreCase)
                    && AsDate(item["effectiveFrom"]) is { } from && from.Month != periodStart.Month)
                    return null;
                return Line(label, 1, flat, flat);
            }

            case "per_seat":
            {
                var seats = await db.ScalarLongAsync(
                    "SELECT COUNT(*) FROM users WHERE company_id=@c AND COALESCE(status,'Active') NOT IN ('Deleted','Disabled')",
                    c => c.Parameters.AddWithValue("@c", companyId), ct);
                var billable = Math.Max(0, seats - (long)included);
                var amount = ApplyBounds(billable * unitPrice, minimum, cap);
                return Line($"{label} — {billable} seat(s)", billable, unitPrice, amount, "seat");
            }

            case "per_unit":
            {
                if (string.IsNullOrWhiteSpace(meterKey))
                    return Line($"{label} — per-unit pricing has no meter configured", 0, unitPrice, 0);
                var used = await MeterValueAsync(companyId, meterKey!, ct);
                var billableQty = used > included ? used - included : 0m;
                var amount = ApplyBounds((long)Math.Round(billableQty * unitPrice, MidpointRounding.AwayFromZero), minimum, cap);
                return Line($"{label} — {used:0.##} used, {included:0.##} included", billableQty, unitPrice, amount, "event");
            }

            case "tiered":
            {
                if (string.IsNullOrWhiteSpace(meterKey))
                    return Line($"{label} — tiered pricing has no meter configured", 0, 0, 0);
                var used = await MeterValueAsync(companyId, meterKey!, ct);
                var (amount, detail) = PriceTiers(item["tiersJson"], used, included);
                return Line($"{label} — {used:0.##} used ({detail})", used, 0, ApplyBounds(amount, minimum, cap), "event");
            }

            case "one_time":
                // Only lands on the period in which it becomes effective, so a
                // setup fee cannot silently recur every month.
                if (AsDate(item["effectiveFrom"]) is { } eff && (eff < periodStart || eff > periodEnd)) return null;
                return Line($"{label} — one-time charge", 1, flat, flat);

            default:
                return Line(label, 1, flat, flat);
        }
    }

    // Graduated tiers: each band prices only the quantity that falls inside it.
    // tiers_json = [{"upTo": 1000, "unitPriceCents": 50}, {"upTo": null, "unitPriceCents": 30}]
    private static (long Amount, string Detail) PriceTiers(object? tiersJson, decimal used, decimal includedFree)
    {
        var billable = used > includedFree ? used - includedFree : 0m;
        if (billable <= 0) return (0, "within included allowance");

        var raw = tiersJson?.ToString();
        if (string.IsNullOrWhiteSpace(raw)) return (0, "no tiers configured");

        List<(decimal? UpTo, long Price)> tiers;
        try
        {
            using var doc = JsonDocument.Parse(raw!);
            tiers = doc.RootElement.EnumerateArray().Select(e => (
                UpTo: e.TryGetProperty("upTo", out var u) && u.ValueKind == JsonValueKind.Number ? u.GetDecimal() : (decimal?)null,
                Price: e.TryGetProperty("unitPriceCents", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt64() : 0L
            )).ToList();
        }
        catch (JsonException) { return (0, "tier definition is not valid JSON"); }

        decimal consumed = 0;
        long amount = 0;
        var parts = new List<string>();
        foreach (var (upTo, price) in tiers)
        {
            if (consumed >= billable) break;
            var bandCeiling = upTo ?? decimal.MaxValue;
            var inBand = Math.Min(billable, bandCeiling) - consumed;
            if (inBand <= 0) continue;
            amount += (long)Math.Round(inBand * price, MidpointRounding.AwayFromZero);
            parts.Add($"{inBand:0.##}@{price}");
            consumed += inBand;
        }
        return (amount, string.Join(" + ", parts));
    }

    private static long ApplyBounds(long amount, long? minimum, long? cap)
    {
        if (minimum is not null && amount < minimum.Value) amount = minimum.Value;
        if (cap is not null && amount > cap.Value) amount = cap.Value;
        return amount;
    }

    private async Task<decimal> MeterValueAsync(long companyId, string meterKey, CancellationToken ct)
    {
        var periodRow = await db.QuerySingleAsync("SELECT period FROM usage_meters WHERE meter_key=@m",
            c => c.Parameters.AddWithValue("@m", meterKey), ct);
        var period = string.Equals(periodRow?["period"]?.ToString(), "lifetime", StringComparison.OrdinalIgnoreCase)
            ? "lifetime" : EntitlementService.CurrentPeriodKey();
        return await db.ScalarDecimalAsync(
            "SELECT COALESCE(value,0) FROM usage_counters WHERE company_id=@c AND meter_key=@m AND period_key=@p",
            c =>
            {
                c.Parameters.AddWithValue("@c", companyId);
                c.Parameters.AddWithValue("@m", meterKey);
                c.Parameters.AddWithValue("@p", period);
            }, ct) ?? 0m;
    }

    // ════════════════════════════ Pricing + tax application ══════════════════

    // Fills gross/net/tax/total on every line. Public so a hand-built manual
    // invoice takes exactly the same arithmetic and rounding path as a generated
    // one — two pricing implementations is how the two disagree.
    public static void Price(List<DraftLine> lines, TaxContext tax)
    {
        var d = tax.Decision;
        foreach (var l in lines)
        {
            if (l.GrossAmountCents == 0 && l.UnitPriceCents != 0)
                l.GrossAmountCents = (long)Math.Round(l.Quantity * l.UnitPriceCents, MidpointRounding.AwayFromZero);

            l.NetAmountCents = Math.Max(0, l.GrossAmountCents - l.DiscountCents);

            l.TaxTreatment = d.Treatment;
            l.TaxCode ??= d.TaxCode;
            l.TaxCategory ??= d.TaxCategory;
            l.TaxRate = d.Treatment == "standard" ? d.Rate : 0m;
            l.ExemptionReason = d.Treatment == "standard" ? null : d.ReasonText;

            // Rounded per line, then summed by the caller. The header never
            // re-derives tax from the subtotal.
            l.TaxAmountCents = l.TaxRate <= 0
                ? 0
                : (long)Math.Round(l.NetAmountCents * l.TaxRate, MidpointRounding.AwayFromZero);

            l.TotalCents = l.NetAmountCents + l.TaxAmountCents;
        }
    }

    // ════════════════════════════ Persistence ════════════════════════════════

    public async Task<long> SaveDraftAsync(
        long companyId, Draft draft, string kind, string documentType, int paymentTermsDays,
        string? notes, string actorEmail, CancellationToken ct = default)
    {
        var provisional = $"DRAFT-{DateTime.UtcNow:yyyyMMddHHmmss}-{companyId}";
        var invoiceId = await db.InsertAsync("""
            INSERT INTO platform_invoices
                (company_id, invoice_number, status, kind, document_type, amount_cents, currency,
                 subtotal_cents, discount_cents, tax_total_cents, total_cents,
                 tax_country, tax_regime, tax_treatment, tax_label, place_of_supply,
                 seller_legal_name, seller_tax_no, buyer_legal_name, buyer_tax_no, buyer_country,
                 period_start, period_end, payment_terms_days, invoicing_scheme, minor_units,
                 notes, line_items, issued_at, due_at)
            VALUES (@cid, @num, 'draft', @kind, @doctype, @total, @cur,
                    @sub, @disc, @taxtotal, @total,
                    @country, @regime, @treatment, @label, @pos,
                    @sellerName, @sellerTax, @buyerName, @buyerTax, @country,
                    @ps, @pe, @terms, @scheme, @minor,
                    @notes, '[]'::jsonb, NULL, NULL)
            """,
            c =>
            {
                c.Parameters.AddWithValue("@cid", companyId);
                c.Parameters.AddWithValue("@num", provisional);
                c.Parameters.AddWithValue("@kind", kind);
                c.Parameters.AddWithValue("@doctype", documentType);
                c.Parameters.AddWithValue("@total", draft.TotalCents);
                c.Parameters.AddWithValue("@cur", draft.Tax.Currency);
                c.Parameters.AddWithValue("@sub", draft.SubtotalCents);
                c.Parameters.AddWithValue("@disc", draft.DiscountCents);
                c.Parameters.AddWithValue("@taxtotal", draft.TaxTotalCents);
                c.Parameters.AddWithValue("@country", (object?)NullIfEmpty(draft.Tax.CountryCode) ?? DBNull.Value);
                c.Parameters.AddWithValue("@regime", draft.Tax.Regime);
                c.Parameters.AddWithValue("@treatment", draft.Tax.Decision.Treatment);
                c.Parameters.AddWithValue("@label", draft.Tax.TaxLabel);
                c.Parameters.AddWithValue("@pos", draft.Tax.PlaceOfSupply);
                c.Parameters.AddWithValue("@sellerName", (object?)draft.Tax.SellerLegalName ?? DBNull.Value);
                c.Parameters.AddWithValue("@sellerTax", (object?)draft.Tax.SellerTaxNo ?? DBNull.Value);
                c.Parameters.AddWithValue("@buyerName", (object?)draft.Tax.BuyerLegalName ?? DBNull.Value);
                c.Parameters.AddWithValue("@buyerTax", (object?)draft.Tax.BuyerTaxNo ?? DBNull.Value);
                c.Parameters.AddWithValue("@ps", draft.PeriodStart.ToDateTime(TimeOnly.MinValue));
                c.Parameters.AddWithValue("@pe", draft.PeriodEnd.ToDateTime(TimeOnly.MinValue));
                c.Parameters.AddWithValue("@terms", paymentTermsDays);
                c.Parameters.AddWithValue("@scheme", (object?)draft.Tax.InvoicingScheme ?? DBNull.Value);
                c.Parameters.AddWithValue("@minor", draft.Tax.MinorUnits);
                c.Parameters.AddWithValue("@notes", (object?)notes ?? DBNull.Value);
            }, ct);

        await ReplaceLinesAsync(invoiceId, draft.Lines, ct);
        return invoiceId;
    }

    public async Task ReplaceLinesAsync(long invoiceId, List<DraftLine> lines, CancellationToken ct = default)
    {
        await db.ExecuteAsync("DELETE FROM platform_invoice_lines WHERE invoice_id=@i",
            c => c.Parameters.AddWithValue("@i", invoiceId), ct);

        var lineNo = 1;
        foreach (var l in lines)
        {
            await db.ExecuteAsync("""
                INSERT INTO platform_invoice_lines
                    (invoice_id, line_no, source, feature_key, meter_key, description, charge_model,
                     quantity, unit, unit_price_cents, gross_amount_cents, discount_cents, net_amount_cents,
                     tax_code, tax_category, tax_treatment, tax_rate, tax_amount_cents, exemption_reason,
                     total_cents, period_start, period_end)
                VALUES (@i,@no,@src,@fk,@mk,@desc,@model,
                        @qty,@unit,@price,@gross,@disc,@net,
                        @tcode,@tcat,@ttreat,@trate,@tamt,@reason,
                        @total,@ps,@pe)
                """,
                c =>
                {
                    c.Parameters.AddWithValue("@i", invoiceId);
                    c.Parameters.AddWithValue("@no", lineNo);
                    c.Parameters.AddWithValue("@src", l.Source);
                    c.Parameters.AddWithValue("@fk", (object?)l.FeatureKey ?? DBNull.Value);
                    c.Parameters.AddWithValue("@mk", (object?)l.MeterKey ?? DBNull.Value);
                    c.Parameters.AddWithValue("@desc", Truncate(l.Description, 400) ?? "");
                    c.Parameters.AddWithValue("@model", l.ChargeModel);
                    c.Parameters.AddWithValue("@qty", l.Quantity);
                    c.Parameters.AddWithValue("@unit", (object?)l.Unit ?? DBNull.Value);
                    c.Parameters.AddWithValue("@price", l.UnitPriceCents);
                    c.Parameters.AddWithValue("@gross", l.GrossAmountCents);
                    c.Parameters.AddWithValue("@disc", l.DiscountCents);
                    c.Parameters.AddWithValue("@net", l.NetAmountCents);
                    c.Parameters.AddWithValue("@tcode", (object?)l.TaxCode ?? DBNull.Value);
                    c.Parameters.AddWithValue("@tcat", (object?)l.TaxCategory ?? DBNull.Value);
                    c.Parameters.AddWithValue("@ttreat", l.TaxTreatment);
                    c.Parameters.AddWithValue("@trate", l.TaxRate);
                    c.Parameters.AddWithValue("@tamt", l.TaxAmountCents);
                    c.Parameters.AddWithValue("@reason", (object?)Truncate(l.ExemptionReason, 240) ?? DBNull.Value);
                    c.Parameters.AddWithValue("@total", l.TotalCents);
                    c.Parameters.AddWithValue("@ps", (object?)l.PeriodStart?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value);
                    c.Parameters.AddWithValue("@pe", (object?)l.PeriodEnd?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value);
                }, ct);
            lineNo++;
        }

        // Header totals are always re-derived from the persisted lines, so a header
        // can never drift from its own detail.
        await db.ExecuteAsync("""
            UPDATE platform_invoices i SET
                subtotal_cents  = COALESCE(t.net, 0),
                discount_cents  = COALESCE(t.disc, 0),
                tax_total_cents = COALESCE(t.tax, 0),
                total_cents     = COALESCE(t.net, 0) + COALESCE(t.tax, 0),
                amount_cents    = COALESCE(t.net, 0) + COALESCE(t.tax, 0),
                updated_at      = NOW()
            FROM (SELECT SUM(net_amount_cents) net, SUM(discount_cents) disc, SUM(tax_amount_cents) tax
                  FROM platform_invoice_lines WHERE invoice_id=@i) t
            WHERE i.id=@i
            """, c => c.Parameters.AddWithValue("@i", invoiceId), ct);
    }

    // Allocates the document number at issue time and stamps the issue/due dates.
    // Numbering is per (scope, year) and gap-free: nothing consumes a number until
    // the document is actually issued, so a cancelled draft leaves no hole in the
    // sequence an auditor would have to explain.
    public async Task<string> IssueAsync(long invoiceId, string actorEmail, CancellationToken ct = default)
    {
        var header = await db.QuerySingleAsync(
            "SELECT status, document_type, tax_country, payment_terms_days FROM platform_invoices WHERE id=@i",
            c => c.Parameters.AddWithValue("@i", invoiceId), ct)
            ?? throw new InvalidOperationException("Invoice not found");

        if (!string.Equals(header["status"]?.ToString(), "draft", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only a draft can be issued. An issued document is immutable — raise a credit note instead.");

        var country = header["taxCountry"]?.ToString();
        var docType = header["documentType"]?.ToString() ?? "invoice";
        var prefix = docType == "credit_note" ? "CN" : "INV";
        var scope = $"{prefix}-{(string.IsNullOrWhiteSpace(country) ? "XX" : country)}";
        var year = DateTime.UtcNow.Year;

        var seq = await db.ScalarLongAsync("""
            INSERT INTO platform_invoice_sequences (scope, period_year, next_value, updated_at)
            VALUES (@s, @y, 2, NOW())
            ON CONFLICT (scope, period_year) DO UPDATE
                SET next_value = platform_invoice_sequences.next_value + 1, updated_at = NOW()
            RETURNING next_value - 1
            """,
            c => { c.Parameters.AddWithValue("@s", scope); c.Parameters.AddWithValue("@y", year); }, ct);

        var number = $"{scope}-{year}-{seq:D5}";
        var terms = (int)AsLong(header["paymentTermsDays"]);
        if (terms <= 0) terms = 15;

        await db.ExecuteAsync("""
            UPDATE platform_invoices
               SET invoice_number=@num, status='sent', issued_at=NOW(),
                   due_at = NOW() + (@terms || ' day')::interval,
                   issued_by=@by, updated_at=NOW()
             WHERE id=@i
            """,
            c =>
            {
                c.Parameters.AddWithValue("@num", number);
                c.Parameters.AddWithValue("@terms", terms.ToString());
                c.Parameters.AddWithValue("@by", actorEmail);
                c.Parameters.AddWithValue("@i", invoiceId);
            }, ct);

        return number;
    }

    // A credit note is a NEW document that mirrors the original with negated
    // amounts. The original is never rewritten — that is the whole point.
    public async Task<long> CreateCreditNoteAsync(long invoiceId, string reason, string actorEmail, CancellationToken ct = default)
    {
        var src = await db.QuerySingleAsync("SELECT * FROM platform_invoices WHERE id=@i",
            c => c.Parameters.AddWithValue("@i", invoiceId), ct)
            ?? throw new InvalidOperationException("Invoice not found");

        if (string.Equals(src["documentType"]?.ToString(), "credit_note", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A credit note cannot itself be credited.");

        // Credit is capped at the original. Without this an invoice can be credited
        // repeatedly, driving the account negative by an amount no reconciliation
        // can explain and over-reclaiming output tax on every pass.
        var originalTotal = AsLong(src["totalCents"]);
        var alreadyCredited = await db.ScalarLongAsync(
            "SELECT COALESCE(SUM(ABS(total_cents)),0) FROM platform_invoices WHERE credit_note_of=@i AND status <> 'void'",
            c => c.Parameters.AddWithValue("@i", invoiceId), ct);
        if (alreadyCredited >= originalTotal)
            throw new InvalidOperationException(
                $"This document is already fully credited ({alreadyCredited} of {originalTotal}). Raise a new invoice instead.");

        var newId = await db.InsertAsync("""
            INSERT INTO platform_invoices
                (company_id, invoice_number, status, kind, document_type, amount_cents, currency,
                 subtotal_cents, discount_cents, tax_total_cents, total_cents,
                 tax_country, tax_regime, tax_treatment, tax_label, place_of_supply,
                 seller_legal_name, seller_tax_no, buyer_legal_name, buyer_tax_no, buyer_country,
                 period_start, period_end, payment_terms_days, invoicing_scheme, minor_units,
                 notes, line_items, credit_note_of)
            SELECT company_id, @num, 'draft', kind, 'credit_note', -amount_cents, currency,
                   -subtotal_cents, -discount_cents, -tax_total_cents, -total_cents,
                   tax_country, tax_regime, tax_treatment, tax_label, place_of_supply,
                   seller_legal_name, seller_tax_no, buyer_legal_name, buyer_tax_no, buyer_country,
                   period_start, period_end, payment_terms_days, invoicing_scheme, minor_units,
                   @notes, '[]'::jsonb, id
            FROM platform_invoices WHERE id=@i
            """,
            c =>
            {
                c.Parameters.AddWithValue("@num", $"DRAFT-CN-{DateTime.UtcNow:yyyyMMddHHmmss}-{invoiceId}");
                c.Parameters.AddWithValue("@notes", Truncate($"Credit note for {src["invoiceNumber"]} — {reason}", 400)!);
                c.Parameters.AddWithValue("@i", invoiceId);
            }, ct);

        await db.ExecuteAsync("""
            INSERT INTO platform_invoice_lines
                (invoice_id, line_no, source, feature_key, meter_key, description, charge_model,
                 quantity, unit, unit_price_cents, gross_amount_cents, discount_cents, net_amount_cents,
                 tax_code, tax_category, tax_treatment, tax_rate, tax_amount_cents, exemption_reason,
                 total_cents, period_start, period_end)
            SELECT @new, line_no, source, feature_key, meter_key, 'Credit — ' || description, charge_model,
                   quantity, unit, unit_price_cents, -gross_amount_cents, -discount_cents, -net_amount_cents,
                   tax_code, tax_category, tax_treatment, tax_rate, -tax_amount_cents, exemption_reason,
                   -total_cents, period_start, period_end
            FROM platform_invoice_lines WHERE invoice_id=@i ORDER BY line_no
            """,
            c => { c.Parameters.AddWithValue("@new", newId); c.Parameters.AddWithValue("@i", invoiceId); }, ct);

        return newId;
    }

    public async Task<object?> GetAsync(long invoiceId, CancellationToken ct = default)
    {
        var header = await db.QuerySingleAsync("""
            SELECT i.*, c.name tenant, c.country tenant_country
            FROM platform_invoices i JOIN companies c ON c.id=i.company_id
            WHERE i.id=@i
            """, c => c.Parameters.AddWithValue("@i", invoiceId), ct);
        if (header is null) return null;

        var lines = await db.QueryAsync(
            "SELECT * FROM platform_invoice_lines WHERE invoice_id=@i ORDER BY line_no",
            c => c.Parameters.AddWithValue("@i", invoiceId), ct);

        // Tax breakdown by rate — the summary block every VAT/GST invoice must
        // print, and what a filing is actually assembled from.
        var breakdown = lines
            .GroupBy(l => new
            {
                Code = l["taxCode"]?.ToString() ?? "",
                Category = l["taxCategory"]?.ToString() ?? "",
                Treatment = l["taxTreatment"]?.ToString() ?? "standard",
                Rate = AsDecimal(l["taxRate"]),
            })
            .Select(g => new
            {
                taxCode = g.Key.Code,
                taxCategory = g.Key.Category,
                treatment = g.Key.Treatment,
                rate = g.Key.Rate,
                taxableCents = g.Sum(l => AsLong(l["netAmountCents"])),
                taxCents = g.Sum(l => AsLong(l["taxAmountCents"])),
                lineCount = g.Count(),
            })
            .OrderByDescending(g => g.rate)
            .ToList();

        return new { invoice = header, lines, taxBreakdown = breakdown };
    }

    // ════════════════════════════ Helpers ════════════════════════════════════

    private static long AsLong(object? v) => v is null or DBNull ? 0 : Convert.ToInt64(v);
    private static decimal AsDecimal(object? v) => v is null or DBNull ? 0m : Convert.ToDecimal(v);
    private static DateOnly? AsDate(object? v) => v is null or DBNull ? null : DateOnly.FromDateTime(Convert.ToDateTime(v));
    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
    private static string? FirstNonEmpty(params string?[] candidates) =>
        candidates.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
    private static string? Truncate(string? s, int max) =>
        s is null ? null : s.Length <= max ? s : s[..max];
}
