# Opstrax Stage 14A Mobile Notification Contract

## Status

Push delivery is not built. Stage 14A only maps the backend events that a future push layer may subscribe to.

## Event Mapping

| Event | Possible Mobile Audience | Intent |
|---|---|---|
| `smart_assignment.recommended` | Dispatcher / supervisor | Notify that a new recommendation is waiting. |
| `site_access.required` | Field worker / dispatcher | Surface access blockers before completion. |
| `pickup_authorization.verified` | Warehouse / pickup | Confirm third-party handoff approval state. |
| `warehouse_handover.completed` | Warehouse / dispatcher | Confirm handover closure. |
| `proof_package.submitted` | Driver / operator, dispatcher | Notify that proof has been submitted. |
| `proof_package.validated` | Customer / dispatcher | Notify that proof has been validated. |
| `billing_confidence.updated` | Finance / operations | Show that the trust signal changed. |

## Rules

- Notification payloads must stay tenant-scoped.
- The mobile app must not depend on push delivery for correctness.
- The app should still function if push is absent.

