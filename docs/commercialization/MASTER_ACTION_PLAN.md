# OpsTrax Accelerated Hardening Factory v2.0

## Consolidated Governing Master Action Plan

**Status:** CONTROLLED MASTER - ACTIVE ON MERGE
**Factory model:** 2.0
**Controlled plan revision:** 2.8
**Effective date:** 2026-09-08
**Executive owner:** CTO Office / OpsTrax Commercialization Program
**Parent tracker:** #110
**Entry baseline:** `main@2ed4e4295136118711fb7dcfd7db29d0ce3a3a42`
**Production POC software:** `294a20ded52b7f6a3c4d689fa0de7d0b3fbe7efa`
**Change control:** `CR-2026-09-08-01`

## 1. Governing decision

OpsTrax will complete all software-controllable engineering across fleet, telematics, camera, sensor, device, provider, compliance, platform and commercial modules without waiting for unavailable physical hardware. Internal verification will use independent tests, real persistence, protocol fixtures, simulated failure and recovery, production-shaped deployment, and visible customer journeys on frozen candidates.

Physical camera, sensor, tracker, CAN/J1939, PT40, OEM and other hardware evidence remains a final external confirmation layer. Missing hardware moves the affected physical gate to **EXTERNAL HOLD**. It does not stop unrelated engineering, integration or software verification, and it never becomes a simulated certification pass.

This document consolidates the valid controls from v1.1 through controlled revision 2.8. It replaces serial wave execution and any later pause rule that would idle engineering solely because another lane lacks hardware, provider access, regulatory evidence or a queued human reviewer. It preserves evidence integrity, exact-SHA acceptance, commercial truth, security, independent assurance and the limited scope of prior waivers.

## 2. Executive objective

Move OpsTrax from a broad development platform to a reliable, commercially defensible fleet product by closing software defects in meaningful vertical batches, integrating continuously, and applying full certification evidence only to frozen release candidates. Every status must state what was proven, what was simulated, what remains external and what customers may rely on.

## 3. Three-speed operating model

### BUILD

Up to six bounded engineering squads may work in parallel. Each squad owns a declared vertical slice, batches related defects, uses targeted tests while changing code, and produces a reviewable integration unit. Human acceptance queues and unavailable external evidence do not stop BUILD work that remains technically useful and truthful.

### INTEGRATE

Completed batches enter a shared integration candidate. The integration function checks compilation, warning movement, unit and contract tests, PostgreSQL behavior, schema migration, tenant and branch isolation, security, frontend and mobile contracts, protocol regressions, release containers, production rehearsal, provenance and bounded load. Shared schema, authentication and design-system authorities remain serialized where concurrent edits would create unsafe conflicts.

### CERTIFY

At most two frozen certification candidates may run simultaneously. Certification occurs after a meaningful integrated batch, not after each minor correction. Customer-facing evidence uses the exact frozen frontend and API SHA, a visible supported browser, persisted data and repeated journeys. Provider, physical-device and regulatory claims require the corresponding real external evidence.

## 4. Concurrency controls

- Maximum six active bounded engineering squads.
- Maximum two simultaneous frozen certification candidates.
- Each squad declares the owned module and shared-core files before integration.
- External dependencies use **EXTERNAL HOLD** and immediately release engineering capacity.
- Human SME acceptance may queue. It blocks promotion or final sign-off, not other engineering.
- One production schema authority writes the migration chain at a time.
- One security authority coordinates shared authentication, authorization and session changes.
- Frozen certification candidates are immutable. Any code change creates a new candidate identity.
- A P0 may stop any affected lane and preempt current priorities.

## 5. Factory functions

| Function | Primary responsibility | Required independence |
|---|---|---|
| Fleet and TMS Engineering | Fleet identity, vehicles, drivers, dispatch, jobs, routes, maintenance, customer operations | Product and SDET review outside the implementation author |
| Connected Vehicle Engineering | Telemetry, DeviceOps, gateways, provider adapters, sensors, GPS, J1939 and protocol software | Telematics, security and SDET review outside the implementation author |
| Compliance Engineering | HOS, ELD partner contract, DVIR and jurisdiction-specific controls | Qualified regulatory acceptance for regulated claims |
| Video Safety Engineering | Camera metadata, provider event and media contracts, privacy, incidents and coaching | Video safety, privacy/security and SDET review |
| Platform Hardening | Tenancy, RBAC/RLS, database, migrations, observability, recovery, deployment and warning debt | Independent security/data integrity and SRE review |
| UX and Performance | Customer journeys, density, accessibility, responsive behavior, maps and frontend performance | Product/UX and performance review |
| Independent SDET | Adversarial journeys, contracts, data reconciliation, failure/recovery, scale and evidence control | Must not be the implementation owner for the accepted result |

