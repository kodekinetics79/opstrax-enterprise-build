# G2A Samsara customer certification runbook

**Owning gate:** G2A / GitHub Issue #115  
**Capability truth:** **PILOT** until the gate formally closes  
**Purpose:** execute and retain the real-account evidence required by Master Action Plan v1.2 without exposing a provider token or overwriting the frozen G1A controlled-pilot deployment

This runbook is an execution and evidence-capture instrument. Completing the document, running local tests or passing CI does not certify the Samsara connector. Only an authorized account, authentic provider responses, an isolated exact-SHA deployment and the required independent acceptance can close G2A.

## 1. Roles and separation of duties

Record a named person for each role before testing. One person may perform more than one operational role, but the implementer may not supply the required independent acceptance.

| Role | Minimum responsibility | Named person / organization |
|---|---|---|
| Customer/Samsara administrator | Authorize the org, region, token/app and least-privilege scopes | |
| OpsTrax operator | Execute Connect → Discover → Map → Validate → Sync → Monitor → Disconnect/Reconnect | |
| Evidence custodian | Capture timestamps, redacted screenshots, response metadata and artifact hashes | |
| Provider Integration SME | Confirm endpoint, scope, pagination and provider-contract behavior | |
| Security reviewer | Independently accept secret lifecycle, tenant isolation, revocation and audit evidence | |
| Principal SDET | Independently accept the exact-SHA functional, failure and recovery evidence | |
| Fleet Product reviewer | Accept that a customer can execute the workflow and understand its limitations | |
| SRE reviewer | Accept health, stale/error behavior, bounded execution and recovery evidence | |
| CTO/program owner | Issue GO / LIMITED GO / NO-GO after all mandatory evidence is available | |

AI-assisted review may support these roles but is not the qualified-human Appendix B approval.

For every P0 claim, record two independent qualified-human perspectives as follows. They must be different people, neither may be the implementer for the evidence under review, and one person's multiple job titles do not count twice.

| P0 claim domain | Independent perspective 1 | Independent perspective 2 | Named reviewers / decision / date |
|---|---|---|---|
| Provider identity, scope and authentic response truth | Provider Integration SME | Principal SDET or second independent provider/telematics SME | |
| Credential security, tenant/branch isolation and revocation | Security reviewer | Second independent Security/Privacy assurance reviewer | |
| Reconciliation, failure recovery and performance boundary | SRE reviewer | Principal SDET with performance/resilience responsibility | |
| Customer workflow and commercial limitations | Fleet Product reviewer | Principal SDET or Customer Success reviewer independent of implementation | |

## 2. Environment and candidate controls

Before entering a provider credential, record:

| Control | Required value | Observed value |
|---|---|---|
| PR | `#118` or its formally approved successor | |
| Git commit | Full 40-character candidate SHA | |
| Frontend deployment identity | Exact SHA/build identifier and URL | |
| API deployment identity | Exact SHA/image digest and URL | |
| Database migration state | Stage 94, Stage 95 and Stage 96 applied; migration evidence retained | |
| Tenant | Dedicated Wave 2 test tenant, not the frozen G1A tenant | |
| Samsara region | **US API cloud only** for this candidate: `https://api.samsara.com`. A Canadian organization is eligible only when its own dashboard/account is documented by the customer administrator as using the US API cloud | |
| Evidence directory | Access-controlled location; no provider secret | |

Stop if the frontend, API and evidence manifest do not identify the same candidate, or if the only available deployment is the frozen G1A candidate `e2230425a8e14249d2c0f477a7ec7b713a6ab27e`.

This exact candidate hardcodes `https://api.samsara.com` and uses bounded polling; it does not implement a configurable EU/UK or Canadian regional base URL and does not claim webhook delivery. An EU/UK- or Canada-cloud account requires a separately reviewed code change, new exact SHA and complete retest. Do not redirect traffic or relabel a regional account to make it fit this candidate.

## 3. Account and secret preflight

The authorized administrator must confirm all of the following:

