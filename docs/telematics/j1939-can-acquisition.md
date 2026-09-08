# J1939 classic-CAN acquisition boundary

**Status:** software engineering candidate

**Capability status:** DEVELOPMENT / physical evidence on EXTERNAL HOLD

**Scope:** classic CAN, 29-bit SAE J1939 identifiers, direct messages and bounded J1939-21 TP.CM/TP.DT acquisition

## Purpose

`J1939CanAcquisition` is the protocol boundary between a CAN adapter capture and OpsTrax's existing J1939 message decoders. It turns one copied raw CAN frame into a parsed evidence envelope and returns a complete J1939 message only when either:

- the frame directly carries the message payload; or
- a valid TP.CM/TP.DT sequence completes the advertised payload exactly.

`J1939CanIngestService` now supplies the bounded production host around that boundary. When explicitly enabled in the trusted gateway topology, it runs the configured absolute `candump` executable as `candump -L <interface>` without a shell, parses timestamped classic-CAN records, resolves one configuration-bound device serial through the trusted registry, and passes catalog-supported messages to `J1939SignalPublisher`.

This host makes software capable of consuming a real Linux SocketCAN interface. Its presence is not proof that an adapter, vehicle, ECU, wiring installation or signal value has been physically tested.

## SocketCAN host and ownership boundary

The CAN host is disabled by default. It is unavailable in the public HTTPS forwarding topology because it requires the platform registry and canonical event backbone. An enabled host fails startup when its executable path, interface, exact adapter evidence label, registry device serial or safety bounds are missing or invalid.

The deployment binds one interface to one registry serial. The registry must resolve that serial uniquely to an active installed device and vehicle. CAN source address remains ECU provenance only: it cannot select or overwrite tenant, company, device or vehicle identity. The registry result is cached for at most the configured refresh interval, with a maximum permitted interval of one minute; a lookup failure clears the cache before retry, so an outage cannot extend a previously accepted owner. Draft, unassigned, unconfigured, quarantined, suspended and retired devices cannot publish.

The local SocketCAN boundary does not provide per-frame cryptographic device authentication. `DirectDevice` means that a locally attached physical acquisition path supplied the bytes; it does not mean the exact hardware tuple is certified or spoof-proof. The default trust score is consequently conservative, and neither trust nor decoder confidence creates a certification claim.

The minimum production configuration uses environment-backed settings equivalent to:

```text
Gateway__J1939Can__Enabled=true
Gateway__J1939Can__CandumpPath=/usr/bin/candump
Gateway__J1939Can__Interface=can0
Gateway__J1939Can__AdapterType=<exact model / hardware revision / firmware evidence label>
Gateway__J1939Can__RegistryDeviceSerial=<provisioned registry serial>
```

The service invokes the executable with `ProcessStartInfo.ArgumentList`, never a shell or a composed command string. It accepts only `candump -L` records carrying an absolute Unix timestamp, the configured interface, an eight-hex-digit extended identifier and zero to eight complete payload bytes. Standard identifiers, CAN FD, RTR, error-shaped, malformed, wrong-interface, control-character and oversized records are dropped. Adapter stderr and raw frame text are never copied to logs. A capture reference contains only a deterministic SHA-256 digest of the admitted record.

Every frame must fall inside the configured capture-age and future-skew window. Supported messages receive deterministic event and correlation identifiers derived from registry scope plus ordered capture references. A replay of exactly the same evidence therefore reaches the PostgreSQL backbone with the same event identifier and is ignored by its existing idempotent ledger check. The stdout stream is consumed sequentially, so event-backbone backpressure propagates to `candump`; an acquisition-process exit resets incomplete transport state and restarts after bounded delay. If canonical publication fails, the exact already-owned envelope is parked in the gateway's existing production store-and-forward ledger and replayed under the same topic, partition key and event identity after recovery.

## Input contract

Every `J1939RawCanFrame` must include:

- a valid 29-bit extended CAN identifier;
- a classic CAN payload of zero to eight bytes;
- the capture timestamp;
- adapter type and bus/channel;
- a stable evidence reference for the captured frame.

Standard 11-bit frames, RTR frames, CAN error frames, oversized payloads, absent timestamps and missing or unbounded evidence labels fail closed.

## Identifier interpretation

The parser preserves the raw identifier and derives priority, 18-bit PGN, source address and destination semantics. For PDU1 (`PF < 240`), PDU Specific is the destination and the PGN low byte is zero. For PDU2, PDU Specific remains the group extension and the logical destination is global.

The payload is copied at admission so later mutation of an adapter-owned buffer cannot rewrite the stored frame evidence.

## Transport isolation and limits

TP.CM/TP.DT sessions are isolated by adapter type, CAN channel, source address and destination address. This prevents identical ECU addresses on different vehicle buses from being combined.

The boundary enforces:

