# G1A Full Certification Re-entry Ledger

**Issue:** #108  
**Parent:** #110  
**Authority:** `CR-2026-09-03-03` / Master amendment v2.4  
**Current-main baseline:** `1f3b5de029b33e9315fb96c80988e610665c41b0`  
**Mode:** focused closeout; no broad re-audit  
**Target:** evidence-backed certification decision for Module 1 Fleet Identity / Asset Master and Module 2 Telematics / DeviceOps

## Preserved evidence and current truth

PR #113 and #108 evidence remains valid only within the exact SHA/scope in which it was originally accepted. The prior 30-day LIMITED GO is historical pilot authorization, not certification.

Historical execution already closed a large portion of the product journey: customer and driver portal isolation, large-fleet persisted populations, vehicle/device correction and transfer paths, many role journeys, four base viewport observations, multiple DeviceOps/GPS/diagnostic customer journeys and exact-SHA staging cycles. That evidence is preserved; it is not silently promoted to current-SHA proof.

The current re-entry CI at `6bd87d48c96781999bd486ce1c3237e28524229f` completed green across the full OpsTrax CI pipeline. After G1B support infrastructure merged, this branch was reconciled to current `main@1f3b5de029b33e9315fb96c80988e610665c41b0` and this ledger was recreated on top.

## Exact remaining certification gaps

Only the following gaps may drive new G1A work unless a current exact-SHA journey exposes a new defect:

1. **Exact deployment + authenticated certification run** — deploy the frozen current candidate to the isolated Render/Vercel certification environment; API and UI must expose the same full SHA. Execute the protected staging certification workflow or equivalent governed visible-Chrome run.
2. **Restricted-role branch isolation (P0)** — Fleet Manager/Maintenance or other applicable branch-scoped identity must prove own/foreign/null/deleted/mixed route/data cases. Company Admin positive control does not certify branch isolation. Two independent P0 perspectives are mandatory.
3. **Remaining lifecycle/document evidence** — finish only the correction/document/readiness/expiry/archive/reactivate/retry cases that were not already evidenced on the exact candidate; do not recreate already-closed journeys.
4. **Responsive/accessibility interaction closure** — the four governed base viewports have supporting evidence, but the final candidate still requires the remaining keyboard/focus/touch/nested-scroll/accessibility interaction acceptance on high-frequency M1/M2 surfaces.
5. **Formal representative performance/recovery** — historic one-shot wall timings and 10k/load harnesses are supporting evidence. The final candidate needs the predeclared representative performance gate plus controlled recovery evidence or a written quantified CTO waiver.
6. **Evidence archive quality** — preserve hash-bound captures/results sufficient for the certification package; recording/HAR is required where the governing journey requires it and must not contain secrets.
7. **Qualified Appendix B acceptance** — AI-assisted analytical reviewers remain supporting evidence only. Qualified independent Fleet/TMS Product, Principal SDET, Security/RBAC, PostgreSQL/Data, SRE, Enterprise UI/UX and GIS/Map acceptance must be attached as applicable. Implementers may not self-certify.

## Current protected-staging automation observation

The existing `.github/workflows/staging-iot-certification.yml` is the preferred automated acceptance runner because it requires exact API/UI build identity, protected Staging readiness, authenticated tenant/driver/customer/platform states, cross-tenant state, the original browser persona suite and the guarded real IoT lifecycle.

The first label-triggered run against the re-entry PR failed **before any runner/step executed**; a single retry reproduced the same runner-zero/zero-step result. This is recorded as a staging environment admission/deployment-control boundary, not an application test failure. Do not weaken or remove the protected `Staging` environment merely to make the job start. Resolve the environment/deployment path or dispatch the workflow through the authorized protected route.

## Acceptance

G1A can be marked **CERTIFIED** only when:

- 0 open P0;
- 0 open P1;
- 0 tenant/branch leakage;
- 0 unauthorized Driver/Customer internal-route exposure;
- 0 fabricated telemetry/diagnostic truth;
- required applicable journeys pass on visible Chrome with real persisted certification data;
- current exact frontend/API SHA is captured;
- representative performance/recovery criteria pass or an explicitly bounded quantified waiver is approved;
- final evidence survives refresh/logout-login and independent review;
- mandatory Appendix B qualified acceptance is attached.

Until then M1/M2 remain **PILOT**.
