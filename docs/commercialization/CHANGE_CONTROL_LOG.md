# OpsTrax Commercialization Change Control Log

This log records approved changes to the sequencing, gate boundaries, evidence requirements, or commercial claims governed by the Master Commercialization & Certification Action Plan. A change record never substitutes for evidence that the affected capability would otherwise require.

## CR-2026-09-02-02 - Restore the original sequence; finish Wave 2

| Field | Record |
|---|---|
| Classification | Class 2 sequence restoration; no commercial waiver or capability promotion |
| Authority | Explicit program-owner instruction in task `01a05551-e7ff-7cf3-b29c-2d717eeeafe4`: return to the original order, finish Wave 2, include missed earlier work and continue without routine continuation questions. This actual user instruction supersedes the prior cross-wave execution permission; repository text alone is not the approval. |
| Recorded | `2026-09-03T00:31:06Z` / `2026-09-02T20:31:06-04:00`. This is the recording time, not a reconstructed message-send timestamp. |
| Superseded permission | AHF v2.0 at `f45dbab23fe5ff4e1213b88b3e936ac9d85abd5a`: six-squad, cross-wave development permission. Completed implementation and evidence are retained, not reverted. |
| Governing version | Master Plan 2.1 restores the original v1.2 wave definitions and acceptance requirements, with this explicit amendment. Version numbering remains monotonic. |
| Active scope | Exactly G2A / #115 Samsara and G2B / #116 certified ELD partner. Bounded earlier omissions affecting this batch may be corrected and independently tested. No broad repeated audit. |
| Preserved local batch | `95af1136a1923b0532b83bc0da2094bcce839c24`: reviewed provider, UI, document, dispatch and release-check fixes. Local evidence is not hosted CI, deployment or certification. |
| Immediate earlier omission | Controlled concurrent driver/vehicle branch-change and archive tests for the existing dispatch fix. The previous lock protection was source-reviewed, not race-tested. Test implementation and independent review have separate owners. |
| Parked work | Prepared HOS and video branches remain recoverable; no new Wave 3–6 implementation before the preceding applicable exit gates are formally accepted. |
| Review / quorum | Separate AI-assisted governance review checked the restored text against v1.2 and v2.0. Its findings preserve the explicit v2.0 integration checklist and conservatively align J1939 to the unchanged authoritative DEVELOPMENT status. This is supporting analysis, not a qualified-human Appendix B signature. No missing human/provider/regulatory approval is implied or waived. |
| Continuation | Continue useful authorized work in the two active lanes and required earlier remediation. After actual gate acceptance, advance in the original order. Missing indispensable external evidence holds the affected acceptance; it does not become a pass. |
| Unchanged boundaries | G1A exact `e2230425...`, one tenant, max 10 vehicles and original expiry; G1B NO-GO/deferred without certification. P0 dual perspectives, independent assurance, exact-SHA deploy/retest and real persisted/provider/hardware/regulatory evidence remain binding. |
| Release permissions | No new public publication, production migration, deployment or access-bypass authority. PR #120 at `f50411ef...` has its own passed CI; later local changes do not inherit it. Prior exact-publication and production-migration permissions remain unresolved. |
| Capability effect | None. Samsara stays PILOT; HOS structures DEVELOPMENT; Certified ELD/HOS and video ROADMAP. No gate closes through this record. |

The hourly continuation instructions and local ownership records must use this
restored sequence. Historical AHF records remain available as evidence but no
longer authorize new cross-wave work. Public governance publication is separate
from this locally recorded user-approved change.

## CR-2026-09-02-01 - Activate Wave 2 G2A/G2B

