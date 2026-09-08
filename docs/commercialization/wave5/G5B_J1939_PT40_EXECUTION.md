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

## DeviceOps registry integration

Stage 115 provides the shared exact manufacturer/model/hardware revision/firmware and software-SHA candidate identity needed by a later PT40 or OEM certification candidate. It deliberately seeds no PT40, GT06, J1939 adapter or OEM record and its database contract fixes every engineering candidate at `ExternalHold` / `Unverified`. A candidate may be recorded only after the exact physical tuple is observed; tier promotion requires the evidence gates in this document and a separately frozen certification candidate.

## Stop conditions

RED if the lane invents PGN values, uses synthetic frames as physical certification, collapses unavailable values to zero, loses source address/provenance, claims universal J1939/CAN support, or treats a PT40/OEM marketing sheet as wire-level certification.
