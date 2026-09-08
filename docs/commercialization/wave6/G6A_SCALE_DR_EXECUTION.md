# G6A Scale / Resilience / DR / Observability — Current-Build Execution Baseline

Parent: #145 / #110  
Entry: `main@1f3b5de029b33e9315fb96c80988e610665c41b0`  
State: ACTIVE under `CR-2026-09-03-04` when v2.5 merges.

## Existing product foundation to preserve

- `tests/load/run_load.mjs` + `tests/load/readonly.js` already provide a guarded k6 execution path and have been used for bounded read-only certification load evidence.
- `tools/dr-restore-drill.sh` already defines a Neon throwaway-branch recovery drill intended to measure RPO/RTO and verify readiness/core counts without risking production.
- Existing production-shaped rehearsal, release-container, exact-SHA provenance and readiness/critical-worker checks remain mandatory foundations.
- Existing pilot recovery documents contain pending restore/RPO/RTO evidence; these are gaps to close, not evidence to relabel.

## Atomic scale program

1. Inventory current load profiles, endpoints, thresholds and destructive-safety guards.
2. Define package-representative workloads: 1K, 2.5K, 5K+ vehicles; map/roster/search/export/report; telemetry bursts; provider backfill; worker queues; device reconnect storms.
3. Predeclare p50/p95/p99/error/drop/resource thresholds per critical endpoint/journey instead of one aggregate threshold.
4. Add stepped load, stress-to-known-limit and soak modes while retaining a hard target allowlist and mutation controls.
5. Run visible Chrome during representative backend load for customer-critical surfaces.
6. Measure DB pool/query saturation, worker lag, queue depth, memory/CPU and telemetry freshness.

## Atomic resilience / DR program

1. Make the existing Neon restore drill reproducible from a frozen source/backup point with explicit expected row/integrity hashes.
2. Measure actual RPO/RTO on an isolated recovery branch.
3. Prove API/worker/gateway restart and rolling-deploy drain/reconnect behavior.
4. Exercise provider outage/backlog/replay and duplicate/reordered event recovery.
5. Exercise database-connectivity interruption and fail-closed/recovery behavior in an isolated environment.
6. Verify key/certificate continuity/recovery and no protected-data loss.
7. Add package-specific object/document/video recovery where those packages are sold.
8. Re-run exact-SHA Chrome/persisted-state acceptance after recovery.

## Observability program

Establish SLO-aligned metrics/alerts for API latency/error, DB saturation, telemetry/provider lag, queue depth, stale/missing/failed workers, device connectivity and gateway/provider health. Correlation must allow support to follow a customer-impacting event without privileged direct database access.

## First execution slice

Do not create a new load framework. Extend the existing guarded k6 runner with an inventory/report of current profiles and a predeclared 1K/2.5K/5K workload matrix, then execute only on an authorized isolated environment. In parallel, dry-run and validate the existing DR restore script inputs/stop conditions; do not point it at customer production.

## Stop conditions

RED if a load test can mutate unapproved data, target production by default, hide dropped iterations, relabel aggregate p95 as every-endpoint SLO, or if a DR drill risks the source database / deletes recovery evidence.