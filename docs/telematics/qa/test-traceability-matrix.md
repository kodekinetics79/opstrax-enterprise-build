# Telematics — Test Traceability Matrix

Owner: QA Architect & Test Automation Manager
Scope: the `telematics/` ingestion fabric (Contracts, GT06 protocol adapter, Gateway host,
protocol + integration test projects) and its intended downstream landing in the `.NET`
`Opstrax.Api` ingest endpoints.

## How to read this document

This maps the **13-layer telematics test strategy** to **concrete, named tests** and gives
each an **honest** status. A layer is only `done` when a real, runnable, asserting test
exists in the tree today and is exercised by `dotnet test`.

| Status | Meaning |
|---|---|
| `done` | Concrete asserting test(s) exist in the tree now and run in CI on `dotnet test`. |
| `partial` | Some real asserting coverage exists; named gaps remain (listed inline). |
| `design-only` | A contract/type/schema exists, but **no runtime code sets it and no test asserts it**. Nothing passes today for this behavior. |
| `blocked-external` | Cannot be completed in-repo without an external dependency (a running Postgres/`Opstrax.Api`, a vendor-cloud account, real hardware/SIM, or a load rig). |

### Ground truth (what actually exists today)

The telematics solution (`telematics/Opstrax.Telematics.sln`) compiles with **5 projects**
(`Contracts`, `Protocols.Gt06`, `Gateway`, `Protocols.Tests`, `IntegrationTests`) under
`net8.0` / `nullable enable`, and runs **38 asserting tests**, all passing:

| Test class | Project | Count |
|---|---|---|
| `Gt06AdapterTests` | `Protocols.Tests` | 30 |
| `LifecycleTransitionsSmokeTests` | `Protocols.Tests` | 2 |
| `GatewaySmokeTests` | `IntegrationTests` | 1 |
| `GatewayTcpSliceTests` | `IntegrationTests` | 5 |
| **Total** | | **38** |

The **6 tests in `IntegrationTests` are real socket integration tests**: five of them
(`GatewayTcpSliceTests`) boot a real `TcpListener` on an ephemeral loopback port and drive it
with a real `TcpClient`, sending byte-accurate GT06 frames.

**The GT06 adapter is fully implemented**, not a placeholder. `Gt06Adapter` frames both
`0x7878` (1-byte length) and `0x7979` (2-byte length) markers, validates CRC-16/X.25
(CRC-ITU), decodes login/location/heartbeat/status/alarm/time/ack, unpacks the BCD IMEI,
builds server acks (`EncodeAck`), encodes a `0x80` downlink (`EncodeCommand`), and maps a fix
to a `CanonicalTelemetryEvent` (`ToCanonicalEvent`). **The gateway is a working
`BackgroundService`** with a bound listener, per-connection isolation, a connection quota,
idle-timeout reaping, malformed-stream fail-close, bounded-channel backpressure,
store-and-forward on publish failure, and fail-closed registry identity resolution.

**Fixtures are committed** under `telematics/fixtures/gt06/` as **19 `.hex` files + a
`README.md`** (see `fixture-catalog.md`). The scheme is one whitespace-free hex string per
`.hex` file — **not** the `.bin` + `.json`-manifest scheme an earlier draft of this document
described.

**What is still absent:** there is no anomaly/quality-scoring stage (the `QualityFlags` and
`TrustScore` fields are contract-only and nothing populates them), no golden-file
replay-regression corpus, and no wiring into `Opstrax.Api` + Postgres — so end-to-end DB
landing and the HTTP gps-ingest security gates remain untested here.

---

## The 13 layers

### Layer 1 — Static analysis & compile gate
Compile-clean build of the whole solution with nullable reference types on; project references
resolve end-to-end (Contracts → Gt06 → Gateway → both test projects).

| Concrete test / check | Artifact | Status |
|---|---|---|
| `dotnet build telematics/Opstrax.Telematics.sln` builds all 5 projects, nullable on | solution + all `.csproj` | done |
| Cross-assembly references link (tests reference Contracts/Gt06/Gateway) | all test classes | done |
| Analyzer ruleset / `.editorconfig` severity gate + `-warnaserror` wired into a CI workflow | (not present) | design-only |

