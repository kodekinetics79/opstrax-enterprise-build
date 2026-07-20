# Opstrax Stage 11 Module Completion Matrix

This matrix tracks the remaining enterprise module families that were considered for Stage 11.

| Module Family | Current Status | Evidence | What Is Missing | Stage 11 Treatment |
|---|---|---|---|---|
| Platform commercial control plane | Partial but strong | `backend-dotnet/Controllers/PlatformEndpoints.cs`, `backend-dotnet/Services/PlatformSchemaService.cs`, `frontend/src/pages/platform/*` | One unified operator-grade cockpit and summary workflow | Complete the cockpit as the Stage 11 slice. |
| Tenant governance / RBAC admin | Built but fragmented | `frontend/src/pages/AdminPage.tsx`, `backend-dotnet/Controllers/EndpointMappings.cs`, `backend-dotnet/Services/SecuritySchemaService.cs` | A single high-level governance story that feels cohesive for SaaS operators | Keep as a follow-on enhancement, not the main slice. |
| CRM / sales | Partially built | `frontend/src/pages/LeadsPage.tsx`, `frontend/src/pages/OpportunitiesPage.tsx`, `frontend/src/pages/CustomersPage.tsx`, `backend-dotnet/Services/RevenueReadinessService.cs` | Canonical sales spine and cleaner backend-backed workflows | Defer to a later CRM completion stage. |
| Telemetry / IoT | Partially built | `backend-dotnet/Services/TelemetrySchemaService.cs`, `frontend/src/pages/LiveMapPage.tsx`, `frontend/src/pages/IotDevicesPage.tsx` | Unified telemetry operations cockpit and tighter command safety | Defer; too broad for the chosen Stage 11 slice. |
| Customer portal / visibility | Partial | `frontend/src/pages/CustomerVisibilityPage.tsx`, `backend-dotnet/Services/CustomerVisibilitySchemaService.cs` | Broader customer-facing workflow completeness | Defer. |
| Mobile shell | Not started | No mobile app root or Expo/React Native shell was present in the repo | App shell, auth, routes, offline contract | Not selected for this Stage 11 build. |

## Priority Decision

The clearest completion win is the platform commercial control plane:

- It is already close enough to be finished in one bounded pass.
- It has immediate SaaS value.
- It can be made materially better without starting a new program.

