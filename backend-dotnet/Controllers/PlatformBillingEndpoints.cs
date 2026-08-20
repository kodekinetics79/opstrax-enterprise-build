using System.Text.Json;
using Opstrax.Api.Data;
using Opstrax.Api.DTOs;
using Opstrax.Api.Services;

namespace Opstrax.Api.Controllers;

// ─────────────────────────────────────────────────────────────────────────────
// PLATFORM BILLING — itemized invoicing, per-feature commercial terms, indirect tax
//
// Three surfaces, all behind the existing platform session guard:
//
//   /api/platform/invoices/*        the document lifecycle
//                                   preview → draft → issue → paid | void | credit
//   /api/platform/tenants/{id}/billing-plan
//                                   per-feature terms: free / included / flat /
//                                   per_seat / per_unit / tiered / one_time
//   /api/platform/tax/*             seller registrations + the determination table
//
// The lifecycle is deliberately narrow. There is no "edit an issued invoice"
// route, because there is no lawful way to do that: a document that has left the
// building is corrected by a credit note, not by a rewrite.
// ─────────────────────────────────────────────────────────────────────────────
public static class PlatformBillingEndpoints
{
    public static void MapPlatformBillingEndpoints(this WebApplication app)
    {
        // ── Document lifecycle ───────────────────────────────────────────────
        app.MapGet("/api/platform/invoices/{id:long}", InvoiceGet);
        app.MapPost("/api/platform/invoices/preview", InvoicePreview);
        app.MapPost("/api/platform/invoices/generate", InvoiceGenerate);
        app.MapPut("/api/platform/invoices/{id:long}/lines", InvoiceReplaceLines);
        app.MapPost("/api/platform/invoices/{id:long}/issue", InvoiceIssue);
        app.MapPost("/api/platform/invoices/{id:long}/void", InvoiceVoid);
        app.MapPost("/api/platform/invoices/{id:long}/credit-note", InvoiceCreditNote);

        // ── Per-tenant commercial terms ──────────────────────────────────────
        app.MapGet("/api/platform/tenants/{id:long}/billing-plan", BillingPlanGet);
        app.MapPut("/api/platform/tenants/{id:long}/billing-plan", BillingPlanUpsert);
        app.MapDelete("/api/platform/tenants/{id:long}/billing-plan/{featureKey}", BillingPlanDelete);
        app.MapGet("/api/platform/tenants/{id:long}/tax-context", TenantTaxContext);

        // ── Tax configuration ────────────────────────────────────────────────
        app.MapGet("/api/platform/tax/registrations", TaxRegistrationsList);
        app.MapPut("/api/platform/tax/registrations/{country}", TaxRegistrationUpsert);
        app.MapGet("/api/platform/tax/rules", TaxRulesList);
        app.MapPut("/api/platform/tax/rules", TaxRuleUpsert);
        app.MapDelete("/api/platform/tax/rules/{ruleKey}", TaxRuleDelete);

        // ── Billing readiness + batch run ────────────────────────────────────
        app.MapGet("/api/platform/billing/readiness", BillingReadiness);
        app.MapPost("/api/platform/invoices/generate-batch", InvoiceGenerateBatch);

        // ── Revenue control (Revenue & Usage cockpit) ────────────────────────
        app.MapGet("/api/platform/revenue/summary", RevenueSummary);
        app.MapDelete("/api/platform/opstrax/tenants/{tenantId:long}/overrides/{meterKey}", OverrideDelete);
    }

    // ════════════════════════════ Documents ══════════════════════════════════

    private static async Task<IResult> InvoiceGet(long id, HttpContext http, Database db, PlatformBillingService billing, CancellationToken ct)
    {
        var (_, error) = await PlatformEndpoints.RequireAsync(http, db, "platform:billing:view", ct);
        if (error is not null) return error;
        var doc = await billing.GetAsync(id, ct);
        return doc is null
            ? Results.Json(ApiResponse<object>.Fail("Not found"), statusCode: StatusCodes.Status404NotFound)
            : Results.Ok(ApiResponse<object>.Ok(doc));
    }

    // Computes what a period would bill without writing anything. Safe to call as
    // often as an operator likes, which is what makes it usable as a live preview.
    private static async Task<IResult> InvoicePreview(HttpContext http, Dictionary<string, object?> body, Database db, PlatformBillingService billing, CancellationToken ct)
    {
        var (_, error) = await PlatformEndpoints.RequireAsync(http, db, "platform:billing:view", ct);
        if (error is not null) return error;

        var companyId = Long(body, "companyId");
        if (companyId is null) return Bad("companyId is required");

        var (start, end) = ResolvePeriod(body);
        var draft = await billing.BuildDraftAsync(companyId.Value, start, end, ct);
        return Results.Ok(ApiResponse<object>.Ok(Project(draft)));
    }

    internal static async Task<IResult> InvoiceGenerate(HttpContext http, Dictionary<string, object?> body, Database db, PlatformBillingService billing, CancellationToken ct)
    {
        var (principal, error) = await PlatformEndpoints.RequireAsync(http, db, "platform:billing:manage", ct);
        if (error is not null) return error;

        var companyId = Long(body, "companyId");
        if (companyId is null) return Bad("companyId is required");

        var (start, end) = ResolvePeriod(body);

        // Same guard the batch run has always had. Without it, pressing Generate
        // twice raises two live, numbered, tax-bearing documents for one supply —
        // double-billed revenue and double-declared output tax.
        if (!(Bool(body, "force") ?? false))
        {
            var existing = await db.QuerySingleAsync("""
                SELECT id, invoice_number, status FROM platform_invoices
                WHERE company_id=@c AND document_type='invoice' AND status <> 'void'
                  AND TO_CHAR(COALESCE(period_start, created_at), 'YYYY-MM') = @p
                ORDER BY id DESC LIMIT 1
                """,
                c => { c.Parameters.AddWithValue("@c", companyId.Value); c.Parameters.AddWithValue("@p", start.ToString("yyyy-MM")); }, ct);
            if (existing is not null)
                return Results.Json(ApiResponse<object>.Fail("Already invoiced",
                    $"{existing["invoiceNumber"]} already covers {start:yyyy-MM} for this tenant. Void it first, or resend with force=true."),
                    statusCode: StatusCodes.Status409Conflict);
        }

        var draft = await billing.BuildDraftAsync(companyId.Value, start, end, ct);
        if (draft.Lines.Count == 0)
            return Bad("Nothing billable for this period — the invoice was not created.");

        var terms = (int)(Long(body, "paymentTermsDays") ?? 15);
        var invoiceId = await billing.SaveDraftAsync(companyId.Value, draft,
            Str(body, "kind") ?? "recurring", "invoice", terms, Str(body, "notes"), principal!.Email, ct);

        string? number = null;
        var issue = Bool(body, "issue") ?? false;
        if (issue) number = await billing.IssueAsync(invoiceId, principal!.Email, ct);

        await PlatformEndpoints.AuditAsync(db, principal!, http,
            issue ? "invoice.generated.issued" : "invoice.generated.draft", "Invoice", invoiceId, companyId,
            new
            {
                period = $"{start:yyyy-MM-dd}..{end:yyyy-MM-dd}",
                forcedDuplicate = Bool(body, "force") ?? false,
                lines = draft.Lines.Count,
                subtotal = draft.SubtotalCents,
                tax = draft.TaxTotalCents,
                total = draft.TotalCents,
                treatment = draft.Tax.Decision.Treatment,
                rule = draft.Tax.Decision.RuleKey,
                number,
            }, ct);

        return Results.Ok(ApiResponse<object>.Ok(
            new { id = invoiceId, invoiceNumber = number, status = issue ? "sent" : "draft", draft = Project(draft) },
            issue ? "Invoice generated and issued" : "Draft invoice generated"));
    }

