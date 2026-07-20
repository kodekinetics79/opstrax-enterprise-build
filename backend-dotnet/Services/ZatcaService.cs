using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

// ─────────────────────────────────────────────────────────────────────────────
// ZATCA Phase-2 foundation service. Produces the compliant e-invoice artifacts from
// an issued_invoices row: UBL 2.1 XML, SHA-256 invoice hash, PIH chain, ICV, and the
// TLV/base64 QR. The cryptographic stamp + live clearance are delegated to
// IZatcaComplianceGateway (stubbed until ZATCA onboarding). No external calls here.
// ─────────────────────────────────────────────────────────────────────────────

public sealed record ZatcaGenerationResult(
    long ZatcaInvoiceId, string InvoiceNumber, Guid Uuid, long Icv,
    string InvoiceHash, string? Pih, string QrBase64, string ClearanceStatus, string UblXml);

// Boundary for the parts that REQUIRE ZATCA onboarding (CSID cert + Fatoora API).
// The stub records intent; the real implementation is wired once onboarding provides
// the cryptographic stamp identity and sandbox/production endpoints.
public interface IZatcaComplianceGateway
{
    // Applies the ECDSA cryptographic stamp (Phase-2). Stub returns unstamped.
    Task<(bool Stamped, string? SignedXml)> StampAsync(string ublXml, CancellationToken ct = default);
    // Submits to the Fatoora clearance (standard) / reporting (simplified) API.
    Task<(string Status, string? ResponseJson)> ClearOrReportAsync(string signedXml, string invoiceType, CancellationToken ct = default);
}

// Default no-op gateway: honest 'pending_onboarding'. Documented boundary — replace with
// a real gateway once ZATCA CSID onboarding is complete (see OPSTRAX_ZATCA_ONBOARDING.md).
public sealed class PendingOnboardingZatcaGateway : IZatcaComplianceGateway
{
    public Task<(bool, string?)> StampAsync(string ublXml, CancellationToken ct = default)
        => Task.FromResult((false, (string?)null));
    public Task<(string, string?)> ClearOrReportAsync(string signedXml, string invoiceType, CancellationToken ct = default)
        => Task.FromResult(("pending_onboarding", (string?)null));
}

public sealed class ZatcaService(Database db, IZatcaComplianceGateway gateway)
{
    private static readonly XNamespace Inv = "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2";
    private static readonly XNamespace Cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";
    private static readonly XNamespace Cac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";

