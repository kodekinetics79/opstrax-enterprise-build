# OpsTrax Commercialization Change Control Log

This log records approved changes to the sequencing, gate boundaries, evidence requirements, or commercial claims governed by the Master Commercialization & Certification Action Plan. A change record never substitutes for evidence that the affected capability would otherwise require.

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
