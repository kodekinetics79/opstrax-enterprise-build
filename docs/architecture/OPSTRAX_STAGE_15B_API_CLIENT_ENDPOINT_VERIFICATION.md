# Stage 15B API Client / Backend Endpoint Verification

| Frontend Client | Method | Frontend Path | Backend Endpoint | Exists? | Auth/RBAC | Tenant Scope | Fake Fallback? | Risk | Final Status |
|---|---|---|---|---|---|---|---|---|---|
| `adminApi` | `permissions()` | `frontend/src/services/adminApi.ts` | `GET /api/admin/permissions` | Yes | Backend-controlled | Tenant-aware | No | Low | Working |
| `incidentsApi` | list/detail/actions | `frontend/src/services/incidentsApi.ts` | incident endpoints in `EndpointMappings.cs` | Yes | Backend-controlled | Tenant-aware | No | Medium | Working |
| `fuelApi` | summary/list/create | `frontend/src/services/fuelApi.ts` | fuel endpoints | Yes | Backend-controlled | Tenant-aware | No | Low | Working |
| `safetyApi` | dashboard/summary | `frontend/src/services/safetyApi.ts` | `/api/safety/*` | Yes | Backend-controlled | Tenant-aware | No | Low | Working |
| `fleetDomainApi` | dashboard summary bridge | `frontend/src/services/fleetDomainApi.ts` | live summary bridge endpoints | Yes | Backend-controlled | Tenant-aware | No silent fallback | Low | Working |
| `tripApi` | trip list/actions | `frontend/src/services/tripApi.ts` | `/api/trips/*` | Yes | Backend-controlled | Tenant-aware | No | Low | Working |
| finance analytics | invoices/payments/profitability | `frontend/src/pages/FinancialAnalyticsPage.tsx` | finance endpoints | Yes | Backend-controlled | Tenant-aware | No | Low | Working |
| `feature-flags` | list/update/rollback | `frontend/src/pages/FeatureFlagsPage.tsx` | feature flag endpoints | Yes | Backend-controlled | Tenant-aware | No | Low | Working |
| Dashboard / command center | summary | `frontend/src/services/commandCenterApi.ts` | `/api/command-center/summary` | Yes | Backend-controlled | Tenant-aware | No | Low | Working |
| telemetry / live map | live state | `frontend/src/services/telemetryApi.ts`, `LiveMapPage.tsx` | telemetry endpoints | Yes | Backend-controlled | Tenant-aware | No | Medium | Working |
| customer / CRM / compliance / reports | read and action clients | relevant page/service files | corresponding backend endpoints | Yes | Backend-controlled | Tenant-aware | Generally no | Low | Working |

