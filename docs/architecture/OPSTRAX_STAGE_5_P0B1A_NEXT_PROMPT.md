# Stage 5 P0-B1B Next Prompt

Implement the next controlled slice on top of the Stage 5 foundation:

- connect the authorization engine to a real policy source instead of in-memory passthrough state
- persist approval requests and decisions through the new schema from one representative workflow, including the high-risk actions `finance.invoice.issue`, `finance.credit_note.approve`, `customer.contract.rate_change`, `dispatch.trip.reassign_high_value`, `iot.vehicle.immobilize`, `ai.action.execute_external`, `platform.tenant.suspend`, and `safety.evidence_pack.share_external`
- publish a domain event and outbox message from one production-like state change
- add an inbox processor path for one external integration callback
- add one idempotent command handler that reuses the new idempotency table
- extend AI action handling so recommendations can raise approval requests without executing directly
- add durable audit-log writing and correlation propagation for one real workflow so the in-memory audit shim can be retired
- add a paired rollback artifact for the foundation migration before any broader rollout

Keep it local-only, additive, and backward compatible with the current demo surface.
