# Device supplier intake and zero-code commissioning

**Purpose:** choose an OEM/GPS unit whose exact production firmware can be connected to OpsTrax without writing backend code after the hardware arrives.

## What “zero-code” means

OpsTrax already has a fail-closed GT06 TCP path, a J1939/CAN acquisition path, tenant-scoped device provisioning, IMEI registration, device credentials, assignment, commissioning, replay protection, store-and-forward recovery, diagnostics, and certification truth controls.

An arriving unit needs no backend change when all of these are true:

1. the supplier confirms the exact manufacturer, model, hardware revision, and firmware version;
2. the exact firmware speaks a protocol already implemented by OpsTrax, currently GT06 direct TCP or the documented J1939/CAN path;
3. the installer can configure the unit's APN and an OpsTrax-controlled server address and port;
4. its identifier and login frame match the documented format, and the supplier provides the protocol manual plus representative raw frames;
5. the public TCP gateway, tenant-scoped gateway credential, device record, IMEI allowlist, assignment, and evidence workspace are ready before delivery.

If a device can report only to the supplier's portal, OpsTrax needs a real vendor account, API documentation, commercial data rights, and a provider adapter. If its binary protocol differs from GT06, the vendor specification or parser is an external dependency. Neither case can honestly be called zero-code.

## Ownership split

| Control | Where it belongs | Purpose |
|---|---|---|
| Exact model/HW/FW compatibility candidate | **Platform Console → Hardware Readiness** | Product-wide engineering declaration bound to an exact release SHA; always starts on External Hold |
| Public device edge and gateway credential | Platform infrastructure | Receives long-lived raw TCP and forwards tenant-scoped authenticated observations |
| IMEI, serial, branch, vehicle assignment, installation, commissioning | **Tenant → Device Health** | Customer-owned inventory and operational lifecycle |
| Provider cloud credentials | Tenant integration configuration, after a real handshake | Customer/provider account connection; never entered in the evaluation catalog |
| Hardware certification evidence and independent acceptance | Frozen certification evidence pack | Promotes an exact tuple only after physical proof; never inferred from registration or simulation |

Platform Hardware Readiness is therefore the reusable product catalog. Tenant Device Health is where a customer's purchased unit is installed and operated.

## Questions the supplier must answer before purchase

Request written answers and files for every exact model under consideration:

- manufacturer, exact model number, hardware revision, firmware version, and whether firmware can change between samples and production orders;
- full protocol name and version, protocol PDF, login/heartbeat/location/alarm/status examples, acknowledgements, checksum, retransmission, offline-buffer, and sequence behavior;
- whether the device can send **directly** to a customer-controlled domain or static IP and TCP port without the supplier cloud;
- exact SMS, USB, Bluetooth, desktop-tool, or OTA commands for APN, server host, server port, reporting interval, timezone, and reset;
- IMEI/login identifier format and whether the IMEI can be read from the case, command response, and protocol frame;
- supported LTE bands, SIM/eSIM type, roaming, carrier certifications, supply voltage, ignition input, backup battery, IP rating, operating temperature, antennas, and harness pinout;
- GPS accuracy specification and supported event/alarm inputs, including SOS, ignition, power loss, towing, vibration, geofence, harsh driving, and any fuel/CAN inputs;
- remote commands supported by the exact firmware and their acknowledgement/result frames;
- CE/FCC/ISED/carrier and environmental reports applicable to the exact production hardware;
- OEM branding, minimum order quantity, sample availability, firmware ownership/change notice, warranty, RMA, replacement lifecycle, and data/commercial rights;
- if their cloud is mandatory: API documentation, sandbox account, rate limits, webhooks, historical backfill, token lifecycle, data retention/export, regional hosting, uptime/support terms, and the right to display and store customer data in OpsTrax.

“Send us the IMEI and we will add it to our account” proves only that the supplier has a portal. It does not answer whether direct device-to-OpsTrax delivery is possible.

Do not send production secrets, gateway keys, customer credentials, or real fleet data through supplier chat. An IMEI is an identifier and not a cryptographic credential, but it should still be shared only for the purchased test unit and recorded in the controlled intake ticket.

## Work completed before the device arrives

1. In Platform Console → Hardware Readiness, register the exact manufacturer/model/HW/FW tuple against the running 40-character release SHA. Record why physical evidence is pending.
2. Declare only an implemented path. For GT06, attach the supplier manual or controlled frame reference and list the intended fields, events, commands, and known limitations. This remains **Engineering-declared / unverified** and **External Hold**.
3. Deploy the public TCP edge on infrastructure that accepts long-lived raw TCP. The normal Render API is HTTPS-only and does not replace this edge. Use [`../../telematics/deploy/README.md`](../../telematics/deploy/README.md).
4. Provision a tenant-scoped gateway credential. Store it only in the edge secret store; do not give it to the supplier.
5. In the customer tenant, create or import the device with its exact identity, IMEI, branch, and intended vehicle. Install it in **Provisioning** state and put its IMEI on the edge allowlist.
6. Run the GT06 simulator against the same gateway topology for login, drive, heartbeat, alarm, checksum corruption, fragmented frames, reconnect, power-cycle sequence reset, duplicate/replay, store-and-forward, and concurrent sessions. Simulation proves the software path and operating procedures; it does not prove the supplier firmware.
7. Prepare the evidence record, timestamps, reviewer assignments, route, failure injections, and 24/72-hour observation windows before the sample arrives.

## First-device procedure

1. Photograph the device labels, packaging, harness, and reported firmware. Reconcile them to the registered exact tuple. A mismatch creates a new candidate.
2. Configure the APN and OpsTrax public edge address/port with the supplier's documented command. Retain the redacted command response as evidence.
3. Confirm the raw login fingerprint and IMEI. If it does not match the implemented protocol, stop and retain the capture for a bounded adapter task; do not guess the parser.
4. Confirm authenticated login, acknowledgement, current location, heartbeat, status/alarm mapping, tenant/vehicle projection, and visible Device Health/GPS results.
5. Run bench, route, power-loss, network-loss, reconnect, duplicate/replay, backlog drain, and recovery checks. Continue through the required 24-hour and 72-hour soak windows.
6. Bind evidence to the same exact hardware tuple and 40-character software SHA. Independent hardware and SDET reviewers record acceptance separately.
7. Promote only through the governed compatibility evaluation. Registration, successful simulation, an online badge, or one good route never certifies hardware.

## Decision rule

- **Proceed to sample:** direct server configuration is confirmed, exact firmware documentation is supplied, and the protocol matches GT06/J1939.
- **Proceed with vendor-cloud evaluation:** direct delivery is unavailable, but a documented API, sandbox, data rights, and commercial terms are available.
- **External Hold:** exact firmware/protocol is unconfirmed, raw frames or documentation are withheld, the unit is portal-locked without API rights, or required radio/electrical/regulatory evidence is missing.
- **Reject:** identity cannot be fixed to an exact production tuple, server destination cannot be controlled, or the supplier will not disclose material firmware/protocol changes.

Passing the real-device gates can make an exact model/HW/FW/SHA combination **OpsTrax Certified Compatible**. It does not by itself create government ELD certification, carrier approval, or regulatory approval for another product claim.
