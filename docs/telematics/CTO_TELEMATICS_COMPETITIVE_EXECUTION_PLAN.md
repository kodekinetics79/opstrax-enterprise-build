# OpsTrax Telematics — CTO Competitive Execution Plan (Device Health, GPS Tracking, OBD/J1939)

**Date:** 2026-08-03
**Scope:** Full execution blueprint for pilot-first delivery, mapped to market competition, control ownership, and product gaps.

This is the “no-narrowing” plan. It keeps market parity with major players in scope (Samsara / Geotab / Motive class) while also layering in enterprise-only differentiators in product quality, trust, and proof.

## 1) Current baseline now (what is truly real in product)

- Telemetry ingest and trust controls are real: HMAC signatures, replay protection, tenant scoping, freshness + status derivation.
- Device lifecycle has live backend operations: provision/revoke/assign/unassign/suspend/activate patterns are actually callable.
- Live tracking and map surface is real: stream + positions + breadcrumbs + map overlays + geofence events.
- OBD/J1939 parsing and normalization path exists in backend, including DM1/DM2/J1939 severity workflows and idempotent fault projection.
- Control-plane gates exist at two levels:
  - **Platform-admin entitlements** in `tenant_entitlements` + `entitlement_policy_mode`.
  - **Tenant permissions/roles** for feature and action visibility.

What is still a deliberate “show with reason / no-op / hard stop”: device install workflow, on-demand diagnostics command, OTA scheduling, alert-level device close, and some deeper sensor/cycle analytics.

## 2) Market comparison: what leaders do vs where we are

### 2.1 Device Health (CTO priority layer)

| Capability | Market leader expectation | Current state | Gap owner | Action
|---|---|---|---|---|
| Device identity and secure onboarding | One-time credentials, rotating secrets, audit-linked onboarding, installation evidence | Provision/credential issuance is implemented | Installation evidence + install lifecycle states | Complete evidence timeline now for pilot-critical operations
| Device lifecycle controls | Assign, transfer, revoke, deprovision, offline lock, audit logs | Assign/revoke/provision are real; update/edit coverage is partial | Update persistence for metadata + lifecycle audit | P0: limit edit to supported fields; add explicit action contract
| Health confidence | Per-device trust score with source + last valid fix + anomalies + escalation intent | Derivation exists from real signals | No consolidated action trail yet | P1: add trust score + exception context timeline
| Operations handoff | Fault, alert, and recovery chain with operator evidence | Recovery hold/resolve exists | Action handoff to maintenance/dispatch incomplete | P1: close-loop handoff workflow

### 2.2 GPS Tracking

| Capability | Market leader expectation | Current state | Gap owner | Action
|---|---|---|---|---|
| Live + history UX | Live wall, replay, stale alerts, map overlays, trip-linked event timeline | Live and replay primitives are present | Trip-centric replay story incomplete | P0/P1: implement journey artifact and route/stop context
| Route optimization | ETA delta, traffic awareness, no-go penalties, exceptions | Deterministic route preview exists, limited traffic/constraint depth | Optimization depth + risk-aware alternatives | P1/P2: confidence-aware route engine
| Geofence control | Dwell, near-miss, oscillation suppression, escalation policy | Basic entry/exit + events exist | Dwell and policy escalation sophistication | P1: add dwell/escalation state + action mapping
| Operations visibility | Unified operational timeline and shared incident graph | Separate streams per surface | Cross-surface timeline is partial | P1: unified timeline + rule actions

### 2.3 OBD/J1939

| Capability | Market leader expectation | Current state | Gap owner | Action
|---|---|---|---|---|
| Decoding + interpretation | Real-time DTC decoding + recurrence trends + service intent | Decoder + normalizer is strong; ingest endpoints for fault projection are present | Real-time command channel and maintenance action orchestration incomplete | P0: connect command/closure evidence path
| Fault to action | Auto-work-order, predicted risk, recurrence patterns | Fault evidence is present; workflow loop partial | Maintenance/dispatch linkage | P1: closed-loop work-order creation and root-cause capture
| Predictive maintenance | Remaining useful life, recurrence probability, service forecast | Early groundwork only | Predictive pipeline incomplete | P2: production-ready reliability scoring and recommendations

## 3) Market gaps we can own before others (enterprise moat)

1. **Telemetry Trust Index**
   - Position freshness + signal confidence + ingestion trust + route consistency + manual tamper anomaly.
   - Use this as a first-class KPI in dashboard and alert thresholds.

2. **Evidence-first operations**
   - Every action must emit: who/why/which-rule/device/event, start/end, evidence link.
   - This is a procurement differentiator for enterprises.

3. **One operational timeline across domains**
   - Merge telematics + geofence + safety + maintenance in one sequence by asset + trip.
   - This is where we reduce dispatch-to-resolution time.

4. **Readiness-to-decision UX**
   - “Can this asset be dispatched now?” scorecard with hard evidence and override history.

## 4) Control ownership map (for pilot and enterprise sales governance)

### 4.1 Platform Admin controls (hard stop)
- **Entitlement policy and catalog**
  - `tenant_entitlements` + `entitlement_policy_mode` (`/api/platform/tenants/{id}/entitlements`, `/api/platform/tenants/{id}/entitlement-policy`).
  - Effective route blocking by module in API middleware (`ModuleKeyForPath`) for keys: telematics / safety / maintenance / dispatch / crm / etc.
