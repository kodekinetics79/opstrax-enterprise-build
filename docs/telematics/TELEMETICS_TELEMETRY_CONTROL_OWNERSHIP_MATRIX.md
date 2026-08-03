# OpsTrax Telemetry Control Ownership Matrix (Sales / Demo Artifact)

**Date:** 2026-08-03  
**Scope:** Device Health, GPS Tracking, OBD/J1939 (pilot-ready + enterprise hardening)

Use this as the executive control sheet before every demo and proposal review.

## 1) Control Ownership (single page)

| Area | Control | Owner | Customer Visibility | Evidence / API Surface | Current Status |
|---|---|---|---|---|---|
| Platform Governance | Enable/disable telematics module, policy mode, entitlements | **Platform Admin** | Yes (as product scope and blocked flows) | `/api/platform/tenants/{id}/entitlements`, `/api/platform/tenants/{id}/entitlement-policy` | ✅ in place |
| Platform Governance | Integration catalog shape, billing boundaries, premium connector policy | **Platform Admin** | Partial (UI messaging + feature visibility) | `/api/integrations` + module catalog settings | ✅ in place |
| Device onboarding | Provision, credential issuance, row lock state (revoked/active) | **Tenant Admin** | Yes | Provision service + `telemetry` read/write endpoints | ✅ in place |
| Device lifecycle assignment | Assign / unassign / revoke / archive | **Tenant Admin** | Yes | `telematicsService.assignDeviceToVehicle`, `unassignDevice`, `archiveDevice` | ✅ in place |
| Device recovery loop | Open/resolve attention with evidence notes | **Tenant Admin** | Yes | `markDeviceAttention`, `resolveDeviceAttention`, alerts feed | ⚠️ readiness depends on fault/alerts feed quality |
| OBD/J1939 commands | On-demand diagnostics command | **Tenant Admin** *(with future provider capability)* | No (blocked) | Service method currently no-op (status surfaced) | ⚠️ not connected |
| OTA/firmware scheduling | Device firmware scheduling | **Tenant Admin** *(not active yet)* | No | Service method currently no-op | ⚠️ not connected |
| Install checklist | Install evidence and completion state | **Tenant Admin** | No | No active provider endpoint for checklist writes in pilot | ⚠️ not connected |
| Integrations & connectors | Sync health, connected device mapping, catalog sync | **Tenant Admin + Platform controls** | Yes | `/api/integrations` and `telematicsService.getProviders` | ✅ / ⚠️ partially visible |
| Provider auditability | Per-device provider mapping and mismatch visibility | **Tenant Admin** | Yes (new detail row) | `DeviceDetail.providers` (derived from /api/integrations + device provider) | ✅ now added |
| Security & trust controls | Session scoping, entitlement checks, lockout handling | **Platform + Tenant RBAC** | Yes (honest disabled states + errors) | `PERMISSIONS`, `canReadEntitledFeed`, lockout messages | ✅ in place |

## 2) Sales-proof row checklist

- Every action must show one of: **Ready / Permission / State / Unavailable**.
- If an action is disabled, show the exact reason; never fake success.
- If connector visibility is missing, present provider-audit row as `Restricted` and keep operational flow continuing.
- Keep telemetry confidence explicit in demo: freshness, signal, and fallback states must be visible.

## 3) “Beat Samsara / Geotab / Motive” differentiation gaps to close next

1. **Decision-grade action telemetry**: action-contract ledger + evidence link per operation (who/why/when).
2. **Provider-confidence overlay**: device-provider matching + unmatched/misconfigured alerts before dispatch calls.
3. **Journey intelligence bundle**: route replay + stop context + exception timeline in one module.
4. **Offline recovery playbook**: structured recovery workflow and handoff to maintenance/dispatch.
