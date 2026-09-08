# G5A DeviceOps 2.0 — Current-Build Execution Baseline

Parent: #143 / #110  
Entry: `main@1f3b5de029b33e9315fb96c80988e610665c41b0`  
State: ACTIVE under `CR-2026-09-03-04` when v2.5 merges.

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

## First implementation slice

Do not create another parallel device table. Reconcile the current `eld_devices` / device-facing API contract and introduce only the normalized lifecycle tables/columns required for support/certification concerns. Production migration enrollment is mandatory; startup schema and production migration must remain equivalent. This lane takes the schema-authority slot only when the integration board grants it.

## Stop conditions

Any design that conflates `connected`, `commissioned`, `certified`, or `production supported`; exposes secrets; creates a generic GT06/J1939/OEM certified row without physical evidence; or bypasses tenant/branch ownership is RED and must not merge.