- a maximum of 64 simultaneously active adapter/channel paths;
- the existing 30-second default session timeout;
- exact eight-byte TP frame size;
- exact sequence and advertised packet count;
- a maximum reassembled payload of 1,785 bytes;
- non-regressing capture time within a transfer;
- valid 18-bit transported PGNs and PDU1 group-extension rules;
- equality between attached acquisition evidence and the frame supplied to the reassembler.

Abandoned paths age out before the path limit is applied. Malformed or out-of-order transfers are discarded rather than partially decoded.

## Output truth

`J1939AcquiredMessage` retains the complete payload and the ordered raw-frame evidence envelopes used to produce it. A direct frame retains its CAN priority. A transported message reports `MessagePriority = null` because TP.CM communicates the target PGN but does not preserve the target message's original CAN priority.

DM1 and DM2 payloads can then pass to `J1939DiagnosticDecoder`. High-value signals pass only through the explicit `J1939SignalDecoder.SupportedSignals` catalog. The acquisition layer itself does not infer or emit signals.

`J1939DiagnosticAcquisition` provides the bounded composition for diagnostic consumers. It returns one of three explicit states for each accepted frame:

- no complete message, covering transport fragments and control frames without asserting that a session remains pending;
- complete non-diagnostic J1939 message, retaining the message and all capture evidence for another explicitly supported decoder;
- decoded DM1/DM2 diagnostic message, paired with the complete acquired message and its frame evidence.

A malformed complete DM1/DM2 message fails closed. The failure retains PGN, source address, capture times and capture references for investigation, but it does not copy raw payload bytes into exception text.

## Explicit signal catalog

The current software catalog contains exactly three reviewed mappings:

| PGN | PGN name | SPN | Signal | Position | Encoding | Canonical path |
| --- | --- | --- | --- | --- | --- | --- |
| 61444 | Electronic Engine Controller 1 (EEC1) | 190 | Engine Speed | bytes 4–5 of an 8-byte payload | unsigned little-endian, 0.125 rpm/bit, zero offset | `Vehicle.Powertrain.CombustionEngine.Speed` |
| 65253 | Engine Hours, Revolutions (HOURS) | 247 | Engine Total Hours of Operation | bytes 1–4 of an 8-byte payload | unsigned little-endian, 0.05 h/bit, zero offset | `Vehicle.Powertrain.CombustionEngine.EngineHours` |
| 65271 | Vehicle Electrical Power 1 (VEP1) | 168 | Battery Potential / Power Input 1 | bytes 5–6 of an 8-byte payload | unsigned little-endian, 0.05 V/bit, zero offset | `Vehicle.LowVoltageBattery.CurrentVoltage` |

