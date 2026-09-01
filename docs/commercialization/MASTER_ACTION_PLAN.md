# OpsTrax Master Commercialization & Certification Action Plan

**Status:** CONTROLLED MASTER - ACTIVE  
**Version:** 1.1
**Effective date:** 2026-09-01
**Executive owner:** CTO Office / OpsTrax Commercialization Program  
**Technical baseline:** `main@155b54a3451c2a4618b4fc6a87fd59f0e68f425d`  
**Parent tracker:** #110  
**Active Gate A:** #108 / PR #113 - M1/M2 current-SHA certification closeout  
**Deferred Gate B:** #109 / PR #114 - GT06 physical compatibility certification; NO-GO / closed without certification under `CR-2026-09-01-01`

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

## Capability truth at v1.0
| Capability | Status | Primary gap |
|---|---|---|
| Fleet Identity / Asset Master | PILOT | Current-SHA closeout: responsive, correction, document/expiry, performance evidence |
| Telematics / DeviceOps | PILOT | Current-SHA closeout + provider/physical certification |
| GT06 | PILOT | Exact physical model/firmware bench, drive and soak |
| Samsara connector | PILOT | Real-account onboarding/mapping/reconciliation/backfill/sync-health certification |
| J1939 | DEVELOPMENT/PILOT | Acquisition/transport, broader PGNs, real-hardware evidence |
| Pacific Track / PT40 | DEVELOPMENT | Real capture, fingerprint, vendor parser, bench/field certification |
| ELD/HOS | DEVELOPMENT | Certified partner/device + complete operational/regulatory workflow |
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

# ACTIVE WAVE 1

## Gate 1A - M1/M2 current-SHA certification
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

# WAVE 2 - INACTIVE / QUEUED

The G1B deferral does not itself activate Wave 2. Activation requires formal G1A GO or LIMITED GO after its remaining acceptance work, including A-02 restricted-role branch-isolation evidence, is independently accepted and recorded.

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
1. Keep #108/#113 as the only software certification closeout lane.
2. Deploy exact #113 candidate to isolated staging and capture build/readiness identity.
3. Run M1 visible-Chrome role/journey closeout on the large-fleet tenant.
4. Run M2 Device Health/GPS/Live Map/geofence/OBD closeout.
5. Fix only observed gate defects and repeat identical journeys.
6. Keep GT06 procurement/freeze for 2-3 identical production candidates in the deferred hardware backlog under `CR-2026-09-01-01`.
7. Preserve the isolated GT06 gateway configuration/evidence fail-closed and stopped while no physical test window is scheduled; enroll no unidentified device.
8. Re-open GT06 bench -> controlled route -> recovery -> soak certification only when the exact physical evidence dependency is available.
9. Prepare Samsara real-account onboarding/acceptance plan so Wave 2 can start immediately after Wave 1.
10. Begin ELD partner due diligence without building an in-house ELD.

# Master scorecard at v1.0
| Gate | Status | Evidence lane | Activation |
|---|---|---|---|
| G0 Program control | GREEN | Master plan + #110 + truth matrix | Active/complete |
| G1A M1/M2 certification | ACTIVE - AMBER | #108 / PR #113 | Active now |
| G1B GT06 physical certification | NO-GO / DEFERRED - CLOSED WITHOUT CERTIFICATION | #109 / PR #114 / `CR-2026-09-01-01` | Re-open only with exact authorized physical hardware |
| G2A Samsara production | QUEUED | Future issue/PR | After G1A formal GO/LIMITED GO; G1B remains excluded |
| G2B Certified ELD partner | QUEUED | Future issue/PR | After G1A formal GO/LIMITED GO; G1B remains excluded |
| G3A HOS | LOCKED | Future issue/PR | After ELD source selected/integrated |
| G3B Dual camera | LOCKED | Future issue/PR | After partner selected |
| G4 Video/provider ecosystem | LOCKED | Future issue/PR | After G3 passes |
| G5 DeviceOps/J1939/PT40 | LOCKED | Future issue/PR | After provider/device foundation stable |
| G6 Scale/commercial release | LOCKED | Final certification | After prerequisite gates |

# CTO GO/NO-GO
- **GO:** applicable gates met, 0 P0/P1, evidence package complete.
- **LIMITED GO:** bounded pilot, no critical security/regulatory defect, limitations and CTO waiver explicit.
- **NO-GO:** unresolved critical/P1 risk, regulatory uncertainty, tenant-isolation defect, fabricated truth or missing required real-world evidence.

This plan remains the master sequence. It is updated by versioned change control, not by silently changing direction.
