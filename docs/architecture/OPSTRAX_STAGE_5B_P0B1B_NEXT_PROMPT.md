# Stage 5B P0-B1B Next Prompt

Build one controlled workflow slice on top of the hardened foundation:

- use the PostgreSQL-backed approval workflow from one real tenant action
- publish a durable domain event and outbox message from one business mutation
- add one inbox consumer path for a single external callback
- add one idempotent command handler that reads and writes the idempotency table
- have one AI recommendation produce an approval request instead of executing directly
- propagate correlation ids through that workflow into authorization, approval, event, and audit records

Keep it local-only, additive, and backward compatible with the current demo surface.
Do not expand Customer, Contract, Job, Trip, Revenue, full CRM, full AI automation, or full IoT ingestion yet.

