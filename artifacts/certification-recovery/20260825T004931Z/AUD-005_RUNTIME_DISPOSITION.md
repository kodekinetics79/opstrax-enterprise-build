# AUD-005 Runtime Disposition

Defect disposition: **CONFIRMED** on production deployment
`b982ef8b7020b490cdf7968364f6c15421fcf83f`. Candidate hardening is locally
verified; dispatcher environment enablement is **BLOCKED** pending the aggregate
backlog/type query below.

This disposition is based on protected runtime evidence, not source defaults alone.
The live Render deploy was `dep-da638lbl550s7389kil0` for service
`srv-d93dha0k1i2s73dm6ub0`, manually deployed at `2026-08-24T12:08:53Z` and
live at `2026-08-24T12:09:25Z`. The complete retained startup window contains
Information-level starts for other named workers but no `Outbox dispatcher
started` event. A second targeted log query from 2026-08-23 onward also returned
zero dispatcher-start records. Because production registration requires both
`Enabled` and `AllowProduction`, and the worker logs before entering its loop,
the dispatcher was not running in that exact process.

Source review also established:

- the worker was absent from critical-worker readiness and heartbeat tracking;
- expired `processing` claims were not eligible for reclaim;
- reliability counts omitted `retry_pending`, `processing`, and `dead_letter`;
- no operator replay/requeue path or dead-letter alert UI was present;
- inbox failures wrote an exponential `next_attempt_at` but the claim query and
  protected owner schema did not honor/materialize it;
- active handlers include invoice, GL, settlement, detention, notification, and
  job-delivered billing handoffs.

Backlog magnitude remains blocked on a sanitized aggregate-only database query:

```sql
SELECT status, count(*) AS messages,
       min(created_at) AS oldest_created_at,
       max(retry_count) AS max_retry_count,
       count(*) FILTER
         (WHERE status='processing' AND locked_until < now()) AS stranded_processing,
       count(*) FILTER
         (WHERE status='retry_pending' AND next_attempt_at <= now()) AS retry_due
FROM outbox_messages
GROUP BY status
ORDER BY status;

SELECT processor,status,count(*) AS events,max(processed_at) AS latest
FROM event_processing_logs
WHERE processor='foundation-dispatcher'
GROUP BY processor,status
ORDER BY status;

SELECT event_type,status,count(*) AS messages,
       min(created_at) AS oldest_created_at,
       max(retry_count) AS max_retry_count
FROM outbox_messages
GROUP BY event_type,status
ORDER BY event_type,status;
```

Only aggregate results are needed; no payload, error text, or credential should be
returned.

Safety decision after independent adversarial review: do not enable the worker in
the production manifest until the aggregate status/type distribution is compared to
the registered handler inventory. The current dispatcher treats a claimed unhandled
type as a failure and eventually dead-letters it; source inspection found durable
event types beyond the registered handler set. Code hardening and readiness
participation are retained, but environment enablement remains BLOCKED.

The candidate additionally enrolls Stage89 in the protected migration runner and
proves that a future-scheduled inbox retry is not claimed early. Local compose and
the isolated production-shaped rehearsal enable the worker explicitly; the
production Render manifest intentionally does not.
