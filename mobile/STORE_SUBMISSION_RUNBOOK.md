# OpsTrax Mobile — App Store & Google Play Submission Runbook

Governing products from one shared Expo/React Native codebase:

| Product | iOS bundle ID / Android package | EAS preview | EAS store |
|---|---|---|---|
| OpsTrax Driver | `com.kodekinetics.opstrax.driver` | `driver-preview` | `driver-production` |
| OpsTrax Fleet | `com.kodekinetics.opstrax.fleet` | `fleet-preview` | `fleet-production` |
| OpsTrax Customer | `com.kodekinetics.opstrax.customer` | `customer-preview` | `customer-production` |

## Current platform baseline

- Expo SDK 56 / React Native 0.85.
- Expo SDK 56 targets Android API 36.
- Production Android profiles build AABs.
- Production profiles use store distribution and auto-increment versions.
- Production builds fail closed if the API URL is not HTTPS/allow-listed.
- Production builds fail closed unless public HTTPS Privacy and Support URLs are configured.
- Production builds cannot use the unified role binary.
- If mobile self-service account creation is enabled, a public HTTPS account deletion URL becomes mandatory.

## Required EAS production environment values

Set these in the EAS `production` environment before any store build:

- `EXPO_PUBLIC_API_BASE_URL`
- `EXPO_PUBLIC_ALLOWED_API_HOSTS`
- `EXPO_PUBLIC_PRIVACY_URL`
- `EXPO_PUBLIC_SUPPORT_URL`
- `EXPO_PUBLIC_ACCOUNT_CREATION_ENABLED=false` unless self-service registration is actually implemented

Do not set product/package IDs globally if using the repo defaults. Product is injected by each EAS build profile.

If legal/deployment requires custom identifiers, use the product-specific build environment and set:
- `EXPO_PUBLIC_IOS_BUNDLE_ID`
- `EXPO_PUBLIC_ANDROID_PACKAGE`

If account creation is later enabled:
- `EXPO_PUBLIC_ACCOUNT_DELETION_URL`

## Gate 1 — Repository candidate

Before native builds:

1. Candidate SHA is fixed and reviewed.
2. `npm ci` succeeds in `mobile/`.
3. `npm run typecheck` succeeds.
4. `npm run lint` succeeds.
5. `npm run test:audit-policy` succeeds.
6. `npm run test:contracts` succeeds.
7. No-inbound-linking/native-generation check succeeds.
8. Expo dependency compatibility check succeeds.
9. Mobile export succeeds.
10. Full repository CI is green.
11. Store privacy inventory is re-audited against the exact candidate SHA.

## Gate 2 — Internal device builds

Build all three role products; do not test only the unified dev binary.

Android internal APKs:

```bash
eas build --platform android --profile driver-preview
eas build --platform android --profile fleet-preview
eas build --platform android --profile customer-preview
```

iOS internal/ad-hoc builds may require registered test devices. For TestFlight, use the production store profiles instead.

Minimum physical-device matrix:
- current iPhone generation
- one older supported iPhone running the minimum supported iOS line where practical
- current Pixel/Samsung-class Android device
- lower/mid-range Android device
- phone with poor/intermittent connectivity

Test each product with real role credentials and tenant-scoped data.

## Gate 3 — Mandatory client-style journeys

### Driver
- login/MFA if enabled
- assignment visibility
- correct vehicle confirmation
- route launch
- status transition
- exception draft recovery
- POD photo upload + durable-reference recovery
- proof submission
- DVIR completion + retry using the same idempotency key
- HOS truth state when certified/usable data is absent
- operational inbox/read state
- offline banner / no fabricated successful mutation
- secure logout

### Fleet
- login
- operations dashboard
- workflow/dispatch permissions
- proof review permissions
- fleet/telemetry visibility
- safety/maintenance visibility
- operational inbox/read state
- cross-role access denial
- secure logout

### Customer
- login
- customer-owned shipment list/detail/timeline/proofs
- invoice detail/taxes/payments
- support request + draft recovery
- support history
- operational inbox/read state
- attempts to access another customer must fail without revealing existence
- secure logout

## Gate 4 — Store privacy and permissions

Reconcile `STORE_PRIVACY_INVENTORY.md` against the exact binary.

Current intended mobile permission posture:
- Camera: contextual Driver proof/inspection evidence only.
- Photos: contextual evidence selection where supported.
- Microphone: disabled for image capture.
- Location: foreground only for proof submission; denial must not block the proof workflow.
- Background location: not currently implemented; must not be declared or requested until separately approved.
- Tracking/advertising identifier: not currently implemented.
- Native push: not yet implemented in the current candidate; update disclosures when device tokens are added.

## Gate 5 — Google Play

1. Developer/organization identity verified in Play Console.
2. Create three app records with exact production package names.
3. Confirm Android developer/package registration status.
4. Complete App access information and provide review credentials/instructions.
5. Complete Data Safety from the candidate privacy inventory and actual backend processors.
6. Enter the public Privacy Policy URL.
7. If account creation exists, enter the external account-deletion web resource and verify the in-app deletion path.
8. Complete content rating, target audience, ads declaration and any required declarations.
9. Upload signed AABs from the exact candidate SHA.
10. Release to Internal Testing first.
11. Promote to Closed Testing / pilot tenants after evidence capture.
12. Public production only after CTO release verdict.

Current Play target requirement: new standard mobile apps and app updates submitted after August 31, 2026 must target Android 16 / API 36 or higher. Expo SDK 56 currently meets API 36.

## Gate 6 — Apple App Store

1. Active Apple Developer Program organization/account.
2. Create three App Store Connect records with exact bundle IDs.
3. Configure app privacy disclosures from the candidate privacy inventory and backend data flow.
4. Provide Privacy Policy URL and Support URL.
5. Provide review notes and working review credentials/instructions because OpsTrax is account-based.
6. Confirm every permission string describes the real user-facing reason.
7. If account creation is introduced, provide an easy-to-find in-app initiation path for full account deletion.
8. Upload production builds to TestFlight first.
9. Execute Driver/Fleet/Customer pilot journeys on TestFlight builds.
10. Submit public App Store versions only after internal/pilot acceptance.

## Gate 7 — Store listing assets

Prepare per product, not one generic listing:
- app icon
- short description/subtitle
- full store description
- category
- keywords where supported
- support URL
- privacy URL
- screenshots from real application state
- tablet screenshots where required/valuable
- review notes
- reviewer test organization and credentials

Do not use fabricated operational metrics in screenshots. Use a controlled seeded tenant whose data is safe for store reviewers.

## Gate 8 — Release verdict

Public release requires all of the following:
- exact candidate SHA recorded
- full CI green
- signed iOS and Android binaries built from that SHA
- physical device evidence for all three products
- tenant/customer/driver isolation regression evidence
- privacy/data-safety declarations reviewed
- store metadata complete
- no dead buttons, placeholders or mock success states
- crash/blocker defects closed
- TestFlight and Google internal/closed pilot accepted

Verdicts:
- `NO-GO`: security, tenant isolation, data loss, crash, invalid store disclosure, missing required legal URL, or build/signing failure.
- `LIMITED GO`: internal/TestFlight/closed pilot only.
- `GO`: eligible for public App Store + Google Play production submission.
