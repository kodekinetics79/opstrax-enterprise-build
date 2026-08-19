using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// Platform billing itemization + indirect tax — accounting proof suite.
//
// The invariants an auditor actually checks, asserted against real Postgres:
//   • Tax is rounded PER LINE and the header total is the SUM of line tax.
//   • The header can never drift from its own line detail.
//   • Per-feature terms (free / flat / per_unit) each produce the right line, and
//     a `free` term still appears — a giveaway must be visible, not invisible.
//   • A meter priced by a tenant plan item is not ALSO billed by the package
//     overage rule (no double billing).
//   • Reverse charge applies when OpsTrax is not registered locally and the buyer
//     has a tax ID; the standard rate applies once a registration is recorded.
//   • Document numbers are allocated at issue, sequentially, and only once.
//   • An issued document is immutable; correction is a credit note that negates.
// ─────────────────────────────────────────────────────────────────────────────
[Collection("platform-control-plane")]
[Trait("Category", "Integration")]
public sealed class PlatformBillingItemizationPostgresTests
{
    private static Database Db()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString,
            })
            .Build();
        return new Database(config);
    }

    private static string Unique() => Guid.NewGuid().ToString("N")[..10];

    private static Microsoft.AspNetCore.Http.DefaultHttpContext Http(string bearer)
    {
        var http = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        http.Request.Headers.Authorization = $"Bearer {bearer}";
        return http;
    }

    private static int? StatusOf(IResult r) => (r as IStatusCodeHttpResult)?.StatusCode;
    private static object? ValueOf(IResult r) => (r as IValueHttpResult)?.Value;

    private static T Field<T>(object? envelope, params string[] path)
    {
        object? current = envelope;
        foreach (var name in path)
        {
            var prop = current?.GetType().GetProperty(name);
            current = prop?.GetValue(current);
        }
        return (T)Convert.ChangeType(current!, typeof(T))!;
    }

    // Serializes one readiness check's findings so a test can assert on the tenants
    // inside it without depending on counters a sibling test also moves.
    private static string CheckJson(object? payload, string checkId)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        foreach (var check in doc.RootElement.GetProperty("checks").EnumerateArray())
            if (check.GetProperty("id").GetString() == checkId)
                return check.GetProperty("items").GetRawText();
        return "";
    }

    private static async Task<string> SeedOperatorAsync(Database db, string email)
    {
        var roleId = await db.ScalarLongAsync("SELECT id FROM platform_roles WHERE role_key='platform_super_admin'");
        var adminId = await db.InsertAsync(
            @"INSERT INTO platform_admins (email, full_name, password_hash, role_id, status)
              VALUES (@e, 'Billing Test Operator', @h, @r, 'Active') RETURNING id",
            c =>
            {
                c.Parameters.AddWithValue("@e", email);
                c.Parameters.AddWithValue("@h", PlatformSchemaService.HashPassword("Test-Password-123!"));
                c.Parameters.AddWithValue("@r", roleId);
            });
        var token = Guid.NewGuid().ToString("N");
        await db.ExecuteAsync(
            "INSERT INTO platform_sessions (admin_id, session_token, expires_at) VALUES (@a, @t, NOW() + INTERVAL '1 hour')",
            c => { c.Parameters.AddWithValue("@a", adminId); c.Parameters.AddWithValue("@t", token); });
        return token;
    }

    private static async Task EnsureSchemaAsync(Database db)
    {
        await new PlatformSchemaService(db).EnsureAsync();
        await new CountryProfileSchemaService(db).EnsureAsync();
        await new RevenueSchemaService(db).EnsureAsync();
        await new MarketPackSchemaService(db).EnsureAsync();
        await new PlatformBillingSchemaService(db).EnsureAsync();
    }

    // A Saudi tenant on a 499.00 package with a tax ID on file. Saudi Arabia carries
    // a 15% VAT rate in the country catalog, so this exercises a real rate rather
    // than a contrived one.
    private static async Task<(long CompanyId, long PackageId)> SeedTenantAsync(Database db, string suffix)
    {
        var companyId = await db.InsertAsync(
            @"INSERT INTO companies (company_code, name, legal_name, industry, status, country, currency, tax_id)
              VALUES (@code, @name, @legal, 'Logistics', 'Active', 'SA', 'SAR', '310123456700003') RETURNING id",
            c =>
            {
                c.Parameters.AddWithValue("@code", $"BILL-{suffix}");
                c.Parameters.AddWithValue("@name", $"Billing Test {suffix}");
                c.Parameters.AddWithValue("@legal", $"Billing Test {suffix} LLC");
            });

        var packageId = await db.InsertAsync(
            @"INSERT INTO packages (package_code, name, base_price_cents, seat_price_cents, included_seats, currency)
              VALUES (@code, 'Growth', 49900, 0, 100, 'SAR') RETURNING id",
            c => c.Parameters.AddWithValue("@code", $"growth-{suffix}"));

        await db.ExecuteAsync(
            @"INSERT INTO tenant_subscriptions (company_id, package_id, status, seat_limit, billing_currency, mrr_cents)
              VALUES (@c, @p, 'active', 50, 'SAR', 49900)",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@p", packageId); });

        return (companyId, packageId);
    }

    private static Task RegisterSellerAsync(Database db, string country, bool registered) =>
        db.ExecuteAsync(
            @"UPDATE platform_tax_registrations
                 SET registered=@r, tax_registration_no=@no, legal_name='OpsTrax Arabia', updated_at=NOW()
               WHERE country_code=@c",
            c =>
            {
                c.Parameters.AddWithValue("@r", registered);
                c.Parameters.AddWithValue("@no", registered ? (object)"300000000000003" : DBNull.Value);
                c.Parameters.AddWithValue("@c", country);
            });

    private static Task AddPlanItemAsync(
        Database db, long companyId, string featureKey, string model,
        long flat = 0, long unit = 0, decimal included = 0, string? meterKey = null) =>
        db.ExecuteAsync(
            @"INSERT INTO tenant_billing_plan_items
                (company_id, feature_key, feature_label, charge_model, meter_key,
                 unit_price_cents, included_quantity, flat_price_cents, currency, updated_by)
              VALUES (@c,@f,@l,@m,@mk,@u,@i,@fl,'SAR','test@opstrax.test')",
            c =>
            {
                c.Parameters.AddWithValue("@c", companyId);
                c.Parameters.AddWithValue("@f", featureKey);
                c.Parameters.AddWithValue("@l", featureKey);
                c.Parameters.AddWithValue("@m", model);
                c.Parameters.AddWithValue("@mk", (object?)meterKey ?? DBNull.Value);
                c.Parameters.AddWithValue("@u", unit);
                c.Parameters.AddWithValue("@i", included);
                c.Parameters.AddWithValue("@fl", flat);
            });

    [Fact]
    public async Task Draft_Is_Itemized_Taxed_Per_Line_And_Header_Equals_Line_Sum()
    {
        var db = Db();
        await EnsureSchemaAsync(db);
        var suffix = Unique();
        var (companyId, packageId) = await SeedTenantAsync(db, suffix);
        await RegisterSellerAsync(db, "SA", registered: true);

        // Three commercial shapes on one tenant: one feature given away, one at a
        // flat fee, one billed per event against a live meter.
        await AddPlanItemAsync(db, companyId, "fleet.dispatch", "free");
        await AddPlanItemAsync(db, companyId, "fleet.telematics", "flat", flat: 20000);
        await AddPlanItemAsync(db, companyId, "fleet.cold_chain", "per_unit",
            unit: 250, included: 10, meterKey: "pod.monthly");

        // 30 proof-of-delivery events this period → 20 billable after the allowance.
        var period = EntitlementService.CurrentPeriodKey();
        await db.ExecuteAsync(
            @"INSERT INTO usage_counters (company_id, meter_key, period_key, value)
              VALUES (@c,'pod.monthly',@p,30)
              ON CONFLICT (company_id, meter_key, period_key) DO UPDATE SET value=30",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@p", period); });

        // A package overage rule on the SAME meter must not fire — the plan item
        // already prices it, and billing it twice is the classic itemization bug.
        await db.ExecuteAsync(
            @"INSERT INTO pricing_rules (package_id, meter_key, included_quantity, unit_price_cents, currency, overage_allowed)
              VALUES (@p,'pod.monthly',0,900,'SAR',true)
              ON CONFLICT (package_id, meter_key) DO NOTHING",
            c => c.Parameters.AddWithValue("@p", packageId));

        var billing = new PlatformBillingService(db);
        var start = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var draft = await billing.BuildDraftAsync(companyId, start, start.AddMonths(1).AddDays(-1));

        Assert.Equal("standard", draft.Tax.Decision.Treatment);
        Assert.Equal(0.15m, draft.Tax.Decision.Rate);
        Assert.Equal("SAR", draft.Tax.Currency);

        var byFeature = draft.Lines.ToDictionary(l => l.FeatureKey ?? "", l => l);

        // Subscription base: 499.00 net, 74.85 VAT.
        Assert.Equal(49900, byFeature["subscription.base"].NetAmountCents);
        Assert.Equal(7485, byFeature["subscription.base"].TaxAmountCents);

        // Free feature still produces a visible zero line.
        Assert.Equal(0, byFeature["fleet.dispatch"].NetAmountCents);
        Assert.Equal("free", byFeature["fleet.dispatch"].ChargeModel);

        // Flat fee: 200.00 net, 30.00 VAT.
        Assert.Equal(20000, byFeature["fleet.telematics"].NetAmountCents);
        Assert.Equal(3000, byFeature["fleet.telematics"].TaxAmountCents);

        // Per event: (30 − 10) × 2.50 = 50.00 net, 7.50 VAT.
        Assert.Equal(5000, byFeature["fleet.cold_chain"].NetAmountCents);
        Assert.Equal(750, byFeature["fleet.cold_chain"].TaxAmountCents);

        // The package overage rule was suppressed, and said so.
        Assert.DoesNotContain(draft.Lines, l => l.Source == "usage" && l.MeterKey == "pod.monthly");
        Assert.Contains(draft.Notes, n => n.Contains("double billing"));

        // The header is the sum of its own lines — never a rate applied to a subtotal.
        Assert.Equal(draft.Lines.Sum(l => l.NetAmountCents), draft.SubtotalCents);
        Assert.Equal(draft.Lines.Sum(l => l.TaxAmountCents), draft.TaxTotalCents);
        Assert.Equal(draft.SubtotalCents + draft.TaxTotalCents, draft.TotalCents);
        Assert.Equal(74900, draft.SubtotalCents);
        Assert.Equal(11235, draft.TaxTotalCents);
    }

    [Fact]
    public async Task Persisted_Header_Is_Derived_From_Lines_And_Issue_Numbers_Sequentially()
    {
        var db = Db();
        await EnsureSchemaAsync(db);
        var suffix = Unique();
        var (companyId, _) = await SeedTenantAsync(db, suffix);
        await RegisterSellerAsync(db, "SA", registered: true);

        var billing = new PlatformBillingService(db);
        var start = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var end = start.AddMonths(1).AddDays(-1);

        var draft = await billing.BuildDraftAsync(companyId, start, end);
        var invoiceId = await billing.SaveDraftAsync(companyId, draft, "recurring", "invoice", 30, null, "test@opstrax.test");

        var header = await db.QuerySingleAsync(
            "SELECT status, invoice_number, subtotal_cents, tax_total_cents, total_cents, amount_cents FROM platform_invoices WHERE id=@i",
            c => c.Parameters.AddWithValue("@i", invoiceId));
        Assert.Equal("draft", header?["status"]?.ToString());
        Assert.StartsWith("DRAFT-", header?["invoiceNumber"]?.ToString());

        var lineSums = await db.QuerySingleAsync(
            "SELECT COALESCE(SUM(net_amount_cents),0) net, COALESCE(SUM(tax_amount_cents),0) tax FROM platform_invoice_lines WHERE invoice_id=@i",
            c => c.Parameters.AddWithValue("@i", invoiceId));
        Assert.Equal(Convert.ToInt64(lineSums!["net"]), Convert.ToInt64(header!["subtotalCents"]));
        Assert.Equal(Convert.ToInt64(lineSums["tax"]), Convert.ToInt64(header["taxTotalCents"]));
        // amount_cents is the legacy grand total the collections KPIs still read.
        Assert.Equal(Convert.ToInt64(header["totalCents"]), Convert.ToInt64(header["amountCents"]));

        // Issue allocates a real number; a second issue is refused because an issued
        // document is immutable.
        var number = await billing.IssueAsync(invoiceId, "test@opstrax.test");
        Assert.Matches(@"^INV-SA-\d{4}-\d{5}$", number);
        await Assert.ThrowsAsync<InvalidOperationException>(() => billing.IssueAsync(invoiceId, "test@opstrax.test"));

        // The next document takes the next number in the same scope, with no gap.
        var second = await billing.SaveDraftAsync(companyId, draft, "recurring", "invoice", 30, null, "test@opstrax.test");
        var secondNumber = await billing.IssueAsync(second, "test@opstrax.test");
        var firstSeq = int.Parse(number.Split('-')[^1]);
        var secondSeq = int.Parse(secondNumber.Split('-')[^1]);
        Assert.Equal(firstSeq + 1, secondSeq);

        // A credit note is a NEW document that negates the original, which is untouched.
        var creditId = await billing.CreateCreditNoteAsync(invoiceId, "Service credit under SLA", "test@opstrax.test");
        var credit = await db.QuerySingleAsync(
            "SELECT document_type, total_cents, credit_note_of FROM platform_invoices WHERE id=@i",
            c => c.Parameters.AddWithValue("@i", creditId));
        Assert.Equal("credit_note", credit?["documentType"]?.ToString());
        Assert.Equal(-Convert.ToInt64(header["totalCents"]), Convert.ToInt64(credit!["totalCents"]));
        Assert.Equal(invoiceId, Convert.ToInt64(credit["creditNoteOf"]));

        var creditLines = await db.QueryAsync(
            "SELECT total_cents FROM platform_invoice_lines WHERE invoice_id=@i",
            c => c.Parameters.AddWithValue("@i", creditId));
        Assert.NotEmpty(creditLines);
        Assert.All(creditLines, l => Assert.True(Convert.ToInt64(l["totalCents"]) <= 0));

        var creditNumber = await billing.IssueAsync(creditId, "test@opstrax.test");
        Assert.StartsWith("CN-SA-", creditNumber);
    }

    [Fact]
    public async Task Unregistered_Seller_With_Buyer_Tax_Id_Reverse_Charges_At_Zero()
    {
        var db = Db();
        await EnsureSchemaAsync(db);
        var suffix = Unique();
        var (companyId, _) = await SeedTenantAsync(db, suffix);
        await RegisterSellerAsync(db, "SA", registered: false);

        var billing = new PlatformBillingService(db);
        var tax = await billing.ResolveTaxAsync(companyId);

        Assert.Equal("reverse_charge", tax.Decision.Treatment);
        Assert.Equal(0m, tax.Decision.Rate);
        Assert.False(tax.SellerRegistered);
        Assert.Contains("customer accounts for the tax", tax.Decision.ReasonText);

        var start = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var draft = await billing.BuildDraftAsync(companyId, start, start.AddMonths(1).AddDays(-1));
        Assert.Equal(0, draft.TaxTotalCents);
        Assert.All(draft.Lines, l => Assert.Equal("AE", l.TaxCategory));
        // A supply that charges no tax must still say why, on the line itself.
        Assert.All(draft.Lines, l => Assert.False(string.IsNullOrWhiteSpace(l.ExemptionReason)));

        // Recording the registration flips the same tenant to a domestic standard supply.
        await RegisterSellerAsync(db, "SA", registered: true);
        var afterRegistration = await billing.ResolveTaxAsync(companyId);
        Assert.Equal("standard", afterRegistration.Decision.Treatment);
        Assert.Equal(0.15m, afterRegistration.Decision.Rate);
    }

    [Fact]
    public async Task Country_Without_An_Indirect_Tax_Regime_Is_Out_Of_Scope()
    {
        var db = Db();
        await EnsureSchemaAsync(db);
        var suffix = Unique();

        // The United States carries no default indirect-tax rate in the country
        // catalog — SaaS there is not a VAT/GST supply.
        var companyId = await db.InsertAsync(
            @"INSERT INTO companies (company_code, name, industry, status, country, currency)
              VALUES (@code, @name, 'Logistics', 'Active', 'US', 'USD') RETURNING id",
            c =>
            {
                c.Parameters.AddWithValue("@code", $"BILL-US-{suffix}");
                c.Parameters.AddWithValue("@name", $"US Billing Test {suffix}");
            });

        var tax = await new PlatformBillingService(db).ResolveTaxAsync(companyId);
        Assert.Equal("out_of_scope", tax.Decision.Treatment);
        Assert.Equal(0m, tax.Decision.Rate);
        Assert.Equal("USD", tax.Currency);
        Assert.Equal(2, tax.MinorUnits);
    }

    [Fact]
    public async Task Readiness_Flags_Missing_Package_And_Unregistered_Country_As_Blockers()
    {
        var db = Db();
        await EnsureSchemaAsync(db);
        var suffix = Unique();
        var token = await SeedOperatorAsync(db, $"ready-{suffix}@opstrax.test");

        // A Saudi tenant on an active subscription with NO package — the classic
        // reason a billing run silently produces nothing.
        var companyId = await db.InsertAsync(
            @"INSERT INTO companies (company_code, name, industry, status, country, currency)
              VALUES (@code, @name, 'Logistics', 'Active', 'SA', 'SAR') RETURNING id",
            c =>
            {
                c.Parameters.AddWithValue("@code", $"RDY-{suffix}");
                c.Parameters.AddWithValue("@name", $"Readiness Test {suffix}");
            });
        await db.ExecuteAsync(
            @"INSERT INTO tenant_subscriptions (company_id, package_id, status, seat_limit, billing_currency)
              VALUES (@c, NULL, 'active', 25, 'SAR')",
            c => c.Parameters.AddWithValue("@c", companyId));
        await RegisterSellerAsync(db, "SA", registered: false);

        var result = await PlatformBillingEndpoints.BillingReadiness(Http(token), db, CancellationToken.None);
        Assert.Equal(StatusCodes.Status200OK, StatusOf(result));

        var payload = ValueOf(result)!.GetType().GetProperty("Data")!.GetValue(ValueOf(result));
        Assert.False(Field<bool>(payload, "ready"), "an unpackaged tenant plus an unregistered taxing country is not ready");
        Assert.True(Field<int>(payload, "summary", "blockers") >= 2);

        // Recording the package and the registration clears both blockers.
        var packageId = await db.InsertAsync(
            @"INSERT INTO packages (package_code, name, base_price_cents, currency)
              VALUES (@code, 'Growth', 49900, 'SAR') RETURNING id",
            c => c.Parameters.AddWithValue("@code", $"rdy-growth-{suffix}"));
        await db.ExecuteAsync("UPDATE tenant_subscriptions SET package_id=@p WHERE company_id=@c",
            c => { c.Parameters.AddWithValue("@p", packageId); c.Parameters.AddWithValue("@c", companyId); });
        await RegisterSellerAsync(db, "SA", registered: true);

        var after = await PlatformBillingEndpoints.BillingReadiness(Http(token), db, CancellationToken.None);
        var afterPayload = ValueOf(after)!.GetType().GetProperty("Data")!.GetValue(ValueOf(after));

        // Sibling tests share this database, so assert on THIS tenant's position in
        // the ladder rather than on the global counters: both blockers must have let
        // go of it, and it must now be sitting in the "ready to bill" bucket.
        var tenantName = $"Readiness Test {suffix}";
        Assert.DoesNotContain(tenantName, CheckJson(afterPayload, "tenants_without_package"));
        Assert.DoesNotContain(tenantName, CheckJson(afterPayload, "countries_without_registration"));
        Assert.Contains(tenantName, CheckJson(afterPayload, "uninvoiced_this_period"));

        await db.ExecuteAsync("DELETE FROM tenant_subscriptions WHERE company_id=@c",
            c => c.Parameters.AddWithValue("@c", companyId));
    }

    [Fact]
    public async Task Batch_Run_Is_Idempotent_And_Never_Double_Bills_A_Period()
    {
        var db = Db();
        await EnsureSchemaAsync(db);
        var suffix = Unique();
        var token = await SeedOperatorAsync(db, $"batch-{suffix}@opstrax.test");
        var (companyId, _) = await SeedTenantAsync(db, suffix);
        await RegisterSellerAsync(db, "SA", registered: true);

        var billing = new PlatformBillingService(db);

        var first = await PlatformBillingEndpoints.InvoiceGenerateBatch(
            Http(token), new() { ["companyIds"] = System.Text.Json.JsonSerializer.SerializeToElement(new[] { companyId }), ["issue"] = true },
            db, billing, CancellationToken.None);
        Assert.Equal(StatusCodes.Status200OK, StatusOf(first));
        var firstPayload = ValueOf(first)!.GetType().GetProperty("Data")!.GetValue(ValueOf(first));
        Assert.Equal(1L, Field<long>(firstPayload, "generated"));

        // Re-running the same period must skip, not raise a second document.
        var second = await PlatformBillingEndpoints.InvoiceGenerateBatch(
            Http(token), new() { ["companyIds"] = System.Text.Json.JsonSerializer.SerializeToElement(new[] { companyId }), ["issue"] = true },
            db, billing, CancellationToken.None);
        var secondPayload = ValueOf(second)!.GetType().GetProperty("Data")!.GetValue(ValueOf(second));
        Assert.Equal(0L, Field<long>(secondPayload, "generated"));
        Assert.Equal(1L, Field<long>(secondPayload, "skipped"));

        Assert.Equal(1, await db.ScalarLongAsync(
            "SELECT COUNT(*) FROM platform_invoices WHERE company_id=@c AND document_type='invoice' AND status<>'void'",
            c => c.Parameters.AddWithValue("@c", companyId)));
    }


    [Fact]
    public async Task Issued_Documents_Resist_Deletion_Duplicate_Billing_And_Over_Crediting()
    {
        var db = Db();
        await EnsureSchemaAsync(db);
        var suffix = Unique();
        var token = await SeedOperatorAsync(db, $"guard-{suffix}@opstrax.test");
        var (companyId, _) = await SeedTenantAsync(db, suffix);
        await RegisterSellerAsync(db, "SA", registered: true);

        var billing = new PlatformBillingService(db);
        var start = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var end = start.AddMonths(1).AddDays(-1);

        var draft = await billing.BuildDraftAsync(companyId, start, end);
        var invoiceId = await billing.SaveDraftAsync(companyId, draft, "recurring", "invoice", 15, null, "test@opstrax.test");
        var number = await billing.IssueAsync(invoiceId, "test@opstrax.test");

        // 1. An issued document holds an allocated sequence number. Deleting it would
        //    leave a hole no tax authority accepts, so the bulk path must refuse.
        var deleteAttempt = await PlatformEndpoints.InvoiceBulk(Http(token),
            new()
            {
                ["ids"] = System.Text.Json.JsonSerializer.SerializeToElement(new[] { invoiceId }),
                ["action"] = "delete",
            }, db, CancellationToken.None);
        Assert.Equal(StatusCodes.Status200OK, StatusOf(deleteAttempt));
        Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM platform_invoices WHERE id=@i",
            c => c.Parameters.AddWithValue("@i", invoiceId)));
        Assert.Contains("Only an unissued draft can be deleted",
            System.Text.Json.JsonSerializer.Serialize(ValueOf(deleteAttempt)));

        // An actual draft, by contrast, is disposable — it never consumed a number.
        var throwaway = await billing.SaveDraftAsync(companyId, draft, "recurring", "invoice", 15, null, "test@opstrax.test");
        await PlatformEndpoints.InvoiceBulk(Http(token),
            new()
            {
                ["ids"] = System.Text.Json.JsonSerializer.SerializeToElement(new[] { throwaway }),
                ["action"] = "delete",
            }, db, CancellationToken.None);
        Assert.Equal(0, await db.ScalarLongAsync("SELECT COUNT(*) FROM platform_invoices WHERE id=@i",
            c => c.Parameters.AddWithValue("@i", throwaway)));

        // 2. Generating the same tenant+period twice must not raise a second live,
        //    numbered, tax-bearing document for one supply.
        var duplicate = await PlatformBillingEndpoints.InvoiceGenerate(Http(token),
            new() { ["companyId"] = System.Text.Json.JsonSerializer.SerializeToElement(companyId) },
            db, billing, CancellationToken.None);
        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(duplicate));
        Assert.Contains(number, System.Text.Json.JsonSerializer.Serialize(ValueOf(duplicate)));

        // 3. Credit is capped at the original: the second full credit is refused.
        await billing.CreateCreditNoteAsync(invoiceId, "Full service credit", "test@opstrax.test");
        var over = await Assert.ThrowsAsync<InvalidOperationException>(
            () => billing.CreateCreditNoteAsync(invoiceId, "Second bite", "test@opstrax.test"));
        Assert.Contains("already fully credited", over.Message);
    }

    [Fact]
    public async Task Annual_Package_Is_Not_Billed_By_Every_Monthly_Run()
    {
        var db = Db();
        await EnsureSchemaAsync(db);
        var suffix = Unique();
        var (companyId, packageId) = await SeedTenantAsync(db, suffix);
        await RegisterSellerAsync(db, "SA", registered: true);

        // An annual term starting in January: only a January run may charge it.
        await db.ExecuteAsync("UPDATE packages SET billing_interval='annual' WHERE id=@p",
            c => c.Parameters.AddWithValue("@p", packageId));
        await db.ExecuteAsync("UPDATE tenant_subscriptions SET contract_start=@d WHERE company_id=@c",
            c =>
            {
                c.Parameters.AddWithValue("@d", new DateTime(DateTime.UtcNow.Year, 1, 1));
                c.Parameters.AddWithValue("@c", companyId);
            });

        var billing = new PlatformBillingService(db);

        var july = new DateOnly(DateTime.UtcNow.Year, 7, 1);
        var offCycle = await billing.BuildDraftAsync(companyId, july, july.AddMonths(1).AddDays(-1));
        Assert.DoesNotContain(offCycle.Lines, l => l.FeatureKey == "subscription.base");
        Assert.Contains(offCycle.Notes, n => n.Contains("billed annually"));

        var january = new DateOnly(DateTime.UtcNow.Year, 1, 1);
        var onCycle = await billing.BuildDraftAsync(companyId, january, january.AddMonths(1).AddDays(-1));
        var baseLine = Assert.Single(onCycle.Lines.Where(l => l.FeatureKey == "subscription.base"));
        Assert.Equal(49900, baseLine.NetAmountCents);
        Assert.Contains("annual term", baseLine.Description);
    }
}
