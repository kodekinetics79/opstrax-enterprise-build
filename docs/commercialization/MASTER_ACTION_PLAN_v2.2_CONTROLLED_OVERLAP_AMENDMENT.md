# OpsTrax Master Commercialization & Certification Action Plan — v2.2 Controlled Overlap Amendment

**Status:** CONTROLLED MASTER AMENDMENT — ACTIVE  
**Effective date:** 2026-09-03  
**Authority:** CTO / Program Owner explicit instruction: complete Wave 2 as far as evidence honestly permits and step into Wave 3 in parallel.  
**Change record:** `CR-2026-09-03-01`  
**Parent:** GitHub Issue #110  
**Entry main:** `aba2636c543c6f77cb47597383d4c2c8c32e61c8`  
**Wave 2 gates:** #115 / #116  
**Wave 3 gates:** #128 / #129

## 1. Governing effect

This amendment changes sequencing/concurrency only. It does **not** convert missing provider, hardware, commercial-rights, qualified-human, or regulatory evidence into a pass and it does not promote any Capability Truth Matrix row.

Master Action Plan v2.1 remains binding except where this amendment expressly changes the Wave 2 -> Wave 3 activation rule and active-lane interpretation. All universal evidence, Appendix B, commercial-truth, exact-SHA, visible-Chrome, real-provider/hardware/regulatory and no-self-certification requirements remain unchanged.

## 2. CTO decision — controlled Wave 2 / Wave 3 overlap

The program shall not waste engineering capacity while indispensable Wave 2 evidence is externally unavailable. Therefore:

1. **G2A #115 and G2B #116 remain open external-closure gates.** They are not declared complete or certified until their real provider/account/device/commercial/regulatory evidence is observed and independently accepted.
2. The current bounded Wave 2 engineering batch on `main@aba2636c543c6f77cb47597383d4c2c8c32e61c8` is treated as the safe software-readiness floor, not as provider certification.
3. **G3A #128 HOS operational workflow and G3B #129 dual-facing camera integration are activated now** for bounded engineering/readiness and evidence acquisition.
4. The previous "exactly two major open gates" interpretation is replaced by **maximum two engineering-intensive implementation lanes at a time**. G2A/G2B may remain open in EXTERNAL CLOSURE HOLD while G3A/G3B consume the two engineering lanes.
5. If real Wave 2 provider/regulatory evidence becomes available and requires material engineering/remediation, the CTO must pause or narrow one Wave 3 engineering lane before starting a third engineering-intensive lane.
6. Wave 4–6 remain locked. Video Safety coaching does not silently move into G3B; provider ecosystem expansion does not silently move into G3A/G3B.

## 3. Wave 2 truth at overlap activation

### G2A — Samsara production connector certification (#115)

**Status:** PILOT / EXTERNAL CLOSURE HOLD / OPEN.

Engineering hardening is merged, including connector truth, response bounds, mapping/reconciliation foundations, replay/idempotency and recovery protections. Formal closure still requires:

- authorized real Samsara organization/account/token and required scopes;
- authentic provider responses and provider-contract confirmation;
- isolated exact-SHA deployment;
- visible Chrome Connect -> Authenticate -> Discover -> Map -> Validate -> Sync -> Monitor -> Disconnect/Reconnect;
- provider-backed pagination, rate-limit, backfill, freshness, retry/recovery and representative-scale evidence;
- persisted reconciliation and tenant/branch isolation;
- qualified independent Security, Principal SDET, Fleet Product/Telematics and SRE acceptance.

No Samsara production-certified claim is permitted before those items pass.

### G2B — certified ELD partner selection/integration (#116)

**Status:** ROADMAP/DEVELOPMENT / EXTERNAL CLOSURE HOLD / OPEN.

Samsara is the primary evidence-acquisition candidate, Motive contingency #1, Geotab contingency #2. Partner-path selection is not ELD certification. Closure still requires:

