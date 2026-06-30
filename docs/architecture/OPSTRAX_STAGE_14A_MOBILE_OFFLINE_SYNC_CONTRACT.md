# Opstrax Stage 14A Mobile Offline Sync Contract

## Status

Offline sync is not built yet. Stage 14A only establishes the contract so future work can be implemented safely.

## Contract Expectations

| Area | Contract | Current State | Risk | Next Step |
|---|---|---|---|---|
| Local queue | Replay-safe mutation queue per tenant/session | Not built | Medium | Add a dedicated offline queue if the product needs field-first capture. |
| Idempotency | Client-generated keys for retryable writes | Backend supports the contract in several flows; mobile shell does not generate queue items yet | Medium | Add deterministic client keys for proof and evidence writes in the next slice. |
| Conflict handling | Last-writer or server-authoritative resolution | Not built | Medium | Define conflict strategy before offline edits are allowed. |
| Evidence capture | Offline metadata capture without fake upload success | Only contract-previewed | High | Build the capture queue before enabling true offline mode. |
| Sync status | Pending, synced, failed, needs-review | Not built | Medium | Surface sync state once the queue exists. |

## Safety Rules

- Never pretend a write succeeded while offline.
- Never bypass RBAC to “make sync work.”
- Never silently drop evidence or proof metadata.

