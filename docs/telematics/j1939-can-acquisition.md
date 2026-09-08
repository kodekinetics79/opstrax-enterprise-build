# J1939 classic-CAN acquisition boundary

**Status:** software engineering candidate

**Capability status:** DEVELOPMENT / physical evidence on EXTERNAL HOLD

**Scope:** classic CAN, 29-bit SAE J1939 identifiers, direct messages and bounded J1939-21 TP.CM/TP.DT acquisition

## Purpose

`J1939CanAcquisition` is the boundary between a CAN adapter capture and OpsTrax's existing J1939 message decoders. It turns one copied raw CAN frame into a parsed evidence envelope and returns a complete J1939 message only when either:

- the frame directly carries the message payload; or
- a valid TP.CM/TP.DT sequence completes the advertised payload exactly.

The component does not open a CAN interface, select a vehicle adapter, identify an ECU, or assert that a capture came from physical hardware. The caller must supply the adapter type, bus/channel, capture timestamp and capture reference from the real acquisition mechanism.

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

## Canonical publication and freshness

`J1939CanonicalEventFactory` accepts a catalog-supported acquired message plus registry-resolved ownership, explicit event/correlation identities, source classification, trust/confidence and a freshness budget. It never derives a tenant, device or vehicle from the CAN source address. Because these PGNs carry no device clock, CAN capture completion anchors both the observation and gateway-receipt time; the separate normalization time determines freshness.

Every decoded SPN becomes a canonical `SignalValue` with a named availability state. Fresh valid data is `Available`. An older valid reading is retained as evidence with `Stale` availability and the event's `IsStale` quality flag, but is not promoted to a typed current-value field. Parameter-specific, error and not-available indicators persist with null values and explicit availability names. This prevents a reserved wire code from appearing as a real customer measurement.

`J1939SignalPublisher` creates the tenant/company/device partition key and publishes the canonical event to `telemetry.normalized`. Its envelope headers retain PGN, SPN, source/destination address, adapter type, CAN channel and the ordered capture references without including raw payload bytes. The production PostgreSQL backbone classifies a non-positional event carrying signals as `vehicle.signal` and stores the full canonical payload and envelope headers. The future physical CAN host must authenticate and resolve the owning device before calling this publisher.

## Verification

The protocol suite covers direct DM1 decoding, PDU1/PDU2 identifier semantics, input rejection, immutable capture copying, multi-packet DM1/DM2 reconstruction, diagnostic outcome classification, explicit signal routing, little-endian RPM/hours/voltage scaling, two-byte and four-byte indicator ranges, canonical availability and freshness, tenant-filtered publication, unsupported-PGN behavior, evidence retention, bounded malformed-message failures, bus isolation, concurrent channels, abandoned-path expiry, timestamp regression, invalid transported PGNs and mismatched evidence rejection.

Synthetic tests establish deterministic software behavior only. Capability promotion still requires an exact adapter/device/firmware tuple, physical CAN traffic, trusted comparison values, controlled vehicle testing, recovery and soak evidence, and qualified human acceptance under the commercialization plan.
