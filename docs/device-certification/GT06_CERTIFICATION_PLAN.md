# GT06 Physical Compatibility Certification Plan

Program tracker: #110  
Device-certification tracker: #109  
Starting OpsTrax baseline: `155b54a3451c2a4618b4fc6a87fd59f0e68f425d`

## Purpose

Convert one exact GT06-family device model/firmware combination from software-compatible to **Opstrax Certified Compatible** using real bench, vehicle, recovery and soak evidence.

The generic name `GT06` is not a certification identity. Every certification record must bind to a specific manufacturer/model/hardware revision/firmware combination.

## Certification ladder

`Candidate -> Protocol Identified -> Bench Compatible -> Vehicle Tested -> Failure/Recovery Tested -> Soak Tested -> Security Reviewed -> Opstrax Certified Compatible -> Production Supported`

## Hardware qualification record

Record before first live test:

| Field | Required value |
|---|---|
| Manufacturer | TBD |
| Exact model | TBD |
| Hardware revision | TBD |
| Firmware | TBD |
| IMEI/serial format | TBD |
| Modem/chipset | TBD |
| SIM/carrier | TBD |
| APN | TBD |
| LTE bands | TBD |
| U.S. FCC evidence | TBD |
| Canada ISED evidence | TBD if Canada in scope |
| Input voltage / backup battery | TBD |
| Installation/wiring | TBD |
| Protocol/revision | TBD |
| Reporting cadence | TBD |
| Supported alarms/events | TBD |
| Supported commands | TBD |
| Procurement source | TBD |

## Minimum bench inventory

- Two identical production-candidate devices; three preferred.
- Activated SIM/data service.
- Safe bench/vehicle power setup.
- Isolated staging gateway destination.
- Reference GPS source.
- Ability to preserve sanitized gateway/API/browser evidence.

## Isolated staging listener admission

The controlled listener configuration is `telematics/fly.staging-certification.toml`.
It is physically and logically separate from any existing provider- or production-configured
gateway, exposes raw TCP port `5023`, and forwards only to the OpsTrax certification-staging API.

The listener starts **fail closed with an empty device allowlist**. Do not add an IMEI merely
because a label or photo is available. Admission requires the exact physical candidate in hand,
the hardware-qualification record above, and a controlled operator-confirmed IMEI. Enrolling an
IMEI enables only protocol-identification and bench evidence; it does not certify the device.

The separately stored gateway HMAC and outbox-encryption secrets must never be committed. The
public listener address, deployed OpsTrax SHA, sanitized configuration hash and provider release
identity belong in the run evidence. Existing endpoints configured for another API are not valid
staging evidence and must not be handed to the certification operator.

## Real-hardware acceptance matrix

| Area | Required result |
|---|---|
| Cold startup | Device boots cleanly and reaches gateway |
| Identity/login | Exact IMEI/identifier maps to the provisioned device and tenant |
| Acknowledgement | Device remains connected and continues its normal reporting lifecycle |
| GPS | Valid coordinates and device-originated timestamps persist correctly |
| Heartbeat | Heartbeat/device-health state updates truthfully |
| Alarms/events | Every claimed supported event is correctly classified |
| Power/ignition | Correct where hardware exposes it |
| Reboot | Device reconnects and legitimate post-reboot telemetry is accepted |
| Server restart | Device recovers without manual data repair |
| Duplicate identity | Documented authoritative-session behavior is enforced |
| Network outage | Offline/stale truth is shown; recovery does not fabricate continuity |
| GPS outage | No-position/degraded state is honest |
| Buffered data | Historical/offline fixes remain historical if device supports buffering |
| High cadence | No uncontrolled memory/queue/data-integrity failure |
| Supported commands | Only explicitly supported exchanges are accepted for the certification record |
| 24-hour soak | Required for Certified Compatible |
| 72-hour soak | Required for Production Supported |

## Controlled vehicle drive

Use a known route and compare four layers:

1. reference GPS/location;
2. raw tracker observation;
3. persisted OpsTrax telemetry;
4. visible OpsTrax map/history.

Check coordinate accuracy, hemisphere, timestamps, ordering, freshness, speed/heading where supplied, geofence transitions, power/ignition where supplied, connectivity-gap behavior and recovery.

## Security / tenancy gates

- Unknown/unprovisioned hardware is refused.
- Device identity cannot silently move to another tenant or asset.
- Duplicate device sessions follow the documented policy.
- Exact duplicate/replayed observations do not create duplicate operational events.
- Post-reboot legitimate telemetry is not discarded as false replay.
- Secrets are not committed to source control or exposed in customer-visible logs/evidence.

## Required evidence package

Store evidence under a run-specific directory such as:

`artifacts/device-certification/gt06/<manufacturer>-<model>/<YYYYMMDD-run-id>/`

Include:

- `hardware-identity.md`
- `firmware-and-radio-evidence.md`
- `test-ledger.md`
- `route-comparison.md`
- `soak-summary.md`
- `defect-fix-retest-ledger.md`
- `final-compatibility-record.md`
- representative sanitized protocol/log evidence only where needed to support a result
- visible-Chrome screenshots/recordings for the customer-facing truth states

Never commit live credentials, SIM secrets or unrestricted customer identifiers.

## Product acceptance record

The final supported-device record must state:

- manufacturer/model;
- qualified firmware;
- protocol;
- capabilities actually observed;
- capabilities not supported/not tested;
- certification status;
- certification reference;
- certification date;
- installation requirements;
- known limitations;
- intended markets.

Device Health must never treat `registry present`, `connected`, `commissioned`, and `certified compatible` as equivalent states.

## Gate definitions

### Opstrax Certified Compatible

Requires real end-to-end identity/login/location/heartbeat, claimed event support, reboot/reconnect/recovery, controlled-drive evidence, 24-hour soak, intended-market radio evidence, truthful customer UI, and zero open P0/P1 hardware/protocol defects.

### Production Supported

Requires everything above plus a 72-hour soak, repeatable provisioning/install instructions, approved procurement source, support/replacement procedure and an operational owner.

## Regulatory boundary

GT06 physical compatibility is **not** ELD certification. It is also not proof of universal OBD-II/J1939/CAN capability or compatibility with every product marketed under the generic GT06 name.
