# OpsTrax Pre-Hardware Software Hardening Gate

**Scope:** Fleet Identity + Telematics software readiness before purchasing GT06/PT40/OEM test hardware.

**Governing closeout:** #108 (Modules 1–2), with GT06 physical compatibility remaining separately governed by #109 and PT40/OEM by #164/#144.

## Procurement hold

Physical GT06/OEM procurement is intentionally deferred until all software-controllable P0/P1 defects in this gate are closed. Passing this gate means **software ready for physical confirmation**; it does not certify any physical device, firmware, ELD boundary or provider.

## Required software evidence

1. Current exact-SHA build and complete CI green.
2. Fleet Identity / Asset Master role, tenant and branch isolation with no cross-boundary leakage.
3. Device registry and assignment truth: registered, connected, commissioned, assigned and certified are distinct states.
4. Native telemetry path: TCP -> protocol identification -> session identity -> CRC/replay -> normalization -> forwarding/outbox -> OpsTrax persistence -> browser truth.
5. GT06 independent protocol fixtures and virtual trackers; test generators must not derive expected values from the production decoder.
6. Real-socket coordinate truth across North/East, North/West, South/East and South/West.
7. Concurrent virtual-device isolation and deterministic duplicate-device/session handling.
8. Failure/recovery: malformed traffic, bad CRC, API outage, durable outbox, reconnect, reboot/serial reset and replay suppression.
9. No fabricated zero/healthy values for unavailable measurements; stale/offline/no-data states remain explicit.
10. Representative large-fleet performance, reconnect/burst behavior and bounded resource use under G6A thresholds.
11. Visible-Chrome customer journeys against persisted staging data for Device Health, GPS/Live Map, geofence, diagnostics and authorized roles.
12. Zero open P0/P1 software defects for the frozen candidate and evidence-backed independent acceptance required by the governing gate.

## GT06 sandbox tiers

- **Tier A — deterministic protocol fixtures:** vendor/reference vectors and independent CRC/bit semantics.
- **Tier B — in-repo virtual trackers:** real TCP sockets with login, location, heartbeat, alarm, fragmentation, bad CRC, power-cycle, duplicate IMEI and reconnect scenarios.
- **Tier C — virtual fleet:** multiple concurrent independent IMEIs through the real gateway; coordinate and ownership isolation reconciled from forwarded observations.
- **Tier D — independent external oracle/emulator:** Traccar/FakeGPS/OpenVTS or equivalent supporting evidence where practical. External simulators do not replace physical-device certification.
- **Tier E — physical confirmation:** only after this software gate passes; exact manufacturer/model/HW revision/FW tuple then follows its own certification plan.

## Disposition vocabulary

- `SOFTWARE_HARDENING_IN_PROGRESS`
- `SOFTWARE_READY_FOR_PHYSICAL_CONFIRMATION`
- `SOFTWARE_NO_GO`

Never use `CERTIFIED HARDWARE`, `ELD CERTIFIED`, or equivalent from software/sandbox evidence alone.
