# OpsTrax — Compliance Readiness Matrix

Target regimes: **SOC 2 Type II**, **GDPR** (EU), **PIPEDA** (Canada), **Saudi PDPL**
+ **ZATCA** e-invoicing, **ISO/IEC 27001**. Status as of 2026-07-02, grounded in the
actual codebase (not aspirational).

Legend: ✅ implemented · 🟡 partial · ❌ missing

## Control coverage

| Control area | Regimes | Status | Evidence / Gap |
|---|---|---|---|
| **Tenant data isolation** | SOC2, ISO, all privacy | ✅ | Postgres RLS actively enforced (opstrax_app role + per-request tenant scope). Coverage regression test. `OPSTRAX_TENANT_ISOLATION_FINDINGS.md`. |
| **Audit logging (who did what)** | SOC2 CC7, ISO A.12.4, all | ✅ | `audit_logs` + `platform_audit_log`; 510 `audit.LogAsync` call sites across mutations; tenant + platform trails. |
| **Access control / RBAC** | SOC2 CC6, ISO A.9 | ✅ | 16 tenant roles + 9 platform roles; authorization pipeline (permission→feature→usage→ownership); wildcard + semantic aliases; custom role creation. |
| **Password security** | SOC2 CC6, ISO A.9 | ✅ | PBKDF2-SHA256 (Rfc2898). `PasswordPolicyService`. No plaintext (demo_password migrates to hash on first login). |
| **Session management / expiry** | SOC2 CC6, ISO A.9 | ✅ | Tenant + platform sessions with `expires_at`; 8h TTL; CSRF double-submit. |
| **MFA** | SOC2 CC6, ISO A.9 | 🟡 | `mfa_enabled` column on platform_admins exists; enforcement flow not yet wired. Gap: implement TOTP challenge. |
| **Data retention** | GDPR Art.5, PDPL, PIPEDA | 🟡 | `data_retention_policies` table + `DataRetentionService` + `/api/compliance/retention`. Gap: verify automated purge job runs on schedule. |
| **Data export (subject access / portability)** | GDPR Art.15/20, PIPEDA, PDPL | 🟡 | `export_requests` + `/api/security/export-requests`, `/api/audit/export-requests`, finance/report exports. Gap: a single per-data-subject (driver/customer) export bundle. |
| **Right to erasure / anonymization** | GDPR Art.17, PDPL | ❌ | Soft-delete exists (`deleted_at`) but no PII anonymization/erasure endpoint for a data subject. **Primary privacy gap.** |
| **Consent tracking** | GDPR Art.6/7, PDPL | ❌ | No consent/privacy-processing tables. Needed if processing personal data on a consent basis. |
| **Access reviews (periodic)** | SOC2 CC6, ISO A.9.2 | ✅ | `access_reviews` + `access_review_items`. |
| **Encryption in transit** | all | ✅ (prod) | TLS at Render/Vercel edge; CSRF cookie Secure over HTTPS (scheme-aware). |
| **Encryption at rest** | all | ✅ (infra) | Neon Postgres encrypts at rest; verify + document in the SOC2 system description. |
| **Data residency** | PDPL (KSA), GDPR | 🟡 | Single Neon region today. Gap: KSA-resident deployment for the Saudi pilot (infra/deploy decision, not code). |
| **ZATCA Phase-2 e-invoicing** | Saudi | 🟡 | Foundation built: UBL 2.1 XML, SHA-256 hash, PIH chain, ICV, TLV/base64 QR, `IZatcaComplianceGateway` (stub gateway pending onboarding). Gap: real ZATCA onboarding/CSID. |
| **Security event monitoring** | SOC2 CC7, ISO A.12 | ✅ | `security_events` table + capture. |
| **Breach notification process** | GDPR Art.33, PDPL | ❌ | No documented process/runbook (operational, not code). |

## Priority gaps to close (code)
1. **Right to erasure endpoint** — per-data-subject anonymization (driver/customer PII →
   redacted, keeping referential integrity + audit record). GDPR Art.17 / PDPL. ← highest.
2. **Consent tracking table + API** — record consent grant/withdraw with timestamp + basis.
3. **MFA enforcement** — wire TOTP for platform admins (column already exists).
4. **Automated retention purge** — confirm/schedule the `DataRetentionService` job.

## Non-code (operational, for the audit)
- SOC2 system description + policies (change mgmt, incident response, vendor mgmt).
- Breach-notification runbook (GDPR 72h / PDPL).
- KSA data-residency deployment decision for the Saudi pilot.
- ZATCA production onboarding (CSID issuance).
