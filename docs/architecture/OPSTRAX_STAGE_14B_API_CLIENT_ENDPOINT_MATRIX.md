# Stage 14B API Client / Endpoint Matrix

| Frontend API Client | Method | Frontend Path | Expected Backend Endpoint | Backend Exists | Auth/RBAC | Tenant Scope | Fake Fallback | Fix Applied | Final Status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `adminApi` | `permissions()` | `frontend/src/services/adminApi.ts` | `GET /api/admin/permissions` | Yes, added in this pass | `users:view` | Tenant-aware admin context | No | Added backend endpoint and removed seed fallback | Working |
| `adminApi` | `overview/users/user/roles/auditLog` | `frontend/src/services/adminApi.ts` | `/api/admin/*` | Yes | `users:view`, `roles:view`, admin permissions | Tenant-aware | No | Removed fake shadow DB fallback | Working |
| `incidentsApi` | `summary/list/detail/timeline/recommendations/...` | `frontend/src/services/incidentsApi.ts` | `/api/incidents/*` | Yes | `safety:view` / `safety:manage` | Tenant-scoped | No | Rewired static timeline/recommendation stubs to real endpoints | Working |
| `fuelApi` | reads/writes | `frontend/src/services/fuelApi.ts` | `/api/fuel/*` | Yes | `fuel:view` / `fuel:manage` | Tenant-scoped | No | Dead seed fallback removed in Stage 14A | Working |
| `safetyApi` | reads/writes | `frontend/src/services/safetyApi.ts` | `/api/safety/*` | Yes | `safety:*` | Tenant-scoped | No | Fake-success create removed in Stage 14A | Working |
| `routesApi` | list/summary/detail/stops | `frontend/src/services/routesApi.ts` | `/api/routes/*` | Yes | dispatch / route permissions | Tenant-scoped | Inert compatibility helper only | No change needed | Working Foundation |
| `customersApi` | list/summary/create | `frontend/src/pages/CustomersPage.tsx` | `/api/customers/*` | Yes | `customers:view` | Tenant-scoped | Inert compatibility helper only | No change needed | Working Foundation |
| `tripApi` | list/detail/start/complete/exception | `frontend/src/services/tripApi.ts` | `/api/trips/*` | Yes | `trip:view` / `dispatch:view` | Tenant-scoped | No | No UI page yet | Working Foundation |
| `telemetryApi` | live map / alerts / devices | `frontend/src/services/telemetryApi.ts` | `/api/telemetry/*` | Yes | telemetry permissions | Tenant-scoped | No | No change needed | Working |
| `maintenanceApi` | dashboard / inspections / work orders | `frontend/src/services/maintenanceApi.ts` | `/api/maintenance/*` | Yes | maintenance permissions | Tenant-scoped | No | No change needed | Working |
| `customerVisibilityApi` | shipment sharing / tracking | `frontend/src/services/customerVisibilityApi.ts` | `/api/customer-visibility/*` | Yes | `customer_portal:*` | Tenant-scoped / customer scoped | No | No change needed | Working |
| `platformApi` | platform auth / config | `frontend/src/services/platformApi.ts` | `/api/platform/*` | Yes | platform admin auth | Platform-scoped | No | No change needed | Working |
| `aiApi` | insights / ask | `frontend/src/services/aiApi.ts` | `/api/ai/*` | Yes | AI-related RBAC | Tenant-scoped | No | Recommendation-only | Working Foundation |

