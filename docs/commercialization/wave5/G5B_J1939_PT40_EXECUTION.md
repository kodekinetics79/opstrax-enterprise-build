# G5B J1939 / PT40 / OEM Hardware Depth — Current-Build Execution Baseline

Parent: #144 / #110  
Entry: `main@1f3b5de029b33e9315fb96c80988e610665c41b0`  
State: ACTIVE under `CR-2026-09-03-04` when v2.5 merges.

## Existing product foundation to preserve

- `telematics/src/Opstrax.Telematics.Protocols.J1939/J1939DiagnosticDecoder.cs` already decodes reassembled J1939-73 DM1 (PGN 65226) and DM2 (PGN 65227) payloads.
- The decoder explicitly states that CAN transport-protocol acquisition/reassembly is outside its current responsibility; this is a real Wave 5 gap, not a reason to rewrite the decoder.
- `backend-dotnet/Services/DiagnosticFaultNormalizer.cs` already normalizes supported J1939 DM1/DM2 diagnostic faults into OpsTrax.
- Existing tests verify lamp state, SPN, FMI, occurrence and conversion method behavior.
- Current product truth correctly classifies J1939 and PT40 as DEVELOPMENT.

## Atomic J1939 build order

1. Freeze acquisition boundary and supported CAN/J1939 adapter/device class; no universal CAN claim.
2. Introduce raw-frame envelope preserving timestamp, bus/channel, source address, priority, PGN and capture provenance.
3. Implement bounded BAM/RTS-CTS transport reassembly as required by the selected physical acquisition path; defend against malformed/oversized/incomplete sessions.
4. Feed complete DM1/DM2 payloads into the existing decoder rather than duplicating it.
5. Add a registry of explicitly supported high-value PGNs/SPNs with units/scaling/unavailable/error semantics.
6. Add canonical engine observations only for physically proven signals (candidate set: engine hours, RPM, coolant, fuel, distance/odometer, battery/voltage, engine load).
7. Persist provenance/freshness and render unsupported/missing/stale as unavailable rather than zero.
8. Bench against real heavy-duty hardware and trusted reference values, then controlled vehicle test and recovery/soak.

## PT40 / OEM execution

1. Acquire and freeze exact manufacturer/model/HW rev/FW tuple.
2. Capture real bytes before selecting/authoring a parser.
3. Fingerprint and obtain vendor specification or independently verified wire behavior.
4. Implement isolated adapter into canonical telemetry; preserve raw evidence and adapter version.
5. Bench identity/location/heartbeat/supported events.
6. Controlled route/reference comparison.
7. Power/network/GPS/server-restart/duplicate-session recovery.
8. 24h Certified Compatible soak; 72h + repeatable install/procurement/RMA for Production Supported.

## First implementation slice

The first code slice is the J1939 acquisition/reassembly contract and hostile-input test harness around the existing decoder. It must not insert guessed vehicle signals or alter DM1/DM2 semantics. Physical CAN source evidence remains mandatory before capability promotion.

## Second implementation slice — classic CAN acquisition

The bounded software candidate now adds the raw classic-CAN acquisition boundary described in [`docs/telematics/j1939-can-acquisition.md`](../../telematics/j1939-can-acquisition.md): 29-bit identifier parsing, copied per-frame provenance, adapter/channel isolation, concurrent-bus safety, transport integration and hostile-input rejection. Transported message priority remains unavailable because TP.CM does not carry the target message's original priority.

This advances deterministic software readiness only. J1939, PT40 and OEM status remains DEVELOPMENT / EXTERNAL HOLD until the exact physical evidence and qualified acceptance above exist.

## Third implementation slice — evidence-bearing DM1/DM2 pipeline

The bounded software candidate now composes classic-CAN admission, transport reassembly and the existing J1939-73 DM1/DM2 decoder through `J1939DiagnosticAcquisition`. Every admitted frame produces an explicit outcome: no complete message, complete non-diagnostic J1939 message, or decoded diagnostic message. The first state covers incomplete transfers and transport control frames without falsely asserting that a session remains pending. Complete non-diagnostic traffic retains its acquisition evidence for a later explicitly supported signal registry; it is never mislabelled as a diagnostic or collapsed into invented engine observations.

Malformed complete DM1/DM2 messages fail closed with PGN, source, time and capture-reference provenance while omitting raw payload bytes from exception text. This is an internal integration boundary only. It does not open a CAN interface, persist a vehicle observation, establish the authority of a source ECU, or create a physical compatibility claim.

