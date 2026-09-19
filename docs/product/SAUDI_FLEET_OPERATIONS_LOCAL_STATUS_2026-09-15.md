# Saudi fleet operations — local implementation status

Date: 2026-09-15
Scope: local branch only; no deployment or pull request

## Operator workflow now implemented

The vehicle roster is the operating workspace for both company-owned and external
capacity. Every unit can record:

- fleet source: Owned, Rented, or Partner;
- rental/partner provider, contract number, and contract end date;
- body/equipment type, including flatbed, box truck, refrigerated, tanker,
  lowbed, and 20/40-foot container;
- payload capacity in kilograms and volume capacity in cubic metres;
- current assigned shipment payload and calculated utilization percentage;
- tracking provider and its external vehicle identity;
- assigned driver and recorded Iqama/work-authorization evidence;
- TAMM verification status and last verification timestamp.

The compact roster exposes these fields without adding another dashboard. The
vehicle drawer keeps status/behaviour, device installation/configuration, and
maintenance/history as operator tasks. A rented Saudi flatbed with a Locus mapping
was created successfully in the isolated local rehearsal database to prove the
create path and list projection.

## Scheduling and return-load support

The scheduler can distinguish vehicle ownership, body type, weight capacity, and
volume capacity. Active shipment weight and volume are projected into the roster so
an operator can see unused capacity or an overload signal from recorded data.

The vehicle drawer now checks return-load candidates. The first safe matching rule
is deliberately strict:

1. the vehicle must have a current Loaded, PickedUp, or InTransit shipment;
2. the current destination must exactly match an unassigned Booked/Planned load's
   origin after case/space normalization;
3. the candidate must fit recorded kg and m³ capacity;
4. company and permitted branch scope must match.

The result is a decision aid. It does not auto-book capacity, invent mileage, or
expose loads across customers. A future routing adapter may add distance/time
feasibility after a routing provider is contracted.

## External connectors and activation boundaries

| Capability | Local state | What activates live use |
|---|---|---|
| Locus | Catalog entry and per-vehicle external-ID mapping implemented | Locus client access, Client ID/authentication details, webhook/API contract, and a provider handshake |
| TAMM | Catalog entry and per-vehicle verification state implemented | Elm/TAMM subscription, authorized service scope, credentials, consent and audit requirements |
| Saudi payment gateway | Catalog entry implemented; existing invoices and payment ledger remain the accounting authority | Contract with a SAMA-licensed payment service provider, Saudi Payments approvals where applicable, hosted/embedded checkout keys, signed webhook secrets, reconciliation and refund rules |

Catalog presence never means a connector is live. The UI must show Not configured,
Pending verification, Connected, or Failed only from persisted evidence. Secrets
must use the encrypted connector-credential path and must never be stored on vehicle
records or in the browser.

The current executable boundary is stricter than the catalog description:

- Locus has no provider adapter in this build. Operators may record a provider and
  external vehicle identifier as a mapping, but OpsTrax does not accept Locus
  credentials, run a handshake, or treat that mapping as live telemetry evidence.
- Elm/TAMM has no provider adapter in this build. Its status stays Not configured;
  vehicle records do not expose a field that lets a tenant self-assert verification.
- TAMM and the Saudi payment-provider catalog entry are market-locked to Saudi
  tenants. Other markets cannot discover them or address stale rows directly.
- A real adapter must provide a successful tenant-specific handshake before its
  status can become Connected. External certification and authority approval remain
  outside the repository and are not claimed here.

## Payment architecture

The intended embedded payment flow is:

1. OpsTrax creates an invoice and immutable amount/currency reference.
2. The backend creates a payment session with the licensed PSP; the browser receives
   only a short-lived hosted/embedded checkout token.
3. The PSP collects card/mada/SADAD-compatible payment details. OpsTrax does not
   store raw card data.
4. A signed, idempotent webhook records the PSP transaction reference.
5. Payment application is capped at invoice balance, duplicate references are
   rejected, and the accounting ledger reconciles settlement separately.
6. Refunds and chargebacks are new ledger events rather than edits to historical
   receipts.

The current customer-payment endpoint already rejects duplicate, foreign-currency,
cross-tenant, and above-balance entries. Real collection remains disabled until a
licensed provider is selected and credentials are supplied.

## Saudi evidence model

Iqama and driver-to-vehicle assignment are treated as recorded evidence with expiry
and source, not as an unsupported government claim. TAMM can become an authoritative
verification source only after the contracted API returns a successful response.
Until then the roster correctly displays Missing or Not configured.

Public product references used for the integration boundary:

- Locus API reference: https://locus.sh/resources/api-references/
- Elm transportation products/TAMM: https://elm.sa/en/our-business/digital-products/Pages/transportation-products.aspx
- SAMA payment-services regulations: https://www.rulebook.sama.gov.sa/en/implementing-regulations-payments-and-payment-services-law

## Verification completed locally

- migration applied to the isolated `opstrax_poc_journey` PostgreSQL database;
- backend build passed with zero errors;
- frontend lint passed;
- frontend production build passed;
- local API `/health/ready` returned Ready;
- vehicle roster loaded and projected Owned/Rented, capacity, utilization,
  Iqama, tracking provider, and TAMM status;
- vehicle device tab opened the installation/configuration workflow;
- owned-vehicle edit saved body type, Saudi jurisdiction, kg and m³ capacity;
- rented-vehicle create saved provider, contract, end date, flatbed capacity,
  Locus mapping, and alternate government identity;
- return-load endpoint was exercised from the UI. A branchless-scope parameter defect
  found during this test was corrected and the empty result state then passed.

## Developer findings reconciled in this tranche

- Findings 1–8 remain verified resolved, including maintenance tenant isolation,
  dispatch concurrency, and payment idempotency/balance controls.
- Findings 9–12, 33–35, and 69 are corrected by removal: deployment and reference
  checks confirmed the old Node API was unused, so the prototype and its obsolete CI
  job were deleted. `backend-dotnet/` remains the only active API.
- Findings 18, 19, 23, 24, 25, and 26 are corrected: safety-summary naming,
  pickup conflict handling, atomic service entry, RFID identity matching, safe
  market-pack JSON serialization, and post-commit DVIR availability reconciliation.
- Finding 62 is resolved locally: supported providers require recent accepted recovery
  evidence plus exact provider identity; unsupported providers remain preview-only. Live
  provider activation and acceptance tests still require provider credentials.
- The remaining open developer findings are tracked in
  `docs/security/DEV_FINDINGS_REVALIDATION_2026-09-15.md`; they are not silently
  represented as completed by this fleet tranche.
