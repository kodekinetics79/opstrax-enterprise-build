# Stage 5D Dispatcher Operations Notes

## Enable / Disable

- Default: disabled.
- Enable locally or in dev with `OutboxDispatcher__Enabled=true`.
- Keep production disabled unless `OutboxDispatcher__AllowProduction=true` is set on purpose.

## Suggested Local Settings

- `OutboxDispatcher__Enabled=false`
- `OutboxDispatcher__BatchSize=10`
- `OutboxDispatcher__PollingIntervalSeconds=5`
- `OutboxDispatcher__MaxRetryCount=3`
- `OutboxDispatcher__RetryBackoffSeconds=15`
- `OutboxDispatcher__ProcessingTimeoutSeconds=30`

## Behavior

- Claims use tenant-scoped `SKIP LOCKED` selection to avoid duplicate processing.
- Success marks the outbox row processed and writes an event-processing log row.
- Failure increments retry count, sets `next_attempt_at`, and records the error.
- Terminal failure moves the row to `dead_letter`.
- Inbox duplicate detection marks duplicates as ignored without business side effects.

## Monitoring Gaps

- There is no alerting UI for dead-letter queues yet.
- There is no operator dashboard for worker lag or retry depth yet.
- There is no dedicated replay console yet.

## Known Limits

- This is a foundation dispatcher only.
- It does not process customer, contract, job, trip, revenue, CRM, AI automation, or IoT business modules.
- It should stay off in production until rollout approval and operational monitoring are in place.

