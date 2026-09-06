# CR 2026 09 06 01 Software Completion and External Hardware Hold

**Classification:** Class 2 operating-model consolidation
**Authority:** Explicit CTO/program-owner direction on 2026-09-06 to complete, verify and certify the software-controlled work while hardware procurement remains the final external testing layer
**Entry baseline:** `main@0af7b14ecf0fde7e833a1db4bb36025746099137`
**Governing document:** OpsTrax Accelerated Hardening Factory v2.0, controlled plan revision 2.7
**Parent tracker:** #110

## Decision

Complete all useful software-controllable work across camera, sensors, trackers, device operations, J1939, PT40/OEM, providers and related customer workflows. Do not wait for physical procurement when fixtures, simulators, persistence, security, failure/recovery, deployment and browser work can proceed honestly.

Use **SOFTWARE VERIFIED** and **SOFTWARE READY FOR EXTERNAL CONFIRMATION** only for the exact declared software boundary. Physical hardware, provider and regulated capability claims retain their original evidence requirements and remain **EXTERNAL HOLD** until the evidence exists.

## Controls

1. Run up to six bounded engineering squads and at most two frozen certification candidates.
2. Use BUILD, INTEGRATE and CERTIFY as separate speeds.
3. Batch related defects by vertical module.
4. A queued human reviewer blocks promotion, not unrelated engineering.
5. Independent SDET and platform hardening remain standing functions.
6. Use P0/P1/P2/P3 policy and stop affected lanes on P0.
7. Do not perform a complete certification deployment after every minor fix.
8. Require exact-SHA browser, real persisted data and production-shaped evidence only at frozen candidates.
9. Require authentic provider, physical device and jurisdiction-specific regulatory evidence for those claims.
10. Maintain truthful POC labels and fail closed when provider/media/hardware authority is absent.

## Immediate capability effect

None. This record authorizes work and scoped software dispositions. It does not certify any camera, sensor, GT06, PT40, CAN/J1939, OEM, Samsara, ELD/HOS or other external capability.

## Preserved governance

Prior exact-SHA evidence, limited-pilot restrictions, commercial-truth rules, Appendix B requirements, independent assurance, zero-P0/P1 release rules, provider/device/regulatory evidence requirements and change-control history remain binding. Conflicting serial or pause mechanics are superseded.
