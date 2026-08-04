# Telematics Event Contracts

The durable event backbone for the OpsTrax telematics fabric: **16 topics**, one envelope, one
keying rule.

- **Code:** `telematics/src/Opstrax.Telematics.Contracts/Eventing/`
  (`IEventBackbone`, `EventEnvelope<T>`, `TelematicsTopics`, `TelematicsEventKey`,
  `InMemoryEventBackbone`)
- **Payload of record:** `Opstrax.Telematics.Contracts.CanonicalTelemetryEvent` — the single
  normalized telemetry shape every producer converges on and every consumer reads.

---

## 1. The envelope

Every event on every topic is wrapped in `EventEnvelope<T>`. The envelope is the metadata
contract; the payload is the domain contract. Routing, dedupe, isolation and replay work off the
envelope alone and never crack the payload open.

| Field | Type | Purpose |
|---|---|---|
| `EventId` | `Guid` | Broker-level idempotency key. Delivery is **at-least-once**; consumers dedupe on this. |
| `CorrelationId` | `Guid` | Threads one originating frame/action through every topic hop. |
| `CausationId` | `Guid?` | Points at the `EventId` this event was derived from. `null` for a root event. |
| `OccurredAt` | `DateTimeOffset` | When the fact happened, not when the derivation ran. |
| `TenantId` | `Guid` | Registry-resolved owning tenant. The authoritative isolation scope. |
| `CompanyId` | `long` | Registry-resolved owning company within the tenant. |
| `SchemaVersion` | `int` | Payload schema version. Consumers gate on it. |
| `Payload` | `T` | The domain payload. |
| `Headers` | `IReadOnlyDictionary<string,string>` | Trace/transport metadata only. Never ownership or causality. |

`TenantId` / `CompanyId` are **copied up from the registry-resolved ownership**
(`ResolvedDeviceOwner`), never from the packet. A device asserting an IMEI proves nothing; the
registry decides who owns the event. This is what makes the envelope safe to route on.

## 2. Keys, partitions, ordering, isolation

One rule, two guarantees. Keys are built exclusively by `TelematicsEventKey`:

```
device-scoped:   {tenantId:N}:{companyId}:{deviceId}
command-scoped:  {tenantId:N}:{companyId}:cmd:{deviceId}
```

- **Ordering** comes from the **device** segment. Same device ⇒ same key ⇒ same partition ⇒
  events are consumed in production order. A stale fix can never overtake a fresher one for the
  same vehicle, and the trip/safety state machines can therefore be written as simple
  single-threaded folds over a partition.
