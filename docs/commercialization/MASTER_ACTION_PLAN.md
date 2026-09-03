# OpsTrax Master Commercialization & Certification Action Plan

**Status:** CONTROLLED MASTER - ACTIVE
**Version:** 2.1 — original wave sequence restored
**Effective date:** 2026-09-02
**Executive owner:** CTO Office / OpsTrax Commercialization Program  
**Technical baseline:** `main@155b54a3451c2a4618b4fc6a87fd59f0e68f425d`
**Parent tracker:** #110  
**Limited Gate A:** #108 / PR #113 - 30-day controlled-pilot LIMITED GO under `CR-2026-09-01-02`
**Deferred Gate B:** #109 / PR #114 - GT06 physical compatibility certification; NO-GO / closed without certification under `CR-2026-09-01-01`
**Active Wave 2 Gate A:** #115 - Samsara production connector certification under `CR-2026-09-02-01`
**Active Wave 2 Gate B:** #116 - certified ELD partner selection and integration under `CR-2026-09-02-01`

## Approved restoration of original wave sequence

`CR-2026-09-02-02` implements the program owner's explicit instruction to return
to the original order, finish Wave 2, and include missed earlier work. It supersedes
the cross-wave/six-squad execution permission in v2.0 at
`f45dbab23fe5ff4e1213b88b3e936ac9d85abd5a`; it does not discard completed code.

- Exactly two major active lanes: G2A / #115 and G2B / #116.
- Preserve the reviewed local integration batch at
  `95af1136a1923b0532b83bc0da2094bcce839c24`, including existing provider, UI,
  document, dispatch and release-check fixes. It is not a deployed or certified
  candidate, and the frozen G1A waiver does not transfer to it.
- Close specific missed earlier requirements that affect this batch, including
  the documented dispatch branch/archive concurrency test gap. Do not repeat an
  application-wide audit or start unrelated module development.
- Park the prepared HOS and video work branches. No new Wave 3–6 code batch
  starts before the preceding applicable exit gates are formally accepted.
- After accepted closure, continue in the original order without routine
  continuation questions. An unavailable external dependency does not become a
  pass: continue safe, useful work inside the two active lanes or required
  earlier remediation; hold the affected acceptance and report a true blocker.
- Retain meaningful batch integration, targeted tests and separate assurance.
  Independent implementation/review ownership, Appendix B, P0 dual perspectives,
  exact-SHA deployment, persisted Chrome retesting and all real provider,
  hardware, regulatory and commercial-rights evidence remain mandatory.
- Existing G1A LIMITED GO and G1B physical deferral remain exactly as recorded.
  This restores sequencing only; no capability status rises and no new waiver,
  public-code publication, production mutation or deployment is authorized.
- Current published PR #120 remains at `f50411ef3e787c25cf582e59411f6eb92d55a0b3`;
  its eleven passed CI jobs do not cover the later local integration batch.
  Release/publication permissions and the unresolved Render schema/access
  boundary must be resolved separately.

The original v1.2 wave definitions and acceptance requirements are restored below.
Version 2.1 is used to keep document revisions monotonic after v2.0.

## Retained integration acceptance checks

The sequencing restoration does not relax v2.0's mandatory integration checks.
A batch cannot advance to certification unless the applicable checks are green:

- Full build with zero errors.
- Warning ceiling must not increase; warning debt has a tracked burn-down target.
- Unit and contract suites.
- PostgreSQL-backed integration.
- Migration enrollment and production-shaped rehearsal.
- RBAC, RLS and tenant-isolation regression.
- Frontend lint, contracts, build and bundle budget.
- Mobile contracts/build where touched.
- Telematics/protocol regression where touched.
- Dependency and security checks.
- Bounded load smoke.
- Release containers and provenance.
- No open P0/P1 in the candidate scope.

These checks apply to the bounded current batch; they do not activate additional
workstreams or justify repeating unchanged full suites after each small fix.
Final acceptance still requires a frozen exact-SHA candidate and all applicable
real-world evidence below. Source-equivalent prior evidence must retain its
original SHA, scope and attribution rather than being relabelled as a new run.

## Master rule
This document governs commercialization sequencing. Work may be refined inside a phase, but no phase may be skipped, broadened, or declared complete without the stated acceptance evidence. Any sequence/gate change requires CTO change control.

## Executive objective
Graduate OpsTrax from pilot-capable fleet/TMS software with a strong telematics foundation into a commercially deployable, device-agnostic connected-fleet platform with certified-compatible hardware, production provider integrations, regulated ELD/HOS through a valid partner/device path, video safety, resilient operations, and evidence that survives customer, auditor, and executive scrutiny.

