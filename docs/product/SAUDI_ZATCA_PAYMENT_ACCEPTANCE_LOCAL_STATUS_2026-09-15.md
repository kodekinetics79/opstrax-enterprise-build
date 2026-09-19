# Saudi ZATCA and Payment Acceptance — Local Status

Date: 2026-09-15
Branch: `redesign/customer-finance-workspaces-local`
Release status: local review only; not pushed

## Client answer

OpsTrax is designed to manage the invoice-to-payment journey inside the product. For Saudi Arabia, live operation must connect two regulated external boundaries:

1. ZATCA onboarding and credentials for e-invoice clearance/reporting.
2. A contracted Saudi payment service provider for hosted payment acceptance, settlement, refunds, and disputes.

The current local build provides the internal invoice, tax, readiness, payment-ledger, and reconciliation foundation. It must not be presented as live ZATCA clearance or live online payment acceptance until the selected production credentials and providers pass end-to-end certification.

## What works in the local build

- Saudi seller VAT registration and published VAT-profile readiness checks.
- Issued-invoice and immutable tax snapshot records.
- Local ZATCA preparation artifacts with UUID, ICV, invoice hash, PIH chain, UBL XML, and a base QR payload.
- Per-seller serialization of ICV and PIH generation to prevent concurrent chain races.
- Honest `pending_onboarding` status while the live ZATCA gateway is absent.
- Customer invoice payment ledger for funds already confirmed by an external source.
- Duplicate-reference, wrong-currency, foreign-invoice, and overpayment rejection.
- Atomic invoice draft creation and atomic driver-settlement payment recording.
- A compact Saudi Finance Readiness panel on the Invoices and Payments workspaces.

## Local activation gates added in this review

- ZATCA API and service operations now reject any tenant whose Platform Admin market
  is not Saudi Arabia. A caller cannot activate the workflow by editing invoice data.
- A registered gateway implementation is no longer treated as proof that ZATCA is
  live. Credentials configured, certification verified, and live submission enabled
  are three separate provider-evidence gates.
- Invoice XML is not sent to an external gateway until all three gates are true.
  A partially configured adapter remains in its explicit provider state, such as
  `pending_onboarding` or `certification_pending`.
- Payment readiness ignores a persisted catalog label such as `Connected`. Because
  this build has no licensed PSP adapter or verified signed webhook path, acceptance
  and webhook verification remain false and the UI/API report provider onboarding as
  required.
- The TAMM and Saudi Payment Gateway catalog entries are visible only to tenants
  locked to Saudi Arabia. Direct requests against stale rows from another market are
  rejected. Catalog entries still cannot accept credentials without a real adapter.

## What is still required for live ZATCA operation

- Complete ZATCA taxpayer/device onboarding and obtain production CSIDs.
- Implement signing, cryptographic stamp, and the production clearance/reporting adapter.
- Complete the required Phase 2 invoice and QR data, then validate generated documents against the applicable ZATCA specifications and test services.
- Store credentials and certificates in managed secrets with rotation and audit controls.
- Implement submission retry, rejection handling, immutable request/response evidence, and operational monitoring.
- Run standard and simplified invoice scenarios end to end before enabling the feature for a tenant.

## What is still required for payment acceptance

- Select and contract with a licensed payment service provider serving Saudi merchants.
- Confirm the required methods for the client, such as mada, cards, Apple Pay, bank transfer, or SADAD, based on the provider contract.
- Use provider-hosted checkout or tokenized components so OpsTrax does not store raw card details.
- Add payment-link or checkout initiation to an issued invoice.
- Treat a verified, signed provider webhook as the authority for payment success.
- Make webhook handling idempotent and reconcile provider settlement against invoice and ledger records.
- Add refund, failed-payment, chargeback/dispute, expiry, and cancellation states.
- Complete sandbox and live acceptance tests with the selected provider.

## Intended production journey

1. OpsTrax issues the invoice and records its Saudi tax snapshot.
2. The ZATCA adapter signs and clears or reports the invoice according to its invoice type.
3. OpsTrax shows the authoritative ZATCA state and stores submission evidence.
4. The customer opens a hosted payment link from the invoice.
5. The licensed provider collects the payment method and completes authentication.
6. OpsTrax receives and verifies the provider webhook.
7. An idempotent ledger update marks the invoice partially or fully paid.
8. Finance reconciles the provider settlement, fees, refunds, and disputes.

## Demo language

Use: “OpsTrax contains the Saudi invoice and payment operating workflow. ZATCA submission and online collection activate through the customer’s onboarded ZATCA identity and licensed payment provider.”

Avoid: “OpsTrax is already ZATCA certified,” “ZATCA is live,” or “OpsTrax processes cards” until the corresponding production integration has been contracted, configured, and verified.

## Verification completed locally

- .NET build: passed.
- Frontend lint and production build: passed.
- ZATCA PostgreSQL coverage now includes non-Saudi rejection and a credentialed but
  uncertified gateway that receives zero invoice calls.
- Integration catalog coverage verifies TAMM and Saudi payment visibility for Saudi
  tenants and exclusion for US or unset markets; Locus remains globally discoverable
  while tenant records and provider evidence stay tenant-scoped.
- Focused external-readiness suite: 23 passed, 0 failed (including database-backed
  ZATCA and two-tenant catalog hydration checks).
- Settlement PostgreSQL tests: 7 passed.
- Revenue-readiness PostgreSQL tests: 29 passed.
- Total focused database-backed checks: 39 passed, 0 failed.