### Layer 2 — Contract invariant unit tests
Canonical types enforce their own invariants: required ownership/timing/provenance fields,
schema version, quality-flag algebra, `ProtocolMatch` / `SignalValue` clamping.

| Concrete test / check | Artifact | Status |
|---|---|---|
| Canonical event carries current schema version + provenance; fresh event is `Quality.IsClean` | `GatewaySmokeTests.Canonical_event_carries_current_schema_version_and_provenance` | done |
| CRC-16/X.25 self-check against the standard `0x906E` reference value | `Gt06AdapterTests.Crc16Itu_matches_the_canonical_x25_check_value` | done |
| `TryIdentify` matches both start markers, rejects others, sets `NeedMoreData` on 1 byte | `Gt06AdapterTests.TryIdentify_matches_both_start_markers_and_rejects_others` | done |
| Required-field omission on `CanonicalTelemetryEvent` fails to compile (enforced by `required` init) | `CanonicalTelemetryEvent` | partial (compiler-enforced; no compile-fail fixture test) |
| `QualityFlags.IsClean` true iff every flag clear; each flag independently flips it | `QualityFlags` | design-only |
| `ProtocolMatch` clamps confidence to [0,1] / forces 0 on non-match; `SignalValue` clamps + treats null as absent | `ProtocolMatch`, `SignalValue` | design-only |

### Layer 3 — Protocol decode conformance (vector-based)
Given known-good GT06/Concox frames, the adapter frames and decodes login / heartbeat /
location / alarm / status / time correctly, validates CRC-ITU, reports `consumed`, and builds
correct acks. **This is the bulk of the 30 `Gt06AdapterTests`.**

| Concrete test / check | Artifact | Status |
|---|---|---|
| `0x7878` login → `MessageType.Login`, IMEI `868120303337976`, `RequiresAck`, serial | `Decodes_valid_login_with_imei_and_requires_ack` | done |
| `EncodeAck(login)` == exact `login_ack.hex` server bytes | `EncodeAck_for_login_matches_the_expected_server_response` | done |
| `0x12` location → time / sats / lat / lng / speed / course / status bits | `Decodes_valid_location_0x12_fields` | done |
| `0x22` location under the `0x7979` 2-byte-length framing | `Decodes_location_0x22_under_the_two_byte_length_7979_framing` | done |
| South / East hemispheres decoded from the status word | `Decodes_south_and_east_hemispheres_from_the_status_word` | done |
| `0x13` heartbeat status + `RequiresAck`; `EncodeAck` matches `heartbeat_ack.hex` | `Decodes_heartbeat_0x13_...`, `EncodeAck_for_heartbeat_...` | done |
| `0x23` status, `0x16` SOS alarm (with fix), `0x8A` time-sync, `0x99` unknown (raw retained) | `Decodes_status_0x23_...`, `Decodes_sos_alarm_0x16_...`, `Decodes_time_0x8A_...`, `Unknown_protocol_number_...` | done |
| Adapter advertises name/version `GT06` / `1.0.0` | `Metadata_publishes_gt06_name_and_version` | done |

### Layer 4 — Protocol robustness / malformed-frame
Malformed, truncated, and adversarial buffers fail closed: `ProtocolException` on impossible
framing, partial-frame handling (`consumed == 0`), bad-CRC skip without fabricating an event,
and a bad frame between good frames does not lose the good ones.

| Concrete test / check | Artifact | Status |
|---|---|---|
| Impossible length header throws `ProtocolException` (`AdapterName == "GT06"`) | `Malformed_length_throws_ProtocolException_and_does_not_crash` | done |
| Bad-CRC frame rejected without throwing, emits no message, buffer drained | `Bad_crc_frame_is_rejected_without_throwing_and_emits_no_message` | done |
| Truncated frame → `consumed == 0`, no throw; completes when the rest arrives | `Truncated_frame_reports_needs_more_...`, `Truncated_frame_completes_once_...` | done |
| Garbage with no start marker → `ProtocolException` (never a crash) | `A_hostile_garbage_buffer_never_throws_anything_but_ProtocolException` | done |
| Bad-CRC frame between two good frames is skipped without losing the others | `A_bad_crc_frame_between_good_frames_is_skipped_...` | done |
| Randomized/generative fuzz corpus asserting no unhandled exception over N iterations | (not present) | design-only |

