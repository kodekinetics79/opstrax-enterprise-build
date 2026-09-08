# OpsTrax Mobile — Known Blockers

This file intentionally lists only blockers that materially prevent internal or public mobile release.

## Current blockers

1. Full mobile typecheck/lint/build evidence is still required for this branch.
2. Remaining Driver trip/POD/compliance screens need the shared premium design and device-level verification.
3. Fleet telemetry and workflow screens need the shared premium design and operational action review.
4. Customer billing/POD/document surfaces need the shared premium design and account-scope verification.
5. Offline queue behavior must be proven on a real device with connection loss/recovery.
6. Cross-tenant, cross-customer, and cross-driver authorization regression must pass against the final mobile API surface.
7. Store privacy, permission, reviewer-account, and account-deletion material must match final production behavior.
8. iOS TestFlight/internal and Android internal/closed-test installation evidence is required.

## Not blockers for visual work

- Public ELD/HOS certification claims are not required for the first non-ELD mobile release, provided uncertified functionality is not marketed or represented as certified.
- Separate customer/driver binaries are not required before shared architecture and role journeys are stable; packaging can follow the product-family architecture without duplicating business logic.
