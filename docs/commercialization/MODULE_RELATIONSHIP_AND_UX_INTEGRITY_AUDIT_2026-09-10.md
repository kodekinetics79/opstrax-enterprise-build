# OpsTrax Module Relationship and UX Integrity Audit

**Audit date:** 2026-09-10

**Working branch:** `fix/module-relationship-integrity`

**Base main SHA:** `a2fcb13712c5f4c5b64405a64cd96af42d71c66b`

**Governing model:** OpsTrax Accelerated Hardening Factory v2.0

**Disposition:** BUILD / INTEGRATE in progress; not a certification record

## 1. Executive finding

The concern is substantiated. OpsTrax contains several strong backend relationships, but screen availability, nearby navigation and shared labels have sometimes implied relationships that the persisted model did not implement. The most serious confirmed example was the commercial register path: Lead, Opportunity and Quotation forms accepted detailed business fields while the generic create handler persisted only a small subset. Other confirmed defects lost the selected Trip context during navigation, opened Quotations from the Price Simulation route and displayed a notification bell backed by an empty hardcoded array.

This branch repairs those defects and changes the customer-facing language where an automated transition does not exist. It does not claim that all OpsTrax modules are now integrated. The remaining relationships below stay on the hardening board until each one has persistence, state-transition, authorization and visible-browser evidence.

## 2. Relationship acceptance rule

A module relationship is accepted only when all four conditions hold:

1. **Identity continuity:** the destination receives a durable source identifier such as `customerId`, `contractId`, `jobId`, `tripId`, `vehicleId` or `deviceId`.
2. **Persistence continuity:** the source and target fields survive save, reload, sign-out/sign-in and service restart without a silent default or discarded field.
3. **Workflow continuity:** the transition changes or creates the correct target record under an explicit state rule; a link to another page is not workflow integration.
4. **Scope continuity:** tenant, branch, role and customer-portal boundaries remain enforced at the destination and in every joined query.

Visual proximity, shared wording, a catalog entry, a button or a successful frontend response is insufficient evidence by itself.

## 3. Confirmed defects and branch disposition

| Severity | Finding | Effect | Branch disposition |
|---|---|---|---|
| P0 | Generic Lead, Opportunity and Quotation create calls discarded visible form fields; their missing-risk fallback also produced database null instead of the schema default | A user could receive a successful save while route, cargo, probability, currency, dates, owner and sales context were absent after reload, or a valid create could fail at the database constraint | Fixed by allowlisted JSONB persistence, presence-aware defaults, typed amount/date mapping and read projection; candidate browser proof still required |
| P1 | Rate Card UI sent `title`, `pricingMethod` and `fuelSurcharge`, while the dedicated API consumes `rateCardName`, `billingBasis` and `fuelSurchargePercent` | Records could be saved with generic names or missing pricing semantics | Fixed; API now also rejects missing name/currency, invalid amount, invalid date range and invalid surcharge |
| P1 | Trip actions passed `tripId` to Jobs, Dispatch and Proof Center although the destinations use job identity | The destination loaded without the selected operating context | Fixed by resolving the linked `jobId`; Dispatch and Proof Center now consume it and the API filters assignments by it |
| P1 | `/price-simulation` rendered the Quotations page | The route name and screen behavior disagreed | Fixed; the route opens the truthful Price Simulation surface, which remains unavailable until its required pricing sources exist |
| P1 | Global notification bell used an empty hardcoded list and sent “View all” to Audit Logs | Persisted notifications were invisible from the shell and the transition target was wrong | Fixed; the bell queries the notification service and opens Notification Center |
| P1 | Campaign details were discarded and missing performance values became zero/SAR claims | Campaign reporting could imply measured attribution and currency without evidence | Fixed for persistence and presentation; automated campaign-to-lead/revenue attribution remains unavailable |
| P2 | Commercial copy implied Lead→Opportunity→Quotation→Contract/Booking automation | Users could infer workflow connectivity that does not exist | Corrected with explicit workflow-boundary language |
| P2 | Routes, navigation and permission declarations are maintained in multiple large files | A valid route can still drift from its menu, label or permission rule | Open architecture debt; consolidate into a typed route/capability registry in a later batch |
| P2 | A legacy unused frontend method calls `DELETE /api/safety/events/{id}`, for which no backend route exists | Latent dead client contract; no current caller was found | Open cleanup; remove the unused method or implement an authorized lifecycle action if product policy requires it |

## 4. Vertical relationship board

