# CR-2026-09-03-03 — Certification-First Completion

**Class:** Class 2 sequence/prioritization change  
**Authority:** CTO / Program Owner explicit instruction on 2026-09-03 to complete the certifications originally started and return to the marked certification path  
**Parent:** #110  
**Entry main:** `547f482dbf47e6f442c5d1f3e3b23806a49872cf`  
**Status:** APPROVED FOR EXECUTION

## Decision

OpsTrax returns to certification-first execution. The program will not treat a time-limited LIMITED GO, an external-closure hold, a deferred gate, or completed engineering readiness as equivalent to the certifications originally targeted.

The two major certification lanes are restored as the critical path:

1. **G1A — Fleet Identity / Asset Master + Telematics / DeviceOps full certification completion** under #108, using the current exact candidate and the original evidence requirements.
2. **G1B — GT06 physical compatibility certification** under #109, beginning with candidate acquisition/freeze and proceeding through protocol identification, bench, vehicle, failure/recovery, soak, security, Certified Compatible and Production Supported evidence.

## Superseded execution priority

The concurrency permissions in `CR-2026-09-03-01` and `CR-2026-09-03-02` are superseded only for **critical-path execution priority**. Their completed code, evidence and truthful capability classifications are preserved.

- G2A/G2B remain open evidence gates but move to **HOLD / PREPARATION ONLY** while G1A/G1B consume the two major certification lanes.
- G3A/G3B/G4A/G4B move to **PAUSED / PREPARATION ONLY**. No new merge-bound shared-production-code feature progression occurs until G1A/G1B are materially closed or the CTO records a new formal change.
- Existing P0/P1 fixes that protect security, tenant isolation, data integrity or release safety may still be performed when they are prerequisites for G1A/G1B certification.

## G1A re-entry rule

The 30-day `CR-2026-09-01-02` LIMITED GO remains a historical bounded pilot authorization only. It is **not** certification and does not satisfy the current objective.

G1A closes only after the current exact candidate has:

- zero open P0/P1;
- zero tenant/branch leakage;
- zero Driver/Customer internal-route exposure;
- no fabricated telemetry/diagnostics;
- applicable Company Admin, Fleet Manager, Dispatcher, Maintenance, Executive, Driver and Customer journeys on visible Chrome with real persisted certification data;
- large-fleet search/filter/sort/paging/export and representative performance evidence;
- Device Health, GPS/Live Map, geofence and persisted diagnostics truth;
- responsive/accessibility and failure/recovery evidence;
- exact frontend/API SHA identity on the final run;
- independent assurance required by Appendix B, with implementation teams prohibited from self-certifying.

No remaining LIMITATION or waiver may be silently converted into CERTIFIED status.

## G1B re-entry rule

`CR-2026-09-01-01` is superseded as a deferral. G1B is reopened at **Candidate acquisition/freeze**. Until an exact physical tuple is frozen, the gate is ACTIVE-BLOCKED and no hardware certification claim is allowed.

Required path:

1. Freeze at least two identical production-candidate units, preferably three, with exact manufacturer/model/hardware-revision/firmware/modem/radio/procurement identity.
2. Confirm real protocol behavior from device-originated bytes.
3. Bench identity/login/ACK/GPS/heartbeat/supported events/reboot/reconnect end to end.
4. Controlled vehicle route vs reference GPS, freshness/geofence/speed/heading where supported.
5. Power/network/GPS loss, duplicate session and server restart recovery.
6. 24-hour soak for **Opstrax Certified Compatible**.
7. Security/provisioning/tenant-binding/replay/session review.
8. Publish exact capability/limitation certification record.
9. 72-hour soak plus repeatable install/procurement/support/RMA process for **Production Supported**.

Simulator/software evidence remains supporting only and cannot close physical stages.

## Evidence and staffing

Apply Appendix B as binding governance. Activate the mandatory specialists for each active gate, require independent assurance for critical claims, prohibit implementation teams from self-certifying their own work, and keep CTO GO / LIMITED GO / NO-GO authority.

Visible Chrome, real persisted data, physical hardware, provider accounts and regulatory evidence remain mandatory wherever the corresponding claim depends on them.

## Commercial truth during execution

Until formal gate closure:

- Fleet Identity / Asset Master: **PILOT**
- Telematics / DeviceOps: **PILOT**
- GT06 software: **PILOT**
- GT06 hardware: **NOT CERTIFIED**
- Samsara connector: **PILOT**
- HOS workflow: **DEVELOPMENT**
- Certified ELD/HOS: **ROADMAP**
- Dual-facing camera: **ROADMAP**
- Video Safety/provider breadth: **ROADMAP**

Sales, website, proposals and product documentation may not exceed these classifications until the owning gate formally closes.