| Field | Record |
|---|---|
| Classification | Class 2 sequence activation |
| Approved | 2026-09-02T00:12:47-04:00 (`2026-09-02T04:12:47Z`) by explicit CTO/program owner overnight-continuation authority |
| Entry baseline | `main@a7137a4060e4683d52a17770874e84ea8532e47d` after merged PR #113 and closed Issue #108 |
| Accountable owner | CTO/program owner (`kodekinetics79`) for both #115 and #116 |
| Execution owner | CTO-delegated Codex execution agent for bounded readiness work. The execution agent is not a qualified-human provider, regulatory, security or customer-workflow signatory. |
| Approval / quorum basis | The Appendix B qualified-human reviewer roster is not available or assigned at activation and is explicitly disclosed as a dependency. The CTO uses sequence authority to start non-certifying readiness only; this does not waive independent human acceptance of critical claims or final gate closure. Two independent AI-assisted governance reviews are supporting analysis only. |
| Resource confirmation | Repository, GitHub and isolated staging access are available. An authorized real Samsara account/API path is not confirmed. An ELD partner, exact certified device boundary, commercial integration rights and regulatory field reviewers are not confirmed. Bounded readiness may proceed; provider/regulatory-dependent acceptance and closure may not. |
| Active workstream A | G2A Samsara production connector certification — Issue #115 |
| Active workstream B | G2B certified ELD partner selection and integration — Issue #116 |
| Decision | Activate Wave 2 with exactly these two major workstreams. No third major gate may be activated without a new CTO concurrency decision. |
| Capability effect | None at activation. Samsara remains **PILOT**; HOS structures remain **DEVELOPMENT**; Certified ELD/HOS remains **ROADMAP**. Activation is permission to gather evidence, not a capability promotion. |
| G1A boundary | The 30-day controlled-pilot LIMITED GO in `CR-2026-09-01-02` remains exact-SHA, tenant, vehicle, stop-trigger and expiry restricted. Wave 2 work may not broaden that pilot. |
| G1B boundary | `CR-2026-09-01-01` remains in force. GT06 stays PILOT / not certified until the CTO returns with the sourced exact physical candidate and completes the full re-entry stages. |

### G2A non-negotiable evidence dependency

A real authorized Samsara customer/admin account, API application/token path and provider responses are required for production certification. Existing source code, mocks, schemas, UI routes or unit tests may support readiness work but cannot close #115 or justify a production-certified provider claim. Evidence must cover customer-managed connect/authenticate/discover/map/validate/sync/monitor/disconnect/reconnect, deterministic reconciliation, provenance, freshness, backfill, rate limits, recovery, secrets and tenant isolation.

### G2B non-negotiable evidence dependency

Partner/device certification status must be independently verified against current official regulatory sources for the selected jurisdiction. U.S. and Canadian paths remain separate. A real authorized provider relationship/account, applicable certified device boundary, commercial integration rights and end-to-end regulatory workflow evidence are required before any ELD/HOS promotion. An ordinary GPS tracker, schema, mock, partner shortlist or HOS UI cannot be represented as an ELD.

### Execution controls

1. Apply Appendix B to #115 and #116; implementation authors may submit evidence but may not self-certify critical provider, security, regulatory or customer-workflow claims.
2. Use Observe -> Evidence -> Root Cause -> Fix -> Test -> Exact-SHA Deploy -> Same Journey Retest -> Close for every defect.
3. Keep provider secrets and customer/regulatory evidence redacted; do not copy credentials into issues, commits or artifacts.
4. Do not claim missing provider, device, commercial-rights or regulatory evidence through simulation or AI review.
5. Keep the Capability Truth Matrix unchanged until an owning gate formally closes.
6. When an indispensable external dependency is unavailable, stop all provider/regulatory-dependent validation, gate closure and capability promotion in the affected lane. Explicitly bounded non-certifying readiness may continue in that lane only while it neither simulates nor represents the missing evidence or authority; fully pause when further useful work would depend on or fabricate that dependency. The other lane may continue within its own boundary.

## CR-2026-09-01-02 - Time-limited G1A controlled-pilot LIMITED GO