- The test is permitted for the selected Samsara organization and tenant.
- The administrator has verified that the organization uses the US API cloud (`api.samsara.com`). EU/UK and Canadian regional API clouds are unsupported by this exact candidate.
- The credential is dedicated to this integration and limited to the minimum organization tags and read scopes required by the endpoints under test.
- The current implementation requires read access to vehicles and vehicle statistics for its handshake and GPS/engine-state/odometer path.
- The token will be entered only into the OpsTrax server-backed integration form. It must not be pasted into chat, tickets, screenshots, shell history, source code, browser developer tools or the evidence manifest.
- A provider-side revocation/rotation owner and rollback window are agreed before the first connection.

Official provider references (links and content version reverified by the evidence custodian on the execution date; last documentation review for this runbook: **2026-09-02**):

- [Samsara API authentication](https://developers.samsara.com/docs/authentication)
- [Samsara sandbox access](https://developers.samsara.com/docs/sandboxes)
- [Samsara regional API base URLs](https://developers.samsara.com/docs/base-url)
- [Samsara app lifecycle and minimum scopes](https://developers.samsara.com/docs/marketplace-apps)

## 4. Evidence manifest

Create one row per action or observation. Redact secrets and personal data before storage; never alter the underlying timestamps, counts, status codes or candidate identity.

| Evidence ID | Run/case ID | UTC time | Candidate SHA | Workload profile | Tenant + redacted provider org | Actor/role | Journey step | Page/attempt | Expected/actual rows | Expected/observed result | Provider metadata | Resource + status/response metrics | Artifact link + SHA-256 | Classification | Independent reviewer |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| G2A-001 | | | | | | | | | | | | | | | |

For provider responses retain, where available: endpoint name, region, HTTP status, request/correlation identifier, page cursor state, rate-limit headers, provider event time, OpsTrax ingest time and redacted response shape. Do not retain the bearer token or raw secrets.

## 5. Evidence lanes and customer journey

Keep two evidence lanes separate:

- **Customer UI lane:** tenant administrator/operator browser evidence from `/integrations`, `/iot-devices` and `/map-view`. It may show only that customer's tenant-scoped data.
- **Staff-assisted assurance lane:** Principal SDET/SRE/Security read-only queries, tenant-scoped logs, audit records, provider response metadata and aggregate/synthetic multi-tenant metrics. Never expose another tenant's identifiers or records in a customer screenshot, export or handoff.

Before the UI journey, prove the operator can reach every required route with the canonical direct permissions below. An inherited alias must be resolved to the effective grant and recorded; absence of any required grant is a stopped test, not an authorization workaround.

| Route/action | Required OpsTrax permission |
|---|---|
| Configure, handshake, discover, validate, sync, disconnect | `telematics:providers:manage` |
| View provider devices and installation evidence | `telemetry.devices.read` |
| Create/transfer effective-dated installations | `telemetry.devices.manage` |
| View the live projection | `telemetry.live_state.read` |

### A. Connect and authenticate

1. Sign in as an authorized tenant operator with `telematics:providers:manage`.
2. Open `/integrations`, select Samsara and open Configure.
3. Enter the dedicated token and save it.
4. Run the real provider handshake.
5. Record the result, provider status, last-test timestamp and redacted provider metadata.

Pass only if the real request succeeds and OpsTrax reports Connected. A catalog entry, saved token, seeded status or mocked response is not a pass.

### B. Discover

1. Select **Run device discovery**.
2. Record vehicles seen, unmatched and rejected counts.
3. Confirm discovered provider identities appear without an inferred OpsTrax asset assignment.
4. Retain one real unmatched example and one provider identity/provenance example, redacted as needed.

Pass only with authentic provider data and no silent mapping by vehicle name.

### C. Map

1. Open `/iot-devices` from the integration journey.
2. Select a discovered provider device.
3. Create an effective-dated installation against the intended OpsTrax vehicle and governed branch context.
4. Have the operator independently confirm the provider identity, vehicle and effective time in the customer UI. Because this candidate does not expose branch ownership as a separate visible field in this installation flow, a staff reviewer must confirm branch lineage using a tenant-scoped read-only API/database artifact. Record this as staff-assisted evidence; do not represent it as customer-visible proof.
5. Include an ambiguous/unmatched case and prove it remains unresolved until an operator decides it.

Pass only with zero silent ambiguous mappings and correct tenant/branch ownership. The branch-visibility gap remains an explicit product limitation until a later candidate exposes that ownership to the authorized customer; it cannot be waived by a screenshot of another field.

### D. Validate and synchronize

1. Return to `/integrations` and select **Run validation sync**.
2. Record positions written, unmatched, historical-only and rejected counts.
3. Execute another bounded sync and confirm provider-event replay does not duplicate canonical history or regress the live projection.
4. Validate GPS, engine state and odometer provenance, provider event time, ingest time, freshness and mapped lineage.
5. Open `/map-view` and confirm only the currently effective installation advances live state.

Pass only if history, latest state and counts reconcile and rejected data is explicit.

### E. Monitor and pre-approved acceptance matrix

Observe at least one normal worker interval. In the customer UI record the separate **Last successful sync**, polling-attempt health and authentic provider-event freshness signals, Connected/Error/Disconnected handshake state, stale warning and actionable result. In the staff-assisted lane reconcile those signals to the tenant-scoped sync-attempt/start, terminal-result, success and newest-provider-event clocks, audit/log result and persisted counts. A provider handshake is not a data-sync attempt, must not advance those clocks, and the Connected badge proves only the most recent credential handshake—not a fresh data feed.

Record:

- last-success and last-attempt timestamps;
- Connected/Error/Disconnected status changes;
- stale-feed and operator-visible error behavior;
- absence of secret material in UI, logs and audit output;
- bounded worker behavior and progress for other tenant integrations using only synthetic or aggregate staff evidence; no other-tenant record is customer-visible.

The Principal SDET and SRE must approve the intended profile and the matrix below before the first measurement. These are certification floors, not a customer SLA or a general production commitment. A stricter customer/provider contract overrides them; a reviewer may not relax them after seeing results.

| Metric | Minimum passing boundary | Required evidence |
|---|---|---|
| Provider-event freshness | During steady state, p95 provider-event-to-normalized latency ≤ 10 minutes and p99 ≤ 15 minutes. The Integration card's newest-event sentinel must be non-green when the newest event exceeds 15 minutes, but that sentinel is neither a substitute for nor proof of the p95/p99 distribution. The separate Connected badge remains handshake truth only. | Every provider-event and normalized timestamp retained as the percentile population; five-minute summaries are supplementary; newest-event UI timestamp captured separately |
| Worker sync duration | Provider work receives a 60-second cancellation budget; terminal cleanup/result must complete within 5 additional seconds and always before the 90-second lease ceiling | Monotonic provider-call start, cancellation signal, terminal commit/release and lease-expiry timestamps |
| Manual sync duration | Provider work receives a 75-second cancellation budget; terminal cleanup/result must complete within 5 additional seconds and always before the 90-second lease ceiling | Monotonic API/provider start, cancellation signal, terminal commit/release and UI-result timestamps |
| Retry behavior | Returned 429/5xx responses permit at most 5 total attempts per provider request; each wait ≤ 10 seconds; `Retry-After` followed within ±1 second when it is ≤ 10 seconds. A transport exception/timeout is one bounded terminal failure in this candidate and is retried only by a later scheduled/manual run. | Attempt number, response/exception class, status, header and monotonic timing |
| Unexpected provider/API error rate | ≤ 1% of requests during steady state, excluding scheduled fault phases; every error explicit and attributable | Request/status count and error ledger |
| Canonical reconciliation | Exactly 0 missing and 0 duplicate canonical events for the known provider event-ID set | Provider input IDs versus persisted idempotency keys/rows |
| Invalid data | 100% of intentionally invalid/future/impossible fixes rejected; 0 fabricated replacement telemetry | Rejected count and row-level sample |
| Mapping/lineage | 0 silent ambiguous mappings, 0 cross-tenant/branch rows and 0 historical event advancing the wrong live projection | Mapping queue plus lineage queries/screens |
| Backlog age/drain | Backlog drains within `ceil(pages / 5) × 5 minutes + 10 minutes` for worker-only recovery, or within the pre-approved manual-run plan; cursor must advance monotonically | Page/cursor timeline and terminal empty-backlog proof |
| Tenant fairness | For a test set of ≤ 8 eligible tenants, every tenant receives an attempt in the same scheduler run; no failing tenant prevents another tenant from progressing or causes two missed 5-minute intervals | Per-tenant attempt clock and result timeline |
| Resource headroom | No sustained 5-minute CPU, memory, DB connection-pool or storage-I/O utilization ≥ 80%; no exhaustion, deadlock or queue growth after the drain phase | One-minute host/database metrics with timestamps |
| Status visibility | Error/stale condition becomes visible on the Integration card no later than one 5-minute worker interval plus the UI's 60-second refresh cadence | UI status timestamp, sync-specific clocks and originating failure |
| Operator response evidence | Test operator records acknowledgment in the external certification incident/evidence register within 15 minutes | External incident/evidence ID, owner and acknowledgment timestamp; this is not claimed as an OpsTrax connector-alert feature |
| Status recovery | After the next successful sync, the customer-visible card recovers within the 60-second refresh cadence while the external incident/evidence record retains the prior failure | Recovery sync clocks, UI status timestamp and external incident-history reference |

Record p50, p95, p99 and maximum for freshness and sync duration even when the pass/fail boundary uses p95/p99. Use the nearest-rank estimator (`rank = ceil(p × N)`) over every provider event for freshness and every terminal run—including success, failure and timeout—for duration. Before execution, the Principal SDET and SRE must approve the population target; the default floor is 100 events and 30 terminal runs for a bounded pilot, and 1,000 events and 60 terminal runs for a large-fleet profile. Record the population count and excluded scheduled-fault phases and retain the raw event/run-level values. The once-per-minute operational sample is supplementary and is not the percentile population. Missing telemetry, a population below the pre-approved floor or an unapproved threshold is a failed case, not “not applicable.”

### F. Failure and recovery

Execute and retain evidence for every applicable case:

| Case | Executable parameters | Passing reconciliation |
|---|---|---|
| Invalid/revoked token | Revoke/rotate at the recorded provider time, then attempt handshake and sync | Authentication fails closed; connector is not Connected; zero telemetry writes |
| Provider 5xx outage response | Use a provider-supported fault or agreed test route; record up to 5 allowed attempts and ≤10-second waits | Provider work stops within its 60/75-second budget and terminal cleanup finishes within the additional 5-second tolerance; Error is explicit; no partial page transaction is committed; any prior complete page transactions and their terminal cursor progress are retained |
| Provider transport timeout/exception | Use a provider-supported fault or agreed test route; record the single bounded provider attempt | The attempt terminates as Error without in-run exception retry; no partial page transaction is committed; any prior complete page transactions and their terminal cursor progress are retained; a later scheduled/manual run retries safely |
| Pagination | Use a known provider event-ID set spanning at least 6 pages for worker proof and 21 pages for manual-boundary proof | In-memory cursor advances per processed page; telemetry commits per page; durable cursor advances once after a successful bounded run to its last committed page. Repeated cursor fails closed; 0 missing/duplicate IDs; next run resumes the durable run cursor |
| Rate limit | Produce an authorized 429 with known `Retry-After`; record monotonic attempt/wait timing | ≤5 attempts, each wait ≤10 seconds, header followed within ±1 second when applicable, explicit terminal result |
| Historical/backfill | Include known current, delayed/pre-transfer and invalid/future event IDs | Valid history retained; invalid count exact; 0 ended-installation event updates current live state |
| Crash before terminal cursor commit | Kill the process after one or more page telemetry transactions commit but before the bounded action persists its terminal cursor; retain lease token/expiry and crash/restart/startup times | Before lease expiry, attempted reacquisition skips/fails without overlap and the lease is not manually cleared. The first post-expiry attempt acquires a new token, resumes from the prior durable run cursor, replays already committed pages idempotently and produces exactly one canonical row per provider event ID. Drain timing starts at post-expiry reacquisition. |
| Restart after bounded-run cursor commit | Stop after the successful bounded action persists its terminal cursor with a remaining known backlog | Resume starts at that durable run cursor, drains by the formula above and produces 0 missing/duplicate IDs |
| Failing-prefix fairness | Run one continuously failing tenant before at least 7 healthy eligible tenants | All healthy tenants are attempted in the same scheduler run; no tenant misses two intervals |
| Disconnect during work | Disconnect while provider response/page processing is in flight; capture generation/lease and row counts | Stale completion cannot restore state or write device/history/latest/alert rows |
| Reconnect | Issue a new credential generation and repeat handshake/discovery/validation | No old credential/cursor resurrection; canonical history retained; no duplicate/open-alert regression |
| Rotation/revocation | Rotate at provider, replace stored credential, prove old and new paths | New credential works; old credential fails; no secret appears in UI/log/evidence |

Do not intentionally overload the provider. Coordinate rate-limit and outage tests with the authorized account owner or use provider-supported sandbox controls.

### G. Representative scale and soak

Choose the profile before execution. A smaller profile cannot be relabelled as a larger one.

| Profile | Minimum authentic provider boundary | Required duration and phases | Permitted conclusion |
|---|---|---|---|
| Sandbox connectivity | Provider sandbox, including its available vehicle set | 30-minute baseline plus one connect/discover/map/validate/disconnect/reconnect journey | Connectivity/readiness only; no performance conclusion |
| Bounded pilot | At least 100 real provider vehicles or the exact smaller proposed LIMITED GO fleet, whichever is larger | 30-minute baseline; 2-hour steady state; ≥6-page worker backlog; ≥21-page manual boundary; one authorized 429/outage; restart before and after cursor commit; terminal drain | Only the measured fleet/tenant boundary |
| Large fleet | At least 1,000 real provider vehicles and the full intended supported tenant/concurrency mix | 30-minute baseline; 4-hour steady state; 1-hour burst/backlog/fault phase; restart before/after commit; full drain; 60-minute post-recovery soak | Candidate for large-fleet acceptance after independent review |

If the provider cannot supply the minimum authentic data volume, the corresponding profile is **NOT TESTED** and cannot pass. Do not synthesize provider evidence to fill the gap.

For every phase sample operational/resource state once per minute and record: provider vehicles/events/pages, durable run cursor, attempts, written/unmatched/historical/rejected counts, error/retry counts, backlog age, drain progress, database growth, CPU, memory, connection-pool and storage-I/O utilization, and operator-visible freshness/status. Separately retain every provider-event freshness value and every run duration, then calculate p50/p95/p99/max using the declared method and population count. Capture pre-run and terminal provider event-ID/row-count sets and require the matrix in section E to pass.

### H. Disconnect and reconnect

1. Record pre-disconnect connector generation/status, credential-present indicator, cursor, device/history/latest/alert counts and audit timestamp.
2. Start a manual or scheduled sync, then disconnect in OpsTrax and record the in-flight lease/generation outcome.
3. Confirm stored credential/cursor/test state is cleared and record the provider-side revoke/rotation timestamp.
4. For more than one complete worker interval (at least 6 minutes), attempt both scheduled and manual paths and prove zero post-disconnect telemetry writes.
5. Reconnect with a newly issued credential and repeat handshake, discovery and validation.
6. Compare post-reconnect device/history/latest/alert counts and IDs to the baseline: canonical history retained, 0 missing/duplicate IDs, and no live-state or open-alert regression.

### I. Browser and accessibility evidence matrix

Run the same customer journey in the exact deployed build. Record defects through Observe → Evidence → Root Cause → Fix → Test → Exact-SHA Deploy → Same Journey Retest → Close.

| Evidence area | Minimum execution |
|---|---|
| Browser/version | Current stable Chrome plus one independently selected supported browser/version; record exact versions |
| Viewports | Desktop 1440×900, laptop 1280×720 and 400×800 responsive viewport |
| Keyboard only | Complete configure, test, discover, map, validate, sync and disconnect without a pointer; no keyboard trap |
| Focus | Visible focus on every action; dialog focus enters the dialog, remains contained and returns to the invoking control |
| Status/error announcements | Handshake, sync, stale, failure and recovery feedback exposed through appropriate live/status semantics without repeated or misleading announcements |
| Screen reader | VoiceOver + Chrome on macOS and one independent supported screen-reader/browser combination; record version and journey result |
| Labels/instructions | Token, provider identity, vehicle, effective time, result counts and destructive-action controls have programmatic names and understandable instructions |
| Reflow/zoom | 200% zoom and responsive viewport preserve controls, error text, evidence counts and action order without hidden critical content |
| Console/network | Zero uncaught console errors; expected tenant-scoped requests only; no provider token in request URLs, responses rendered to the page, console output or screenshots |

Store customer screenshots/video separately from staff-assisted logs, queries and metrics. Each artifact must carry evidence ID, exact SHA, browser/version, viewport, UTC time, tenant alias, role, classification and SHA-256.

## 6. Customer handoff and support record

Before gate disposition, publish an access-controlled customer handoff containing:

- supported Samsara boundary (`api.samsara.com` US API cloud only), polled endpoints/fields and explicit EU/UK/Canada-cloud/webhook exclusions;
- required OpsTrax permissions and Samsara scopes/tags;
- install, mapping, validation, rotation, disconnect and reconnect steps;
- status/error/stale-feed interpretation and escalation route;
- provider and OpsTrax support ownership, severity definitions and evidence-retention location;
- pilot size/throughput boundary and any observed provider-rate limitations;
- rollback procedure and the exact candidate SHA.

The handoff must be executable by a customer administrator without database access or engineering-only commands.

## 7. Stop conditions

The named Principal SDET, Security reviewer, SRE reviewer or CTO may stop the affected run. Stop immediately for any P0/P1, suspected cross-tenant/branch leakage, secret exposure, silent ambiguous mapping, provider-truth mismatch, data loss/corruption, duplicate canonical event, live-state regression, unbounded retry/backlog or candidate/deployment mismatch.

On stop: disable manual/scheduled sync for the tenant, revoke or isolate the test credential when security/provider truth is at risk, preserve immutable evidence, open an incident/defect ID and record the last committed cursor and row baselines. Resumption requires CTO plus the controlling independent reviewer, a recorded root cause/fix, a new exact-SHA baseline when code changed, and full same-journey retest.

Record Observe → Evidence → Root Cause → Fix → Test → Exact-SHA Deploy → Same Journey Retest → Close for every defect. A fixed defect remains open until the same customer journey passes on the replacement exact SHA.

## 8. Gate disposition checklist

- [ ] Real authorized account and region recorded.
- [ ] Exact frontend/API/database candidate recorded.
- [ ] Connect → Authenticate → Discover → Map → Validate → Sync → Monitor → Disconnect/Reconnect passed.
- [ ] Provider pagination, retry/rate-limit, backfill, recovery and representative-scale evidence retained.
- [ ] Zero P0/P1, leakage, silent ambiguous mapping and fabricated data.
- [ ] Customer handoff/support record accepted.
- [ ] Independent Security, Principal SDET, Fleet Product and SRE acceptance recorded.
- [ ] CTO GO / LIMITED GO / NO-GO recorded in #115.
- [ ] Capability Truth Matrix changed only if the formal disposition authorizes it.