### Layer 5 — Identity resolution (fail-closed)
`IDeviceRegistry.ResolveAsync` returns the registry owner only, never packet-asserted
ownership; returns `null` (→ reject) on unknown identity; the gateway binds tenant only from
the resolved owner; Suspended/Retired owners are barred at ingest.

| Concrete test / check | Artifact | Status |
|---|---|---|
| IMEI is a claim/index only — ownership on the canonical event is the registry's, not the packet's | `Imei_is_not_trusted_as_tenant_ownership_comes_only_from_the_registry` | done |
| Well-formed CRC-valid login for an unprovisioned IMEI → rejected, **unbound** (tenant `Guid.Empty`), not acked | `GatewayTcpSliceTests.Login_from_an_unknown_imei_is_rejected_...` | done |
| `InMemoryDeviceRegistry.ResolveAsync` returns `null` for any unseeded identity (fail closed) | `InMemoryDeviceRegistry` | done (exercised via the unknown-IMEI socket test) |
| Suspended / Retired resolved owner barred at ingest (`GatewayConnection.IsIngestAllowed`) | `DeviceLifecycleState`, `GatewayConnection` | partial (enforced in code; no dedicated suspended/retired test) |
| Ambiguous/duplicate IMEI → `null` (no auto-pick) | `IDeviceRegistry` | design-only (registry keys by identifier; no dup-IMEI test fixture) |

### Layer 6 — Normalization (adapter fields → canonical + VSS)
Adapter-local `DecodedMessage.Fields` map into canonical typed fields; raw frame preserved.

| Concrete test / check | Artifact | Status |
|---|---|---|
| `ToCanonicalEvent` maps the `0x12` fix into `GeoPoint` (lat/lng/sats/speed/heading) + device time | `Gt06AdapterTests.ToCanonicalEvent_stamps_gt06_provenance_and_maps_the_fix` | done |
| Provenance stamped: `DirectDevice` / `Tcp` / `GT06` / adapter name+version / schema version | same test + `GatewayTcpSliceTests.Login_then_location_...` | done |
| `DecodedMessage.RawFrame` retained verbatim (unknown-protocol frame round-trips byte-identical) | `Unknown_protocol_number_yields_Unknown_and_retains_raw_frame` | done |
| Odometer/coolant/fuel/battery → `VssSignals.*` dotted keys with correct units | `VssSignals`, `CanonicalTelemetryEvent.Signals` | design-only (GT06 fix maps geometry; the signal bag is not populated/tested) |

### Layer 7 — Quality / anomaly detection
Normalization should raise the `QualityFlags` (duplicate, out-of-order, replay, stale,
clock-skew, teleport, impossible-speed, GPS-jamming) and derive `TrustScore`.

| Concrete test / check | Artifact | Status |
|---|---|---|
| Default (all-clear) `QualityFlags.IsClean` is true on a fresh event | `GatewaySmokeTests` (incidental) | done |
| Decoder faithfully reports a **reused serial** so a downstream stage can detect a duplicate | `Duplicate_serial_is_reported_faithfully_for_downstream_idempotency` | done (decode fidelity only — no dedup) |
| Decoder faithfully reports an **earlier device time on a higher serial** (out-of-order material) | `Out_of_order_frame_reports_its_own_earlier_device_time_and_higher_serial` | done (decode fidelity only) |
| Out-of-range coordinates surfaced verbatim + `coordinatesValid=false`; extreme speed not clamped | `Invalid_coordinates_are_surfaced_verbatim_...`, `Extreme_speed_is_decoded_verbatim_...` | done (decode fidelity only) |
| A quality/anomaly stage that actually **sets** `IsDuplicate`/`IsReplay`/`IsStale`/… and depresses `TrustScore` | (no such stage) | design-only |

> The decoder deliberately **does not** dedupe, reorder, or score — it reports faithfully so a
> downstream stage can. That stage does not exist yet, so every *flag-raising* assertion is
> `design-only`. The fixtures that make it testable (`duplicate_serial.hex`, `out_of_order.hex`,
> `invalid_coordinates.hex`, `extreme_speed.hex`) are already committed.