    // Manual itemization for a draft. Lines arrive net of tax; tax is applied here
    // from the tenant's own determination so a hand-typed line cannot carry a rate
    // an operator invented.
    private static async Task<IResult> InvoiceReplaceLines(long id, HttpContext http, Dictionary<string, object?> body, Database db, PlatformBillingService billing, CancellationToken ct)
    {
        var (principal, error) = await PlatformEndpoints.RequireAsync(http, db, "platform:billing:manage", ct);
        if (error is not null) return error;

        var header = await db.QuerySingleAsync("SELECT company_id, status FROM platform_invoices WHERE id=@i",
            c => c.Parameters.AddWithValue("@i", id), ct);
        if (header is null) return Results.Json(ApiResponse<object>.Fail("Not found"), statusCode: StatusCodes.Status404NotFound);
        if (!string.Equals(header["status"]?.ToString(), "draft", StringComparison.OrdinalIgnoreCase))
            return Bad("Only a draft can be edited. Issue is final — raise a credit note to correct an issued document.");

        var companyId = Convert.ToInt64(header["companyId"]);
        var lines = ReadLines(body);
        if (lines.Count == 0) return Bad("At least one line is required");

        var tax = await billing.ResolveTaxAsync(companyId, ct);
        PlatformBillingService.Price(lines, tax);
        await billing.ReplaceLinesAsync(id, lines, ct);

        await PlatformEndpoints.AuditAsync(db, principal!, http, "invoice.lines.replaced", "Invoice", id, companyId,
            new { lines = lines.Count, total = lines.Sum(l => l.TotalCents) }, ct);

        // Same contract as InvoiceGet: the document is the response, and a vanished invoice
        // is a 404 rather than a success envelope wrapping null.
        var updated = await billing.GetAsync(id, ct);
        return updated is null
            ? Results.Json(ApiResponse<object>.Fail("Not found"), statusCode: StatusCodes.Status404NotFound)
            : Results.Ok(ApiResponse<object>.Ok(updated, "Invoice lines updated"));
    }

    private static async Task<IResult> InvoiceIssue(long id, HttpContext http, Database db, PlatformBillingService billing, CancellationToken ct)
    {
        var (principal, error) = await PlatformEndpoints.RequireAsync(http, db, "platform:billing:manage", ct);
        if (error is not null) return error;

        var companyId = await db.ScalarLongAsync("SELECT company_id FROM platform_invoices WHERE id=@i",
            c => c.Parameters.AddWithValue("@i", id), ct);
        try
        {
            var number = await billing.IssueAsync(id, principal!.Email, ct);
            await PlatformEndpoints.AuditAsync(db, principal!, http, "invoice.issued", "Invoice", id, companyId, new { number }, ct);
            return Results.Ok(ApiResponse<object>.Ok(new { id, invoiceNumber = number, status = "sent" }, $"Issued as {number}"));
        }
        catch (InvalidOperationException ex) { return Bad(ex.Message); }
    }