    // Generate (or return existing) the ZATCA e-invoice for an issued invoice.
    public async Task<ZatcaGenerationResult?> GenerateForIssuedInvoiceAsync(
        long companyId, Guid issuedInvoiceId, string invoiceType, CancellationToken ct = default)
    {
        var inv = await db.QuerySingleAsync(
            @"SELECT ii.id, ii.invoice_number, ii.currency, ii.subtotal, ii.tax_total, ii.total,
                     ii.issued_at, ii.due_at, c.name customer_name,
                     co.name seller_name
              FROM issued_invoices ii
              JOIN companies co ON co.id = ii.company_id
              LEFT JOIN customers c ON c.id = ii.customer_id AND c.company_id = ii.company_id
              WHERE ii.company_id=@cid AND ii.id=@id",
            b => { b.Parameters.AddWithValue("@cid", companyId); b.Parameters.AddWithValue("@id", issuedInvoiceId); }, ct);
        if (inv is null) return null;

        // Idempotent: if already generated, return it.
        var existing = await db.QuerySingleAsync(
            "SELECT id, invoice_number, uuid, icv, invoice_hash, pih, qr_base64, clearance_status, ubl_xml FROM zatca_invoices WHERE company_id=@cid AND issued_invoice_id=@id",
            b => { b.Parameters.AddWithValue("@cid", companyId); b.Parameters.AddWithValue("@id", issuedInvoiceId); }, ct);
        if (existing is not null)
            return new ZatcaGenerationResult(
                Convert.ToInt64(existing["id"]), existing["invoiceNumber"]?.ToString() ?? "",
                Guid.Parse(existing["uuid"]!.ToString()!), Convert.ToInt64(existing["icv"]),
                existing["invoiceHash"]?.ToString() ?? "", existing["pih"]?.ToString(),
                existing["qrBase64"]?.ToString() ?? "", existing["clearanceStatus"]?.ToString() ?? "",
                existing["ublXml"]?.ToString() ?? "");

        var invoiceNumber = inv["invoiceNumber"]?.ToString() ?? "";
        var currency = inv["currency"]?.ToString() ?? "SAR";
        var subtotal = Dec(inv["subtotal"]);
        var vatTotal = Dec(inv["taxTotal"]);
        var total = Dec(inv["total"]);
        var sellerName = inv["sellerName"]?.ToString() ?? "Seller";
        var customerName = inv["customerName"]?.ToString() ?? "Customer";
        var issuedAt = inv["issuedAt"] is DateTime dt ? dt : DateTime.UtcNow;
        var invoiceUuid = Guid.NewGuid();

        // ICV = strictly increasing per seller; PIH = hash of the previous invoice (chain).
        var prev = await db.QuerySingleAsync(
            "SELECT icv, invoice_hash FROM zatca_invoices WHERE company_id=@cid ORDER BY icv DESC LIMIT 1",
            b => b.Parameters.AddWithValue("@cid", companyId), ct);
        var icv = prev is null ? 1 : Convert.ToInt64(prev["icv"]) + 1;
        // Phase-2: the first invoice's PIH is the base64 of SHA-256("0").
        var pih = prev is null ? Base64Sha256("0") : prev["invoiceHash"]?.ToString();

        var ubl = BuildUblXml(invoiceUuid, invoiceNumber, invoiceType, issuedAt, currency,
            sellerName, customerName, subtotal, vatTotal, total, icv, pih);
        var ublXml = ubl.ToString(SaveOptions.DisableFormatting);
        var invoiceHash = Base64Sha256(ublXml);
        var qrBase64 = BuildQrTlv(sellerName, /*vatNumber*/ "300000000000003", issuedAt, total, vatTotal, invoiceHash);

        // Onboarding boundary: stamp + clearance (stub -> pending_onboarding).
        var (stamped, signedXml) = await gateway.StampAsync(ublXml, ct);
        var (clearanceStatus, clearanceJson) = await gateway.ClearOrReportAsync(signedXml ?? ublXml, invoiceType, ct);

        var newId = await db.InsertAsync(
            @"INSERT INTO zatca_invoices
                (company_id, issued_invoice_id, invoice_number, invoice_type, uuid, icv, pih, invoice_hash,
                 qr_base64, ubl_xml, currency, subtotal, vat_total, total, clearance_status, clearance_response_json, stamped)
              VALUES (@cid, @iid, @num, @itype, @uuid, @icv, @pih, @hash, @qr, @xml, @cur, @sub, @vat, @tot,
                      @cstatus, CAST(@cjson AS JSONB), @stamped)",
            b =>
            {
                b.Parameters.AddWithValue("@cid", companyId);
                b.Parameters.AddWithValue("@iid", issuedInvoiceId);
                b.Parameters.AddWithValue("@num", invoiceNumber);
                b.Parameters.AddWithValue("@itype", invoiceType);
                b.Parameters.AddWithValue("@uuid", invoiceUuid);
                b.Parameters.AddWithValue("@icv", icv);
                b.Parameters.AddWithValue("@pih", (object?)pih ?? DBNull.Value);
                b.Parameters.AddWithValue("@hash", invoiceHash);
                b.Parameters.AddWithValue("@qr", qrBase64);
                b.Parameters.AddWithValue("@xml", ublXml);
                b.Parameters.AddWithValue("@cur", currency);
                b.Parameters.AddWithValue("@sub", subtotal);
                b.Parameters.AddWithValue("@vat", vatTotal);
                b.Parameters.AddWithValue("@tot", total);
                b.Parameters.AddWithValue("@cstatus", clearanceStatus);
                b.Parameters.AddWithValue("@cjson", (object?)clearanceJson ?? DBNull.Value);
                b.Parameters.AddWithValue("@stamped", stamped);
            }, ct);

        return new ZatcaGenerationResult(newId, invoiceNumber, invoiceUuid, icv, invoiceHash, pih, qrBase64, clearanceStatus, ublXml);
    }

    public Task<List<Dictionary<string, object?>>> ListAsync(long companyId, CancellationToken ct = default)
        => db.QueryAsync(
            @"SELECT id, issued_invoice_id, invoice_number, invoice_type, uuid, icv, invoice_hash, pih,
                     clearance_status, stamped, currency, subtotal, vat_total, total, created_at
              FROM zatca_invoices WHERE company_id=@cid ORDER BY icv DESC LIMIT 200",
            b => b.Parameters.AddWithValue("@cid", companyId), ct);

