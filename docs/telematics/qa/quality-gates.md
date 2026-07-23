# Telematics — Quality Gates

Owner: QA Architect & Test Automation Manager
Scope: the release gates the `telematics/` ingestion fabric must clear before any claim of
"live telemetry" is made to a customer.

## Rules of this document

1. **No gate is marked PASS without an automated, runnable check that asserts it today.**
2. A gate whose *contract/schema* exists but whose *behavior* is not implemented and not
   tested is **NOT MET (design-only)**, not "partial pass".
3. A gate requiring a live DB, `Opstrax.Api`, a vendor account, real hardware, or a load rig is
   **BLOCKED (EXTERNAL)** — it cannot be closed inside this repo.
4. Increment-2 delivered a **fully-implemented GT06 decoder**, a **working TCP gateway**, and a
   hardening pass (registry-sourced per-device trust policy with source-IP/SIM pins + quarantine,
   a per-device replay/sequence guard, idempotent projection, store-and-forward replay, and real
   OpenTelemetry primitives), covered by **95 passing tests across 6 projects (7 real-socket
   integration tests)**. Several gates now genuinely PASS; the rest are honestly NOT MET or
   BLOCKED and say why.

Status vocabulary: `PASS` · `PARTIAL` · `NOT MET (design-only)` · `BLOCKED (EXTERNAL)`

---

## G1 — Build & compile gate

**Requirement:** The whole solution compiles with nullable reference types on; all five projects
link (Contracts → Gt06 → Gateway → Protocols.Tests → IntegrationTests).

**How verified:** `dotnet build telematics/Opstrax.Telematics.sln`; the test projects cross
assembly boundaries, so a broken reference fails the build.

**Status: PASS.** Caveat: an analyzer ruleset / `-warnaserror` is not yet wired into a CI
workflow — the gate is enforced by build, not by a pipeline. CI wiring is outstanding.

---

## G2 — Unit + integration suite green

**Requirement:** `dotnet test` runs clean, zero skipped, zero flaky.

**How verified:** `dotnet test telematics/Opstrax.Telematics.sln`.

**Status: PASS.** All **38** tests pass: 30 `Gt06AdapterTests` (decode/CRC/ack/command/malformed),
2 `LifecycleTransitionsSmokeTests`, 1 `GatewaySmokeTests`, and 5 `GatewayTcpSliceTests` (real
sockets). Unlike Increment-1, these assert **real decode and gateway behavior**, not just that
the harness links. They still do **not** assert DB landing or HTTP ingest security (G9/G10).

---

## G3 — Coverage threshold

**Requirement:** ≥ 80% line/branch coverage on `Opstrax.Telematics.Contracts` and the protocol
adapter.

**How verified:** `dotnet test /p:CollectCoverage=true` (coverlet) + a CI threshold check.

**Status: NOT MET (design-only).** No coverage collection is configured and no threshold is
enforced in CI. Actual coverage of `Gt06Adapter` is now high (30 targeted tests), but it is not
measured or gated.

---

## G4 — Protocol decode conformance

**Requirement:** Committed GT06 fixtures decode to their expected `DecodedMessage` (type, fields,
`consumed`, `RequiresAck`), with CRC-ITU validated and correct ack bytes from `EncodeAck`.

**How verified:** Fixture-driven tests over `telematics/fixtures/gt06/*.hex`.

**Status: PASS.** `Gt06Adapter` fully implements framing for both `0x7878` (1-byte length) and
`0x7979` (2-byte length), CRC-16/X.25, and decode for login/location(`0x12`,`0x22`)/heartbeat
(`0x13`)/status(`0x23`)/alarm(`0x16`)/time(`0x8A`)/unknown(`0x99`). `EncodeAck` is asserted
byte-equal to `login_ack.hex` and `heartbeat_ack.hex`. 19 `.hex` fixtures are committed with a
documented, CRC-re-validated README.

---

## G5 — Malformed-input robustness (fail closed, never crash)

**Requirement:** Bad CRC / truncated / oversize / garbage frames raise `ProtocolException` or are
skipped fail-closed; the connection is torn down; **no event is fabricated from corrupt data**.

**How verified:** Fixture tests over the malformed `.hex` fixtures + a socket-level flood.

