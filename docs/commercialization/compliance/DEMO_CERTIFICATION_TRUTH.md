# Demo Certification Truth Boundary

**Control:** RULE-DATA / GitHub issue #197  
**Status:** candidate implementation; acceptance requires the merged exact SHA in production and signed-in customer-browser evidence  
**Applies to:** demo tenant fixtures, legacy opt-in demo seeds, scale harness data, HOS presentation, ELD/device presentation, camera metadata and compatibility catalogs

## Governing rule

Synthetic data may demonstrate a workflow, layout, calculation path or failure state. It is never provider, device, physical, media, regulatory or certification evidence. A certification or provider-health claim may be displayed only when the underlying evidence record identifies the real source and satisfies the applicable frozen-candidate gate.

The application identifies an explicitly synthetic tenant as **Demo Data** in the signed-in shell. That tenant label does not by itself make a misleading row safe. Each regulated or externally sourced record must also state its own provenance and verification state.

## Reviewed semantics

| Record or field | Meaning allowed in OpsTrax | Demo rule |
|---|---|---|
| `hos_logs.is_certified` | The authenticated driver attested to the complete daily HOS record | Seeded HOS rows remain false. Customer wording is **Driver attestation**, never ELD product or regulatory certification. |
| `driver_certifications` | Driver qualification or credential records such as a CDL, medical certificate or endorsement | These are driver records only. They do not establish ELD, provider, device or carrier certification. |
| finance, expense, maintenance and telemetry-rule `approval_status` | An internal person or workflow approved the stated business action | This is never regulatory approval. Demo approvals exercise workflow state and remain covered by the tenant-wide **Demo Data** label; telemetry templates remain disabled and `unapproved`. |
| market-pack `log_certification_status` | Driver-log workflow state | The seeded/default state is `uncertified`. It is not an ELD product listing or regulator decision. |
| `eld_devices.provider_sync_status` and sync timestamps | Evidence from an authenticated provider/device path | Synthetic rows use `Unverified`, no provider account, no heartbeat/sync time and no device credentials. |
| `integrations` connection and sync state | Result of a real provider authorization, handshake and sync | Built-in catalog entries start disconnected with no successful sync. A provider name alone is not evidence. |
| camera provider/media state | Authenticated provider intake plus retrievable media and accepted privacy controls | Legacy synthetic camera rows remain unverified or are retired. Camera provider, media, physical-device and privacy acceptance remain `EXTERNAL HOLD`. |
| device compatibility candidate | Engineering design intent for one exact hardware/firmware/software tuple | No device is seeded as compatible or certified. Engineering declarations remain `Unverified` / `ExternalHold` until frozen-candidate evidence is accepted. |

## Implemented controls

1. The canonical demo ELD is named **Synthetic demo ELD**, identifies **Synthetic fixture — no provider account**, has no sync time and reports provider evidence as `Unverified`.
2. Historical time-series enrichment no longer mints Geotab, Samsara or Motive rows, device credentials, recent heartbeat timestamps or `Active` device state. Its future rows are explicit synthetic diagnostic records.
3. Stage 133 scrubs the exact legacy Meridian demo ELD shape and the deterministic enriched-demo gateway shape. Its predicate requires a demo-named tenant and exact legacy identifiers; unrelated customer/provider rows are outside the update.
4. The opt-in legacy Batch 6 HOS fixture no longer pre-attests driver logs and no longer seeds provider-branded active ELDs.
5. The Acme scale harness names its tenant as demo data and creates only synthetic, unverified, unsynced diagnostic ELD workflow records.
6. HOS screens distinguish driver attestation from ELD product certification. The ELD table exposes provider-evidence state, and the compliance summary counts records without calling them registered.
7. The predeployment migration runner fails if recognized demo ELD shapes retain provider branding, a healthy/active implication, credentials, heartbeat data or sync timestamps.

## Certification boundary

Passing source tests, database migrations, CI or a browser workflow proves only the software behavior covered by that evidence. It does not certify a provider, ELD, camera, gateway, J1939 adapter or regulatory implementation. Those claims require a frozen exact SHA and the real provider-account, physical-device, route/recovery/soak, privacy and qualified regulatory evidence specified by the governing commercialization plan.

Issue #197 may close when the change is merged with all required CI green, the exact merge SHA is deployed, and a signed-in customer-browser check confirms the production wording and unverified synthetic ELD state. External certification holds remain open.
