# Opstrax ELD 1.0 — Certification Candidate

**Applicant / provider:** Kode Kinetics LLC  
**Product:** Opstrax ELD  
**Certification candidate:** Opstrax ELD 1.0  
**Internal release designation:** `1.0.0-CERT`  
**Controlled branch:** `cert/opstrax-eld-1.0`  
**Current status:** CERTIFICATION CANDIDATE — NOT CERTIFIED / NOT REGISTERED  
**Program start:** 2026-09-17

## Purpose

This document establishes the controlled product boundary that will be taken through U.S. FMCSA provider testing/self-certification and then Canadian accredited third-party ELD certification. It does **not** make any certification claim.

## Certification boundary

The controlled ELD subsystem includes:

1. Vehicle/ECM acquisition and engine synchronization.
2. Driver identity and authentication used by the ELD.
3. Automatic driving-state detection and duty-status event generation.
4. Records of Duty Status (RODS) / Hours-of-Service event persistence.
5. Unidentified-driver handling and reassignment controls.
6. Driver edits, annotations and audit history.
7. Special driving categories including personal conveyance and yard moves where applicable.
8. Malfunction and data-diagnostic detection, indication and persistence.
9. ELD event sequence/integrity controls.
10. FMCSA-compatible ELD output-file generation.
11. Enforcement data-transfer subsystem.
12. Certified hardware/ECM interface configuration.
13. Certification-relevant mobile application behavior.
14. Certification-relevant backend services and schemas.

The following remain outside the certification boundary unless a regulator/certification body determines otherwise: dispatch, routing, invoicing, maintenance, CRM, customer portal, last-mile workflows, generic telematics analytics, cameras and unrelated AI features.

## Configuration freeze fields

No regulatory submission may occur until every field below has a recorded value and evidence:

| Item | Candidate value | State |
|---|---|---|
| Backend commit SHA | TBD | OPEN |
| Driver app build/version | TBD | OPEN |
| Driver app OS bundle | Android first; exact supported versions TBD | OPEN |
| iOS bundle | Separate registration/certification decision required | OPEN |
| ECM interface hardware vendor/model | TBD | OPEN |
| Hardware revision | TBD | OPEN |
| Firmware version | TBD | OPEN |
| J1939 250 kbps | Required candidate capability | OPEN |
| J1939 500 kbps | Required candidate capability | OPEN |
| J1708/J1587 | Required candidate capability for broad legacy coverage | OPEN |
| J1979/OBD-II | Required candidate capability for supported vehicle classes | OPEN |
| GNSS source | TBD | OPEN |
| Network/data-transfer path | Telematics target: Web Services + Email | OPEN |
| Cryptographic provider key/certificate | Requires FMCSA provider portal | BLOCKED-PROVIDER-ACCOUNT |
| Canada technical-standard baseline | Engineer to v1.3 delta; formal test baseline to be confirmed by CB | OPEN |

## Release-control rule

This branch is a controlled certification branch. Changes must follow:

`main -> certification impact assessment -> cert/opstrax-eld-1.0 -> certification regression -> evidence update -> approval`

After registration/certification, no certification-relevant behavior may be changed without documenting regulatory impact and determining whether re-registration/re-certification or certification-body review is required.

## First regulatory targets

### United States — FMCSA

1. Create Kode Kinetics LLC ELD Provider Account.
2. Obtain provider-portal technical resources and provider public/private key material.
3. Generate a minimal compliant ELD output file with 2–3 events.
4. Pass FMCSA File Validator structural checks.
5. Validate presentation in Web eRODS.
6. Complete requirements/test evidence matrix.
7. Complete physical ECM/vehicle test campaign.
8. Self-certify only after evidence demonstrates compliance.
9. Register exact Opstrax ELD bundle(s) with FMCSA.

### Canada — Transport Canada

1. Engage an accredited certification body (FPInnovations and/or COMDriver Tech).
2. Confirm formal Technical Standard baseline during the v1.2 -> v1.3 transition.
3. Submit exact hardware/software configuration.
4. Complete simulation, bench and in-vehicle test campaign.
5. Resolve certification-body findings.
6. Obtain certification number and active Transport Canada listing.

## Hard gates

- No marketing claim may state that Opstrax is FMCSA-certified, FMCSA-approved, Transport Canada certified, or otherwise regulator-approved before the official listing/certificate exists.
- Partner/device status does not transfer certification to Opstrax.
- Hardware capability is not ELD certification.
- A green status requires evidence, not implementation assertions.
- Different OS/device bundles must be evaluated as separate regulatory configurations where required.
