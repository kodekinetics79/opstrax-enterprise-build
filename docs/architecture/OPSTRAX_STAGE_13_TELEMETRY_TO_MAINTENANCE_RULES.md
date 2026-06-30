# Opstrax Stage 13 Telemetry to Maintenance Rules

## Live Bridge Rules

| Telemetry / Vehicle Signal | Maintenance Outcome | Evidence | Notes |
| --- | --- | --- | --- |
| Fault-code ingestion | Create or update fault-code records and maintenance attention | `backend-dotnet/Services/MaintenanceSchemaService.cs`, maintenance handlers | Still live-only |
| DVIR defect detection | Surface maintenance defects and work-order candidates | `dvir_defects`, `work_orders` | No silent auto-resolution |
| Out-of-service vehicle | Reduce readiness and block dispatch readiness | `FleetHealthSummary` SQL | Safety remains authoritative |
| Overdue PM threshold | Surface overdue maintenance in dashboard and fleet-health score | `maintenance_items` and summary queries | No fake repair completion |

## Maintenance Guardrails

- Maintenance data stays tenant-scoped.
- The bridge can recommend work but cannot complete it automatically.
- No external repair-system call is introduced.
- Dashboard metrics must match persisted rows, not seeded mocks.