The six-squad limit applies to engineering squads. Independent SDET and platform release control are standing functions and must have enough independence to challenge a squad result.

## 6. Severity policy

| Severity | Definition | Required action |
|---|---|---|
| P0 | Tenant or role isolation failure, authentication bypass, data corruption or loss, cross-device attribution, fabricated telemetry/HOS/video truth, material regulatory corruption, or critical secret exposure | Contain immediately; stop the affected lane; fix and run independent adversarial regression before integration resumes |
| P1 | Broken core customer workflow, materially wrong live state or diagnostics, unsafe provider mapping, major availability or recovery failure, or material permission defect | Fix in the current module batch; candidate cannot pass INTEGRATE or CERTIFY |
| P2 | Bounded workflow, performance, accessibility, usability or operational defect without P0/P1 impact | Batch with the owning vertical slice; no standalone certification run |
| P3 | Cosmetic defect, minor copy issue or low-risk enhancement | Backlog or include when safe within an existing batch |

No frozen candidate may receive `SOFTWARE VERIFIED`, `PRODUCTION READY` or `CERTIFIED` status with an unresolved P0 or P1 in its declared scope.

## 7. Batch-first defect policy

Related defects close as one vertical unit. Typical batches include:

- Fleet Master: validation, correction, duplicate handling, assignment, archive/reactivation, history, import and export.
- Telematics Truth: identity, freshness, source time, receipt time, position, diagnostics, alerts, provenance and branch scope.
- Camera Truth: manual metadata, provider authority, event/media lifecycle, private fields, review, incident, evidence and coaching admission.
- Provider Lifecycle: connect, authenticate, discover, map, reconcile, backfill, incremental sync, health, rate limit, disconnect and recovery.
- DeviceOps: inventory, SIM/eSIM, install, commission, transfer, replace, suspend, revoke, firmware, remote command, RMA and audit.
- Compliance: source authority, duty state, clocks, edits, annotations, certification, diagnostics, transfer/inspection and jurisdiction rules.
- Platform: RBAC/RLS, migration, readiness, workers, queues, backup, restore, observability, deployment identity and warning reduction.
- Interface Efficiency: shared shell, information priority, page density, responsive structure, accessible controls, tables, and truthful loading, empty, stale and error states.

A batch receives targeted BUILD checks, one INTEGRATE promotion and one frozen-candidate certification run unless a P0 requires earlier isolation.

## 8. Interface density and information priority standard

OpsTrax is an operating product. Each page must maximize useful decision information without obscuring hierarchy, accessibility or commercial truth. This standard applies to all existing modules and every new or changed vertical slice.

- Put the page's primary status, exception or task and its most likely action above the fold on a standard 1366 by 768 desktop viewport wherever the content permits.
- Keep the global header, breadcrumbs, quick access and navigation compact. They must orient the user without consuming the working area needed for fleet, safety, device, sensor and compliance decisions.
- Use one clear page heading. Keep supporting explanations concise and place secondary guidance beside or below the work it explains.
- Size visible buttons for their action and label. Preserve accessible focus, target size and spacing without using large padded controls that displace operational content.
- Use dense, readable KPI strips, filters, tables and split views for frequently scanned information. Give more space only to maps, media, complex forms or analysis that needs it.
- Keep empty, loading, unavailable and error states proportional to their message. They must explain the truthful state and recovery action without expanding a small message into an otherwise empty full-screen panel.
- Make responsive behavior structural. Reflow, collapse or progressively disclose secondary content at narrow widths while keeping DOM order, keyboard order and visual order aligned.
- Prefer shared shell, token and primitive improvements that raise the quality of every module. Add page-specific layout only where the task and information hierarchy require it.
- Review realistic long labels, large row counts, zero-data states, API failures, stale/offline states, browser zoom and desktop and handheld widths.

During BUILD, the owning squad records the page's primary task path and checks the changed responsive states. During INTEGRATE, UX and independent SDET verify shared-shell regressions, keyboard behavior, density and information visibility. During CERTIFY, visible browser evidence on the frozen exact SHA confirms that critical status and actions remain understandable with persisted customer-scale data. A density defect that materially hides or obstructs a core workflow is P2 or P1 according to its operational effect; a cosmetic spacing issue is P3.

## 9. Software completion and hardware boundary

