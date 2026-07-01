using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

// ─────────────────────────────────────────────────────────────────────────────
// ZATCA (Saudi e-invoicing, "Fatoora") Phase-2 — FOUNDATION schema.
//
// Records, per issued_invoice, the compliant e-invoice artifacts we CAN produce
// without ZATCA-portal onboarding:
//   - UBL 2.1 invoice XML
//   - the SHA-256 invoice hash (base64) and the PIH (Previous Invoice Hash) that
//     chains each invoice to the prior one per seller/tenant (Phase-2 requirement)
//   - the TLV/base64 QR payload
//   - clearance/reporting status fields, left in 'pending_onboarding' until the
//     cryptographic stamp (CSID) + ZATCA clearance API are wired during onboarding.
//
// This is additive + tenant-scoped (company_id, RLS-consistent). It does NOT itself
// call ZATCA; the crypto stamp + live clearance are behind IZatcaComplianceGateway,
// which is a documented stub until onboarding provides the CSID/credentials.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class ZatcaSchemaService(Database db)
{
    public async Task EnsureAsync()
    {
        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS zatca_invoices (
                id                 BIGINT       GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                company_id         BIGINT       NOT NULL REFERENCES companies(id),
                issued_invoice_id  UUID         NOT NULL,
                invoice_number     VARCHAR(60)  NOT NULL,
                invoice_type       VARCHAR(20)  NOT NULL DEFAULT 'standard',   -- standard | simplified
                uuid               UUID         NOT NULL,                       -- invoice UUID (cbc:UUID)
                icv                BIGINT       NOT NULL,                       -- invoice counter value (per seller)
                pih                VARCHAR(120) NULL,                           -- previous invoice hash (base64)
                invoice_hash       VARCHAR(120) NOT NULL,                       -- SHA-256(base64) of this invoice
                qr_base64          TEXT         NOT NULL,                       -- TLV base64 QR payload
                ubl_xml            TEXT         NOT NULL,                        -- UBL 2.1 invoice XML
                currency           VARCHAR(8)   NOT NULL DEFAULT 'SAR',
                subtotal           NUMERIC(18,2) NOT NULL DEFAULT 0,
                vat_total          NUMERIC(18,2) NOT NULL DEFAULT 0,
                total              NUMERIC(18,2) NOT NULL DEFAULT 0,
                -- Clearance / reporting (Phase-2). Filled during ZATCA onboarding.
                clearance_status   VARCHAR(30)  NOT NULL DEFAULT 'pending_onboarding',
                                   -- pending_onboarding | cleared | reported | rejected
                clearance_response_json JSONB   NULL,
                stamped            BOOLEAN      NOT NULL DEFAULT false,          -- cryptographic stamp applied?
                created_at         TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
                updated_at         TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
                UNIQUE (company_id, issued_invoice_id)
            )
            """);
        await db.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_zatca_company ON zatca_invoices (company_id)");
        // ICV must be strictly increasing per seller — enforce uniqueness of the counter.
        await db.ExecuteAsync("CREATE UNIQUE INDEX IF NOT EXISTS uq_zatca_company_icv ON zatca_invoices (company_id, icv)");
    }
}