## Non-negotiables
- Product truth over UI/schema/marketing appearance.
- Visible Chrome + real persisted data for customer-facing acceptance.
- Physical hardware certification requires physical hardware.
- Provider certification requires a real provider account/API path.
- ELD/HOS claims require applicable regulatory evidence and complete workflow proof.
- No fabricated telemetry, diagnostics, video, safety, or HOS data in pilot/production acceptance.
- No broad repeated audits; defects close inside the owning gate.
- Exact-SHA final evidence.
- No unresolved P0/P1 at CERTIFIED/PRODUCTION READY status.
- Maximum two active critical commercialization tracks unless CTO explicitly authorizes more.

## Capability truth — retained without promotion
| Capability | Status | Primary gap |
|---|---|---|
| Fleet Identity / Asset Master | PILOT | Current-SHA closeout: responsive, correction, document/expiry, performance evidence |
| Telematics / DeviceOps | PILOT | Current-SHA closeout + provider/physical certification |
| GT06 | PILOT | Exact physical model/firmware bench, drive and soak |
| Samsara connector | PILOT | Real-account onboarding/mapping/reconciliation/backfill/sync-health certification |
| J1939 | DEVELOPMENT | Acquisition/transport, broader PGNs, real-hardware evidence |
| Pacific Track / PT40 | DEVELOPMENT | Real capture, fingerprint, vendor parser, bench/field certification |
| HOS data structures / alert concepts | DEVELOPMENT | Certified source plus complete operational/regulatory workflow |
| Certified ELD/HOS product | ROADMAP | Certified partner/device, commercial rights and jurisdiction-specific end-to-end evidence |
| Dual-facing camera | ROADMAP | Partner selection and secure event/video integration |
| Video Safety | ROADMAP | AI event/incident/coaching/retention workflow |
| Geotab/Motive/OEM | ROADMAP | Provider-specific production connectors |

## CTO-led SME council
- CTO / Program Owner - sequence, architecture, risk, final GO/NO-GO.
- Fleet Product SME - workflow completeness and operational value.
- Telematics / IoT SME - protocols, gateway, canonical telemetry, provider adapters.
- Hardware Certification SME - exact-model bench/vehicle/recovery/soak/supportability.
- ELD/HOS Regulatory SME - U.S./Canada certification boundary and HOS evidence.
- Video Telematics / Safety SME - camera architecture, event taxonomy, privacy/coaching.
- SDET / Performance SME - real-client Chrome, large-fleet, failure and performance evidence.
- Cybersecurity SME - tenant/branch isolation, device trust, secrets, privacy.
- SRE / DevOps SME - deployment, observability, recovery, backup/restore, scale.
- UI/UX SME - responsive, accessibility, high-volume usability and workflow clarity.
- Data/Analytics SME - telemetry semantics, provenance, quality and KPI truth.
- Commercial/Sales SME - packaging, pricing, claims and pilot boundaries.
- Customer Success/Support SME - onboarding, installation, training, SLA and RMA readiness.

Any SME can raise a RED finding. Only the CTO may waive a gate, and every waiver must be written, quantified, time-bounded and reflected in the Capability Truth Matrix.

## Program waves
| Wave | Critical workstreams | Exit result |
|---|---|---|
| 0 - Control | Truth matrix, baseline freeze, master plan, GitHub evidence lanes | Controlled program |
| 1 - Certify what exists | A: M1/M2 current-SHA closeout; B: GT06 physical certification deferred under `CR-2026-09-01-01` | Limited core only; no certified-compatible GPS family claim |
| 2 - Connect and comply | A: Samsara production certification; B: certified ELD partner selection/integration | Commercial BYOT + regulated ELD source |
| 3 - HOS and cameras | A: OpsTrax HOS workflow; B: dual-facing camera integration | Operational HOS + real camera/video path |
| 4 - Safety/ecosystem | A: Video Safety; B: Geotab/Motive prioritized connectors | Competitive safety + provider breadth |
| 5 - Device depth | DeviceOps 2.0, J1939 acquisition, PT40, OEM expansion | Supportable device lifecycle + heavy-duty breadth |
| 6 - Scale release | 1K-5K+ resilience, DR/recovery, support, billing, commercial packaging | Commercial connected-fleet release |

# WAVE 1 - TIME-LIMITED CONTROLLED PILOT