## Fourth implementation slice — explicit engine-signal admission

The software candidate now implements the first entry in an immutable supported-signal catalog: EEC1 PGN 61444 / SPN 190 Engine Speed, bytes 4–5, little-endian, 0.125 rpm/bit and zero offset. The output is mapped to `Vehicle.Powertrain.CombustionEngine.Speed` with an `rpm` unit and stays paired with the acquired source/timestamp/adapter/channel/capture evidence.

The decoder evaluates the SAE J1939 two-byte indicator ranges before applying scaling. Parameter-specific, error and not-available codes produce explicit nonnumeric statuses with a null decoded value. Zero remains a valid zero. Unlisted PGNs are unsupported, and a malformed EEC1 payload fails closed without putting raw bytes in exception text.

`J1939MessageAcquisition` routes complete messages to the existing DM1/DM2 path, the exact signal catalog or an explicit unsupported outcome. This completes catalog item 5 and the deterministic software portion of item 6 for one signal. It does not prove that any ECU broadcasts the signal, that an adapter acquired it correctly, or that a vehicle reference value agrees. Those claims remain DEVELOPMENT / EXTERNAL HOLD through bench, controlled-route, recovery and soak testing.

## Fifth implementation slice — engine hours and battery potential

The explicit catalog now also admits HOURS PGN 65253 / SPN 247 Engine Total Hours of Operation at bytes 1–4 with 0.05 h/bit and VEP1 PGN 65271 / SPN 168 Battery Potential / Power Input 1 at bytes 5–6 with 0.05 V/bit. They map to the COVESA-style `Vehicle.Powertrain.CombustionEngine.EngineHours` and `Vehicle.LowVoltageBattery.CurrentVoltage` paths.

The numeric decoder now handles reviewed unsigned two- and four-byte little-endian definitions while classifying the indicator range from the most-significant byte before scaling. Focused tests cover real numeric values and every four-byte indicator boundary. The exact ECU/device/adapter/firmware tuple and agreement with trusted physical reference readings remain EXTERNAL HOLD.

## Sixth implementation slice — canonical freshness and durable publication seam

Catalog signals now convert to registry-owned `CanonicalTelemetryEvent` records through an explicit factory. CAN capture completion anchors the timestamp because the admitted PGNs do not carry a device clock. The caller must supply authenticated registry ownership, event/correlation identities, source classification, normalization time, freshness budget, trust and confidence; source address is retained as provenance and never treated as device identity.

Canonical signals carry named `Available`, `Stale`, `ParameterSpecific`, `Error` or `NotAvailable` states. Stale measurements retain their evidence value and set the event stale flag, while nonnumeric indicators store null. Only a fresh available battery reading reaches the typed current battery field.

The gateway publication seam writes the event to the tenant/company/device partition on `telemetry.normalized`, with bounded headers for PGN/SPN, source/destination address, adapter/channel and capture references. The PostgreSQL backbone stores these as `vehicle.signal` events. In-memory integration tests prove partitioning, tenant-filter isolation and rejection of mixed adapter evidence. At this slice, the production acquisition host and proof against an exact adapter/device/firmware candidate remained following work.

## Seventh implementation slice — durable customer signal visibility

Stage 129 projects the latest canonical signal per tenant, registry device and exact signal path in the same transaction as canonical event storage. Newer observations win deterministically; a later explicit unavailable/error indicator clears the numeric value. The projection preserves source, CAN transport, protocol, adapter version, freshness, confidence, trust and approved capture provenance while fixing `certification_claim` at false. Forced row-level security gives the tenant application SELECT-only access and the system publisher SELECT/INSERT/UPDATE access without delete.

The OBD/J1939 customer diagnostics page now consumes the three reviewed catalog paths and shows engine speed, engine hours, battery voltage, named signal state, transport, adapter version, trust and capture reference. A signal-only device is eligible for the diagnostics inventory without borrowing another device's GPS position. Every projected observation is labelled `Operational observation only — not certification`.

This closes the software path from admitted CAN evidence through decode, canonical publication, durable latest-value projection, tenant API and customer UI. It does not close G5B certification. Exact physical adapter/device/firmware identity, authenticated acquisition, reference-value agreement, controlled vehicle testing, recovery, soak and qualified human acceptance remain `EXTERNAL HOLD`.

## Eighth implementation slice — production SocketCAN acquisition host

