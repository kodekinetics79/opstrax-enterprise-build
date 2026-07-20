# Stage 9 Mobile Readiness Notes

The mobile brief does not require a separate mobile app implementation yet. It requires the backend and Stage 9 slice to be designed as if mobile is a first-class client.

The current backend already has several mobile-relevant operational endpoints, but the product still reads like a desktop-admin system in some flows. Stage 9 must keep every operational workflow mobile-safe, role-aware, tenant-scoped, idempotent, and auditable.

| Mobile Role | Required Flow | Backend APIs Needed | Permissions | Offline/Idempotency Need | Evidence/Location Need | Stage 9 Decision | Future Phase |
|---|---|---|---|---|---|---|---|
| Driver / Operator | View assigned trips/jobs, accept/reject, capture pickup/delivery proof, report exceptions, geo check-in/out | Existing dispatch assignment, proof, telemetry, and exception endpoints; future mobile-safe assignment status endpoints | `dispatch:manage`, `dispatch:view`, `trip.read`, `trip.update`, `driver.proof.submit`, `driver.exception.submit` | High: weak networks and repeated submits must use idempotency keys and client-generated IDs | High: proof photos, signatures, timestamps, geo location, device metadata | Required for Stage 9 design | Mobile app phase |
| Cleaner / Technician / Field Worker / Guard | View service job, check in/out, checklist completion, evidence upload, issue reporting | Job/task assignment endpoints, checklist/evidence endpoints, future service-job mobile endpoints | Role-scoped operational permissions per tenant | High: offline capture and retry-safe submit | High: before/after photos, signoff, timestamp, geo, metadata | Required for Stage 9 design | Mobile app phase |
| Dispatcher / Operations Coordinator | Review exceptions, reassign, approve high-risk smart assignment, monitor POD gaps | Dispatch assignment, reassignment, exception, approval, and proof review APIs | `dispatch:manage`, `dispatch:view`, approval permissions for high-risk actions | Medium: action replay should be guarded with idempotency | Moderate: audit trail, reason capture, correlation | Required for Stage 9 design | Mobile app phase |
| Warehouse User | View handovers, verify pickup authorization, record handover status, attach receipt | Handover and warehouse receipt endpoints to be formalized in Stage 9 | Warehouse-scoped operational permissions | High: handover retries must not duplicate effect | High: receipt photo, scan value, timestamp, user/device metadata | Required for Stage 9 design | Mobile app phase |
| Third-Party Pickup / Handover User | Confirm pickup, attach handover proof, close receipt trail | External handover confirmation endpoints with strict token scope | Narrow external actor permission set | High: external retries and weak connectivity must be duplicate-safe | High: signed receipt, QR/barcode, geo, timestamp | Required for Stage 9 design | Mobile app phase |
| Customer / Client Portal User | View own jobs/orders, view proof, approve/reject proof where allowed, submit feedback | Customer-visible tracking/proof endpoints and approval endpoints with tenant and ownership checks | `customer_portal:view`, `customer.account.summary.read`, proof approval permissions | Medium: submit actions should be replay-safe | Moderate: proof visibility only, never admin-only fields | Required for Stage 9 design | Mobile app phase |
| Supervisor / Manager | Review exceptions, approve waivers, reassign where permitted, monitor risk | Approval, dispatch, and proof-review endpoints | `dispatch:manage`, finance approval permissions, tenant-scoped manager permissions | Medium: sensitive actions must be idempotent | Moderate: audit/correlation, optional geo for mobile approval actions | Required for Stage 9 design | Mobile app phase |
| Tenant Admin | Manage users/roles, profile, workflows/modules | Tenant admin APIs only, no platform-wide controls | Tenant admin RBAC with strict tenant boundary | Low to medium: standard idempotency for configuration writes | Low: audit/correlation required | Required for Stage 9 design | Admin mobile companion only |
| Platform Admin | Manage tenants, subscriptions, feature flags, limits | Platform admin APIs only, separated from tenant ops | Platform admin permission set; must not bypass tenant audit rules | Low to medium: config writes should still be replay-safe | Low: audit/correlation mandatory | Required for Stage 9 design | Platform admin app surface |

## Backend design rules for mobile

- Mobile must use the same centralized authorization engine as web.
- Every mobile API must resolve tenant/company context on the backend and fail closed if missing.
- Mobile APIs must not leak internal margin, cost, or admin-only fields to customer or operator users.
- Sensitive mobile actions must write audit and correlation records.
- Evidence and proof endpoints should accept metadata needed for offline retry, even before full offline sync exists.
- Idempotency keys should be accepted for submit actions that can be retried on weak networks.
- Domain events and outbox messages should already include event types that future mobile notifications can subscribe to.

## Current state

- The repo already contains mobile-relevant operational behavior in dispatch, proof, telemetry, and customer visibility.
- The backend is not yet uniformly mobile-shaped across every operational workflow.
- Stage 9 must keep the mobile surface as a first-class SaaS client concern, not a later UI polish task.
