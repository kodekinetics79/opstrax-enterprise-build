# OpsTrax Mobile — Store Privacy & Data Safety Engineering Inventory

Status: working submission inventory for `feat/mobile-premium-glass-ui`. Re-audit this file at the exact store candidate SHA before completing Apple App Privacy or Google Play Data Safety forms.

## Products

The shared React Native / Expo codebase produces three public binaries:

- OpsTrax Driver
- OpsTrax Fleet
- OpsTrax Customer

Platform Super Admin is intentionally excluded from public mobile products.

## Current account model

The mobile app currently supports sign-in only. It does not provide self-service account creation. User accounts are provisioned by the authenticated organization/platform workflow.

If `EXPO_PUBLIC_ACCOUNT_CREATION_ENABLED=true` is introduced, production builds fail unless `EXPO_PUBLIC_ACCOUNT_DELETION_URL` is also configured to a public HTTPS deletion resource. The in-app Account screen exposes that direct deletion path.

## Data accessed or submitted by the mobile client

### Account and organization data
- user ID
- name
- work email
- organization/company ID, code and name
- role and permissions
- country/currency where returned by the authenticated tenant session

Purpose: authentication, tenant isolation, authorization, localization and product routing.

### Operational fleet/logistics data
Depending on product and server-granted role/permissions:
- assignments, trips, shipments and delivery status
- vehicle identifiers and operational state
- proof-of-pickup / proof-of-delivery metadata
- DVIR inspection results and defect notes
- HOS status only when the backend has an actual usable source
- coaching tasks
- operational notifications
- customer invoices/payment history returned by customer-scoped endpoints
- customer support/feedback tied to customer-owned shipments

Purpose: core OpsTrax fleet, logistics and customer-service functionality.

### Camera / photos
Driver proof workflows can request camera access only when the user chooses to capture delivery or inspection evidence. Captured proof images are uploaded to tenant-scoped server storage before a proof record is submitted.

The app does not request microphone permission for image capture.

### Location
The current proof workflow requests foreground location at proof submission time. Location is optional: if permission is denied or location cannot be obtained, proof can continue without coordinates.

Current branch does **not** implement background location tracking. Do not declare background location until a reviewed implementation is actually present and justified.

### Device-local secure data
Expo SecureStore holds:
- authenticated mobile session token/session metadata
- selected workspace item key
- small driver form drafts such as exception notes
- proof notes and durable uploaded proof references

Draft keys are scoped by product, environment, tenant, user and work item. Binary photo evidence is not stored in SecureStore.

## Current SDK / tracking posture

At the time of this inventory:
- no advertising SDK is present
- no advertising identifier access is implemented
- no App Tracking Transparency request is implemented
- no analytics SDK was found in the mobile branch
- no crash-reporting SDK was found in the mobile branch
- no Contacts or Calendar permission is used
- no background location API was found
- native push registration is not yet implemented; the app currently uses the authenticated server notification inbox

Any future addition of analytics, crash reporting, push tokens, background location, BLE/hardware identifiers, dashcam SDKs or third-party identity providers must update this inventory and the store disclosures before release.

## Store resources required for production

Production app configuration fails closed unless these are valid public non-loopback HTTPS resources:
- `EXPO_PUBLIC_PRIVACY_URL`
- `EXPO_PUBLIC_SUPPORT_URL`

If account creation is enabled:
- `EXPO_PUBLIC_ACCOUNT_DELETION_URL`

The Privacy and Support resources are linked from the in-app account screen.

## Submission declarations to validate at candidate SHA

Before Apple/Google submission, independently verify:
1. Exact data collected by the production API and storage services, not only mobile source code.
2. Retention/deletion periods for user, driver, shipment, proof, location, DVIR, billing and support data.
3. Every processor/subprocessor that receives mobile-originated data.
4. Whether any data is used for analytics, product personalization, advertising or cross-app tracking.
5. Country-specific legal retention requirements for fleet/compliance records.
6. Privacy policy wording matches the store developer legal entity and each published OpsTrax product name.
7. Google Play Data Safety answers and Apple App Privacy labels are regenerated from this exact candidate SHA and production architecture.

This document is an engineering truth inventory, not a substitute for legal/privacy approval.