- **Isolation** comes from the **tenant/company** prefix. One tenant's traffic can never collide
  with, reorder, or be mistaken for another's on a shared topic. `Subscribe<T>(topic,
  tenantFilter)` adds a second, defense-in-depth check on `Envelope.TenantId`.

Partition assignment uses a deterministic **FNV-1a** hash (`TelematicsEventKey.Partition`), not
`string.GetHashCode()`, so a key co-partitions identically across processes, restarts, languages
and between the in-memory dev bus and a real cluster.

**Guarantees:** at-least-once delivery; ordering **per key only** — never across keys. Consumers
must be idempotent on `EventId`.

**Partition counts:** high-volume ingest topics (`telemetry.raw`, `telemetry.decoded`,
`telemetry.normalized`, `vehicle.position.validated`, `vehicle.signal.normalized`) start at **32**;
mid-volume domain topics (`device.health`, `trip.lifecycle`, `diagnostic.event`, `safety.event`,
`media.metadata`) at **12**; control and error topics (`command.*`, `telemetry.rejected`,
`integration.deadletter`) at **6**. Partition counts are grow-only — increasing them rehashes
keys and breaks ordering continuity for in-flight devices, so grow only during a maintenance
window with consumers drained.

---

## 3. Topic contracts

### Ingest lane

#### `telemetry.raw`
- **Purpose:** opaque inbound frames exactly as they arrived off the wire, captured before any
  parsing. The forensic tape: everything downstream can be rebuilt from this topic.
- **Key:** `ForDevice(tenant, company, deviceId)`. Before identity resolution the device id is the
  *claimed* identifier (IMEI/serial) under a reserved `unresolved` tenant/company scope.
- **Ordering:** per connection/device — frames must be replayed to the decoder in arrival order,
  since stateful protocols (GT06 login → position) depend on it.
- **Schema:** `EventEnvelope<RawFrame>` — raw bytes (base64), `Transport`, `ProtocolName` (if
  sniffed), remote endpoint, gateway id, `ReceivedAtGatewayUtc`.
- **Producer:** `Opstrax.Telematics.Gateway` (TCP/UDP/MQTT/HTTP listeners).
- **Consumers:** decoder workers; protocol-forensics/replay tooling; abuse detection.
- **Retention:** 7 days (bytes are cheap relative to the debugging value; PII exposure is low
  since payloads are unparsed and access-controlled).
- **Replay:** the primary replay source. Re-run a new adapter version over a device's raw history
  to regression-test a decoder without touching production devices.
- **DLQ:** none — this topic *is* the pre-parse capture. Unroutable frames go to
  `telemetry.rejected`.

#### `telemetry.decoded`
- **Purpose:** frames successfully decoded by a protocol adapter but **not yet identity-resolved
  or normalized**. Still carries protocol-shaped fields.
- **Key:** `ForDevice(...)` with the claimed identity; still under the `unresolved` scope until the
  registry resolves it.
- **Ordering:** per device — normalization depends on prior-fix state (distance, speed sanity).
- **Schema:** `EventEnvelope<DecodedMessage>` — reuses
  `Contracts.Adapters.DecodedMessage` (`MessageType`, `DeviceIdentityRef`, decoded fields,
  `AdapterMetadata`).
- **Producer:** decoder workers (protocol adapter host).
- **Consumers:** the identity-resolution + normalization worker; adapter-quality metrics.
- **Retention:** 3 days.
- **Replay:** re-normalize after a normalization bug fix without re-decoding.
- **DLQ:** decode succeeded but identity/normalization failed ⇒ `telemetry.rejected`; infra
  failure after retries ⇒ `integration.deadletter`.

#### `telemetry.rejected`
- **Purpose:** the **poison-pill lane** for business-rule rejections — undecodable frames, parser
  guard trips, unknown IMEI, suspended/retired device (`DeviceLifecycleState`), replay/spoof
  suspicion, clock skew beyond tolerance. Distinct from `integration.deadletter`, which is for
  *infrastructure* failure.
- **Key:** `ForDevice(...)` on whatever identity was claimed, so a single misbehaving device's
  rejections stay grouped and can be rate-limited as a unit.
- **Ordering:** per claimed device. Not latency-sensitive.
- **Schema:** `EventEnvelope<RejectedFrame>` — rejection reason code, stage
  (`Decode` | `IdentityResolve` | `Normalize`), `ProtocolException` detail, the offending raw
  frame (truncated), `DeviceIdentityRef` as claimed.
- **Producer:** decoder and normalization workers.
- **Consumers:** device-onboarding alerting ("unknown IMEI hammering the gateway"); security
  (spoof/replay detection); a support console showing "why is my device not reporting?".
- **Retention:** 14 days — long enough for a support ticket cycle.
- **Replay:** after onboarding a previously-unknown device, replay its rejected window to backfill
  the fixes that were dropped while it was unregistered.
- **DLQ:** terminal — this *is* a rejection sink.

#### `telemetry.normalized`
- **Purpose:** the **spine of the fabric**. Fully decoded, identity-resolved, normalized
  observations. This is the fan-out point: everything downstream reads from here and nothing
  downstream needs to know which wire protocol produced the event.
- **Key:** `ForDevice(tenant, company, deviceId)` — now with **registry-resolved** ownership.
- **Ordering:** strict per device. Trip detection, odometer accumulation and geofence
  enter/exit are all order-dependent folds over this stream.
- **Schema:** `EventEnvelope<CanonicalTelemetryEvent>` — the canonical record, verbatim. Envelope
  `SchemaVersion` mirrors `CanonicalTelemetryEvent.SchemaVersion`
  (`CurrentSchemaVersion = 1`).
- **Producer:** the normalization worker.
- **Consumers:** position validator; signal normalizer; device-health monitor; trip detector;
  diagnostic engine; safety/rules engine; the timeseries/Postgres sink; analytics.
- **Retention:** **30 days** (the longest of the telemetry lane — it is the rebuild source for
  every derived read model), compacted-off, plus tiered/object-storage archive for long-horizon
  analytics.
- **Replay:** the canonical rebuild path. Drop and rebuild any derived store (live map positions,
  trip tables, health rollups) by replaying this topic from offset 0 for a tenant.
- **DLQ:** `integration.deadletter` after 3 retries with exponential backoff.

### Enriched domain lane

#### `vehicle.position.validated`
- **Purpose:** position fixes that passed geospatial/plausibility validation (no zero-island, no
  teleport, HDOP/accuracy within bounds, `TrustScore` above the map threshold) and are therefore
  safe to render. Directly feeds `latest_vehicle_positions` / `location_events`.
- **Key:** `ForDevice(...)`.
- **Ordering:** strict per device — a validated position is only meaningful relative to the last
  one; out-of-order delivery would make a vehicle jump backwards on the map.
- **Schema:** `EventEnvelope<ValidatedPosition>` — `GeoPoint`, speed, heading, `QualityFlags`,
  `TrustScore`, `Confidence`, `VehicleId`, plus the `CausationId` of the source canonical event.
- **Producer:** position validator.
- **Consumers:** live-map read-model projector; geofence engine; ETA engine; dispatch board.
- **Retention:** 7 days on the stream (the durable history lives in Postgres/timeseries).
- **Replay:** rebuild the live-map read model. Note the known freshness decay: positions older
  than ~15 min are surfaced as `Offline` rather than rendered as current.
- **DLQ:** `integration.deadletter`.

#### `vehicle.signal.normalized`
- **Purpose:** the VSS/COVESA signal bag exploded into **one event per signal** for consumers that
  care about a single signal (fuel, odometer, coolant temp) and should not have to subscribe to
  the full canonical firehose.
- **Key:** `ForDevice(...)` — **not** keyed by signal path, so all signals for a device stay
  co-partitioned and a consumer can correlate them without a cross-partition join.
- **Ordering:** per device; per-signal ordering follows from it.
- **Schema:** `EventEnvelope<NormalizedSignal>` — VSS path (e.g. `Vehicle.Powertrain.Odometer`,
  see `Signals.VssSignals`), `SignalValue`, unit, source `EventId` as `CausationId`.
- **Producer:** signal normalizer.
- **Consumers:** fuel/odometer rollups; utilization analytics; threshold alerting;
  maintenance-interval tracking.
- **Retention:** 7 days on the stream; **log-compacted** companion topic keyed
  `{tenant}:{company}:{device}:{vssPath}` retains the *latest* value of every signal forever, so a
  new consumer can bootstrap current state without a full replay.
- **Replay:** rebuild signal rollups; bootstrap from the compacted companion.
- **DLQ:** `integration.deadletter`.

#### `device.health`
- **Purpose:** connectivity, battery, firmware version and heartbeat **state transitions**
  (online → degraded → offline). Transitions, not samples: emitted on change, not per heartbeat.
- **Key:** `ForDevice(...)`.
- **Ordering:** strict per device — this is a state machine; a stale transition would flip a device
  back to `Online` after it went offline.
- **Schema:** `EventEnvelope<DeviceHealthChanged>` — `DeviceLifecycleState` (see
  `Lifecycle.DeviceLifecycleState` / `LifecycleTransitions`), previous state, connectivity status,
  `BatteryVoltage`, firmware version, last-seen timestamp, reason.
- **Producer:** device-health monitor (consumes `telemetry.normalized` + gateway connection
  events).
- **Consumers:** fleet-health dashboard; alerting/paging; device-lifecycle admin; billing (active
  device counts).
- **Retention:** 30 days.
- **Replay:** rebuild the device-health read model and the "devices dark for > N hours" report.
- **DLQ:** `integration.deadletter`.

#### `trip.lifecycle`
- **Purpose:** trip start / stop / segment-close events derived from ignition and motion —
  the unit of work the business actually bills and reports on.
- **Key:** `ForDevice(...)` (**not** by trip id — a device has at most one open trip, and keying by
  device is what guarantees start and stop land on the same partition in order).
- **Ordering:** strict per device. A `TripEnded` overtaking its `TripStarted` would corrupt the
  trip table.
- **Schema:** `EventEnvelope<TripLifecycleEvent>` — trip id, phase
  (`Started` | `Updated` | `Ended`), `VehicleId`, driver id, start/end `GeoPoint`, distance km,
  duration, idle time.
- **Producer:** trip detector.
- **Consumers:** trip/jobs read model; driver-hours (HOS); billing/utilization; the dispatch
  board.
- **Retention:** **90 days** — trips are business records with a long correction/dispute window.
- **Replay:** rebuild trip tables end-to-end.
- **DLQ:** `integration.deadletter`.

#### `diagnostic.event`
- **Purpose:** DTCs and maintenance-relevant diagnostics (MIL on, DTC set/cleared, service
  interval reached).
- **Key:** `ForDevice(...)`.
- **Ordering:** per device — set/cleared pairs must not invert.
- **Schema:** `EventEnvelope<DiagnosticEvent>` — DTC codes (from
  `CanonicalTelemetryEvent.DtcCodes`), severity, set/cleared, `CoolantTempC`, `OdometerKm` at
  detection, freeze-frame signals.
- **Producer:** diagnostic engine.
- **Consumers:** maintenance module (work-order creation); fleet-health dashboard; warranty
  reporting.
- **Retention:** **90 days**.
- **Replay:** rebuild the open-fault list per vehicle.
- **DLQ:** `integration.deadletter`.

#### `safety.event`
- **Purpose:** harsh braking/acceleration/cornering, speeding, crash detection, panic/SOS. The
  **highest-urgency** topic — a crash event has a human on the other end of it.
- **Key:** `ForDevice(...)`.
- **Ordering:** per device.
- **Schema:** `EventEnvelope<SafetyEvent>` — event type, severity, `GeoPoint`, speed at event,
  g-force/threshold breached, `VehicleId`, driver id, media correlation id linking to
  `media.metadata`.
- **Producer:** safety/rules engine.
- **Consumers:** real-time alerting/paging; driver scorecard; incident/claims workflow;
  notification service.
- **Retention:** **365 days** — safety events are evidence in insurance and liability disputes.
- **Replay:** recompute driver scorecards after a scoring-model change.
- **DLQ:** `integration.deadletter`, but with **paging on non-empty DLQ** — a silently dropped
  crash event is unacceptable.

#### `media.metadata`
- **Purpose:** metadata for uploaded dashcam clips/photos: **pointers only, never bytes**. Media
  blobs go to object storage; the topic carries the reference. Keeps the log small and avoids
  putting video in a broker.
- **Key:** `ForDevice(...)`.
- **Ordering:** per device.
- **Schema:** `EventEnvelope<MediaMetadata>` — media id, object-storage URI, content type, size,
  duration, capture time, trigger (`SafetyEvent` correlation id), upload status, retention class.
- **Producer:** media-ingest service.
- **Consumers:** media library UI; incident workflow (attaches clips to a safety event);
  retention/GC enforcement.
- **Retention:** 30 days on the stream (the blob's own lifecycle is governed by object-storage
  policy, not by the topic).
- **Replay:** rebuild the media index.
- **DLQ:** `integration.deadletter`.

### Control lane — downlink command lifecycle

The four command topics form one state machine:
`requested → dispatched → acknowledged | failed`.
All four use **`ForCommand(tenant, company, deviceId)`** — keyed on the *target device*, not the
command id, so the entire lifecycle of every command aimed at a device is ordered on a single
partition and a device can never process an `EngineCutoff` out of order relative to its
`LocationRequest`.

#### `command.requested`
- **Purpose:** an operator or system has requested a downlink command; not yet sent to a device.
  **Written by the .NET control plane via the transactional outbox** (§4) — the request is only
  published if the authorization/audit row committed.
- **Ordering:** per target device.
- **Schema:** `EventEnvelope<CommandRequested>` — command id, `Adapters.DeviceCommand`
  (`Name` + `Arguments`), target `DeviceId` / `VehicleId`, requesting user id, reason, expiry.
- **Producer:** `Opstrax.Api` control plane (outbox relay).
- **Consumers:** the command dispatcher (encodes via `IProtocolAdapter.EncodeCommand` and pushes
  to the device's open gateway session); the audit log.
- **Retention:** 30 days.
- **Replay:** **audit-only — never blind-replay this topic.** Re-delivering an `EngineCutoff` from
  history would cut an engine on a truck that is now on a motorway. Replay consumers must run in
  a mode that projects to the audit/read model and does not dispatch.
- **DLQ:** `command.failed` for business failures (device offline, adapter cannot express the
  command, command expired); `integration.deadletter` for infra failures.

#### `command.dispatched`
- **Purpose:** the command was encoded to wire bytes and pushed toward the device/gateway.
- **Ordering:** per target device.
- **Schema:** `EventEnvelope<CommandDispatched>` — command id, encoded frame reference, gateway id,
  session id, dispatch timestamp, ack-timeout deadline.
- **Producer:** command dispatcher.
- **Consumers:** command status read model; the ack-timeout reaper (emits `command.failed` when the
  deadline passes with no ack).
- **Retention:** 30 days. **Replay:** audit-only. **DLQ:** `integration.deadletter`.

#### `command.acknowledged`
- **Purpose:** the device (or gateway) confirmed receipt/execution. Terminal success.
- **Ordering:** per target device.
- **Schema:** `EventEnvelope<CommandAcknowledged>` — command id, ack source (`Device` | `Gateway`),
  device response code, round-trip latency.
- **Producer:** gateway (on receipt of the protocol's ack frame).
- **Consumers:** command status read model; UI notification; audit log.
- **Retention:** 30 days. **Replay:** audit-only. **DLQ:** `integration.deadletter`.

#### `command.failed`
- **Purpose:** terminal failure — undispatchable, ack timeout, device rejection, expired, or
  unsupported by the adapter (`EncodeCommand` returned `null`).
- **Ordering:** per target device.
- **Schema:** `EventEnvelope<CommandFailed>` — command id, failure reason code, stage
  (`Authorize` | `Encode` | `Dispatch` | `Ack`), retry count, whether retryable, error detail.
- **Producer:** command dispatcher; ack-timeout reaper.
- **Consumers:** command status read model; operator alerting; retry policy engine.
- **Retention:** 30 days. **Replay:** audit-only. **DLQ:** terminal — this *is* the command
  failure sink.

### Cross-cutting

#### `integration.deadletter`
- **Purpose:** the single terminal sink for envelopes that exhausted retries on their home topic
  **for infrastructure reasons** (serialization failure, downstream store unavailable, unhandled
  consumer exception). Business rejections do **not** come here — telemetry rejections go to
  `telemetry.rejected` and command failures to `command.failed`. Keeping the two apart is what
  makes a non-empty DLQ mean "something is broken" rather than "a device sent junk".
- **Key:** the **original key** of the failed event, preserved verbatim. This keeps a device's
  dead-lettered events grouped, and means a redrive replays them back onto the source topic in
  their original order.
- **Ordering:** per original key.
- **Schema:** `EventEnvelope<DeadLetter>` — source topic, source key, source partition/offset,
  original envelope (serialized), failure reason, exception detail, retry count, first/last
  failure timestamps, the consumer group that gave up.
- **Producer:** every consumer, via the shared retry/DLQ policy (3 attempts, exponential backoff
  with jitter, then dead-letter).
- **Consumers:** the DLQ console; the **redrive** tool (replays back to `SourceTopic` under
  `SourceKey` once the underlying defect is fixed); alerting — **any** non-empty DLQ pages.
- **Retention:** **30 days**, log-compacted off — long enough that a weekend outage is still
  redrivable on Monday.
- **Replay:** redrive to the source topic. Because consumers are idempotent on `EventId`, a redrive
  of an event that actually did succeed is a no-op.
- **DLQ:** terminal. A failure while writing to the DLQ escalates to a local durable spool + page;
  it must never be swallowed.

---

## 4. The outbox pattern (.NET control plane)

The control plane (`Opstrax.Api`, `backend-dotnet/`) owns Postgres state — a command was
authorized, a device was suspended, a geofence changed — and must publish the corresponding
event. Doing both directly is a **dual-write**, and it is always wrong:

> Commit the row, then the broker publish fails ⇒ the DB says the engine cutoff was authorized,
> the fleet never got it. Publish first, then the transaction rolls back ⇒ a truck's engine is cut
> for a command that officially never existed.

There is no ordering of two independent writes that fixes this. The fix is to make the publish
part of the *same* transaction.

**The outbox table** lives in the control-plane database, in the same Postgres transaction as the
business write:

```sql
CREATE TABLE event_outbox (
    id             BIGSERIAL PRIMARY KEY,   -- monotonic; also the relay's ordering cursor
    event_id       UUID        NOT NULL UNIQUE,  -- becomes EventEnvelope.EventId
    correlation_id UUID        NOT NULL,
    causation_id   UUID        NULL,
    tenant_id      UUID        NOT NULL,
    company_id     BIGINT      NOT NULL,
    topic          TEXT        NOT NULL,   -- a TelematicsTopics constant
    partition_key  TEXT        NOT NULL,   -- from TelematicsEventKey
    schema_version INT         NOT NULL,
    payload        JSONB       NOT NULL,
    headers        JSONB       NOT NULL DEFAULT '{}',
    occurred_at    TIMESTAMPTZ NOT NULL,
    published_at   TIMESTAMPTZ NULL,       -- NULL ⇒ still pending
    attempts       INT         NOT NULL DEFAULT 0,
    last_error     TEXT        NULL
);

