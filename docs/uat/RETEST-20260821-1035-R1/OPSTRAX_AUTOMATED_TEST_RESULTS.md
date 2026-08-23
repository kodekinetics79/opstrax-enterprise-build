# OpsTrax Automated Test Results — Gate 5

Run: `RETEST-20260821-1035-R1` · **Executed SERIALIZED** (no concurrent packet), because measured interference between concurrent packets sharing the isolated database produced spurious RLS failures and a `40P01: deadlock detected` that vanished on isolation. Counts from a shared run are not trustworthy and are not reported here.

Environment: isolated PostgreSQL `opstrax-retest-r1-pg` @ 127.0.0.1:55437 (`postgres:17`, CI-pinned digest), restricted roles `opstrax_app`/`opstrax_system` provisioned. Local HEAD `a6378c7` + remediation worktree. Deployed staging SHA `979c142` (unchanged — nothing deployed).

## Results

| Lane | Result | Artifact |
|---|---|---|
| .NET non-DB | **1670 / 1670 PASS**, 0 failed, 0 skipped | `GATE5-backend-unit.trx` |
| .NET PostgreSQL + integration | **431 / 431 PASS** on retry; **1 intermittent failure** in run 1 (see below) | `GATE5-backend-db.trx`, `GATE5-backend-db-retry.trx` |
| RLS / tenant isolation | included in the DB lane filter, 0 failures | — |
| Telematics protocols | **39 / 39 PASS** | `GATE5-telematics-Protocols.trx` |
| Telematics security | **47 / 47 PASS** | `GATE5-telematics-Security.trx` |
| Telematics integration (incl. Postgres durability) | **177 / 177 PASS** | `GATE5-telematics-Integration.trx` |
| Frontend `tsc --noEmit` | **PASS** (exit 0) | — |
| Frontend production build + bundle budget | **PASS** — 201 chunks, largest gzip 95.22 KiB | — |
| Frontend RBAC contract script | **PASS** | — |
| Frontend device-installation contract script | **PASS** | — |
| Node backend build | **PASS** | — |
| Node backend typecheck | **PASS** | — |
| Node security-hardening suite | **6 / 6 PASS**, 0 fail | — |
| .NET warning ratchet | **PASS** — 486 distinct vs ceiling 487 (decreased) | — |

## Totals

**2,371 automated tests executed, 0 failing at rest.** (.NET 1670 + 431, telematics 39 + 47 + 177, Node 6, plus 4 frontend gates.)

For contrast, the preserved UAT run recorded the PostgreSQL/RLS lane as **BLOCKED — no product assertion executed at all**, and telematics integration at 166/173 with 7 environment-blocked.

## Intermittent failure — recorded, not swept up

Run 1 of the serialized DB lane failed `TelemetryLineagePostgresTests.BreadcrumbApi_ReturnsPersistedPerEventOperationalLineage` with `Assert.Single() Failure: The collection was empty` (HTTP 200, zero breadcrumb points).

Investigation, before any retry:
- The test file is **untouched by this run** (`git diff` empty).
- The `TelemetryBreadcrumbs` handler is **untouched by this run**.
- No leftover rows from a prior aborted run (`assignment_id=730001` → 0 rows).
- The test **passes in isolation** (1/1).
- The full lane **passes on one retry** (431/431).

**CORRECTED CONCLUSION (superseding this run's first analysis).** I originally recorded the cause as "concurrent tests mutating vehicles/branches within the shared lane." **That was wrong and is retracted.** Test parallelisation is disabled outright — `TestAssemblyConfiguration.cs:7` sets `[assembly: CollectionBehavior(DisableTestParallelization = true)]` and `xunit.runner.json` sets both `parallelizeTestCollections` and `parallelizeAssembly` to `false` — so concurrent mutation was never possible. I verified this directly.

The real mechanism, established by an independent reviewer and confirmed here, is a **cross-clock comparison across a zero-slack window edge**:
- The test seeds its single `location_events` row with `event_time = NOW()` — the **PostgreSQL container's** clock.
- It passes no `from`/`to`, so the handler defaults `to = DateTime.UtcNow` — the **.NET host's** clock — and `from = to.AddHours(-24)`.
- Both queries filter `event_time BETWEEN @from AND @to`. The left edge has 24 hours of slack; **the right edge has none.**

When the container clock ran even a few milliseconds ahead of the host, the just-inserted row was strictly in the future relative to `@to`, both queries returned nothing, and the handler correctly returned HTTP 200 with an empty array. The failure duration was 26 ms across ~10 round trips, implying a 2-4 ms gap. Docker Desktop's LinuxKit VM on Apple Silicon keeps an independent guest clock, so millisecond drift is routine — which is why this never reproduces on the Linux CI runner, where the service container shares the host kernel clock.

Ruled out by direct check: no unscoped `DELETE`/`TRUNCATE` on `location_events`/`vehicles` anywhere in the assembly; GUID-suffixed ids so no collision; RLS/GUC leakage impossible (the policy is fail-closed, so the INSERT would have thrown 42501 rather than returning an id).

Fix is one token in the test — seed at `NOW() - INTERVAL '2 minutes'`, converting a 0 ms margin into 120,000 ms while leaving all four assertions and ~23h58m of window slack intact. Logged as **NEW-R1-12 (Medium, test hygiene)**. It is NOT reported as a clean 431/431: the first serialized run failed, and that is stated here.

## Gate-5 evidence caveats — stated, not buried

1. **Frontend lint is NOT cited as evidence.** `eslint`'s flat config ignores all of `frontend/src`, so "lint clean" proves nothing about the reviewed files. It was cited in earlier checkpoints of this run; that was an error, corrected here. The real frontend gates are `tsc --noEmit`, the production build, and the two contract scripts.
2. **The .NET DB lane runs against a dev-shaped database** (`opstrax_local`). It proves handler logic. It **cannot** prove protected-environment schema parity — that is what the migration-pure proof does, separately.
3. **Schema-contract proof is separate and uses a chain-only oracle.** See `OPSTRAX_MIGRATION_VERIFICATION_REPORT.md` and `artifacts/stage88-migration-pure-proof.log`.

## Guard tests repaired during this run

The three "drift ledger" tests certified false claims and were repaired before any result was trusted. Post-repair they run with an **empty allowlist** (83 entries retired — stage88 made every one unnecessary):

| Instrument | Before | After |
|---|---|---|
| Schema parity | scanned 2 of 48 services; table-unqualified matching; 83-entry allowlist with a prose rationale | globs all 48 + `CREATE TABLE`; table-qualified; **mechanical rule** forbidding allowlisting any column referenced in controller SQL; allowlist empty |
| Endpoint gate coverage | apostrophe-in-comment parser bug → bodies up to 2,387,562 chars inheriting other methods' gates; 683 registrations from 1 file | char-literal parsing + comment stripping + ordering assertion + non-literal enumeration; **1,002 registrations across 28 files**; largest body 157,358 |
| Migration orphan parity | false prose claim ("re-established later") | mechanical consumption rule; 10 orphans / 33 tables detected, all closed by stage88 |

## Warning classification

Baseline ceiling 487; current **486 distinct** (decreased by 1). Ratchet script PASS.
