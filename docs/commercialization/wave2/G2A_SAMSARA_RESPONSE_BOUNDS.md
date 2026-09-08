# G2A Samsara response-bound hardening — 2026-09-02

Owning gate: G2A / #115. Local follow-up on `wave2/samsara-response-bounds`,
based on merged main `865e990d847bc4bf7b84d147d0020af8ebd45f9e`.
This is code-readiness work, not a deployment or gate closure. Samsara remains
PILOT / HOLD for real-account and exact-SHA customer-journey evidence.

## Observation → root cause

While closing the analogous Motive response-bound finding, the two Samsara
handshake requests and the statistics sync request were observed using default
`HttpClient.GetAsync` completion followed by `ReadAsStringAsync`. This permits
response buffering without an application byte cap. Buffering also reads error
bodies before the existing status-based retry logic can inspect their headers.

Twelve new synthetic regression cases failed against the unchanged base before
the fix. Their content fixtures deliberately throw if HTTP tries to prebuffer;
this reproduces the unsafe completion mode without allocating an enormous body
or sending traffic to any provider. Independent AppSec classified this as bounded
P2 availability hardening, not evidence of an observed provider exploit.

## Focused fix

- Headers-only completion for both handshake requests and every sync attempt.
- A 4 MiB actual-byte limit before parsing JSON. Declared lengths are an early
  rejection hint; missing, dishonest and endless bodies stop at limit plus one.
- Twenty-second per-request deadlines cover headers and body reads. One shared
  25-second handshake deadline bounds the two sequential scope checks.
- Existing overall sync budgets, five-attempt status-only retry limit and
  Retry-After/backoff behavior are retained. Retry error responses are disposed
  without body reads, before waiting; oversize and body timeout do not add retries.
- Caller cancellation propagates during sync; overall-run and request expiry
  retain distinct failure messages and only previous complete-page progress.
- The current page must be fully read, bounded, parsed and validated before any
  database scope or page transaction. Oversized pages are not truncated, skipped,
  written or used to advance the returned cursor.
- Streams, responses and request deadlines remain alive for the complete read
  and are disposed on every result path. New oversized-response failures are
  fixed text. Existing unrelated generic exception reporting is not certified by
  this narrow change.

The 4 MiB limit is an OpsTrax protective policy, not a verified Samsara page-size
guarantee. A legitimate larger page will fail resumably and require a reviewed
adjustment supported by provider evidence; it must not be silently discarded.

## Local test and independent review evidence

- Before fix: 12/12 targeted buffering regressions failed, zero skips.
- After fix: 34/34 focused Samsara cases passed in 26 seconds, zero skips.
  Concurrent real-timer cases cover autonomous per-attempt body expiry and the
  shared handshake budget, including a first request consuming 12 seconds.
- Targeted PostgreSQL case: 1/1 passed. One unique synthetic tenant receives a
  valid, nonempty unmatched-device history page followed by an oversized page.
  The first history/device record and provider-event timestamp survive, no latest
  vehicle state or alert is fabricated, and only the completed-page cursor is
  returned. The test cleans up its own tenant and records.
- Broader non-database regressions: 2,170/2,170 passed, zero skips. The 34 focused
  cases are included in this total; counts must not be added together.
- Completed Release rebuild: zero errors and 485 distinct warnings against the
  existing 487 ceiling. The local SDK is 10.0.300 targeting .NET 8; the baseline
  was not changed to disguise a toolchain-dependent warning difference.
- Independent AI SDET separately reproduced 34/34 focused and 1/1 PostgreSQL
  cases. Independent AI AppSec reviewed the production and test delta and found
  no blocker within this bounded defect. Neither reviewer implemented the fix.

The database case proves committed unmatched history and the returned resume
cursor. It does not prove durable `config_json.syncCursor` persistence, mapped
live-vehicle projection, a valid telemetry prefix inside an oversized second body,
production-role RLS or live-provider behavior. Unchanged transaction/finalization
paths remain supporting source evidence. AI review is not qualified-human
Appendix B acceptance.

## Remaining release boundary

This follow-up is local only until its own exact head is published and passes
hosted CI. Main baseline `865e990...` passed all eleven controls in run
[33664632681](https://github.com/kodekinetics79/opstrax-enterprise-build/actions/runs/33664632681),
but those results do not cover this new code. The separate Motive PR #119 passed
all eleven controls on its own `df2f1df...` candidate in run
[33666751740](https://github.com/kodekinetics79/opstrax-enterprise-build/actions/runs/33666751740);
that is a different candidate and is not Samsara evidence.

No production or frozen G1A deployment is performed by this work. An isolated
Wave 2 exact-SHA deployment, visible same-journey retest, authorized Samsara
account/provider responses and required independent acceptance remain necessary.
No hardware, regulatory, live-provider or production-certification claim changes.
