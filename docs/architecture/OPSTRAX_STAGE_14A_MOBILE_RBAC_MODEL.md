# Opstrax Stage 14A Mobile RBAC Model

| Role | Home Screen | Allowed Route Families | Required Permissions | Hidden Data | Backend Enforcement |
|---|---|---|---|---|---|
| Driver / Operator | Dashboard + Workflows | Dashboard, Workflows, Proof, Telemetry | `operations.proof.read`, `operations.proof.submit`, `operations.execution_summary.read` | Internal cost, admin controls, cross-tenant data | Backend session permissions only. |
| Field Worker | Dashboard + Workflows | Dashboard, Workflows, Proof, Settings | `operations.site_access.read`, `operations.proof.create`, `operations.proof_artifact.create` | Finance actions, platform tools | Backend session permissions only. |
| Dispatcher / Supervisor | Dashboard + Workflows | Dashboard, Workflows, Telemetry, Settings | `dispatch.smart_assign.read`, `dispatch.smart_assign.accept`, `operations.execution_summary.read` | Customer-only views, platform admin actions | Backend session permissions only. |
| Warehouse / Pickup | Dashboard + Workflows | Dashboard, Workflows, Proof, Settings | `operations.pickup_authorization.read`, `operations.warehouse_handover.read` | Driver telematics, pricing, platform controls | Backend session permissions only. |
| Customer / Client | Dashboard + Proof | Dashboard, Proof, Settings | `operations.proof.read`, `customer_portal:view` | Driver private fields, ops internal notes | Backend session permissions only. |
| Safety / Maintenance | Dashboard + Telemetry | Dashboard, Telemetry, Workflows, Settings | `safety:view`, `maintenance:view`, `telemetry.live_state.read` | Customer portal data, finance admin actions | Backend session permissions only. |
| Tenant Admin | Dashboard + Settings | Dashboard, Workflows, Telemetry, Settings | `users:view`, `roles:update`, `settings:update` | Platform-wide tenant controls | Backend session permissions only. |
| Platform Admin | Dashboard + Settings | Dashboard, Telemetry, Settings | `ops:view`, `platform:manage` | Tenant private operational details unless explicitly allowed | Backend session permissions only. |

