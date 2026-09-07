# G5A DeviceOps 2.0 — Current-Build Execution Baseline

Parent: #143 / #110  
Original entry: `main@1f3b5de029b33e9315fb96c80988e610665c41b0`
Current software lane: `hardening/deviceops-software-followup-20260907`
State: BUILD / INTEGRATE. Hardware certification remains EXTERNAL HOLD.

## Existing product foundation to preserve

This lane extends existing OpsTrax capabilities rather than replacing them:

- `frontend/src/pages/IotDevicesPage.tsx` already supports real device onboarding/assignment and explicitly notes that SIM/firmware/power/compliance are outside the current connection handshake.
- `backend-dotnet/Services/TelemetrySchemaService.cs` already carries `eld_devices` company/IMEI/credential/last-seen/revocation lifecycle fields and trusted-gateway telemetry security.
- Existing permissions include device view/create/update/delete/assign/diagnostics/firmware/export and telemetry device read/manage boundaries.
- Existing Device Health / Control Tower / GPS / diagnostics surfaces and persisted assignment/install history remain the operating UI foundation.

## Gap statement

Current capability is device registry + connection/telemetry operations. It is not yet a complete support lifecycle for SIM/eSIM/carrier, installer evidence, firmware campaigns, RMA/warranty/replacement, governed command catalog, certification/compatibility status and support tier.

## Atomic build order

1. **Canonical lifecycle model** — introduce separate support/certification records without overloading connection status. Define Device Registry, Installation, Commissioning, Connectivity, Certification and Support Tier as independent states.
2. **SIM/carrier lifecycle** — protected ICCID/MSISDN/carrier/APN metadata, assignment/change history, no credential leakage.
3. **Install/commissioning evidence** — installer/work order, timestamp/location, install checklist/document references, vehicle binding, commissioning result.
4. **Compatibility catalog** — exact manufacturer/model/HW rev/FW/protocol/certification reference/capabilities/limitations; initial rows remain Candidate/Pilot unless external certification exists.
5. **Firmware campaigns** — desired/current firmware, campaign batches, eligibility, staged rollout, failure/hold/rollback state; no remote-upgrade claim until a device/provider actually supports it.
6. **RMA/replacement** — failure reason, warranty posture, replacement device, custody/swap history and support SLA.
7. **Remote-command governance** — capability-negotiated allowlist, explicit RBAC, idempotency/audit and safety confirmation.
8. **Operator UX** — exception-first health queues, bulk workflows, compact enterprise density, truthful unavailable states.
9. **Scale/acceptance** — 1K+ inventory, export integrity, tenant/branch adversarial tests and exact-SHA Chrome journeys.

## Completed software slices

1. The existing `eld_devices` inventory and effective-dated `device_installations` history remain the canonical device and installation records. No parallel device master was created.
2. Compatibility tier evaluation is now bound to one exact software candidate SHA, one exact manufacturer/model/hardware revision/firmware tuple, required evidence references and independent acceptance. A reviewer cannot self-certify both mandatory perspectives.
3. Stage 115 adds an exact-tuple engineering candidate registry. Database constraints and an immutable-identity trigger lock every row to `ExternalHold`; the application role can only read it. No GT06, PT40 or OEM candidate rows are seeded.
4. Single-device and bulk onboarding preserve manufacturer, model, hardware revision and reported firmware as operator-recorded inventory. Missing fields fail closed in the customer surface. Registration never becomes compatibility evidence.
5. Device details now expose compatibility truth: exact tuple completeness, frozen candidate SHA when one exists, maximum tier `Unverified`, the external-hold reason and required physical evidence.
6. Legacy empty-database startup no longer writes invented `last_seen_at` values. A device becomes online only through an authenticated telemetry observation.
7. A dedicated isolated PostgreSQL and frontend contract workflow verifies migration repeat safety, database refusal of certification promotion or tuple drift, fail-closed projection and the production frontend build.
8. Stage 116 adds encrypted SIM/eSIM assignment history with exact effective time, source reference, change reason and idempotency. ICCID, MSISDN and APN payloads are encrypted; the tenant application role can read only masked last-four/configured projections. One current profile is allowed per device and per ICCID, and ended history is immutable.

## Current truth disposition

| Concern | Software status | Certification status |
| --- | --- | --- |
| Inventory and exact hardware tuple | Implemented for single and bulk onboarding | Operator-recorded; not identity proof |
| Effective installation history | Existing foundation retained | Physical commissioning evidence pending |
| Compatibility candidate registry | Implemented, immutable tuple/SHA | EXTERNAL HOLD / Unverified |
| Device online state | Authenticated telemetry only | Provider/device evidence pending |
| SIM/eSIM and carrier lifecycle | First assignment/change-history slice implemented | Inventory only; network attachment remains unverified |
| Firmware campaigns | Read-only reported version only | No remote-upgrade claim |
| RMA/warranty/replacement | Not implemented | Not applicable |
| Remote command governance | Not complete | No general command-support claim |

The next safe software sequence is firmware campaign control, RMA/replacement and capability-negotiated command governance. Carrier API reconciliation, activation/suspension status and usage telemetry remain later SIM lifecycle work. Physical bench, route, recovery, 24/72-hour soak, installation repeatability, procurement, warranty and independent acceptance remain EXTERNAL HOLD and must not block those engineering slices.

## Stop conditions

Any design that conflates `connected`, `commissioned`, `certified`, or `production supported`; exposes secrets; creates a generic GT06/J1939/OEM certified row without physical evidence; seeds an unobserved hardware identity; converts missing telemetry into a check-in; or bypasses tenant/branch ownership is RED and must not merge.