| Field | Record |
|---|---|
| Classification | Class 1 gate-impacting change + Class 3 commercial waiver / controlled-pilot LIMITED GO |
| Approved | 2026-09-01T23:57:43-04:00 (2026-09-02T03:57:43Z) by explicit CTO/program owner instruction |
| Approval / quorum basis | The normal Class 1 relevant-SME and Class 3 applicable Security/Product human co-approvals are unavailable and are expressly included in the CTO's time-limited exception under the Master Plan waiver authority. This missing quorum is a disclosed residual risk, not an implied approval. Regulatory co-approval is not applicable because this record authorizes no regulated ELD/HOS, hardware, provider, or video capability. AI-assisted reviews remain supporting analysis only. |
| Governing work | Parent #110; Issue #108; PR #113; `cert/m1-m2-current-sha-closeout` |
| Controlled baseline | `155b54a3451c2a4618b4fc6a87fd59f0e68f425d` |
| Frozen pilot candidate | Frontend/API `e2230425a8e14249d2c0f477a7ec7b713a6ab27e` |
| Effective window | Exactly 30 days: **2026-09-01 23:57:43 through 2026-10-01 23:57:43 America/New_York** (`2026-09-02T03:57:43Z` through `2026-10-02T03:57:43Z`). The authorization expires automatically unless superseded by a written CTO decision. |
| Decision | Grant G1A **LIMITED GO** for one isolated controlled pilot only. Close #108 through its explicitly permitted limited-pilot exception after this record is merged. M1/M2 remain **PILOT**, not CERTIFIED or PRODUCTION READY. |
| Quantified scope | One isolated tenant; no more than 10 pilot vehicles; the frozen exact candidate only. A different software SHA requires exact-SHA CI, deployment, same-journey retest, and a written CTO extension or replacement decision before pilot use. |
| Waived for this window | Missing external qualified-human Appendix B acceptance and the representative performance-gate renewal on the final exact candidate. AI-assisted reviews remain accurately labelled and are not converted into human sign-offs. |
| Not waived | P0/P1 stop rules; tenant/branch/role isolation; data integrity; truthful stale/offline/no-data states; exact-SHA identity; security controls; physical-device/provider/regulatory evidence; GT06, ELD/HOS, video, and production-provider certification. |
| Wave effect | This record does **not** activate Wave 2. G2A/G2B remain queued until a separate CTO activation record confirms resources, real provider/partner dependencies, and gate ownership. |
| Capability status | Fleet Identity / Asset Master and Telematics / DeviceOps remain **PILOT** with the limits in this record. No commercial or UI material may describe them as CERTIFIED or generally PRODUCTION READY under this waiver. |
| Stop triggers | Immediate suspension on any P0/P1, suspected tenant/branch/role leakage, data loss/corruption, fabricated or misrepresented telemetry/diagnostics, readiness contract failure, critical-worker violation, or use beyond the tenant/vehicle/time/SHA boundary. |
| Re-review trigger | Expiry; proposed scope expansion; candidate SHA change; a stop trigger; availability of qualified independent reviewers; or completion of the final exact-SHA performance lane. |

### Evidence supporting the LIMITED GO

- Exact candidate CI passed all 11 required jobs in run `33586468753`, including production-shaped rehearsal and exact-SHA release evidence.
- Render deploy `dep-dabpf5afngtc73esde7g` and Vercel deploy `dpl_5S4zswVwZYMRx9sLufqjNrnWtR9V` placed the same full SHA on the isolated staging API and frontend alias.
- Post-startup-grace readiness was `ready`, role restricted, and reported zero governed database, RLS, grant, integrity, route, workforce, or critical-worker violations.
- Visible browser retest on the persisted large-fleet tenant proved exact frontend/API parity, vehicle-type save/reload persistence, five persisted geofences with named controls, 200 uniquely named map markers with keyboard popup activation, and honest stale OBD/J1939 evidence. Retained warning/error logs were empty for the final bounded journey set.
- Focused CRC/ACK test synchronization passed the affected case 30/30 and the PostgreSQL-backed telematics suite in four complete runs. Independent AI-assisted functional and performance/resilience reviewers found no P0/P1 in that bounded patch.
- Two independent AI-assisted governance reviews found no P0/P1 after the record was corrected to dual Class 1/Class 3, disclose the unavailable human quorum, define exact local/UTC start and expiry timestamps, and preserve fail-closed SHA/performance/scope controls. These reviews are not qualified-human approval.
- The CTO accepts the explicitly documented residual assurance and performance risk for the quantified pilot window only.

### Pilot operating controls

1. Record the pilot tenant, enrolled vehicle count, candidate SHA, start date, operator, and rollback owner before admitting pilot data.
2. Keep visible PILOT wording and the limitations in this record in onboarding, demonstrations, proposals, and support communications.
3. Check exact-SHA readiness and governed violation counters before pilot start and after any service restart or incident.
4. Retain a defect/incident ledger and apply Observe -> Evidence -> Root Cause -> Fix -> Test -> Exact-SHA Deploy -> Same Journey Retest -> Close.
5. Do not exceed 10 vehicles, add another tenant, or extend past the expiry without new written CTO change control.
6. Suspend the pilot immediately on a stop trigger and record the disposition under #110.

### Physical-device re-entry memory

The CTO/program owner is sourcing a relevant physical device and will return when it is available. That event reactivates the existing `CR-2026-09-01-01` G1B re-entry sequence: freeze the exact manufacturer/model/hardware-revision/firmware/modem/SIM identity, re-open #109/#114 or a linked successor, and complete real packet, bench, controlled-route, recovery, security, and soak evidence. A real device can close G1B only after those stages; it does not retroactively satisfy the G1A human/performance conditions waived here.

