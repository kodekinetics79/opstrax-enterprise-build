using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Configuration;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

// ZATCA Phase-2 FOUNDATION proof (real Postgres). Verifies the artifacts we produce
// without ZATCA onboarding: UBL 2.1 XML (parses + carries ICV/PIH/totals), SHA-256
// invoice hash, TLV/base64 QR (decodes to the 5 mandatory tags), ICV increment, and the
// PIH chain (invoice N's stored PIH == invoice N-1's hash). Clearance stays
// 'pending_onboarding' (stub gateway) — asserted, not faked.
public class ZatcaPostgresTests
{
    private const string LocalConnectionString =
        "Host=127.0.0.1;Port=5433;Database=opstrax_local;Username=zayra;Password=zayra";

    [Fact]
    public async Task Generate_Produces_Ubl_Hash_Qr_And_Chains_Pih_Across_Invoices()
    {
        var db = CreateDatabase();
        await new ZatcaSchemaService(db).EnsureAsync();
        var svc = new ZatcaService(db, new PendingOnboardingZatcaGateway());
        var companyCode = $"ZATCA-{Guid.NewGuid():N}".ToUpperInvariant()[..18];

        long companyId = await db.InsertAsync(
            "INSERT INTO companies (company_code, name, industry, status, country, currency) VALUES (@c,'ZATCA Seller','Logistics','Active','SA','SAR') RETURNING id",
            c => c.Parameters.AddWithValue("@c", companyCode));

        try
        {
            var inv1 = await SeedIssuedInvoiceAsync(db, companyId, "ZINV-1", 1000m, 150m, 1150m);
            var inv2 = await SeedIssuedInvoiceAsync(db, companyId, "ZINV-2", 2000m, 300m, 2300m);

            var r1 = await svc.GenerateForIssuedInvoiceAsync(companyId, inv1, "standard");
            Assert.NotNull(r1);
            Assert.Equal(1, r1!.Icv);                                   // first ICV = 1
            Assert.Equal("pending_onboarding", r1.ClearanceStatus);     // stub, honest
            Assert.False(string.IsNullOrWhiteSpace(r1.InvoiceHash));

            // UBL parses and carries the mandatory Phase-2 references.
            var xml = XDocument.Parse(r1.UblXml);
            var text = r1.UblXml;
            Assert.Contains("ZINV-1", text);
            Assert.Contains("<cbc:UUID", text);
            Assert.Contains("ICV", text);
            Assert.Contains("PIH", text);
            Assert.Contains("388", text);                               // tax invoice type code
            Assert.NotNull(xml.Root);

            // QR decodes to TLV with the 5 mandatory tags (1..5).
            var tlv = Convert.FromBase64String(r1.QrBase64);
            var tags = ParseTlvTags(tlv);
            Assert.Contains(1, tags); // seller name
            Assert.Contains(2, tags); // VAT number
            Assert.Contains(3, tags); // timestamp
            Assert.Contains(4, tags); // invoice total
            Assert.Contains(5, tags); // VAT total

            // Second invoice: ICV increments and PIH == first invoice's hash (the chain).
            var r2 = await svc.GenerateForIssuedInvoiceAsync(companyId, inv2, "standard");
            Assert.NotNull(r2);
            Assert.Equal(2, r2!.Icv);
            Assert.Equal(r1.InvoiceHash, r2.Pih);

            // Idempotent: regenerating returns the same record (same ICV/hash).
            var r1Again = await svc.GenerateForIssuedInvoiceAsync(companyId, inv1, "standard");
            Assert.Equal(r1.Icv, r1Again!.Icv);
            Assert.Equal(r1.InvoiceHash, r1Again.InvoiceHash);

            // Persisted + tenant-scoped.
            var count = await db.ScalarLongAsync("SELECT COUNT(*) FROM zatca_invoices WHERE company_id=@c",
                c => c.Parameters.AddWithValue("@c", companyId));
            Assert.Equal(2, count);
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM zatca_invoices WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
            await db.ExecuteAsync("DELETE FROM issued_invoices WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
            await db.ExecuteAsync("DELETE FROM invoice_drafts WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
            await db.ExecuteAsync("DELETE FROM customers WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
            await db.ExecuteAsync("DELETE FROM companies WHERE id=@c", c => c.Parameters.AddWithValue("@c", companyId));
        }
    }

    [Fact]
    public async Task First_Invoice_Pih_Is_Base64_Sha256_Of_Zero()
    {
        var db = CreateDatabase();
        await new ZatcaSchemaService(db).EnsureAsync();
        var svc = new ZatcaService(db, new PendingOnboardingZatcaGateway());
        var companyCode = $"ZATCA0-{Guid.NewGuid():N}".ToUpperInvariant()[..18];
        long companyId = await db.InsertAsync(
            "INSERT INTO companies (company_code, name, industry, status, country, currency) VALUES (@c,'Z','Logistics','Active','SA','SAR') RETURNING id",
            c => c.Parameters.AddWithValue("@c", companyCode));
        try
        {
            var inv = await SeedIssuedInvoiceAsync(db, companyId, "Z0-1", 100m, 15m, 115m);
            var r = await svc.GenerateForIssuedInvoiceAsync(companyId, inv, "standard");
            var expected = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes("0")));
            Assert.Equal(expected, r!.Pih);
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM zatca_invoices WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
            await db.ExecuteAsync("DELETE FROM issued_invoices WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
            await db.ExecuteAsync("DELETE FROM invoice_drafts WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
            await db.ExecuteAsync("DELETE FROM customers WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
            await db.ExecuteAsync("DELETE FROM companies WHERE id=@c", c => c.Parameters.AddWithValue("@c", companyId));
        }
    }

    private static async Task<Guid> SeedIssuedInvoiceAsync(Database db, long companyId, string number, decimal sub, decimal vat, decimal total)
    {
        var custId = await db.InsertAsync(
            "INSERT INTO customers (company_id, customer_code, name, status) VALUES (@cid, @code, 'ZATCA Customer', 'Active') RETURNING id",
            c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@code", $"ZCUST-{Guid.NewGuid():N}".ToUpperInvariant()[..14]); });
        // issued_invoices.source_invoice_draft_id FKs to invoice_drafts — seed a minimal draft.
        var draftId = Guid.NewGuid();
        var draftNo = $"DRAFT-{number}";
        await db.ExecuteAsync(
            "INSERT INTO invoice_drafts (id, company_id, customer_id, invoice_draft_no) VALUES (@id, @cid, @cust, @no)",
            c => { c.Parameters.AddWithValue("@id", draftId); c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@cust", custId); c.Parameters.AddWithValue("@no", draftNo); });
        var id = Guid.NewGuid();
        await db.ExecuteAsync(
            @"INSERT INTO issued_invoices (id, company_id, customer_id, source_invoice_draft_id, source_invoice_draft_no, invoice_number, status, currency, subtotal, tax_total, total, amount_paid, balance_due, payment_status, issued_at)
              VALUES (@id, @cid, @cust, @draftId, @draftNo, @num, 'issued', 'SAR', @sub, @vat, @tot, 0, @tot, 'unpaid', NOW())",
            c =>
            {
                c.Parameters.AddWithValue("@id", id);
                c.Parameters.AddWithValue("@cid", companyId);
                c.Parameters.AddWithValue("@cust", custId);
                c.Parameters.AddWithValue("@draftId", draftId);
                c.Parameters.AddWithValue("@draftNo", draftNo);
                c.Parameters.AddWithValue("@num", number);
                c.Parameters.AddWithValue("@sub", sub);
                c.Parameters.AddWithValue("@vat", vat);
                c.Parameters.AddWithValue("@tot", total);
            });
        return id;
    }

    private static HashSet<int> ParseTlvTags(byte[] tlv)
    {
        var tags = new HashSet<int>();
        var i = 0;
        while (i + 2 <= tlv.Length)
        {
            int tag = tlv[i];
            int len = tlv[i + 1];
            tags.Add(tag);
            i += 2 + len;
        }
        return tags;
    }

    private static Database CreateDatabase()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = LocalConnectionString })
            .Build();
        return new Database(config);
    }
}
