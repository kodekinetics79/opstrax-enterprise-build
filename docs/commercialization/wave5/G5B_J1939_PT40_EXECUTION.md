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

## Stop conditions

RED if the lane invents PGN values, uses synthetic frames as physical certification, collapses unavailable values to zero, loses source address/provenance, claims universal J1939/CAN support, or treats a PT40/OEM marketing sheet as wire-level certification.