## Gate 1A - M1/M2 current-SHA LIMITED GO
Governance: #108 / PR #113 / `cert/m1-m2-current-sha-closeout`.

Required closeout:
1. Exact candidate frontend/API build identity and readiness.
2. Company Admin/Fleet Manager/Dispatcher/Maintenance/Executive/Driver/Customer journeys as applicable.
3. Vehicle/driver/asset/device invalid/duplicate/correction and persistence workflows.
4. Large-fleet search/filter/sort/pagination/export.
5. Device Health list/detail/lifecycle/install/commissioning truth.
6. GPS/live-map identity, freshness, drilldown, branch restriction and geofence behavior.
7. Real persisted OBD/J1939 diagnostic evidence and fault/hold workflow.
8. Required responsive viewports.
9. Browser console/failed-network evidence.
10. Open performance gate re-test.
11. Defect loop: observe -> evidence -> root cause -> focused fix -> tests -> exact-SHA deploy -> identical re-test.

Exit: 0 P0/P1; 0 tenant/branch leakage; 0 Driver/Customer internal-route exposure; 0 fabricated telemetry/diagnostics; exact-SHA evidence; customer journeys persist after refresh/logout-login; limitations declared.

`CR-2026-09-01-02` grants a CTO-approved, 30-day **LIMITED GO** for exact frontend/API candidate `e2230425a8e14249d2c0f477a7ec7b713a6ab27e`. It authorizes one isolated tenant and no more than 10 pilot vehicles from 2026-09-01 23:57:43 through 2026-10-01 23:57:43 America/New_York. M1/M2 remain PILOT. The Class 1/Class 3 exception covers the missing relevant-SME/Security/Product human quorum, external qualified-human Appendix B acceptance, and final-candidate representative performance renewal for this window only; it discloses rather than fabricates those approvals and does not waive security, isolation, data-integrity, truth, exact-SHA, hardware, provider, or regulatory requirements. Any P0/P1, isolation concern, data loss/corruption, truth violation, readiness failure, critical-worker violation, scope excess, SHA change, or expiry suspends the authorization.

## Gate 1B - GT06 physical certification - NO-GO / DEFERRED / CLOSED WITHOUT CERTIFICATION
Governance: #109 / PR #114 / `cert/gt06-physical-compatibility`.

`CR-2026-09-01-01` removes this gate from the active Wave 1 exit boundary because no authorized physical GT06 production candidate is available. The software harness remains at PILOT and may be retained in isolated staging, but this disposition is not a compatibility pass, waiver of physical evidence, supported-device listing, or permission to make a certified-hardware claim. Re-entry requires an exact physical manufacturer/model/hardware-revision/firmware combination, controlled enrollment, the mandatory Appendix B specialists, and completion of every stage below.

Certification stages:
1. Candidate - exact manufacturer/model/hardware revision/firmware/modem/radio/procurement identity.
2. Protocol Identified - real device byte behavior confirmed; no guessed decoder evidence.
3. Bench Compatible - identity/login/ACK/GPS/heartbeat/supported events/reboot/reconnect end to end.
4. Vehicle Tested - controlled route vs reference GPS; freshness/geofence/speed/heading where supported.
5. Failure/Recovery - power/network/GPS loss, duplicate session and server restart integrity.
6. Soak - 24h for Certified Compatible; 72h for Production Supported.
7. Security Review - provisioning, tenant binding, secrets/logs, replay/session policy.
8. Certified Compatible - exact model/firmware capability and limitation record published.
9. Production Supported - repeatable install, procurement, support/replacement process.

Physical hardware, SIM/data service, safe bench/vehicle access and the exact production candidate are external evidence dependencies. Simulator results cannot close them.

# ACTIVE WAVE 2

`CR-2026-09-02-01` activates exactly two major workstreams after G1A LIMITED GO, merged PR #113 and closed #108: G2A under #115 and G2B under #116. Activation does not promote any capability. Real provider/account/device/commercial-rights/regulatory evidence remains mandatory, and G1B remains excluded and deferred.

## Samsara production certification
- Customer-managed Connect -> Authenticate -> Discover -> Map -> Validate -> Sync -> Monitor -> Disconnect/Reconnect flow.
- Deterministic matching and unmatched/reconciliation queue; no silent ambiguous mapping.
- Backfill, incremental sync, backlog/last-success/error/stale-feed visibility.
- Real Samsara account/provider data evidence.
- Start with GPS/engine-state/odometer and expand only contract-tested fields.
- Acceptance: customer connects without engineering intervention; provider provenance is visible; disconnect/reconnect is safe and idempotent.

