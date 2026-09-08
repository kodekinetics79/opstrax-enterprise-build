# G2A Samsara missing-engine evidence correction — 2026-09-02

Owning gate: G2A / #115. Branch: `wave2/samsara-engine-state-truth`.
Parent candidate: local response-bound hardening
`2c70406a27b200bd2fdcb34565c6b7e034acb3df`, itself based on main
`865e990d847bc4bf7b84d147d0020af8ebd45f9e`.

This is a separate, local-only data-truth correction. Neither this candidate nor
its parent has been published, hosted-CI verified, merged or deployed. Samsara
remains PILOT / HOLD; no master-plan or capability promotion occurs.

## Observe → evidence → root cause

In the parser-accepted GPS path, `SamsaraSync` wrote absent engine status as NULL
in `location_events` but substituted `Running` for the same missing value in
`latest_vehicle_positions`. `TelemetryLiveStateService` then copied the invented
value into `telemetry_live_asset_states`. Blank strings were also preserved as
if they were meaningful engine observations.

A new PostgreSQL theory reproduced 10/10 failures against the unchanged parent:
expected NULL versus actual `Running`, empty string or whitespace. These are
local, synthetic persisted-data reproductions, not evidence of a production or
real-provider incident. A valid current installation was created for each unique
synthetic tenant, so the test exercised the actual governed mapped-write path.

Independent AI provider review confirmed the defect and the narrow correction.
Samsara documents engine values `On`, `Off`, `Idle` and different update cadences
for GPS and engine data; absence cannot establish an engine-running observation.
[Official Samsara telematics documentation](https://developers.samsara.com/docs/telematics).

## Fix

- Normalize absent/null/empty/whitespace engine evidence to NULL once after
  extraction; preserve every existing explicit nonblank string unchanged.
- Bind the nullable value identically in history and latest position. Never infer
  engine state from a GPS fix or speed, and never invent `Running`.
- A newer GPS-only event clears the coupled engine value instead of silently
  retaining an older observation without a separately tracked engine timestamp.
- No schema, tenant scope, installation, lease, transaction, alert, timestamp,
  response-bound, retry, cursor or deployment behavior is changed.

## Local verification

The same new PostgreSQL theory passed 10/10 after correction, zero skips. It
uses only a controlled HTTP handler and the disposable local test database,
cleans up only its own synthetic rows, and verifies all three persisted stores:

1. Initial absent, explicit null, missing object value, null object value, empty
   object value and whitespace string become NULL.
2. Explicit `On`, `Off`, `Idle`, and the existing string compatibility value
   `Unknown` are preserved verbatim. The latter is compatibility coverage, not
   a new documented provider enum claim.
3. A newer explicit `Off` is asserted in all three stores before a GPS-only
   event; the newest engine value then becomes NULL in history, latest position
   and the live-asset projection.
4. An older novel `On` remains historical; an exact replay is a no-op. Latest
   event count remains three, history contains four unique events, and no alerts
   are created by these stationary synthetic fixes.

The changed production candidate also passed 2,170/2,170 non-database regressions
in 49 seconds, zero skips. After the independent SDET suggested explicitly
checking the intermediate `Off` update, that test-only assertion was added and
the 10 PostgreSQL cases passed again in three seconds.

Release rebuild completed with zero errors and 485 distinct warnings against
the unchanged 487 ceiling in 21.06 seconds. An initial warning check correctly
failed on cached `NU1900` vulnerability-metadata download failure from the first
restricted restore. A successful fresh restore removed the network warning;
no audit suppression, dependency change or baseline relaxation was used. The
SDK remains 10.0.300 targeting .NET 8, so the toolchain-dependent lower warning
count was not used to ratchet the existing baseline.

Independent AI AppSec reviewed the production/test delta and found no blocker:
tenant predicates, effective installation resolution, lease/generation fencing,
page transaction, idempotency, monotonic latest writes and alert gating are
unchanged. Independent AI SDET reproduced the final strengthened suite at 10/10
passed in two seconds, zero skips, and found no remaining code-readiness blocker.
The provider, SDET and AppSec reviewers did not implement the correction.

The ASP.NET Core skill guided the existing .NET 8 service/transaction pattern
and layered tests; no framework migration was introduced.

## Limits and remaining closure

- The fixtures deliberately target the existing parser-accepted object/string
  shape. Canonical statistics-feed arrays, cross-timeseries association,
  malformed non-string fields, missing speed/heading and full provider contract
  coverage are not certified by this fix.
- A single synthetic tenant and the existing test database role do not provide
  new tenant/RLS, scale, authorized-provider or browser acceptance evidence.
- Persisted projection rows are verified; visible map rendering, SSE delivery
  and customer journeys have not been retested on a deployed candidate.
- No existing deployed records were inspected or repaired. No production or
  frozen G1A environment was changed.
- Independent AI reviews are supporting assurance, not qualified-human
  Appendix B approval. Exact-head hosted CI, isolated Wave 2 deployment,
  same-journey retest, authorized provider data and required acceptance remain
  necessary before field or gate closure.
