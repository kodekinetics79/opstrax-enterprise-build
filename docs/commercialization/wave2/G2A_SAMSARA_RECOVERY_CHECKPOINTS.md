# G2A Samsara projection and checkpoint recovery — 2026-09-02

Owning gate: #115. Branch: `wave2/samsara-partial-gps`.
Parent: `1c809faf192057d24e94461e7bfd0a06a0b96096`.
This continues the response-bound, engine-evidence, canonical-array and optional-GPS
fixes; it does not replace their rollout constraints. Samsara remains PILOT / HOLD.
No master-plan gate, Capability Truth Matrix status or qualified-human approval
is changed by this supporting local evidence.

## Observed defect and reproduction

Independent security/data review found that the page transaction committed before
the derived live-state refresh. Refresh exceptions were suppressed, and a replay
skipped the vehicle before adding it to the refresh set. Thus saved history/latest
could disagree with the operator-facing live state while a cursor advanced.

Two executable PostgreSQL cases reproduced the defect on unchanged parent
application source: a test-owned trigger rejected the live projection, but neither
run propagated an error. Both cases failed the required rollback assertion.
These are synthetic local faults, not observed customer/provider incidents.

## Fix and recovery boundary

- Refresh mapped live state inside the existing leased page transaction. Resolve
  the real projection service as required, in the same database scope. Do not open
  a nested transaction, perform provider I/O in the transaction, or suppress failure.
- Refresh in sorted vehicle order. History, latest state, alerts, provider freshness
  and the derived live row roll back together if this page's projection fails.
- Duplicate history never increments the latest event count or recreates alerts.
  A currently valid mapping may repair a stranded live row using canonical latest
  state, not the replayed measurement. Historical/ambiguous mappings are excluded.
- Independent review identified a race in that repair path: replay does not acquire
  the latest-row UPSERT lock. The correction locks the tenant/vehicle latest row
  `FOR UPDATE` before the canonical read, preventing a concurrent writer from being
  overwritten with stale display values. No canonical row means no repair.
- This is a database projection update, not proof of an SSE delivery or browser
  render. Transactions hold locks longer; representative scale/soak remains required.

The durable `config_json.syncCursor` is finalized **after** completed page
transactions, using the generation/token/expiry fence. It is not an atomic
page-plus-cursor checkpoint. An interruption before finalization leaves the prior
cursor and requires at-least-once replay after lease expiry. The tests exercise
that boundary rather than claiming exactly-once transport or actual process-kill
certification.

## Executable evidence

Four new projection/recovery test cases (the first scenario has two inputs)
supplement the prior partial-GPS suite:

1. Missing speed/heading and known excessive speed each encounter a real projection
   write failure. Prior committed values survive; retry retains truthful NULLs and
   one history/event-count/alert effect. Deleting only the owned fixture's derived
   row demonstrates replay repair, including older replay after a newer position.
   Missing canonical latest state is not reconstructed from old history on replay.
2. A second vehicle's failing refresh observes the first vehicle's live row inside
   the transaction. The whole page then rolls back both vehicles, alerts and
   freshness. Retrying the identical page succeeds without duplicate effects.
3. A competing connection holds a newer canonical row while replay reaches an
   observable database lock barrier. After commit, live state reflects the newer
   22 mph / 180-degree measurement, not the replay's older 40 mph / 90 degrees.
   The competing writer is synthetic SQL, not a certified second-provider ingest.

The new durable-cursor suite has nine cases through the actual private manual
endpoint, registry, connector, page transactions and finalizer. It covers terminal
and bounded success, empty-page checkpoint behavior, a partial page followed by a
malformed page, default-off refusal, cursor-cycle recovery, disconnect racing with
completion, and interrupted finalization followed by lease-expiry replay.

The recovered connection is retested through the real handshake endpoint against
synthetic HTTP responses. A handshake must preserve sync state; it is not itself a
sync checkpoint. Fresh connector/database scopes read the saved cursor and preserve
the encrypted synthetic token plus unrelated region/profile metadata. The partial
page's NULL measurements, coordinates, timestamps and provider freshness are checked
in all applicable stores. Fixtures supply a real test-owned operator and entitlement.

Initial harness errors (missing synthetic operator context and a test URL helper
assuming every request had `types`) were corrected; they are not product defects.
Provider responses are in-process fixtures, and endpoint methods are invoked by
reflection. No HTTP middleware, protected-role/RLS, global worker schedule, provider
account, browser or regulatory acceptance is implied. No unrelated tenant is synced.
Only owned local rows and uniquely named fault triggers/functions are cleaned up.

- Root final focused Samsara suite: **105/105 passed**, 44 seconds, zero skips,
  including the final post-commit count-only debug log and corrected comment.
- Independent SDET rerun: **105/105 passed**, 30 seconds, zero skips, before that
  logging/comment delta. SDET and AppSec separately reviewed the final two-line
  delta and retained their scoped opinions; no redundant independent rerun claimed.
- Broader non-PostgreSQL/non-integration regression: **2,192/2,192 passed**,
  54 seconds, zero skips, before the logging/comment delta. Counts overlap the
  focused suite and must not be summed as unique cases.
- Final Release build: zero errors; **485 distinct warnings / unchanged 487
  ceiling**, 37.14 seconds. The first Release check found a new unused-logger
  warning after the catch removal; the count-only debug log corrected it without
  removing constructor compatibility or suppressing/expanding the warning baseline.
- `git diff --check` passed. No framework, dependency or warning-baseline changes.

## Independent review and release boundary

Root implements the projection change and failure tests. The provider/data agent
implements the durable-cursor tests and is not their independent approver.
Separate SDET and security agents review the final candidate. The final security
source review found both P2 defects addressed and gave LIMITED GO for **local code
readiness only**, not a release gate or qualified-human Appendix B signature.
ASP.NET Core guidance shaped layered transaction/endpoint tests and shared DI scope.

`Samsara:AllowPartialGpsMeasurements` stays default false. Owner migration, compatible
readers, isolated exact-SHA deployment, real-provider journeys, protected-role
migration/old-schema refusal, representative scale and visible Chrome evidence
remain outstanding. Keep the simulator disabled. No deployment settings changed.

Separately, the approved Motive PR #119 was merged into `main` as
`a859dffd47400774f9992eb7e361517222a6d1ea` at 20:48:23 UTC after fresh Render CLI
verification that production auto-deploy and previews were off. The approval
system first blocked the merge; the user then specifically approved PR #119 into
main. No workaround was used. Post-merge workflow `33681467249` passed all eleven
mandatory jobs on exact merge SHA `a859dff`, including its release-evidence ledger.

Render's last live production deployment still reports controlled baseline
`155b54a3451c2a4618b4fc6a87fd59f0e68f425d`. Its newer failed `865e990` deployment
was explicitly marked `manual` and predates this merge; it is not a successful
deployment or a trigger from this action. No service settings were edited; the
temporary local CLI workspace selection was restored. Render skill guidance
enabled a read-only deployment safeguard check despite Chrome's disconnected
debugger. Chrome customer-journey acceptance remains unverified.

Motive/main CI does not cover the separate local Samsara changes. No Samsara push,
PR, hosted CI, merge, deployment or certification is asserted in this record.
Wave 3 remains locked behind its actual prerequisites.
