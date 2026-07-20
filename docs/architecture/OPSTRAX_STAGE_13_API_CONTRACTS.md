# Opstrax Stage 13 API Contracts

## Contract Summary

Stage 13 consumes existing live API contracts and does not introduce a new business API family.

| Endpoint | Purpose | Permission / Scope | Notes |
| --- | --- | --- | --- |
| `GET /api/command-center/summary` | Main dashboard summary | Tenant-scoped app session | Primary Dashboard shell feed |
| `GET /api/safety/dashboard` | Safety KPI and trend summary | `safety:view` | Real safety rows only |
| `GET /api/maintenance/dashboard` | Maintenance KPI and exception summary | `maintenance:view` | Real DVIR / work order rows only |
| `GET /api/fleet-health/summary` | Fleet health aggregation | `dashboard:view` | Weighted readiness + safety composite |
| `GET /api/fleet-health/risks` | Risk-ranked fleet items | `dashboard:view` | Action queue for vehicles and drivers |
| `GET /api/safety/events` | Safety event list | `safety:view` | Used by safety workbench pages |
| `GET /api/dashcam/events` | Dashcam event list | `dashcam.view` | Still a live operational surface |
| `GET /api/maintenance/work-orders` | Work-order list | `maintenance:view` | Used by maintenance workflows |

## Contract Rules

- All data is tenant-scoped.
- The frontend must not mask backend failures with seed data on Stage 13 surfaces.
- Dashboard naming stays visible to users.
- Cockpit remains an internal concept only if it appears in comments or code internals.
- No AI or external system is allowed to directly mutate these records.
