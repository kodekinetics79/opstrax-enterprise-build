# G2A canonical Samsara GPS-feed arrays — 2026-09-02

Owning gate: G2A / #115. Branch: `wave2/samsara-feed-arrays`.
Parent: local `5abc722a2e7854e08a997e8bf6f543f57e05abf0`, which includes the
response-bound and engine-evidence corrections. This candidate is local only;
no publication, hosted CI, merge, deployment, provider journey or gate closure
is performed by this work. Capability remains PILOT / HOLD.

## Observe → evidence → root cause

Independent AI provider review classified a P1 contract mismatch: the parser
expected one `gps` object per vehicle. The official statistics feed instead
returns `data[].gps[]`, including its initial last-known response. The old parser
rejected those arrays, returned zero readings and allowed the connector to
advance the page cursor. Valid GPS updates could therefore be skipped.

This is a code/contract finding with synthetic reproduction, not an assertion
that any particular customer or live provider account lost data. Thirteen new
tests failed against the unchanged parent behavior; an unchanged-query/cursor
control passed. All fourteen passed after the parser correction.

Primary contract evidence checked on 2026-09-02:

- [Samsara feed guide](https://developers.samsara.com/docs/vehicle-stats-feed):
  timestamped event arrays, initial last-known values and incremental cursors.
- [Samsara telematics guide](https://developers.samsara.com/docs/telematics):
  separate time series and event-associated decorations.
- [Feed reference](https://developers.samsara.com/reference/getvehiclestatsfeed)
  and [official OpenAPI](https://developers.samsara.com/openapi/samsara-api.json):
  `VehicleStatsListResponse_data.gps` is an array; `VehicleStatsListGps` requires
  latitude, longitude and time, but speed and heading are optional. GPS-event
  decorations may include engine-state and odometer values.

## Focused fix and deliberate boundaries

1. Iterate every GPS event in the canonical array. Empty arrays and legitimate
   engine/odometer-only updates do not fabricate or reject a GPS fix.
2. Wrong GPS container/event shapes fail the entire page before any database
   scope or transaction, preserving prior completed-page progress. Do not accept
   the old object-shaped test fixtures as if they were real feed evidence.
3. Preserve existing invalid-coordinate/time/speed protections and count invalid
   fixes individually without discarding their valid neighbours.
4. Never zip sibling engine/odometer arrays, choose their last element or infer
   contemporaneous values. Only a GPS event's own decorations can supply those
   fields; otherwise they remain NULL. Missing/blank engine evidence stays NULL.
5. Preserve the exact request profile
   `types=gps,engineStates,obdOdometerMeters` and encoded `after` cursor. No query
   switch, cursor reset or request for decorations is hidden in this correction.
6. Count accepted provider vehicle IDs once per page rather than once per GPS
   event. The outer run still sums page counts; it is not a distinct fleet-wide
   vehicle count. Unmatched-result wording now correctly refers to GPS fixes.
7. Normalize numeric headings to existing whole-degree storage by flooring,
   with 360 degrees mapped to zero. Fractional bearing precision is not retained.
8. Retain the existing lease/generation fence, effective installation resolution,
   page transaction, idempotency, monotonic latest projection, freshness and alert
   rules. Response-size, request/run deadlines and retry limits remain unchanged.

### Partial GPS is a disclosed readiness dependency

The provider permits GPS records without speed or heading, but existing OpsTrax
history/latest/live schemas require nonnull speed; latest/live also require
nonnull heading. The new parser does **not** fabricate zero, falsely call the
valid provider location invalid, or consume its cursor. It pauses the whole page
resumably with an explicit OpsTrax-storage limitation before any writes.

This may block an entire page behind one partial GPS record. It is not full
Samsara compatibility or a production-ready recovery policy. A focused nullable
telemetry/quarantine design, corresponding migration and downstream tests remain
necessary before that limitation can be removed. No schema migration is bundled.

### Engine/odometer and cursor-profile boundary

The current request does not ask for decorations, so this correction does not
establish engine/odometer acquisition or independent engine-state history.
GPS-event decorations are supported defensively when present, not promised.
Explicit nonblank engine strings are retained as evidence, not certified against
every provider enum; the `Unknown` regression value is compatibility coverage.

A GPS-led decorated query is officially supported, but cursor portability across
changed query profiles was not established. A future profile transition must be
versioned, retain the old cursor, and require reconciliation/backfill evidence.
A request without `after` returns last-known values, not complete history. This
fix neither repairs previously skipped records nor claims historical completeness.

## Test and independent-assurance evidence

- Pre-fix: 13 failed / 1 passed / zero skipped in the new contract suite.
- Post-fix focused suite: 69/69 Samsara tests passed in 27 seconds, zero skipped,
  including the new contract cases and disposable PostgreSQL cases.
- Existing GPS fixtures were changed from incorrect objects to arrays without
  weakening their replay, disconnect, backfill or transaction assertions. The
  oversized-page case gained an explicit heading measurement.
- Ten mapped-engine database cases now use event-bound decorations and also
  deliver a three-event batch containing newest, older and exact-replay fixes.
  Both unique history events survive; latest advances once; one provider vehicle
  is counted; history/latest/live projection retain the newest engine evidence.
- Two new PostgreSQL cases commit one valid page, then receive a later page with
  a valid prefix followed by a wrong container or missing heading. No part of
  the later page is written or advances provider-event freshness. Only the first
  history/device record and completed-page resume cursor survive.

- Broader non-PostgreSQL/non-integration regression: 2,184/2,184 passed in
  40 seconds, zero skipped. These counts overlap the focused suite and must not
  be added together as unique coverage.
- Release warning verification: zero errors, 485 distinct warnings against the
  unchanged 487-warning ceiling in 15.98 seconds. No warning-baseline ratchet,
  dependency change or platform upgrade was made.
- Independent AI provider assurance reviewed the source and reran the fourteen
  parser/contract tests: 14/14 passed, zero skipped. Reviewed production diff
  SHA-256: `c3d9f84b776bb6509daaef08db2f71d056013fe81cb3c58291009a9f8cce20fb`.
- Independent AI SDET assurance reran the full focused suite: 69/69 passed in
  28 seconds, zero skipped, comprising 48 non-database and 21 PostgreSQL cases.
  It independently reviewed fixture corrections and page-atomicity assertions.
- Independent AI security/data-integrity assurance reviewed the source, fence,
  transaction boundaries and tests. It found no new bounded blocker; it did not
  claim an independent test execution or a production RLS assessment.
- All three reviewers gave LIMITED GO for this narrow local code correction,
  not a G2A gate decision, live-provider readiness or qualified-human approval.
  None implemented this correction. `git diff --check` passed.

The ASP.NET Core skill guided layered parser and transactional persistence tests
without changing the .NET 8 platform.

## Closure boundary

The database cases use a disposable synthetic tenant/test role. They verify
persisted history/projections and the returned resume cursor, not a new durable
`config_json.syncCursor` finalization proof, production RLS, scale, live-provider
capture, browser rendering or qualified-human Appendix B acceptance. Independent
AI review cannot supply the missing field or human evidence. Exact-head hosted
CI, isolated Wave 2 deployment, same-journey retest and genuine provider/operator
acceptance remain required; production and frozen G1A are unchanged.
