# GT06 Physical Compatibility Certification Plan

**Program tracker:** #110  
**Device-certification tracker:** #109  
**Authority:** `CR-2026-09-03-03` / Master amendment v2.4  
**Current re-entry baseline:** `main@d1bd4c7c7d9b1b24d3d24b610b00ad321b5368fa`  
**Historical software/listener lane:** closed PR #114 at `a297b2773a27466388c4cf49c40eaa5360461852` — supporting evidence only, not current-main certification

## Purpose

Convert one exact GT06-family device model/firmware combination from software-compatible to **Opstrax Certified Compatible** using real bench, vehicle, recovery and soak evidence, then graduate the same frozen tuple to **Production Supported** only after the additional 72-hour/supportability gate.

The generic name `GT06` is not a certification identity. Every certification record must bind to a specific manufacturer/model/hardware revision/firmware combination.

## Certification ladder

`Candidate -> Protocol Identified -> Bench Compatible -> Vehicle Tested -> Failure/Recovery Tested -> 24h Soak Tested -> Security Reviewed -> Opstrax Certified Compatible -> 72h Soak + Supportability -> Production Supported`

## Hardware qualification record — freeze before first admitted login

| Field | Required value |
|---|---|
| Manufacturer | TBD from physical candidate |
| Exact model | TBD from physical candidate |
| Hardware revision | TBD from physical candidate |
| Firmware | TBD from physical candidate/runtime command or vendor record |
| Unit count | Minimum 2 identical; 3 preferred |
| IMEI/serial format | TBD; store full values only in protected evidence, mask in public artifacts |
| Modem/chipset | TBD |
| SIM/carrier | TBD |
| APN | TBD |
| LTE bands | TBD |
| U.S. FCC evidence | TBD if U.S. in scope |
| Canada ISED evidence | TBD if Canada in scope |
| Input voltage / backup battery | TBD |
| Installation/wiring | TBD |
| Protocol/revision | Must be identified from real device behavior; do not assume from listing |
| Reporting cadence | TBD/observed |
| Supported alarms/events | TBD/observed |
| Supported commands | TBD/observed |
| Procurement source | TBD; retain invoice/listing/SKU/lot evidence |
| Intended markets | U.S. / Canada only as supported by actual radio/compliance evidence |

## Minimum bench inventory

- Two identical production-candidate devices; three preferred.
- Activated SIM/data service for every bench unit.
- Safe fused 12/24 V bench supply or approved vehicle power setup.
- Isolated certification-staging raw-TCP gateway destination.
- Reference GPS source with timestamps.
- Ability to preserve sanitized raw-frame, gateway, API, persistence and visible-Chrome evidence.
- Operator worksheet recording every power/network/GPS/reboot action and exact UTC timestamp.

## Isolated staging listener admission

The controlled listener configuration is `telematics/fly.staging-certification.toml`.
It is physically and logically separate from customer production traffic, exposes raw TCP port `5023`, and forwards normalized observations only to the OpsTrax certification-staging API.

The listener starts **fail closed with an empty device allowlist**. Do not add an IMEI merely because a label, seller page or photograph exists. Admission requires the exact physical candidate in hand, the completed hardware-qualification record above, and an operator-confirmed identifier from the same unit. Enrolling an IMEI enables protocol-identification and bench evidence only; it does not certify the device.

Gateway HMAC, outbox-encryption keys, SIM credentials and any unrestricted device identifiers are secrets and must never be committed. Public listener address, deployed OpsTrax SHA, sanitized configuration hash and provider release identity belong in the controlled evidence package.

## Real-hardware acceptance matrix

