# Fleet / Assets / Vehicles

## Current State
- `VehiclesModulePage.tsx` and adjacent fleet modules provide live roster, planning, health, and records views.
- `FleetAssignmentsPage.tsx` ties fleet objects back to dispatch behavior.

## Productization Notes
- Fleet surfaces should continue to favor operational clarity over oversized tables.
- Keep the linked entity jump points intact so the operator can follow the workflow.

## Remaining Gaps
- Some legacy fleet pages are broader than the main operational views and may still need small cleanup passes.

## Verdict
- Fleet is productized enough for Stage 15A-2, with low-to-medium polish risk.
