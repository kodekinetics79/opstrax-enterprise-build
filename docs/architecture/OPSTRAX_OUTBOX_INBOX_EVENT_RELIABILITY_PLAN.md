# OpsTrax Outbox / Inbox Event Reliability Plan

## Required Pieces
- Domain event outbox
- Integration inbox
- Retry policy
- Dead-letter handling
- Event processing logs
- Idempotent consumer semantics

## Reliability Goal
Exactly-once business effect should be achieved through idempotency and durable processing state, not by pretending the transport itself is exactly-once.