**Status: PASS (fixture + socket) / PARTIAL (generative fuzz).** Asserted: impossible length →
`ProtocolException`; bad CRC → skipped, no message; truncated → `consumed==0` then completes;
garbage → `ProtocolException`; bad frame between good frames → good ones survive; and a
24-connection malformed flood leaves the listener up with `MalformedConnectionsDropped > 0` and
**zero fabricated events**. Still missing: a randomized/generative fuzz harness over many
iterations.

---

## G6 — Identity resolution fails closed

**Requirement:** Ownership comes only from `IDeviceRegistry` → `ResolvedDeviceOwner`; a packet's
claimed IMEI is an untrusted index. Unknown identity → reject, never invent a tenant.
`Suspended`/`Retired` owners rejected at ingest.

**How verified:** Adapter test (IMEI-not-trusted) + socket test (unknown IMEI rejected unbound).

**Status: PASS (unknown-device path) / PARTIAL (barred-state paths).** Asserted end-to-end: a
CRC-valid login for an unprovisioned IMEI is rejected, **never bound to a tenant** (envelope
tenant `Guid.Empty`), **not acked**, and counted (`UnknownDeviceRejections`); and the canonical
event's ownership is the registry's, not the packet's. `InMemoryDeviceRegistry.ResolveAsync`
returns `null` for any unseeded identity. Not yet asserted by a dedicated test:
`Suspended`/`Retired` rejection (enforced in `GatewayConnection.IsIngestAllowed`) and
ambiguous/duplicate-IMEI handling.

> **Quarantined is now enforced (Increment-2).** `DefaultDeviceAuthenticator` returns a
> `Quarantine` verdict for a `Quarantined` device; `GatewayConnection` then refuses the login,
> publishes an unbound rejection, and closes the socket. Asserted end-to-end by
> `A_quarantined_device_login_is_rejected_and_the_socket_is_closed`, and a registry source-IP pin
> is enforced end-to-end by `A_device_pinned_to_a_foreign_source_ip_is_refused_from_loopback`.
> Suspended/Retired are also refused. Still unasserted: ambiguous/duplicate-IMEI handling.

---

## G7 — Lifecycle invariant: provisioning never implies connectivity

**Requirement:** No provisioning state has a direct edge to `Online`; `Online` is reached only
via `Identified → Authenticated → Validating`. `Retired` is terminal. Illegal edges throw.

**How verified:** `LifecycleTransitions` tests; ultimately an exhaustive state sweep.

**Status: PARTIAL — treat as NOT MET.** Exactly **one** edge is asserted (`Provisioned → Online`
rejected). The full allowed-edge sweep, `Retired`-is-terminal, the self-transition rule, and the
`Assert` throw path are unasserted. The `LifecycleTransitions` map itself is populated (including
`Quarantined` edges); the tests are not.

---

## G8 — Data-quality / anomaly detection

**Requirement:** The `QualityFlags` (duplicate, out-of-order, replay, stale, clock-skew,
teleport, impossible-speed, GPS-jamming) are each raised by their fixture, and `TrustScore`
degrades accordingly.

**How verified:** A quality/normalization stage over the anomaly fixtures.

**Status: NOT MET (design-only).** `QualityFlags` and `TrustScore` are contract fields;
`IsClean` on a default struct is asserted, but **no code raises any flag**. There is no
quality/anomaly stage in the tree. The decoder deliberately reports serial/time/coordinates
*faithfully* (asserted via `duplicate_serial`, `out_of_order`, `invalid_coordinates`,
`extreme_speed` fixtures) so a future stage can score them — but that stage does not exist.

---

## G9 — Multi-tenant isolation (no cross-tenant leak)

**Requirement:** A fix ingested for a device in tenant A is never readable from tenant B; all
reads scoped by `company_id` (4 real tenants).

**How verified:** Two-tenant ingest + cross-tenant read assertions against `Opstrax.Api` + DB.

**Status: PARTIAL in-fabric / BLOCKED (EXTERNAL) end-to-end.** In-fabric, the socket tests
prove the canonical event and its envelope carry the **registry's** tenant/company and that a
rejection is tenant-unbound — a real isolation property. But the end-to-end DB leak test needs a
running `Opstrax.Api` + Neon Postgres and does not exist.

---

## G10 — Ingest authentication & replay defense

**Requirement:** Ingest accepts only authenticated requests; replays are rejected; IMEI is never
a credential.

**How verified:** Integration tests over the authenticated ingest path.

