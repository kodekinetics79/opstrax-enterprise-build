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

DM1 and DM2 payloads can then pass to `J1939DiagnosticDecoder`. No high-value engine signal is emitted from this acquisition layer, and unavailable values are not converted to zero.

`J1939DiagnosticAcquisition` provides the bounded composition for diagnostic consumers. It returns one of three explicit states for each accepted frame:

- no complete message, covering transport fragments and control frames without asserting that a session remains pending;
- complete non-diagnostic J1939 message, retaining the message and all capture evidence for another explicitly supported decoder;
- decoded DM1/DM2 diagnostic message, paired with the complete acquired message and its frame evidence.

A malformed complete DM1/DM2 message fails closed. The failure retains PGN, source address, capture times and capture references for investigation, but it does not copy raw payload bytes into exception text.

## Verification

The protocol suite covers direct DM1 decoding, PDU1/PDU2 identifier semantics, input rejection, immutable capture copying, multi-packet DM1/DM2 reconstruction, diagnostic outcome classification, evidence retention, bounded malformed-diagnostic failures, bus isolation, concurrent channels, abandoned-path expiry, timestamp regression, invalid transported PGNs and mismatched evidence rejection.

Synthetic tests establish deterministic software behavior only. Capability promotion still requires an exact adapter/device/firmware tuple, physical CAN traffic, trusted comparison values, controlled vehicle testing, recovery and soak evidence, and qualified human acceptance under the commercialization plan.
