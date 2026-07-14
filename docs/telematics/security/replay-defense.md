# Telematics replay + sequence defense

Status: design + isolated implementation (integration into the gateway framing loop is a
follow-up). This document specifies the durable replay/sequence guard, the two shipped
implementations, and how they close the gaps called out in the
[threat model](threat-model.md) (§1.2 weak path, row **D2**, and the "packet replay" rows).

## 1. Problem

The strong ingest path (`/api/telemetry/ingest`) already has a **durable** replay defense: an
atomic `INSERT INTO telemetry_nonces(device_id, nonce)` guarded by `UNIQUE(device_id, nonce)`,
where a `23505` unique-violation means *replay → reject* (threat model §1.1).

The weak gps-ingest path does **not**. Its replay cache is:

- **process-local** — a `ConcurrentDictionary` on one instance's heap, so a replay accepted by
  instance B after instance A saw it slips through;
- **non-durable** — the window is empty again after a restart/redeploy, reopening the replay
  window on every bounce; and
- **unbounded except by TTL** — a distinct-nonce flood can balloon process memory (row **D2**),
  and it is pruned only inline on the next request.

We want the gps-ingest / gateway path to get the *same* guarantee the strong path has, plus a
per-device **sequence** check the nonce table does not provide.

## 2. Contract

```
ReplayDecision Check(deviceId, protocolSerial, contentHash, deviceFixTimeUtc)

ReplayDecision ∈ { Accept | DuplicateReplay | OutOfOrder(lastSeen) }
```

Two defenses are combined behind one call:

1. **Bounded dedup window** keyed on the exact triple `(deviceId, serial, contentHash)`. A hit is
   a byte-for-byte replay → `DuplicateReplay`.
2. **Per-device monotonic high-water serial.** A serial strictly behind the device's high-water
   mark, that is *not* a recognised duplicate, is `OutOfOrder(lastSeen)` — a stale/reordered
   packet or a replay whose window entry was already evicted.

`protocolSerial` for GT06 is the frame **information serial number** decoded by `Gt06Adapter`
(surfaced as the `"serial"` field and `DecodedMessage.ProtocolMessageId`). `contentHash` is an
opaque digest (e.g. SHA-256 hex) of the frame the caller wants to deduplicate on.
`deviceFixTimeUtc` is supplementary audit context; the canonical ordering token is the serial.

### Safety property

A replay of a previously **accepted** frame is *always* rejected:

- while its entry is still in the dedup window → `DuplicateReplay`;
- after eviction → its serial is by then below the high-water mark → `OutOfOrder`.

Eviction can therefore never silently readmit a replay.

### Serial wraparound

GT06's serial is 16-bit and wraps at 65 536. Two handling modes:

- **In-memory guard**: construct with `serialModulus: 65536` to compare serials on a circle — a
  step is "forward" when the circular distance ahead is within the near half `(0, modulus/2]`, so
  `65530 → 3` is forward progress, not out-of-order.
- **Postgres guard**: compares serials as plain `BIGINT`. Feed it a monotonic ingest sequence, or
  unwrap the counter before calling, when wrap tolerance is required.

## 3. Implementations

### 3.1 `InMemoryReplayGuard` (dev / test / single-instance)

- Per-device state: a high-water serial plus a bounded **LRU** set of recently-seen
  `(serial, contentHash)` keys. Least-recently-*seen* is evicted first (a duplicate hit refreshes
  recency). The window size is a hard constant per device, fixing the D2 unbounded-growth concern.
- **Thread-safe**: device lookup via `ConcurrentDictionary`; the classify-then-record critical
  section for a single device runs under that device's own lock, so different devices never
  contend and two racing duplicates cannot both be accepted.
- **Not durable / not shared** — window is empty after a restart and is per-process. Use only for
  dev, tests, and single-instance deployments.

### 3.2 `PostgresReplayGuard` (durable + shared — production)

- Backed by `telemetry_replay_seen (device_id, serial, content_hash, device_fix_time, seen_at)`
  with `UNIQUE(device_id, serial, content_hash)` (migration
  `database/migrations/telematics/005_replay_guard.sql`).
- One round-trip per check. A CTE captures the device high-water serial *before* the insert and
  performs an idempotent `INSERT … ON CONFLICT (device_id, serial, content_hash) DO NOTHING`:

  | Result of the statement | Decision |
  |---|---|
  | insert suppressed by the unique constraint | `DuplicateReplay` |
  | inserted, but `serial < prev high-water` | `OutOfOrder(prev)` |
  | inserted, at or ahead of the mark | `Accept` |

- The window **is the database**: durable across restarts and shared across every gateway
  instance sharing the DB — the same primitive `telemetry_nonces` uses, now with a sequence check.
- Not tenant-scoped / no RLS: `device_id` is a pre-ownership identifier written under a system
  scope, mirroring `eld_devices` / `telemetry_nonces`.
- **Retention**: a serial below the high-water mark is caught as `OutOfOrder` even without a dedup
  row, so the table only needs a bounded window. Prune well beyond the ingest freshness window
  (gps-ingest ±300 s), e.g. `DELETE … WHERE seen_at < NOW() - INTERVAL '24 hours'` on a timer.

## 4. Threat-model mapping

| Threat-model item | How this addresses it |
|---|---|
| §1.2 "Replay defense: in-memory, process-local, non-durable" | `PostgresReplayGuard` makes it durable + shared, matching §1.1's `telemetry_nonces`. |
| **D2** — replay-store exhaustion / unbounded in-memory cache | `InMemoryReplayGuard` is a bounded per-device LRU; Postgres is prunable on a timer. |
| "Packet replay" (§1.1 vs §1.2) | Exact-triple dedup + per-device sequence high-water reject both byte-replays and stale/reordered frames. |

Out of scope here (unchanged): per-device / per-gateway authentication (S1/S2), GPS spoofing
(S3), and sensor-value falsification (T3) — replay defense assumes the frame is otherwise
authenticated upstream.

## 5. Integration (follow-up, not in this change)

The gateway framing loop calls `Check` after decode + ownership resolution and before publishing:
`Accept` → publish; `DuplicateReplay` / `OutOfOrder` → drop and count a metric. Wiring the guard
into `GatewayConnection` / the ingest handler, choosing the implementation by config, and adding
the drop metrics are deliberately left to the integration step so this defense lands as isolated,
independently-reviewable units.