The following dispositions apply to software-controlled work:

- **SOFTWARE HARDENING IN PROGRESS** - code or required internal evidence remains incomplete.
- **SOFTWARE VERIFIED** - the exact frozen software candidate passed its declared automated, persistence, security, failure/recovery and customer-journey evidence.
- **SOFTWARE READY FOR EXTERNAL CONFIRMATION** - software is verified and only named physical device, provider, field or regulatory evidence remains.
- **SOFTWARE NO-GO** - a P0/P1, integrity failure or material acceptance gap blocks the candidate.

These labels are scoped software dispositions. They do not mean Certified Compatible hardware, certified provider integration, regulated ELD/HOS approval or general commercial release.

Final external evidence includes, as applicable:

- exact manufacturer, model, hardware revision, firmware, radio/modem and SIM identity;
- real device bytes and supported protocol behavior;
- bench, controlled route, sensor reference comparison and installation evidence;
- power, network, GPS, server restart, duplicate-session and recovery testing;
- 24-hour or 72-hour soak according to the claimed support tier;
- real provider account, permissions, responses, limits, webhook/polling and reconnect behavior;
- jurisdiction-specific official regulatory status and qualified review;
- privacy, retention, support, replacement, RMA and customer operating evidence.

Only this real evidence can promote the related external claim.

## 10. Evidence rules by speed

| Evidence | BUILD | INTEGRATE | CERTIFY |
|---|---:|---:|---:|
| Targeted unit and contract tests | Required when relevant | Repeated in integrated suite | Supporting |
| PostgreSQL and persistence reconciliation | Targeted | Required when relevant | Required for customer data claims |
| Security, RBAC, RLS and branch isolation | Targeted | Required | Required on final candidate where affected |
| Protocol fixtures and virtual devices | Required for protocol work | Required regression | Supporting only for hardware claims |
| Production-shaped build and migration rehearsal | Optional per small change | Required | Required on frozen candidate |
| Visible customer journey | Useful during development | Smoke coverage | Required for customer-facing claims |
| Information priority, density and responsive states | Changed-page review | Shared-shell and representative-route review | Required for affected customer-facing claims |
| Exact frontend and API SHA parity | Not required per edit | Candidate identity required | Mandatory |
| Real provider account and responses | When available | When available | Mandatory for provider certification |
| Physical hardware and field evidence | When available | When available | Mandatory for hardware certification |
| Regulatory evidence and qualified acceptance | Prepared | Reviewed | Mandatory for regulated claims |

Prior evidence remains attached to its original SHA and scope. It may be reused only when the relevant behavior is source-equivalent and the governing gate allows that use.

## 11. Integration gate

A batch cannot enter CERTIFY until all applicable checks are green:

1. Full build with zero errors.
2. Compiler warning ceiling does not increase; warning debt has an owned reduction plan.
3. Unit, contract and targeted integration suites.
4. PostgreSQL-backed tests and persistence reconciliation.
5. Migration enrollment and production-shaped rehearsal.
6. Tenant, branch, role, export and direct-route authorization regression.
7. Frontend contracts, build, bundle budget, shared-shell density, representative desktop and narrow layouts, and truthful loading/empty/error states.
8. Mobile contracts and build when mobile code changes.
9. Telematics, protocol, camera, sensor or provider regression when affected.
10. Dependency and security checks.
11. Bounded load and recovery smoke appropriate to the change.
12. Release containers and provenance.
13. Zero unresolved P0/P1 in candidate scope.

## 12. Certification gate

Only a frozen candidate can be certified. Its evidence record must name the exact frontend, API, worker, gateway and database migration identity in scope.

Customer-facing acceptance requires visible Chrome on the intended POC or release surface, persisted data, refresh and sign-out/sign-in survival, role and branch boundaries, efficient information priority at desktop and narrow widths, and truthful empty/stale/offline/error states. Demo data must be clearly labelled and cannot serve as customer, provider, hardware, video or regulatory evidence.

Provider claims require an authorized real provider account and authentic responses. Hardware claims require the exact physical device. ELD/HOS claims require the selected provider/device boundary, commercial rights, jurisdiction-appropriate official evidence and complete operational workflow proof. Production-support claims require measured performance, recovery, observability, backup/restore and support operations.

Implementation teams may submit evidence but cannot self-certify critical claims. P0 domains require two logically independent qualified perspectives. AI-assisted review is supporting analysis only and cannot be represented as qualified-human acceptance.

## 13. Current repository and production truth

### Adoption baseline on 2026-09-02