### Layer 8 — Lifecycle state machine
`LifecycleTransitions` is the single allowed-edge authority: no direct provisioning→`Online`,
`Retired` terminal, self-transition rejected unless `allowSelf`, `Assert` throws on illegal edges.

| Concrete test / check | Artifact | Status |
|---|---|---|
| `Provisioned` never transitions directly to `Online` | `LifecycleTransitionsSmokeTests.Provisioned_never_transitions_directly_to_online` | done |
| `Gt06Adapter.ProtocolName` constant is `GT06` (cross-project link smoke) | `LifecycleTransitionsSmokeTests.Gt06_adapter_declares_protocol_name` | done |
| Full allowed-edge sweep, `Retired`-is-terminal, self-transition rule, `Assert` throw path | `LifecycleTransitions` (map includes `Quarantined` edges) | partial (1 edge asserted; exhaustive sweep not written) |

### Layer 9 — Command / downlink encoding
`IProtocolAdapter.EncodeCommand` turns a `DeviceCommand` into wire bytes and returns **null**
for commands it cannot express.

| Concrete test / check | Artifact | Status |
|---|---|---|
| Missing command text → `null` (never an approximation) | `EncodeCommand_returns_null_when_no_command_text_is_supplied` | done |
| Given command text → valid `0x80` frame that round-trips through the decoder with a good CRC | `EncodeCommand_builds_a_valid_0x80_frame_when_given_command_text` | done |
| `EncodeAck` returns empty for a frame needing no ack | `EncodeAck_returns_empty_for_a_frame_that_needs_no_ack` | done |
| Typed high-level commands (`EngineCutoff`, `SetInterval`) → specific documented wire bytes | `DeviceCommand`, GT06 adapter | design-only (only the generic text/`0x80` path is implemented) |

### Layer 10 — Gateway integration (host + framing loop)
Host binds a TCP listener, isolates each connection, reassembles frames across reads, acks the
device with exact bytes, publishes canonical events, sheds load past the quota, and survives a
hostile flood. **This is the Increment-2 headline and is genuinely covered by real sockets.**

| Concrete test / check | Artifact | Status |
|---|---|---|
| Login over a real socket returns the exact `login_ack.hex` bytes; a location publishes a canonical event with the registry's ownership | `GatewayTcpSliceTests.Login_then_location_returns_the_exact_ack_bytes_and_publishes_a_canonical_event` | done |
| Frames split across TCP segments (login torn in half; location dribbled 1 byte/segment) reassemble and decode | `Frames_split_across_tcp_segments_are_reassembled_and_decoded` | done |
| A 24-connection malformed flood never takes the listener down; a healthy connection beside it still decodes; `MalformedConnectionsDropped > 0`; no event fabricated | `A_malformed_flood_never_takes_the_gateway_down_...` | done |
| Connections beyond `MaxConnections` are shed (`ConnectionsRejectedQuota`), not queued | `Connections_beyond_the_quota_are_shed_rather_than_queued` | done |
| Idle connection past `IdleTimeout` is reaped (`IdleConnectionsClosed`) | `GatewayConnection.ReadWithIdleTimeoutAsync` | partial (code + counter exist; no dedicated idle-timeout test) |

### Layer 11 — End-to-end pipeline (packet → canonical → store)
Packet → adapter → identity → normalize → backbone, and ultimately into
`location_events` / `latest_vehicle_positions` via `Opstrax.Api`.

| Concrete test / check | Artifact | Status |
|---|---|---|
| Packet → adapter → registry → canonical event on the in-memory backbone, keyed by device, tenant-scoped envelope | `GatewayTcpSliceTests.Login_then_location_...` (full slice) | done (in-fabric, `InMemoryEventBackbone`) |
| Store-and-forward parks an event when the backbone publish fails, instead of dropping it | `InMemoryStoreAndForwardBuffer`, `GatewayConnection.PublishPumpAsync` | partial (code exists; no test forces a publish failure) |
| Decoded fix lands in `location_events` / `latest_vehicle_positions` via `POST /api/telemetry/gps-ingest` | `Opstrax.Api` (`EndpointMappings.cs`), Neon Postgres | blocked-external |
| SSE `/api/telemetry/stream` emits the ingested fix to a subscriber | `Opstrax.Api` | blocked-external |

