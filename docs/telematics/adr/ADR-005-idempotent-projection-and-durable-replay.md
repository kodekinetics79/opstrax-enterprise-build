# ADR-005 — Idempotent Projection + Durable Store-and-Forward Replay

- **Status:** Proposed
- **Date:** 2026-07-12
- **Deciders:** PostgreSQL + Distributed Systems Engineer
- **Target posture:** Full cloud-native
- **Related:** ADR-002 (event backbone), ADR-003 (storage tiers), ADR-004 (gateway hosting)

## Context — verified current state

The gateway already opens a durability seam for backbone outages but never closed the loop:

| Concern | Verified current implementation |
|---|---|
| Parking on failure | `GatewayConnection.PublishPumpAsync` catches a backbone publish failure and calls `IStoreAndForwardBuffer.EnqueueAsync(...)` instead of dropping the fix. Correct. |
| Draining the buffer | `IStoreAndForwardBuffer.TryDequeue` existed but **had no consumer**. The Increment-1 review flagged the replay path as unimplemented and called out an ordering risk on failure: parked events were never re-emitted, and any naive re-emit that re-queued a failed entry to the tail would let a later fix for the same device overtake it. |
| Projection idempotency | The consumer that folds canonical events into `latest_vehicle_positions` had no dedupe. The backbone is **at-least-once** (`IEventBackbone`: "a consumer … must deduplicate on `EventId`"), and replay makes redelivery certain, so a naive projector double-counts and can stamp an older fix over a newer one. |

## Decision

Add two cooperating, layered defenses — one at the gateway, one at the database — implemented as **isolated new files** that touch neither the framing loop nor the existing connection classes.

### 1. Store-and-forward replay (ordering-safe, lossless)

`StoreAndForwardReplayService` (a `BackgroundService`) is the sole consumer of `TryDequeue`. It drains oldest-first and **holds each entry until it republishes successfully** — a failing entry *blocks* the drain rather than being dropped or re-queued to the tail. This is the crux of the ordering fix: because the buffer is FIFO and a device's fixes are enqueued in order, never advancing past a stuck entry guarantees no later fix for that device overtakes it. Failed attempts grow a geometric backoff up to a cap, so a minutes-long outage costs a steady trickle of attempts, not a hot loop. On host shutdown mid-retry the held entry is returned to the buffer so it is not lost for the process lifetime (cross-restart durability remains the WAL-backed buffer's job — see `IStoreAndForwardBuffer`).

### 2. Idempotent + monotonic projection (order-independent correctness)

`IPositionProjectionStore.ApplyAsync` is **idempotent** (a redelivered `EventId` is a no-op) and **monotonic** (an older device-fix-time never overwrites a newer stored fix). The Postgres implementation runs two statements in one transaction, per `database/migrations/telematics/006_projection_inbox.sql`:

1. `INSERT INTO telemetry_projection_inbox (event_id, …) ON CONFLICT (event_id) DO NOTHING` — the `UNIQUE(event_id)` dedupe ledger. Zero rows affected ⇒ duplicate ⇒ skip the upsert.
2. A `latest_vehicle_positions` upsert whose `ON CONFLICT (company_id, vehicle_id) DO UPDATE … WHERE EXCLUDED.device_fix_time >= stored device_fix_time` guard makes a stale (older) fix a no-op.

`InMemoryPositionProjectionStore` mirrors the same two invariants (a `HashSet` of seen `EventId`s + a per-vehicle last-write-wins-by-fix-time map) for tests.

## Why both layers

The two guarantees compose into belt-and-suspenders correctness. Store-and-forward preserves per-device order on the happy path; the projection stays correct **even when that order is lost** (a delayed replay, a redelivery, a reordering across partitions). A downstream outage can therefore neither lose a fix (replay re-emits it) nor corrupt the live-map snapshot with a stale one (the monotonic guard rejects it). Neither layer alone is sufficient: replay cannot prevent duplicates the backbone itself injects, and the projection cannot resurrect an event the gateway dropped.

## Consequences

- The gateway now gains one external dependency (`Npgsql`) — confined to `PostgresPositionProjectionStore`; `Contracts` stays dependency-free.
- `latest_vehicle_positions.correlation_id` is intentionally **not** written by the projector (stage12a `VARCHAR(120)` vs. UUID drift). The clean UUID correlation anchor lives on `telemetry_projection_inbox.correlation_id`.
- `telemetry_projection_inbox` needs an owner-run pruning job to age out rows past the replay window (index `idx_projection_inbox_company_projected` supports it).
- Non-positional (heartbeat) and vehicle-unbound events are deduped in the inbox but not projected — the inbox is the authoritative "have I seen this event" ledger regardless of whether it moved the map.
