# Opstrax Stage 13 Fleet Health Scoring

## Score Model

- Fleet health is already computed on the backend from vehicle readiness, safety exposure, DVIR defects, PM status, work orders, and dispatch blockers.
- The current summary logic combines readiness and safety into a single operational story for the dashboard.

| Component | Source | Meaning |
| --- | --- | --- |
| Vehicle readiness | `vehicles`, `dvir_defects`, `work_orders`, `maintenance_items` | How ready the fleet is for dispatch |
| Safety posture | `drivers`, `safety_events`, `coaching_tasks` | How risky the operator population is |
| Blockers | Critical defects, out-of-service state, overdue PM | Whether the fleet can execute safely |

## Stage 13 Use

- The new dashboard bridge simply surfaces the real score and its inputs.
- The score remains server-owned and tenant-scoped.
- No client-side recomputation was introduced.

