# Telematics storage: partitioning, retention & query-plan guarantees

Scope: the telematics ingest/live-map data path — `latest_vehicle_positions`,
`location_events`, and the tiered store introduced by the
`database/migrations/telematics/` migrations (`canonical_telemetry_events`,
`raw_packets`, `device_state_transitions`).

Companion migrations:

| File | Adds |
| --- | --- |
| `telematics/001_latest_position_provenance.sql` | Provenance + trust columns on `latest_vehicle_positions`; `legacy` backfill |
| `telematics/002_device_lifecycle.sql` | `eld_devices.device_state` (16-state CHECK) + `device_state_transitions` audit |
| `telematics/003_canonical_telemetry_timeseries.sql` | Monthly RANGE-partitioned `canonical_telemetry_events` (warm) + `raw_packets` (cold) |

> These are NEW migration files. They do **not** auto-run against prod — the app
> connects as the restricted `opstrax_app` role and skips startup schema init
> under RLS (see stage28/stage29). They must be applied **by the DB owner**.

---

## 1. Tiered model (hot / warm / cold)

Telemetry has a sharp access-frequency gradient: the map only ever reads *the
latest* fix per vehicle, dashboards read *recent* history, and forensic/compliance
reads touch *old* data rarely. We store each tier where its access pattern is cheap.

```
        write path                         read frequency        store
        ----------                         --------------        -----
device → raw frame ────────────────────►   rare (forensic)       COLD  raw_packets / object store
       → normalized canonical event ───►    medium (history)     WARM  canonical_telemetry_events (monthly parts)
       → live snapshot upsert ─────────►    very high (map 3s)   HOT   latest_vehicle_positions (1 row/vehicle)
       → breadcrumb append ────────────►    high (recent trail)  HOT→  location_events (recent) ── ages into WARM
```

### HOT — `latest_vehicle_positions`
- **Shape:** exactly one row per `(company_id, vehicle_id)` (enforced by the
  existing `UNIQUE (company_id, vehicle_id)`), upserted on every ping.
- **Served to:** the live map SSE loop (`/api/telemetry/positions/stream`, 3s tick)
  and the REST snapshot (`/api/telemetry/positions`).
- **Size:** bounded by fleet size, not by time — it never grows with history, so it
  needs no retention policy. Provenance/trust columns (telematics 001) let the map
  de-weight or flag stale/low-trust fixes without touching history.

### WARM — `canonical_telemetry_events`
- **Shape:** append-only normalized events, **RANGE-partitioned by month on
  `event_time`**.
- **Served to:** breadcrumb replay, trip reconstruction, analytics, stale-device
  and behavior queries.
- **Retention:** **~13 months** online (rolling year + current). Ageing out is a
  metadata `DETACH PARTITION` + `DROP TABLE`, **not** a row-by-row `DELETE`, so it
  is O(1) and never bloats/rewrites the live table or its indexes.

### COLD — `raw_packets` (or object storage)
- **Shape:** byte-exact inbound frames (`BYTEA`) **or** an object-storage pointer
  (`raw = NULL`, `storage_url = 's3://…'`), enforced XOR by `ck_raw_packets_body`.
- **Served to:** forensic replay, dispute/compliance evidence. Read almost never.
- **Retention:** per compliance window (e.g. multi-year for KSA/ELD mandates).
  Large fleets should **offload the bytes to object storage** and keep only the
  pointer so the primary stays small; object lifecycle rules own cold expiry.

`location_events` (the legacy flat breadcrumb log) remains the short-horizon hot
trail; as volume grows it is expected to migrate onto the same monthly-partition
scheme as `canonical_telemetry_events` (the warm store is the forward target).

---

## 2. Retention windows (summary)