### Layer 12 — Multi-tenant isolation & ingest security
Ownership from the registry (not the packet); rejections are tenant-unbound; no cross-tenant
leak; and — on the HTTP path — gateway-signature auth + replay defense.

| Concrete test / check | Artifact | Status |
|---|---|---|
| Canonical event + envelope tenant/company come from the registry, never the frame's IMEI | `GatewayTcpSliceTests.Login_then_location_...` (asserts `envelope.TenantId == registry`) | done |
| Rejection lane is **unbound** (tenant `Guid.Empty`, company `0`); masked IMEI never carries the raw value | `Login_from_an_unknown_imei_is_rejected_and_is_never_bound_to_a_tenant` | done |
| Fix for a device in tenant A never appears in tenant B reads (scoped by `company_id`) | `Opstrax.Api` reads + DB | blocked-external |
| HTTP gateway HMAC signature / freshness / replay-window on `gps-ingest` | `Opstrax.Api` `GpsTrackerIngest` | design-only in this fabric (the TCP gateway is raw GT06 with **no** crypto auth — see the security docs); tested only against the API, which is `blocked-external` |

> Honest note: the raw GT06 TCP gateway has **no cryptographic device authentication** — the
> IMEI is a spoofable index, and trust comes from provisioning + allowlist + fail-closed
> resolution, not proof. See `../security/threat-model.md` and
> `../security/identity-trust-architecture.md`.

### Layer 13 — Non-functional: performance, replay-regression, dual-feed, chaos

| Concrete test / check | Artifact | Status |
|---|---|---|
| Concurrent-hostile-connection resilience (24 simultaneous malformed peers, listener survives) | `A_malformed_flood_never_takes_the_gateway_down_...` | done (resilience smoke — **not** a perf SLO) |
| Replay-corpus regression: committed corpus yields byte-identical canonical goldens | golden corpus (not present); adapter still uses internal `Guid.NewGuid()`/`UtcNow` for `EventId`/`NormalizedAtUtc` | design-only |
| Sustained ingest throughput / soak SLO | load rig (not provisioned) | blocked-external |
| Dual-feed reconciliation (`DirectDevice` vs `VendorCloud` for one vehicle) | vendor-cloud feed | blocked-external |
| PT40/hardware field-commissioning: one valid signed packet end-to-end | real device + SIM/APN | blocked-external |

---

## Roll-up

| Layer | Title | Status |
|---|---|---|
| 1 | Static analysis & compile gate | done (CI analyzer/`-warnaserror` wiring still design-only) |
| 2 | Contract invariant unit tests | partial (schema/CRC/identify asserted; flag algebra + clamping design-only) |
| 3 | Protocol decode conformance | done |
| 4 | Protocol robustness / malformed | done (generative fuzz still design-only) |
| 5 | Identity resolution (fail-closed) | done (unknown-device path); partial (suspended/retired/dup-IMEI) |
| 6 | Normalization → canonical/VSS | partial (geometry + provenance done; VSS signal bag design-only) |
| 7 | Quality / anomaly detection | design-only (decode fidelity done; no flag-raising stage) |
| 8 | Lifecycle state machine | partial (1 edge asserted) |
| 9 | Command / downlink encoding | done (generic `0x80`); typed high-level commands design-only |
| 10 | Gateway integration | done |
| 11 | End-to-end pipeline | done in-fabric; DB/API landing blocked-external |
| 12 | Multi-tenant isolation & security | done (fabric ownership/rejection); DB leak blocked-external; HTTP crypto auth design-only |
| 13 | Non-functional | resilience smoke done; replay-regression design-only; perf/dual-feed/hardware blocked-external |

**Honest headline:** Increment-2 delivers a fully-implemented GT06 decoder and a working,
attack-resilient TCP gateway, covered by **38 passing tests including 6 real socket integration
tests**. Layers 3, 4, 9, 10 are genuinely done; 1, 11, 12 are done for the parts that do not
need Postgres/hardware. What is **not** built and therefore honestly design-only: the
quality/anomaly-scoring stage (Layer 7), the VSS signal-bag normalization (Layer 6), the
golden replay-regression corpus (Layer 13), and any cryptographic device auth on ingest. The DB
landing, cross-tenant leak proof, perf/soak, dual-feed, and hardware commissioning remain
blocked-external. Do not quote a layer above its status here.