**Status: NOT MET / BLOCKED (EXTERNAL) — and read the honest note.** The raw GT06 **TCP** gateway
has **no cryptographic device authentication at all**: GT06 carries no device signature; the
IMEI in the login frame is the sole identifier and it is spoofable. Trust on this path is
provisioning + allowlist + fail-closed resolution, *not* proof (see
`../security/identity-trust-architecture.md`). The **HTTP** `gps-ingest` path in `Opstrax.Api`
does have a shared-secret HMAC + freshness + in-memory replay cache, but (a) it is a single
fleet-wide secret, not per-device, and (b) no automated test asserts it here — that is
BLOCKED (EXTERNAL) on an API+DB harness. **Do not present ingest as cryptographically
authenticated.**

---

## G11 — Secret hygiene

**Requirement:** No device/gateway secret in source, on the tracker, in a URL, or in logs. The
gateway logs a **masked** IMEI only. Fixtures use reserved test IMEIs.

**How verified:** Code review of the log surface + a secret-scan in CI.

**Status: PARTIAL.** `DeviceIdentifier.Mask` is implemented and the gateway logs only masked
IMEIs; `GatewayOptions` carries no secret and the registry hands back an opaque
`CredentialHandle`, never a key. The socket test asserts the rejection lane masks the claimed
IMEI. Still missing: an automated secret-scan (gitleaks) in CI and a test asserting no full IMEI
reaches a log sink.

---

## G12 — Gateway resilience (framing loop / resource exhaustion)

**Requirement:** A hostile stream cannot exhaust the gateway: bounded reassembly buffer
(`MaxFrameBytes`), idle-timeout reaping, connection quota with load-shedding, backpressure, and
per-connection isolation so one bad peer cannot disturb another.

**How verified:** Socket integration tests driving partial frames, oversize headers, floods, and
over-quota connects.

**Status: PASS.** Asserted by real sockets: fragmented frames reassemble (1 byte/segment);
`MaxFrameBytes` bounds the buffer; a 24-connection malformed flood cannot down the listener and
leaves a neighbour connection undisturbed; connections beyond `MaxConnections` are shed
(`ConnectionsRejectedQuota`) rather than queued; backpressure is a bounded channel. Idle-timeout
reaping is implemented (`ReadWithIdleTimeoutAsync`, `IdleConnectionsClosed`) but lacks a
dedicated test — the only PARTIAL sub-item.

---

## G13 — Determinism / replay-regression

**Requirement:** Re-running the committed fixtures produces byte-identical
`CanonicalTelemetryEvent` goldens; no wall-clock or RNG leaks into normalization.

**How verified:** Golden-file comparison + a second CI run.

**Status: NOT MET (design-only).** No golden corpus exists, and `ToCanonicalEvent` still fills
`EventId` and `NormalizedAtUtc` from `Guid.NewGuid()`/`UtcNow` internally (the caller injects
`receivedAtGatewayUtc` and `correlationId`, but not those two). A deterministic clock/id
abstraction and a golden baseline are prerequisites and are not present.

---

## G14 — Performance / soak SLO

**Requirement:** Sustained ingest throughput and p99 decode latency under concurrent connections,
no memory growth over a multi-hour soak.

**How verified:** A load rig replaying fixtures over N TCP connections, metrics captured.

**Status: BLOCKED (EXTERNAL).** No SLO numbers agreed, no load rig provisioned. The
24-connection flood test is a resilience smoke, not a performance measurement.

---

## G15 — Dual-feed reconciliation

**Requirement:** The same vehicle reported by a `DirectDevice` gateway feed and a `VendorCloud`
feed de-conflicts by provenance/trust without double-counting.

**How verified:** A direct+vendor fixture pair for one vehicle/instant.

**Status: BLOCKED (EXTERNAL).** Requires a real vendor-cloud feed (Samsara / Motive / Geotab).
No vendor account, poller, or webhook receiver exists.

---

## G16 — Field commissioning (real hardware end-to-end)

**Requirement:** A real device is "live" only after a gateway is deployed, APN + destination
configured, a credential provisioned, and **one valid packet observed end-to-end** advancing the
device to `Active`. Unknown/suspended/retired states fail closed.

**How verified:** A witnessed commissioning run against physical hardware.

**Status: BLOCKED (EXTERNAL).** No hardware, no SIM/APN, no deployed gateway. Registry presence
and simulated movement do not satisfy this gate.