| Tier | Table | Online window | Age-out mechanism |
| --- | --- | --- | --- |
| Hot | `latest_vehicle_positions` | current snapshot only | overwritten on upsert (no history) |
| Hot | `location_events` | ~7–30 days recent trail | migrate to warm partitions / DETACH+DROP |
| Warm | `canonical_telemetry_events` | ~13 months | `DETACH PARTITION` → `DROP TABLE` per month |
| Cold | `raw_packets` | compliance window (years) | object-store offload + lifecycle expiry, or monthly partition + DETACH |
| Audit | `device_state_transitions` | lifetime of device + retention policy | append-only; archive with tenant data |

**Monthly rollout job** (owner-run: `pg_cron`, `pg_partman`, or an app worker) must
pre-create the *next* month partition before month start and keep the DEFAULT
partition empty:

```sql
CREATE TABLE IF NOT EXISTS canonical_telemetry_events_2026_09
  PARTITION OF canonical_telemetry_events
  FOR VALUES FROM ('2026-09-01 00:00:00+00') TO ('2026-10-01 00:00:00+00');
```

The `canonical_telemetry_events_default` partition exists only as a safety net so an
out-of-window fix never fails ingest. Attaching a real month partition scans the
default for overlaps, so it must be kept near-empty.

---

## 3. Query-plan guarantees (no unbounded scans)

The two latency-critical read paths must stay **index-bounded** and **partition-pruned**
under the tenant RLS predicate. Because RLS is composed as an extra
`company_id = current_setting('app.current_tenant_id')::bigint` conjunct, and every
telematics index is **tenant-leading** (`company_id` first), the RLS predicate is
SARGable and rides the same index — it never forces a sequential scan.

> These plan shapes are the design contract. Reproduce them off-prod with
> `EXPLAIN (ANALYZE, BUFFERS)` after loading representative data — **do not** run
> `EXPLAIN` against production from this work.

### 3.1 Live-map snapshot — `latest_vehicle_positions`

Actual query (`EndpointMappings.cs`, `/api/telemetry/positions[/stream]`):

```sql
SELECT lvp.vehicle_id, lvp.lat, lvp.lng, /* … */,
       EXTRACT(EPOCH FROM (NOW() - lvp.received_at))::BIGINT AS seconds_since_ping,
       CASE WHEN EXTRACT(EPOCH FROM (NOW() - lvp.received_at))::BIGINT > 900
            THEN 1 ELSE 0 END AS is_stale
FROM latest_vehicle_positions lvp
LEFT JOIN vehicles v ON v.id = lvp.vehicle_id
LEFT JOIN drivers  d ON d.id = lvp.driver_id
WHERE lvp.company_id = $1;                 -- + RLS: company_id = current_tenant
```

Expected plan shape:

```
Nested Loop Left Join
  ->  Nested Loop Left Join
        ->  Index Scan using latest_vehicle_positions_company_id_vehicle_id_key
              Index Cond: (company_id = $1)         -- leading col of UNIQUE(company_id, vehicle_id)
        ->  Index Scan using vehicles_pkey (Index Cond: id = lvp.vehicle_id)
  ->  Index Scan using drivers_pkey (Index Cond: id = lvp.driver_id)
```

- **Bounded by:** `UNIQUE (company_id, vehicle_id)` — `company_id` is the leading
  column, so the WHERE + RLS predicate is an index range over exactly one tenant.
- Rows scanned = that tenant's vehicle count (≤ fleet size), **not** the whole table.
  The `is_stale` / `seconds_since_ping` expressions are computed **on the already
  index-selected rows** (the 900s threshold is a projection, not a filter), so they
  add no scan cost. **No Seq Scan.**

### 3.2 Stale-device / recent-history — `canonical_telemetry_events`