- **Platform-wide pricing and module economics**
  - Governing controls in the module catalog and package reconciliation stack.
- **Connector and environment risk controls**
  - `/api/integrations` is separately gated by `integrations` entitlement so connector spend can be controlled without disabling core telematics.

### 4.2 Tenant admin controls (operational)
- **Role/permission envelope**
  - Telemetry actions gate on `PERMISSIONS` and aliases in frontend + `Authorization` checks in API contracts.
  - Device action permissions: create/update/delete/assign/diagnostic/firmware/Providers-manage.
- **Tenant policy choices**
  - Alert thresholds, map/rule policy, feature flags, retention preferences, escalation recipients.
- **Operational assignment controls**
  - Provisioning, assignment/unassignment, recovery open/resolve, archive/revoke.

### 4.3 Tenant-visible but still controlled surfaces
- Navigation tabs and UI states should match entitlement + permission model.
- Any unsupported feature must show an explicit non-functional state (disabled with reason + link to rollout status), never fake success.

## 5) Submodule completion playbook (feature-by-feature)

### Device Health execution
- **P0:**
  - Keep unsupported actions in “explicitly blocked” state.
  - Convert edit dialog to persist only supported fields and move all unsupported metadata into a “Read-only / evidence-backed” panel.
  - Add one-row action contract chip per device: `connected`, `attention`, `audit`, `controls`.
- **P1:**
  - Assignment history + command audit timeline endpoint and UI.
  - “Readiness confidence” widget with reasons and recency details.

### GPS Tracking execution
- **P0:**
  - Add trip-level journey artifact with breadcrumb replay + stop context + geofence transitions.
  - Surface position source and confidence (raw fix age, gateway delay, protocol source).
- **P1:**
  - Dwell + oscillation + geofence false-positive suppression.
  - Route-level risk hints and ETA uncertainty display.

### OBD/J1939 execution
- **P0:**
  - Build per-reading timeline endpoint in UI from real fault stream grouped by trip/vehicle and open event linking.
  - Show recurring fault cluster insights (same DTC across time) on device detail.
- **P1:**
  - Connect diagnostic incident to maintenance/work-order and closure evidence.
  - Add recurrence and severity score for maintenance pre-clearance.

## 6) What we should prioritize to beat competitors next (non-narrow)

1. **Route Recovery Guidance** (low complexity, high executive value)
   - Instead of just showing deviation, suggest next action: “reroute, hold at next stop, escalate driver message.”

2. **Fleet trust packet export**
   - Customer-ready PDF/CSV containing evidence of signal, status changes, decisions, and action timestamps.

3. **Customer-facing operational confidence**
   - Public ETA improvements only when confidence is high; else show clear caveats automatically.

4. **Automation with human override**
  - Auto-assign recovery workflows + optional auto-lock actions for high-risk conditions.

## 7) SME consolidated review (broad review from parallel experts)

- Device Health: close-loop realism > feature quantity. Hide unsupported features, add audit-first evidence paths first.
- GPS Tracking: market parity exists on live map and core telematics; competitive edge is journey analytics + escalation intelligence.
- OBD/J1939: normalization is strong; missing is command, recurrence interpretation, and maintenance action closure.

## 8) Delivery checklist for your full-speed pilot

### Immediate (next 5 business days)
1. Finalize action-contract consistency on Device Health and remove any implied success on unsupported operations.
2. Publish a controlled “telematics delivery matrix” before every sales demo.
3. Validate entitlement behavior end-to-end:
   - disable `telematics` entitlement as Platform Admin, confirm API blocks and UI fallback.
   - confirm no action triggers backend success if it is not implemented.
4. Keep `Device Health`, `GPS`, and `OBD/J1939` detail views in a single “evidence-first” shell.

### Next 2–6 weeks
1. Implement unified operational timeline event envelope and task handoff links.
2. Add assignment + audit history for devices.
3. Add provider sync health and route/replay artifact.
4. Add recurring fault + trip correlation for OBD/J1939 and pre-open maintenance tasks.

### Next quarter
1. Trust index-based route actioning.
2. Predictive maintenance and exception automation.
3. Driver-facing closure module (status + evidence capture + photo/sign-off where required).

This blueprint is sales-ready and execution-oriented: it keeps today’s delivered core stable, avoids overpromising, and deliberately attacks the enterprise value gap with trust, evidence, and decision quality rather than adding button noise.

## 9) Platform-admin sign-in runway (critical for pilot access)

### What we validated in browser checks
- `/platform/login` submits to `POST /api/platform/auth/login`.
- Real backend responses are now visible in UI (e.g., `Invalid credentials`).
- `Too many failed attempts` returns from backend as lockout and is now explicitly surfaced in `PlatformLoginPage` with a direct recovery instruction.
- The route is a dedicated platform staff portal (`platform admins`, not tenant users).

### Immediate control policy for stable demos
1. Use a dedicated platform admin test account for demo and keep tenant login separate.
2. Avoid repeated wrong-password attempts in front of customer view; even one operator should own retries.
3. If lockout appears, pause and retry after ~15 minutes from the same source or reset through platform admin operations.

### Why we added this now
- It removes a hidden failure mode in the sales flow.
- It improves operator confidence before platform governance walkthroughs.
- It keeps the enterprise control surface accessible and recoverable without over-guessing backend behavior.
