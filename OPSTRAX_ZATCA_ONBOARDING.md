# OpsTrax — ZATCA Phase-2 e-Invoicing: Status & Onboarding Checklist

## What is built (foundation — done, tested)
The structural + document layer that does NOT require ZATCA credentials:

- **`zatca_invoices` table** (`ZatcaSchemaService`) — per issued invoice: UBL XML, invoice
  hash, PIH, ICV, QR, clearance status, tenant-scoped (`company_id`, RLS-consistent).
- **UBL 2.1 invoice XML** (`ZatcaService.BuildUblXml`) — KSA subset: ProfileID, ID, UUID,
  IssueDate/Time, InvoiceTypeCode 388 (standard `0100000` / simplified `0200000`),
  currency, ICV + PIH additional-document-references, supplier/customer parties, TaxTotal,
  LegalMonetaryTotal.
- **Invoice hash** — SHA-256(base64) of the UBL document.
- **PIH chain** — each invoice stores the previous invoice's hash; the first invoice's PIH
  is `base64(SHA256("0"))` per the ZATCA spec. Verified across invoices by test.
- **ICV** — invoice counter value, strictly increasing per seller (unique index enforced).
- **QR (TLV/base64)** — tags 1–5 (seller, VAT #, timestamp, total, VAT). Tags 6–9
  (hash, ECDSA signature, public key, stamp signature) are added at the stamping step.
- **Endpoints:** `POST /api/finance/zatca/invoices/{issuedInvoiceId}/generate`,
  `GET /api/finance/zatca/invoices`.
- **Tests:** `ZatcaPostgresTests` (UBL parse, hash, QR TLV, PIH chain, ICV increment,
  idempotency, pending-onboarding status). Suite 886/886.

## What is NOT built — requires ZATCA (Fatoora) onboarding
Behind `IZatcaComplianceGateway` (currently `PendingOnboardingZatcaGateway`, which honestly
returns `stamped=false`, `clearance_status='pending_onboarding'`). To complete:

1. **ZATCA portal onboarding (Zack / KSA business):**
   - Register the seller in the Fatoora portal; obtain the **CSID** (Cryptographic Stamp
     Identifier) via CSR generation + OTP.
   - Provide the real **seller VAT number** (currently a placeholder in the QR builder).
   - Obtain sandbox + production API base URLs + credentials.
2. **Cryptographic stamp (ECDSA secp256k1):** sign the invoice hash with the CSID private
   key; embed signature + public key + stamp into QR tags 6–9 and the UBL `UBLExtensions`
   (XAdES signature). → implement in a real `IZatcaComplianceGateway.StampAsync`.
3. **Clearance / Reporting API:** standard invoices are **cleared** (synchronous) and
   simplified invoices **reported** (within 24h) via the Fatoora API. → implement
   `ClearOrReportAsync` (HTTP to the onboarded endpoints); store the response +
   `clearance_status` (`cleared`/`reported`/`rejected`).
4. **Real seller VAT # + address** wired from company/country-profile settings into the
   UBL parties and QR (replace the placeholder VAT number in `ZatcaService.BuildQrTlv`).

## Recommendation for the Saudi pilot
- Operational modules can pilot now.
- **Live invoicing in KSA must wait** for steps 1–3 (regulatory). Sequence: complete portal
  onboarding → implement the real gateway (stamp + clearance) → switch the DI registration
  from `PendingOnboardingZatcaGateway` to the real one → validate against the ZATCA sandbox
  → go live.

## Where to wire the real gateway
`Program.cs`: replace
`builder.Services.AddSingleton<IZatcaComplianceGateway, PendingOnboardingZatcaGateway>();`
with the real implementation once onboarding is complete. No other code changes needed —
`ZatcaService` already delegates stamping + clearance through the interface.
