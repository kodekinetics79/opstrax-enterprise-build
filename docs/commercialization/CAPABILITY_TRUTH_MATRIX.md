# OpsTrax Commercialization Capability Truth Matrix

Baseline: `155b54a3451c2a4618b4fc6a87fd59f0e68f425d`  
Program tracker: #110  
Current controlled-pilot lane: closed #108 / merged PR #113 under `CR-2026-09-01-02`
Active Wave 2 lanes: #115 Samsara production connector; #116 certified ELD partner
Approved changes: `CR-2026-09-01-01` defers #109/#114 without certification; `CR-2026-09-01-02` grants a 30-day G1A controlled-pilot LIMITED GO; `CR-2026-09-02-01` activates G2A/G2B without capability promotion

## Status vocabulary

Only these statuses are permitted in sales, product, certification and roadmap material:

- **CERTIFIED** — exact released build has completed the required browser/operational/provider/device acceptance evidence.
- **PRODUCTION READY** — implementation and operational controls are complete, but a separate external/device/provider certification may still apply.
- **PILOT** — real implementation exists and may be used in a controlled customer pilot with stated limitations.
- **DEVELOPMENT** — implementation exists but required product/integration/field evidence is incomplete.
- **ROADMAP** — not available for customer reliance.

## Current capability truth

| Capability | Status | Evidence / boundary |
|---|---|---|
| Fleet identity: vehicles, drivers, branches, assets | PILOT | Exact `e2230425...` has 30-day LIMITED GO under `CR-2026-09-01-02`: one isolated tenant, max 10 vehicles, through 2026-10-01 23:57:43 America/New_York. Not CERTIFIED or generally PRODUCTION READY. |
| Large-fleet data handling | PILOT | Persisted 1,001-vehicle final-candidate browser evidence exists. Representative final-SHA performance renewal and qualified-human acceptance are waived only inside `CR-2026-09-01-02`; no general scale claim. |
| Tenant / branch isolation and RBAC | PILOT | Exact-SHA automated/adversarial evidence and restricted-role readiness passed. External qualified-human acceptance remains outstanding and is waived only for the quantified `CR-2026-09-01-02` pilot window; any isolation concern is an immediate stop trigger. |
| Device registry and lifecycle | PILOT | Provision/install/commission/transfer/suspend/revoke/archive/history exist. |
| Native authenticated telemetry ingest | PRODUCTION READY | Signed ingest, timestamp/nonce/replay controls, tenant-bound resolution and ordered projections exist. Provider/device evidence is separate. |
| Protocol gateway architecture | PRODUCTION READY | Real TCP/protocol-gateway path exists; each protocol/device family requires separate compatibility certification. |
| GT06 protocol software | PILOT | Strong parser/session/replay/concurrency evidence and an isolated exact-SHA test listener exist. #109/#114 were closed as deferred under `CR-2026-09-01-01`; no physical GT06 combination is certified or production supported. |
| GPS / latest position / live state | PILOT | Real persistence/SSE/maps; production usefulness depends on a commissioned real feed. |
| Geofences / telemetry alerts | PILOT | Real evaluation/persistence and exact-SHA named-control/map retest passed; use remains inside the `CR-2026-09-01-02` controlled-pilot boundary. |
| Telemetry provenance / quality controls | PRODUCTION READY | Device-fix time, gateway receipt, source/provider/protocol and quality/trust controls exist. |
| Samsara connector | PILOT | Real API connector and GPS/engine-state/odometer sync exist; real-account production certification and onboarding workflow remain. |
| J1939 diagnostics | DEVELOPMENT | DM1/DM2 decoder exists; full acquisition/transport/vehicle/device evidence is incomplete. |
| PT40 / Pacific Track | DEVELOPMENT | Adapter seam exists but real device bytes/parser have not been certified. |
| Cold-chain telemetry workflows | PILOT | DB-backed workflow exists; real sensor/provider activation is customer/device dependent. |
| Maintenance / safety operational modules | PILOT | Substantial real implementation; complete module certification remains. |
| Dispatch / TMS workflows | PILOT | Substantial real implementation; later module certification remains. |
| HOS data structures / alert concepts | DEVELOPMENT | Schema/readiness concepts exist; not equivalent to a complete regulated HOS engine. |
| Certified ELD / HOS product | ROADMAP | No OpsTrax-certified U.S./Canada ELD today. Partner-first strategy required. |
| Road-facing / driver-facing video | ROADMAP | No certified production video ingest/retrieval pipeline. |
| AI video safety / coaching | ROADMAP | Requires camera/provider integration and field evidence. |
| Geotab connector | ROADMAP | No production-certified connector. |
| Motive connector | ROADMAP | No production-certified connector. |
| OEM telematics connectors | ROADMAP | No production-certified OEM fleet connector set. |
| Device firmware/SIM/RMA ecosystem | DEVELOPMENT | Some device lifecycle concepts exist; fleet-scale operational lifecycle incomplete. |
| Mobile driver companion | PILOT | Real mobile project/build exists; field and release certification incomplete. |
| SaaS billing / entitlements | DEVELOPMENT | Significant platform foundation exists; commercial packaging and end-to-end activation remain. |

## Commercial rule

A capability may move upward only after the owning release gate has evidence. A UI route, schema table, mock fixture, simulator or passing unit test alone cannot promote a capability to PILOT, PRODUCTION READY or CERTIFIED.

## Current active, limited and deferred release gates

1. #108 / PR #113 — **LIMITED GO / 30-DAY CONTROLLED PILOT** for exact `e2230425...` under `CR-2026-09-01-02`; one isolated tenant, max 10 vehicles, automatic expiry 2026-10-01 23:57:43 America/New_York. M1/M2 remain PILOT.
2. #109 / PR #114 — **NO-GO / DEFERRED / CLOSED WITHOUT CERTIFICATION** under `CR-2026-09-01-01`; re-open only when an authorized exact physical GT06 candidate is available.
3. #115 — **ACTIVE / AMBER** G2A Samsara production connector certification under `CR-2026-09-02-01`; Samsara remains PILOT pending real-account evidence and gate closure.
4. #116 — **ACTIVE / AMBER** G2B certified ELD partner selection/integration under `CR-2026-09-02-01`; Certified ELD/HOS remains ROADMAP and HOS structures DEVELOPMENT pending official regulatory, real partner/device, commercial-rights and end-to-end evidence.

Do not broaden the controlled pilot or repeat an application-wide audit. Continue defect-by-defect evidence under #110, #115 and #116 with at most these two active major Wave 2 lanes. Qualified-human Appendix B acceptance and representative final-SHA performance evidence remain outstanding for unqualified G1A GO; the GT06 physical-device sequence remains deferred until the sourced exact device is available.
