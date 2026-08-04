using Microsoft.Extensions.Configuration;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

// Tax engine (VAT / GST / ZATCA-VAT) — ADR-008 P3. Verified against real Postgres: the reconciliation
// invariant (no profile => today's zero tax exactly), exclusive/inclusive VAT math, exemptions,
// zero-rating, and every fail-closed gate (unregistered seller, unsupported regime, unmatched line).
[Trait("Category", "Integration")]
public class TaxServicePostgresTests
{
    [Fact]
    public async Task No_Profile_Reproduces_Zero_Tax()
    {
        var (db, svc, cid, cust) = Setup();
        try
        {
            var draft = await SeedDraftAsync(db, cid, cust, "USD", ("BASE", "base", 1000m));
            var o = await svc.ComputeForDraftAsync(cid, draft, TaxMode.Commit);

            Assert.False(o.Applied);
            Assert.Equal("no_tax_profile", o.Reason);
            var (tax, total) = await Totals(db, cid, draft);
            Assert.Equal(0m, tax);
            Assert.Equal(1000m, total);
            Assert.Equal(0, await TaxLineCount(db, cid, draft));
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task Vat_Exclusive_Standard_Rate()
    {
        var (db, svc, cid, cust) = Setup();
        try
        {
            var p = await PublishProfileAsync(db, cid, "vat", inclusive: false);
            await RuleAsync(db, cid, p, "STANDARD", 0.15m, "S");
            await SellerRegAsync(db, cid, "vat");
            var draft = await SeedDraftAsync(db, cid, cust, "USD", ("BASE", "base", 1000m));

            var o = await svc.ComputeForDraftAsync(cid, draft, TaxMode.Commit);

            Assert.True(o.Applied);
            Assert.Equal(150.00m, o.TaxTotal);
            Assert.Equal(1000m, o.Subtotal);
            Assert.Equal(1150.00m, o.Total);
            var (tax, total) = await Totals(db, cid, draft);
            Assert.Equal(150.00m, tax);
            Assert.Equal(1150.00m, total);
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task Vat_Inclusive_Backs_Out_Tax()
    {
        var (db, svc, cid, cust) = Setup();
        try
        {
            var p = await PublishProfileAsync(db, cid, "vat", inclusive: true);
            await RuleAsync(db, cid, p, "STANDARD", 0.15m, "S");
            await SellerRegAsync(db, cid, "vat");
            var draft = await SeedDraftAsync(db, cid, cust, "USD", ("BASE", "base", 1150m));

            var o = await svc.ComputeForDraftAsync(cid, draft, TaxMode.Commit);

            Assert.True(o.Applied);
            Assert.Equal(150.00m, o.TaxTotal);   // 1150 - 1150/1.15
            Assert.Equal(1000.00m, o.Subtotal);
            Assert.Equal(1150.00m, o.Total);
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task Exempt_Customer_Pays_Zero_Tax()
    {
        var (db, svc, cid, cust) = Setup();
        try
        {
            var p = await PublishProfileAsync(db, cid, "vat", inclusive: false);
            await RuleAsync(db, cid, p, "STANDARD", 0.15m, "S");
            await SellerRegAsync(db, cid, "vat");
            await db.ExecuteAsync(
                "INSERT INTO customer_tax_status (company_id, customer_id, tax_exempt, exemption_reason, effective_date) VALUES (@c,@cust,TRUE,'Diplomatic', DATE '2025-01-01')",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@cust", cust); });
            var draft = await SeedDraftAsync(db, cid, cust, "USD", ("BASE", "base", 1000m));

            var o = await svc.ComputeForDraftAsync(cid, draft, TaxMode.Commit);

            Assert.True(o.Applied);
            Assert.Equal(0m, o.TaxTotal);
            Assert.Equal(1000m, o.Total);
            var code = (await db.QuerySingleAsync("SELECT tax_code FROM invoice_tax_lines WHERE company_id=@c AND invoice_draft_id=@d LIMIT 1",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@d", draft); }))!["taxCode"]?.ToString();
            Assert.Equal("EXEMPT", code);
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task Zero_Rated_Rule_Charges_No_Tax()
    {
        var (db, svc, cid, cust) = Setup();
        try
        {
            var p = await PublishProfileAsync(db, cid, "vat", inclusive: false);
            await RuleAsync(db, cid, p, "ZERO", 0m, "Z");   // zero-rated, no seller reg needed
            var draft = await SeedDraftAsync(db, cid, cust, "USD", ("BASE", "base", 1000m));

            var o = await svc.ComputeForDraftAsync(cid, draft, TaxMode.Commit);

            Assert.True(o.Applied);
            Assert.Equal(0m, o.TaxTotal);
            Assert.Equal(1000m, o.Total);
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task Seller_Not_Registered_Is_FailClosed()
    {
        var (db, svc, cid, cust) = Setup();
        try
        {
            var p = await PublishProfileAsync(db, cid, "vat", inclusive: false);
            await RuleAsync(db, cid, p, "STANDARD", 0.15m, "S");   // non-zero tax, but NO seller registration
            var draft = await SeedDraftAsync(db, cid, cust, "USD", ("BASE", "base", 1000m));

            var o = await svc.ComputeForDraftAsync(cid, draft, TaxMode.Commit);

            Assert.False(o.Applied);
            Assert.Equal("seller_not_registered", o.Reason);
            Assert.Equal(0, await TaxLineCount(db, cid, draft));   // zero writes
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task Unsupported_Regime_Is_FailClosed()
    {
        var (db, svc, cid, cust) = Setup();
        try
        {
            var p = await PublishProfileAsync(db, cid, "us_sales_tax", inclusive: false);
            await RuleAsync(db, cid, p, "STANDARD", 0.08m, "S");
            var draft = await SeedDraftAsync(db, cid, cust, "USD", ("BASE", "base", 1000m));

            var o = await svc.ComputeForDraftAsync(cid, draft, TaxMode.Commit);

            Assert.False(o.Applied);
            Assert.StartsWith("regime_unsupported_phase1", o.Reason);
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task Line_With_No_Matching_Rule_Is_FailClosed()
    {
        var (db, svc, cid, cust) = Setup();
        try
        {
            var p = await PublishProfileAsync(db, cid, "vat", inclusive: false);
            await SellerRegAsync(db, cid, "vat");
            // A rule that only matches a different charge code, and NO catch-all.
            await db.ExecuteAsync(
                "INSERT INTO tax_rules (company_id, tax_profile_id, match_charge_code, tax_code, tax_category, rate, taxable, priority) VALUES (@c,@p,'SPECIAL','STANDARD','S',0.15,TRUE,10)",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@p", p); });
            var draft = await SeedDraftAsync(db, cid, cust, "USD", ("BASE", "base", 1000m));

            var o = await svc.ComputeForDraftAsync(cid, draft, TaxMode.Commit);

            Assert.False(o.Applied);
            Assert.StartsWith("no_rule_for_line", o.Reason);
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task Recompute_Is_Idempotent()
    {
        var (db, svc, cid, cust) = Setup();
        try
        {
            var p = await PublishProfileAsync(db, cid, "vat", inclusive: false);
            await RuleAsync(db, cid, p, "STANDARD", 0.15m, "S");
            await SellerRegAsync(db, cid, "vat");
            var draft = await SeedDraftAsync(db, cid, cust, "USD", ("BASE", "base", 1000m));

            await svc.ComputeForDraftAsync(cid, draft, TaxMode.Commit);
            var o = await svc.ComputeForDraftAsync(cid, draft, TaxMode.Commit);

            Assert.True(o.Applied);
            Assert.Equal(1, await TaxLineCount(db, cid, draft));   // one line, not two
            var (tax, _) = await Totals(db, cid, draft);
            Assert.Equal(150.00m, tax);
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task Gst_Unregistered_Seller_Is_FailClosed()
    {
        var (db, svc, cid, cust) = Setup();
        try
        {
            var p = await PublishProfileAsync(db, cid, "gst", inclusive: false);
            await RuleAsync(db, cid, p, "STANDARD", 0.10m, "S");   // non-zero GST, NO seller registration
            var draft = await SeedDraftAsync(db, cid, cust, "USD", ("BASE", "base", 1000m));

            var o = await svc.ComputeForDraftAsync(cid, draft, TaxMode.Commit);

            Assert.False(o.Applied);
            Assert.Equal("seller_not_registered", o.Reason);
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task Reverse_Charge_Rule_Is_FailClosed()
    {
        var (db, svc, cid, cust) = Setup();
        try
        {
            var p = await PublishProfileAsync(db, cid, "vat", inclusive: false);
            await RuleAsync(db, cid, p, "REVERSE_CHARGE", 0m, "S");
            await SellerRegAsync(db, cid, "vat");
            var draft = await SeedDraftAsync(db, cid, cust, "USD", ("BASE", "base", 1000m));

            var o = await svc.ComputeForDraftAsync(cid, draft, TaxMode.Commit);

            Assert.False(o.Applied);
            Assert.StartsWith("regime_unsupported_phase1:reverse_charge", o.Reason);
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task Zatca_NonSAR_Currency_Is_FailClosed()
    {
        var (db, svc, cid, cust) = Setup();
        try
        {
            var p = await PublishProfileAsync(db, cid, "zatca_vat", inclusive: false);
            await RuleAsync(db, cid, p, "STANDARD", 0.15m, "S");
            await SellerRegAsync(db, cid, "zatca_vat");
            var draft = await SeedDraftAsync(db, cid, cust, "USD", ("BASE", "base", 1000m));   // not SAR

            var o = await svc.ComputeForDraftAsync(cid, draft, TaxMode.Commit);

            Assert.False(o.Applied);
            Assert.Equal("fx_vat_unsupported_phase1", o.Reason);
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task MultiLine_Mixed_Categories_And_PerLine_Rounding()
    {
        var (db, svc, cid, cust) = Setup();
        try
        {
            var p = await PublishProfileAsync(db, cid, "vat", inclusive: false);
            await SellerRegAsync(db, cid, "vat");
            // Standard for FUEL, zero-rated for GROCERY, catch-all standard. Amounts chosen to force
            // per-line half-up rounding: 100.10*0.15 = 15.015 -> 15.02; 33.33*0.15 = 4.9995 -> 5.00.
            await db.ExecuteAsync("INSERT INTO tax_rules (company_id, tax_profile_id, match_charge_code, tax_code, tax_category, rate, taxable, priority) VALUES (@c,@p,'GROCERY','ZERO','Z',0,TRUE,20)",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@p", p); });
            await RuleAsync(db, cid, p, "STANDARD", 0.15m, "S");   // catch-all standard, priority 0
            var draft = await SeedDraftAsync(db, cid, cust, "USD",
                ("BASE", "base", 100.10m), ("GROCERY", "base", 500m), ("FUEL", "fuel", 33.33m));

            var o = await svc.ComputeForDraftAsync(cid, draft, TaxMode.Commit);

            Assert.True(o.Applied);
            // 15.02 (base) + 0 (grocery zero-rated) + 5.00 (fuel) = 20.02
            Assert.Equal(20.02m, o.TaxTotal);
            var lineSum = await db.ScalarDecimalAsync("SELECT COALESCE(SUM(tax_amount),0) FROM invoice_tax_lines WHERE company_id=@c AND invoice_draft_id=@d",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@d", draft); });
            Assert.Equal(o.TaxTotal, lineSum);   // canonical invariant: tax_total == Σ line tax_amount
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task Freight_Line_Not_Taxed_When_Profile_Disables_Freight()
    {
        var (db, svc, cid, cust) = Setup();
        try
        {
            // freight_taxable=false; a catch-all standard rule would otherwise tax it.
            var p = await db.InsertAsync(
                @"INSERT INTO tax_profiles (company_id, profile_code, profile_name, regime, price_inclusive, freight_taxable, currency, effective_date, status, author_user_id, published_by_user_id, published_at)
                  VALUES (@c, @code, 'P', 'vat', FALSE, FALSE, NULL, DATE '2025-01-01', 'published', 1, 2, NOW()) RETURNING id",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@code", $"TPF-{cid}"); });
            await RuleAsync(db, cid, p, "STANDARD", 0.15m, "S");
            await SellerRegAsync(db, cid, "vat");
            var draft = await SeedDraftAsync(db, cid, cust, "USD", ("FREIGHT", "freight", 200m), ("BASE", "base", 1000m));

            var o = await svc.ComputeForDraftAsync(cid, draft, TaxMode.Commit);

            Assert.True(o.Applied);
            Assert.Equal(150.00m, o.TaxTotal);   // only the 1000 base is taxed (200 freight out-of-scope)
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task Blocked_Recompute_Clears_Stale_Tax()
    {
        var (db, svc, cid, cust) = Setup();
        try
        {
            var p = await PublishProfileAsync(db, cid, "vat", inclusive: false);
            await RuleAsync(db, cid, p, "STANDARD", 0.15m, "S");
            var seller = await db.InsertAsync(
                "INSERT INTO seller_tax_registration (company_id, jurisdiction, regime, tax_registration_no, effective_date) VALUES (@c,'XX','vat','TRN', DATE '2025-01-01') RETURNING id",
                c => c.Parameters.AddWithValue("@c", cid));
            var draft = await SeedDraftAsync(db, cid, cust, "USD", ("BASE", "base", 1000m));

            var first = await svc.ComputeForDraftAsync(cid, draft, TaxMode.Commit);
            Assert.Equal(150.00m, first.TaxTotal);
            Assert.Equal(1, await TaxLineCount(db, cid, draft));

            // Seller registration lapses -> recompute now BLOCKS. Stale 150 tax + line must be cleared.
            await db.ExecuteAsync("DELETE FROM seller_tax_registration WHERE id=@s", c => c.Parameters.AddWithValue("@s", seller));
            var second = await svc.ComputeForDraftAsync(cid, draft, TaxMode.Commit);

            Assert.False(second.Applied);
            Assert.Equal("seller_not_registered", second.Reason);
            var (tax, total) = await Totals(db, cid, draft);
            Assert.Equal(0m, tax);
            Assert.Equal(1000m, total);
            Assert.Equal(0, await TaxLineCount(db, cid, draft));   // stale line cleared
        }
        finally { await CleanupAsync(db, cid); }
    }

    // ── helpers ──

    private static (Database, TaxService, long, long) Setup()
    {
        var db = new Database(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString }).Build());
        var cid = db.InsertAsync("INSERT INTO companies (company_code, name, industry) VALUES (@code,'Tax Co','logistics') RETURNING id",
            c => c.Parameters.AddWithValue("@code", $"TX-{Guid.NewGuid():N}".Substring(0, 16))).GetAwaiter().GetResult();
        var cust = db.InsertAsync("INSERT INTO customers (company_id, customer_code, name) VALUES (@c,@code,'Buyer') RETURNING id",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@code", $"CU-{cid}"); }).GetAwaiter().GetResult();
        return (db, new TaxService(db), cid, cust);
    }

    private static async Task<long> PublishProfileAsync(Database db, long cid, string regime, bool inclusive) =>
        await db.InsertAsync(
            @"INSERT INTO tax_profiles (company_id, profile_code, profile_name, regime, price_inclusive, currency, effective_date, status, author_user_id, published_by_user_id, published_at)
              VALUES (@c, @code, 'P', @regime, @incl, NULL, DATE '2025-01-01', 'published', 1, 2, NOW()) RETURNING id",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@code", $"TP-{cid}-{regime}");
                   c.Parameters.AddWithValue("@regime", regime); c.Parameters.AddWithValue("@incl", inclusive); });

    private static async Task RuleAsync(Database db, long cid, long profileId, string taxCode, decimal rate, string category) =>
        await db.ExecuteAsync(
            "INSERT INTO tax_rules (company_id, tax_profile_id, tax_code, tax_category, rate, taxable, priority) VALUES (@c,@p,@code,@cat,@rate,TRUE,0)",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@p", profileId);
                   c.Parameters.AddWithValue("@code", taxCode); c.Parameters.AddWithValue("@cat", category); c.Parameters.AddWithValue("@rate", rate); });

    private static async Task SellerRegAsync(Database db, long cid, string regime) =>
        await db.ExecuteAsync(
            "INSERT INTO seller_tax_registration (company_id, jurisdiction, regime, tax_registration_no, effective_date) VALUES (@c,'XX',@r,'TRN123', DATE '2025-01-01')",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@r", regime); });

    private static async Task<Guid> SeedDraftAsync(Database db, long cid, long cust, string currency, params (string code, string type, decimal amount)[] lines)
    {
        var subtotal = lines.Sum(l => l.amount);
        var draftId = (Guid)(await db.QuerySingleAsync(
            @"INSERT INTO invoice_drafts (company_id, customer_id, invoice_draft_no, status, currency, subtotal, tax_total, total)
              VALUES (@c, @cust, @no, 'draft', @cur, @sub, 0, @sub) RETURNING id",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@cust", cust);
                   c.Parameters.AddWithValue("@no", $"D-{cid}-{Guid.NewGuid():N}".Substring(0, 24));
                   c.Parameters.AddWithValue("@cur", currency); c.Parameters.AddWithValue("@sub", subtotal); }))!["id"]!;
        var ln = 1;
        foreach (var l in lines)
            await db.ExecuteAsync(
                "INSERT INTO invoice_draft_lines (company_id, invoice_draft_id, line_no, description, charge_code, unit, amount) VALUES (@c,@d,@ln,@desc,@code,@type,@amt)",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@d", draftId); c.Parameters.AddWithValue("@ln", ln++);
                       c.Parameters.AddWithValue("@desc", l.code); c.Parameters.AddWithValue("@code", l.code); c.Parameters.AddWithValue("@type", l.type); c.Parameters.AddWithValue("@amt", l.amount); });
        return draftId;
    }

    private static async Task<(decimal tax, decimal total)> Totals(Database db, long cid, Guid draft)
    {
        var r = (await db.QuerySingleAsync("SELECT tax_total, total FROM invoice_drafts WHERE company_id=@c AND id=@d",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@d", draft); }))!;
        return (Convert.ToDecimal(r["taxTotal"]), Convert.ToDecimal(r["total"]));
    }

    private static async Task<long> TaxLineCount(Database db, long cid, Guid draft) =>
        await db.ScalarLongAsync("SELECT COUNT(*) FROM invoice_tax_lines WHERE company_id=@c AND invoice_draft_id=@d",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@d", draft); });

    private static async Task CleanupAsync(Database db, long cid)
    {
        foreach (var t in new[] { "invoice_tax_lines", "invoice_draft_lines", "invoice_drafts", "tax_rules", "tax_profiles", "customer_tax_status", "seller_tax_registration", "customers" })
            await db.ExecuteAsync($"DELETE FROM {t} WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", cid));
        await db.ExecuteAsync("DELETE FROM companies WHERE id=@c", c => c.Parameters.AddWithValue("@c", cid));
    }
}
