# G2A partial GPS measurement truth — 2026-09-02

Owning gate: G2A / #115. Branch: `wave2/samsara-partial-gps`.
Parent: local `a0c5531a89b87f64c44b5bc502d1cfda5174a6f6`, retaining the
response bounds, engine-evidence and canonical feed-array corrections.
This is a local, default-disabled implementation candidate, not a deployment,
provider certification or gate closure. Samsara remains PILOT / HOLD.

## Observe → evidence → root cause

The [official Samsara schema](https://developers.samsara.com/openapi/samsara-api.json)
requires GPS latitude, longitude and time, but speed and heading are optional.
The prior local candidate preserved cursor safety by pausing on these legitimate
partial measurements because history/latest/live stores required nonnull values.

The focused reader trace found that changing the parser and columns alone would
still fabricate data: the shared live projection and Fleet/TMS conversion used
zero fallbacks, map markers substituted north for missing bearing, and roster or
replay views treated missing speed as zero. The idling detector counted unknown
samples while its maximum-speed aggregation ignored NULL, and it accepted missing
engine evidence while claiming the engine was on.

Reproduction against unchanged parent application source:

- Parser suite: four missing/explicit-null cases failed; seventeen controls passed.
- PostgreSQL behavior: ten failures (four partial-GPS sequences and six false-idling
  conditions), three controls passed, zero skipped. Stage 98 was applied only to
  the disposable test database to admit NULL fixtures. Initial test-harness compile
  and numeric-parameter mistakes were corrected before this reproduction count.
- Frontend production-function/selected-JSX tests: seven failures, two controls
  passed. These are executable unit/SSR tests, not a mounted browser observation.

No customer loss, real-provider behavior or physical measurement is inferred from
these synthetic cases.

## Focused implementation

1. Parse absent or explicit-null speed/heading independently as nullable values.
   Preserve real zero and measured north; retain range validation, reject malformed
   supplied types before writes, and never carry forward a prior measurement.
2. Stage 98 drops NOT NULL on six columns across `location_events`,
   `latest_vehicle_positions` and `telemetry_live_asset_states`. Legacy zero
   defaults and historical rows remain unchanged. Samsara explicitly binds NULL.
   Runtime create-table declarations match, but the enrolled owner migration is
   authoritative; no runtime ALTER or broad data repair is introduced.
3. Keep the exact query/cursor profile, lease/generation fence, page transaction,
   idempotency, effective installation and monotonic latest projection. A mixed
   known/partial page can retain every valid event once the deployment opts in.
4. Preserve NULL through the shared live projection, entity JSON and Fleet/TMS
   speed-unit conversion. Summary text identifies unavailable measurements.
   Speeding requires an observed excessive speed; missing heading does not suppress
   an otherwise supported speed alert, and unknown updates do not resolve alerts.
5. Require known low speed and affirmative `On`/`Idle` engine evidence at **every
   sample** admitted to an idling candidate window. Do not filter unknown samples
   away or infer engine-on from a legacy `Running` default. Alert text reports
   possible idling samples, explicitly not continuous idling duration. Other vendor
   engine synonyms require their own evidence before addition; this is conservative
   alert eligibility, not a certified idling algorithm or retrospective repair.
6. Use shared nullable frontend helpers. Missing speed cannot enter Moving/Idle
   buckets from business status alone. Missing bearing produces no directional
   arrow; genuine north still does. Roster, drawer, tracking cards and replay show
   unavailable values and gaps, with reported-peak coverage rather than invented
   zero/one-mph peaks. Existing low-speed `Idle` UI buckets are movement buckets,
   not proof of engine-on or regulated duty status.

## Rollout and recovery boundary

`Samsara:AllowPartialGpsMeasurements` is application configuration and defaults
**false**. It is not a customer/tenant bypass. The connector parses the full page,
then refuses any partial page before opening a database scope while this setting
is disabled. With explicit opt-in, a six-column nullability preflight runs after
the lease assertion inside the fenced page transaction. Missing schema fails
without consuming that page. The latter negative path is source-reviewed, not
yet executed against an old protected schema.

No deployment setting was changed by this work. Controlled activation requires:

1. A separately authorized isolated Wave 2 environment, never frozen G1A or the
   production baseline. Pin frontend/API to the accepted exact candidate.
2. Verify all active readers and browser clients are NULL-compatible; prevent old
   clients/readers from consuming new NULL rows during transition.
3. Apply the enrolled owner migration with protected-role/rollback evidence.
   Keep `Telemetry:Simulator:Enabled=false`; simulated writes cannot support
   provider acceptance and must not overwrite real measurements.
4. Only then enable the writer setting and retest the same real-provider journey.
5. If rollback is needed, disable the writer and retain the nullable schema and
   NULL-compatible readers. Never replace unknowns with zero or reapply NOT NULL.

Retained defaults mean other producers that omit fields can still receive legacy
zeros; this correction does not claim universal producer omission correctness.
The migration does not identify or rewrite old synthetic/default-derived data.
The existing query still does not request GPS decorations, so engine/odometer
acquisition and cursor-profile transition remain separate explicit dependencies.

## Tests and independent assurance

- Root final focused Samsara suite: **92/92 passed**, 27 seconds, zero skipped.
  Includes 56 non-database and 36 PostgreSQL cases; counts overlap other suites.
- Four mapped partial-GPS sequences verify initial unknown/zero, explicit zero,
  known nonzero, newer unknown clearing, older history-only, replay no-op, known
  speeding without heading, and preservation of existing open alerts.
- A mixed known → newer partial → older known page retains three distinct history
  events, advances latest twice, preserves newest NULLs and provider-event freshness,
  and returns its completed-page cursor. Counts remain distinct per page, not per run.
- Real position/breadcrumb handler methods query the disposable database and their
  result JSON preserves null versus zero. This bypasses HTTP/auth middleware;
  it is not live HTTP/SSE transport or production RLS evidence.
- Ten idling cases include mixed/all-null speed, missing/blank/unknown/default/off
  engine evidence, affirmative On/Idle controls, dedupe and open-alert preservation.
  The positive samples are sparse; the message deliberately disclaims continuity.
- The actual Stage 98 script runs twice against unique test-owned old-shape tables.
  All six columns become nullable, legacy defaults and existing zeros survive,
  explicit NULL persists, and the migration ledger has one entry. The temporary
  schema is removed after the case; shared/public tables are never dropped.
- Frontend implementer and independent SDET: **20/20 focused tests passed**, full
  frontend contracts passed, TypeScript/Vite build and bundle budget passed.
  Production functions and selected JSX are exercised; no provider/API calls or
  mounted browser are simulated as evidence. Reported largest bundle is
  314.86 KiB raw / 95.23 KiB gzip across 210 chunks.
- Final broader non-PostgreSQL/non-integration regression: **2,192/2,192 passed**,
  39 seconds, zero skipped. Counts overlap the focused suite; do not sum them.
- Final Release: zero errors, **485 distinct warnings / unchanged 487 ceiling**,
  15.15 seconds. No dependency, target-framework or baseline changes.
- Independent SDET reran the final backend suite: **92/92 passed**, 27 seconds,
  zero skipped. Its prior unchanged frontend reruns/build remain applicable.
  It issued LIMITED GO only for local code readiness, finding no bounded blocker.
- Independent security/data-integrity review found no bounded blocker in the final
  source, including the default-off opt-in guard, and issued the same scoped local
  opinion after review. It did not claim its own test execution or field evidence.
- `git diff --check` passed before commit.

Implementation roles: root owns backend/schema/tests; the provider/data agent owns
the frontend correction and is **not** an independent approver for this candidate.
Separate SDET and security/data-integrity agents review acceptance. Their AI verdicts
are local code-readiness opinions, never qualified-human Appendix B signatures.
ASP.NET Core guidance shaped layered persistence tests; React guidance kept nullable
display state derived without new effects or dependencies.

## Browser and closure limits

Chrome skill/runtime is available and its existing connection can list tabs.
The one bounded rendered read this heartbeat returned `Debugger unattached` on the
production settings tab. No alternate browser, credential extraction or hidden
browser mechanism was used. The Chrome/frontend-testing skills therefore prevent
claiming rendered acceptance: page identity, mounted content, overlays, console,
screenshots and user interaction are **not verified** for this local candidate.
Intended visual retest: live map / roster → known, unknown and explicit-zero GPS
updates → nondirectional unknown-bearing marker and truthful instruments/replay.

Production auto-deploy safeguard has not been freshly reverified; PR #119 is not
merged by this work. No Samsara publication, hosted CI, deployment, live-provider
account journey, HTTP/SSE evidence, protected-role/RLS migration, durable
`config_json.syncCursor` finalization or qualified-human approval is claimed.
No Capability Truth Matrix or master-plan status is promoted, and Wave 3 remains
locked behind its actual prerequisites.
