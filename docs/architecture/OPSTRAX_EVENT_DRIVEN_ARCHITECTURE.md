# OpsTrax Event-Driven Architecture

## Principles
- Emit domain events from business services.
- Persist events with an outbox pattern when moving beyond local bootstrap.
- Use inbox handling for idempotent consumers.
- Preserve retries, dead-letter tracking, and audit history.

## Example Event Families
- Customer and contract events
- Job, trip, and POD events
- Telematics and safety events
- Maintenance and fleet-health events
- Billing, invoice, payment, and renewal events
- AI observation, recommendation, approval, and execution events