The trusted gateway topology can now opt into a bounded Linux SocketCAN host. It launches an absolute `candump` executable without a shell, reads only `-L` timestamped classic-CAN output from one configured interface, rejects CAN FD/standard-ID/RTR/wrong-interface/malformed/oversized records, and sends admitted frames through the existing transport, signal, canonical publication and durable projection path. Sequential stdout consumption carries event-backbone backpressure to the acquisition process, while bounded restart delay recovers from an adapter-process exit without joining an incomplete transport session across restarts.

Deployment binds the interface to one provisioned registry device serial and one exact adapter evidence label. Registry ownership determines tenant, company, device and vehicle. The ECU source address cannot select ownership. Short registry refresh permits lifecycle holds to stop publication; lookup failure clears the cached owner and fails closed. Draft, unassigned, unconfigured, quarantined, suspended and retired states do not publish. Capture timestamps outside configured age/skew bounds are dropped, raw frames and adapter stderr are not logged, and deterministic evidence-derived event identities let the PostgreSQL ledger ignore an exact replay. A backbone failure parks the exact canonical envelope in the existing production store-and-forward ledger for ordered replay after recovery.

This closes the missing software orchestration seam between a real SocketCAN stream and the customer-visible latest-signal projection. It does not prove per-frame cryptographic authenticity, physical attachment, adapter compatibility, ECU authority, mapping agreement, route behavior, recovery or soak. G5B remains DEVELOPMENT / EXTERNAL HOLD until the exact device/adapter/hardware/firmware tuple is procured and the physical evidence plan is executed.

## Ninth implementation slice — durable canonical DM1/DM2 evidence

The CAN host now publishes decoded DM1/DM2 through the tenant-owned canonical durability path instead of dropping the already-decoded result. The additive canonical diagnostic snapshot retains PGN, active/historical state, source address, eight lamp states, decoded SPN/FMI codes, occurrence counts and conversion-method flags. Bounded headers retain the diagnostic kind, adapter/channel and capture hashes without raw bus bytes. Exact capture replay converges on the same event identity, and the PostgreSQL ledger classifies it as `diagnostic.event`.

DM1 is active evidence. DM2 is historical evidence only and cannot clear DM1 state. Physical source authenticity, reference-value agreement and hardware certification remain EXTERNAL HOLD.

## Tenth implementation slice — order-safe maintenance diagnostic projection

The production PostgreSQL backbone now persists decoded DTC occurrences and current DM1 state atomically with the canonical diagnostic event. It resolves the registry device serial and exact installation effective at the observation time; ambiguous, missing or quarantined ownership remains canonical evidence and cannot be projected by guessing a vehicle or branch. Exact replay is idempotent. Older or freshness-failed DM1 records remain occurrence evidence without changing live state. DM2 always remains historical occurrence evidence and cannot clear or overwrite `fault_codes`.

The customer maintenance surfaces can now read received canonical CAN diagnostic state through their existing fault-code APIs. This slice does not create `diagnostic_holds`, ground vehicles, hold dispatch, create work orders or promote any certification candidate. Those safety effects remain a separate independently reviewed gate, and all physical claims remain EXTERNAL HOLD.

The customer fault list now distinguishes `CanonicalCanObservation`, `AuthenticatedDeviceObservation` and legacy/unclassified records, and separately reports whether a persisted vehicle/review hold exists. The diagnostics pages derive protocol and latest diagnostic time from the fault record when no GPS position exists. Raw evidence JSON is omitted from the list response, and the maintenance insight no longer states that an automatic defect was created without a persisted action.

The pre-existing diagnostic-hold ledger and acknowledge/verified-resolve endpoints are now visible in a dedicated Maintenance tab. Acknowledgement explicitly leaves availability unchanged. Resolution requires one accepted verification type, an evidence reference and notes; the server then rechecks all diagnostic, DVIR and work-order blockers. This exposes the governed workflow without making CAN observations create holds automatically.

## DeviceOps registry integration

Stage 115 provides the shared exact manufacturer/model/hardware revision/firmware and software-SHA candidate identity needed by a later PT40 or OEM certification candidate. It deliberately seeds no PT40, GT06, J1939 adapter or OEM record and its database contract fixes every engineering candidate at `ExternalHold` / `Unverified`. A candidate may be recorded only after the exact physical tuple is observed; tier promotion requires the evidence gates in this document and a separately frozen certification candidate.

## Stop conditions

RED if the lane invents PGN values, uses synthetic frames as physical certification, collapses unavailable values to zero, loses source address/provenance, claims universal J1939/CAN support, or treats a PT40/OEM marketing sheet as wire-level certification.