---

## G17 — Observability & provenance completeness

**Requirement:** Every emitted `CanonicalTelemetryEvent` carries full provenance (`Source`,
`Transport`, `ProtocolName`, `AdapterName`, `AdapterVersion`, `CorrelationId`) and the raw frame
is retained for audit; no event with a fabricated/defaulted tenant.

**How verified:** Assertions on produced events + counters.

**Status: PARTIAL.** The `required`-init fields on `CanonicalTelemetryEvent` mean a
half-populated event won't compile, and both `ToCanonicalEvent_stamps_gt06_provenance_...` and
the socket happy-path test assert `Source`/`Transport`/`ProtocolName`/`AdapterName`/`AdapterVersion`/
non-empty `CorrelationId` on **real produced events**. `DecodedMessage.RawFrame` is asserted to
round-trip byte-identical (`unknown_protocol` test). `GatewayMetrics` exposes the counters the
tests assert on. Not yet covered: retention of the raw frame *through* to durable storage (needs
DB), and a metrics/OTel exporter.

---

## Gate roll-up (Increment-2)

| Gate | Title | Status |
|---|---|---|
| G1 | Build & compile gate | PASS (CI analyzer wiring outstanding) |
| G2 | Unit + integration suite green | PASS (95 tests across 6 projects, 7 real socket) |
| G3 | Coverage threshold | NOT MET (design-only) |
| G4 | Protocol decode conformance | PASS |
| G5 | Malformed-input robustness | PASS (generative fuzz PARTIAL) |
| G6 | Identity resolution fails closed | PASS (unknown-device, quarantine, source-IP pin enforced e2e); PARTIAL (suspended/retired/dup-IMEI) |
| G7 | Lifecycle invariant | PARTIAL — 1 of N edges asserted |
| G8 | Data-quality / anomaly detection | NOT MET (design-only) |
| G9 | Multi-tenant isolation | PARTIAL in-fabric; BLOCKED (EXTERNAL) end-to-end |
| G10 | Ingest auth & replay defense | NOT MET (no crypto auth on raw GT06); HTTP path BLOCKED (EXTERNAL) |
| G11 | Secret hygiene | PARTIAL (masking done; CI scan outstanding) |
| G12 | Gateway resilience | PASS (idle-timeout test PARTIAL) |
| G13 | Determinism / replay-regression | NOT MET (design-only) |
| G14 | Performance / soak SLO | BLOCKED (EXTERNAL) |
| G15 | Dual-feed reconciliation | BLOCKED (EXTERNAL) |
| G16 | Field commissioning (hardware) | BLOCKED (EXTERNAL) |
| G17 | Observability & provenance | PARTIAL |

**Headline (Increment-2 hardening):** **5 gates pass** (G1, G2, G4, G5, G12), and **G6/G9/G17
partially pass with real behavior** — now including registry-sourced per-device trust enforcement
(allowlist + source-IP/SIM pins + quarantine), a per-device replay/sequence guard (durable when a
Telematics Postgres connection string is configured; in-memory otherwise), idempotent projection,
and real OpenTelemetry primitives. **95 tests pass.** Still not built: the anomaly-scoring stage
(G8 — though duplicate/out-of-order fixes are now flagged by the replay guard), the determinism
corpus (G13), and **cryptographic auth for RAW GT06 devices (G10)** — impossible at that protocol
layer, though per-device HMAC/mTLS exist and are tested for transports that support them. DB
landing, cross-tenant leak proof, perf, dual-feed, and hardware (G9 e2e, G14–G16) remain
blocked-external. A customer statement must not claim more than the passing/partial gates above,
and must never present raw-GT06 ingest as cryptographically authenticated.

### What unblocks the blocked gates

| Blocked gate | Unblocked by |
|---|---|
| G9 (e2e), G10 (HTTP) | A CI-provisioned Postgres + `Opstrax.Api` test host (Testcontainers or an ephemeral Neon branch). |
| G10 (crypto auth on GT06 path) | Per-device HMAC / mTLS at a signing gateway — not possible for *raw* GT06 devices themselves; see `legacy-cutover-plan.md`. |
| G14 | Agreed SLO numbers + a load rig (and a deployed gateway to load). |
| G15 | A Samsara / Motive / Geotab account and API credentials. |
| G16 | Physical PT40-class hardware, SIM/APN, and a deployed gateway. |