| Operational chain | Current evidence | Missing evidence or behavior | Governed state |
|---|---|---|---|
| Campaign → Lead → Opportunity → Quotation | Separate persisted registers; this branch preserves their visible fields | No durable source/target foreign keys, conversion command, idempotency rule or audit lineage between registers | DEVELOPMENT; no automated-conversion claim |
| Customer → Contract → Rate Card | Dedicated customer, contract and rate-card stores exist; rate cards can carry customer/contract identifiers | Frozen-candidate create/edit/reload, role and cross-tenant browser proof | INTEGRATE after this repair |
| Quotation → Contract / Booking | Quotation register exists | No conversion command, target transaction, replay protection or source lineage | DEVELOPMENT / relationship hold |
| Job → Dispatch Assignment → Trip → Proof | Dedicated job, assignment, trip and proof services exist; job/trip links exist in the backend; this branch repairs job-context screen transitions | Exact-candidate browser proof across refresh and role boundaries; reconcile every alternate dispatch surface | INTEGRATE |
| Proof → Charge → Invoice → Payment / Revenue | Stage 9 proof and billing confidence plus dedicated job-charge/invoice foundations exist | Full customer POC journey, accounting reconciliation and failure/retry evidence | DEVELOPMENT / partially proven |
| Vehicle / Driver → Assignment → Route / Job | Dedicated identity and assignment stores with tenant/branch filters exist | Review every vehicle/driver shortcut for target identity continuity; frozen-candidate browser evidence | INTEGRATE backlog |
| Vehicle → Device → Installation → Telemetry → Alert | Dedicated device, installation, latest-state and alert foundations exist | Real provider/device input according to claim, reconnect/replay evidence and exact-candidate browser proof | SOFTWARE HARDENING; provider/device EXTERNAL HOLD |
| Camera Device / Provider → Event → Media → Review → Incident → Coaching | Provider-intake, metadata, incident and coaching foundations exist with explicit unverified states | Authentic provider event/media, privacy acceptance, physical device and acknowledgement journey | SOFTWARE HARDENING; external certification hold |
| Driver / Vehicle → DVIR / HOS → Compliance | Dedicated compliance and HOS foundations exist | Certified data source, jurisdiction-specific regulatory evidence and qualified human acceptance | DEVELOPMENT; regulated claim hold |
| Connector → Discovery → Mapping → Sync → Normalized Record | Samsara connector lifecycle, discovery and mapping foundations exist | Authorized real account, authentic responses, limits, recovery and production POC journey | SOFTWARE READYING; provider certification EXTERNAL HOLD |
| Domain event → Notification → Shell / Notification Center | Persisted notification endpoints exist; this branch connects the shell | Exact-candidate role and unread/read/acknowledgement browser proof | INTEGRATE |
| Platform Admin → Tenant Account → Tenant Module | Separate platform and tenant surfaces exist | Continue verifying ownership rules for email, identity, billing and provider configuration; tenant secrets must not become platform-wide defaults | ARCHITECTURE REVIEW |

## 5. Hardcoding and commercial-truth controls

The following rules apply immediately to every changed module:

- Currency comes from the tenant or the persisted record. Mixed currencies remain separate. Missing currency or value renders unavailable.
- Missing probability, margin, audience, attribution, provider status, telemetry freshness or certification evidence remains unavailable; it is not converted to zero, healthy, connected or certified.
- Default form values are allowed only when visible to the user before save and valid as an explicit business choice.
- Backend handlers reject malformed values rather than coercing them to a plausible value.
- Generic metadata persistence is allowlisted by module. Credentials, secrets and arbitrary client keys are not copied into the record.
- A deterministic suggestion is labelled guidance. It is not described as AI insight, a measured fact or an executed workflow unless the corresponding evidence exists.

## 6. UX transition standard

Every context action must name the target and preserve the identifier the target actually consumes. If no durable relationship exists, the action is disabled or opens the destination without claiming that a record is selected. Destinations must show loading, not-found, forbidden and unavailable states distinctly. Browser back/forward, refresh and direct URLs must preserve the same result.

Operational pages should keep identity, status, exceptions and primary action above the fold. Related actions belong beside the source record or in its detail panel. Large promotional cards, duplicate headings and oversized buttons must not displace the working queue, evidence or exception state.

## 7. Verification completed on this branch

- Frontend production build and bundle budget: passed.
- Frontend lint with zero reported errors: passed.
- New module relationship integrity contract: passed.
- Real PostgreSQL create/reload/API-projection test for Lead, Opportunity, Quotation and Campaign visible fields: passed.
- Targeted backend tenant-scope, dispatch-context and commercial-truth regression tests: 26 passed.
- Backend build: passed with the repository's existing warning debt and no new build error.
- Static route comparison found every module-catalog route represented in the application router; nested platform routes were reviewed separately.
- Impeccable detector on all changed customer surfaces: zero findings.

These checks support integration. They do not replace a real-data, visible-browser run on a frozen exact SHA.

## 8. Next closure sequence

1. Merge the bounded relationship repair after CI and review.
2. Freeze an integration candidate and deploy only with explicit approval for its exact SHA.
3. Run visible customer POC journeys for Campaign/Lead/Opportunity/Quotation/Rate Card and Job/Dispatch/Trip/Proof, including save, reload, direct link, refresh, sign-out/sign-in and role boundaries.
4. Reconcile stored values directly against PostgreSQL and captured API responses.
5. Continue the board by operational chain, beginning with Quotation→Contract/Booking and Proof→Charge→Invoice.
6. Keep Samsara, camera, device, sensor and regulatory claims on their named external holds until authentic external evidence is available.

No module, provider, device or commercial release is certified by this audit alone.
