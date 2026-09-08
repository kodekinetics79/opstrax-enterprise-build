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
9. Stage 117 adds immutable firmware campaign planning with explicit target/rollback versions, rollout strategy, batches, maintenance windows and exact per-device identity snapshots. Every campaign and target is fixed at `ExternalHold`; provider capability is `Unverified`, `remote_upgrade_claim` is false, and the workflow cannot dispatch an OTA command.
10. Stage 118 adds append-only RMA cases, P0/P1/P2/P3 severity, failure/warranty/SLA facts, custody events and one exact registered replacement-device plan. Warranty and custody evidence stay `Unverified`; replacement stays `Planned / ExternalHold`; every response and customer screen denies a physical swap or readiness claim.
11. Stage 119 adds a capability-governed remote-command admission ledger for position requests, diagnostic requests and controlled device restart. Admission requires a current `Verified` capability record for the exact serial/manufacturer/model/hardware/firmware/provider tuple, a bounded payload, dedicated RBAC, an idempotency key, purpose/source evidence and an exact typed confirmation whose hash alone is retained. The tenant application role is read-only on capability and command records. No capability rows are seeded, no provider delivery adapter is claimed, and recorded requests explicitly remain undispatched, unacknowledged, unapplied and without physical outcome.
12. The DeviceOps inventory now includes a bounded, server-paged software exception queue. It derives only from persisted exact identity, current installation, assigned SIM/eSIM profile, current telemetry, lifecycle hold and open RMA facts. Missing or internally inconsistent API assessment fields render as unavailable instead of healthy. The customer surface states that this queue is operational triage and never certification evidence; hardware, provider and certification holds remain separate.
13. A PostgreSQL scale oracle inserts 1,000 tenant-scoped devices and verifies the tenth 100-row exception page, exact serial ordering, the full filtered total and the full-fleet gap summary. This is database-backed software paging evidence; it does not replace frozen-candidate browser, provider or physical acceptance.
14. Stage 120 adds an append-only provider-observation ledger for carrier and connectivity facts. Intake accepts only an authenticated adapter payload, protects case-sensitive provider account and observation identities with keyed HMAC blind indexes, stores a payload hash rather than the raw response, and reconciles the reported ICCID to one exact current encrypted SIM/eSIM profile. The customer surface labels subscription, network registration, data session, roaming and usage as provider-reported software status. Provider verification, RF attachment, telemetry delivery, physical operation and certification remain false. No carrier account, provider adapter or device was exercised in this software slice.
15. Stage 121 adds the missing installer appointment and evidence-recording workflow. A tenant/branch-scoped operator can schedule one exact device/vehicle work package, with the signed-in operator preserved as the assigned installer, then append explicit checklist observations and hash-addressed governed-storage artifact references. Work packages, observations and references are append-only. The customer surface derives readiness from the latest recorded item but labels appointment attendance, checklist assurance, artifact content, physical work and certification as unverified. The workflow neither uploads evidence nor marks commissioning complete; authenticated telemetry remains mandatory for the existing commissioning transition.
16. Stage 122 adds an explicit, append-only link from a completed installer work package to one persisted installation for the same tenant, branch, device and vehicle. The database admits the link only for the active assigned installer after every category-required latest checklist result is `Pass` or `NotApplicable` and at least one artifact reference exists. The customer surface shows the installation identity and lifecycle state but keeps the link `RecordedUnverified`; it does not prove physical work, validate artifact content, or grant device or installation certification.
17. Stage 123 replaces the customer-facing emergency archive shortcut with governed device retirement. Retirement requires the exact current device revision and typed serial confirmation, refuses to infer that an active installation was physically removed, atomically ends any current SIM/eSIM profile, invalidates current and grace-period credentials, moves the device to `Retired`, appends lifecycle history and creates one immutable receipt. The selected return/recycle/storage disposition is an operator plan with `Unverified` physical status; no disposal or certification claim is created.
18. Stage 124 adds accountable RMA support ownership and escalation. The first active operator can claim a tenant/branch-scoped open case; another active in-scope operator can explicitly take ownership; and the current case can be escalated at P0/P1/P2/P3 while preserving its owner. Every action is append-only, serialized per case, idempotent and visible in the customer device record. It remains an `OperatorRecorded` routing fact and cannot claim that support responded, a vendor accepted warranty, or any physical outcome occurred.

## Current truth disposition

| Concern | Software status | Certification status |
| --- | --- | --- |
| Inventory and exact hardware tuple | Implemented for single and bulk onboarding | Operator-recorded; not identity proof |
| Effective installation history | Existing foundation retained | Physical commissioning evidence pending |
| Installer work packages | Appointment plan, exact device/vehicle/installer identity, checklist observations and SHA-256 artifact references implemented and visible | Operator-recorded / Unverified; attendance, artifact content and physical work still require independent review |
| Work-package installation traceability | Exact completed work package can be linked once to its matching persisted installation; append-only database and customer projection gates are implemented | Recorded / Unverified; physical execution, artifact authenticity and certification still require independent evidence |
| Device retirement and lifecycle history | Exact-revision retirement, credential shutdown, connectivity-profile closure, immutable receipt and correctly labelled lifecycle history are implemented | Software retirement is recorded; physical removal/disposition and certification remain Unverified |
| Compatibility candidate registry | Implemented, immutable tuple/SHA | EXTERNAL HOLD / Unverified |
| Device online state | Authenticated telemetry only | Provider/device evidence pending |
| SIM/eSIM and carrier lifecycle | Protected assignment/change history plus an exact-profile, append-only boundary for authenticated provider-reported subscription/network/session/usage observations | No real carrier account or adapter evidence yet; RF attachment, telemetry delivery and physical operation remain unverified |
| Firmware campaigns | Planning, batching, eligibility and rollback intent implemented | ExternalHold; no command dispatch or remote-upgrade claim |
| RMA/warranty/replacement | Support cases, accountable ownership, P0/P1/P2/P3 escalation history, custody references, SLA due dates and exact replacement planning implemented | Routing is OperatorRecorded; support response, warranty and physical evidence remain Unverified; swap ExternalHold |
| Remote command governance | Capability-gated admission, bounded catalog, immutable identity, audit and customer history implemented; provider delivery adapters are not implemented | ExternalHold until an exact provider/device capability is observed; no dispatch, acknowledgement, application or physical-outcome claim |
| Operator software exception queue | Server-paged readiness filter, fleet count and per-device reasons implemented from persisted facts; rolling API mismatch fails closed | Operational triage only; no device, provider or certification evidence |

The exact-profile carrier/provider reconciliation boundary is ready for a real adapter, but adapter execution remains EXTERNAL HOLD until a provider account is available. Carrier activation or suspension commands are not implemented. Firmware and remote-command delivery adapters and result reconciliation remain blocked until exact provider/device capability is observed. RMA custody references and replacement plans remain operator-recorded facts until vendor and physical evidence is independently checked. Physical bench, route, recovery, 24/72-hour soak, installation repeatability, procurement, warranty and independent acceptance remain EXTERNAL HOLD and must not block those engineering slices.

## Stop conditions

Any design that conflates `connected`, `commissioned`, `certified`, or `production supported`; exposes secrets; creates a generic GT06/J1939/OEM certified row without physical evidence; seeds an unobserved hardware identity; converts missing telemetry into a check-in; or bypasses tenant/branch ownership is RED and must not merge.