| Area | Required result |
|---|---|
| Cold startup | Device boots cleanly and reaches gateway |
| Identity/login | Exact physical identifier maps only to the provisioned device and tenant |
| Login ACK | ACK matches the observed protocol and the device remains in its normal reporting lifecycle |
| GPS | Valid coordinates and device-originated timestamps persist correctly |
| Heartbeat | Heartbeat/device-health state updates truthfully |
| Alarms/events | Every claimed supported event is correctly classified |
| Power/ignition | Correct where hardware exposes it; otherwise explicitly unsupported |
| Reboot | Device reconnects and legitimate post-reboot telemetry is accepted |
| Server restart | Device reconnects without manual data repair or cross-device corruption |
| Duplicate identity | Documented authoritative-session behavior is enforced |
| Mobile-network outage | Offline/stale truth is shown; recovery does not fabricate continuity |
| GPS outage | No-position/degraded state is honest |
| Buffered data | Historical/offline fixes remain historical if the device supports buffering |
| Replay/duplicate frames | Duplicate observations do not create duplicate operational events |
| High cadence | No uncontrolled memory/queue/data-integrity failure |
| Supported commands | Only explicitly observed/supported exchanges enter the certification record |
| 24-hour soak | Required for **Opstrax Certified Compatible** |
| 72-hour soak | Required for **Production Supported** |

## Controlled vehicle drive

Use a known route and compare four layers with synchronized timestamps:

1. reference GPS/location;
2. raw tracker observation;
3. persisted OpsTrax telemetry;
4. visible OpsTrax map/history.

Check coordinate accuracy, hemisphere, timestamps, ordering, freshness, speed/heading where supplied, geofence transitions, power/ignition where supplied, connectivity-gap behavior and recovery. Any field not emitted by the candidate is recorded as unsupported/untested, never defaulted to zero/healthy.

## Security / tenancy gates

- Unknown/unprovisioned hardware is refused.
- Device identity cannot silently move to another tenant or asset.
- Duplicate device sessions follow the documented policy.
- Exact duplicate/replayed observations do not create duplicate operational events.
- Post-reboot legitimate telemetry is not discarded as false replay.
- Secrets are not committed to source control or exposed in customer-visible logs/evidence.
- Device Health keeps `registered`, `connected`, `commissioned`, `assigned`, `certified-compatible` and `production-supported` as distinct states.

## Defect loop

Every physical/protocol failure follows:

`Observe -> Evidence -> Root Cause -> Fix -> Targeted Test -> Exact-SHA Deploy -> Repeat Same Physical Journey -> Close`

A source-code fix, simulator pass or synthetic packet replay does not close a physical-device defect by itself.

## Required evidence package

Store evidence under a run-specific controlled location such as:

`artifacts/device-certification/gt06/<manufacturer>-<model>/<YYYYMMDD-run-id>/`

Required index:

- `hardware-identity.md`
- `firmware-and-radio-evidence.md`
- `operator-action-timeline.md`
- `raw-protocol-evidence-index.md`
- `bench-test-ledger.md`
- `route-comparison.md`
- `failure-recovery-ledger.md`
- `24h-soak-summary.md`
- `security-review.md`
- `defect-fix-retest-ledger.md`
- `visible-chrome-reconciliation.md`
- `final-compatibility-record.md`
- `72h-soak-summary.md` and `support-rma-install-record.md` for Production Supported

Never commit live credentials, SIM secrets or unrestricted customer/device identifiers.

## Product acceptance record

The final supported-device record states:

- manufacturer/model;
- hardware revision;
- qualified firmware;
- protocol;
- capabilities actually observed;
- capabilities not supported/not tested;
- certification status;
- certification reference;
- certification date;
- installation requirements;
- known limitations;
- intended markets and supporting radio evidence;
- procurement/support/RMA status.

## Gate definitions

### Opstrax Certified Compatible

Requires real end-to-end identity/login/location/heartbeat, claimed event support, reboot/reconnect/recovery, controlled-drive evidence, 24-hour soak, intended-market radio evidence, truthful customer UI, zero open P0/P1 hardware/protocol defects, and independent Hardware + Security + Principal SDET acceptance under Appendix B.

### Production Supported

Requires everything above plus a 72-hour soak, repeatable provisioning/install instructions, approved procurement source, support/replacement/RMA procedure and an operational owner.

## Regulatory boundary

GT06 physical compatibility is **not** ELD certification. It is also not proof of universal OBD-II/J1939/CAN capability or compatibility with every product marketed under the generic GT06 name.

Until the exact candidate physically passes these gates, the commercial truth remains **GT06 software: PILOT / hardware: NOT CERTIFIED**.