The following is the dated historical snapshot used when the Accelerated Hardening Factory v2.0 was adopted. It establishes the transition from v1.1 and v1.2; it is not a claim about the repository after that date.

- Wave 1 G1A was merged and closed under a time-bounded LIMITED GO; M1/M2 remained PILOT.
- GT06 physical certification was deferred on EXTERNAL HOLD and remained NOT CERTIFIED.
- Wave 2 was active through #115 for Samsara and #116 for certified ELD partner selection and integration readiness.
- PRs #118 and #119 were merged; PR #120 was active for Samsara truth and recovery hardening.

### Later verified truth on 2026-09-08

The later repository and production facts below supersede the adoption snapshot wherever status changed.

- Current `main` is `2ed4e4295136118711fb7dcfd7db29d0ce3a3a42`.
- The production POC frontend and API report exact deployed software SHA `294a20ded52b7f6a3c4d689fa0de7d0b3fbe7efa`.
- PR #120 has since merged.
- PRs #201, #202, #210 and #216 are merged with successful checks.
- PR #131 was closed without merge; its function was superseded by later integrated HOS work.
- PR #159 is merged with successful checks.
- PRs #217 through #220 repaired and documented the production POC navigation, deployment identity and truth incident.
- The production POC exposes the Camera Metadata module and visibly identifies the current tenant as demo data.
- Stored camera records are metadata only. No real camera media, provider event stream or automated video assessment is certified.
- Authenticated Firefox review on 2026-09-08 showed the Camera Metadata page unable to load records, Fleet Overview unable to reach the vehicle service, and Telematics Control Tower receiving HTTP 401. These are unresolved production POC acceptance failures; repository health does not override them.
- The current hardening branch contains later software corrections and UI-density work that remain unpublished until a new integration candidate is frozen and authorized.

Repository health and merged checks are evidence of software integration. They do not close the remaining real provider, device, field, regulatory or qualified-human gates.

## 14. Current vertical-slice board

| Slice | Engineering position | External dependency | Current governed disposition |
|---|---|---|---|
| Fleet Identity and Asset Master | Core foundation merged; renewed candidate acceptance remains | Qualified acceptance and performance renewal for general promotion | PILOT; prior limited-pilot terms remain controlling |
| Telematics and Live Operations | Core ingestion, persistence and customer surfaces exist | Commissioned real feeds and device/provider evidence by claim | PILOT / software hardening |
| GT06 | Protocol parser, gateway and virtual test foundation exist | Exact physical model/firmware, bench, route, recovery and soak | EXTERNAL HOLD / hardware not certified |
| Samsara | Connector and recovery hardening merged through #120 | Authorized real account and customer journey | PILOT / EXTERNAL HOLD for provider certification |
| HOS | Substantial workflow, source-truth and Canada/KSA shadow-engine work merged | Certified source path, regulatory scope and qualified acceptance | DEVELOPMENT / regulated claim on hold |
| Camera Metadata | POC route and truthful metadata-only surface deployed; additional authority hardening in current batch | None for metadata-only software verification | SOFTWARE HARDENING IN PROGRESS until frozen-candidate gates pass |
| Camera and Video Safety | Event, incident, coaching and privacy foundations exist | Real camera/provider/media and field/privacy evidence | EXTERNAL HOLD for camera/video certification |
| DeviceOps | Lifecycle, transfer and operating foundations exist | Exact device installation, command, firmware, RMA and field evidence | DEVELOPMENT / software hardening plus external hold |
| J1939 and PT40 | Decoder, transport and adapter foundations exist | Physical CAN/J1939 acquisition and exact PT40/OEM hardware | DEVELOPMENT / EXTERNAL HOLD |
| Cold-chain sensors | Workflow and telemetry foundations exist | Exact sensor/provider accuracy, calibration, route and recovery evidence | PILOT / EXTERNAL HOLD for sensor claims |
| Scale, DR and Operations | CI, containers, migrations, observability and release foundations are green | Measured supported tiers, restore and RPO/RTO acceptance | DEVELOPMENT / partially proven |
| Enterprise Platform | Enterprise, mobile, API, migration and automation foundations merged | Real IdP/SCIM, external clients, scale and customer acceptance by claim | DEVELOPMENT |

No Wave 1 through Wave 7 is completely certified and formally closed. Overall disposition remains **COMMERCIAL RELEASE - NOT GO**. A bounded M1/M2 pilot remains supportable only within its approved scope and current operational controls.

## 15. Hardware-related software completion lanes

### Camera and video