## Certified ELD partner selection/integration
Mandatory evaluation: U.S./Canada regulatory status, API depth, commercial rights, geography, hardware availability/install/support, security, webhooks/polling/backfill/rate limits/sandbox.

Do not build/market an ordinary GPS tracker as an ELD. U.S. and Canadian regulatory paths remain separate and must be verified against official sources.

# WAVE 3

## HOS workflow
Minimum: OFF/SB/D/ON timeline, automatic driving source, unidentified driving, applicable clocks and violation risk, edits/annotations/certification, supported special statuses, malfunction/diagnostic handling, inspection/transfer behavior as applicable, dispatch warnings when remaining legal time is insufficient.

Release only after source ELD evidence, edits/exceptions, audit reconstruction, role boundaries and inspection/transfer behavior are proven end to end.

## Dual-facing camera
Use a proven road-facing + driver-facing OEM/provider. Require secure API/SDK, event metadata, clip/live retrieval where needed, canonical vehicle/driver/trip/time/location linkage, and privacy/retention/access policy before driver-facing video is enabled.

# WAVE 4

## Video Safety
Event -> Review -> Severity -> Driver -> Vehicle -> Trip -> Coaching -> Driver acknowledgement -> Supervisor closure -> Safety history. Preserve provider evidence and review decisions.

## Geotab / Motive / OEM
Reuse one canonical telemetry model and one connector lifecycle. Each connector passes auth, discovery, mapping, backfill, incremental sync, failure/recovery, disconnect and tenant-isolation gates. Provider priority follows real sales pipeline/installed base.

# WAVE 5

## DeviceOps 2.0
Inventory, SIM/eSIM/carrier, firmware campaigns, RMA/warranty/replacement, installer appointments/evidence, governed remote commands, certification/compatibility catalog.

## J1939 depth
Keep DM1/DM2; add selected gateway acquisition/transport/reassembly and certify high-value PGNs/SPNs against real hardware. No universal-J1939 claim without evidence.

## PT40
Acquire exact unit -> capture real bytes -> fingerprint -> vendor parser/spec -> bench -> drive -> failure/recovery -> soak -> security -> certification.

# Continuous hardening
- Drive compiler/build warning debt downward.
- Dependency/secrets/SAST + RBAC/RLS/device-trust regression.
- Migrations remain production schema authority.
- Metrics/logs/traces and actionable alerts for API/workers/connectors/gateway.
- Backup verification, restore drills, restart/reconnect, queue replay/idempotency.
- Browser/API/DB/gateway performance at representative scale.
- Long-run worker/gateway/provider soak.
- Honest no-data states and telemetry provenance/freshness.
- Driver-camera and driver-record privacy/retention/audit.

# Universal evidence stack
1. Source/code review - supporting only.
2. Automated tests - supporting only.
3. Persisted data reconciliation - supporting only.
4. Visible Chrome - required for customer-facing workflows.
5. Real provider - required for provider claims.
6. Physical hardware - required for hardware support claims.
7. Regulatory evidence - required for ELD/HOS claims.
8. Scale/recovery - required for production support tier.

# Change control
- New ideas go to backlog unless they remove a blocker on the current active gate.
- No module scope expansion during certification except required defect/market/compliance closure.
- No future critical workstream starts without CTO activation.
- Severe security/regulatory findings supersede schedule and attach to the owning gate.
- External evidence can be prepared for but never simulated as passed.
- Sales claims are checked against the Capability Truth Matrix before proposal/contract.

Change classes:
- Class 0 in-scope refinement - workstream lead.
- Class 1 gate-impacting - CTO + relevant SME; issue + master change log.
- Class 2 sequence change - CTO after SME review; master document version change.
- Class 3 commercial waiver - CTO + applicable Security/Regulatory/Product; quantified waiver + expiry.

# Fast-path cadence
- Daily: active-gate evidence/blocker/P0-P1 pulse only.
- Per defect: same observe/fix/exact-SHA/re-test loop.
- Twice weekly: gate review.
- Weekly: commercial truth review.
- Gate close: independent SDET + Security/Regulatory/Product review as applicable.

# Accelerated 12-week target map
Calendar is subordinate to the gates and external partner/hardware availability.