    private static async Task<IResult> InvoiceVoid(long id, HttpContext http, Dictionary<string, object?> body, Database db, CancellationToken ct)
    {
        var (principal, error) = await PlatformEndpoints.RequireAsync(http, db, "platform:billing:manage", ct);
        if (error is not null) return error;

        var reason = Str(body, "reason");
        if (string.IsNullOrWhiteSpace(reason)) return Bad("A void reason is required and is recorded in the audit trail");

        var header = await db.QuerySingleAsync("SELECT company_id, status FROM platform_invoices WHERE id=@i",
            c => c.Parameters.AddWithValue("@i", id), ct);
        if (header is null) return Results.Json(ApiResponse<object>.Fail("Not found"), statusCode: StatusCodes.Status404NotFound);
        if (string.Equals(header["status"]?.ToString(), "paid", StringComparison.OrdinalIgnoreCase))
            return Bad("A paid invoice cannot be voided — raise a credit note so the reversal is on the record.");

        await db.ExecuteAsync(
            "UPDATE platform_invoices SET status='void', voided_at=NOW(), void_reason=@r, updated_at=NOW() WHERE id=@i",
            c => { c.Parameters.AddWithValue("@r", reason!); c.Parameters.AddWithValue("@i", id); }, ct);

        await PlatformEndpoints.AuditAsync(db, principal!, http, "invoice.voided", "Invoice", id,
            Convert.ToInt64(header["companyId"]), new { reason }, ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id, status = "void" }, "Invoice voided"));
    }

    private static async Task<IResult> InvoiceCreditNote(long id, HttpContext http, Dictionary<string, object?> body, Database db, PlatformBillingService billing, CancellationToken ct)
    {
        var (principal, error) = await PlatformEndpoints.RequireAsync(http, db, "platform:billing:manage", ct);
        if (error is not null) return error;

        var reason = Str(body, "reason");
        if (string.IsNullOrWhiteSpace(reason)) return Bad("A credit-note reason is required");

        var companyId = await db.ScalarLongAsync("SELECT company_id FROM platform_invoices WHERE id=@i",
            c => c.Parameters.AddWithValue("@i", id), ct);
        try
        {
            var newId = await billing.CreateCreditNoteAsync(id, reason!, principal!.Email, ct);
            string? number = null;
            if (Bool(body, "issue") ?? true) number = await billing.IssueAsync(newId, principal!.Email, ct);

            await PlatformEndpoints.AuditAsync(db, principal!, http, "invoice.credit_note", "Invoice", newId, companyId,
                new { creditNoteOf = id, reason, number }, ct);
            return Results.Ok(ApiResponse<object>.Ok(new { id = newId, invoiceNumber = number, creditNoteOf = id },
                number is null ? "Credit note drafted" : $"Credit note {number} issued"));
        }
        catch (InvalidOperationException ex) { return Bad(ex.Message); }
    }

    // ════════════════════════════ Billing plan ═══════════════════════════════

    // Returns the tenant's terms alongside the catalog of everything that COULD be
    // priced, so the editor never has to guess a feature key. A feature absent from
    // the plan simply follows its package default.
    private static async Task<IResult> BillingPlanGet(long id, HttpContext http, Database db, PlatformBillingService billing, CancellationToken ct)
    {
        var (_, error) = await PlatformEndpoints.RequireAsync(http, db, "platform:billing:view", ct);
        if (error is not null) return error;

        var items = await db.QueryAsync(
            "SELECT * FROM tenant_billing_plan_items WHERE company_id=@c ORDER BY feature_key",
            c => c.Parameters.AddWithValue("@c", id), ct);

        var modules = await db.QueryAsync("""
            SELECT DISTINCT module_key AS key, module_key AS label, 'module' AS kind
            FROM tenant_entitlements WHERE company_id=@c
            UNION
            SELECT jsonb_array_elements_text(module_keys), jsonb_array_elements_text(module_keys), 'module'
            FROM module_packages WHERE active
            ORDER BY 1
            """, c => c.Parameters.AddWithValue("@c", id), ct);

        var meters = await db.QueryAsync(
            "SELECT meter_key AS key, name AS label, unit, period, 'meter' AS kind FROM usage_meters WHERE active ORDER BY meter_key", ct: ct);

        var packs = await db.QueryAsync(
            "SELECT code AS key, name AS label, region, base_price_cents, 'market_pack' AS kind FROM market_packs ORDER BY name", ct: ct);

        var tax = await billing.ResolveTaxAsync(id, ct);

        return Results.Ok(ApiResponse<object>.Ok(new
        {
            items,
            catalog = new { modules, meters, marketPacks = packs },
            chargeModels = new[] { "free", "included", "flat", "per_seat", "per_unit", "tiered", "one_time" },
            currency = tax.Currency,
            taxContext = ProjectTax(tax),
        }));
    }

    private static async Task<IResult> BillingPlanUpsert(long id, HttpContext http, Dictionary<string, object?> body, Database db, CancellationToken ct)
    {
        var (principal, error) = await PlatformEndpoints.RequireAsync(http, db, "platform:billing:manage", ct);
        if (error is not null) return error;

        var featureKey = Str(body, "featureKey");
        if (string.IsNullOrWhiteSpace(featureKey)) return Bad("featureKey is required");

        var model = (Str(body, "chargeModel") ?? "included").ToLowerInvariant();
        string[] allowed = ["free", "included", "flat", "per_seat", "per_unit", "tiered", "one_time"];
        if (!allowed.Contains(model))
            return Bad($"chargeModel must be one of: {string.Join(", ", allowed)}");

        var meterKey = Str(body, "meterKey");
        // Consumption pricing without a meter would silently bill zero forever.
        if (model is "per_unit" or "tiered" && string.IsNullOrWhiteSpace(meterKey))
            return Bad($"chargeModel '{model}' bills against usage, so meterKey is required");

        if (!string.IsNullOrWhiteSpace(meterKey))
        {
            var known = await db.ScalarLongAsync("SELECT COUNT(*) FROM usage_meters WHERE meter_key=@m AND active",
                c => c.Parameters.AddWithValue("@m", meterKey!), ct);
            if (known == 0) return Bad($"Unknown or inactive meter: {meterKey}");
        }

        string? tiers = null;
        if (body.TryGetValue("tiers", out var t) && t is not null)
        {
            tiers = t is JsonElement je ? je.GetRawText() : JsonSerializer.Serialize(t);
            if (model == "tiered")
            {
                try
                {
                    using var doc = JsonDocument.Parse(tiers);
                    if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                        return Bad("tiers must be a non-empty array of { upTo, unitPriceCents }");
                }
                catch (JsonException) { return Bad("tiers is not valid JSON"); }
            }
        }
        if (model == "tiered" && tiers is null) return Bad("chargeModel 'tiered' requires a tiers definition");

        await db.ExecuteAsync("""
            INSERT INTO tenant_billing_plan_items
                (company_id, feature_key, feature_label, charge_model, meter_key, unit_price_cents,
                 included_quantity, flat_price_cents, minimum_cents, cap_cents, tiers_json,
                 currency, billing_interval, tax_code, effective_from, effective_to, active, note,
                 updated_by, updated_at)
            VALUES (@c,@fk,@label,@model,@mk,@unit,@incl,@flat,@min,@cap,CAST(@tiers AS JSONB),
                    @cur,@interval,@taxcode,COALESCE(@from, CURRENT_DATE),@to,@active,@note,@by,NOW())
            ON CONFLICT (company_id, feature_key) DO UPDATE SET
                feature_label     = EXCLUDED.feature_label,
                charge_model      = EXCLUDED.charge_model,
                meter_key         = EXCLUDED.meter_key,
                unit_price_cents  = EXCLUDED.unit_price_cents,
                included_quantity = EXCLUDED.included_quantity,
                flat_price_cents  = EXCLUDED.flat_price_cents,
                minimum_cents     = EXCLUDED.minimum_cents,
                cap_cents         = EXCLUDED.cap_cents,
                tiers_json        = EXCLUDED.tiers_json,
                currency          = EXCLUDED.currency,
                billing_interval  = EXCLUDED.billing_interval,
                tax_code          = EXCLUDED.tax_code,
                effective_from    = EXCLUDED.effective_from,
                effective_to      = EXCLUDED.effective_to,
                active            = EXCLUDED.active,
                note              = EXCLUDED.note,
                updated_by        = EXCLUDED.updated_by,
                updated_at        = NOW()
            """,
            c =>
            {
                c.Parameters.AddWithValue("@c", id);
                c.Parameters.AddWithValue("@fk", featureKey!);
                c.Parameters.AddWithValue("@label", (object?)Str(body, "featureLabel") ?? DBNull.Value);
                c.Parameters.AddWithValue("@model", model);
                c.Parameters.AddWithValue("@mk", (object?)meterKey ?? DBNull.Value);
                c.Parameters.AddWithValue("@unit", Long(body, "unitPriceCents") ?? 0);
                c.Parameters.AddWithValue("@incl", Decimal(body, "includedQuantity") ?? 0m);
                c.Parameters.AddWithValue("@flat", Long(body, "flatPriceCents") ?? 0);
                c.Parameters.AddWithValue("@min", (object?)Long(body, "minimumCents") ?? DBNull.Value);
                c.Parameters.AddWithValue("@cap", (object?)Long(body, "capCents") ?? DBNull.Value);
                c.Parameters.AddWithValue("@tiers", (object?)tiers ?? DBNull.Value);
                c.Parameters.AddWithValue("@cur", (object?)Str(body, "currency") ?? DBNull.Value);
                c.Parameters.AddWithValue("@interval", Str(body, "billingInterval") ?? "monthly");
                c.Parameters.AddWithValue("@taxcode", (object?)Str(body, "taxCode") ?? DBNull.Value);
                c.Parameters.AddWithValue("@from", (object?)Str(body, "effectiveFrom") ?? DBNull.Value);
                c.Parameters.AddWithValue("@to", (object?)Str(body, "effectiveTo") ?? DBNull.Value);
                c.Parameters.AddWithValue("@active", Bool(body, "active") ?? true);
                c.Parameters.AddWithValue("@note", (object?)Str(body, "note") ?? DBNull.Value);
                c.Parameters.AddWithValue("@by", principal!.Email);
            }, ct);

        await PlatformEndpoints.AuditAsync(db, principal!, http, "billing.plan_item.set", "BillingPlanItem", null, id,
            new { featureKey, chargeModel = model, meterKey }, ct);
        return Results.Ok(ApiResponse<object>.Ok(new { featureKey, chargeModel = model }, "Billing term saved"));
    }

    private static async Task<IResult> BillingPlanDelete(long id, string featureKey, HttpContext http, Database db, CancellationToken ct)
    {
        var (principal, error) = await PlatformEndpoints.RequireAsync(http, db, "platform:billing:manage", ct);
        if (error is not null) return error;

        var removed = await db.ExecuteAsync(
            "DELETE FROM tenant_billing_plan_items WHERE company_id=@c AND feature_key=@f",
            c => { c.Parameters.AddWithValue("@c", id); c.Parameters.AddWithValue("@f", featureKey); }, ct);
        if (removed == 0) return Results.Json(ApiResponse<object>.Fail("Not found"), statusCode: StatusCodes.Status404NotFound);

        await PlatformEndpoints.AuditAsync(db, principal!, http, "billing.plan_item.removed", "BillingPlanItem", null, id,
            new { featureKey }, ct);
        return Results.Ok(ApiResponse<object>.Ok(new { featureKey }, "Billing term removed — the feature falls back to its package default"));
    }

    private static async Task<IResult> TenantTaxContext(long id, HttpContext http, Database db, PlatformBillingService billing, CancellationToken ct)
    {
        var (_, error) = await PlatformEndpoints.RequireAsync(http, db, "platform:billing:view", ct);
        if (error is not null) return error;
        return Results.Ok(ApiResponse<object>.Ok(ProjectTax(await billing.ResolveTaxAsync(id, ct))));
    }

    // ════════════════════════════ Tax configuration ══════════════════════════

    private static async Task<IResult> TaxRegistrationsList(HttpContext http, Database db, CancellationToken ct)
    {
        var (_, error) = await PlatformEndpoints.RequireAsync(http, db, "platform:billing:view", ct);
        if (error is not null) return error;
        var rows = await db.QueryAsync("""
            SELECT r.*, cp.country_name, cp.tax_id_label, cp.default_currency,
                   (SELECT COUNT(*) FROM companies c WHERE c.country = r.country_code) AS tenant_count
            FROM platform_tax_registrations r
            LEFT JOIN country_profiles cp ON cp.country_code = r.country_code
            ORDER BY (SELECT COUNT(*) FROM companies c WHERE c.country = r.country_code) DESC, r.country_code
            """, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(rows));
    }

    private static async Task<IResult> TaxRegistrationUpsert(string country, HttpContext http, Dictionary<string, object?> body, Database db, CancellationToken ct)
    {
        var (principal, error) = await PlatformEndpoints.RequireAsync(http, db, "platform:billing:manage", ct);
        if (error is not null) return error;

        var code = country.ToUpperInvariant();
        var known = await db.ScalarLongAsync("SELECT COUNT(*) FROM country_profiles WHERE country_code=@c",
            c => c.Parameters.AddWithValue("@c", code), ct);
        if (known == 0) return Bad($"Unknown country code: {code}");

        var registered = Bool(body, "registered") ?? false;
        var taxNo = Str(body, "taxRegistrationNo");
        // Claiming a registration without its number would put an empty VAT box on
        // every invoice raised for that country.
        if (registered && string.IsNullOrWhiteSpace(taxNo))
            return Bad("A registration number is required to mark this country as registered");

        await db.ExecuteAsync("""
            INSERT INTO platform_tax_registrations
                (country_code, regime, legal_name, tax_registration_no, standard_rate, tax_label,
                 invoicing_scheme, registered, effective_from, effective_to, note, updated_by, updated_at)
            VALUES (@c,@regime,@legal,@no,@rate,@label,@scheme,@reg,COALESCE(@from,CURRENT_DATE),@to,@note,@by,NOW())
            ON CONFLICT (country_code) DO UPDATE SET
                regime              = COALESCE(EXCLUDED.regime, platform_tax_registrations.regime),
                legal_name          = EXCLUDED.legal_name,
                tax_registration_no = EXCLUDED.tax_registration_no,
                standard_rate       = COALESCE(EXCLUDED.standard_rate, platform_tax_registrations.standard_rate),
                tax_label           = COALESCE(EXCLUDED.tax_label, platform_tax_registrations.tax_label),
                invoicing_scheme    = COALESCE(EXCLUDED.invoicing_scheme, platform_tax_registrations.invoicing_scheme),
                registered          = EXCLUDED.registered,
                effective_from      = EXCLUDED.effective_from,
                effective_to        = EXCLUDED.effective_to,
                note                = EXCLUDED.note,
                updated_by          = EXCLUDED.updated_by,
                updated_at          = NOW()
            """,
            c =>
            {
                c.Parameters.AddWithValue("@c", code);
                c.Parameters.AddWithValue("@regime", Str(body, "regime") ?? "vat");
                c.Parameters.AddWithValue("@legal", (object?)Str(body, "legalName") ?? DBNull.Value);
                c.Parameters.AddWithValue("@no", (object?)taxNo ?? DBNull.Value);
                c.Parameters.AddWithValue("@rate", (object?)Decimal(body, "standardRate") ?? DBNull.Value);
                c.Parameters.AddWithValue("@label", (object?)Str(body, "taxLabel") ?? DBNull.Value);
                c.Parameters.AddWithValue("@scheme", (object?)Str(body, "invoicingScheme") ?? DBNull.Value);
                c.Parameters.AddWithValue("@reg", registered);
                c.Parameters.AddWithValue("@from", (object?)Str(body, "effectiveFrom") ?? DBNull.Value);
                c.Parameters.AddWithValue("@to", (object?)Str(body, "effectiveTo") ?? DBNull.Value);
                c.Parameters.AddWithValue("@note", (object?)Str(body, "note") ?? DBNull.Value);
                c.Parameters.AddWithValue("@by", principal!.Email);
            }, ct);

        await PlatformEndpoints.AuditAsync(db, principal!, http, "tax.registration.set", "TaxRegistration", null, null,
            new { country = code, registered, taxNo }, ct);
        return Results.Ok(ApiResponse<object>.Ok(new { country = code, registered }, "Tax registration saved"));
    }

    private static async Task<IResult> TaxRulesList(HttpContext http, Database db, CancellationToken ct)
    {
        var (_, error) = await PlatformEndpoints.RequireAsync(http, db, "platform:billing:view", ct);
        if (error is not null) return error;
        var rows = await db.QueryAsync("SELECT * FROM platform_tax_rules ORDER BY priority, rule_key", ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(rows));
    }

    private static async Task<IResult> TaxRuleUpsert(HttpContext http, Dictionary<string, object?> body, Database db, CancellationToken ct)
    {
        var (principal, error) = await PlatformEndpoints.RequireAsync(http, db, "platform:billing:manage", ct);
        if (error is not null) return error;

        var key = Str(body, "ruleKey");
        if (string.IsNullOrWhiteSpace(key)) return Bad("ruleKey is required");

        var treatment = (Str(body, "treatment") ?? "standard").ToLowerInvariant();
        string[] treatments = ["standard", "zero_rated", "exempt", "reverse_charge", "out_of_scope"];
        if (!treatments.Contains(treatment))
            return Bad($"treatment must be one of: {string.Join(", ", treatments)}");

        var rate = Decimal(body, "rate");
        if (rate is < 0 or > 1) return Bad("rate is a fraction — 0.15 means 15%");
        // Anything other than a standard-rated supply charges nothing by definition;
        // a non-zero rate on it would produce tax an auditor cannot justify.
        if (treatment != "standard" && rate is > 0)
            return Bad($"treatment '{treatment}' cannot carry a non-zero rate");

        await db.ExecuteAsync("""
            INSERT INTO platform_tax_rules
                (rule_key, description, match_country, match_registered, match_has_tax_id, match_feature_key,
                 treatment, tax_code, tax_category, rate, reason_text, priority, active, updated_at)
            VALUES (@k,@d,@c,@reg,@tid,@fk,@t,@code,@cat,@rate,@reason,@p,@a,NOW())
            ON CONFLICT (rule_key) DO UPDATE SET
                description       = EXCLUDED.description,
                match_country     = EXCLUDED.match_country,
                match_registered  = EXCLUDED.match_registered,
                match_has_tax_id  = EXCLUDED.match_has_tax_id,
                match_feature_key = EXCLUDED.match_feature_key,
                treatment         = EXCLUDED.treatment,
                tax_code          = EXCLUDED.tax_code,
                tax_category      = EXCLUDED.tax_category,
                rate              = EXCLUDED.rate,
                reason_text       = EXCLUDED.reason_text,
                priority          = EXCLUDED.priority,
                active            = EXCLUDED.active,
                updated_at        = NOW()
            """,
            c =>
            {
                c.Parameters.AddWithValue("@k", key!);
                c.Parameters.AddWithValue("@d", (object?)Str(body, "description") ?? DBNull.Value);
                c.Parameters.AddWithValue("@c", (object?)Str(body, "matchCountry")?.ToUpperInvariant() ?? DBNull.Value);
                c.Parameters.AddWithValue("@reg", (object?)Bool(body, "matchRegistered") ?? DBNull.Value);
                c.Parameters.AddWithValue("@tid", (object?)Bool(body, "matchHasTaxId") ?? DBNull.Value);
                c.Parameters.AddWithValue("@fk", (object?)Str(body, "matchFeatureKey") ?? DBNull.Value);
                c.Parameters.AddWithValue("@t", treatment);
                c.Parameters.AddWithValue("@code", Str(body, "taxCode") ?? "STD");
                c.Parameters.AddWithValue("@cat", Str(body, "taxCategory") ?? "S");
                c.Parameters.AddWithValue("@rate", (object?)rate ?? DBNull.Value);
                c.Parameters.AddWithValue("@reason", (object?)Str(body, "reasonText") ?? DBNull.Value);
                c.Parameters.AddWithValue("@p", (int)(Long(body, "priority") ?? 100));
                c.Parameters.AddWithValue("@a", Bool(body, "active") ?? true);
            }, ct);

        await PlatformEndpoints.AuditAsync(db, principal!, http, "tax.rule.set", "TaxRule", null, null,
            new { ruleKey = key, treatment, rate }, ct);
        return Results.Ok(ApiResponse<object>.Ok(new { ruleKey = key }, "Tax rule saved"));
    }

    private static async Task<IResult> TaxRuleDelete(string ruleKey, HttpContext http, Database db, CancellationToken ct)
    {
        var (principal, error) = await PlatformEndpoints.RequireAsync(http, db, "platform:billing:manage", ct);
        if (error is not null) return error;
        var removed = await db.ExecuteAsync("DELETE FROM platform_tax_rules WHERE rule_key=@k",
            c => c.Parameters.AddWithValue("@k", ruleKey), ct);
        if (removed == 0) return Results.Json(ApiResponse<object>.Fail("Not found"), statusCode: StatusCodes.Status404NotFound);
        await PlatformEndpoints.AuditAsync(db, principal!, http, "tax.rule.removed", "TaxRule", null, null, new { ruleKey }, ct);
        return Results.Ok(ApiResponse<object>.Ok(new { ruleKey }, "Tax rule removed"));
    }

    // ════════════════════════════ Billing readiness ══════════════════════════

    // Pre-flight for the billing run. Everything that would make an invoice wrong —
    // or make it not exist at all — surfaced BEFORE anyone presses generate, with the
    // exact fix attached to each finding.
    //
    // The ordering is deliberate: a blocker means a document cannot be raised, or
    // would be raised with the wrong tax. A warning means it will be raised but
    // something a controller should have seen is missing.
    internal static async Task<IResult> BillingReadiness(HttpContext http, Database db, CancellationToken ct)
    {
        var (_, error) = await PlatformEndpoints.RequireAsync(http, db, "platform:billing:view", ct);
        if (error is not null) return error;

        var period = EntitlementService.CurrentPeriodKey();
        var checks = new List<object>();

        // 1. BLOCKER — a billable tenant with no package produces no subscription line.
        var noPackage = await db.QueryAsync("""
            SELECT c.id, c.name, c.country, s.status
            FROM companies c
            JOIN tenant_subscriptions s ON s.company_id = c.id
            WHERE s.package_id IS NULL
              AND s.status IN ('active','trial','manual_contract')
            ORDER BY c.name
            """, ct: ct);
        checks.Add(Check("tenants_without_package", noPackage.Count == 0 ? "ok" : "blocker",
            "Tenants with no package assigned",
            "Without a package there is no subscription line and no metered pricing rules — the invoice would be empty.",
            "assign_package", noPackage));

        // 2. BLOCKER — the place of supply levies tax but OpsTrax has no registration
        //    on file, so every invoice raised there charges nothing.
        var unregistered = await db.QueryAsync("""
            SELECT r.country_code, cp.country_name, r.tax_label, r.standard_rate,
                   COUNT(c.id) AS tenant_count
            FROM platform_tax_registrations r
            JOIN country_profiles cp ON cp.country_code = r.country_code
            JOIN companies c ON c.country = r.country_code
            WHERE r.registered = false
              AND r.regime <> 'none'
              AND COALESCE(r.standard_rate, 0) > 0
            GROUP BY r.country_code, cp.country_name, r.tax_label, r.standard_rate
            ORDER BY COUNT(c.id) DESC
            """, ct: ct);
        checks.Add(Check("countries_without_registration", unregistered.Count == 0 ? "ok" : "blocker",
            "Countries you supply but are not registered in",
            "Tax is only charged where a registration is on file. Until one is recorded these tenants are reverse-charged or zero-rated.",
            "register_tax", unregistered));

        // 3. WARNING — no buyer tax ID means the supply cannot be reverse-charged and
        //    is treated as a consumer sale.
        var noTaxId = await db.QueryAsync("""
            SELECT c.id, c.name, c.country, cp.tax_id_label
            FROM companies c
            JOIN country_profiles cp ON cp.country_code = c.country
            JOIN tenant_subscriptions s ON s.company_id = c.id
            WHERE COALESCE(c.tax_id, '') = ''
              AND COALESCE(cp.default_tax_rate, 0) > 0
              AND s.status IN ('active','trial','manual_contract')
            ORDER BY c.name
            """, ct: ct);
        checks.Add(Check("tenants_without_tax_id", noTaxId.Count == 0 ? "ok" : "warning",
            "Tenants with no tax registration number",
            "A business customer without a tax ID cannot be reverse-charged and appears on the return as a consumer sale.",
            "edit_tenant", noTaxId));

        // 4. WARNING — a term priced against a meter nothing feeds bills zero forever,
        //    silently. This is the failure mode consumption pricing actually has.
        var deadMeters = await db.QueryAsync("""
            SELECT b.company_id, c.name AS tenant, b.feature_key, b.meter_key,
                   COALESCE((SELECT SUM(uc.value) FROM usage_counters uc
                             WHERE uc.meter_key = b.meter_key), 0) AS lifetime_usage
            FROM tenant_billing_plan_items b
            JOIN companies c ON c.id = b.company_id
            WHERE b.active AND b.charge_model IN ('per_unit','tiered')
              AND NOT EXISTS (
                  SELECT 1 FROM usage_counters uc
                  WHERE uc.meter_key = b.meter_key AND uc.value > 0)
            ORDER BY c.name, b.feature_key
            """, ct: ct);
        checks.Add(Check("consumption_terms_on_idle_meters", deadMeters.Count == 0 ? "ok" : "warning",
            "Consumption pricing on a meter with no recorded usage",
            "These terms bill zero every period. Either the meter is not wired to an action yet, or the tenant genuinely has not used the feature.",
            "review_plan", deadMeters));

        // 5. ACTION — who has not been invoiced for the current period.
        var uninvoiced = await db.QueryAsync("""
            SELECT c.id, c.name, c.country, s.status, s.mrr_cents, s.billing_currency
            FROM companies c
            JOIN tenant_subscriptions s ON s.company_id = c.id
            WHERE s.status IN ('active','manual_contract')
              AND s.package_id IS NOT NULL
              AND NOT EXISTS (
                  SELECT 1 FROM platform_invoices i
                  WHERE i.company_id = c.id
                    AND i.document_type = 'invoice'
                    AND i.status <> 'void'
                    AND TO_CHAR(COALESCE(i.period_start, i.created_at), 'YYYY-MM') = @p)
            ORDER BY s.mrr_cents DESC NULLS LAST, c.name
            """, c => c.Parameters.AddWithValue("@p", period), ct);
        checks.Add(Check("uninvoiced_this_period", uninvoiced.Count == 0 ? "ok" : "action",
            $"Billable tenants with no invoice for {period}",
            "These accounts are active on a package and have not been billed for the current period.",
            "generate_batch", uninvoiced));

        // 6. WARNING — a draft is not revenue. It has no number and nobody has seen it.
        var drafts = await db.QueryAsync("""
            SELECT i.id, i.invoice_number, c.name AS tenant, i.total_cents, i.currency, i.created_at
            FROM platform_invoices i JOIN companies c ON c.id = i.company_id
            WHERE i.status = 'draft' ORDER BY i.created_at
            """, ct: ct);
        checks.Add(Check("drafts_pending_issue", drafts.Count == 0 ? "ok" : "warning",
            "Drafts not yet issued",
            "A draft carries no document number and has not reached the customer. It is not collectable until it is issued.",
            "issue_invoice", drafts));

        // 7. WARNING — sent and past due. Collections, not configuration.
        var overdue = await db.QueryAsync("""
            SELECT i.id, i.invoice_number, c.name AS tenant, i.total_cents, i.currency, i.due_at,
                   EXTRACT(DAY FROM NOW() - i.due_at)::int AS days_overdue
            FROM platform_invoices i JOIN companies c ON c.id = i.company_id
            WHERE i.status IN ('sent','overdue') AND i.due_at < NOW()
            ORDER BY i.due_at
            """, ct: ct);
        checks.Add(Check("overdue_invoices", overdue.Count == 0 ? "ok" : "warning",
            "Issued invoices past their due date",
            "Payment terms have elapsed without settlement.",
            "chase", overdue));

        var blockers = checks.Count(c => SeverityOf(c) == "blocker");
        var warnings = checks.Count(c => SeverityOf(c) == "warning");
        var actions = checks.Count(c => SeverityOf(c) == "action");

        return Results.Ok(ApiResponse<object>.Ok(new
        {
            period,
            ready = blockers == 0,
            summary = new { blockers, warnings, actions, passed = checks.Count(c => SeverityOf(c) == "ok") },
            checks,
        }));
    }

    private static object Check(string id, string severity, string title, string detail, string action, List<Dictionary<string, object?>> items) =>
        new { id, severity, title, detail, action, count = items.Count, items };

    private static string SeverityOf(object check) =>
        check.GetType().GetProperty("severity")?.GetValue(check)?.ToString() ?? "ok";

    // Runs the whole billing period in one pass. Each tenant is independent: one that
    // has nothing billable is skipped and reported, never allowed to abort the run.
    // Idempotent by default — a tenant already invoiced for the period is skipped
    // rather than double-billed, which is the mistake a batch run gets to make once.
    internal static async Task<IResult> InvoiceGenerateBatch(
        HttpContext http, Dictionary<string, object?> body, Database db, PlatformBillingService billing, CancellationToken ct)
    {
        var (principal, error) = await PlatformEndpoints.RequireAsync(http, db, "platform:billing:manage", ct);
        if (error is not null) return error;

        var (start, end) = ResolvePeriod(body);
        var issue = Bool(body, "issue") ?? false;
        var terms = (int)(Long(body, "paymentTermsDays") ?? 15);
        var periodKey = start.ToString("yyyy-MM");

        var explicitIds = ReadLongArray(body, "companyIds");
        var candidates = await db.QueryAsync($"""
            SELECT c.id, c.name
            FROM companies c
            JOIN tenant_subscriptions s ON s.company_id = c.id
            WHERE s.status IN ('active','manual_contract')
              AND s.package_id IS NOT NULL
              {(explicitIds.Count > 0 ? "AND c.id = ANY(@ids)" : "")}
            ORDER BY c.name
            """,
            c => { if (explicitIds.Count > 0) c.Parameters.AddWithValue("@ids", explicitIds.ToArray()); }, ct);

        var results = new List<object>();
        long generated = 0, skipped = 0, failed = 0, totalBilled = 0;

        foreach (var row in candidates)
        {
            var companyId = Convert.ToInt64(row["id"]);
            var name = row["name"]?.ToString() ?? $"Tenant {companyId}";
            try
            {
                var already = await db.ScalarLongAsync("""
                    SELECT COUNT(*) FROM platform_invoices
                    WHERE company_id=@c AND document_type='invoice' AND status <> 'void'
                      AND TO_CHAR(COALESCE(period_start, created_at), 'YYYY-MM') = @p
                    """,
                    c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@p", periodKey); }, ct);
                if (already > 0)
                {
                    skipped++;
                    results.Add(new { companyId, tenant = name, outcome = "skipped", reason = "already invoiced for this period" });
                    continue;
                }

                var draft = await billing.BuildDraftAsync(companyId, start, end, ct);
                if (draft.Lines.Count == 0)
                {
                    skipped++;
                    results.Add(new { companyId, tenant = name, outcome = "skipped", reason = "nothing billable" });
                    continue;
                }

                var invoiceId = await billing.SaveDraftAsync(companyId, draft, "recurring", "invoice", terms, null, principal!.Email, ct);
                string? number = null;
                if (issue) number = await billing.IssueAsync(invoiceId, principal!.Email, ct);

                generated++;
                totalBilled += draft.TotalCents;
                results.Add(new
                {
                    companyId, tenant = name, outcome = issue ? "issued" : "drafted",
                    invoiceId, invoiceNumber = number,
                    lines = draft.Lines.Count,
                    currency = draft.Tax.Currency,
                    netCents = draft.SubtotalCents,
                    taxCents = draft.TaxTotalCents,
                    totalCents = draft.TotalCents,
                    treatment = draft.Tax.Decision.Treatment,
                });
            }
            catch (Exception ex)
            {
                // One tenant's bad configuration must not cost the other six their run.
                failed++;
                results.Add(new { companyId, tenant = name, outcome = "failed", reason = ex.Message });
            }
        }

        await PlatformEndpoints.AuditAsync(db, principal!, http, "invoice.batch.generated", "Invoice", null, null,
            new { period = periodKey, issue, generated, skipped, failed, totalBilled }, ct);

        return Results.Ok(ApiResponse<object>.Ok(
            new { period = periodKey, generated, skipped, failed, totalBilledCents = totalBilled, results },
            $"{generated} {(issue ? "issued" : "drafted")}, {skipped} skipped{(failed > 0 ? $", {failed} failed" : "")}"));
    }

    private static List<long> ReadLongArray(Dictionary<string, object?> b, string k)
    {
        if (!b.TryGetValue(k, out var v) || v is not JsonElement je || je.ValueKind != JsonValueKind.Array) return [];
        var list = new List<long>();
        foreach (var e in je.EnumerateArray())
            if (e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out var n)) list.Add(n);
        return list;
    }

    // ════════════════════════════ Revenue control ════════════════════════════

    // The roll-up behind Revenue & Usage: contracted MRR/ARR, what has actually
    // been billed and collected, per-tenant revenue with its billing posture, and
    // the meters that are actually moving this period.
    private static async Task<IResult> RevenueSummary(HttpContext http, Database db, CancellationToken ct)
    {
        var (_, error) = await PlatformEndpoints.RequireAsync(http, db, "platform:billing:view", ct);
        if (error is not null) return error;

        var period = EntitlementService.CurrentPeriodKey();

        // Counts only. Money is NEVER summed here: adding SAR to JPY to USD produces
        // a number that is wrong in every currency at once, and their minor units
        // differ besides. Amounts come back grouped by currency below.
        var totals = await db.QuerySingleAsync("""
            SELECT
              (SELECT COUNT(*) FROM tenant_subscriptions WHERE status IN ('active','manual_contract')) AS paying_tenants,
              (SELECT COUNT(*) FROM tenant_subscriptions WHERE status='trial') AS trial_tenants,
              (SELECT COUNT(*) FROM platform_invoices WHERE status='draft') AS drafts,
              (SELECT COUNT(*) FROM tenant_billing_plan_items WHERE active) AS plan_items,
              (SELECT COUNT(*) FROM platform_tax_registrations WHERE registered) AS registered_countries
            """, ct: ct);

        var byCurrency = await db.QueryAsync("""
            SELECT currency,
                   SUM(contracted_mrr) AS contracted_mrr,
                   SUM(collected)      AS collected,
                   SUM(outstanding)    AS outstanding,
                   SUM(tax_billed)     AS tax_billed
            FROM (
                SELECT COALESCE(billing_currency,'USD') AS currency,
                       COALESCE(SUM(mrr_cents),0) AS contracted_mrr,
                       0::bigint AS collected, 0::bigint AS outstanding, 0::bigint AS tax_billed
                FROM tenant_subscriptions
                WHERE status IN ('active','manual_contract')
                GROUP BY COALESCE(billing_currency,'USD')
                UNION ALL
                SELECT COALESCE(currency,'USD'),
                       0,
                       COALESCE(SUM(total_cents) FILTER (WHERE status='paid' AND document_type='invoice'),0),
                       COALESCE(SUM(total_cents) FILTER (WHERE status IN ('sent','overdue')),0),
                       COALESCE(SUM(tax_total_cents) FILTER (WHERE status IN ('sent','paid','overdue')),0)
                FROM platform_invoices
                GROUP BY COALESCE(currency,'USD')
            ) t
            GROUP BY currency
            ORDER BY SUM(contracted_mrr) DESC, currency
            """, ct: ct);

        var tenants = await db.QueryAsync("""
            SELECT c.id, c.name, c.country, s.status, s.billing_currency, s.mrr_cents,
                   p.name AS package_name,
                   (SELECT COUNT(*) FROM tenant_billing_plan_items b WHERE b.company_id=c.id AND b.active) AS plan_items,
                   (SELECT COUNT(*) FROM tenant_billing_plan_items b WHERE b.company_id=c.id AND b.active AND b.charge_model='free') AS free_items,
                   (SELECT COALESCE(SUM(i.total_cents),0) FROM platform_invoices i WHERE i.company_id=c.id AND i.status='paid') AS collected,
                   (SELECT COALESCE(SUM(i.total_cents),0) FROM platform_invoices i WHERE i.company_id=c.id AND i.status IN ('sent','overdue')) AS outstanding,
                   (SELECT MAX(i.issued_at) FROM platform_invoices i WHERE i.company_id=c.id AND i.status<>'draft') AS last_invoiced_at
            FROM companies c
            LEFT JOIN tenant_subscriptions s ON s.company_id=c.id
            LEFT JOIN packages p ON p.id=s.package_id
            ORDER BY s.mrr_cents DESC NULLS LAST, c.name
            """, ct: ct);

        // Only meters with movement this period — a wall of zeroes tells nobody
        // anything, and hides the two meters that are actually being consumed.
        var meterActivity = await db.QueryAsync("""
            SELECT m.meter_key, m.name, m.unit, m.period,
                   COALESCE(SUM(uc.value),0) AS total_value,
                   COUNT(DISTINCT uc.company_id) FILTER (WHERE uc.value > 0) AS active_tenants
            FROM usage_meters m
            LEFT JOIN usage_counters uc
              ON uc.meter_key = m.meter_key
             AND uc.period_key = CASE WHEN m.period='lifetime' THEN 'lifetime' ELSE @p END
            WHERE m.active
            GROUP BY m.meter_key, m.name, m.unit, m.period
            ORDER BY COALESCE(SUM(uc.value),0) DESC, m.meter_key
            """, c => c.Parameters.AddWithValue("@p", period), ct);

        var recent = await db.QueryAsync("""
            SELECT i.id, i.invoice_number, i.document_type, i.status, i.total_cents, i.tax_total_cents,
                   i.currency, i.issued_at, i.due_at, c.name AS tenant
            FROM platform_invoices i JOIN companies c ON c.id=i.company_id
            ORDER BY i.created_at DESC LIMIT 10
            """, ct: ct);

        return Results.Ok(ApiResponse<object>.Ok(new { period, totals, byCurrency, tenants, meterActivity, recentDocuments = recent }));
    }

    private static async Task<IResult> OverrideDelete(long tenantId, string meterKey, HttpContext http, Database db, CancellationToken ct)
    {
        var (principal, error) = await PlatformEndpoints.RequireAsync(http, db, "platform:billing:manage", ct);
        if (error is not null) return error;
        var removed = await db.ExecuteAsync(
            "DELETE FROM tenant_contract_overrides WHERE company_id=@c AND meter_key=@m",
            c => { c.Parameters.AddWithValue("@c", tenantId); c.Parameters.AddWithValue("@m", meterKey); }, ct);
        if (removed == 0) return Results.Json(ApiResponse<object>.Fail("Not found"), statusCode: StatusCodes.Status404NotFound);
        await PlatformEndpoints.AuditAsync(db, principal!, http, "billing.override.removed", "ContractOverride", null, tenantId,
            new { meterKey }, ct);
        return Results.Ok(ApiResponse<object>.Ok(new { meterKey }, "Contract override removed"));
    }

    // ════════════════════════════ Projection + parsing ═══════════════════════

    private static object Project(PlatformBillingService.Draft d) => new
    {
        companyId = d.CompanyId,
        tenant = d.TenantName,
        periodStart = d.PeriodStart.ToString("yyyy-MM-dd"),
        periodEnd = d.PeriodEnd.ToString("yyyy-MM-dd"),
        currency = d.Tax.Currency,
        tax = ProjectTax(d.Tax),
        lines = d.Lines.Select((l, i) => new
        {
            lineNo = i + 1,
            source = l.Source,
            featureKey = l.FeatureKey,
            meterKey = l.MeterKey,
            description = l.Description,
            chargeModel = l.ChargeModel,
            quantity = l.Quantity,
            unit = l.Unit,
            unitPriceCents = l.UnitPriceCents,
            grossAmountCents = l.GrossAmountCents,
            discountCents = l.DiscountCents,
            netAmountCents = l.NetAmountCents,
            taxCode = l.TaxCode,
            taxCategory = l.TaxCategory,
            taxTreatment = l.TaxTreatment,
            taxRate = l.TaxRate,
            taxAmountCents = l.TaxAmountCents,
            exemptionReason = l.ExemptionReason,
            totalCents = l.TotalCents,
        }),
        taxBreakdown = d.Lines
            .GroupBy(l => new { l.TaxCode, l.TaxCategory, l.TaxTreatment, l.TaxRate })
            .Select(g => new
            {
                taxCode = g.Key.TaxCode,
                taxCategory = g.Key.TaxCategory,
                treatment = g.Key.TaxTreatment,
                rate = g.Key.TaxRate,
                taxableCents = g.Sum(l => l.NetAmountCents),
                taxCents = g.Sum(l => l.TaxAmountCents),
                lineCount = g.Count(),
            })
            .OrderByDescending(g => g.rate),
        subtotalCents = d.SubtotalCents,
        discountCents = d.DiscountCents,
        taxTotalCents = d.TaxTotalCents,
        totalCents = d.TotalCents,
        notes = d.Notes,
    };

    private static object ProjectTax(PlatformBillingService.TaxContext t) => new
    {
        countryCode = t.CountryCode,
        countryName = t.CountryName,
        regime = t.Regime,
        taxLabel = t.TaxLabel,
        sellerRegistered = t.SellerRegistered,
        sellerLegalName = t.SellerLegalName,
        sellerTaxNo = t.SellerTaxNo,
        buyerLegalName = t.BuyerLegalName,
        buyerTaxNo = t.BuyerTaxNo,
        currency = t.Currency,
        minorUnits = t.MinorUnits,
        invoicingScheme = t.InvoicingScheme,
        placeOfSupply = t.PlaceOfSupply,
        treatment = t.Decision.Treatment,
        taxCode = t.Decision.TaxCode,
        taxCategory = t.Decision.TaxCategory,
        rate = t.Decision.Rate,
        reasonText = t.Decision.ReasonText,
        ruleKey = t.Decision.RuleKey,
    };

    // A billing period defaults to the calendar month containing today, which is
    // what "generate this month's invoice" means to everyone who asks for it.
    private static (DateOnly Start, DateOnly End) ResolvePeriod(Dictionary<string, object?> body)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var start = ParseDate(Str(body, "periodStart")) ?? new DateOnly(today.Year, today.Month, 1);
        var end = ParseDate(Str(body, "periodEnd")) ?? start.AddMonths(1).AddDays(-1);
        if (end < start) end = start;
        return (start, end);
    }

    private static DateOnly? ParseDate(string? s) =>
        DateOnly.TryParse(s, out var d) ? d : null;

    // Manual lines are net-of-tax by contract: the caller supplies quantity and
    // unit price (or an explicit amount), and tax is layered on here.
    internal static List<PlatformBillingService.DraftLine> ReadLines(Dictionary<string, object?> body)
    {
        var result = new List<PlatformBillingService.DraftLine>();
        if (!body.TryGetValue("lines", out var raw) || raw is not JsonElement je || je.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var e in je.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object) continue;
            var description = GetString(e, "description");
            if (string.IsNullOrWhiteSpace(description)) continue;

            var qty = GetDecimal(e, "quantity") ?? 1m;
            var unitPrice = GetLong(e, "unitPriceCents") ?? 0L;
            var explicitAmount = GetLong(e, "amountCents");

            result.Add(new PlatformBillingService.DraftLine
            {
                Source = GetString(e, "source") ?? "manual",
                FeatureKey = GetString(e, "featureKey"),
                MeterKey = GetString(e, "meterKey"),
                Description = description!,
                ChargeModel = GetString(e, "chargeModel") ?? "flat",
                Quantity = qty,
                Unit = GetString(e, "unit"),
                UnitPriceCents = unitPrice,
                // An explicit amount wins so a negotiated lump sum is not re-derived
                // (and re-rounded) from quantity × price.
                GrossAmountCents = explicitAmount ?? 0L,
                DiscountCents = GetLong(e, "discountCents") ?? 0L,
            });
        }
        return result;
    }

    private static string? GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static long? GetLong(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : null;
    private static decimal? GetDecimal(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var n) ? n : null;

    private static IResult Bad(string detail) =>
        Results.Json(ApiResponse<object>.Fail("Validation failed", detail), statusCode: StatusCodes.Status400BadRequest);

    // ── Body readers (JsonElement-aware, mirroring PlatformEndpoints) ────────
    private static string? Str(Dictionary<string, object?> b, string k)
    {
        if (!b.TryGetValue(k, out var v) || v is null) return null;
        if (v is JsonElement je)
            return je.ValueKind switch
            {
                JsonValueKind.String => je.GetString(),
                JsonValueKind.Number => je.ToString(),
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => je.ToString(),
            };
        var s = v.ToString();
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    private static long? Long(Dictionary<string, object?> b, string k)
    {
        if (!b.TryGetValue(k, out var v) || v is null) return null;
        if (v is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Number && je.TryGetInt64(out var n)) return n;
            if (je.ValueKind == JsonValueKind.String && long.TryParse(je.GetString(), out var sn)) return sn;
            return null;
        }
        return long.TryParse(v.ToString(), out var f) ? f : null;
    }

    private static decimal? Decimal(Dictionary<string, object?> b, string k)
    {
        if (!b.TryGetValue(k, out var v) || v is null) return null;
        if (v is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Number && je.TryGetDecimal(out var n)) return n;
            if (je.ValueKind == JsonValueKind.String && decimal.TryParse(je.GetString(), out var sn)) return sn;
            return null;
        }
        return decimal.TryParse(v.ToString(), out var f) ? f : null;
    }

    private static bool? Bool(Dictionary<string, object?> b, string k)
    {
        if (!b.TryGetValue(k, out var v) || v is null) return null;
        if (v is JsonElement je)
            return je.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => bool.TryParse(je.GetString(), out var pb) ? pb : null,
                _ => null,
            };
        return bool.TryParse(v.ToString(), out var fb) ? fb : null;
    }
}
