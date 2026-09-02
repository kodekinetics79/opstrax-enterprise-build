# OpsTrax Commercialization Change Control Log

This log records approved changes to the sequencing, gate boundaries, evidence requirements, or commercial claims governed by the Master Commercialization & Certification Action Plan. A change record never substitutes for evidence that the affected capability would otherwise require.

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