- authorized provider organization/account and real token/scopes;
- written commercial/API integration/support rights;
- exact provider/device/application/firmware/software boundary per jurisdiction;
- separate U.S. registration and Canadian certification evidence;
- authentic HOS/ELD source data and applicable inspection/transfer behavior;
- exact-SHA customer/driver/back-office Chrome evidence;
- two independent qualified regulatory perspectives for the P0 jurisdictional boundary plus Security/SDET/Fleet Product acceptance.

Certified ELD/HOS remains ROADMAP; supporting HOS structures remain DEVELOPMENT.

## 4. Activated Wave 3

### G3A — OpsTrax HOS operational workflow (#128)

**Branch:** `wave3/g3a-hos-workflow`  
**Capability truth:** DEVELOPMENT until the gate closes.

Engineering may build canonical duty-status, source/provenance, timeline, clock boundaries, unidentified driving, edits/annotations/certification, special-status gates, malfunction/diagnostic workflow, inspection/transfer interfaces, dispatch remaining-hours warnings, authorization, audit, accessibility and evidence harnesses.

Provider-dependent behavior must fail closed or remain explicitly unavailable until G2B supplies the certified source. No synthetic/provider-independent event is relabelled as certified automatic driving.

Required SMEs: CTO; Principal U.S. ELD/HOS Regulatory; Canadian Regulatory where applicable; Driver Operations; Fleet/TMS Product; Backend/Data; Principal SDET; Security; Enterprise UI/UX; SRE. Two independent qualified regulatory perspectives are mandatory for any P0 jurisdictional release claim.

### G3B — dual-facing camera partner integration (#129)

**Branch:** `wave3/g3b-dual-camera`  
**Capability truth:** ROADMAP until the gate closes.

Engineering may build the canonical video-event envelope, provider adapter contract, clip-reference authorization boundary, event/context UI, privacy/retention/access enforcement points, audit, idempotency/recovery, storage/observability and truthful provider-pending states.

Acceptance still requires a real authorized provider account/API/SDK, exact road+driver camera hardware/model/firmware, authentic events/clips, correct tenant/vehicle/driver/trip/time/location linkage, privacy/retention enforcement, failure/recovery, visible Chrome and independent Privacy + Security + SDET + Driver Safety Product acceptance.

Required SMEs: CTO; Principal Video Telematics/Safety; Camera Hardware; Privacy; Cybersecurity; Storage/SRE; Principal SDET; Driver Safety Product; Enterprise UI/UX; Data/Analytics; Commercial/Customer Success as partner terms mature.

## 5. Shared execution controls

- Apply Appendix B as binding governance for every active gate.
- Implementation teams may submit evidence but may not certify their own work.
- P0 domains require at least two independent expert perspectives.
- Every defect follows Observe -> Evidence -> Root Cause -> Fix -> Test -> Exact-SHA Deploy -> Same Journey Retest -> Close.
- Visible Chrome + real persisted data remain the customer acceptance surface.
- Provider claims require real provider-account evidence.
- Hardware/video claims require real exact hardware evidence.
- ELD/HOS claims require jurisdiction-specific regulatory evidence.
- UI/UX shared density/overflow controls remain application-wide and are verified within each active customer-facing gate.
- No fabricated telemetry, HOS, video, provider, device or regulatory evidence.
- No Capability Truth Matrix promotion until the owning gate formally closes.

## 6. CTO disposition at activation

- **G2A #115:** HOLD/open — PILOT; external provider evidence required.
- **G2B #116:** HOLD/open — DEVELOPMENT/ROADMAP; external commercial/provider/regulatory evidence required.
- **G3A #128:** ACTIVE — DEVELOPMENT; engineering/readiness may proceed now.
- **G3B #129:** ACTIVE — ROADMAP; engineering/readiness/provider selection may proceed now.
- **Wave 4–6:** LOCKED.

This controlled overlap is intended to accelerate commercialization without falsifying Wave 2 completion. Formal Wave 2 GO remains evidence-driven and will be recorded separately when its external acceptance gates actually pass.
