# Customer Portal / Client Visibility

## Current State
- `CustomerVisibilityPage.tsx` is already a live, customer-safe surface for shipment tracking, ETA risk, proof visibility, and token management.
- The page exports the live query rows rather than seed fixtures.

## Productization Notes
- Keep customer-facing fields narrow and tenant-safe.
- Keep the tabbed workflow readable for external users.
- Avoid exposing internal safety or dispatch details that do not belong in the portal.

## Remaining Gaps
- Broader customer self-service workflows still live in adjacent modules and are not yet unified here.
- Future customer API contracts should stay role-scoped and minimal.

## Verdict
- This module is productized enough for Stage 15A-2, with low residual risk.