Complete the metadata authority model, provider adapter contract, event identity, media authorization, retention, audit, incident and coaching workflow, failure states, privacy controls and exact-candidate browser journey. Without real provider media, workflows that would imply verified review, AI assessment, evidence packaging or provider-backed coaching must fail closed.

### Sensors and cold chain

Complete sensor identity, calibration metadata, unit conversion, thresholds, alerting, timestamp/provenance, offline/stale behavior, trip linkage, audit, simulated fault injection and reference-input reconciliation. Physical accuracy and environmental performance remain external.

### Trackers and telematics devices

Complete parser and session contracts, device identity, replay/CRC behavior, normalization, assignment, commissioning, firmware and command governance, durable forwarding, reconnect, duplicate-session and virtual-fleet testing. Physical RF/GPS/electrical/install/route/soak evidence remains external.

### CAN, J1939, PT40 and OEM devices

Complete transport/reassembly, selected PGN/SPN decoding, provenance, unknown-signal behavior, fault persistence, adapter boundaries and independent fixtures. Vehicle-bus acquisition, exact device bytes and physical validation remain external.

## 16. Daily executive velocity dashboard

The daily dashboard reports:

- open P0 and P1 by slice;
- P0/P1 closed in the last 24 hours;
- active squads and declared scope;
- slices in RED, HARDENING, INTEGRATION GREEN, CERTIFICATION, EXTERNAL HOLD and LOCKED;
- active frozen certification candidates, maximum two;
- regression pass/fail totals and new regressions;
- build error count, compiler warning count and daily delta;
- production and candidate readiness plus exact-SHA parity;
- measured p95 indicators for affected critical paths;
- representative desktop and narrow-view density findings, including critical information displaced below the fold;
- external dependencies, owner, requested item and next review date;
- queued qualified-human acceptances;
- packages newly eligible, still blocked or suspended.

Evidence details remain in issues, pull requests and immutable artifacts. The executive view reports decisions, movement and blockers without converting activity into progress.

## 17. Commercial truth rules

- UI, route, schema or catalog presence is not capability proof.
- Demo or generated data is not real customer evidence.
- Simulator or emulator evidence is not physical-device certification.
- Provider reachability is not a certified production integration.
- Stored metadata is not camera media or automated video analysis.
- An ordinary GPS tracker is not an ELD.
- Stale, unavailable or unverified data is never presented as current, healthy or authoritative.
- Sales, proposals, demos, contracts and support statements must match the Capability Truth Matrix.
- A capability may be excluded from a package while the rest of the package advances.

## 18. Change control

This consolidated master supersedes conflicting serial-execution language in earlier plans and amendments. Earlier evidence, issue history, acceptance requirements and commercial limitations remain effective where they do not conflict with the operating model here.

- Class 0: in-scope refinement or squad allocation within this master.
- Class 1: gate-impacting change, approved by the CTO and relevant SME with an issue and change record.
- Class 2: sequence or factory-model change, approved by the CTO after SME review and recorded in a new controlled revision.
- Class 3: commercial waiver, quantified, time bounded, explicit about scope, stop triggers and expiry, with applicable Security, Regulatory and Product participation.

No change record substitutes for missing evidence. A waiver must never be silently inherited by another tenant, vehicle count, device, provider, jurisdiction, candidate SHA or time period.

## 19. Required next actions

1. Finish and independently verify the current camera authority/truth batch; freeze it only after all integration gates pass.
2. Complete the shared shell and high-priority fleet, camera, telematics, sensor and device density batch, then verify representative desktop, narrow, loading, empty and failure states.
3. Continue software-controllable DeviceOps, sensor, J1939, PT40, GT06 and provider work in bounded vertical batches.
4. Maintain an external procurement ledger naming the exact device or provider access needed for each final confirmation gate.
5. Prepare repeatable physical test scripts, evidence templates and reference measurements before hardware arrives.
6. Form at most two frozen certification candidates from integrated meaningful batches.
7. Keep the production POC truthful: demo labels visible, no invented provider/media/AI claims, and exact deployed identity observable.
8. Update #110 and the daily executive dashboard with software movement, external holds and actual certification eligibility.

## 20. End state

The factory completes when the selected commercial package has evidence-backed fleet operations, secure and supportable device/software behavior, applicable provider and regulatory acceptance, proven performance and recovery, truthful customer journeys, qualified independent sign-off and a final exact-SHA release disposition.

Until then, complete the software, verify its declared boundary, keep external hardware work ready to execute, and state every limitation plainly.