Stale-device sweep and breadcrumb reads filter one tenant + one vehicle over a time
window (the app's stale threshold is 900 s / 15 min — `stale_device` rule):

```sql
SELECT vehicle_id, event_time, lat, lng, trust_score
FROM canonical_telemetry_events
WHERE company_id = $1
  AND vehicle_id = $2
  AND event_time >= now() - interval '15 minutes'   -- window
ORDER BY event_time DESC
LIMIT 100;                                            -- + RLS: company_id = current_tenant
```

Expected plan shape:

```
Limit
  ->  Append                                   -- only partitions overlapping the window
        Subplans Removed: N                    -- PARTITION PRUNING drops all other months
        ->  Index Scan Backward using canonical_telemetry_events_2026_07_company_id_vehicle_id_event_time_idx
              Index Cond: (company_id = $1 AND vehicle_id = $2 AND event_time >= now() - '00:15:00')
```

- **Partition pruning:** the `event_time` predicate lets the planner touch only the
  partition(s) overlapping the window (`Subplans Removed` / `Partitions: 1 of N`).
  A 15-minute stale sweep touches **one** month partition, never the full history.
- **Index-bounded within the partition:** `idx_cte_company_vehicle_time
  (company_id, vehicle_id, event_time DESC)` matches the equality-equality-range
  predicate and the `ORDER BY event_time DESC LIMIT`, so it is an `Index Scan
  Backward` with an early `Limit` cutoff — no sort, no heap scan of the month.
- Tenant-wide stale sweeps (drop the `vehicle_id` equality) still ride the same
  index on its `(company_id, event_time)` prefix and still prune by month.
- Correlation/provenance joins use the partial `idx_cte_correlation` on
  `correlation_id`. **No Seq Scan, no cross-partition full scan.**

### 3.3 Device transition audit — `device_state_transitions`

```sql
SELECT from_state, to_state, reason, actor, at
FROM device_state_transitions
WHERE company_id = $1 AND device_id = $2
ORDER BY at DESC LIMIT 50;
```

Rides `idx_dst_company_device_at (company_id, device_id, at DESC)` as an
`Index Scan` with the `LIMIT` satisfied directly from index order — bounded to one
device's timeline within one tenant.

---

## 4. RLS / tenant-scope requirement (mandatory)

Every telematics table introduced here carries `company_id BIGINT NOT NULL` and is
enrolled in the platform's Row-Level Security model (stage19 policies, stage20
`FORCE` + restricted `opstrax_app` role):

- `canonical_telemetry_events`, `raw_packets`, `device_state_transitions` each get
  `ENABLE` + `FORCE ROW LEVEL SECURITY` and the two standard policies:
  - **`tenant_isolation`** — `company_id = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint`
    for both `USING` and `WITH CHECK` (read and write).
  - **`platform_admin_bypass`** — explicit, GUC-gated
    (`app.platform_admin = 'on'`), never a blanket role bypass. Permissive policies
    OR-combine: a row is reachable iff tenant matches **or** platform-admin context
    is explicitly set.
- `latest_vehicle_positions` already carries `company_id` and is therefore already
  RLS-enabled + FORCE'd by stage19/stage20; telematics 001 adds columns only, no
  policy change.
- `eld_devices` is a **global** device registry with **no** `company_id` (keyed by
  `device_serial`), so it is intentionally *not* RLS-scoped — the tenant-scoped
  audit of its lifecycle lives in `device_state_transitions`, which **is** RLS-scoped.

**Enforcement prerequisites** (same as stage19/stage20, still owner/deploy actions):

1. The app must connect as a non-superuser, non-`BYPASSRLS`, non-owner role
   (`opstrax_app`); the local/test superuser continues to bypass RLS.
2. The request pipeline must `SET app.current_tenant_id = <company_id>` on a
   request-scoped connection/transaction before any tenant query. Until that is
   wired, RLS is a dormant backstop — **application-layer `WHERE company_id = …`
   predicates remain mandatory** on every telematics read/write (the map query in
   §3.1 already does this).

**Design rule:** never expose a telematics read/write that is not filtered by
`company_id`. Cross-tenant reads (fleet-wide platform analytics) must go through the
explicit `platform_admin_bypass` GUC, never by dropping the tenant predicate.
