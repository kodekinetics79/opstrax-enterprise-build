# OpsTrax Commercialization Capability Truth Matrix

Baseline: `155b54a3451c2a4618b4fc6a87fd59f0e68f425d`  
Program tracker: #110  
Current certification closeout: #108
Approved sequence change: `CR-2026-09-01-01` defers #109/#114 without certification

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
| Fleet identity: vehicles, drivers, branches, assets | PILOT | Real DB-backed lifecycle/import/RBAC/history. Module 1 closeout still required. |
| Large-fleet data handling | PILOT | Prior Chrome evidence at 1,000 vehicles / 1,250 drivers / 1,100 devices / 300 assets; current-SHA closeout pending. |
| Tenant / branch isolation and RBAC | PILOT | Strong RLS and role controls; must be re-proved on final exact SHA. |
| Device registry and lifecycle | PILOT | Provision/install/commission/transfer/suspend/revoke/archive/history exist. |
| Native authenticated telemetry ingest | PRODUCTION READY | Signed ingest, timestamp/nonce/replay controls, tenant-bound resolution and ordered projections exist. Provider/device evidence is separate. |
| Protocol gateway architecture | PRODUCTION READY | Real TCP/protocol-gateway path exists; each protocol/device family requires separate compatibility certification. |
| GT06 protocol software | PILOT | Strong parser/session/replay/concurrency evidence and an isolated exact-SHA test listener exist. #109/#114 were closed as deferred under `CR-2026-09-01-01`; no physical GT06 combination is certified or production supported. |
| GPS / latest position / live state | PILOT | Real persistence/SSE/maps; production usefulness depends on a commissioned real feed. |
| Geofences / telemetry alerts | PILOT | Real evaluation and persistence; closeout evidence pending. |
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

## Current active and deferred release gates

1. #108 — close Module 1 and Module 2 against a frozen exact SHA using real persisted data and visible Chrome.
2. #109 / PR #114 — **NO-GO / DEFERRED / CLOSED WITHOUT CERTIFICATION** under `CR-2026-09-01-01`; re-open only when an authorized exact physical GT06 candidate is available.

Do not begin another broad application audit while #108 remains open. Wave 2 remains queued until G1A receives formal GO or LIMITED GO after its remaining acceptance evidence, including A-02, is independently accepted. Later capability tracks remain subordinate to #110.
