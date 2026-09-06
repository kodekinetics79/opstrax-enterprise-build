# OpsTrax Mobile Product Family v1

## CTO decision

OpsTrax mobile is a product family on one shared platform and codebase:

- **OpsTrax Driver** — field execution for drivers.
- **OpsTrax Fleet** — dispatcher, fleet manager, operations supervisor, and tenant-admin mobile command.
- **OpsTrax Customer** — customer / shipper / consignee visibility, documents, billing, and service workflows.
- **OpsTrax Command** — existing web enterprise control plane; platform-super-admin remains web-only in v1.

The mobile applications share auth, tenant context, API contracts, security primitives, design system, networking, and domain logic. They ship as separate iOS bundle IDs / Android package IDs and enforce product-role boundaries after authentication.

## Current foundation being preserved

The existing `mobile/` Expo application already provides secure session storage, MFA-aware authentication, tenant-bound backend sessions, permission-aware navigation, Driver screens, proof upload, location/network dependencies, and operational screens. This work is reused rather than replaced.

The existing authenticated customer portal backend is also reused. Customer mobile must use `/api/portal/*`, whose backend enforces both `company_id` and authenticated `customer_id`; customer mobile must not substitute generic internal `/api/jobs` access.

## Production identities

| Product | Expo variant | iOS bundle / Android package |
|---|---|---|
| OpsTrax Driver | `driver` | `com.opstrax.driver` |
| OpsTrax Fleet | `fleet` | `com.opstrax.fleet` |
| OpsTrax Customer | `customer` | `com.opstrax.customer` |

`unified` exists only as a non-production compatibility/development mode. Production config must fail closed if a product variant is not explicitly selected.

## Product access rules

### Driver

A Driver binary accepts a session only when the backend session is driver-scoped (`driver:self`) and not a broad dashboard/admin session.

### Customer

A Customer binary accepts a session only when:

1. the role classifies as customer/client; and
2. the session carries `customer_portal:view`.

This is a UI/product gate only. The `/api/portal/*` backend remains the authoritative customer ownership boundary.

### Fleet

Fleet accepts authorized internal tenant operational roles but excludes:

- driver-only sessions;
- customer portal sessions; and
- platform-super-admin sessions.

Platform super administration remains in OpsTrax Command (web) for v1.

## Build sequence

### M0 — Product boundary and build foundation

Acceptance:
- three explicit app variants and bundle/package IDs;
- product-specific session gate;
- secure-storage keys separated by app variant;
- production build fails if variant is ambiguous;
- no customer device-location permission unless a future reviewed feature truly requires it.

### M1 — OpsTrax Customer first-class experience

Acceptance:
- customer home;
- own shipments and ETA/status;
- shipment detail;
- POD/proof gallery;
- invoices and invoice detail;
- feedback/support request;
- customer-only API family;
- zero internal fields or cross-customer records exposed.

### M2 — OpsTrax Driver hardening

Acceptance:
- assignment lifecycle and stop workflow;
- vehicle confirmation;
- POD photo/signature;
- DVIR / incident / delay / fuel flows as supported by backend;
- idempotent offline queue for critical actions;
- weak-network recovery;
- active-trip-only location policy;
- push assignment/change notifications;
- no ELD compliance claim until the separate certification program passes.

### M3 — OpsTrax Fleet hardening

Acceptance:
- live fleet summary/map;
- trips, delayed work, exceptions, driver/vehicle state;
- approved mobile actions only;
- tenant-admin lite;
- no platform-admin control plane.

### M4 — Security and client-style SDET gate

Required evidence:
- customer A cannot access customer B data inside the same tenant;
- tenant A cannot access tenant B;
- driver cannot access another driver's assignment;
- role/app mismatch is blocked;
- IDOR/BOLA tests;
- expired/revoked session tests;
- offline duplicate-submit tests;
- physical iPhone and Android tests;
- poor-network/background/terminated-app scenarios;
- real seeded large-fleet data and client-style browser/device evidence.

### M5 — Store readiness

Required before public submission:
- privacy policy / terms / support URLs;
- in-app account deletion/request path where applicable;
- App Store privacy disclosures and Play Data Safety declarations;
- least-privilege permission copy;
- store icons/screenshots/descriptions;
- reviewer tenant and credentials;
- crash/error telemetry without sensitive token logging;
- version/build strategy;
- TestFlight and Google Play internal/closed testing packages.

### M6 — Pilot and public release

1. Internal TestFlight + Google internal testing.
2. Closed pilot with 2–3 real tenants.
3. Driver/customer field workflow acceptance.
4. Security/regression re-certification against release SHA.
5. Public US/Canada/KSA rollout only for capabilities and compliance claims actually supported in each market.

## Commercial truth rule

A store listing may describe only features proven against the release build. HOS/ELD, certified hardware, regulated safety, or country-compliance claims remain gated by their independent certification evidence and may not be inferred from the presence of mobile screens.
