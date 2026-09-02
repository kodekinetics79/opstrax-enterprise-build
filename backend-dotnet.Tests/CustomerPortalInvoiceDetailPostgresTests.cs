using Microsoft.Extensions.Configuration;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// Customer portal — invoice detail tenancy boundary.
//
// The portal list deliberately carried no invoice id, so an invoice could never
// be opened. Exposing the id and adding a detail read widens the surface, so the
// property that matters is: the detail read is scoped by company_id AND
// customer_id, and a guessed id belonging to somebody else resolves to null —
// never to their line items, their prices, or the fact that it exists.
// ─────────────────────────────────────────────────────────────────────────────
[Trait("Category", "Integration")]
public sealed class CustomerPortalInvoiceDetailPostgresTests
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

    // The finance tables sit on a dependency chain — issued_invoice_lines references
    // invoice_draft_lines (RevenueReadiness), which references job_charges
    // (BusinessSpine). Stood up in the same order Program.cs boots them, so this
    // suite works on a cold database instead of inheriting tables from a prior run.
    private static async Task EnsureFinanceSchemaAsync(Database db)
    {
        await new FoundationSchemaService(db).EnsureAsync();
        await new BusinessSpineSchemaService(db).EnsureAsync();
        await new CommercialFoundationSchemaService(db).EnsureAsync();
        await new RevenueReadinessSchemaService(db).EnsureAsync();
        await new FinanceActivationSchemaService(db).EnsureAsync();
        await new TaxSchemaService(db).EnsureAsync();
        // companies.legal_name is added by PlatformSchemaService (and stage79 for
        // protected environments); the invoice document prints it as the supplier.
        await new PlatformSchemaService(db).EnsureAsync();
    }

    private static Task<long> SeedCompanyAsync(Database db, string code, string name) =>
        db.InsertAsync(
            "INSERT INTO companies (company_code, name, industry, status) VALUES (@code, @name, 'Logistics', 'Active') RETURNING id",
            c => { c.Parameters.AddWithValue("@code", code); c.Parameters.AddWithValue("@name", name); });

    private static Task<long> SeedCustomerAsync(Database db, long companyId, string name) =>
        db.InsertAsync(
            "INSERT INTO customers (company_id, customer_code, name, status) VALUES (@c, @code, @n, 'Active') RETURNING id",
            c =>
            {
                c.Parameters.AddWithValue("@c", companyId);
                c.Parameters.AddWithValue("@code", $"C-{Guid.NewGuid():N}"[..12]);
                c.Parameters.AddWithValue("@n", name);
            });

    private static async Task<Guid> SeedInvoiceAsync(
        Database db, long companyId, long customerId, string number, decimal total)
    {
        var id = Guid.NewGuid();
        var net = Math.Round(total / 1.15m, 2);

        // issued_invoices carries an FK to the draft it was issued from, so the
        // chain has to be real rather than a synthesised id.
        var draftId = Guid.NewGuid();
        await db.ExecuteAsync(
            @"INSERT INTO invoice_drafts
                (id, company_id, customer_id, invoice_draft_no, status, currency, subtotal, tax_total, total)
              VALUES (@id, @c, @cust, @no, 'issued', 'SAR', @sub, @tax, @total)",
            c =>
            {
                c.Parameters.AddWithValue("@id", draftId);
                c.Parameters.AddWithValue("@c", companyId);
                c.Parameters.AddWithValue("@cust", customerId);
                c.Parameters.AddWithValue("@no", $"DR-{number}");
                c.Parameters.AddWithValue("@sub", net);
                c.Parameters.AddWithValue("@tax", total - net);
                c.Parameters.AddWithValue("@total", total);
            });

        await db.ExecuteAsync(
            @"INSERT INTO issued_invoices
                (id, company_id, customer_id, source_invoice_draft_id, source_invoice_draft_no,
                 invoice_number, currency, subtotal, tax_total, total, amount_paid, balance_due, due_at)
              VALUES (@id, @c, @cust, @draft, @draftNo, @num, 'SAR', @sub, @tax, @total, 0, @total, NOW() + INTERVAL '30 days')",
            c =>
            {
                c.Parameters.AddWithValue("@id", id);
                c.Parameters.AddWithValue("@c", companyId);
                c.Parameters.AddWithValue("@cust", customerId);
                c.Parameters.AddWithValue("@draft", draftId);
                c.Parameters.AddWithValue("@draftNo", $"DR-{number}");
                c.Parameters.AddWithValue("@num", number);
                c.Parameters.AddWithValue("@sub", net);
                c.Parameters.AddWithValue("@tax", total - net);
                c.Parameters.AddWithValue("@total", total);
            });

        await db.ExecuteAsync(
            @"INSERT INTO issued_invoice_lines
                (company_id, issued_invoice_id, line_no, description, charge_code, quantity, unit, unit_rate, amount)
              VALUES (@c, @id, 1, 'Linehaul Riyadh to Dammam', 'LINEHAUL', 1, 'load', @amt, @amt)",
            c =>
            {
                c.Parameters.AddWithValue("@c", companyId);
                c.Parameters.AddWithValue("@id", id);
                c.Parameters.AddWithValue("@amt", net);
            });

        return id;
    }

    [Fact]
    public async Task Detail_Returns_Lines_For_The_Owner_And_Null_For_Everyone_Else()
    {
        var db = Db();
        await EnsureFinanceSchemaAsync(db);
        var svc = new CustomerPortalService(db);
        var suffix = Unique();

        var companyId = await SeedCompanyAsync(db, $"PRT-{suffix}", $"Portal Invoice Test {suffix}");
        var otherCompanyId = await SeedCompanyAsync(db, $"PRT2-{suffix}", $"Other Tenant {suffix}");

        var mine = await SeedCustomerAsync(db, companyId, $"Acme Shipper {suffix}");
        var sibling = await SeedCustomerAsync(db, companyId, $"Rival Shipper {suffix}");
        var foreign = await SeedCustomerAsync(db, otherCompanyId, $"Foreign Shipper {suffix}");

        var myInvoice = await SeedInvoiceAsync(db, companyId, mine, $"INV-{suffix}-A", 1150m);
        var siblingInvoice = await SeedInvoiceAsync(db, companyId, sibling, $"INV-{suffix}-B", 2300m);
        var foreignInvoice = await SeedInvoiceAsync(db, otherCompanyId, foreign, $"INV-{suffix}-C", 3450m);

        // The owner gets the document, with the line detail the summary card cannot show.
        var own = await svc.GetOwnInvoiceDetailAsync(companyId, mine, myInvoice);
        Assert.NotNull(own);
        Assert.Equal($"INV-{suffix}-A", own!["invoiceNumber"]?.ToString());
        var lines = (own["lines"] as IEnumerable<Dictionary<string, object?>>)!.ToList();
        Assert.Single(lines);
        Assert.Equal("Linehaul Riyadh to Dammam", lines[0]["description"]?.ToString());

        // A sibling customer inside the SAME tenant cannot read it — this is the
        // boundary a shared company_id would otherwise leak straight through.
        Assert.Null(await svc.GetOwnInvoiceDetailAsync(companyId, sibling, myInvoice));
        Assert.Null(await svc.GetOwnInvoiceDetailAsync(companyId, mine, siblingInvoice));

        // A customer in a different tenant cannot read it either, in either direction.
        Assert.Null(await svc.GetOwnInvoiceDetailAsync(companyId, mine, foreignInvoice));
        Assert.Null(await svc.GetOwnInvoiceDetailAsync(otherCompanyId, foreign, myInvoice));

        // A random id is indistinguishable from someone else's — no existence oracle.
        Assert.Null(await svc.GetOwnInvoiceDetailAsync(companyId, mine, Guid.NewGuid()));

        await db.ExecuteAsync("DELETE FROM issued_invoices WHERE company_id = ANY(@ids)",
            c => c.Parameters.AddWithValue("@ids", new[] { companyId, otherCompanyId }));
    }

    [Fact]
    public async Task List_Exposes_The_Id_So_A_Card_Can_Open_Its_Own_Document()
    {
        var db = Db();
        await EnsureFinanceSchemaAsync(db);
        var svc = new CustomerPortalService(db);
        var suffix = Unique();

        var companyId = await SeedCompanyAsync(db, $"PRT3-{suffix}", $"Portal List Test {suffix}");
        var customerId = await SeedCustomerAsync(db, companyId, $"Shipper {suffix}");
        var invoiceId = await SeedInvoiceAsync(db, companyId, customerId, $"INV-{suffix}-L", 575m);

        var list = await svc.GetOwnInvoicesAsync(companyId, customerId);
        var row = Assert.Single(list);

        // The id was previously queried and then dropped from the projection, which
        // is exactly why the card could never be opened.
        Assert.True(row.ContainsKey("id"), "the portal list must carry the invoice id");
        Assert.Equal(invoiceId.ToString(), row["id"]?.ToString());

        // Customer-safe projection only — no cost, margin or internal references.
        foreach (var leaked in new[] { "cost", "margin", "contractId", "jobId", "issuedByActorId" })
            Assert.False(row.ContainsKey(leaked), $"portal projection must not expose {leaked}");

        await db.ExecuteAsync("DELETE FROM issued_invoices WHERE company_id=@c",
            c => c.Parameters.AddWithValue("@c", companyId));
    }
}