    // ── UBL 2.1 invoice document (KSA subset) ──────────────────────────────────
    private static XElement BuildUblXml(Guid uuid, string invoiceNumber, string invoiceType, DateTime issued,
        string currency, string seller, string customer, decimal subtotal, decimal vat, decimal total, long icv, string? pih)
    {
        // InvoiceTypeCode: 388 = tax invoice. name subtype: "0100000" standard / "0200000" simplified.
        var typeName = invoiceType.Equals("simplified", StringComparison.OrdinalIgnoreCase) ? "0200000" : "0100000";
        return new XElement(Inv + "Invoice",
            new XAttribute(XNamespace.Xmlns + "cbc", Cbc.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "cac", Cac.NamespaceName),
            new XElement(Cbc + "ProfileID", "reporting:1.0"),
            new XElement(Cbc + "ID", invoiceNumber),
            new XElement(Cbc + "UUID", uuid.ToString()),
            new XElement(Cbc + "IssueDate", issued.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            new XElement(Cbc + "IssueTime", issued.ToString("HH:mm:ss", CultureInfo.InvariantCulture)),
            new XElement(Cbc + "InvoiceTypeCode", new XAttribute("name", typeName), "388"),
            new XElement(Cbc + "DocumentCurrencyCode", currency),
            new XElement(Cbc + "TaxCurrencyCode", currency),
            // Additional document references: ICV + PIH (Phase-2 chain).
            new XElement(Cac + "AdditionalDocumentReference",
                new XElement(Cbc + "ID", "ICV"),
                new XElement(Cbc + "UUID", icv.ToString(CultureInfo.InvariantCulture))),
            new XElement(Cac + "AdditionalDocumentReference",
                new XElement(Cbc + "ID", "PIH"),
                new XElement(Cac + "Attachment",
                    new XElement(Cbc + "EmbeddedDocumentBinaryObject", new XAttribute("mimeCode", "text/plain"), pih ?? ""))),
            new XElement(Cac + "AccountingSupplierParty",
                new XElement(Cac + "Party",
                    new XElement(Cac + "PartyLegalEntity", new XElement(Cbc + "RegistrationName", seller)))),
            new XElement(Cac + "AccountingCustomerParty",
                new XElement(Cac + "Party",
                    new XElement(Cac + "PartyLegalEntity", new XElement(Cbc + "RegistrationName", customer)))),
            new XElement(Cac + "TaxTotal",
                new XElement(Cbc + "TaxAmount", new XAttribute("currencyID", currency), Money(vat))),
            new XElement(Cac + "LegalMonetaryTotal",
                new XElement(Cbc + "LineExtensionAmount", new XAttribute("currencyID", currency), Money(subtotal)),
                new XElement(Cbc + "TaxExclusiveAmount", new XAttribute("currencyID", currency), Money(subtotal)),
                new XElement(Cbc + "TaxInclusiveAmount", new XAttribute("currencyID", currency), Money(total)),
                new XElement(Cbc + "PayableAmount", new XAttribute("currencyID", currency), Money(total))));
    }

    // ── ZATCA QR: base64 of TLV (tags 1..5): seller, VAT#, timestamp, total, VAT ──
    private static string BuildQrTlv(string seller, string vatNumber, DateTime ts, decimal total, decimal vat, string invoiceHash)
    {
        static byte[] Tlv(byte tag, string val)
        {
            var v = Encoding.UTF8.GetBytes(val);
            var buf = new byte[2 + v.Length];
            buf[0] = tag; buf[1] = (byte)v.Length;
            Array.Copy(v, 0, buf, 2, v.Length);
            return buf;
        }
        using var ms = new MemoryStream();
        void Write(byte[] b) => ms.Write(b, 0, b.Length);
        Write(Tlv(1, seller));
        Write(Tlv(2, vatNumber));
        Write(Tlv(3, ts.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)));
        Write(Tlv(4, Money(total)));
        Write(Tlv(5, Money(vat)));
        return Convert.ToBase64String(ms.ToArray());
    }

    private static string Base64Sha256(string input)
        => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(input)));

    private static string Money(decimal d) => d.ToString("0.00", CultureInfo.InvariantCulture);
    private static decimal Dec(object? o) => o is null or DBNull ? 0m : Convert.ToDecimal(o);
}