The mapping is cross-checked against the public [Cummins A079E226 installation manual, section 6.3.4](https://www.cummins.com/sites/default/files/2025-08/onan-rv-generator-installation-manual-hglca-a.pdf). The canonical path and `rpm` unit follow the [COVESA VSS combustion-engine catalog](https://github.com/COVESA/vehicle_signal_specification/blob/master/spec/Powertrain/CombustionEngine.vspec). SAE J1939DA remains the assignment authority.

J1939 indicator ranges are classified from the most-significant byte before scaling. For a two-byte value, the ranges are:

- `0x0000`–`0xFAFF`: valid numeric value;
- `0xFB00`–`0xFDFF`: parameter-specific indicator, not interpreted by this decoder;
- `0xFE00`–`0xFEFF`: error indicator;
- `0xFF00`–`0xFFFF`: not available.

The same most-significant-byte rule is applied to the four-byte engine-hours value. Only a valid value receives a scaled reading. An error, unavailable or parameter-specific indicator retains its status and raw numeric code while its decoded value remains null. An unlisted PGN remains unsupported even if its bytes look plausible. A supported fixed-length PGN with any other payload length fails closed with bounded provenance and no raw bytes in exception text.

`J1939MessageAcquisition` provides the general routing boundary: incomplete transport traffic, unsupported complete messages, decoded DM1/DM2 diagnostics and decoded catalog signals are distinct outcomes. The signal result retains the original `J1939AcquiredMessage`, including source address, timestamps, adapter/channel and ordered capture references.

## Canonical DM1/DM2 evidence

The SocketCAN host now routes decoded DM1 and DM2 messages into the same tenant-owned canonical durability path as catalog signals. `J1939DiagnosticCanonicalEventFactory` records the message PGN, active/historical distinction, source address, all eight lamp states and every decoded SPN/FMI, occurrence count and conversion-method flag. It also fills the existing canonical DTC-code list with stable `SPN-<n>-FMI-<n>` labels. Controller origin remains explicit in each canonical identity as `J1939:SA:<decimal-address>:SPN:<n>:FMI:<n>`; source address still has no ownership authority.

DM1 is marked active evidence. DM2 is marked historical evidence and never becomes a clear command. An empty DM2 remains an evidence event with an empty trouble-code list. The PostgreSQL canonical ledger classifies structured diagnostic snapshots as `diagnostic.event` and deduplicates an exact capture replay by its evidence-derived event identity. Raw diagnostic bytes are not placed in headers or logs; the canonical payload retains decoded semantics and bounded capture hashes.

The PostgreSQL backbone now writes each decoded DTC to `fault_occurrences` in the same transaction as the canonical event after resolving the exact registry device serial and the installation that was effective at the observation time. A fresh direct-CAN DM1 may advance the order-safe `fault_codes` projection. Exact event replay is a no-op, an older DM1 remains occurrence evidence only, and a stale DM1 cannot establish live state. DM2 remains occurrence evidence only and never clears or changes `fault_codes`. Ambiguous, missing or quarantined installation ownership leaves the canonical record intact without guessing a maintenance vehicle or branch.

This maintenance projection deliberately stops before `diagnostic_holds`, vehicle grounding, dispatch holds or work-order creation. Those safety actions require their own independent SDET and operational acceptance gate. A critical lamp can therefore appear as a critical active fault without automatically changing vehicle availability in this slice.

## Canonical publication and freshness

`J1939CanonicalEventFactory` accepts a catalog-supported acquired message plus registry-resolved ownership, explicit event/correlation identities, source classification, trust/confidence and a freshness budget. It never derives a tenant, device or vehicle from the CAN source address. Because these PGNs carry no device clock, CAN capture completion anchors both the observation and gateway-receipt time; the separate normalization time determines freshness.

Every decoded SPN becomes a canonical `SignalValue` with a named availability state. Fresh valid data is `Available`. An older valid reading is retained as evidence with `Stale` availability and the event's `IsStale` quality flag, but is not promoted to a typed current-value field. Parameter-specific, error and not-available indicators persist with null values and explicit availability names. This prevents a reserved wire code from appearing as a real customer measurement.

`J1939SignalPublisher` creates the tenant/company/device partition key and publishes the canonical event to `telemetry.normalized`. Its envelope headers retain PGN, SPN, source/destination address, adapter type, CAN channel and the ordered capture references without including raw payload bytes. The production PostgreSQL backbone classifies a non-positional event carrying signals as `vehicle.signal` and stores the full canonical payload and envelope headers. The CAN host resolves the configuration-bound registry device before calling this publisher; the interface and CAN address never become ownership authority.

## Customer-visible latest signal projection

Stage 129 adds `latest_device_signals`, a tenant-readable projection updated in the same PostgreSQL transaction as the append-only canonical event. Its key is company, registry device and canonical signal path. A late event cannot replace a newer observation; equal observation times use normalization time as the deterministic tiebreaker. Explicit unavailable/error states replace the prior value with null, so the UI cannot keep showing an old numeric reading after the ECU reports that the parameter is unavailable.

The projection carries source, transport, protocol, adapter version, trust, confidence, availability, observation/receipt/normalization times and a permanent false certification flag. Customer access is SELECT-only through forced row-level security. The gateway removes arbitrary envelope headers and persists only the approved J1939 PGN, SPN, address, adapter, channel and capture-reference fields. Raw frames, credentials and unrestricted canonical history remain outside the customer surface.

The OBD/J1939 diagnostics page reads only the three cataloged engine/electrical paths listed above. It displays engine speed, engine hours, battery voltage, named availability, provenance, capture reference and the evidence label `Operational observation only — not certification`. A signal observation makes the device visible in the diagnostics inventory, including when it has no GPS position. This is product visibility for received canonical evidence; it does not change the external-hold certification status.

## Verification

The protocol suite covers direct DM1 decoding, PDU1/PDU2 identifier semantics, input rejection, immutable capture copying, multi-packet DM1/DM2 reconstruction, diagnostic outcome classification, structured canonical lamp/DTC evidence, explicit DM2 historical semantics, explicit signal routing, little-endian RPM/hours/voltage scaling, two-byte and four-byte indicator ranges, canonical availability and freshness, tenant-filtered publication, unsupported-PGN behavior, evidence retention, bounded malformed-message failures, bus isolation, concurrent channels, abandoned-path expiry, timestamp regression, invalid transported PGNs and mismatched evidence rejection. CAN-host tests cover strict `candump` admission, registry-bound ownership, source-address non-authority, lifecycle rejection, capture-time bounds, malformed-message recovery, DM1/DM2 publication and deterministic replay identity. PostgreSQL durability tests cover diagnostic classification/idempotency, historical-installation ownership, active DM1 projection, stale-DM1 suppression, DM2 evidence-only behavior, newest-signal ordering, explicit unavailable replacement, the false certification boundary and removal of non-allowlisted headers. The clean predeployment migration rehearsal covers Stage 129 creation, idempotent replay, RLS policies and least-privilege grants.

Synthetic tests establish deterministic software behavior only. Capability promotion still requires an exact adapter/device/firmware tuple, physical CAN traffic, trusted comparison values, controlled vehicle testing, recovery and soak evidence, and qualified human acceptance under the commercialization plan.
