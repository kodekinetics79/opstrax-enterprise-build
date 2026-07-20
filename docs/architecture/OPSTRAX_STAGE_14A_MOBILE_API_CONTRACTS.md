# Opstrax Stage 14A Mobile API Contracts

## Contract Rules

- Use the centralized backend auth session only.
- Preserve tenant/company scoping from the authenticated backend context.
- Treat every write as retryable only when the backend contract is already idempotent or replay-safe.
- Show honest empty states when the backend has no data yet.
- Never fabricate a successful action.

## Core Route Families

| Family | Primary Routes | Notes |
|---|---|---|
| Authentication | `/api/auth/login`, `/api/auth/me`, `/api/auth/refresh`, `/api/auth/logout` | The session comes from the backend, not the client. |
| Operational summary | `/api/operations/jobs/{jobId}/execution-summary` | The dashboard and workflow tabs consume the live summary. |
| Smart assignment | `/api/jobs/{jobId}/smart-assign/recommendations`, `/api/jobs/{jobId}/smart-assign/recommend`, `/api/smart-assign/recommendations/{id}/accept`, `/api/smart-assign/recommendations/{id}/reject` | Recommendation-only AI; no auto-assign. |
| Site access | `/api/jobs/{jobId}/site-access`, `/api/site-access/{id}` | Gate pass and NOC remain explicit records. |
| Pickup authorization | `/api/jobs/{jobId}/pickup-authorizations`, `/api/pickup-authorizations/{id}` | Third-party handoff stays tenant-scoped. |
| Warehouse handover | `/api/jobs/{jobId}/warehouse-handovers`, `/api/warehouse-handovers/{id}` | No full warehouse portal. |
| Proof | `/api/jobs/{jobId}/proof-packages`, `/api/proof-packages/{id}`, `/api/proof-packages/{id}/submit`, `/api/proof-packages/{id}/validate` | Proof stays controlled and auditable. |
| Proof artifacts | `/api/proof-packages/{proofPackageId}/artifacts` | Evidence metadata only, no fake uploads. |
| Billing confidence | `/api/proof-packages/{proofPackageId}/billing-confidence` | Read-only trust signal only. |
| Telemetry | `/api/telemetry/live-map-summary`, `/api/telemetry/assets/live-state`, `/api/telemetry/assets/{vehicleId}/live-state` | Read-only visibility preview. |
| Safety / maintenance | `/api/safety/dashboard`, `/api/maintenance/dashboard` | Mobile preview of live health. |

## Response Shape

- The client expects the backend envelope shape with `success`, `data`, `message`, and `errors`.
- The mobile API client retries once on 401 after refresh when possible.
- The app only renders fields returned by the backend.

## Data Minimization

- The mobile UI shows only the fields required for the current role.
- Internal-only or cross-tenant data remains hidden by design.