CREATE INDEX ix_event_outbox_pending
    ON event_outbox (id) WHERE published_at IS NULL;
```

**Flow:**

1. **Write side (transactional).** In one `BEGIN … COMMIT`: insert/update the business row *and*
   insert the outbox row. Either both land or neither does. The API returns success on commit —
   it does not wait for the broker.
2. **Relay (background).** A hosted service polls the partial index
   (`WHERE published_at IS NULL ORDER BY id`), claims a batch with
   `SELECT … FOR UPDATE SKIP LOCKED` (so multiple API replicas can relay concurrently without
   double-claiming), rehydrates each row into an `EventEnvelope<T>`, calls
   `IEventBackbone.PublishAsync(topic, partition_key, envelope, ct)`, and stamps `published_at`.
   On failure it increments `attempts` and leaves the row pending for the next sweep.
3. **Crash safety.** A crash between publish and stamping re-publishes on the next sweep. That is
   fine and expected: this is exactly the at-least-once guarantee the backbone already declares,
   and consumers already dedupe on `EventId` — which is why `event_id` is generated at *insert*
   time and carries a `UNIQUE` constraint, rather than being minted by the relay.
4. **Ordering.** The relay processes strictly by ascending `id` **within a partition key**, so
   per-key order is preserved from the database into the log. Rows with different keys may be
   published concurrently.
5. **Housekeeping.** Delete published rows older than 7 days. Alert when the oldest pending row
   exceeds ~60s (relay lag) or when any row's `attempts` crosses a threshold (poison outbox row →
   move to `integration.deadletter`).

The mirror image on the read side is the **inbox**: a consumer records `EventId` in a
`processed_events` table inside the same transaction that applies the event's effect, and skips
any `EventId` it has already seen. Outbox + inbox together turn at-least-once transport into
effectively-exactly-once processing — without distributed transactions.

**Long term:** the polling relay can be swapped for Debezium CDC on the outbox table's WAL,
removing the poll interval from the latency budget. The table shape above is already
CDC-compatible; nothing about the contract changes.

---

## 5. Brokers: Redpanda (local) / Kafka (prod)

`IEventBackbone` exists so that no producer or consumer ever names a broker. Three
implementations sit behind it:

| Environment | Implementation | Notes |
|---|---|---|
| Unit tests, deterministic demos | `InMemoryEventBackbone` | In-process. Reproduces per-key ordering and tenant isolation; **not durable**, no replay, no historical catch-up for late subscribers. |
| Local dev, integration tests, CI | **Redpanda** | Single Docker container, no ZooKeeper/KRaft controller to babysit, boots in seconds. Kafka **API-compatible**, so the same `Confluent.Kafka` client and the same topic/partition/key semantics as prod. |
| Staging / production | **Kafka** (managed) | Multi-broker, `replication.factor=3`, `min.insync.replicas=2`, `acks=all` — no acknowledged write is lost to a single broker failure. Tiered storage backs the long-retention topics (`safety.event`, `trip.lifecycle`). |

Because Redpanda speaks the Kafka wire protocol, the *only* difference between local and
production is the bootstrap-server string and the security config — not the client library, not
the partitioning, not the ordering semantics. That is deliberate: an ordering bug must be
reproducible on a laptop.

**Non-negotiables that must match across Redpanda and Kafka:**

- **Partition counts per topic** — they determine key→partition mapping. A topic with a different
  partition count locally will co-partition differently and hide ordering bugs.
- **Producer config:** `enable.idempotence=true`, `acks=all`, `max.in.flight.requests.per.connection=5`
  — idempotent producers preserve per-key order across retries; without idempotence, a retry can
  reorder a key's events.
- **Consumer config:** `enable.auto.commit=false`. Commit offsets **after** the effect is durably
  applied (see the inbox above), never before — auto-commit is how you silently lose events.
- **Serialization:** JSON + a schema registry today, with `SchemaVersion` on the envelope as the
  compatibility gate. Compatibility mode is **BACKWARD**: add optional fields, never remove or
  retype a field; a breaking change bumps `SchemaVersion` and, where consumers cannot be moved in
  lockstep, gets a new topic rather than a mutated one.
- **Topic provisioning:** created from `TelematicsTopics.All` by the provisioning tool, so a new
  topic constant cannot be shipped without the topic existing.
