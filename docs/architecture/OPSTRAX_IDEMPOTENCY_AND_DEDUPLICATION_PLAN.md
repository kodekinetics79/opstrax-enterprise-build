# OpsTrax Idempotency and Deduplication Plan

## Must Be Idempotent
- IoT events
- Webhooks
- Payment callbacks
- Invoice creation
- AI action execution
- Work order creation
- POD upload
- Domain event processing

## Foundation Rules
- Use request or event identifiers where available.
- Store dedupe keys per tenant and per source.
- Keep business writes behind idempotent service methods.
- Use immutable processing logs where retries are expected.