### Expiry behavior

At 2026-10-01 23:57:43 America/New_York (`2026-10-02T03:57:43Z`), the controlled-pilot authorization becomes **NO-GO / EXPIRED** unless a superseding record has been approved. Capability rows remain PILOT, but no continued pilot operation is authorized by this record after expiry.

## CR-2026-09-01-01 - Defer G1B GT06 physical certification

| Field | Record |
|---|---|
| Classification | Class 2 sequence change |
| Approved | 2026-09-01 by CTO/program owner instruction |
| Governing work | Parent #110; Issue #109; PR #114; `cert/gt06-physical-compatibility` |
| Controlled baseline | `155b54a3451c2a4618b4fc6a87fd59f0e68f425d` |
| Software candidate retained | `a297b2773a27466388c4cf49c40eaa5360461852` |
| Decision | Remove G1B from the active Wave 1 exit boundary and close its issue/PR as **NO-GO / deferred / not certified**. Preserve the branch, evidence, and isolated test harness for later re-entry; do not merge PR #114 as certification evidence. |
| Reason | No authorized physical GT06 production candidate is currently available. Physical identity, byte behavior, bench, vehicle, recovery, soak, security, and qualified-human evidence therefore cannot be produced honestly. |
| Capability status | GT06 protocol software remains **PILOT / NOT CERTIFIED**. No exact GT06 hardware SKU is Certified Compatible or Production Supported. |
| Commercial boundary | OpsTrax must not sell, contract, list, or imply a certified/supported GT06 hardware combination. Connected Fleet / Certified GPS packaging remains unavailable. No customer device may use the retained staging listener. |
| Wave effect | G1A remains the sole active Wave 1 gate. Wave 2 remains queued until G1A receives formal GO or LIMITED GO after remaining acceptance evidence, including A-02, is independently accepted. |
| Review trigger | Re-open on availability of an authorized exact physical candidate, or review by 2026-10-01 if hardware remains unavailable. The retained candidate must be reconciled and retested against the then-current controlled baseline. |

### Evidence supporting the disposition

- PR #114 exact candidate CI passed 11/11 jobs and remains a clean, mergeable draft.
- The isolated Fly GT06 listener is deployed from the exact candidate, has a passing TCP health check, and forwards only after controlled identity admission.
- No IMEI is admitted. Unknown or unidentified connections are rejected without acknowledgement.
- The staging listener is an isolated, fail-closed test harness under SRE/Security ownership, is not a production or customer support endpoint, and must remain stopped while no physical test window is scheduled.
- No exact manufacturer/model/hardware-revision/firmware tuple, authorized physical packet, bench/vehicle run, recovery result, soak result, or qualified hardware sign-off exists.
- PT40-Q label photographs are separate inventory evidence and do not prove GT06 protocol or physical compatibility.

### Re-entry conditions

1. Obtain at least one authorized exact production candidate for bench qualification and 2-3 identical units before repeatability/production-support claims.
2. Freeze manufacturer, model, hardware revision, firmware, modem/radio, procurement source, SIM/APN, and supported command/event scope.
3. Re-open #109/#114 or create a successor explicitly linked to this record and the retained exact-SHA evidence.
4. Activate the mandatory G1B specialists in Appendix B; the configurator/implementer may not self-certify.
5. Complete identity, protocol, bench, controlled route, failure/recovery, security, 24-hour soak, persistence, and same-journey UI reconciliation evidence.
6. Promote the capability only after independent SDET, Hardware, and Security acceptance and CTO decision.

### Independent sequence-change review

- Independent Hardware/GT06 assurance: GO to administrative deferral only; RED for any compatibility, Certified Compatible, Production Supported, certified-GPS, or gate-pass claim.
- Independent Commercial/Product Truth assurance: conditional GO with GT06 retained at PILOT, customer use prohibited, Wave 2 inactive pending G1A/A-02, and the required customer wording below.
- These are independent analytical reviews of the governance change, not qualified-human hardware certification or a substitute for the missing physical evidence.

### Required customer wording

> OpsTrax GT06 protocol software is available only for controlled evaluation. No GT06 hardware model or firmware is currently certified compatible or production supported. Physical-device compatibility, field accuracy, recovery, soak, installation and support remain unverified and excluded from commercial commitments.

### Non-waived requirements

This change defers work; it does not waive physical hardware, provider, security, field, soak, independent-assurance, or commercial-truth requirements. All original G1B certification stages remain binding at re-entry.