| Window | Primary | Parallel | Outcome target |
|---|---|---|---|
| Weeks 1-2 | M1/M2 defect/evidence closeout | Freeze GT06 hardware + bench | Defect burn-down + hardware ready |
| Weeks 3-4 | Final M1/M2 exact-SHA acceptance | GT06 bench + controlled vehicle | Core software gate + device decision |
| Weeks 5-6 | Samsara real-account certification | ELD partner due diligence/API/commercial review | BYOT + ELD shortlist |
| Weeks 7-8 | Finish Samsara customer workflow | Integrate ELD baseline + start HOS | Connected fleet + regulated source |
| Weeks 9-10 | HOS workflows/evidence | Select/integrate camera baseline | HOS pilot + video event path |
| Weeks 11-12 | HOS/video stabilization + sales packaging | Next provider/DeviceOps only if green | Controlled paid connected-fleet pilot package |

# Commercial release rule
Revenue activates package-by-package as gates pass; the whole roadmap does not need to be complete.

- Fleet/TMS Pilot: M1 customer workflows accepted, limitations declared.
- Connected Fleet / Existing Samsara: Samsara connector certified.
- Connected Fleet / Certified GPS: exact hardware Certified Compatible/Production Supported.
- Regulated Fleet / ELD-HOS: certified partner + OpsTrax HOS end-to-end accepted.
- Video Safety: dual-camera + video/event + coaching accepted.

# Immediate action register
1. Monitor the #108/#113 controlled pilot under `CR-2026-09-01-02`; enforce its exact SHA, one-tenant, 10-vehicle, stop-trigger and expiry boundaries.
2. Under #115, inventory the existing Samsara connector, tests, schemas and customer workflow and maintain a focused evidence/gap ledger.
3. Continue bounded Samsara readiness fixes only where they do not depend on, simulate or represent missing real-account/provider evidence.
4. Do not perform or claim Samsara field certification, gate closure or promotion until an authorized real account, API path and provider evidence are available.
5. Under #116, perform official-source U.S./Canada regulatory and partner-readiness research while keeping the jurisdictions and certification boundaries separate.
6. Do not select or represent an ELD integration as certified without a real partner/device boundary, commercial rights and the applicable end-to-end evidence.
7. Maintain the disclosed dependency ledger: the Samsara account/API authority, ELD provider/device/commercial rights and qualified Appendix B human reviewer roster are not yet confirmed.
8. Keep #109/#114 deferred, the isolated GT06 listener stopped and no device enrolled until the exact authorized physical candidate is available.
9. Keep exactly #115 and #116 as the two active major Wave 2 lanes; no future gate starts without new CTO change control.
10. Update the Capability Truth Matrix only when an owning gate formally closes or an approved change alters a boundary.

# Master scorecard — original sequence retained
| Gate | Status | Evidence lane | Activation |
|---|---|---|---|
| G0 Program control | GREEN | Master plan + #110 + truth matrix | Active/complete |
| G1A M1/M2 certification | LIMITED GO - 30-DAY CONTROLLED PILOT | #108 / PR #113 / `CR-2026-09-01-02` | One tenant / max 10 vehicles / exact `e2230425...` / expires 2026-10-01 23:57:43 America/New_York |
| G1B GT06 physical certification | NO-GO / DEFERRED - CLOSED WITHOUT CERTIFICATION | #109 / PR #114 / `CR-2026-09-01-01` | Re-open only with exact authorized physical hardware |
| G2A Samsara production | ACTIVE - AMBER | #115 / `CR-2026-09-02-01` | Real authorized Samsara account/provider evidence required; G1B remains excluded |
| G2B Certified ELD partner | ACTIVE - AMBER | #116 / `CR-2026-09-02-01` | Official regulatory verification, real partner/device boundary and commercial rights required |
| G3A HOS | LOCKED | Future issue/PR | After ELD source selected/integrated |
| G3B Dual camera | LOCKED | Future issue/PR | After partner selected |
| G4 Video/provider ecosystem | LOCKED | Future issue/PR | After G3 passes |
| G5 DeviceOps/J1939/PT40 | LOCKED | Future issue/PR | After provider/device foundation stable |
| G6 Scale/commercial release | LOCKED | Final certification | After prerequisite gates |

# CTO GO/NO-GO
- **GO:** applicable gates met, 0 P0/P1, evidence package complete.
- **LIMITED GO:** bounded pilot, no critical security/regulatory defect, limitations and CTO waiver explicit.
- **NO-GO:** unresolved critical/P1 risk, regulatory uncertainty, tenant-isolation defect, fabricated truth or missing required real-world evidence.

This restored sequence governs execution under CR-2026-09-02-02. A new sequence change requires recorded approval; code progress alone never closes a certification gate.
