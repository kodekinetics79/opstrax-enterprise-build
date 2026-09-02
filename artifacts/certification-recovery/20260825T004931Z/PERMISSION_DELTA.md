# AUD-003 Permission Delta

Enforcement now uses one directed graph in `PermissionPolicy`. Dot/colon spelling
variants remain equivalent, but a narrow action never implies a sibling mutation or
its manage umbrella. Backend middleware and the foundation authorization engine both
delegate to that policy; the frontend mirrors the same held-grant-to-required edge
direction.

## Direct fallback-role changes from b982ef8

| Role | Added direct grants | Removed direct grants |
|---|---|---|
| Super Admin | none | none |
| Tenant Admin | fleet, dispatch, safety, maintenance, alerts, reports, users, roles and settings `:manage` umbrellas | none |
| Fleet Owner | dispatch, alerts, compliance, reports and settings `:manage` umbrellas | none |
| Fleet Manager | dispatch, alerts, compliance and reports `:manage` umbrellas | none |
| Dispatcher | explicit shipment/dispatch action grants mirrored into seed data | legacy `jobs:view`, `jobs:manage`, `fleet:view`, and `dispatch:manage`; no broad or sibling escalation remains |
| Driver | none | none |
| Safety Manager | `safety:manage`, `compliance:manage` | none |
| Maintenance Manager | `maintenance:manage` | none |
| Customer | none | `shipments:view`, `alerts:view` |
| Customer Portal User | none | `shipments:view` (fallback; protected seed was already portal-only) |
| Customer Viewer | none | `shipments:view`, `fleet.pod.view`, `fleet.tracking.view`, `fleet.shipments.view` |
| All other shipped fallback roles | none | none |

The added umbrellas are explicit declarations for roles that already held the
corresponding action set; they preserve intended manager access after removing
symmetric sibling closure. Customer personas are deliberately portal-only and use
customer-scoped portal APIs rather than internal shipment/alert routes.

Representative negative witnesses now denied: create→delete, update→close,
acknowledge→close, export→manage, invoice issue→approve, settlement create→pay,
tax update→publish, and read→verify/submit/validate. Approved manage→action edges
remain green. Backend role defaults, seed SQL, Stage77, reconciler, frontend role
defaults, route guards and navigation metadata were reconciled together.

The independent review also found Stage9 direct gates that still accepted
`dispatch:assign`/`dispatch:manage` for smart-assignment mutations while the frontend
correctly denied them. Those legacy aliases were removed; Stage9 reads now use the
canonical directed policy so action→read works without opening sibling writes.
